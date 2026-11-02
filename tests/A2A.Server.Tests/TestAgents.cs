using System.Collections.Concurrent;
using System.Security.Claims;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Server.Tests;

/// <summary>Agent behavior supplied by a test.</summary>
public delegate ValueTask AgentScript(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct);

/// <summary>Holds the script the <see cref="ScriptedAgent"/> runs. One instance per test host.</summary>
public sealed class AgentScripts
{
    public AgentScript Execute { get; set; } = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.Completed, null, ct);

    /// <summary>Null → the interface default (set CANCELED).</summary>
    public AgentScript? Cancel { get; set; }

    public ConcurrentQueue<AgentRequestContext> Contexts { get; } = new();
}

/// <summary>Runs whatever script the test set. Created per execution through ActivatorUtilities.</summary>
public sealed class ScriptedAgent(AgentScripts scripts) : IAgentHandler
{
    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        scripts.Contexts.Enqueue(context);
        return scripts.Execute(context, sink, ct);
    }

    public ValueTask CancelAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => scripts.Cancel is { } cancel ? cancel(context, sink, ct) : ((IAgentHandler)new DefaultCancelAgent()).CancelAsync(context, sink, ct);

    // Uses the IAgentHandler default CancelAsync.
    private sealed class DefaultCancelAgent : IAgentHandler
    {
        public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct) => ValueTask.CompletedTask;
    }
}

/// <summary>A TimeProvider that always returns the same instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Gate a script awaits until the test opens it.</summary>
public sealed class Gate
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Open() => _tcs.TrySetResult();
    public Task WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);
}

/// <summary>A signal a script sets and a test awaits (with a timeout so tests never hang).</summary>
public sealed class Signal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Set() => _tcs.TrySetResult();
    public Task WaitAsync() => _tcs.Task.WaitAsync(TestHost.Timeout);
}

/// <summary>A TaskManager wired to one scripted agent named "test".</summary>
public sealed class TestHost
{
    public const string AgentName = "test";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public AgentScripts Scripts { get; } = new();
    public InMemoryTaskStore Store { get; } = new();
    public TaskEventBroker Broker { get; }
    public TaskManager Manager { get; }

    public TestHost(AgentCard? card = null, AgentCard? extendedCard = null, int brokerCapacity = TaskEventBroker.DefaultCapacity)
    {
        var services = new ServiceCollection().AddSingleton(Scripts).BuildServiceProvider();
        var registration = new AgentRegistration(AgentName, typeof(ScriptedAgent), card ?? DefaultCard, extendedCard, null);
        Broker = new TaskEventBroker(brokerCapacity);
        Manager = new TaskManager(new AgentCatalog([registration]), Store, Broker, services, time: new FixedTimeProvider(Now));
    }

    public static AgentCard DefaultCard => new()
    {
        Name = AgentName,
        Capabilities = new AgentCapabilities { Streaming = true },
        DefaultInputModes = ["text/plain", "application/json"],
        DefaultOutputModes = ["text/plain"],
    };

    public static A2ACallContext Call(string owner = "alice", params string[] extensions)
        => new(AgentName, new ClaimsPrincipal(new ClaimsIdentity()), owner, null, A2AConstants.ProtocolVersion, extensions);

    public static Message UserText(string text, string? taskId = null) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Role = Role.User,
        TaskId = taskId,
        Parts = [Part.FromText(text)],
    };

    public static SendMessageRequest Text(string text, string? taskId = null, bool returnImmediately = false, int? historyLength = null)
        => new()
        {
            Message = UserText(text, taskId),
            Configuration = new SendMessageConfiguration { ReturnImmediately = returnImmediately, HistoryLength = historyLength },
        };

    public static Message AgentText(string text) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Role = Role.Agent,
        Parts = [Part.FromText(text)],
    };

    public Task<SendMessageResponse> SendAsync(SendMessageRequest request, string owner = "alice")
        => Manager.SendMessageAsync(Call(owner), request, CancellationToken.None).WaitAsync(Timeout);

    public Task<AgentTask> GetAsync(string taskId, string owner = "alice", int? historyLength = null)
        => Manager.GetTaskAsync(Call(owner), new GetTaskRequest { Id = taskId, HistoryLength = historyLength }, CancellationToken.None);

    public static async Task<List<StreamResponse>> CollectAsync(IAsyncEnumerable<StreamResponse> stream)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var list = new List<StreamResponse>();
        await foreach (var e in stream.WithCancellation(cts.Token)) list.Add(e);
        return list;
    }
}
