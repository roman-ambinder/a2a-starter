namespace A2A.Protocol;

/// <summary>Result of SendMessage: exactly one of Task or Message.</summary>
public sealed record SendMessageResponse
{
    public AgentTask? Task { get; init; }
    public Message? Message { get; init; }
}

/// <summary>One streamed event or push payload: exactly one member is set.</summary>
public sealed record StreamResponse
{
    public AgentTask? Task { get; init; }
    public Message? Message { get; init; }
    public TaskStatusUpdateEvent? StatusUpdate { get; init; }
    public TaskArtifactUpdateEvent? ArtifactUpdate { get; init; }
}

public sealed record ListTasksResponse
{
    public IReadOnlyList<AgentTask> Tasks { get => field ?? []; init; }
    public string NextPageToken { get => field ?? ""; init; }
    public int PageSize { get; init; }
    public int TotalSize { get; init; }
}

public sealed record ListTaskPushNotificationConfigsResponse
{
    public IReadOnlyList<TaskPushNotificationConfig> Configs { get => field ?? []; init; }
    public string? NextPageToken { get; init; }
}
