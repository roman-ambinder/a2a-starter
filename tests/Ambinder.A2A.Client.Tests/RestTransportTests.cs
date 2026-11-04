using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Ambinder.A2A.Client.Tests.TestDoubles;
using Ambinder.A2A.Client.Transports.Rest;
using Ambinder.A2A.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Client.Tests;

public class RestTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string TaskJson = """{"id":"t1","contextId":"c1","status":{"state":"TASK_STATE_COMPLETED"}}""";

    private static readonly AgentTask Task1 = new()
    {
        Id = "t1",
        ContextId = "c1",
        Status = new AgentTaskStatus { State = TaskState.Completed },
    };

    private static IA2ATransport Open(StubHandler handler, A2AClientLimits? limits = null, A2AClientTimeouts? timeouts = null,
        TimeProvider? time = null, string url = "http://agent.test/agents/x/rest/")
    {
        var http = new HttpClient(handler);
        var card = Cards.Rest(url);
        return new RestTransportFactory().Create(card.SupportedInterfaces[0], card, new A2ATransportContext
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

    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/a2a+json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static HttpResponseMessage Sse(params string[] events)
    {
        var text = new StringBuilder();
        foreach (var e in events) text.Append(e.StartsWith(':') ? e : "data: " + e).Append("\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    private static string StatusEvent(TaskState state)
        => JsonSerializer.Serialize(new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", ContextId = "c1", Status = new AgentTaskStatus { State = state } },
        }, A2AJson.Options);

    private const string NoReasonError = """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"bad params"}}""";

    private const string NotFoundError =
        """{"error":{"code":404,"status":"NOT_FOUND","message":"no such task","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"TASK_NOT_FOUND","domain":"a2a-protocol.org"}]}}""";

    // ---------- paths and verbs ----------

    public static TheoryData<string, object, string, string, string> PathCases() => new()
    {
        // method, body, expected HTTP method, expected path, response body
        { "SendMessage", new SendMessageRequest(), "POST", "/agents/x/rest/message:send", """{"task":""" + TaskJson + "}" },
        { "GetTask", new GetTaskRequest { Id = "a b/c:d" }, "GET", "/agents/x/rest/tasks/a%20b%2Fc%3Ad", TaskJson },
        { "ListTasks", new ListTasksRequest(), "GET", "/agents/x/rest/tasks", """{"tasks":[],"nextPageToken":"","pageSize":5,"totalSize":0}""" },
        { "CancelTask", new CancelTaskRequest { Id = "t 1" }, "POST", "/agents/x/rest/tasks/t%201:cancel", TaskJson },
        { "CreateTaskPushNotificationConfig", new TaskPushNotificationConfig { TaskId = "t 1", Url = "http://h" }, "POST",
            "/agents/x/rest/tasks/t%201/pushNotificationConfigs", """{"taskId":"t 1","id":"c1","url":"http://h"}""" },
        { "GetTaskPushNotificationConfig", new GetTaskPushNotificationConfigRequest { TaskId = "t 1", Id = "c 1" }, "GET",
            "/agents/x/rest/tasks/t%201/pushNotificationConfigs/c%201", """{"taskId":"t 1","id":"c 1","url":"http://h"}""" },
        { "ListTaskPushNotificationConfigs", new ListTaskPushNotificationConfigsRequest { TaskId = "t 1" }, "GET",
            "/agents/x/rest/tasks/t%201/pushNotificationConfigs", """{"configs":[]}""" },
        { "DeleteTaskPushNotificationConfig", new DeleteTaskPushNotificationConfigRequest { TaskId = "t 1", Id = "c 1" }, "DELETE",
            "/agents/x/rest/tasks/t%201/pushNotificationConfigs/c%201", "" },
        { "GetExtendedAgentCard", new GetExtendedAgentCardRequest(), "GET", "/agents/x/rest/extendedAgentCard",
            JsonSerializer.Serialize(Cards.Rest("http://agent.test/"), A2AJson.Options) },
    };

    [Theory]
    [MemberData(nameof(PathCases))]
    public async Task Each_unary_method_uses_its_verb_and_path(string method, object body, string verb, string path, string response)
    {
        var handler = new StubHandler(_ => response.Length == 0 ? new HttpResponseMessage(HttpStatusCode.NoContent) : Reply(response));
        await using var transport = Open(handler);

        var result = await transport.SendAsync(Request(method, body), Ct);

        Assert.NotNull(result.Body);
        var message = handler.Requests[0];
        Assert.Equal(verb, message.Method.Method);
        Assert.Equal(path, message.RequestUri!.AbsolutePath);
        Assert.Equal("agent.test", message.RequestUri.Host);
    }

    [Fact]
    public async Task Results_have_the_typed_shape()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("message:send")
            ? Reply("""{"task":""" + TaskJson + "}")
            : Reply(TaskJson));
        await using var transport = Open(handler);

        var sent = await transport.SendAsync(Request("SendMessage", new SendMessageRequest()), Ct);
        var task = await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct);

        Assert.Equal("t1", Assert.IsType<SendMessageResponse>(sent.Body).Task!.Id);
        Assert.Equal("t1", Assert.IsType<AgentTask>(task.Body).Id);
    }

    [Fact]
    public async Task SendMessage_posts_the_request_as_a2a_json()
    {
        var handler = new StubHandler(_ => Reply("""{"task":""" + TaskJson + "}"));
        await using var transport = Open(handler);
        var body = new SendMessageRequest { Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hi")] } };

        await transport.SendAsync(Request("SendMessage", body), Ct);

        Assert.Equal("application/a2a+json", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("m1", json.RootElement.GetProperty("message").GetProperty("messageId").GetString());
    }

    [Theory]
    [InlineData("http://agent.test/rest/", "/rest/tasks/t1")]
    [InlineData("http://agent.test/rest", "/rest/tasks/t1")]
    [InlineData("http://agent.test/", "/tasks/t1")]
    [InlineData("http://agent.test/a/b/c", "/a/b/c/tasks/t1")]
    public async Task Base_url_with_or_without_trailing_slash_and_path_prefix(string baseUrl, string expectedPath)
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
        await using var transport = Open(handler, url: baseUrl);
        await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct);
        Assert.Equal(expectedPath, handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_tenant_in_the_request_becomes_the_first_path_segment_for_every_operation()
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
        await using var transport = Open(handler);

        await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1", Tenant = "acme corp" }), Ct);
        await transport.SendAsync(Request("CancelTask", new CancelTaskRequest { Id = "t1", Tenant = "acme" }), Ct);

        Assert.Equal("/agents/x/rest/acme%20corp/tasks/t1", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/agents/x/rest/acme/tasks/t1:cancel", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Tenant_prefixes_message_send_and_the_stream_paths()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("message:send")
            ? Reply("""{"task":""" + TaskJson + "}")
            : Sse(StatusEvent(TaskState.Completed)));
        await using var transport = Open(handler);

        await transport.SendAsync(Request("SendMessage", new SendMessageRequest { Tenant = "acme" }), Ct);
        await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest { Tenant = "acme" }), Ct)) { }
        await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t1", Tenant = "acme" }), Ct)) { }

        Assert.Equal("/agents/x/rest/acme/message:send", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/agents/x/rest/acme/message:stream", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal((HttpMethod.Post, "/agents/x/rest/acme/message:stream"), (handler.Requests[1].Method, handler.Requests[1].RequestUri!.AbsolutePath));
        Assert.Equal((HttpMethod.Get, "/agents/x/rest/acme/tasks/t1:subscribe"), (handler.Requests[2].Method, handler.Requests[2].RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task List_requests_use_camelCase_percent_encoded_query_and_skip_unset_members()
    {
        var handler = new StubHandler(_ => Reply("""{"tasks":[],"nextPageToken":"","pageSize":5,"totalSize":0}"""));
        await using var transport = Open(handler);

        await transport.SendAsync(Request("ListTasks", new ListTasksRequest
        {
            ContextId = "c 1&x",
            Status = TaskState.Working,
            PageSize = 5,
            PageToken = "tok",
            HistoryLength = 0,
            IncludeArtifacts = true,
            StatusTimestampAfter = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        }), Ct);
        await transport.SendAsync(Request("ListTasks", new ListTasksRequest()), Ct);
        await transport.SendAsync(Request("ListTaskPushNotificationConfigs",
            new ListTaskPushNotificationConfigsRequest { TaskId = "t1", PageSize = 3, PageToken = "p" }), Ct);

        var query = HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query);
        Assert.Equal("c 1&x", query["contextId"]);
        Assert.Equal("TASK_STATE_WORKING", query["status"]);
        Assert.Equal("5", query["pageSize"]);
        Assert.Equal("tok", query["pageToken"]);
        Assert.Equal("0", query["historyLength"]);
        Assert.Equal("true", query["includeArtifacts"]);
        Assert.StartsWith("2026-01-02T03:04:05", query["statusTimestampAfter"]);
        Assert.Empty(handler.Requests[1].RequestUri!.Query);
        var push = HttpUtility.ParseQueryString(handler.Requests[2].RequestUri!.Query);
        Assert.Equal("3", push["pageSize"]);
        Assert.Equal("p", push["pageToken"]);
    }

    [Fact]
    public async Task GetTask_sends_historyLength_as_a_query_parameter()
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
        await using var transport = Open(handler);
        await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1", HistoryLength = 2 }), Ct);
        Assert.Equal("?historyLength=2", handler.Requests[0].RequestUri!.Query);
    }

    // ---------- headers ----------

    [Fact]
    public async Task Headers_version_accept_extra_headers_authorization_and_call_info()
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
        await using var transport = Open(handler);
        var headers = new A2ARequestHeaders { ["X-Trace"] = "abc" };
        headers.SetAuthorization("Bearer secret");
        var request = Request("GetTask", new GetTaskRequest { Id = "t1" }, headers: headers);

        await transport.SendAsync(request, Ct);

        var message = handler.Requests[0];
        Assert.Equal("1.0", Assert.Single(message.Headers.GetValues("A2A-Version")));
        Assert.Equal("application/a2a+json", Assert.Single(message.Headers.GetValues("Accept")));
        Assert.Equal("abc", Assert.Single(message.Headers.GetValues("X-Trace")));
        Assert.Equal("Bearer secret", message.Headers.Authorization!.ToString());
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
    public async Task A_header_the_HttpClient_refuses_throws_naming_the_header_not_its_value()
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
        await using var transport = Open(handler);
        var headers = new A2ARequestHeaders { ["Content-Length"] = "secret-value" };
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }, headers: headers), Ct));
        Assert.Contains("Content-Length", ex.Message);
        Assert.DoesNotContain("secret-value", ex.Message);
    }

    // ---------- errors ----------

    [Fact]
    public async Task An_error_body_maps_by_ErrorInfo_reason_and_keeps_the_message()
    {
        await using var transport = Open(new StubHandler(_ => Reply(NotFoundError, HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t" }), Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
        Assert.Equal("no such task", ex.Message);
    }

    [Fact]
    public async Task ErrorInfo_metadata_becomes_exception_data()
    {
        const string body = """{"error":{"code":400,"status":"FAILED_PRECONDITION","message":"bad version","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"VERSION_NOT_SUPPORTED","domain":"a2a-protocol.org","metadata":{"supportedVersions":"1.0"}}]}}""";
        await using var transport = Open(new StubHandler(_ => Reply(body, HttpStatusCode.BadRequest)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t" }), Ct));
        Assert.Equal(A2AErrorCode.VersionNotSupported, ex.Code);
        Assert.Equal("1.0", ex.Data!.Value.GetProperty("supportedVersions").GetString());
    }

    [Theory]
    [InlineData(400, A2AErrorCode.InvalidParams)]
    [InlineData(404, A2AErrorCode.MethodNotFound)]
    [InlineData(500, A2AErrorCode.InternalError)]
    public async Task An_error_body_without_a_reason_is_mapped_by_http_status(int status, A2AErrorCode code)
    {
        var body = $$"""{"error":{"code":{{status}},"status":"X","message":"boom"} }""";
        await using var transport = Open(new StubHandler(_ => Reply(body, (HttpStatusCode)status)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t" }), Ct));
        Assert.Equal(code, ex.Code);
        Assert.Contains("boom", ex.Message);
        Assert.Contains(status.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    public async Task A_non_2xx_without_an_error_body_is_a_transport_exception(int status, bool transient)
    {
        await using var transport = Open(new StubHandler(_ => Reply("<html>nope</html>", (HttpStatusCode)status, "text/html")));
        var ex = await Assert.ThrowsAsync<A2ATransportException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal(transient, ex.IsTransient);
        Assert.Contains("billing", ex.Message);
    }

    [Fact]
    public async Task An_error_body_over_MaxErrorBodyBytes_names_the_option()
    {
        var handler = new StubHandler(_ => Reply(new string('x', 100_000), HttpStatusCode.InternalServerError));
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
        var handler = new StubHandler(_ => Reply(new string(' ', 5000) + TaskJson));
        await using var transport = Open(handler, new A2AClientLimits { MaxResponseBytes = 1024 });
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Contains("A2AClientOptions.Limits.MaxResponseBytes", ex.Message);
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

    [Theory]
    [InlineData("""{"task":{},"message":{}}""")]
    [InlineData("""{}""")]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task A_malformed_success_body_is_an_invalid_agent_response(string body)
    {
        await using var transport = Open(new StubHandler(_ => Reply(body)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("SendMessage", new SendMessageRequest()), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task SendMessage_result_with_both_task_and_message_is_rejected()
    {
        var both = JsonSerializer.Serialize(new SendMessageResponse { Task = Task1, Message = new Message { MessageId = "m", Role = Role.Agent } }, A2AJson.Options);
        await using var transport = Open(new StubHandler(_ => Reply(both)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
            await transport.SendAsync(Request("SendMessage", new SendMessageRequest()), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task Delete_ignores_the_body_of_a_success_response()
    {
        await using var transport = Open(new StubHandler(_ => Reply("{}")));
        var response = await transport.SendAsync(
            Request("DeleteTaskPushNotificationConfig", new DeleteTaskPushNotificationConfigRequest { TaskId = "t1", Id = "p1" }), Ct);
        Assert.NotNull(response.Body);
    }

    [Fact]
    public async Task An_unknown_method_is_an_argument_error()
    {
        await using var transport = Open(new StubHandler(_ => Reply(TaskJson)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await transport.SendAsync(Request("Nope", new GetTaskRequest()), Ct));
    }

    // ---------- OnHeaders ----------

    [Fact]
    public async Task OnHeaders_gets_extensions_status_and_version_for_unary_calls()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Reply(TaskJson);
            response.Headers.TryAddWithoutValidation("A2A-Extensions", "https://x.test/a, https://x.test/b ,");
            return response;
        });
        await using var transport = Open(handler);
        A2AResponseInfo? seen = null;
        var response = await transport.SendAsync(Request("GetTask", new GetTaskRequest { Id = "t1" }, onHeaders: i => seen = i), Ct);

        Assert.NotNull(seen);
        Assert.Equal(["https://x.test/a", "https://x.test/b"], seen.ActivatedExtensions);
        Assert.Equal(200, seen.HttpStatus);
        Assert.Equal("HTTP+JSON", seen.Transport);
        Assert.Equal("1.0", seen.ProtocolVersion);
        Assert.Equal(seen.ActivatedExtensions, response.Info.ActivatedExtensions);
    }

    // ---------- streams ----------

    [Fact]
    public async Task Stream_yields_events_skips_heartbeats_and_calls_OnHeaders_before_the_first_event()
    {
        var handler = new StubHandler(_ => Sse(":heartbeat", StatusEvent(TaskState.Working), "   ", StatusEvent(TaskState.Completed)));
        await using var transport = Open(handler);
        var order = new List<string>();
        var request = Request("SendStreamingMessage", new SendMessageRequest(), onHeaders: _ => order.Add("headers"));

        await foreach (var evt in transport.StreamAsync(request, Ct)) order.Add(evt.StatusUpdate!.Status.State.ToString());

        Assert.Equal(["headers", "Working", "Completed"], order);
    }

    [Fact]
    public async Task Subscribe_streams_with_GET_and_no_body()
    {
        var handler = new StubHandler(_ => Sse(StatusEvent(TaskState.Completed)));
        await using var transport = Open(handler);
        await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t1" }), Ct)) { }
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Null(handler.Requests[0].Content);
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
    public async Task A_stream_event_with_no_payload_is_rejected()
    {
        await using var transport = Open(new StubHandler(_ => Sse("{}")));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task A_stream_event_that_is_not_JSON_is_rejected()
    {
        await using var transport = Open(new StubHandler(_ => Sse("not json")));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task An_error_frame_after_the_first_event_throws_the_mapped_exception()
    {
        var handler = new StubHandler(_ => Sse(StatusEvent(TaskState.Working), NotFoundError));
        await using var transport = Open(handler);
        var received = new List<StreamResponse>();
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var e in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) received.Add(e);
        });
        Assert.Single(received);
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public async Task An_in_stream_error_without_a_reason_is_mapped_by_its_code_and_does_not_mention_HTTP_200()
    {
        var handler = new StubHandler(_ => Sse(NoReasonError));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
        Assert.Equal("bad params", ex.Message);
    }

    [Fact]
    public async Task A_pre_stream_error_response_is_thrown()
    {
        var handler = new StubHandler(_ => Reply(NotFoundError, HttpStatusCode.NotFound));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t" }), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public async Task A_pre_stream_html_error_is_a_transport_exception()
    {
        var handler = new StubHandler(_ => Reply("<html/>", HttpStatusCode.BadGateway, "text/html"));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2ATransportException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SubscribeToTask", new SubscribeToTaskRequest { Id = "t" }), Ct)) { }
        });
        Assert.Equal(502, ex.HttpStatus);
    }

    [Fact]
    public async Task A_stream_request_answered_with_401_is_an_auth_exception()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await using var transport = Open(handler);
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () =>
        {
            await foreach (var _ in transport.StreamAsync(Request("SendStreamingMessage", new SendMessageRequest()), Ct)) { }
        });
        Assert.Equal(401, ex.HttpStatus);
    }

    [Fact]
    public async Task A_2xx_JSON_answer_to_a_stream_request_is_a_protocol_violation()
    {
        var handler = new StubHandler(_ => Reply(TaskJson));
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
        var big = JsonSerializer.Serialize(new StreamResponse { Message = new Message { MessageId = new string('m', 5000), Role = Role.Agent } }, A2AJson.Options);
        await using var transport = Open(new StubHandler(_ => Sse(big)), new A2AClientLimits { MaxEventBytes = 1024 });
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

    // ---------- factory ----------

    [Fact]
    public void Factory_declares_the_HTTP_JSON_binding_at_1_0()
    {
        var factory = new RestTransportFactory();
        Assert.Equal(["HTTP+JSON"], factory.Bindings);
        Assert.Equal(["1.0"], factory.ProtocolVersions);
    }

    [Fact]
    public void Factory_rejects_a_relative_interface_url()
    {
        var card = Cards.Rest("relative/path");
        var http = new HttpClient(new StubHandler(_ => Reply(TaskJson)));
        var ex = Assert.Throws<ArgumentException>(() =>
            new RestTransportFactory().Create(card.SupportedInterfaces[0], card, ScriptedTransportFactory.NewContext(http)));
        Assert.Contains("default", ex.Message);
    }

    [Fact]
    public async Task Disposing_the_transport_does_not_dispose_the_HttpClient()
    {
        var http = new HttpClient(new StubHandler(_ => Reply(TaskJson)));
        var card = Cards.Rest("http://agent.test/rest");
        var transport = new RestTransportFactory().Create(card.SupportedInterfaces[0], card, ScriptedTransportFactory.NewContext(http));
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
