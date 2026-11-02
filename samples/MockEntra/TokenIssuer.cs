using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MockEntra;

/// <summary>Holds the signing key and mints Entra-shaped v2.0 access tokens.</summary>
public sealed class TokenIssuer
{
    private readonly MockEntraOptions _options;
    private readonly RsaSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenIssuer(MockEntraOptions options)
    {
        _options = options;
        var rsa = RSA.Create(2048);
        var kid = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];
        _key = new RsaSecurityKey(rsa) { KeyId = kid };
    }

    public string Issuer => $"{_options.IssuerBaseUrl.TrimEnd('/')}/{_options.TenantId}/v2.0";

    public object Jwks()
    {
        var p = _key.Rsa.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA", use = "sig", alg = "RS256", kid = _key.KeyId,
                    n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent),
                },
            },
        };
    }

    public MockClient? Authenticate(string? clientId, string? secret) =>
        _options.Clients.FirstOrDefault(c =>
            c.ClientId == clientId && secret is not null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(c.Secret), Encoding.UTF8.GetBytes(secret)));

    public bool IsKnownClient(string? clientId) => _options.Clients.Any(c => c.ClientId == clientId);

    /// <summary>Audience for a scope string, or null when any scope is not "{audience}/.default".</summary>
    public string? AudienceForScope(string? scope)
    {
        var parts = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var expected = $"{_options.Audience}/.default";
        return parts.All(s => s == expected) ? _options.Audience : null;
    }

    public (string Token, int ExpiresIn) Issue(MockClient client, string audience)
    {
        var now = DateTime.UtcNow;
        var oid = StableGuid($"oid:{_options.TenantId}:{client.ClientId}");
        var claims = new Dictionary<string, object>
        {
            ["tid"] = _options.TenantId,
            ["azp"] = client.ClientId,
            ["appid"] = client.ClientId,
            ["oid"] = oid,
            ["sub"] = oid,
            ["ver"] = "2.0",
        };
        if (client.Roles.Count > 0) claims["roles"] = client.Roles.ToArray();
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(_options.TokenLifetimeSeconds),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
        return (token, _options.TokenLifetimeSeconds);
    }

    private static string StableGuid(string seed) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16]).ToString();
}
