using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Testing;

/// <summary>One task event seen by <see cref="TaskEventLog"/>.</summary>
public sealed record TaskEvent
{
    /// <summary>The name of the agent the task belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>The task id.</summary>
    public required string TaskId { get; init; }

    /// <summary>The task state after the event, when the task has one.</summary>
    public TaskState? State { get; init; }

    /// <summary>The event.</summary>
    public required StreamResponse Event { get; init; }

    /// <summary>The position of the event in the log, starting at 0.</summary>
    public required long Index { get; init; }
}
