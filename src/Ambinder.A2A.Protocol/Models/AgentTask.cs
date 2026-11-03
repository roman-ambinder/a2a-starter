namespace Ambinder.A2A.Protocol;

/// <summary>A unit of work (proto <c>Task</c>).</summary>
public sealed record AgentTask
{
    public string Id { get => field ?? ""; init; }
    public string? ContextId { get; init; }
    public AgentTaskStatus Status { get => field ?? new(); init; }
    public IReadOnlyList<Artifact> Artifacts { get => field ?? []; init; }
    public IReadOnlyList<Message> History { get => field ?? []; init; }
    public JsonElement? Metadata { get; init; }
}

/// <summary>Current state of a task with an optional message and timestamp.</summary>
public sealed record AgentTaskStatus
{
    public TaskState State { get; init; }
    public Message? Message { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
}
