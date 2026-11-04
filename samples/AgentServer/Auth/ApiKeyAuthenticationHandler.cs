using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgentServer.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = ApiKeyAuthenticationHandler.HeaderName;

    /// <summary>Key name → key value.</summary>
    public IDictionary<string, string> Keys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// <c>X-API-Key: {key}</c> → principal with identity type <c>ApiKey</c>, claim <c>a2a:apikey</c> = key name, name = key name and
/// role <c>A2A.Invoke</c>. No header → no result. Unknown key or several header values → failure (401).
/// Keys are compared as SHA-256 hashes with <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    /// <summary>Claim that carries the key name.</summary>
    public const string ApiKeyClaim = "a2a:apikey";
    public const string HeaderName = "X-API-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var values) || values.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
            return Task.FromResult(AuthenticateResult.Fail($"Exactly one non-empty {Options.HeaderName} header is required."));

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        string? match = null;
        foreach (var (name, key) in Options.Keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            // Check every key so the time taken does not depend on which one matched.
            if (CryptographicOperations.FixedTimeEquals(presented, SHA256.HashData(Encoding.UTF8.GetBytes(key))))
                match ??= name;
        }
        if (match is null)
        {
            Logger.LogInformation("Rejected an unknown API key.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ApiKeyClaim, match),
                new Claim(ClaimTypes.Name, match),
                new Claim(ClaimTypes.Role, AuthSetup.InvokeRole),
            ],
            SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
