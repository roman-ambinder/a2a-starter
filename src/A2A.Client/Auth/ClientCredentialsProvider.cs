using System.Text.Json;
using A2A.Protocol;

namespace A2A.Client.Auth;

/// <summary>
/// OAuth 2.0 client-credentials flow. The token URL comes from the card's first
/// <c>oauth2SecurityScheme.flows.clientCredentials.tokenUrl</c> unless an override is given.
/// The token is cached until 60 seconds before it expires. Thread-safe.
/// </summary>
public sealed class ClientCredentialsProvider : IA2ACredentialProvider, IDisposable
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

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
    private DateTimeOffset _validUntil;

    public ClientCredentialsProvider(string clientId, string clientSecret, string scope, Uri? tokenUrlOverride = null,
        HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _tokenUrlOverride = tokenUrlOverride;
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
    {
        var tokenUrl = _tokenUrlOverride ?? FindTokenUrl(card);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is null || _cachedUrl != tokenUrl || _time.GetUtcNow() >= _validUntil - Skew)
            {
                var (token, lifetime) = await RequestTokenAsync(tokenUrl, ct).ConfigureAwait(false);
                _token = token;
                _cachedUrl = tokenUrl;
                _validUntil = _time.GetUtcNow() + lifetime;
            }
            return new Dictionary<string, string> { ["Authorization"] = "Bearer " + _token };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Uri FindTokenUrl(AgentCard card)
    {
        var url = card.SecuritySchemes?.Values
            .Select(s => s.Oauth2SecurityScheme?.Flows.ClientCredentials?.TokenUrl)
            .FirstOrDefault(u => !string.IsNullOrEmpty(u))
            ?? throw new InvalidOperationException(
                $"Agent card '{card.Name}' has no oauth2 clientCredentials flow, and no token URL override was given.");
        return new Uri(url, UriKind.Absolute);
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

        var seconds = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) ? d : 3600;
        return (token, TimeSpan.FromSeconds(seconds));
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
