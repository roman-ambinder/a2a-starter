namespace Ambinder.A2A.Protocol;

/// <summary>One way to reach an agent: a URL, a protocol binding and a protocol version.</summary>
public sealed record AgentInterface
{
    /// <summary>The absolute URL of this interface.</summary>
    public string Url { get => field ?? ""; init; }
    /// <summary>The protocol binding: <c>JSONRPC</c>, <c>HTTP+JSON</c> or <c>GRPC</c>. See <see cref="A2AConstants"/>.</summary>
    public string ProtocolBinding { get => field ?? ""; init; }
    /// <summary>The tenant this interface serves, or <see langword="null"/> when it has none.</summary>
    public string? Tenant { get; init; }
    /// <summary>The A2A protocol version this interface speaks, for example <c>1.0</c>.</summary>
    public string ProtocolVersion { get => field ?? ""; init; }
}

/// <summary>The organization that runs an agent.</summary>
public sealed record AgentProvider
{
    /// <summary>The web site of the organization.</summary>
    public string Url { get => field ?? ""; init; }
    /// <summary>The name of the organization.</summary>
    public string Organization { get => field ?? ""; init; }
}

/// <summary>A protocol extension that an agent supports.</summary>
public sealed record AgentExtension
{
    /// <summary>The URI that identifies the extension.</summary>
    public string? Uri { get; init; }
    /// <summary>A short description of how the agent uses the extension.</summary>
    public string? Description { get; init; }
    /// <summary>Whether a client must support this extension to talk to the agent. The default is <see langword="false"/>.</summary>
    public bool Required { get; init; }
    /// <summary>Settings of the extension, as a JSON value.</summary>
    public JsonElement? Params { get; init; }
}

/// <summary>The optional features an agent supports.</summary>
public sealed record AgentCapabilities
{
    /// <summary>Whether the agent can stream results. <see langword="null"/> means not declared.</summary>
    public bool? Streaming { get; init; }
    /// <summary>Whether the agent can send push notifications. <see langword="null"/> means not declared.</summary>
    public bool? PushNotifications { get; init; }
    /// <summary>The extensions the agent supports. The default is an empty list.</summary>
    public IReadOnlyList<AgentExtension> Extensions { get => field ?? []; init; }
    /// <summary>Whether the agent has an extended agent card for authenticated callers. <see langword="null"/> means not declared.</summary>
    public bool? ExtendedAgentCard { get; init; }
}

/// <summary>A skill (a kind of work) that an agent can do.</summary>
public sealed record AgentSkill
{
    /// <summary>The ID of the skill. It is unique within the agent.</summary>
    public string Id { get => field ?? ""; init; }
    /// <summary>The display name of the skill.</summary>
    public string Name { get => field ?? ""; init; }
    /// <summary>What the skill does.</summary>
    public string Description { get => field ?? ""; init; }
    /// <summary>Keywords that describe the skill.</summary>
    public IReadOnlyList<string> Tags { get => field ?? []; init; }
    /// <summary>Example prompts for the skill.</summary>
    public IReadOnlyList<string> Examples { get => field ?? []; init; }
    /// <summary>The media types the skill accepts. Empty means the agent defaults apply.</summary>
    public IReadOnlyList<string> InputModes { get => field ?? []; init; }
    /// <summary>The media types the skill produces. Empty means the agent defaults apply.</summary>
    public IReadOnlyList<string> OutputModes { get => field ?? []; init; }
    /// <summary>The security requirements for this skill. Empty means the agent-level requirements apply.</summary>
    public IReadOnlyList<SecurityRequirement> SecurityRequirements { get => field ?? []; init; }
}

/// <summary>A JSON Web Signature of an agent card.</summary>
public sealed record AgentCardSignature
{
    /// <summary>The protected header of the signature, base64url encoded.</summary>
    public string Protected { get => field ?? ""; init; }
    /// <summary>The signature value, base64url encoded.</summary>
    public string Signature { get => field ?? ""; init; }
    /// <summary>The unprotected header of the signature, as a JSON value.</summary>
    public JsonElement? Header { get; init; }
}

/// <summary>The public description of an agent: who it is, how to reach it, what it can do and how to authenticate.</summary>
public sealed record AgentCard
{
    /// <summary>The display name of the agent.</summary>
    public string Name { get => field ?? ""; init; }
    /// <summary>What the agent does.</summary>
    public string Description { get => field ?? ""; init; }
    /// <summary>The interfaces clients can use to reach the agent, in order of preference.</summary>
    public IReadOnlyList<AgentInterface> SupportedInterfaces { get => field ?? []; init; }
    /// <summary>The organization that runs the agent, or <see langword="null"/>.</summary>
    public AgentProvider? Provider { get; init; }
    /// <summary>The version of the agent (not of the protocol).</summary>
    public string Version { get => field ?? ""; init; }
    /// <summary>A URL of the documentation of the agent, or <see langword="null"/>.</summary>
    public string? DocumentationUrl { get; init; }
    /// <summary>The optional features the agent supports.</summary>
    public AgentCapabilities Capabilities { get => field ?? new(); init; }
    /// <summary>The security schemes the agent accepts, by name, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyDictionary<string, SecurityScheme>? SecuritySchemes { get; init; }
    /// <summary>The security requirements for calling the agent. Each item names schemes from <see cref="SecuritySchemes"/>.</summary>
    public IReadOnlyList<SecurityRequirement> SecurityRequirements { get => field ?? []; init; }
    /// <summary>The media types the agent accepts when a skill does not say otherwise.</summary>
    public IReadOnlyList<string> DefaultInputModes { get => field ?? []; init; }
    /// <summary>The media types the agent produces when a skill does not say otherwise.</summary>
    public IReadOnlyList<string> DefaultOutputModes { get => field ?? []; init; }
    /// <summary>The skills of the agent.</summary>
    public IReadOnlyList<AgentSkill> Skills { get => field ?? []; init; }
    /// <summary>The signatures of this card. Empty when the card is not signed.</summary>
    public IReadOnlyList<AgentCardSignature> Signatures { get => field ?? []; init; }
    /// <summary>A URL of an icon for the agent, or <see langword="null"/>.</summary>
    public string? IconUrl { get; init; }
}
