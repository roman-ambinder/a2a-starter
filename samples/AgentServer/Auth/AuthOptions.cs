namespace AgentServer.Auth;

/// <summary>The <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Entra-shaped authority, e.g. <c>http://localhost:5100/{tenant}/v2.0</c>. JwtBearer reads its metadata from here.</summary>
    public string Authority { get; set; } = "";

    /// <summary>Expected token audience, e.g. <c>api://a2a-demo</c>.</summary>
    public string Audience { get; set; } = "";

    /// <summary>Token endpoint advertised in the cards' <c>entra</c> oauth2 scheme.</summary>
    public string TokenUrl { get; set; } = "";

    /// <summary>OpenID configuration URL advertised in the cards' <c>entraOidc</c> scheme.</summary>
    public string OpenIdConfigurationUrl { get; set; } = "";

    /// <summary>JwtBearer <c>RequireHttpsMetadata</c>. False only in Development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>API keys by key name (name → secret). The name becomes the caller identity (<c>apikey:{name}</c>).</summary>
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The scope clients request: <c>{Audience}/.default</c>.</summary>
    public string Scope => $"{Audience}/.default";
}
