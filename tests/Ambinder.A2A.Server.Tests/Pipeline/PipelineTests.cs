using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Tasks;
using Ambinder.A2A.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server.Tests.Pipeline;

public sealed class PipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- test doubles ----

    private sealed class Recorder
    {
        private readonly List<string> _names = [];
        private readonly List<A2AOperation> _operations = [];
        private readonly Lock _gate = new();

        public void Add(string name) { lock (_gate) _names.Add(name); }
        public void Add(A2AOperation operation) { lock (_gate) _operations.Add(operation); }
        public string[] Names { get { lock (_gate) return [.. _names]; } }
        public A2AOperation[] Operations { get { lock (_gate) return [.. _operations]; } }
        public void Clear() { lock (_gate) _names.Clear(); }
    }

    private sealed class CountingStore : ITaskStore
    {
        private readonly InMemoryTaskStore _inner = new();
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return _inner.GetAsync(agentName, taskId, ct);
        }

        public ValueTask SaveAsync(StoredTask task, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return _inner.SaveAsync(task, ct);
        }

        public ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return _inner.ListAsync(agentName, owner, request, ct);
        }
    }

    private sealed class WorkingThenCompletedAgent : IAgentHandler
    {
        public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        }
    }

    private sealed class G1(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add("G1");
            return next(context);
        }
    }

    private sealed class G2(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add("G2");
            return next(context);
        }
    }

    private sealed class A1(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add("A1");
            return next(context);
        }
    }

    private sealed class RecordOperations(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add(context.Operation);
            return next(context);
        }
    }

    private sealed class RejectGetTask : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
            => context.Operation == A2AOperation.GetTask
                ? throw new A2AException(A2AErrorCode.InvalidParams, "nope")
                : next(context);
    }

    private sealed class CountStream(Recorder recorder) : IA2AMiddleware
    {
        public async ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            var result = await next(context);
            return result.Events is { } events ? A2AOperationResult.FromEvents(Count(events, recorder)) : result;
        }

        private static async IAsyncEnumerable<StreamResponse> Count(IAsyncEnumerable<StreamResponse> events, Recorder recorder)
        {
            await foreach (var e in events)
            {
                recorder.Add("event");
                yield return e;
            }
        }
    }

    private sealed class ThrowBeforeNext : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
            => throw new A2AException(A2AErrorCode.InvalidParams, "too early");
    }

    private sealed class ReplaceRequest : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            context.Request = new GetTaskRequest { Id = "x" };
            return next(context);
        }
    }

    // ---- helpers ----

    private static AgentCard Card(string name) => new()
    {
        Name = name,
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true, ExtendedAgentCard = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
    };

    private static Task<A2ATestServer> StartAsync(Action<A2AServerBuilder> agents, CountingStore? store = null)
        => A2ATestServer.StartAsync(agents, services =>
        {
            services.AddSingleton<Recorder>();
            services.Configure<A2AServerOptions>(o => o.Push.AllowPrivateNetworks = true);
            if (store is not null) services.AddSingleton<ITaskStore>(store);
        });

    private static A2ACallContext Call(string agent)
        => new(agent, new ClaimsPrincipal(new ClaimsIdentity()), "alice", null, A2AConstants.ProtocolVersion, []);

    private static SendMessageRequest Send(string text) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString(), Role = Role.User, Parts = [Part.FromText(text)] },
    };

    private static async Task<List<StreamResponse>> CollectAsync(IAsyncEnumerable<StreamResponse> stream)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var list = new List<StreamResponse>();
        await foreach (var e in stream.WithCancellation(cts.Token)) list.Add(e);
        return list;
    }

    // ---- tests ----

    [Fact]
    public async Task RunsGlobalThenPerAgentInRegistrationOrder()
    {
        await using var server = await StartAsync(a => a
            .UseMiddleware<G1>()
            .UseMiddleware<G2>()
            .AddAgent<WorkingThenCompletedAgent>("with", _ => Card("with"), null, null, o => o.UseMiddleware<A1>())
            .AddAgent<WorkingThenCompletedAgent>("without", _ => Card("without")));
        var recorder = server.Services.GetRequiredService<Recorder>();

        await server.Handler.SendMessageAsync(Call("with"), Send("hi"), Ct);
        Assert.Equal(["G1", "G2", "A1"], recorder.Names);

        recorder.Clear();
        await server.Handler.SendMessageAsync(Call("without"), Send("hi"), Ct);
        Assert.Equal(["G1", "G2"], recorder.Names);
    }

    [Fact]
    public async Task SeesAllElevenOperations()
    {
        await using var server = await StartAsync(a => a
            .UseMiddleware<RecordOperations>()
            .AddAgent<WorkingThenCompletedAgent>("a", _ => Card("a"), _ => Card("a")));
        var recorder = server.Services.GetRequiredService<Recorder>();
        var handler = server.Handler;
        var call = Call("a");

        var sent = await handler.SendMessageAsync(call, Send("one"), Ct);
        var taskId = sent.Task!.Id;
        await CollectAsync(handler.SendStreamingMessageAsync(call, Send("two"), Ct));
        await handler.GetTaskAsync(call, new GetTaskRequest { Id = taskId }, Ct);
        await handler.ListTasksAsync(call, new ListTasksRequest(), Ct);
        await handler.CancelTaskAsyncIgnoringRefusal(call, taskId, Ct);
        await handler.SubscribeIgnoringRefusal(call, taskId, Ct);
        var config = await handler.CreateTaskPushNotificationConfigAsync(call,
            new TaskPushNotificationConfig { TaskId = taskId, Url = "http://hooks.test/hook" }, Ct);
        await handler.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = taskId, Id = config.Id! }, Ct);
        await handler.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = taskId }, Ct);
        await handler.DeleteTaskPushNotificationConfigAsync(call, new DeleteTaskPushNotificationConfigRequest { TaskId = taskId, Id = config.Id! }, Ct);
        await handler.GetExtendedAgentCardAsync(call, new GetExtendedAgentCardRequest(), Ct);

        Assert.Equal(Enum.GetValues<A2AOperation>().Order(), recorder.Operations.Distinct().Order());
    }

    [Fact]
    public async Task RejectWithA2AException()
    {
        var store = new CountingStore();
        await using var server = await StartAsync(a => a
            .UseMiddleware<RejectGetTask>()
            .AddAgent<WorkingThenCompletedAgent>("a", _ => Card("a")), store);
        var before = store.Calls;

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            server.Handler.GetTaskAsync(Call("a"), new GetTaskRequest { Id = "t1" }, Ct));

        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
        Assert.Equal("nope", ex.Message);
        Assert.Equal(before, store.Calls); // TaskManager never ran
    }

    [Fact]
    public async Task StreamWrapping()
    {
        await using var server = await StartAsync(a => a
            .UseMiddleware<CountStream>()
            .AddAgent<WorkingThenCompletedAgent>("a", _ => Card("a")));
        var recorder = server.Services.GetRequiredService<Recorder>();

        var events = await CollectAsync(server.Handler.SendStreamingMessageAsync(Call("a"), Send("hi"), Ct));

        Assert.True(events.Count >= 3); // task, working, completed
        Assert.Equal(events.Count, recorder.Names.Count(n => n == "event"));
    }

    [Fact]
    public async Task ThrowBeforeFirstEventSurfacesOnFirstMoveNext()
    {
        await using var server = await StartAsync(a => a
            .UseMiddleware<ThrowBeforeNext>()
            .AddAgent<WorkingThenCompletedAgent>("a", _ => Card("a")));

        var stream = server.Handler.SendStreamingMessageAsync(Call("a"), Send("hi"), Ct); // must not throw here
        await using var enumerator = stream.GetAsyncEnumerator(Ct);
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await enumerator.MoveNextAsync());

        Assert.Equal("too early", ex.Message);
    }

    [Fact]
    public async Task ReplacingRequestWithWrongTypeFails()
    {
        await using var server = await StartAsync(a => a
            .UseMiddleware<ReplaceRequest>()
            .AddAgent<WorkingThenCompletedAgent>("a", _ => Card("a")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.Handler.SendMessageAsync(Call("a"), Send("hi"), Ct));

        Assert.Contains("SendMessage", ex.Message);
        Assert.Contains("GetTaskRequest", ex.Message);
        Assert.Contains("it must stay SendMessageRequest", ex.Message);
    }

    [Fact]
    public void MetadataAfterCommitThrows()
    {
        var metadata = new A2AResponseMetadata();
        metadata.Set("A2A-Extensions", "x");
        Assert.True(metadata.TryGet("a2a-extensions", out var value));
        Assert.Equal("x", value);
        Assert.False(metadata.IsCommitted);

        metadata.MarkCommitted();

        Assert.True(metadata.IsCommitted);
        var ex = Assert.Throws<InvalidOperationException>(() => metadata.Set("A2A-Other", "y"));
        Assert.Equal("Response metadata must be set before the first byte of the response. Set it before calling next or before the first event.", ex.Message);
        Assert.Single(metadata.Entries);
    }
}

internal static class PipelineTestExtensions
{
    // Cancel and subscribe on a finished task are refused with an A2AException. The test only needs the middleware to see the call.
    public static async Task CancelTaskAsyncIgnoringRefusal(this IA2ARequestHandler handler, A2ACallContext call, string taskId, CancellationToken ct)
    {
        try { await handler.CancelTaskAsync(call, new CancelTaskRequest { Id = taskId }, ct); }
        catch (A2AException) { }
    }

    public static async Task SubscribeIgnoringRefusal(this IA2ARequestHandler handler, A2ACallContext call, string taskId, CancellationToken ct)
    {
        try
        {
            await foreach (var _ in handler.SubscribeToTaskAsync(call, new SubscribeToTaskRequest { Id = taskId }, ct)) { }
        }
        catch (A2AException) { }
    }
}
