using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Stores;

/// <summary>A validated list query. The core has checked page size and history length and decoded the cursor.</summary>
public sealed record TaskQuery
{
    /// <summary>The tenant, agent and owner the query is limited to.</summary>
    public required OwnerScope Scope { get; init; }

    /// <summary>Only tasks with this context id.</summary>
    public string? ContextId { get; init; }

    /// <summary>Only tasks in this state.</summary>
    public TaskState? State { get; init; }

    /// <summary>Only tasks whose status timestamp is at or after this time (inclusive).</summary>
    public DateTimeOffset? StatusTimestampFrom { get; init; }

    /// <summary>Page size, already validated: 1 to 100.</summary>
    public required int PageSize { get; init; }

    /// <summary>Return items strictly after this cursor. Already decoded.</summary>
    public TaskCursor? After { get; init; }

    /// <summary>Include artifacts. When false, artifacts are empty.</summary>
    public bool IncludeArtifacts { get; init; }

    /// <summary>Keep the last N history messages (0 means none). Null keeps all. Already validated.</summary>
    public int? HistoryLength { get; init; }
}

/// <summary>A position in the list order.</summary>
/// <param name="StatusTimestamp">The status timestamp of the last item.</param>
/// <param name="TaskId">The task id of the last item.</param>
public readonly record struct TaskCursor(DateTimeOffset StatusTimestamp, string TaskId);

/// <summary>One page of tasks.</summary>
public sealed record TaskPage
{
    /// <summary>The tasks on this page.</summary>
    public required IReadOnlyList<AgentTask> Tasks { get; init; }

    /// <summary>The cursor of the last item, or null on the last page.</summary>
    public TaskCursor? Next { get; init; }

    /// <summary>The number of all matches of the filter, ignoring the cursor.</summary>
    public required int TotalSize { get; init; }
}
