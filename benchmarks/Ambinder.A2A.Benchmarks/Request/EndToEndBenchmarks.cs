extern alias clientgrpc;
using Ambinder.A2A.Benchmarks.Hosts;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using BenchmarkDotNet.Attributes;
using GrpcClientTransport = clientgrpc::Ambinder.A2A.Client.Grpc.GrpcClientTransport;

namespace Ambinder.A2A.Benchmarks.Request;

/// <summary>
/// End to end: the real client against the real server on Kestrel (loopback, <c>127.0.0.1:0</c>-style free ports). One client per
/// agent is created in <c>GlobalSetup</c> from the agent card, so the transport is the card's interface for the chosen binding.
/// </summary>
[BenchmarkCategory(Categories.Request)]
public class EndToEndBenchmarks
{
    [Params(Transport.JsonRpc, Transport.Rest, Transport.Grpc)]
    public Transport Transport { get; set; }

    private BenchServer _server = null!;
    private HttpClient _http = null!;
    private IA2AClient _hello = null!;
    private IA2AClient _task = null!;
    private SendMessageRequest _helloRequest = null!;
    private SendMessageRequest _taskRequest = null!;
    private SendMessageRequest _taskImmediateRequest = null!;
    private GetTaskRequest _getTaskRequest = null!;
    private ListTasksRequest _listRequest = null!;
    private Uri _helloCardUrl = null!;

    [GlobalSetup]
    public void Setup()
    {
        _server = BenchServer.StartKestrelAsync().GetAwaiter().GetResult();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _helloCardUrl = new Uri($"{_server.BaseUrl}/agents/{BenchServer.HelloAgentName}");
        _hello = CreateClient(BenchServer.HelloAgentName);
        _task = CreateClient(BenchServer.TaskAgentName);
        _helloRequest = BenchServer.MessageRequest("hi");
        _taskRequest = BenchServer.MessageRequest("2");
        _taskImmediateRequest = BenchServer.MessageRequest("2", returnImmediately: true);
        _getTaskRequest = new GetTaskRequest { Id = BenchServer.KnownTaskId };
        _listRequest = new ListTasksRequest { PageSize = 50 };

        // Check every scenario once: a benchmark that gets a wrong answer must fail, not just be slow.
        SendMessage_Hello().GetAwaiter().GetResult();
        SendMessage_Task().GetAwaiter().GetResult();
        GetTask().GetAwaiter().GetResult();
        ListTasks().GetAwaiter().GetResult();
        GetAgentCard().GetAwaiter().GetResult();
        SendMessageReturnImmediately_ThenGetTask().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _hello.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _task.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _http.Dispose();
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task SendMessage_Hello()
    {
        var r = await _hello.SendMessageAsync(_helloRequest).ConfigureAwait(false);
        if (r.Task is not null || r.Message?.Parts.Count != 1 || r.Message.Parts[0].Text != BenchServer.HelloText)
        {
            throw new InvalidOperationException("HelloAgent should answer with its message and no task.");
        }
    }

    [Benchmark]
    public async Task SendMessage_Task()
    {
        var r = await _task.SendMessageAsync(_taskRequest).ConfigureAwait(false);
        if (r.Task is not { } t || t.Status.State != TaskState.Completed || t.Artifacts.Count != 1)
        {
            throw new InvalidOperationException($"TaskAgent result is wrong: state {r.Task?.Status.State}, artifacts {r.Task?.Artifacts.Count}.");
        }
    }

    [Benchmark]
    public async Task GetTask()
    {
        var t = await _task.GetTaskAsync(_getTaskRequest).ConfigureAwait(false);
        if (t.Id != BenchServer.KnownTaskId) throw new InvalidOperationException($"GetTask returned task '{t.Id}'.");
    }

    [Benchmark]
    public async Task ListTasks()
    {
        var page = await _task.ListTasksAsync(_listRequest).ConfigureAwait(false);
        if (page.Tasks.Count != 50) throw new InvalidOperationException($"ListTasks returned {page.Tasks.Count} tasks, expected 50.");
    }

    // A new resolver per call: the resolver caches cards for five minutes, so a reused one would measure a dictionary lookup.
    [Benchmark]
    public async Task GetAgentCard()
    {
        var card = await new A2ACardResolver(_http).GetAgentCardAsync(_helloCardUrl).ConfigureAwait(false);
        if (card.Name != BenchServer.HelloAgentName) throw new InvalidOperationException($"Agent card has the name '{card.Name}'.");
    }

    [Benchmark]
    public async Task SendMessageReturnImmediately_ThenGetTask()
    {
        var sent = await _task.SendMessageAsync(_taskImmediateRequest).ConfigureAwait(false);
        var id = sent.Task?.Id ?? throw new InvalidOperationException("returnImmediately should answer with a task.");
        var t = await _task.GetTaskAsync(new GetTaskRequest { Id = id }).ConfigureAwait(false);
        if (t.Id != id) throw new InvalidOperationException($"GetTask returned task '{t.Id}', expected '{id}'.");
    }

    private IA2AClient CreateClient(string agent)
    {
        var card = new A2ACardResolver(_http).GetAgentCardAsync(new Uri($"{_server.BaseUrl}/agents/{agent}")).GetAwaiter().GetResult();
        var binding = Transport switch
        {
            Transport.JsonRpc => A2AConstants.BindingJsonRpc,
            Transport.Rest => A2AConstants.BindingRest,
            _ => A2AConstants.BindingGrpc,
        };
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            PreferredBinding = binding,
            Transports = [GrpcClientTransport.Instance],
        });
    }
}
