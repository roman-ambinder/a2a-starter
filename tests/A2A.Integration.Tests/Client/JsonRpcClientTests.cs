using System.Net;
using System.Text;
using System.Text.Json;
using A2A.Client;
using A2A.Client.Auth;
using A2A.Client.Transports;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Client;

[Collection(MinimalServerCollection.Name)]
public sealed class JsonRpcClientTests(MinimalServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestUserCredentials(string? user, string? roles = null) : IA2ACredentialProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
        {
            var headers = new Dictionary<string, string>();
            if (user is not null) headers[MinimalServer.UserHeader] = user;
            if (roles is not null) headers[MinimalServer.RolesHeader] = roles;
            return new(headers);
        }
    }

    private async Task<JsonRpcA2AClient> CreateAsync(string agent = MinimalServer.EchoAgentName, IA2ACredentialProvider? credentials = null,
        IReadOnlyList<string>? extensions = null, string version = "1.0")
    {
        Assert.NotNull(server.App); // fixture started
        using var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{MinimalServer.BaseUrl}/agents/{agent}"), Ct);
        var endpoint = new Uri($"{MinimalServer.BaseUrl}/agents/{agent}/jsonrpc");
        return new JsonRpcA2AClient(endpoint, card, new A2AClientOptions
        {
            Credentials = credentials,
            Extensions = extensions ?? [],
            ProtocolVersion = version,
        });
    }

    private static SendMessageRequest Send(string text, bool returnImmediately = false) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(text)] },
        Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
    };

    [Fact]
    public async Task All_eleven_operations_work_over_jsonrpc()
    {
        await using var client = await CreateAsync(credentials: new TestUserCredentials("alice"));

        // 1 SendMessage
        var sent = await client.SendMessageAsync(Send("hello"), Ct);
        var task = Assert.IsType<AgentTask>(sent.Task);
        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal("echo: hello", task.Artifacts[0].Parts[0].Text);

        // 2 SendStreamingMessage
        var events = new List<StreamResponse>();
        await foreach (var e in client.SendStreamingMessageAsync(Send("stream"), Ct)) events.Add(e);
        Assert.NotNull(events[0].Task);
        Assert.Contains(events, e => e.ArtifactUpdate is not null);
        Assert.Equal(TaskState.Completed, events[^1].StatusUpdate!.Status.State);

        // 3 GetTask
        var got = await client.GetTaskAsync(new GetTaskRequest { Id = task.Id }, Ct);
        Assert.Equal(task.Id, got.Id);

        // 4 ListTasks
        var list = await client.ListTasksAsync(new ListTasksRequest { ContextId = task.ContextId }, Ct);
        Assert.Contains(list.Tasks, t => t.Id == task.Id);

        // 5 CancelTask: a slow task, left running
        var slow = (await client.SendMessageAsync(Send("slow:5000", returnImmediately: true), Ct)).Task!;
        var canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = slow.Id }, Ct);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);

        // 6 SubscribeToTask: a second slow task, subscribe while it runs
        var slow2 = (await client.SendMessageAsync(Send("slow:300", returnImmediately: true), Ct)).Task!;
        var sub = new List<StreamResponse>();
        await foreach (var e in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = slow2.Id }, Ct)) sub.Add(e);
        Assert.NotNull(sub[0].Task);

        // 7-10 push config CRUD
        var created = await client.CreateTaskPushNotificationConfigAsync(
            new TaskPushNotificationConfig { TaskId = task.Id, Url = "http://127.0.0.1:1/hook" }, Ct);
        Assert.False(string.IsNullOrEmpty(created.Id));
        var fetched = await client.GetTaskPushNotificationConfigAsync(
            new GetTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, Ct);
        Assert.Equal("http://127.0.0.1:1/hook", fetched.Url);
        var configs = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, Ct);
        Assert.Single(configs.Configs);
        await client.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, Ct);
        var after = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, Ct);
        Assert.Empty(after.Configs);

        // 11 GetExtendedAgentCard
        var card = await client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct);
        Assert.Equal("Extended echo card", card.Description);
    }

    [Fact]
    public async Task Protocol_error_surfaces_as_A2AException_with_code()
    {
        await using var client = await CreateAsync();

        var unary = await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "nope" }, Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, unary.Code);

        // Pre-stream error: plain JSON-RPC response instead of an event stream.
        var stream = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "nope" }, Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, stream.Code);
    }

    [Fact]
    public async Task Unsupported_version_surfaces_as_VersionNotSupported()
    {
        await using var client = await CreateAsync(version: "9.9");
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await client.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(A2AErrorCode.VersionNotSupported, ex.Code);
    }

    [Fact]
    public async Task Unauthorized_surfaces_as_A2AAuthException_401()
    {
        await using var client = await CreateAsync(MinimalServer.ProtectedAgentName);
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await client.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(401, ex.HttpStatus);

        // Authenticated but without the role → 403.
        await using var noRole = await CreateAsync(MinimalServer.ProtectedAgentName, new TestUserCredentials("bob"));
        var forbidden = await Assert.ThrowsAsync<A2AAuthException>(async () => await noRole.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(403, forbidden.HttpStatus);

        // With the role it works.
        await using var ok = await CreateAsync(MinimalServer.ProtectedAgentName, new TestUserCredentials("bob", MinimalServer.InvokerRole));
        Assert.NotNull((await ok.SendMessageAsync(Send("x"), Ct)).Task);
    }

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

    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/a2a+json")
        => new(status) { Content = new StringContent(json, Encoding.UTF8, contentType) };

    private static JsonRpcA2AClient Stubbed(StubHandler handler, A2AClientOptions? options = null)
        => new(new Uri("http://stub.test/jsonrpc"), new AgentCard { Name = "stub" },
            new A2AClientOptions
            {
                HttpClient = new HttpClient(handler),
                Credentials = options?.Credentials,
                Extensions = options?.Extensions ?? [],
            });

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"task":{},"message":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":"nonsense"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    [InlineData("not json at all")]
    public async Task Malformed_server_response_throws_invalid_agent_response(string body)
    {
        await using var client = Stubbed(new StubHandler((_, _) => Reply(body)));
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await client.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task Stream_frame_with_two_payloads_is_invalid_agent_response()
    {
        const string frame = """data: {"jsonrpc":"2.0","id":1,"result":{"task":{},"message":{}}}""" + "\n\n";
        await using var client = Stubbed(new StubHandler((_, _) => Reply(frame, contentType: "text/event-stream")));
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.SendStreamingMessageAsync(Send("x"), Ct)) { }
        });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public async Task Error_frame_after_stream_start_surfaces_as_A2AException()
    {
        const string frames =
            """data: {"jsonrpc":"2.0","id":1,"result":{"message":{"messageId":"m","role":"ROLE_AGENT","parts":[{"text":"hi"}]}}}""" + "\n\n" +
            """data: {"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"gone","data":{"x":1}}}""" + "\n\n";
        await using var client = Stubbed(new StubHandler((_, _) => Reply(frames, contentType: "text/event-stream")));

        var received = new List<StreamResponse>();
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var e in client.SendStreamingMessageAsync(Send("x"), Ct)) received.Add(e);
        });

        Assert.Single(received);
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
        Assert.Equal("gone", ex.Message);
        Assert.Equal(1, ex.Data!.Value.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Error_mapping_for_unknown_codes_and_plain_http_failures()
    {
        await using var unknown = Stubbed(new StubHandler((_, _) =>
            Reply("""{"jsonrpc":"2.0","id":1,"error":{"code":-31999,"message":"weird"}}""")));
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await unknown.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.InternalError, ex.Code);
        Assert.Contains("-31999", ex.Message);

        await using var http500 = Stubbed(new StubHandler((_, _) => Reply("boom", HttpStatusCode.BadGateway, "text/plain")));
        var ex2 = await Assert.ThrowsAsync<A2AException>(async () => await http500.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Equal(A2AErrorCode.InternalError, ex2.Code);
        Assert.Contains("502", ex2.Message);

        await using var http413 = Stubbed(new StubHandler((_, _) => Reply("", HttpStatusCode.RequestEntityTooLarge, "text/plain")));
        var ex3 = await Assert.ThrowsAsync<A2AException>(async () => await http413.GetTaskAsync(new GetTaskRequest { Id = "t" }, Ct));
        Assert.Contains("413", ex3.Message);
    }

    [Fact]
    public async Task Requests_carry_version_content_type_accept_extensions_and_credentials_and_increment_ids()
    {
        var handler = new StubHandler((_, _) => Reply("""{"jsonrpc":"2.0","id":1,"result":{"id":"t1","contextId":"c","status":{"state":"TASK_STATE_COMPLETED"}}}"""));
        await using var client = Stubbed(handler, new A2AClientOptions
        {
            Credentials = new TestUserCredentials("carol"),
            Extensions = ["urn:a", "urn:b"],
        });

        await client.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct);
        await client.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct);

        var r = handler.Requests[0];
        Assert.Equal("1.0", r.Headers.GetValues("A2A-Version").Single());
        Assert.Equal("urn:a,urn:b", r.Headers.GetValues("A2A-Extensions").Single());
        Assert.Equal("application/a2a+json", r.Headers.Accept.ToString());
        Assert.Equal("application/a2a+json", r.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("carol", r.Headers.GetValues(MinimalServer.UserHeader).Single());

        using var first = JsonDocument.Parse(handler.Bodies[0]);
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal("GetTask", first.RootElement.GetProperty("method").GetString());
        Assert.Equal("t1", first.RootElement.GetProperty("params").GetProperty("id").GetString());
        Assert.Equal(1, first.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(2, second.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Streaming_requests_accept_event_stream()
    {
        var handler = new StubHandler((_, _) => Reply("", contentType: "text/event-stream"));
        await using var client = Stubbed(handler);
        // An empty stream has no final event: InvalidAgentResponse. The request still asked for an event stream.
        var ex = await Assert.ThrowsAsync<A2AException>(async () => { await foreach (var _ in client.SendStreamingMessageAsync(Send("x"), Ct)) { } });
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal("text/event-stream", handler.Requests[0].Headers.Accept.ToString());
    }
}
