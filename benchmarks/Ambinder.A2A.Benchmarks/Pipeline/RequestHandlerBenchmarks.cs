using System.Security.Claims;
using Ambinder.A2A.Benchmarks.Agents;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Benchmarks.Pipeline;

/// <summary>The request handler without a transport: validation, task manager, store, event broker and the agent.</summary>
[BenchmarkCategory(Categories.Pipeline)]
public class RequestHandlerBenchmarks
{
    private const int Updates = 10;

    private ServiceProvider _provider = null!;
    private IA2ARequestHandler _handler = null!;
    private A2ACallContext _helloCall = null!;
    private A2ACallContext _taskCall = null!;
    private SendMessageRequest _helloRequest = null!;
    private SendMessageRequest _taskRequest = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The same calls the docs tell users to make; no debug logging.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5200")
            .AddAgent<HelloAgent>("hello", _ => new AgentCard { Name = "hello", Description = "Bench", Version = "1.0.0" })
            .AddAgent<TaskAgent>("task", _ => new AgentCard { Name = "task", Description = "Bench", Version = "1.0.0" });
        _provider = services.BuildServiceProvider();
        _handler = _provider.GetRequiredService<IA2ARequestHandler>();

        _helloCall = Call("hello");
        _taskCall = Call("task");
        _helloRequest = Request("hi");
        _taskRequest = Request(Updates.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Check the results once: a benchmark that gets a wrong answer must fail, not just be slow.
        var hello = _handler.SendMessageAsync(_helloCall, _helloRequest, CancellationToken.None).GetAwaiter().GetResult();
        CheckHello(hello);
        var task = _handler.SendMessageAsync(_taskCall, _taskRequest, CancellationToken.None).GetAwaiter().GetResult();
        CheckTask(task);
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public async Task<SendMessageResponse> SendMessage_Hello()
    {
        var r = await _handler.SendMessageAsync(_helloCall, _helloRequest, CancellationToken.None).ConfigureAwait(false);
        CheckHello(r);
        return r;
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public async Task<SendMessageResponse> SendMessage_TaskWith10Updates()
    {
        var r = await _handler.SendMessageAsync(_taskCall, _taskRequest, CancellationToken.None).ConfigureAwait(false);
        CheckTask(r);
        return r;
    }

    private static void CheckHello(SendMessageResponse r)
    {
        if (r.Task is not null || r.Message?.Parts.Count != 1)
        {
            throw new InvalidOperationException("HelloAgent should answer with one message and no task.");
        }
    }

    private static void CheckTask(SendMessageResponse r)
    {
        // 10 updates alternate status, artifact: 5 artifacts. The task ends Completed.
        if (r.Task is not { } t || t.Status.State != TaskState.Completed || t.Artifacts.Count != Updates / 2)
        {
            throw new InvalidOperationException($"TaskAgent result is wrong: state {r.Task?.Status.State}, artifacts {r.Task?.Artifacts.Count}.");
        }
    }

    private static A2ACallContext Call(string agent)
        => new(agent, new ClaimsPrincipal(new ClaimsIdentity()), "bench", null, A2AConstants.ProtocolVersion, []);

    private static SendMessageRequest Request(string text) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(text)] },
    };
}
