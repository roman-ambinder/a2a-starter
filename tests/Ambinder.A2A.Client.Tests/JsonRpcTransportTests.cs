using System.Net;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Client.Tests.TestDoubles;
using Ambinder.A2A.Client.Transports.JsonRpc;
using Ambinder.A2A.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Client.Tests;

public class JsonRpcTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AgentTask Task1 = new()
    {
        Id = "t1",
        ContextId = "c1",
        Status = new AgentTaskStatus { State = TaskState.Completed },
    };

    private static IA2ATransport Open(StubHandler handler, A2AClientLimits? limits = null, A2AClientTimeouts? timeouts = null,
        TimeProvider? time = null)
    {
        var http = new HttpClient(handler);
        var card = Cards.JsonRpc("http://agent.test/rpc");
        return new JsonRpcTransportFactory().Create(card.SupportedInterfaces[0], card, new A2ATransportContext
        {
            ClientName = "billing",
            HttpClient = http,
            HandlerFactory = null,
            Limits = limits ?? new A2AClientLimits(),
            Timeouts = timeouts ?? new A2AClientTimeouts(),
            Logger = NullLogger.Instance,
            Time = time ?? TimeProvider.System,
        });
    }

    private static A2ATransportRequest Request(string method, object body, Action<A2AResponseInfo>? onHeaders = null,
        A2ARequestHeaders? headers = null) => new()
    {
        Method = method,
        Body = body,
        Headers = headers ?? new A2ARequestHeaders(),
        ProtocolVersion = "1.0",
        CallInfo = new A2ACallInfo
        {
            Method = method,
            ClientName = "billing",
            Safety = A2ARetrySafety.Idempotent,
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutOption = "A2AClientOptions.Timeouts.Unary",
        },
        OnHeaders = onHeaders,
    };

    private static HttpResponseMessage Sse(params string[] events)
    {
        var text = new StringBuilder();
        foreach (var e in events) text.Append(e.StartsWith(':') ? e : "data: " + e).Append("\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    private static string Envelope(object result) => StubHandler.JsonRpcResult(result);

    private static string StatusEvent(TaskState state) => Envelope(new StreamResponse
    {
        StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", ContextId = "c1", Status = new AgentTaskStatus { State = state } },
    });

    public static TheoryData<string, object, object> UnaryCases() => new()
    {
        { "SendMessage", new SendMessageRequest(), new SendMessageResponse { Task = Task1 } },
        { "GetTask", new GetTaskRequest { Id = "t1" }, Task1 },
        { "ListTasks", new ListTasksRequest(), new ListTasksResponse() },
        { "CancelTask", new CancelTaskRequest { Id = "t1" }, Task1 },
        { "CreateTaskPushNotificationConfig", new TaskPushNotificationConfig { TaskId = "t1", Url = "https://hook.test/" }, new TaskPushNotificationConfig { TaskId = "t1", Url = "https://hook.test/" } },
        { "GetTaskPushNotificationConfig", new GetTaskPushNotificationConfigRequest { TaskId = "t1", Id = "p1" }, new TaskPushNotificationConfig { TaskId = "t1", Url = "https://hook.test/" } },
        { "ListTaskPushNotificationConfigs", new ListTaskPushNotificationConfigsRequest { TaskId = "t1" }, new ListTaskPushNotificationConfigsResponse() },
        { "GetExtendedAgentCard", new GetExtendedAgentCardRequest(), Cards.JsonRpc("http://agent.test/rpc") },
    };

    [Theory]
    [MemberData(nameof(UnaryCases))]
    public async Task Unary_methods_post_an_envelope_and_return_the_typed_result(string method, object body, object result)
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(result));
        await using var transport = Open(handler);

        var response = await transport.SendAsync(Request(method, body), Ct);

        Assert.IsType(result.GetType(), response.Body);
        Assert.Equal(1, handler.Calls);
        var message = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, message.Method);
        Assert.Equal("http://agent.test/rpc", message.RequestUri!.ToString());
        using var json = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("2.0", json.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(method, json.RootElement.GetProperty("method").GetString());
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("params").ValueKind);
    }

    [Fact]
    public async Task Delete_push_config_returns_an_empty_result()
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk("null"));
        await using var transport = Open(handler);
        var response = await transport.SendAsync(
            Request("DeleteTaskPushNotificationConfig", new DeleteTaskPushNotificationConfigRequest { TaskId = "t1", Id = "p1" }), Ct);
        Assert.NotNull(response.Body);
        using var json = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("DeleteTaskPushNotificationConfig", json.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Request_ids_increase()
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(Task1));
        await using var transport = Open(handler);
        await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct);
        await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct);
        var ids = handler.Bodies.Select(b => JsonDocument.Parse(b).RootElement.GetProperty("id").GetInt64()).ToArray();
        Assert.True(ids[1] > ids[0]);
    }

    [Fact]
    public async Task Headers_version_accept_extra_headers_authorization_and_call_info()
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(Task1));
        await using var transport = Open(handler);
        var headers = new A2ARequestHeaders { ["X-Trace"] = "abc" };
        headers.SetAuthorization("Bearer secret");
        var request = Request("GetTask", new GetTaskRequest { Id = "t1" }, headers: headers);

        await transport.SendAsync(request, Ct);

        var message = handler.Requests[0];
        Assert.Equal("1.0", Assert.Single(message.Headers.GetValues("A2A-Version")));
        Assert.Equal("application/json", Assert.Single(message.Headers.GetValues("Accept")));
        Assert.Equal("abc", Assert.Single(message.Headers.GetValues("X-Trace")));
        Assert.Equal("Bearer secret", message.Headers.Authorization!.ToString());
        Assert.Equal("application/json", handler.Bodies.Count == 1 ? message.Content!.Headers.ContentType!.MediaType : null);
        Assert.Equal(new Version(1, 1), message.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, message.VersionPolicy);
        Assert.True(message.Options.TryGetValue(A2ACallInfo.Key, out var info));
        Assert.Same(request.CallInfo, info);
    }

    [Fact]
    public async Task A_stream_asks_for_an_event_stream()
    {
        var handler = new StubHandler(_ => Sse(StatusEvent(TaskState.Completed)));
        await using var transport = Open(handler);
        await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        Assert.Equal("text/event-stream", Assert.Single(handler.Requests[0].Headers.GetValues("Accept")));
    }

    [Fact]
    public async Task An_error_envelope_with_ErrorInfo_maps_to_the_A2A_code_and_metadata()
    {
        const string body =
            """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"old","data":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"VERSION_NOT_SUPPORTED","domain":"a2a-protocol.org","metadata":{"supportedVersions":"1.0"}}]}}""";
        var handler = new StubHandler(_ => StubHandler.Json(body));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.VersionNotSupported, ex.Code);
        Assert.Contains("supportedVersions", ex.Data?.ToString());
    }

    [Fact]
    public async Task A_numeric_error_code_without_details_maps_to_the_code()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"gone"}}""", HttpStatusCode.NotFound));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
        Assert.Equal("gone", ex.Message);
    }

    [Fact]
    public async Task An_error_body_over_MaxErrorBodyBytes_names_the_option()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(new string('x', 100_000)),
        });
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Contains("MaxErrorBodyBytes", ex.Message);
        Assert.Contains("billing", ex.Message);
    }

    [Fact]
    public async Task A_unary_body_over_MaxResponseBytes_names_the_option()
    {
        var handler = new StubHandler(_ => StubHandler.Json(new string(' ', 5000) + Envelope(Task1)));
        await using var transport = Open(handler, new A2AClientLimits { MaxResponseBytes = 1024 });
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Contains("A2AClientOptions.Limits.MaxResponseBytes", ex.Message);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    public async Task A_non_2xx_without_a_JSON_RPC_body_is_a_transport_exception(int status, bool transient)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("<html>nope</html>") });
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2ATransportException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal(transient, ex.IsTransient);
        Assert.Contains("billing", ex.Message);
    }

    [Theory]
    [InlineData(401, A2AAuthFailureKind.Unauthenticated)]
    [InlineData(403, A2AAuthFailureKind.Forbidden)]
    public async Task Auth_failures_keep_the_challenge(int status, A2AAuthFailureKind kind)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer error=\"insufficient_scope\", scope=\"a2a\"");
            return response;
        });
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal("Bearer error=\"insufficient_scope\", scope=\"a2a\"", ex.Data["WWW-Authenticate"]);
    }

    [Fact]
    public async Task A_2xx_that_is_not_JSON_RPC_is_an_invalid_agent_response()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"hello":1}"""));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task SendMessage_result_with_both_task_and_message_is_rejected()
    {
        var both = new SendMessageResponse { Task = Task1, Message = new Message { MessageId = "m", Role = Role.Agent } };
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(both));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("SendMessage", new SendMessageRequest()), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task OnHeaders_gets_extensions_status_and_version_for_unary_calls()
    {
        var handler = new StubHandler(_ =>
        {
            var response = StubHandler.JsonRpcOk(Task1);
            response.Headers.TryAddWithoutValidation("A2A-Extensions", "https://x.test/a, https://x.test/b ,");
            return response;
        });
        await using var transport = Open(handler);
        A2AResponseInfo? seen = null;
        var response = await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }, onHeaders: i => seen = i), Ct);

        Assert.NotNull(seen);
        Assert.Equal(["https://x.test/a", "https://x.test/b"], seen.ActivatedExtensions);
        Assert.Equal(200, seen.HttpStatus);
        Assert.Equal("JSONRPC", seen.Transport);
        Assert.Equal("1.0", seen.ProtocolVersion);
        Assert.Equal(seen.ActivatedExtensions, response.Info.ActivatedExtensions);
    }

    [Fact]
    public async Task Stream_yields_events_skips_heartbeats_and_calls_OnHeaders_before_the_first_event()
    {
        var handler = new StubHandler(_ => Sse(
            ":heartbeat",
            StatusEvent(TaskState.Working),
            "   ",
            StatusEvent(TaskState.Completed)));
        await using var transport = Open(handler);
        var order = new List<string>();
        var request = Request("SendStreamingMessage", new SendMessageRequest(), onHeaders: _ => order.Add("headers"));

        await foreach (var evt in transport.StreamAsync(request, Ct)) order.Add(evt.StatusUpdate!.Status.State.ToString());

        Assert.Equal(["headers", "Working", "Completed"], order);
    }

    [Fact]
    public async Task Stream_without_a_final_event_fails()
    {
        var handler = new StubHandler(_ => Sse(StatusEvent(TaskState.Working)));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal("stream ended before a final event", ex.Message);
    }

    [Fact]
    public async Task An_error_frame_inside_a_stream_throws_the_mapped_exception()
    {
        var handler = new StubHandler(_ => Sse(StatusEvent(TaskState.Working),
            """{"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"gone"}}"""));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t1" }), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public async Task A_pre_stream_JSON_RPC_error_is_thrown()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"gone"}}""", HttpStatusCode.NotFound));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t1" }), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public async Task A_2xx_JSON_answer_to_a_stream_request_is_a_protocol_violation()
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(Task1));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t1" }), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Contains("text/event-stream", ex.Message);
    }

    [Fact]
    public async Task A_stream_event_over_MaxEventBytes_names_the_option()
    {
        var handler = new StubHandler(_ => Sse(Envelope(new StreamResponse { Message = new Message { MessageId = new string('m', 5000), Role = Role.Agent } })));
        await using var transport = Open(handler, new A2AClientLimits { MaxEventBytes = 1024 });
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Contains("MaxEventBytes", ex.Message);
    }

    [Fact]
    public async Task A_silent_stream_times_out_after_the_idle_timeout()
    {
        var time = new FakeTimeProvider();
        var started = new TaskCompletionSource();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream(started)) { Headers = { { "Content-Type", "text/event-stream" } } },
        });
        await using var transport = Open(handler, timeouts: new A2AClientTimeouts { StreamIdleTimeout = TimeSpan.FromSeconds(30) }, time: time);
        var read = Assert.ThrowsAsync<A2ATimeoutException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        await started.Task.WaitAsync(Ct);
        time.Advance(TimeSpan.FromSeconds(31));
        await read;
    }

    [Fact]
    public async Task Cancelling_a_stream_stops_it_with_OperationCanceledException()
    {
        var started = new TaskCompletionSource();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream(started)) { Headers = { { "Content-Type", "text/event-stream" } } },
        });
        await using var transport = Open(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var read = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), cts.Token)) { }
        });
        await started.Task.WaitAsync(Ct);
        await cts.CancelAsync();
        await read;
    }

    [Fact]
    public void Factory_declares_the_JSONRPC_binding_at_1_0()
    {
        var factory = new JsonRpcTransportFactory();
        Assert.Equal(["JSONRPC"], factory.Bindings);
        Assert.Equal(["1.0"], factory.ProtocolVersions);
    }

    [Fact]
    public async Task Disposing_the_transport_does_not_dispose_the_HttpClient()
    {
        var handler = new StubHandler(_ => StubHandler.JsonRpcOk(Task1));
        var http = new HttpClient(handler);
        var card = Cards.JsonRpc("http://agent.test/rpc");
        var transport = new JsonRpcTransportFactory().Create(card.SupportedInterfaces[0], card, ScriptedTransportFactory.NewContext(http));
        await transport.DisposeAsync();
        using var message = new HttpRequestMessage(HttpMethod.Get, "http://agent.test/");
        using var response = await http.SendAsync(message, Ct); // would throw ObjectDisposedException
        Assert.True(response.IsSuccessStatusCode);
    }

    /// <summary>A stream that signals when it is first read, then waits until the read is cancelled.</summary>
    private sealed class BlockingStream(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
