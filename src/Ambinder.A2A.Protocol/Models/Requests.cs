namespace Ambinder.A2A.Protocol;

/// <summary>Options for a <c>SendMessage</c> call.</summary>
public sealed record SendMessageConfiguration
{
    /// <summary>The media types the caller accepts in the reply. Empty means any.</summary>
    public IReadOnlyList<string> AcceptedOutputModes { get => field ?? []; init; }
    /// <summary>A push notification configuration to set for the task, or <see langword="null"/>.</summary>
    public TaskPushNotificationConfig? TaskPushNotificationConfig { get; init; }
    /// <summary>The most messages of history to return in the task, or <see langword="null"/> for the server default. Zero means none.</summary>
    public int? HistoryLength { get; init; }
    /// <summary>Whether the call returns at once with the task instead of waiting for it to finish. The default is <see langword="false"/>.</summary>
    public bool ReturnImmediately { get; init; }
}

/// <summary>The request of <c>SendMessage</c> and <c>SendStreamingMessage</c>.</summary>
public sealed record SendMessageRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The message to send.</summary>
    public Message Message { get => field ?? new(); init; }
    /// <summary>Options for the call, or <see langword="null"/>.</summary>
    public SendMessageConfiguration? Configuration { get; init; }
    /// <summary>Extra data attached to the request, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
}

/// <summary>The request of <c>GetTask</c>.</summary>
public sealed record GetTaskRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task.</summary>
    public string Id { get => field ?? ""; init; }
    /// <summary>The most messages of history to return, or <see langword="null"/> for the server default. Zero means none.</summary>
    public int? HistoryLength { get; init; }
}

/// <summary>The request of <c>ListTasks</c>. Every filter is optional.</summary>
public sealed record ListTasksRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>Return only tasks of this context.</summary>
    public string? ContextId { get; init; }
    /// <summary>Return only tasks in this state.</summary>
    public TaskState? Status { get; init; }
    /// <summary>The most tasks to return in one page, or <see langword="null"/> for the server default.</summary>
    public int? PageSize { get; init; }
    /// <summary>The token of the page to return. Use <see cref="ListTasksResponse.NextPageToken"/> of the previous page, or <see langword="null"/> for the first page.</summary>
    public string? PageToken { get; init; }
    /// <summary>The most messages of history to return in each task, or <see langword="null"/> for the server default. Zero means none.</summary>
    public int? HistoryLength { get; init; }
    /// <summary>Return only tasks whose status changed after this time.</summary>
    public DateTimeOffset? StatusTimestampAfter { get; init; }
    /// <summary>Whether to include artifacts in the tasks. <see langword="null"/> means the server default.</summary>
    public bool? IncludeArtifacts { get; init; }
}

/// <summary>The request of <c>CancelTask</c>.</summary>
public sealed record CancelTaskRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task to cancel.</summary>
    public string Id { get => field ?? ""; init; }
    /// <summary>Extra data attached to the request, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
}

/// <summary>The request of <c>SubscribeToTask</c>.</summary>
public sealed record SubscribeToTaskRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task to follow.</summary>
    public string Id { get => field ?? ""; init; }
}

/// <summary>The request of <c>GetTaskPushNotificationConfig</c>.</summary>
public sealed record GetTaskPushNotificationConfigRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task.</summary>
    public string TaskId { get => field ?? ""; init; }
    /// <summary>The ID of the push notification configuration.</summary>
    public string Id { get => field ?? ""; init; }
}

/// <summary>The request of <c>DeleteTaskPushNotificationConfig</c>.</summary>
public sealed record DeleteTaskPushNotificationConfigRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task.</summary>
    public string TaskId { get => field ?? ""; init; }
    /// <summary>The ID of the push notification configuration to delete.</summary>
    public string Id { get => field ?? ""; init; }
}

/// <summary>The request of <c>ListTaskPushNotificationConfigs</c>.</summary>
public sealed record ListTaskPushNotificationConfigsRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of the task.</summary>
    public string TaskId { get => field ?? ""; init; }
    /// <summary>The most configurations to return in one page, or <see langword="null"/> for the server default.</summary>
    public int? PageSize { get; init; }
    /// <summary>The token of the page to return, or <see langword="null"/> for the first page.</summary>
    public string? PageToken { get; init; }
}

/// <summary>The request of <c>GetExtendedAgentCard</c>.</summary>
public sealed record GetExtendedAgentCardRequest
{
    /// <summary>The tenant to send the request to, or <see langword="null"/> for the default tenant.</summary>
    public string? Tenant { get; init; }
}
