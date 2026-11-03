using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WebhookReceiver;

/// <summary>The outcome of checking an <c>X-A2A-Push-Signature</c> token. <see cref="TaskId"/> is the signed <c>task_id</c> claim.</summary>
public sealed record SignatureResult(bool Valid, string? TaskId, string? Failure)
{
    public static SignatureResult Fail(string reason) => new(false, null, reason);
}

/// <summary>
/// Verifies the push signature JWT: the issuer must be a trusted A2A server (checked before anything is fetched), the key comes from
/// that server's JWKS (cached; refreshed on an unknown <c>kid</c> at most once per <see cref="MinRefreshInterval"/>), then
/// signature (RS256 only), audience, lifetime (with <see cref="ClockSkew"/>) and <c>body_sha256</c> against the exact raw body.
/// </summary>
public sealed class PushSignatureValidator(IHttpClientFactory httpClients, ReceiverOptions options, ILogger<PushSignatureValidator> logger,
    TimeProvider? time = null)
{
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(10);
    public const string JwksPath = "/.well-known/a2a-push-jwks.json";

    private sealed class ServerKeys
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Dictionary<string, SecurityKey> Keys = new(StringComparer.Ordinal);
        public DateTimeOffset? LastFetch;
    }

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ServerKeys> _servers = new(StringComparer.Ordinal);
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<SignatureResult> ValidateAsync(string? token, byte[] body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return SignatureResult.Fail("missing signature");
        JsonWebToken jwt;
        try { jwt = new JsonWebToken(token); }
        catch (Exception) { return SignatureResult.Fail("malformed signature token"); }

        var issuer = Normalize(jwt.Issuer);
        if (!options.TrustedServers.Contains(issuer, StringComparer.Ordinal)) return SignatureResult.Fail("untrusted issuer");
        if (string.IsNullOrEmpty(jwt.Kid)) return SignatureResult.Fail("missing kid");

        var key = await GetKeyAsync(issuer, jwt.Kid, ct).ConfigureAwait(false);
        if (key is null) return SignatureResult.Fail("unknown signing key");

        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [issuer, issuer + "/"],
            ValidateAudience = true,
            ValidAudience = options.WebhookUrl,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = ClockSkew,
            LifetimeValidator = (nbf, exp, _, _) => exp is not null && exp.Value + ClockSkew >= _time.GetUtcNow().UtcDateTime
                                                    && (nbf is null || nbf.Value - ClockSkew <= _time.GetUtcNow().UtcDateTime),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
        }).ConfigureAwait(false);
        if (!result.IsValid) return SignatureResult.Fail("invalid signature token");

        var now = _time.GetUtcNow().UtcDateTime;
        if (!jwt.TryGetPayloadValue<long>("iat", out var iatSeconds)) return SignatureResult.Fail("missing iat claim");
        var iat = DateTimeOffset.FromUnixTimeSeconds(iatSeconds).UtcDateTime;
        if (iat > now + ClockSkew) return SignatureResult.Fail("iat is in the future");
        if (jwt.ValidTo - iat > MaxLifetime) return SignatureResult.Fail("token lifetime too long");

        if (!jwt.TryGetPayloadValue<string>("task_id", out var taskId) || string.IsNullOrEmpty(taskId))
            return SignatureResult.Fail("missing task_id claim");
        if (!jwt.TryGetPayloadValue<string>("body_sha256", out var claimed) || string.IsNullOrEmpty(claimed))
            return SignatureResult.Fail("missing body_sha256 claim");
        var actual = Base64UrlEncoder.Encode(SHA256.HashData(body));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(claimed), Encoding.ASCII.GetBytes(actual)))
            return SignatureResult.Fail("body does not match body_sha256");
        return new SignatureResult(true, taskId, null);
    }

    private async Task<SecurityKey?> GetKeyAsync(string server, string kid, CancellationToken ct)
    {
        var entry = _servers.GetOrAdd(server, static _ => new ServerKeys());
        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (entry.Keys.TryGetValue(kid, out var cached)) return cached;
            var now = _time.GetUtcNow();
            if (entry.LastFetch is { } last && now - last < MinRefreshInterval) return null;
            entry.LastFetch = now;
            entry.Keys = await FetchAsync(server, ct).ConfigureAwait(false) ?? entry.Keys;
            return entry.Keys.GetValueOrDefault(kid);
        }
        finally { entry.Gate.Release(); }
    }

    private async Task<Dictionary<string, SecurityKey>?> FetchAsync(string server, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var json = await httpClients.CreateClient(nameof(PushSignatureValidator)).GetStringAsync(server + JwksPath, timeout.Token).ConfigureAwait(false);
            var set = new JsonWebKeySet(json);
            return set.GetSigningKeys().Where(k => !string.IsNullOrEmpty(k.KeyId)).GroupBy(k => k.KeyId!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not fetch the JWKS of {Server}: {Error}", server, ex.GetType().Name);
            return null;
        }
    }

    internal static string Normalize(string? url) => (url ?? "").TrimEnd('/');
}
