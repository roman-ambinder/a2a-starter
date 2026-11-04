using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>
/// The A2A operations, independent of transport. JSON-RPC, REST and gRPC all call this.
/// Errors are thrown as <see cref="A2AException"/>.
/// </summary>
/// <remarks>
/// The default implementation is registered as a singleton by <c>AddA2AServerCore</c> and wrapped with tracing. Calls can overlap, so
/// an implementation must be thread-safe. Each method takes the <see cref="A2ACallContext"/> that the transport built. Every method can
/// also throw <see cref="OperationCanceledException"/> when <c>ct</c> is canceled.
/// </remarks>
public interface IA2ARequestHandler
{
    /// <summary>Sends a message to an agent and waits for the reply.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The message and its options.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task the agent created, or its direct reply.</returns>
    /// <exception cref="A2AException">The request is not valid, the agent is unknown, or the task cannot take this message.</exception>
    Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct);

    /// <summary>Sends a message and streams the events. Validation errors are thrown on the first <c>MoveNextAsync</c>, before any event.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The message and its options.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The events in order.</returns>
    /// <exception cref="A2AException">The request is not valid, or the agent cannot stream.</exception>
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct);

    /// <summary>Gets a task that the caller owns.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The task ID and the history length.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task.</returns>
    /// <exception cref="A2AException">The task does not exist or belongs to another owner (<see cref="A2AErrorCode.TaskNotFound"/>).</exception>
    Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct);

    /// <summary>Lists the tasks the caller owns, one page at a time.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The filters and the page.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of tasks.</returns>
    /// <exception cref="A2AException">The page size, history length or page token is not valid (<see cref="A2AErrorCode.InvalidParams"/>).</exception>
    Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct);

    /// <summary>Cancels a task that the caller owns.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The ID of the task.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task after the cancel.</returns>
    /// <exception cref="A2AException">The task does not exist (<see cref="A2AErrorCode.TaskNotFound"/>) or cannot be canceled (<see cref="A2AErrorCode.TaskNotCancelable"/>).</exception>
    Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct);

    /// <summary>Follows a task and streams its events. Validation errors are thrown on the first <c>MoveNextAsync</c>, before any event.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The ID of the task.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The events in order. The first one is the current task.</returns>
    /// <exception cref="A2AException">The task does not exist or cannot be followed.</exception>
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct);

    /// <summary>Creates a push notification configuration for a task.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The configuration. It names the task.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The stored configuration, with its ID.</returns>
    /// <exception cref="A2AException">The task does not exist, or the webhook URL is refused.</exception>
    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct);

    /// <summary>Gets one push notification configuration of a task.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The task ID and the configuration ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="A2AException">The task or the configuration does not exist.</exception>
    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct);

    /// <summary>Lists the push notification configurations of a task, one page at a time.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The task ID and the page.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of configurations.</returns>
    /// <exception cref="A2AException">The task does not exist, or the page is not valid.</exception>
    Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct);

    /// <summary>Deletes a push notification configuration of a task.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The task ID and the configuration ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the configuration is gone.</returns>
    /// <exception cref="A2AException">The task does not exist.</exception>
    Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct);

    /// <summary>Gets the extended agent card of the agent in <paramref name="call"/>.</summary>
    /// <param name="call">The caller and the transport details.</param>
    /// <param name="request">The request. It only carries an optional tenant.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The extended agent card.</returns>
    /// <exception cref="A2AException">The agent has no extended card (<see cref="A2AErrorCode.ExtendedAgentCardNotConfigured"/>).</exception>
    Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct);
}
