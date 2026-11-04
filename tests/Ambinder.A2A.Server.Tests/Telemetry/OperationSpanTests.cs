using System.Diagnostics;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Telemetry;
using Ambinder.A2A.Tests.Shared;

namespace Ambinder.A2A.Server.Tests.Telemetry;

[Collection("Telemetry")]
public sealed class OperationSpanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (TestHost Host, TracingRequestHandler Handler) Create()
    {
        var host = new TestHost();
        return (host, new TracingRequestHandler(host.Manager));
    }

    [Fact]
    public async Task SendMessage_creates_operation_span_with_a2a_attributes()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (_, handler) = Create();

        var response = await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("hi"), Ct);

        var span = collector.Single("a2a.server SendMessage");
        var task = Assert.IsType<AgentTask>(response.Task);
        Assert.Equal("SendMessage", span.GetTagItem(A2ATelemetryNames.Method));
        Assert.Equal("test", span.GetTagItem(A2ATelemetryNames.Agent));
        Assert.Equal(task.Id, span.GetTagItem(A2ATelemetryNames.TaskId));
        Assert.Equal(task.ContextId, span.GetTagItem(A2ATelemetryNames.ContextId));
        Assert.Equal(ActivityKind.Internal, span.Kind);
        Assert.Equal(collector.Root.Id, span.ParentId);
    }

    [Fact]
    public async Task Unknown_task_sets_error_status_and_error_type()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (_, handler) = Create();

        await Assert.ThrowsAsync<A2AException>(() =>
            handler.GetTaskAsync(TestHost.Call(), new GetTaskRequest { Id = "missing" }, Ct));

        var span = collector.Single("a2a.server GetTask");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(nameof(A2AErrorCode.TaskNotFound), span.GetTagItem(A2ATelemetryNames.ErrorType));
    }

    [Fact]
    public async Task Every_handler_method_gets_a_span()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (_, handler) = Create();
        var call = TestHost.Call();

        async Task Try(Func<Task> action)
        {
            try { await action(); }
            catch (A2AException) { /* errors are fine, the span is what counts */ }
        }
        async Task Drain(IAsyncEnumerable<StreamResponse> stream)
        {
            await foreach (var _ in stream.WithCancellation(Ct)) { }
        }

        await Try(() => handler.SendMessageAsync(call, TestHost.Text("a"), Ct));
        await Try(() => Drain(handler.SendStreamingMessageAsync(call, TestHost.Text("b"), Ct)));
        await Try(() => handler.GetTaskAsync(call, new GetTaskRequest { Id = "x" }, Ct));
        await Try(() => handler.ListTasksAsync(call, new ListTasksRequest(), Ct));
        await Try(() => handler.CancelTaskAsync(call, new CancelTaskRequest { Id = "x" }, Ct));
        await Try(() => Drain(handler.SubscribeToTaskAsync(call, new SubscribeToTaskRequest { Id = "x" }, Ct)));
        await Try(() => handler.CreateTaskPushNotificationConfigAsync(call, new TaskPushNotificationConfig { TaskId = "x" }, Ct));
        await Try(() => handler.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = "x", Id = "y" }, Ct));
        await Try(() => handler.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = "x" }, Ct));
        await Try(() => handler.DeleteTaskPushNotificationConfigAsync(call, new DeleteTaskPushNotificationConfigRequest { TaskId = "x", Id = "y" }, Ct));
        await Try(() => handler.GetExtendedAgentCardAsync(call, new GetExtendedAgentCardRequest(), Ct));

        string[] methods =
        [
            "SendMessage", "SendStreamingMessage", "GetTask", "ListTasks", "CancelTask", "SubscribeToTask",
            "CreateTaskPushNotificationConfig", "GetTaskPushNotificationConfig", "ListTaskPushNotificationConfigs",
            "DeleteTaskPushNotificationConfig", "GetExtendedAgentCard",
        ];
        foreach (var method in methods) collector.Single("a2a.server " + method);
    }

    [Fact]
    public async Task Stream_cancelled_mid_way_returns_active_streams_to_zero_without_error()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        using var metrics = new MetricCollector(A2ATelemetryNames.ServerMeter);
        var (host, handler) = Create();
        var working = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            working.Set();
            await Task.Delay(Timeout.Infinite, ct);
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in handler.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), cts.Token).WithCancellation(cts.Token))
                cts.Cancel();
        });

        var span = collector.Single("a2a.server SendStreamingMessage");
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
        var active = metrics.Measurements.Where(m => m.Instrument == A2ATelemetryNames.StreamsActive && (string?)m.Tags[A2ATelemetryNames.Agent] == "test");
        Assert.Equal(0, active.Sum(m => m.Value));
        Assert.NotEmpty(active);
    }

    [Fact]
    public async Task Tenant_attribute_only_when_set()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (_, handler) = Create();

        await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("a"), Ct);
        var without = collector.Single("a2a.server SendMessage");
        Assert.Null(without.GetTagItem(A2ATelemetryNames.Tenant));

        using var collector2 = new ActivityCollector(A2ATelemetryNames.ServerSource);
        await handler.SendMessageAsync(TestHost.Call() with { Tenant = "acme" }, TestHost.Text("b"), Ct);
        Assert.Equal("acme", collector2.Single("a2a.server SendMessage").GetTagItem(A2ATelemetryNames.Tenant));
    }
}
