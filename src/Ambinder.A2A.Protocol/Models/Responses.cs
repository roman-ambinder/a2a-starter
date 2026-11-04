namespace Ambinder.A2A.Protocol;

/// <summary>Result of SendMessage: exactly one of Task or Message.</summary>
/// <remarks>Check <see cref="Task"/> first. A reply without a task is a plain <see cref="Message"/>.</remarks>
public sealed record SendMessageResponse
{
    /// <summary>The task, when the agent created one.</summary>
    public AgentTask? Task { get; init; }
    /// <summary>A direct reply, when the agent answered without a task.</summary>
    public Message? Message { get; init; }
}

/// <summary>One streamed event or push payload: exactly one member is set.</summary>
public sealed record StreamResponse
{
    /// <summary>The task snapshot, when this event is one.</summary>
    public AgentTask? Task { get; init; }
    /// <summary>A message, when this event is one.</summary>
    public Message? Message { get; init; }
    /// <summary>A status update, when this event is one.</summary>
    public TaskStatusUpdateEvent? StatusUpdate { get; init; }
    /// <summary>An artifact update, when this event is one.</summary>
    public TaskArtifactUpdateEvent? ArtifactUpdate { get; init; }
}

/// <summary>One page of tasks, the result of <c>ListTasks</c>.</summary>
public sealed record ListTasksResponse
{
    /// <summary>The tasks of this page.</summary>
    public IReadOnlyList<AgentTask> Tasks { get => field ?? []; init; }
    /// <summary>The token of the next page. It is empty on the last page.</summary>
    public string NextPageToken { get => field ?? ""; init; }
    /// <summary>The page size the server used.</summary>
    public int PageSize { get; init; }
    /// <summary>The total number of tasks that match the filters.</summary>
    public int TotalSize { get; init; }
}

/// <summary>One page of push notification configurations.</summary>
public sealed record ListTaskPushNotificationConfigsResponse
{
    /// <summary>The configurations of this page.</summary>
    public IReadOnlyList<TaskPushNotificationConfig> Configs { get => field ?? []; init; }
    /// <summary>The token of the next page, or <see langword="null"/> on the last page.</summary>
    public string? NextPageToken { get; init; }
}
