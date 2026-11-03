namespace A2A.Protocol;

public sealed record AgentInterface
{
    public string Url { get => field ?? ""; init; }
    public string ProtocolBinding { get => field ?? ""; init; }
    public string? Tenant { get; init; }
    public string ProtocolVersion { get => field ?? ""; init; }
}

public sealed record AgentProvider
{
    public string Url { get => field ?? ""; init; }
    public string Organization { get => field ?? ""; init; }
}

public sealed record AgentExtension
{
    public string? Uri { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }
    public JsonElement? Params { get; init; }
}

public sealed record AgentCapabilities
{
    public bool? Streaming { get; init; }
    public bool? PushNotifications { get; init; }
    public IReadOnlyList<AgentExtension> Extensions { get => field ?? []; init; }
    public bool? ExtendedAgentCard { get; init; }
}

public sealed record AgentSkill
{
    public string Id { get => field ?? ""; init; }
    public string Name { get => field ?? ""; init; }
    public string Description { get => field ?? ""; init; }
    public IReadOnlyList<string> Tags { get => field ?? []; init; }
    public IReadOnlyList<string> Examples { get => field ?? []; init; }
    public IReadOnlyList<string> InputModes { get => field ?? []; init; }
    public IReadOnlyList<string> OutputModes { get => field ?? []; init; }
    public IReadOnlyList<SecurityRequirement> SecurityRequirements { get => field ?? []; init; }
}

public sealed record AgentCardSignature
{
    public string Protected { get => field ?? ""; init; }
    public string Signature { get => field ?? ""; init; }
    public JsonElement? Header { get; init; }
}

public sealed record AgentCard
{
    public string Name { get => field ?? ""; init; }
    public string Description { get => field ?? ""; init; }
    public IReadOnlyList<AgentInterface> SupportedInterfaces { get => field ?? []; init; }
    public AgentProvider? Provider { get; init; }
    public string Version { get => field ?? ""; init; }
    public string? DocumentationUrl { get; init; }
    public AgentCapabilities Capabilities { get => field ?? new(); init; }
    public IReadOnlyDictionary<string, SecurityScheme>? SecuritySchemes { get; init; }
    public IReadOnlyList<SecurityRequirement> SecurityRequirements { get => field ?? []; init; }
    public IReadOnlyList<string> DefaultInputModes { get => field ?? []; init; }
    public IReadOnlyList<string> DefaultOutputModes { get => field ?? []; init; }
    public IReadOnlyList<AgentSkill> Skills { get => field ?? []; init; }
    public IReadOnlyList<AgentCardSignature> Signatures { get => field ?? []; init; }
    public string? IconUrl { get; init; }
}
