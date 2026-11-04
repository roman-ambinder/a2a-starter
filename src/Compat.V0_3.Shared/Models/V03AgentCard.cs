using System.Text.Json;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>a2a.json <c>AgentCard</c>.</summary>
internal sealed record V03AgentCard
{
    public string ProtocolVersion { get; init; } = "0.3.0";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Url { get; init; } = "";
    public string? PreferredTransport { get; init; }
    public IReadOnlyList<V03AgentInterface>? AdditionalInterfaces { get; init; }
    public V03AgentProvider? Provider { get; init; }
    public string? IconUrl { get; init; }
    public string Version { get; init; } = "";
    public string? DocumentationUrl { get; init; }
    public V03AgentCapabilities Capabilities { get; init; } = new();
    public IReadOnlyDictionary<string, V03SecurityScheme>? SecuritySchemes { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? Security { get; init; }
    public IReadOnlyList<string> DefaultInputModes { get; init; } = [];
    public IReadOnlyList<string> DefaultOutputModes { get; init; } = [];
    public IReadOnlyList<V03AgentSkill> Skills { get; init; } = [];
    public bool? SupportsAuthenticatedExtendedCard { get; init; }
    public IReadOnlyList<V03AgentCardSignature>? Signatures { get; init; }
}

/// <summary>a2a.json <c>AgentInterface</c>.</summary>
internal sealed record V03AgentInterface
{
    public string Url { get; init; } = "";
    public string Transport { get; init; } = "";
}

/// <summary>a2a.json <c>AgentProvider</c>.</summary>
internal sealed record V03AgentProvider
{
    public string Organization { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>a2a.json <c>AgentCapabilities</c> (<c>stateTransitionHistory</c> is not in the proto).</summary>
internal sealed record V03AgentCapabilities
{
    public bool? Streaming { get; init; }
    public bool? PushNotifications { get; init; }
    public bool? StateTransitionHistory { get; init; }
    public IReadOnlyList<V03AgentExtension>? Extensions { get; init; }
}

/// <summary>a2a.json <c>AgentExtension</c>.</summary>
internal sealed record V03AgentExtension
{
    public string Uri { get; init; } = "";
    public string? Description { get; init; }
    public bool? Required { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Params { get; init; }
}

/// <summary>a2a.json <c>AgentSkill</c>.</summary>
internal sealed record V03AgentSkill
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string>? Examples { get; init; }
    public IReadOnlyList<string>? InputModes { get; init; }
    public IReadOnlyList<string>? OutputModes { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? Security { get; init; }
}

/// <summary>a2a.json <c>AgentCardSignature</c>.</summary>
internal sealed record V03AgentCardSignature
{
    public string Protected { get; init; } = "";
    public string Signature { get; init; } = "";
    public IReadOnlyDictionary<string, JsonElement>? Header { get; init; }
}

/// <summary>
/// a2a.json <c>SecurityScheme</c> (<c>apiKey</c>, <c>http</c>, <c>oauth2</c>, <c>openIdConnect</c>, <c>mutualTLS</c>) as one
/// flat record keyed by <see cref="Type"/>; only the members that belong to that type are set.
/// </summary>
internal sealed record V03SecurityScheme
{
    public string Type { get; init; } = "";
    public string? Description { get; init; }
    public string? In { get; init; }
    public string? Name { get; init; }
    public string? Scheme { get; init; }
    public string? BearerFormat { get; init; }
    public V03OAuthFlows? Flows { get; init; }
    public string? Oauth2MetadataUrl { get; init; }
    public string? OpenIdConnectUrl { get; init; }
}

/// <summary>a2a.json <c>OAuthFlows</c>.</summary>
internal sealed record V03OAuthFlows
{
    public V03OAuthFlow? Implicit { get; init; }
    public V03OAuthFlow? Password { get; init; }
    public V03OAuthFlow? ClientCredentials { get; init; }
    public V03OAuthFlow? AuthorizationCode { get; init; }
}

/// <summary>One OAuth flow; a2a.json <c>ImplicitOAuthFlow</c>, <c>PasswordOAuthFlow</c>, <c>ClientCredentialsOAuthFlow</c> and <c>AuthorizationCodeOAuthFlow</c> share these members.</summary>
internal sealed record V03OAuthFlow
{
    public string? AuthorizationUrl { get; init; }
    public string? TokenUrl { get; init; }
    public string? RefreshUrl { get; init; }
    public IReadOnlyDictionary<string, string> Scopes { get; init; } = new Dictionary<string, string>();
}
