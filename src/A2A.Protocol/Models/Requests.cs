namespace A2A.Protocol;

public sealed record SendMessageConfiguration
{
    public IReadOnlyList<string> AcceptedOutputModes { get => field ?? []; init; }
    public TaskPushNotificationConfig? TaskPushNotificationConfig { get; init; }
    public int? HistoryLength { get; init; }
    public bool ReturnImmediately { get; init; }
}

public sealed record SendMessageRequest
{
    public string? Tenant { get; init; }
    public Message Message { get => field ?? new(); init; }
    public SendMessageConfiguration? Configuration { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record GetTaskRequest
{
    public string? Tenant { get; init; }
    public string Id { get => field ?? ""; init; }
    public int? HistoryLength { get; init; }
}

public sealed record ListTasksRequest
{
    public string? Tenant { get; init; }
    public string? ContextId { get; init; }
    public TaskState? Status { get; init; }
    public int? PageSize { get; init; }
    public string? PageToken { get; init; }
    public int? HistoryLength { get; init; }
    public DateTimeOffset? StatusTimestampAfter { get; init; }
    public bool? IncludeArtifacts { get; init; }
}

public sealed record CancelTaskRequest
{
    public string? Tenant { get; init; }
    public string Id { get => field ?? ""; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record SubscribeToTaskRequest
{
    public string? Tenant { get; init; }
    public string Id { get => field ?? ""; init; }
}

public sealed record GetTaskPushNotificationConfigRequest
{
    public string? Tenant { get; init; }
    public string TaskId { get => field ?? ""; init; }
    public string Id { get => field ?? ""; init; }
}

public sealed record DeleteTaskPushNotificationConfigRequest
{
    public string? Tenant { get; init; }
    public string TaskId { get => field ?? ""; init; }
    public string Id { get => field ?? ""; init; }
}

public sealed record ListTaskPushNotificationConfigsRequest
{
    public string? Tenant { get; init; }
    public string TaskId { get => field ?? ""; init; }
    public int? PageSize { get; init; }
    public string? PageToken { get; init; }
}

public sealed record GetExtendedAgentCardRequest
{
    public string? Tenant { get; init; }
}
