using System.Diagnostics;
using System.Runtime.CompilerServices;
using A2A.Protocol;

namespace A2A.Client;

/// <summary>
/// Wraps an <see cref="IA2AClient"/> and records one span per operation (<c>a2a.client &lt;Method&gt;</c>).
/// The HTTP or gRPC call below it joins the span's trace, so a server span becomes its descendant.
/// Streaming spans stay open for the whole enumeration. Records IDs and error types only, never message content.
/// <paramref name="agent"/> is the agent card's name (<c>a2a.agent</c>), <paramref name="transport"/> the <c>a2a.transport</c> value,
/// <paramref name="tenant"/> the card interface's tenant.
/// </summary>
public sealed class TracingA2AClient(IA2AClient inner, string agent, string transport, string? tenant) : IA2AClient
{
    private static readonly ActivitySource Source = new(A2ATelemetryNames.ClientSource);

    /// <summary>The wrapped transport client.</summary>
    public IA2AClient Inner { get; } = inner;

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => RunAsync("SendMessage", request.Tenant, request.Message.TaskId, request.Message.ContextId,
            () => Inner.SendMessageAsync(request, ct),
            (span, r) => SetIds(span, r.Task?.Id ?? r.Message?.TaskId, r.Task?.ContextId ?? r.Message?.ContextId));

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => StreamAsync("SendStreamingMessage", request.Tenant, request.Message.TaskId, request.Message.ContextId,
            () => Inner.SendStreamingMessageAsync(request, ct), ct);

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default)
        => RunAsync("GetTask", request.Tenant, request.Id, null, () => Inner.GetTaskAsync(request, ct), SetTask);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default)
        => RunAsync("ListTasks", request.Tenant, null, request.ContextId, () => Inner.ListTasksAsync(request, ct));

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default)
        => RunAsync("CancelTask", request.Tenant, request.Id, null, () => Inner.CancelTaskAsync(request, ct), SetTask);

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default)
        => StreamAsync("SubscribeToTask", request.Tenant, request.Id, null, () => Inner.SubscribeToTaskAsync(request, ct), ct);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default)
        => RunAsync("CreateTaskPushNotificationConfig", request.Tenant, request.TaskId, null, () => Inner.CreateTaskPushNotificationConfigAsync(request, ct));

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => RunAsync("GetTaskPushNotificationConfig", request.Tenant, request.TaskId, null, () => Inner.GetTaskPushNotificationConfigAsync(request, ct));

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default)
        => RunAsync("ListTaskPushNotificationConfigs", request.Tenant, request.TaskId, null, () => Inner.ListTaskPushNotificationConfigsAsync(request, ct));

    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => RunAsync("DeleteTaskPushNotificationConfig", request.Tenant, request.TaskId, null, async () =>
        {
            await Inner.DeleteTaskPushNotificationConfigAsync(request, ct).ConfigureAwait(false);
            return true;
        });

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default)
        => RunAsync("GetExtendedAgentCard", request.Tenant, null, null, () => Inner.GetExtendedAgentCardAsync(request, ct));

    public ValueTask DisposeAsync() => Inner.DisposeAsync();

    private async Task<T> RunAsync<T>(string method, string? requestTenant, string? taskId, string? contextId,
        Func<Task<T>> operation, Action<Activity, T>? onResult = null)
    {
        using var span = Start(method, requestTenant, taskId, contextId);
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

    private async IAsyncEnumerable<StreamResponse> StreamAsync(string method, string? requestTenant, string? taskId, string? contextId,
        Func<IAsyncEnumerable<StreamResponse>> operation, [EnumeratorCancellation] CancellationToken ct)
    {
        using var span = Start(method, requestTenant, taskId, contextId);
        // A manual loop: yield cannot sit inside a try block that has a catch.
        // Creating the enumerator is inside the try too, so a failure there is recorded.
        IAsyncEnumerator<StreamResponse>? events = null;
        try
        {
            while (true)
            {
                StreamResponse item;
                try
                {
                    events ??= operation().GetAsyncEnumerator(ct);
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
        finally
        {
            if (events is not null) await events.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Activity? Start(string method, string? requestTenant, string? taskId, string? contextId)
    {
        var span = Source.StartActivity("a2a.client " + method, ActivityKind.Internal);
        if (span is null) return null;
        span.SetTag(A2ATelemetryNames.Method, method);
        if (!string.IsNullOrEmpty(agent)) span.SetTag(A2ATelemetryNames.Agent, agent);
        span.SetTag(A2ATelemetryNames.Transport, transport);
        // A request without a tenant uses the card interface's tenant.
        var effectiveTenant = string.IsNullOrEmpty(requestTenant) ? tenant : requestTenant;
        if (!string.IsNullOrEmpty(effectiveTenant)) span.SetTag(A2ATelemetryNames.Tenant, effectiveTenant);
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
