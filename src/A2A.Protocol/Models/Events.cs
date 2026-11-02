namespace A2A.Protocol;

/// <summary>Notifies a change in task status.</summary>
public sealed record TaskStatusUpdateEvent
{
    public string TaskId { get => field ?? ""; init; }
    public string ContextId { get => field ?? ""; init; }
    public AgentTaskStatus Status { get => field ?? new(); init; }
    public JsonElement? Metadata { get; init; }
}

/// <summary>Notifies a new or updated artifact chunk.</summary>
public sealed record TaskArtifactUpdateEvent
{
    public string TaskId { get => field ?? ""; init; }
    public string ContextId { get => field ?? ""; init; }
    public Artifact Artifact { get => field ?? new(); init; }
    public bool Append { get; init; }
    public bool LastChunk { get; init; }
    public JsonElement? Metadata { get; init; }
}
