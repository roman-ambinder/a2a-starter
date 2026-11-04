using System.Diagnostics;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Telemetry;
using Ambinder.A2A.Tests.Shared;

namespace Ambinder.A2A.Server.Tests.Telemetry;

[Collection("Telemetry")]
public sealed class TaskTelemetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (TestHost Host, IA2ARequestHandler Handler) Create()
    {
        var host = new TestHost();
        return (host, host.TracedHandler());
    }

    private static string[] StateEvents(Activity span)
        => [.. span.Events.Where(e => e.Name == A2ATelemetryNames.TaskStateEvent)
            .Select(e => (string)e.Tags.Single(t => t.Key == A2ATelemetryNames.TaskState).Value!)];

    [Fact]
    public async Task Agent_run_span_is_child_of_send_span_and_outlives_it()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (host, handler) = Create();
        var gate = new Gate();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct); // the send returns on the first state
            await gate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };

        var response = await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go", returnImmediately: true), Ct);
        var send = collector.Single("a2a.server SendMessage");
        Assert.DoesNotContain(collector.Stopped, a => a.OperationName == "a2a.agent.run");
        gate.Open();
        var run = await collector.WaitForAsync("a2a.agent.run", TestHost.Timeout);

        Assert.Equal(send.Id, run.ParentId);
        Assert.Equal(send.TraceId, run.TraceId);
        Assert.Equal(response.Task!.Id, run.GetTagItem(A2ATelemetryNames.TaskId));
        Assert.Equal("test", run.GetTagItem(A2ATelemetryNames.Agent));
        Assert.True(run.StartTimeUtc + run.Duration >= send.StartTimeUtc + send.Duration);
    }

    [Fact]
    public async Task Streaming_send_agent_run_span_is_child_of_stream_span()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (_, handler) = Create();

        await TestHost.CollectAsync(handler.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct));

        var stream = collector.Single("a2a.server SendStreamingMessage");
        var run = await collector.WaitForAsync("a2a.agent.run", TestHost.Timeout);
        Assert.Equal(stream.Id, run.ParentId);
        Assert.Equal(stream.TraceId, run.TraceId);
    }

    [Fact]
    public async Task State_changes_are_events_on_agent_run_span()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (host, handler) = Create();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };

        await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct);

        var run = await collector.WaitForAsync("a2a.agent.run", TestHost.Timeout);
        // A new task is created on the agent's first write, so its "submitted" event lands on the run span too.
        Assert.Equal(["submitted", "working", "completed"], StateEvents(run));
    }

    [Fact]
    public async Task Cancel_state_event_lands_on_cancel_span()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (host, handler) = Create();
        var working = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            working.Set();
            await Task.Delay(Timeout.Infinite, ct);
        };
        var response = await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go", returnImmediately: true), Ct);
        await working.WaitAsync();

        await handler.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = response.Task!.Id }, Ct);

        var cancel = collector.Single("a2a.server CancelTask");
        Assert.Contains("canceled", StateEvents(cancel));
    }

    [Fact]
    public async Task Completed_counter_counts_terminal_states_once()
    {
        using var metrics = new MetricCollector(A2ATelemetryNames.ServerMeter);
        var (host, handler) = Create();
        foreach (var state in new[] { TaskState.Completed, TaskState.Failed, TaskState.Canceled, TaskState.Rejected })
        {
            host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(state, null, ct);
            await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct);
        }

        var done = metrics.Measurements.Where(m => m.Instrument == A2ATelemetryNames.TasksCompleted).ToList();
        Assert.Equal(4, done.Count);
        Assert.All(done, m =>
        {
            Assert.Equal(1, m.Value);
            Assert.Equal("test", m.Tags[A2ATelemetryNames.Agent]);
        });
        Assert.Equal(["canceled", "completed", "failed", "rejected"],
            done.Select(m => (string)m.Tags[A2ATelemetryNames.TaskState]!).Order().ToArray());
    }

    [Fact]
    public async Task Input_required_then_completed_counts_once()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        using var metrics = new MetricCollector(A2ATelemetryNames.ServerMeter);
        var (host, handler) = Create();
        var calls = 0;
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(
            Interlocked.Increment(ref calls) == 1 ? TaskState.InputRequired : TaskState.Completed, null, ct);

        var first = await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct);
        await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("more", first.Task!.Id), Ct);

        var done = Assert.Single(metrics.Measurements, m => m.Instrument == A2ATelemetryNames.TasksCompleted);
        Assert.Equal("completed", done.Tags[A2ATelemetryNames.TaskState]);
        var events = collector.Stopped.Where(a => a.OperationName == "a2a.agent.run").SelectMany(StateEvents).ToList();
        Assert.Contains("input-required", events);
        Assert.Contains("completed", events);
    }

    [Fact]
    public async Task Agent_exception_marks_run_span_error()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var (host, handler) = Create();
        host.Scripts.Execute = (_, _, _) => throw new InvalidOperationException("boom");

        await handler.SendMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct);

        var run = await collector.WaitForAsync("a2a.agent.run", TestHost.Timeout);
        Assert.Equal(ActivityStatusCode.Error, run.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, run.GetTagItem(A2ATelemetryNames.ErrorType));
    }

    [Fact]
    public async Task Throwing_metric_listener_does_not_stop_the_publish()
    {
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == A2ATelemetryNames.ServerMeter && instrument.Name == A2ATelemetryNames.TasksCompleted)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("listener failed"));
        listener.Start();
        var (_, handler) = Create();

        // The completed state is counted between save and publish; the stream still gets it.
        var events = await TestHost.CollectAsync(handler.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), Ct))
            .WaitAsync(TestHost.Timeout, Ct);

        Assert.Equal(TaskState.Completed, events[^1].StatusUpdate?.Status.State);
    }
}
