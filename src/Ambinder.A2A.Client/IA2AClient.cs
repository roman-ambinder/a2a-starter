using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>
/// An A2A client over one protocol binding. The 11 operations mirror the server-side request handler.
/// Protocol errors throw <see cref="A2AException"/>; HTTP 401/403 (gRPC UNAUTHENTICATED / PERMISSION_DENIED)
/// throw <see cref="A2AAuthException"/>. A response that cannot be parsed or breaks a oneof rule throws
/// <see cref="A2AException"/> with <see cref="A2AErrorCode.InvalidAgentResponse"/>.
/// </summary>
/// <remarks>
/// A client is bound to one agent interface. It is safe to call from many threads at the same time. Dispose it when you are done;
/// it disposes an <see cref="System.Net.Http.HttpClient"/> only when it created that client itself.
/// Every operation can also throw <see cref="OperationCanceledException"/> when <c>ct</c> is canceled, and
/// <see cref="HttpRequestException"/> when the network fails.
/// </remarks>
public interface IA2AClient : IAsyncDisposable
{
    /// <summary>Sends a message to the agent and waits for the reply.</summary>
    /// <param name="request">The message and its options. The message needs a <see cref="Message.MessageId"/> and at least one part.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task the agent created, or the agent's direct reply message.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.InvalidParams"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    /// <exception cref="ArgumentException">The request names a tenant that differs from the tenant of the chosen interface.</exception>
    /// <example>
    /// <code>
    /// // requires: IA2AClient client;
    /// var request = new SendMessageRequest
    /// {
    ///     Message = new Message
    ///     {
    ///         MessageId = Guid.NewGuid().ToString("N"),
    ///         Role = Role.User,
    ///         Parts = [Part.FromText("What is the weather in Oslo?")],
    ///     },
    /// };
    /// SendMessageResponse response = await client.SendMessageAsync(request);
    /// Console.WriteLine(response.Task?.Status.State ?? TaskState.Completed);
    /// </code>
    /// </example>
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default);

    /// <summary>Sends a message and streams the events the agent produces.</summary>
    /// <param name="request">The message and its options.</param>
    /// <param name="ct">A token that cancels the stream.</param>
    /// <returns>The events in the order the agent sent them: the task, status updates, artifact updates and messages. The sequence ends when the task reaches a final or paused state.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.UnsupportedOperation"/> when the agent cannot stream.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    /// <example>
    /// <code>
    /// // requires: IA2AClient client; SendMessageRequest request;
    /// await foreach (StreamResponse evt in client.SendStreamingMessageAsync(request))
    /// {
    ///     if (evt.StatusUpdate is { } update)
    ///     {
    ///         Console.WriteLine(update.Status.State);
    ///     }
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default);

    /// <summary>Gets a task by its ID.</summary>
    /// <param name="request">The task ID and the history length to return.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotFound"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default);

    /// <summary>Lists tasks, one page at a time.</summary>
    /// <param name="request">The filters and the page to read.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of tasks. Use <see cref="ListTasksResponse.NextPageToken"/> to read the next page.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default);

    /// <summary>Asks the agent to cancel a task.</summary>
    /// <param name="request">The ID of the task to cancel.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The task after the cancel request.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotCancelable"/> or <see cref="A2AErrorCode.TaskNotFound"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default);

    /// <summary>Follows a running task and streams its events.</summary>
    /// <param name="request">The ID of the task to follow.</param>
    /// <param name="ct">A token that cancels the stream.</param>
    /// <returns>The events of the task. The first event is the current task.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotFound"/> or <see cref="A2AErrorCode.UnsupportedOperation"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default);

    /// <summary>Creates a push notification (webhook) configuration for a task.</summary>
    /// <param name="request">The configuration. Set <see cref="TaskPushNotificationConfig.TaskId"/> and <see cref="TaskPushNotificationConfig.Url"/>.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The stored configuration, with the ID the agent gave it.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.PushNotificationNotSupported"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default);

    /// <summary>Gets one push notification configuration of a task.</summary>
    /// <param name="request">The task ID and the configuration ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotFound"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default);

    /// <summary>Lists the push notification configurations of a task, one page at a time.</summary>
    /// <param name="request">The task ID and the page to read.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of configurations.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotFound"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default);

    /// <summary>Deletes a push notification configuration of a task.</summary>
    /// <param name="request">The task ID and the configuration ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the agent has deleted the configuration.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.TaskNotFound"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default);

    /// <summary>Gets the extended agent card, which only authenticated callers can read.</summary>
    /// <param name="request">The request. It only carries an optional tenant.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The extended agent card.</returns>
    /// <exception cref="A2AException">The agent returned an A2A error, for example <see cref="A2AErrorCode.ExtendedAgentCardNotConfigured"/>.</exception>
    /// <exception cref="A2AAuthException">The agent answered with HTTP 401 or 403.</exception>
    Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default);
}
