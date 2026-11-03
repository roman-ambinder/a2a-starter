namespace Ambinder.A2A.Protocol;

public sealed record StringList
{
    public IReadOnlyList<string> List { get => field ?? []; init; }
}

public sealed record SecurityRequirement
{
    public IReadOnlyDictionary<string, StringList> Schemes { get => field ?? new Dictionary<string, StringList>(); init; }
}

/// <summary>Security scheme; exactly one member is set (proto oneof).</summary>
public sealed record SecurityScheme
{
    public ApiKeySecurityScheme? ApiKeySecurityScheme { get; init; }
    public HttpAuthSecurityScheme? HttpAuthSecurityScheme { get; init; }
    public OAuth2SecurityScheme? Oauth2SecurityScheme { get; init; }
    public OpenIdConnectSecurityScheme? OpenIdConnectSecurityScheme { get; init; }
    public MutualTlsSecurityScheme? MtlsSecurityScheme { get; init; }
}

public sealed record ApiKeySecurityScheme
{
    public string? Description { get; init; }
    /// <summary>"query", "header" or "cookie".</summary>
    public string Location { get => field ?? ""; init; }
    public string Name { get => field ?? ""; init; }
}

public sealed record HttpAuthSecurityScheme
{
    public string? Description { get; init; }
    public string Scheme { get => field ?? ""; init; }
    public string? BearerFormat { get; init; }
}

public sealed record OAuth2SecurityScheme
{
    public string? Description { get; init; }
    public OAuthFlows Flows { get => field ?? new(); init; }
    public string? Oauth2MetadataUrl { get; init; }
}

public sealed record OpenIdConnectSecurityScheme
{
    public string? Description { get; init; }
    public string OpenIdConnectUrl { get => field ?? ""; init; }
}

public sealed record MutualTlsSecurityScheme
{
    public string? Description { get; init; }
}

/// <summary>
/// OAuth flows; exactly one member is set. <see cref="Implicit"/> and <see cref="Password"/> are deprecated in the spec
/// (removed from OAuth 2.1). They are modeled only so cards that use them round-trip; the client never uses them to get tokens.
/// </summary>
public sealed record OAuthFlows
{
    public AuthorizationCodeOAuthFlow? AuthorizationCode { get; init; }
    public ClientCredentialsOAuthFlow? ClientCredentials { get; init; }
    /// <summary>Deprecated: use authorization code with PKCE, or device code.</summary>
    public ImplicitOAuthFlow? Implicit { get; init; }
    /// <summary>Deprecated: use authorization code with PKCE, or device code.</summary>
    public PasswordOAuthFlow? Password { get; init; }
    public DeviceCodeOAuthFlow? DeviceCode { get; init; }
}

/// <summary>Deprecated OAuth implicit flow (proto <c>ImplicitOAuthFlow</c>). Kept so cards round-trip.</summary>
public sealed record ImplicitOAuthFlow
{
    public string AuthorizationUrl { get => field ?? ""; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

/// <summary>Deprecated OAuth resource owner password flow (proto <c>PasswordOAuthFlow</c>). Kept so cards round-trip.</summary>
public sealed record PasswordOAuthFlow
{
    public string TokenUrl { get => field ?? ""; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

public sealed record AuthorizationCodeOAuthFlow
{
    public string AuthorizationUrl { get => field ?? ""; init; }
    public string TokenUrl { get => field ?? ""; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
    public bool PkceRequired { get; init; }
}

public sealed record ClientCredentialsOAuthFlow
{
    public string TokenUrl { get => field ?? ""; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

public sealed record DeviceCodeOAuthFlow
{
    public string DeviceAuthorizationUrl { get => field ?? ""; init; }
    public string TokenUrl { get => field ?? ""; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}
