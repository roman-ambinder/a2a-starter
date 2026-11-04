using System.Net;
using System.Text.Json;
using Ambinder.A2A.Benchmarks.Hosts;
using Ambinder.A2A.Protocol;
using BenchmarkDotNet.Attributes;

namespace Ambinder.A2A.Benchmarks.Request;

/// <summary>
/// Server-only layer: requests are pre-built (the body bytes are ready; a fresh <see cref="HttpRequestMessage"/> over the same
/// array is made per call), so the client side costs almost nothing. The server runs on <c>TestServer</c>.
/// </summary>
/// <remarks>The gRPC rows of this layer wait for package 09 (gRPC routing by tenant instead of local port): see <see cref="BenchServer"/>.</remarks>
[BenchmarkCategory(Categories.Request)]
public class ServerOnlyBenchmarks
{
    private const string Version = "A2A-Version";

    // Only JSON-RPC and REST: see the remark above. GetAgentCard does not depend on the transport, so it shows the same number in both rows.
    [Params(Transport.JsonRpc, Transport.Rest)]
    public Transport Transport { get; set; }

    private BenchServer _server = null!;
    private HttpClient _http = null!;
    private Call _hello;
    private Call _task;
    private Call _taskImmediate;
    private Call _getTask;
    private Call _list;
    private string _cardUrl = null!;

    /// <summary>One request: how to build it and what the answer must contain.</summary>
    private readonly record struct Call(HttpMethod Method, string Url, byte[]? Body, string? ContentType, string Expect);

    [GlobalSetup]
    public void Setup()
    {
        _server = BenchServer.StartTestServerAsync().GetAwaiter().GetResult();
        _http = _server.InMemoryClient!;
        _cardUrl = $"/agents/{BenchServer.HelloAgentName}/.well-known/agent-card.json";
        var rpc = Transport == Transport.JsonRpc;
        var hello = BenchServer.MessageRequest("hi");
        var task = BenchServer.MessageRequest("2");
        var immediate = BenchServer.MessageRequest("2", returnImmediately: true);

        _hello = Send(BenchServer.HelloAgentName, hello, BenchServer.HelloText);
        _task = Send(BenchServer.TaskAgentName, task, "TASK_STATE_COMPLETED");
        _taskImmediate = Send(BenchServer.TaskAgentName, immediate, "\"id\"");
        if (rpc)
        {
            _getTask = Rpc(BenchServer.TaskAgentName, "GetTask", new GetTaskRequest { Id = BenchServer.KnownTaskId }, A2AJson.GetTaskRequest, BenchServer.KnownTaskId);
            _list = Rpc(BenchServer.TaskAgentName, "ListTasks", new ListTasksRequest { PageSize = 50 }, A2AJson.ListTasksRequest, "nextPageToken");
        }
        else
        {
            _getTask = new Call(HttpMethod.Get, $"/agents/{BenchServer.TaskAgentName}/rest/tasks/{BenchServer.KnownTaskId}", null, null, BenchServer.KnownTaskId);
            _list = new Call(HttpMethod.Get, $"/agents/{BenchServer.TaskAgentName}/rest/tasks?pageSize=50", null, null, "nextPageToken");
        }

        // Check every scenario once: a benchmark that gets a wrong answer must fail, not just be slow.
        foreach (var call in new[] { _hello, _task, _taskImmediate, _getTask, _list })
        {
            Run(call).GetAwaiter().GetResult();
        }

        var card = _http.GetByteArrayAsync(_cardUrl).GetAwaiter().GetResult();
        if (!BenchServer.Contains(card, "\"name\":\"hello\""u8)) throw new InvalidOperationException("Agent card has the wrong name.");
        CheckPageOf50().GetAwaiter().GetResult();

        Call Send(string agent, SendMessageRequest request, string expect)
            => rpc
                ? Rpc(agent, "SendMessage", request, A2AJson.SendMessageRequest, expect)
                : new Call(HttpMethod.Post, $"/agents/{agent}/rest/message:send",
                    JsonSerializer.SerializeToUtf8Bytes(request, A2AJson.SendMessageRequest), "application/json", expect);

        static Call Rpc<T>(string agent, string method, T request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, string expect)
            => new(HttpMethod.Post, $"/agents/{agent}/jsonrpc", BenchServer.JsonRpcBody(method, request, info), "application/json", expect);
    }

    [GlobalCleanup]
    public void Cleanup() => _server.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public Task SendMessage_Hello() => Run(_hello);

    [Benchmark]
    public Task SendMessage_Task() => Run(_task);

    [Benchmark]
    public Task GetTask() => Run(_getTask);

    [Benchmark]
    public Task ListTasks() => Run(_list);

    [Benchmark]
    public async Task GetAgentCard()
    {
        using var response = await _http.GetAsync(_cardUrl).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK || !BenchServer.Contains(bytes, "\"name\":\"hello\""u8))
        {
            throw new InvalidOperationException($"GetAgentCard answered {(int)response.StatusCode} without the card.");
        }
    }

    [Benchmark]
    public async Task SendMessageReturnImmediately_ThenGetTask()
    {
        var sent = await Run(_taskImmediate).ConfigureAwait(false);
        var id = TaskIdOf(sent);
        // The task may still run or already be done; GetTask must find it either way.
        var get = _getTaskFor(id);
        await Run(get).ConfigureAwait(false);
    }

    private Call _getTaskFor(string id)
        => Transport == Transport.JsonRpc
            ? new Call(HttpMethod.Post, $"/agents/{BenchServer.TaskAgentName}/jsonrpc",
                BenchServer.JsonRpcBody("GetTask", new GetTaskRequest { Id = id }, A2AJson.GetTaskRequest), "application/json", id)
            : new Call(HttpMethod.Get, $"/agents/{BenchServer.TaskAgentName}/rest/tasks/{id}", null, null, id);

    private async Task<byte[]> Run(Call call)
    {
        using var request = new HttpRequestMessage(call.Method, call.Url);
        request.Headers.TryAddWithoutValidation(Version, A2AConstants.ProtocolVersion);
        if (call.Body is { } body)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(call.ContentType!);
        }

        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK
            || !BenchServer.Contains(bytes, System.Text.Encoding.UTF8.GetBytes(call.Expect))
            || BenchServer.Contains(bytes, "\"error\""u8))
        {
            throw new InvalidOperationException($"{call.Method} {call.Url} answered {(int)response.StatusCode}, expected {call.Expect}: {System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300))}");
        }

        return bytes;
    }

    /// <summary>The ListTasks answer must hold exactly 50 tasks of the 1,000 (an empty page would be a silent wrong answer).</summary>
    private async Task CheckPageOf50()
    {
        var bytes = await Run(_list).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (Transport == Transport.JsonRpc) root = root.GetProperty("result");
        var count = root.GetProperty("tasks").GetArrayLength();
        if (count != 50) throw new InvalidOperationException($"ListTasks returned {count} tasks, expected 50.");
    }

    private string TaskIdOf(byte[] response)
    {
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        if (Transport == Transport.JsonRpc) root = root.GetProperty("result");
        return root.GetProperty("task").GetProperty("id").GetString()
               ?? throw new InvalidOperationException("returnImmediately answer has no task id.");
    }
}
