using System.Net;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Grpc;
using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Client.Transports;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Integration.Tests.Client;

/// <summary>REST wire details, the Task 11 carry-overs and gRPC response validation, against stubs and a misbehaving server.</summary>
public sealed class ClientHardeningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            Bodies.Add(body);
            return respond(request, body);
        }
    }

    private sealed class FixedCredentials(IReadOnlyDictionary<string, string> headers) : IA2ACredentialProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct) => new(headers);
    }

    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/a2a+json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static RestA2AClient Rest(StubHandler handler, IA2ACredentialProvider? credentials = null)
        => new(new Uri("http://stub.test/agents/x/rest/"), new AgentCard { Name = "stub" },
            new A2AClientOptions { HttpClient = new HttpClient(handler), Credentials = credentials, Extensions = ["urn:a", "urn:b"] });

    private static SendMessageRequest Send(string text) => new()
    {
        Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText(text)] },
    };

    private const string TaskJson = """{"id":"t1","contextId":"c","status":{"state":"TASK_STATE_COMPLETED"}}""";

    // ---------- carried Task 11 items ----------

    [Fact]
    public async Task Refused_credential_header_throws_naming_the_header_not_the_value()
    {
        var creds = new FixedCredentials(new Dictionary<string, string> { ["Bad Name"] = "s3cret-value" });

        await using var rest = Rest(new StubHandler((_, _) => Reply(TaskJson)), creds);
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await rest.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct));
        Assert.Contains("Bad Name", ex.Message);
        Assert.DoesNotContain("s3cret-value", ex.Message);

        await using var rpc = new JsonRpcA2AClient(new Uri("http://stub.test/jsonrpc"), new AgentCard { Name = "stub" },
            new A2AClientOptions { HttpClient = new HttpClient(new StubHandler((_, _) => Reply(TaskJson))), Credentials = creds });
        var ex2 = await Assert.ThrowsAsync<ArgumentException>(async () => await rpc.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct));
        Assert.DoesNotContain("s3cret-value", ex2.Message);

        await using var grpc = new GrpcA2AClient(new Uri("http://127.0.0.1:1"), new AgentCard { Name = "stub" }, new A2AClientOptions { Credentials = creds });
        var ex3 = await Assert.ThrowsAsync<ArgumentException>(async () => await grpc.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct));
        Assert.Contains("Bad Name", ex3.Message);
        Assert.DoesNotContain("s3cret-value", ex3.Message);
    }

    [Fact]
    public async Task Empty_sse_data_payloads_are_skipped()
    {
        const string rpcFrames = "data:\n\ndata: \n\n"
            + """data: {"jsonrpc":"2.0","id":1,"result":{"message":{"messageId":"m","role":"ROLE_AGENT","parts":[{"text":"hi"}]}}}""" + "\n\n";
        await using var rpc = new JsonRpcA2AClient(new Uri("http://stub.test/jsonrpc"), new AgentCard { Name = "stub" },
            new A2AClientOptions { HttpClient = new HttpClient(new StubHandler((_, _) => Reply(rpcFrames, contentType: "text/event-stream"))) });
        var fromRpc = new List<StreamResponse>();
        await foreach (var e in rpc.SendStreamingMessageAsync(Send("x"), Ct)) fromRpc.Add(e);
        Assert.Single(fromRpc);

        const string restFrames = "data:\n\ndata: \n\n"
            + """data: {"message":{"messageId":"m","role":"ROLE_AGENT","parts":[{"text":"hi"}]}}""" + "\n\n";
        await using var rest = Rest(new StubHandler((_, _) => Reply(restFrames, contentType: "text/event-stream")));
        var fromRest = new List<StreamResponse>();
        await foreach (var e in rest.SendStreamingMessageAsync(Send("x"), Ct)) fromRest.Add(e);
        Assert.Single(fromRest);
    }

    [Fact]
    public void Deserialize_maps_any_converter_failure_to_invalid_agent_response()
    {
        // Guid has no registered type info: the serializer throws NotSupportedException, not JsonException.
        var ex = Assert.Throws<A2AException>(() => ClientSupport.Deserialize<Guid>(JsonSerializer.SerializeToElement("x"), "thing"));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    // ---------- REST wire details ----------

    [Fact]
    public async Task Rest_requests_use_camelCase_query_percent_encoded_ids_and_headers()
    {
        var handler = new StubHandler((req, _) => Reply(req.RequestUri!.AbsolutePath.EndsWith("/tasks")
            ? """{"tasks":[],"nextPageToken":"","pageSize":5,"totalSize":0}""" : TaskJson));
        await using var client = Rest(handler, new FixedCredentials(new Dictionary<string, string> { ["X-API-Key"] = "k" }));

        await client.ListTasksAsync(new ListTasksRequest
        {
            ContextId = "c 1&x",
            Status = TaskState.Working,
            PageSize = 5,
            PageToken = "tok",
            HistoryLength = 0,
            IncludeArtifacts = true,
            StatusTimestampAfter = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        }, Ct);
        var list = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, list.Method);
        Assert.Equal("/agents/x/rest/tasks", list.RequestUri!.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(list.RequestUri.Query);
        Assert.Equal("c 1&x", query["contextId"]);
        Assert.Equal("TASK_STATE_WORKING", query["status"]);
        Assert.Equal("5", query["pageSize"]);
        Assert.Equal("tok", query["pageToken"]);
        Assert.Equal("0", query["historyLength"]);
        Assert.Equal("true", query["includeArtifacts"]);
        Assert.StartsWith("2026-01-02T03:04:05", query["statusTimestampAfter"]);
        Assert.Equal("1.0", list.Headers.GetValues("A2A-Version").Single());
        Assert.Equal("urn:a,urn:b", list.Headers.GetValues("A2A-Extensions").Single());
        Assert.Equal("k", list.Headers.GetValues("X-API-Key").Single());

        handler.Requests.Clear();
        await client.GetTaskAsync(new GetTaskRequest { Id = "a b/c:d" }, Ct);
        Assert.Equal("/agents/x/rest/tasks/a%20b%2Fc%3Ad", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Empty(handler.Requests[0].RequestUri!.Query);
    }

    [Theory]
    [InlineData("http://stub.test/rest/", "/rest/tasks/t1")]
    [InlineData("http://stub.test/rest", "/rest/tasks/t1")]
    [InlineData("http://stub.test/", "/tasks/t1")]
    [InlineData("http://stub.test/a/b/c", "/a/b/c/tasks/t1")]
    public async Task Rest_base_url_with_or_without_trailing_slash_and_path_prefix(string baseUrl, string expectedPath)
    {
        var handler = new StubHandler((_, _) => Reply(TaskJson));
        await using var client = new RestA2AClient(new Uri(baseUrl), new AgentCard { Name = "stub" },
            new A2AClientOptions { HttpClient = new HttpClient(handler) });
        await client.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct);
        Assert.Equal(expectedPath, handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Rest_uses_the_documented_verbs_and_paths()
    {
        var handler = new StubHandler((req, _) => req.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Reply(req.RequestUri!.AbsolutePath.EndsWith("pushNotificationConfigs") && req.Method == HttpMethod.Post
                ? """{"taskId":"t 1","id":"c1","url":"http://h"}""" : TaskJson));
        await using var client = Rest(handler);

        await client.CancelTaskAsync(new CancelTaskRequest { Id = "t 1" }, Ct);
        await client.CreateTaskPushNotificationConfigAsync(new TaskPushNotificationConfig { TaskId = "t 1", Url = "http://h" }, Ct);
        await client.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = "t 1", Id = "c 1" }, Ct);

        Assert.Equal((HttpMethod.Post, "/agents/x/rest/tasks/t%201:cancel"), (handler.Requests[0].Method, handler.Requests[0].RequestUri!.AbsolutePath));
        Assert.Equal((HttpMethod.Post, "/agents/x/rest/tasks/t%201/pushNotificationConfigs"), (handler.Requests[1].Method, handler.Requests[1].RequestUri!.AbsolutePath));
        Assert.Equal("http://h", JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("url").GetString());
        Assert.Equal((HttpMethod.Delete, "/agents/x/rest/tasks/t%201/pushNotificationConfigs/c%201"), (handler.Requests[2].Method, handler.Requests[2].RequestUri!.AbsolutePath));
        Assert.Equal("application/a2a+json", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Rest_error_body_maps_by_reason_else_by_http_status_keeping_status_and_message()
    {
        const string notFound = """{"error":{"code":404,"status":"NOT_FOUND","message":"no such task","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"TASK_NOT_FOUND","domain":"a2a-protocol.org"}]}}""";
        await using var withReason = Rest(new StubHandler((_, _) => Reply(notFound, HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await withReason.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
        Assert.Equal("no such task", ex.Message);

        const string versionBody = """{"error":{"code":400,"status":"FAILED_PRECONDITION","message":"bad version","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"VERSION_NOT_SUPPORTED","domain":"a2a-protocol.org","metadata":{"supportedVersions":"1.0"}}]}}""";
        await using var version = Rest(new StubHandler((_, _) => Reply(versionBody, HttpStatusCode.BadRequest)));
        var vex = await Assert.ThrowsAsync<A2AException>(async () => await version.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.VersionNotSupported, vex.Code);
        Assert.Equal("1.0", vex.Data!.Value.GetProperty("supportedVersions").GetString());

        const string noReason = """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"bad params"}}""";
        await using var plain = Rest(new StubHandler((_, _) => Reply(noReason, HttpStatusCode.BadRequest)));
        var pex = await Assert.ThrowsAsync<A2AException>(async () => await plain.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.InvalidParams, pex.Code); // 400 without a reason
        Assert.Contains("bad params", pex.Message);
        Assert.Contains("400", pex.Message);

        const string noMethod = """{"error":{"code":404,"status":"NOT_FOUND","message":"no route"}}""";
        await using var missing = Rest(new StubHandler((_, _) => Reply(noMethod, HttpStatusCode.NotFound)));
        var mex = await Assert.ThrowsAsync<A2AException>(async () => await missing.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.MethodNotFound, mex.Code); // 404 without a reason

        const string internalBody = """{"error":{"code":500,"status":"INTERNAL","message":"boom"}}""";
        await using var broken = Rest(new StubHandler((_, _) => Reply(internalBody, HttpStatusCode.InternalServerError)));
        Assert.Equal(A2AErrorCode.InternalError,
            (await Assert.ThrowsAsync<A2AException>(async () => await broken.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct))).Code);

        // In-stream error frame without a reason: the body's code decides.
        await using var frame = Rest(new StubHandler((_, _) => Reply("data: " + noReason + "\n\n", contentType: "text/event-stream")));
        var fex = await Assert.ThrowsAsync<A2AException>(async () => { await foreach (var _ in frame.SendStreamingMessageAsync(Send("x"), Ct)) { } });
        Assert.Equal(A2AErrorCode.InvalidParams, fex.Code);

        await using var html = Rest(new StubHandler((_, _) => Reply("<html/>", HttpStatusCode.BadGateway, "text/html")));
        var hex = await Assert.ThrowsAsync<A2AException>(async () => await html.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.InternalError, hex.Code);
        Assert.Contains("502", hex.Message);

        await using var denied = Rest(new StubHandler((_, _) => Reply("", HttpStatusCode.Unauthorized, "text/plain")));
        var auth = await Assert.ThrowsAsync<A2AAuthException>(async () => await denied.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(401, auth.HttpStatus);
    }

    [Fact]
    public async Task Rest_stream_errors_before_and_after_start()
    {
        const string errorJson = """{"error":{"code":404,"status":"NOT_FOUND","message":"gone","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"TASK_NOT_FOUND","domain":"a2a-protocol.org"}]}}""";

        // Before the stream: a plain JSON error response.
        await using var pre = Rest(new StubHandler((_, _) => Reply(errorJson, HttpStatusCode.NotFound)));
        var preEx = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in pre.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }, Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, preEx.Code);

        // After the first event: a final `data: {"error":..}` frame.
        var frames = """data: {"message":{"messageId":"m","role":"ROLE_AGENT","parts":[{"text":"hi"}]}}""" + "\n\n" + "data: " + errorJson + "\n\n";
        await using var post = Rest(new StubHandler((_, _) => Reply(frames, contentType: "text/event-stream")));
        var received = new List<StreamResponse>();
        var postEx = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var e in post.SendStreamingMessageAsync(Send("x"), Ct)) received.Add(e);
        });
        Assert.Single(received);
        Assert.Equal(A2AErrorCode.TaskNotFound, postEx.Code);

        // An in-stream error without a reason does not mention HTTP 200.
        const string plainFrame = "data: {\"error\":{\"code\":500,\"status\":\"INTERNAL\",\"message\":\"kaput\"}}\n\n";
        await using var plainPost = Rest(new StubHandler((_, _) => Reply(plainFrame, contentType: "text/event-stream")));
        var plainEx = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in plainPost.SendStreamingMessageAsync(Send("x"), Ct)) { }
        });
        Assert.Equal("kaput", plainEx.Message);

        // A 200 that is not an event stream is a protocol violation.
        await using var notStream = Rest(new StubHandler((_, _) => Reply(TaskJson)));
        var vio = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in notStream.SendStreamingMessageAsync(Send("x"), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, vio.Code);
    }

    [Theory]
    [InlineData("""{"task":{},"message":{}}""")]
    [InlineData("""{}""")]
    [InlineData("nonsense")]
    public async Task Rest_malformed_success_body_is_invalid_agent_response(string body)
    {
        await using var client = Rest(new StubHandler((_, _) => Reply(body)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await client.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    // ---------- gRPC against a server that misbehaves on purpose ----------

    private sealed class BadService : Proto.A2AService.A2AServiceBase
    {
        public static Metadata? LastHeaders;

        public override Task<Proto.SendMessageResponse> SendMessage(Proto.SendMessageRequest request, ServerCallContext context)
        {
            LastHeaders = context.RequestHeaders;
            return Task.FromResult(new Proto.SendMessageResponse()); // no payload
        }

        public override Task<Proto.Task> GetTask(Proto.GetTaskRequest request, ServerCallContext context)
            => request.Id switch
            {
                "nostatus" => Task.FromResult(new Proto.Task { Id = "nostatus" }),
                "unauth" => throw new RpcException(new Status(StatusCode.Unauthenticated, "who are you")),
                "denied" => throw new RpcException(new Status(StatusCode.PermissionDenied, "no")),
                "invalid" => throw new RpcException(new Status(StatusCode.InvalidArgument, "bad params")),
                "unimplemented" => throw new RpcException(new Status(StatusCode.Unimplemented, "no such method")),
                _ => throw new RpcException(new Status(StatusCode.Unavailable, "overloaded")),
            };

        public override Task SendStreamingMessage(Proto.SendMessageRequest request, IServerStreamWriter<Proto.StreamResponse> responseStream,
            ServerCallContext context)
            => responseStream.WriteAsync(new Proto.StreamResponse()); // no payload

        public override async Task SubscribeToTask(Proto.SubscribeToTaskRequest request, IServerStreamWriter<Proto.StreamResponse> responseStream,
            ServerCallContext context)
        {
            if (request.Id == "early")
            {
                // A valid WORKING snapshot, then the stream closes without a final event.
                await responseStream.WriteAsync(new Proto.StreamResponse
                {
                    Task = new Proto.Task { Id = "early", ContextId = "c", Status = new Proto.TaskStatus { State = Proto.TaskState.Working } },
                });
                return;
            }
            await responseStream.WriteAsync(new Proto.StreamResponse { StatusUpdate = new Proto.TaskStatusUpdateEvent { TaskId = "t" } }); // no status
        }
    }

    private static async Task<(WebApplication App, Uri Address)> StartBadServerAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        var app = builder.Build();
        app.MapGrpcService<BadService>();
        await app.StartAsync();
        return (app, new Uri(app.Urls.First()));
    }

    [Fact]
    public async Task Grpc_missing_payloads_are_invalid_agent_response_and_statuses_map()
    {
        var (app, address) = await StartBadServerAsync();
        await using var _ = app;
        await using var client = new GrpcA2AClient(address, new AgentCard { Name = "bad" }, new A2AClientOptions
        {
            Extensions = ["urn:a", "urn:b"],
            Credentials = new FixedCredentials(new Dictionary<string, string> { ["X-API-Key"] = "k", ["Authorization"] = "Bearer t" }),
        });

        var noPayload = await Assert.ThrowsAsync<A2AException>(async () => await client.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, noPayload.Code);
        Assert.Equal("1.0", BadService.LastHeaders!.GetValue("a2a-version"));
        Assert.Equal("urn:a,urn:b", BadService.LastHeaders.GetValue("a2a-extensions"));
        Assert.Equal("k", BadService.LastHeaders.GetValue("x-api-key"));
        Assert.Equal("Bearer t", BadService.LastHeaders.GetValue("authorization"));

        var noStatus = await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "nostatus" }, Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, noStatus.Code);

        var emptyEvent = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.SendStreamingMessageAsync(Send("x"), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, emptyEvent.Code);

        var noEventStatus = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }, Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, noEventStatus.Code);

        Assert.Equal(401, (await Assert.ThrowsAsync<A2AAuthException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "unauth" }, Ct))).HttpStatus);
        Assert.Equal(403, (await Assert.ThrowsAsync<A2AAuthException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "denied" }, Ct))).HttpStatus);

        var other = await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "other" }, Ct));
        Assert.Equal(A2AErrorCode.InternalError, other.Code);
        Assert.Contains("overloaded", other.Message);

        // No ErrorInfo reason: INVALID_ARGUMENT → InvalidParams, UNIMPLEMENTED → MethodNotFound (same as REST 400 / 404).
        Assert.Equal(A2AErrorCode.InvalidParams,
            (await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "invalid" }, Ct))).Code);
        Assert.Equal(A2AErrorCode.MethodNotFound,
            (await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "unimplemented" }, Ct))).Code);

        // A stream that closes before a final event.
        var events = new List<StreamResponse>();
        var early = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var e in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "early" }, Ct)) events.Add(e);
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, early.Code);
        Assert.Contains("stream ended before a final event", early.Message);
        Assert.Equal(TaskState.Working, Assert.Single(events).Task!.Status.State);
    }

    // ---------- streams that end without a final event ----------

    private const string WorkingTaskJson = """{"id":"t1","contextId":"c","status":{"state":"TASK_STATE_WORKING"}}""";

    public static TheoryData<string, bool> StreamEndings => new()
    {
        { "WORKING task only", false },
        { "WORKING status update", false },
        { "COMPLETED status update", true },
        { "INPUT_REQUIRED task", true },
        { "single message", true },
    };

    private static string RestFrames(string ending) => ending switch
    {
        "WORKING task only" => $"data: {{\"task\":{WorkingTaskJson}}}\n\n",
        "WORKING status update" => $"data: {{\"task\":{WorkingTaskJson}}}\n\n" +
                                   """data: {"statusUpdate":{"taskId":"t1","contextId":"c","status":{"state":"TASK_STATE_WORKING"}}}""" + "\n\n",
        "COMPLETED status update" => $"data: {{\"task\":{WorkingTaskJson}}}\n\n" +
                                     """data: {"statusUpdate":{"taskId":"t1","contextId":"c","status":{"state":"TASK_STATE_COMPLETED"}}}""" + "\n\n",
        "INPUT_REQUIRED task" => """data: {"task":{"id":"t1","contextId":"c","status":{"state":"TASK_STATE_INPUT_REQUIRED"}}}""" + "\n\n",
        "single message" => """data: {"message":{"messageId":"m","role":"ROLE_AGENT","parts":[{"text":"hi"}]}}""" + "\n\n",
        _ => throw new ArgumentOutOfRangeException(nameof(ending)),
    };

    /// <summary>The same frames wrapped as JSON-RPC results.</summary>
    private static string JsonRpcFrames(string ending)
        => string.Concat(RestFrames(ending).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(f => $"data: {{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{f["data: ".Length..]}}}\n\n"));

    private static async Task<Exception?> DrainAsync(IAsyncEnumerable<StreamResponse> stream)
    {
        try
        {
            await foreach (var _ in stream) { }
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Theory]
    [MemberData(nameof(StreamEndings))]
    public async Task Rest_and_jsonrpc_streams_must_end_with_a_final_event(string ending, bool ok)
    {
        await using var rest = Rest(new StubHandler((_, _) => Reply(RestFrames(ending), contentType: "text/event-stream")));
        await using var rpc = new JsonRpcA2AClient(new Uri("http://stub.test/jsonrpc"), new AgentCard { Name = "stub" }, new A2AClientOptions
        {
            HttpClient = new HttpClient(new StubHandler((_, _) => Reply(JsonRpcFrames(ending), contentType: "text/event-stream"))),
        });

        foreach (var stream in new[] { rest.SendStreamingMessageAsync(Send("x"), Ct), rpc.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t1" }, Ct) })
        {
            var error = await DrainAsync(stream);
            if (ok)
            {
                Assert.Null(error);
            }
            else
            {
                var a2a = Assert.IsType<A2AException>(error);
                Assert.Equal(A2AErrorCode.InvalidAgentResponse, a2a.Code);
                Assert.Contains("stream ended before a final event", a2a.Message);
            }
        }
    }
}
