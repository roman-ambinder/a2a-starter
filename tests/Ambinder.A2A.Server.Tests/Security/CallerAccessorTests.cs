using System.Collections.Concurrent;
using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server.Tests.Security;

public class CallerAccessorTests
{
    public readonly record struct Seen(A2ACaller? Accessor, A2ACaller Context);

    /// <summary>What the probe agent saw, per task id, and the gates it waits on.</summary>
    public sealed class Probe
    {
        public ConcurrentDictionary<string, Seen> Executed { get; } = new();
        public ConcurrentDictionary<string, Seen> Canceled { get; } = new();
        public ConcurrentDictionary<string, Signal> Entered { get; } = new();
        public Gate ExecuteGate { get; } = new();
        public Signal Reached { get; } = new();
        public int Expected { get; set; } = 1;
        private int _entered;

        public void Enter() { if (Interlocked.Increment(ref _entered) >= Expected) Reached.Set(); }
    }

    /// <summary>An agent that reads the accessor through constructor injection, like a deep service would.</summary>
    public sealed class ProbeAgent(IA2ACallerAccessor accessor, Probe probe) : IAgentHandler
    {
        public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            probe.Enter();
            await probe.ExecuteGate.WaitAsync(ct);
            probe.Executed[context.TaskId] = new Seen(accessor.Current, context.Caller);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        }

        public async ValueTask CancelAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            probe.Canceled[context.TaskId] = new Seen(accessor.Current, context.Caller);
            await sink.UpdateStatusAsync(TaskState.Canceled, null, ct);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public Probe Probe { get; } = new();
        public IA2ACallerAccessor Accessor { get; }
        public Ambinder.A2A.Server.Tasks.TaskManager Manager { get; }

        public Rig()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Probe);
            services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200")
                .AddAgent<ProbeAgent>(TestHost.AgentName, _ => TestHost.DefaultCard);
            _provider = services.BuildServiceProvider();
            Accessor = _provider.GetRequiredService<IA2ACallerAccessor>();
            Manager = _provider.GetRequiredService<Ambinder.A2A.Server.Tasks.TaskManager>();
        }

        public Task<SendMessageResponse> StartAsync(string owner)
            => Manager.SendMessageAsync(TestHost.Call(owner), TestHost.Text("go", returnImmediately: true), CancellationToken.None).WaitAsync(TestHost.Timeout);

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    private static async Task<T> Eventually<T>(Func<T?> read) where T : struct
    {
        // The run finishes on its own; poll the observable result without sleeping a fixed time.
        var deadline = DateTime.UtcNow + TestHost.Timeout;
        while (read() is not { } value)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The agent did not record the caller in time.");
            await Task.Yield();
        }
        return read()!.Value;
    }

    [Fact]
    public async Task Current_equals_the_context_caller_inside_the_agent()
    {
        await using var rig = new Rig();
        rig.Probe.ExecuteGate.Open();

        var task = (await rig.StartAsync("alice")).Task!;
        var seen = await Eventually(() => rig.Probe.Executed.TryGetValue(task.Id, out var v) ? v : (Seen?)null);

        Assert.NotNull(seen.Accessor);
        Assert.Same(seen.Context, seen.Accessor);
        Assert.Equal("alice", seen.Accessor!.Owner);
    }

    [Fact]
    public async Task Current_is_still_set_after_the_request_has_ended()
    {
        await using var rig = new Rig();

        var response = await rig.StartAsync("alice"); // returnImmediately: the "HTTP request" is over here
        await rig.Probe.Reached.WaitAsync();          // the agent is running and blocked on the gate
        Assert.False(rig.Probe.Executed.ContainsKey(response.Task!.Id));
        rig.Probe.ExecuteGate.Open();
        var seen = await Eventually(() => rig.Probe.Executed.TryGetValue(response.Task!.Id, out var v) ? v : (Seen?)null);

        Assert.Equal("alice", seen.Accessor!.Owner);
        Assert.Same(seen.Context, seen.Accessor);
    }

    [Fact]
    public async Task Current_is_set_in_the_cancel_path()
    {
        await using var rig = new Rig();
        var task = (await rig.StartAsync("alice")).Task!;
        await rig.Probe.Reached.WaitAsync();

        await rig.Manager.CancelTaskAsync(TestHost.Call("alice"), new CancelTaskRequest { Id = task.Id }, TestContext.Current.CancellationToken).WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken);

        var seen = rig.Probe.Canceled[task.Id];
        Assert.Equal("alice", seen.Accessor!.Owner);
        Assert.Same(seen.Context, seen.Accessor);
    }

    [Fact]
    public async Task Current_is_null_outside_a_run_before_and_after()
    {
        await using var rig = new Rig();
        Assert.Null(rig.Accessor.Current);
        rig.Probe.ExecuteGate.Open();

        var task = (await rig.StartAsync("alice")).Task!;
        await Eventually(() => rig.Probe.Executed.TryGetValue(task.Id, out var v) ? v : (Seen?)null);

        Assert.Null(rig.Accessor.Current);
    }

    [Fact]
    public async Task Concurrent_runs_of_different_callers_each_see_their_own_caller()
    {
        await using var rig = new Rig();
        rig.Probe.Expected = 2;

        var a = (await rig.StartAsync("alice")).Task!;
        var b = (await rig.StartAsync("bob")).Task!;
        await rig.Probe.Reached.WaitAsync(); // both runs are inside the agent at the same time
        rig.Probe.ExecuteGate.Open();
        var seenA = await Eventually(() => rig.Probe.Executed.TryGetValue(a.Id, out var v) ? v : (Seen?)null);
        var seenB = await Eventually(() => rig.Probe.Executed.TryGetValue(b.Id, out var v) ? v : (Seen?)null);

        Assert.Equal("alice", seenA.Accessor!.Owner);
        Assert.Equal("bob", seenB.Accessor!.Owner);
    }

    private static A2ACaller CallerNamed(string owner)
        => new() { Owner = owner, Kind = CallerKind.Other, Principal = new ClaimsPrincipal(new ClaimsIdentity()) };

    [Fact]
    public void Set_exposes_the_caller_and_dispose_restores_the_previous_value()
    {
        var accessor = new A2ACallerAccessor();
        var outer = CallerNamed("outer");
        var inner = CallerNamed("inner");
        Assert.Null(accessor.Current);

        using (A2ACallerAccessor.Set(outer))
        {
            Assert.Same(outer, accessor.Current);
            using (A2ACallerAccessor.Set(inner))
            {
                Assert.Same(inner, accessor.Current);
            }
            Assert.Same(outer, accessor.Current); // nested dispose restores the outer caller
        }

        Assert.Null(accessor.Current); // and the outer dispose restores "no caller"
    }
}
