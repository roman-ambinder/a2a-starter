namespace Ambinder.A2A.Protocol;

/// <summary>A unit of work (proto <c>Task</c>).</summary>
public sealed record AgentTask
{
    /// <summary>The ID of the task.</summary>
    public string Id { get => field ?? ""; init; }
    /// <summary>The ID of the context (conversation) the task belongs to.</summary>
    public string? ContextId { get; init; }
    /// <summary>The current status of the task.</summary>
    public AgentTaskStatus Status { get => field ?? new(); init; }
    /// <summary>The outputs the task has produced so far.</summary>
    public IReadOnlyList<Artifact> Artifacts { get => field ?? []; init; }
    /// <summary>The messages exchanged for this task. It may be shortened by <c>HistoryLength</c> in the request.</summary>
    public IReadOnlyList<Message> History { get => field ?? []; init; }
    /// <summary>Extra data attached to the task, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
}

/// <summary>Current state of a task with an optional message and timestamp.</summary>
public sealed record AgentTaskStatus
{
    /// <summary>The state of the task.</summary>
    public TaskState State { get; init; }
    /// <summary>A message that explains the state, for example the question when the state is <see cref="TaskState.InputRequired"/>.</summary>
    public Message? Message { get; init; }
    /// <summary>When the task entered this state (UTC).</summary>
    public DateTimeOffset? Timestamp { get; init; }
}
