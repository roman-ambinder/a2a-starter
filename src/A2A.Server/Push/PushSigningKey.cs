using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace A2A.Server.Push;

/// <summary>
/// RSA 2048 key generated at startup. Signs the <c>X-A2A-Push-Signature</c> JWT (RS256) and publishes its public half as a JWKS.
/// A new key per process: receivers must fetch the JWKS (by <c>kid</c>) rather than cache keys for good.
/// </summary>
public sealed class PushSigningKey : IDisposable
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public PushSigningKey()
    {
        Kid = Guid.NewGuid().ToString("N");
        _key = new RsaSecurityKey(_rsa) { KeyId = Kid };
        _credentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
    }

    public string Kid { get; }

    /// <summary>The public key as a JSON Web Key Set: <c>{"keys":[{kty, use, alg, kid, n, e}]}</c>.</summary>
    public JsonObject GetJwks()
    {
        var p = _rsa.ExportParameters(false);
        return new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = SecurityAlgorithms.RsaSha256,
                ["kid"] = Kid,
                ["n"] = Base64UrlEncoder.Encode(p.Modulus),
                ["e"] = Base64UrlEncoder.Encode(p.Exponent),
            }),
        };
    }

    /// <summary>Creates the JWT: iss, aud (webhook URL), task_id, body_sha256, iat, exp = iat + 5 minutes.</summary>
    public string CreateToken(string issuer, string audience, string taskId, byte[] body, DateTimeOffset now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.UtcDateTime,
            Expires = now.UtcDateTime + TokenLifetime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                ["task_id"] = taskId,
                ["body_sha256"] = Base64UrlEncoder.Encode(SHA256.HashData(body)),
            },
        };
        return _handler.CreateToken(descriptor);
    }

    public void Dispose() => _rsa.Dispose();
}
