using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Telemetry;

/// <summary>
/// Wraps an <see cref="IA2ARequestHandler"/> and records one span per operation (<c>a2a.server &lt;Method&gt;</c>).
/// Streaming spans stay open for the whole enumeration. Records IDs and error types only, never message content.
/// </summary>
internal sealed class TracingRequestHandler(IA2ARequestHandler inner) : IA2ARequestHandler
{
    public Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => RunAsync("SendMessage", call, request.Message.TaskId, request.Message.ContextId,
            () => inner.SendMessageAsync(call, request, ct),
            (span, r) => SetIds(span, r.Task?.Id ?? r.Message?.TaskId, r.Task?.ContextId ?? r.Message?.ContextId));

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => StreamAsync("SendStreamingMessage", call, request.Message.TaskId, request.Message.ContextId,
            () => inner.SendStreamingMessageAsync(call, request, ct), ct);

    public Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct)
        => RunAsync("GetTask", call, request.Id, null, () => inner.GetTaskAsync(call, request, ct), SetTask);

    public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
        => RunAsync("ListTasks", call, null, request.ContextId, () => inner.ListTasksAsync(call, request, ct));

    public Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct)
        => RunAsync("CancelTask", call, request.Id, null, () => inner.CancelTaskAsync(call, request, ct), SetTask);

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct)
        => StreamAsync("SubscribeToTask", call, request.Id, null, () => inner.SubscribeToTaskAsync(call, request, ct), ct);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct)
        => RunAsync("CreateTaskPushNotificationConfig", call, request.TaskId, null, () => inner.CreateTaskPushNotificationConfigAsync(call, request, ct));

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct)
        => RunAsync("GetTaskPushNotificationConfig", call, request.TaskId, null, () => inner.GetTaskPushNotificationConfigAsync(call, request, ct));

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct)
        => RunAsync("ListTaskPushNotificationConfigs", call, request.TaskId, null, () => inner.ListTaskPushNotificationConfigsAsync(call, request, ct));

    public Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct)
        => RunAsync("DeleteTaskPushNotificationConfig", call, request.TaskId, null, async () =>
        {
            await inner.DeleteTaskPushNotificationConfigAsync(call, request, ct).ConfigureAwait(false);
            return true;
        });

    public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct)
        => RunAsync("GetExtendedAgentCard", call, null, null, () => inner.GetExtendedAgentCardAsync(call, request, ct));

    private static async Task<T> RunAsync<T>(string method, A2ACallContext call, string? taskId, string? contextId,
        Func<Task<T>> operation, Action<Activity, T>? onResult = null)
    {
        using var span = Start(method, call, taskId, contextId);
        try
        {
            var result = await operation().ConfigureAwait(false);
            if (span is not null) onResult?.Invoke(span, result);
            return result;
        }
        catch (Exception ex)
        {
            Fail(span, ex);
            throw;
        }
    }

    private static async IAsyncEnumerable<StreamResponse> StreamAsync(string method, A2ACallContext call, string? taskId, string? contextId,
        Func<IAsyncEnumerable<StreamResponse>> operation, [EnumeratorCancellation] CancellationToken ct)
    {
        using var span = Start(method, call, taskId, contextId);
        var agentTag = new KeyValuePair<string, object?>(A2ATelemetryNames.Agent, call.AgentName);
        ServerInstruments.StreamsActive.Add(1, agentTag);
        try
        {
            // A manual loop: yield cannot sit inside a try block that has a catch.
            var events = operation().GetAsyncEnumerator(ct);
            await using (events.ConfigureAwait(false))
            {
                while (true)
                {
                    StreamResponse item;
                    try
                    {
                        if (!await events.MoveNextAsync().ConfigureAwait(false)) break;
                        item = events.Current;
                    }
                    catch (Exception ex)
                    {
                        Fail(span, ex);
                        throw;
                    }
                    if (span is not null)
                    {
                        var task = item.Task;
                        SetIds(span, task?.Id ?? item.StatusUpdate?.TaskId ?? item.ArtifactUpdate?.TaskId ?? item.Message?.TaskId,
                            task?.ContextId ?? item.StatusUpdate?.ContextId ?? item.ArtifactUpdate?.ContextId ?? item.Message?.ContextId);
                    }
                    yield return item;
                }
            }
        }
        finally
        {
            ServerInstruments.StreamsActive.Add(-1, agentTag);
        }
    }

    private static Activity? Start(string method, A2ACallContext call, string? taskId, string? contextId)
    {
        var span = ServerInstruments.Source.StartActivity("a2a.server " + method, ActivityKind.Internal);
        if (span is null) return null;
        span.SetTag(A2ATelemetryNames.Method, method);
        span.SetTag(A2ATelemetryNames.Agent, call.AgentName);
        if (!string.IsNullOrEmpty(call.Transport)) span.SetTag(A2ATelemetryNames.Transport, call.Transport);
        if (!string.IsNullOrEmpty(call.Tenant)) span.SetTag(A2ATelemetryNames.Tenant, call.Tenant);
        SetIds(span, taskId, contextId);
        return span;
    }

    private static void SetIds(Activity span, string? taskId, string? contextId)
    {
        if (!string.IsNullOrEmpty(taskId)) span.SetTag(A2ATelemetryNames.TaskId, taskId);
        if (!string.IsNullOrEmpty(contextId)) span.SetTag(A2ATelemetryNames.ContextId, contextId);
    }

    private static void SetTask(Activity span, AgentTask task) => SetIds(span, task.Id, task.ContextId);

    /// <summary>Marks the span failed. A cancelled call is not an error.</summary>
    private static void Fail(Activity? span, Exception ex)
    {
        if (span is null || ex is OperationCanceledException) return;
        span.SetStatus(ActivityStatusCode.Error);
        span.SetTag(A2ATelemetryNames.ErrorType, ex is A2AException a2a ? a2a.Code.ToString() : ex.GetType().FullName);
    }
}
