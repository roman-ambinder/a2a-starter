namespace Ambinder.A2A.Protocol;

/// <summary>Notifies a change in task status.</summary>
public sealed record TaskStatusUpdateEvent
{
    /// <summary>The ID of the task that changed.</summary>
    public string TaskId { get => field ?? ""; init; }
    /// <summary>The ID of the context of the task.</summary>
    public string ContextId { get => field ?? ""; init; }
    /// <summary>The new status.</summary>
    public AgentTaskStatus Status { get => field ?? new(); init; }
    /// <summary>Extra data attached to the event, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
}

/// <summary>Notifies a new or updated artifact chunk.</summary>
public sealed record TaskArtifactUpdateEvent
{
    /// <summary>The ID of the task that produced the artifact.</summary>
    public string TaskId { get => field ?? ""; init; }
    /// <summary>The ID of the context of the task.</summary>
    public string ContextId { get => field ?? ""; init; }
    /// <summary>The artifact, or the new chunk of it.</summary>
    public Artifact Artifact { get => field ?? new(); init; }
    /// <summary>Whether the parts are added to an artifact with the same ID (<see langword="true"/>) or replace it.</summary>
    public bool Append { get; init; }
    /// <summary>Whether this is the last chunk of the artifact.</summary>
    public bool LastChunk { get; init; }
    /// <summary>Extra data attached to the event, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
}
