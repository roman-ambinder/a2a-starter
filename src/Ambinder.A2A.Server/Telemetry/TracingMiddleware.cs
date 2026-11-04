using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Telemetry;

/// <summary>
/// Built-in middleware in slot 100: records one span per operation (<c>a2a.server &lt;Operation&gt;</c>).
/// Streaming spans stay open for the whole enumeration. Records IDs and error types only, never message content.
/// It also runs for forwarded calls.
/// </summary>
internal sealed class TracingMiddleware : IA2AMiddleware
{
    public async ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
    {
        var method = context.Operation.ToString();
        var call = context.Call;
        var (taskId, contextId) = RequestIds(context.Request);
        var span = Start(method, call, taskId, contextId);

        if (context.IsStreaming)
        {
            A2AOperationResult streaming;
            try
            {
                streaming = await next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Fail(span, ex);
                span?.Dispose();
                throw;
            }
            return streaming.Events is { } events
                ? A2AOperationResult.FromEvents(WrapAsync(span, call.AgentName, events))
                : streaming;
        }

        using (span)
        {
            try
            {
                var result = await next(context).ConfigureAwait(false);
                if (span is not null)
                {
                    switch (result.Value)
                    {
                        case SendMessageResponse r:
                            SetIds(span, r.Task?.Id ?? r.Message?.TaskId, r.Task?.ContextId ?? r.Message?.ContextId);
                            break;
                        case AgentTask t:
                            SetIds(span, t.Id, t.ContextId);
                            break;
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Fail(span, ex);
                throw;
            }
        }
    }

    private static async IAsyncEnumerable<StreamResponse> WrapAsync(Activity? span, string agentName, IAsyncEnumerable<StreamResponse> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var _ = span;
        // The span was started in the middleware's own flow, and Activity.Current does not flow back out of it. The stream starts the agent
        // run on its first MoveNextAsync, so make the span current again there. The agent run span is then a child of the stream span.
        if (span is not null) Activity.Current = span;
        var agentTag = new KeyValuePair<string, object?>(A2ATelemetryNames.Agent, agentName);
        ServerInstruments.StreamsActive.Add(1, agentTag);
        try
        {
            // A manual loop: yield cannot sit inside a try block that has a catch.
            var events = source.GetAsyncEnumerator(ct);
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

    private static (string? TaskId, string? ContextId) RequestIds(object request) => request switch
    {
        SendMessageRequest r => (r.Message.TaskId, r.Message.ContextId),
        GetTaskRequest r => (r.Id, null),
        ListTasksRequest r => (null, r.ContextId),
        CancelTaskRequest r => (r.Id, null),
        SubscribeToTaskRequest r => (r.Id, null),
        TaskPushNotificationConfig r => (r.TaskId, null),
        GetTaskPushNotificationConfigRequest r => (r.TaskId, null),
        ListTaskPushNotificationConfigsRequest r => (r.TaskId, null),
        DeleteTaskPushNotificationConfigRequest r => (r.TaskId, null),
        _ => (null, null),
    };

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

    /// <summary>Marks the span failed. A cancelled call is not an error.</summary>
    private static void Fail(Activity? span, Exception ex)
    {
        if (span is null || ex is OperationCanceledException) return;
        span.SetStatus(ActivityStatusCode.Error);
        span.SetTag(A2ATelemetryNames.ErrorType, ex is A2AException a2a ? a2a.Code.ToString() : ex.GetType().FullName);
    }
}
