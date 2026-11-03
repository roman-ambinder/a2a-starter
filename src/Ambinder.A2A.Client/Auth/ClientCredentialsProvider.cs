using System.Globalization;
using System.Text.Json;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Auth;

/// <summary>
/// OAuth 2.0 client-credentials flow. Thread-safe.
/// <para>
/// Token URL: the explicit <c>tokenUrlOverride</c> when given. Otherwise the card's first
/// <c>oauth2SecurityScheme.flows.clientCredentials.tokenUrl</c>, but only when its host is in <see cref="AllowedTokenHosts"/>
/// and it is <c>https</c> (plain <c>http</c> only for a loopback host: localhost, 127.0.0.1, ::1). The client secret is sent to
/// that URL, so a card must never be able to choose where it goes. A refused URL throws <see cref="A2AAuthException"/> (401)
/// naming the host, and no request is made.
/// </para>
/// <para>
/// The token is cached until 60 seconds before it expires. <c>expires_in</c> (number or numeric string) is capped at 24 hours;
/// a value of 60 seconds or less (or one that cannot be read as positive) still caches the token for 30 seconds.
/// </para>
/// </summary>
public sealed class ClientCredentialsProvider : IA2ACredentialProvider, IDisposable
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinCache = TimeSpan.FromSeconds(30);
    private const double MaxLifetimeSeconds = 24 * 60 * 60;
    private const double DefaultLifetimeSeconds = 3600;

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scope;
    private readonly Uri? _tokenUrlOverride;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Uri? _cachedUrl;
    private string? _token;
    private DateTimeOffset _refreshAt;

    /// <param name="tokenUrlOverride">The token URL to use. When set, the card's token URL is ignored.</param>
    /// <param name="allowedTokenHosts">
    /// Hosts whose card-advertised token URL may be used (case-insensitive, for example <c>login.microsoftonline.com</c>).
    /// Empty or null: the card's token URL is never used and <paramref name="tokenUrlOverride"/> is required.
    /// </param>
    public ClientCredentialsProvider(string clientId, string clientSecret, string scope, Uri? tokenUrlOverride = null,
        HttpClient? httpClient = null, TimeProvider? timeProvider = null, IEnumerable<string>? allowedTokenHosts = null)
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _tokenUrlOverride = tokenUrlOverride;
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _time = timeProvider ?? TimeProvider.System;
        AllowedTokenHosts = new HashSet<string>(
            (allowedTokenHosts ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(NormalizeHost),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Hosts whose card-advertised token URL may receive the client secret. Empty: only the explicit override is used.</summary>
    public IReadOnlySet<string> AllowedTokenHosts { get; }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
    {
        var tokenUrl = _tokenUrlOverride ?? TrustedCardTokenUrl(card);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is null || _cachedUrl != tokenUrl || _time.GetUtcNow() >= _refreshAt)
            {
                var (token, lifetime) = await RequestTokenAsync(tokenUrl, ct).ConfigureAwait(false);
                _token = token;
                _cachedUrl = tokenUrl;
                _refreshAt = _time.GetUtcNow() + CacheFor(lifetime);
            }
            return new Dictionary<string, string> { ["Authorization"] = "Bearer " + _token };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The card's token URL when it passes the host and scheme rules; else <see cref="A2AAuthException"/> (401).</summary>
    private Uri TrustedCardTokenUrl(AgentCard card)
    {
        var url = card.SecuritySchemes?.Values
            .Select(s => s.Oauth2SecurityScheme?.Flows.ClientCredentials?.TokenUrl)
            .FirstOrDefault(u => !string.IsNullOrEmpty(u))
            ?? throw new InvalidOperationException(
                $"Agent card '{card.Name}' has no oauth2 clientCredentials flow, and no token URL override was given.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new A2AAuthException(401, $"Agent card '{card.Name}' has an invalid token URL; pass the token URL explicitly.");

        var host = NormalizeHost(uri.IdnHost);
        if (!AllowedTokenHosts.Contains(host))
            throw new A2AAuthException(401,
                $"Refused the token URL host '{host}' from agent card '{card.Name}': it is not in AllowedTokenHosts. " +
                "Pass the token URL explicitly or allow the host.");
        var secure = uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(host));
        if (!secure)
            throw new A2AAuthException(401,
                $"Refused the token URL host '{host}' from agent card '{card.Name}': the token URL must be https (http only for loopback).");
        return uri;
    }

    private static string NormalizeHost(string host) => host.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static bool IsLoopback(string host) => host is "localhost" or "127.0.0.1" or "::1";

    /// <summary>How long a token with <paramref name="lifetime"/> is reused: lifetime minus 60 s, at least 30 s.</summary>
    private static TimeSpan CacheFor(TimeSpan lifetime)
    {
        var cache = lifetime - Skew;
        return cache < MinCache ? MinCache : cache;
    }

    /// <summary><c>expires_in</c> as seconds (number or numeric string), capped at 24 h; missing or unreadable → 1 h.</summary>
    private static TimeSpan ReadLifetime(JsonElement root)
    {
        double seconds = DefaultLifetimeSeconds;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("expires_in", out var e))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d)) seconds = d;
            else if (e.ValueKind == JsonValueKind.String
                     && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) seconds = parsed;
        }
        if (double.IsNaN(seconds)) seconds = 0;
        seconds = Math.Clamp(seconds, 0, MaxLifetimeSeconds); // never overflows TimeSpan
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<(string Token, TimeSpan Lifetime)> RequestTokenAsync(Uri tokenUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["scope"] = _scope,
            }),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        JsonElement root = default;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // handled below
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = Str(root, "error") ?? $"HTTP {(int)response.StatusCode}";
            var description = Str(root, "error_description");
            throw new A2AAuthException(401, $"Token request failed: {error}" + (description is null ? "" : $" - {description}"));
        }

        if (Str(root, "access_token") is not { Length: > 0 } token)
            throw new A2AAuthException(401, "Token request failed: the token endpoint returned no access_token.");

        return (token, ReadLifetime(root));
    }

    private static string? Str(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
