using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>
/// The A2A operations, independent of transport. JSON-RPC, REST and gRPC all call this.
/// Errors are thrown as <see cref="A2AException"/>.
/// </summary>
public interface IA2ARequestHandler
{
    Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct);

    /// <summary>Validation errors are thrown on the first <c>MoveNextAsync</c>, before any event.</summary>
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct);

    Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct);

    Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct);

    Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct);

    /// <summary>Validation errors are thrown on the first <c>MoveNextAsync</c>, before any event.</summary>
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct);

    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct);

    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct);

    Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct);

    Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct);

    Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct);
}
