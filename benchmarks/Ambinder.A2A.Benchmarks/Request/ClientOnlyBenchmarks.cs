extern alias clientgrpc;
using System.Text.Json;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Benchmarks.Hosts;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Protocol.ProtoMapping;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using GrpcClientTransport = clientgrpc::Ambinder.A2A.Client.Grpc.GrpcClientTransport;

namespace Ambinder.A2A.Benchmarks.Request;

/// <summary>
/// Client-only: the real client with a <see cref="CannedHandler"/> that returns pre-serialized response bytes for each method.
/// The JSON-RPC <c>SendMessage</c> benchmark is named <c>ClientSendMessage</c>; package 10's acceptance criteria cite that name.
/// </summary>
/// <remarks>
/// gRPC: the plan says "a fake gRPC CallInvoker". The generated client and <c>GrpcA2AClient</c> are internal to
/// <c>Ambinder.A2A.Client.Grpc</c> and that package has no InternalsVisibleTo for the benchmarks, so the real gRPC client runs over
/// the same canned handler instead (HTTP/2 answer with one framed message and the grpc-status trailer). The cost of the channel
/// is part of the number, like in the other two transports.
/// </remarks>
[BenchmarkCategory(Categories.Request, Categories.Client)]
public class ClientOnlyBenchmarks
{
    [Params(Transport.JsonRpc, Transport.Rest, Transport.Grpc)]
    public Transport Transport { get; set; }

    private IA2AClient _send = null!;
    private IA2AClient _get = null!;
    private IA2AClient _list = null!;
    private SendMessageRequest _sendRequest = null!;
    private GetTaskRequest _getRequest = null!;
    private ListTasksRequest _listRequest = null!;
    private HttpClient[] _https = null!;

    private static readonly AgentTask Task = Payloads.Task20;

    [GlobalSetup]
    public void Setup()
    {
        var reply = new SendMessageResponse { Task = Task };
        var page = new ListTasksResponse
        {
            Tasks = Enumerable.Range(0, 50).Select(i => Payloads.Task0 with { Id = "bench-task-" + i }).ToList(),
            NextPageToken = "next",
            PageSize = 50,
            TotalSize = 1000,
        };
        _sendRequest = BenchServer.MessageRequest("hi");
        _getRequest = new GetTaskRequest { Id = Task.Id };
        _listRequest = new ListTasksRequest { PageSize = 50 };

        var (sendHandler, getHandler, listHandler) = Transport switch
        {
            Transport.JsonRpc => (
                CannedHandler.Json(Envelope(JsonSerializer.SerializeToUtf8Bytes(reply, A2AJson.SendMessageResponse))),
                CannedHandler.Json(Envelope(JsonSerializer.SerializeToUtf8Bytes(Task, A2AJson.AgentTask))),
                CannedHandler.Json(Envelope(JsonSerializer.SerializeToUtf8Bytes(page, A2AJson.ListTasksResponse)))),
            Transport.Rest => (
                CannedHandler.Json(JsonSerializer.SerializeToUtf8Bytes(reply, A2AJson.SendMessageResponse)),
                CannedHandler.Json(JsonSerializer.SerializeToUtf8Bytes(Task, A2AJson.AgentTask)),
                CannedHandler.Json(JsonSerializer.SerializeToUtf8Bytes(page, A2AJson.ListTasksResponse))),
            _ => (
                CannedHandler.Grpc(ProtoMapper.ToProto(reply).ToByteArray()),
                CannedHandler.Grpc(ProtoMapper.ToProto(Task).ToByteArray()),
                CannedHandler.Grpc(ProtoMapper.ToProto(page).ToByteArray())),
        };
        _https = [new HttpClient(sendHandler), new HttpClient(getHandler), new HttpClient(listHandler)];
        _send = Create(_https[0]);
        _get = Create(_https[1]);
        _list = Create(_https[2]);

        // Check every scenario once: a benchmark that gets a wrong answer must fail, not just be slow.
        ClientSendMessage().GetAwaiter().GetResult();
        ClientGetTask().GetAwaiter().GetResult();
        ClientListTasks().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _send.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _get.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _list.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var h in _https) h.Dispose();
    }

    [Benchmark]
    public async Task ClientSendMessage()
    {
        var r = await _send.SendMessageAsync(_sendRequest).ConfigureAwait(false);
        if (r.Task?.Id != Task.Id || r.Task.History.Count != Task.History.Count)
        {
            throw new InvalidOperationException("SendMessage returned the wrong task.");
        }
    }

    [Benchmark]
    public async Task ClientGetTask()
    {
        var t = await _get.GetTaskAsync(_getRequest).ConfigureAwait(false);
        if (t.Id != Task.Id) throw new InvalidOperationException($"GetTask returned task '{t.Id}'.");
    }

    [Benchmark]
    public async Task ClientListTasks()
    {
        var p = await _list.ListTasksAsync(_listRequest).ConfigureAwait(false);
        if (p.Tasks.Count != 50) throw new InvalidOperationException($"ListTasks returned {p.Tasks.Count} tasks, expected 50.");
    }

    private IA2AClient Create(HttpClient http)
    {
        var (binding, url) = Transport switch
        {
            Transport.JsonRpc => (A2AConstants.BindingJsonRpc, "http://canned.test/agents/bench/jsonrpc"),
            Transport.Rest => (A2AConstants.BindingRest, "http://canned.test/agents/bench/rest"),
            _ => (A2AConstants.BindingGrpc, "http://canned.test:50051"),
        };
        var card = new AgentCard
        {
            Name = "bench",
            Description = "Canned",
            Version = "1.0.0",
            SupportedInterfaces = [new AgentInterface { Url = url, ProtocolBinding = binding, ProtocolVersion = A2AConstants.ProtocolVersion }],
        };
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = http,
            PreferredBinding = binding,
            Transports = [GrpcClientTransport.Instance],
        });
    }

    private static byte[] Envelope(byte[] result)
    {
        using var ms = new MemoryStream();
        ms.Write("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":"u8);
        ms.Write(result);
        ms.Write("}"u8);
        return ms.ToArray();
    }
}
