using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>
/// An A2A client over one protocol binding. The 11 operations mirror the server-side request handler.
/// Protocol errors throw <see cref="A2AException"/>; HTTP 401/403 (gRPC UNAUTHENTICATED / PERMISSION_DENIED)
/// throw <see cref="A2AAuthException"/>. A response that cannot be parsed or breaks a oneof rule throws
/// <see cref="A2AException"/> with <see cref="A2AErrorCode.InvalidAgentResponse"/>.
/// </summary>
public interface IA2AClient : IAsyncDisposable
{
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default);
    Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default);
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default);
    Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default);
    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default);
    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default);
    Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default);
    Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default);
    Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default);
}
