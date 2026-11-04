namespace Ambinder.A2A.Protocol;

/// <summary>A list of strings. It exists because the wire format needs a message type for a list inside a map.</summary>
public sealed record StringList
{
    /// <summary>The strings.</summary>
    public IReadOnlyList<string> List { get => field ?? []; init; }
}

/// <summary>One way to satisfy the security of an agent: every scheme in the map must be used together.</summary>
public sealed record SecurityRequirement
{
    /// <summary>The scheme names, each with the scopes it needs. Scopes are empty for schemes that have none.</summary>
    public IReadOnlyDictionary<string, StringList> Schemes { get => field ?? new Dictionary<string, StringList>(); init; }
}

/// <summary>Security scheme; exactly one member is set (proto oneof).</summary>
public sealed record SecurityScheme
{
    /// <summary>An API key scheme, when this is one.</summary>
    public ApiKeySecurityScheme? ApiKeySecurityScheme { get; init; }
    /// <summary>An HTTP authentication scheme, when this is one.</summary>
    public HttpAuthSecurityScheme? HttpAuthSecurityScheme { get; init; }
    /// <summary>An OAuth 2.0 scheme, when this is one.</summary>
    public OAuth2SecurityScheme? Oauth2SecurityScheme { get; init; }
    /// <summary>An OpenID Connect scheme, when this is one.</summary>
    public OpenIdConnectSecurityScheme? OpenIdConnectSecurityScheme { get; init; }
    /// <summary>A mutual TLS scheme, when this is one.</summary>
    public MutualTlsSecurityScheme? MtlsSecurityScheme { get; init; }
}

/// <summary>A security scheme that uses an API key.</summary>
public sealed record ApiKeySecurityScheme
{
    /// <summary>A description of the scheme.</summary>
    public string? Description { get; init; }
    /// <summary>"query", "header" or "cookie".</summary>
    public string Location { get => field ?? ""; init; }
    /// <summary>The name of the header, query parameter or cookie that holds the key.</summary>
    public string Name { get => field ?? ""; init; }
}

/// <summary>A security scheme that uses an HTTP authentication scheme, for example Bearer.</summary>
public sealed record HttpAuthSecurityScheme
{
    /// <summary>A description of the scheme.</summary>
    public string? Description { get; init; }
    /// <summary>The name of the HTTP scheme, for example <c>Bearer</c>.</summary>
    public string Scheme { get => field ?? ""; init; }
    /// <summary>A hint for the format of a bearer token, for example <c>JWT</c>.</summary>
    public string? BearerFormat { get; init; }
}

/// <summary>A security scheme that uses OAuth 2.0.</summary>
public sealed record OAuth2SecurityScheme
{
    /// <summary>A description of the scheme.</summary>
    public string? Description { get; init; }
    /// <summary>The OAuth flows the scheme supports.</summary>
    public OAuthFlows Flows { get => field ?? new(); init; }
    /// <summary>A URL of the authorization server metadata (RFC 8414), or <see langword="null"/>.</summary>
    public string? Oauth2MetadataUrl { get; init; }
}

/// <summary>A security scheme that uses OpenID Connect.</summary>
public sealed record OpenIdConnectSecurityScheme
{
    /// <summary>A description of the scheme.</summary>
    public string? Description { get; init; }
    /// <summary>The URL of the OpenID Connect discovery document.</summary>
    public string OpenIdConnectUrl { get => field ?? ""; init; }
}

/// <summary>A security scheme that uses mutual TLS (a client certificate).</summary>
public sealed record MutualTlsSecurityScheme
{
    /// <summary>A description of the scheme.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// OAuth flows; exactly one member is set. <see cref="Implicit"/> and <see cref="Password"/> are deprecated in the spec
/// (removed from OAuth 2.1). They are modeled only so cards that use them round-trip; the client never uses them to get tokens.
/// </summary>
public sealed record OAuthFlows
{
    /// <summary>The authorization code flow, when it is the one in use.</summary>
    public AuthorizationCodeOAuthFlow? AuthorizationCode { get; init; }
    /// <summary>The client credentials flow, when it is the one in use.</summary>
    public ClientCredentialsOAuthFlow? ClientCredentials { get; init; }
    /// <summary>Deprecated: use authorization code with PKCE, or device code.</summary>
    [Obsolete("Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.")]
    public ImplicitOAuthFlow? Implicit { get; init; }
    /// <summary>Deprecated: use authorization code with PKCE, or device code.</summary>
    [Obsolete("Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.")]
    public PasswordOAuthFlow? Password { get; init; }
    /// <summary>The device code flow, when it is the one in use.</summary>
    public DeviceCodeOAuthFlow? DeviceCode { get; init; }
}

/// <summary>Deprecated OAuth implicit flow (proto <c>ImplicitOAuthFlow</c>). Kept so cards round-trip.</summary>
[Obsolete("Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.")]
public sealed record ImplicitOAuthFlow
{
    /// <summary>The URL of the authorization endpoint.</summary>
    public string AuthorizationUrl { get => field ?? ""; init; }
    /// <summary>The URL for refreshing tokens, or <see langword="null"/>.</summary>
    public string? RefreshUrl { get; init; }
    /// <summary>The available scopes, each with a short description.</summary>
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

/// <summary>Deprecated OAuth resource owner password flow (proto <c>PasswordOAuthFlow</c>). Kept so cards round-trip.</summary>
[Obsolete("Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.")]
public sealed record PasswordOAuthFlow
{
    /// <summary>The URL of the token endpoint.</summary>
    public string TokenUrl { get => field ?? ""; init; }
    /// <summary>The URL for refreshing tokens, or <see langword="null"/>.</summary>
    public string? RefreshUrl { get; init; }
    /// <summary>The available scopes, each with a short description.</summary>
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

/// <summary>The OAuth 2.0 authorization code flow.</summary>
public sealed record AuthorizationCodeOAuthFlow
{
    /// <summary>The URL of the authorization endpoint.</summary>
    public string AuthorizationUrl { get => field ?? ""; init; }
    /// <summary>The URL of the token endpoint.</summary>
    public string TokenUrl { get => field ?? ""; init; }
    /// <summary>The URL for refreshing tokens, or <see langword="null"/>.</summary>
    public string? RefreshUrl { get; init; }
    /// <summary>The available scopes, each with a short description.</summary>
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
    /// <summary>Whether the flow needs PKCE. The default is <see langword="false"/>.</summary>
    public bool PkceRequired { get; init; }
}

/// <summary>The OAuth 2.0 client credentials flow.</summary>
public sealed record ClientCredentialsOAuthFlow
{
    /// <summary>The URL of the token endpoint.</summary>
    public string TokenUrl { get => field ?? ""; init; }
    /// <summary>The URL for refreshing tokens, or <see langword="null"/>.</summary>
    public string? RefreshUrl { get; init; }
    /// <summary>The available scopes, each with a short description.</summary>
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}

/// <summary>The OAuth 2.0 device code flow.</summary>
public sealed record DeviceCodeOAuthFlow
{
    /// <summary>The URL of the device authorization endpoint.</summary>
    public string DeviceAuthorizationUrl { get => field ?? ""; init; }
    /// <summary>The URL of the token endpoint.</summary>
    public string TokenUrl { get => field ?? ""; init; }
    /// <summary>The URL for refreshing tokens, or <see langword="null"/>.</summary>
    public string? RefreshUrl { get; init; }
    /// <summary>The available scopes, each with a short description.</summary>
    public IReadOnlyDictionary<string, string> Scopes { get => field ?? new Dictionary<string, string>(); init; }
}
