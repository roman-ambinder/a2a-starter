using A2A.Client;
using A2A.Client.Auth;
using A2A.Client.Transports;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Client;

/// <summary>The same client scenarios over JSON-RPC, REST and gRPC, created through <see cref="A2AClientFactory"/>.</summary>
[Collection(MinimalServerCollection.Name)]
public sealed class TransportParityTests(MinimalServer server)
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

    private async Task<IA2AClient> CreateAsync(string binding, string agent = MinimalServer.EchoAgentName,
        IA2ACredentialProvider? credentials = null, string version = "1.0")
    {
        Assert.NotNull(server.App); // fixture started
        using var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{MinimalServer.BaseUrl}/agents/{agent}"), Ct);
        var client = A2AClientFactory.Create(card, new A2AClientOptions
        {
            PreferredBinding = binding,
            Credentials = credentials,
            ProtocolVersion = version,
        });
        Type expected = binding switch
        {
            "JSONRPC" => typeof(JsonRpcA2AClient),
            "HTTP+JSON" => typeof(RestA2AClient),
            _ => typeof(GrpcA2AClient),
        };
        Assert.IsType(expected, client);
        return client;
    }

    private static SendMessageRequest Send(string text, bool returnImmediately = false) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(text)] },
        Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
    };

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task All_eleven_operations_work(string binding)
    {
        await using var client = await CreateAsync(binding, credentials: new TestUserCredentials("alice"));

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
        var noHistory = await client.GetTaskAsync(new GetTaskRequest { Id = task.Id, HistoryLength = 0 }, Ct);
        Assert.Empty(noHistory.History);

        // 4 ListTasks
        var list = await client.ListTasksAsync(new ListTasksRequest
        {
            ContextId = task.ContextId,
            Status = TaskState.Completed,
            PageSize = 10,
            IncludeArtifacts = true,
            StatusTimestampAfter = DateTimeOffset.UtcNow.AddMinutes(-5),
        }, Ct);
        Assert.Contains(list.Tasks, t => t.Id == task.Id);

        // 5 CancelTask: a slow task, left running
        var slow = (await client.SendMessageAsync(Send("slow:5000", returnImmediately: true), Ct)).Task!;
        var canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = slow.Id }, Ct);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);

        // 6 SubscribeToTask
        var slow2 = (await client.SendMessageAsync(Send("slow:1500", returnImmediately: true), Ct)).Task!;
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

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Errors_have_same_code_on_every_transport(string binding)
    {
        await using var client = await CreateAsync(binding);

        var notFound = await Assert.ThrowsAsync<A2AException>(async () => await client.GetTaskAsync(new GetTaskRequest { Id = "nope" }, Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, notFound.Code);

        var stream = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "nope" }, Ct)) { }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, stream.Code);

        var done = (await client.SendMessageAsync(Send("x"), Ct)).Task!;
        var notCancelable = await Assert.ThrowsAsync<A2AException>(async () => await client.CancelTaskAsync(new CancelTaskRequest { Id = done.Id }, Ct));
        Assert.Equal(A2AErrorCode.TaskNotCancelable, notCancelable.Code);

        await using var oldClient = await CreateAsync(binding, version: "0.3");
        var version = await Assert.ThrowsAsync<A2AException>(async () => await oldClient.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(A2AErrorCode.VersionNotSupported, version.Code);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Auth_failures_have_same_status_on_every_transport(string binding)
    {
        await using var anonymous = await CreateAsync(binding, MinimalServer.ProtectedAgentName);
        var unauthorized = await Assert.ThrowsAsync<A2AAuthException>(async () => await anonymous.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(401, unauthorized.HttpStatus);

        await using var noRole = await CreateAsync(binding, MinimalServer.ProtectedAgentName, new TestUserCredentials("bob"));
        var forbidden = await Assert.ThrowsAsync<A2AAuthException>(async () => await noRole.SendMessageAsync(Send("x"), Ct));
        Assert.Equal(403, forbidden.HttpStatus);

        // Streams fail the same way.
        var streamForbidden = await Assert.ThrowsAsync<A2AAuthException>(async () =>
        {
            await foreach (var _ in noRole.SendStreamingMessageAsync(Send("x"), Ct)) { }
        });
        Assert.Equal(403, streamForbidden.HttpStatus);

        await using var ok = await CreateAsync(binding, MinimalServer.ProtectedAgentName, new TestUserCredentials("bob", MinimalServer.InvokerRole));
        Assert.NotNull((await ok.SendMessageAsync(Send("x"), Ct)).Task);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Cancelling_a_stream_surfaces_only_OperationCanceledException(string binding)
    {
        await using var client = await CreateAsync(binding);
        var slow = (await client.SendMessageAsync(Send("slow:3000", returnImmediately: true), Ct)).Task!;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = slow.Id }, cts.Token))
                cts.Cancel(); // after the first event
        });

        // Disposing the enumerator early is quiet.
        await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = slow.Id }, Ct))
            break;
    }

    [Fact]
    public void Factory_prefers_requested_binding_and_falls_back_to_card_order()
    {
        var card = new AgentCard
        {
            Name = "multi",
            SupportedInterfaces =
            [
                new AgentInterface { Url = "http://localhost:1/grpc", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "http://localhost:1/rest", ProtocolBinding = "HTTP+JSON", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "http://localhost:1/rpc", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
            ],
        };

        Assert.IsType<RestA2AClient>(A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = "HTTP+JSON" }));
        Assert.IsType<JsonRpcA2AClient>(A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = "jsonrpc" }));
        Assert.IsType<GrpcA2AClient>(A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = "GRPC" }));
        // No preference, or a preference the card does not offer → first interface in card order.
        Assert.IsType<GrpcA2AClient>(A2AClientFactory.Create(card, new A2AClientOptions()));
        Assert.IsType<GrpcA2AClient>(A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = "CARRIER-PIGEON" }));

        var restOnly = card with { SupportedInterfaces = [card.SupportedInterfaces[1]] };
        Assert.IsType<RestA2AClient>(A2AClientFactory.Create(restOnly, new A2AClientOptions { PreferredBinding = "GRPC" }));
    }

    [Fact]
    public void Factory_throws_NotSupported_naming_binding_and_url_for_a_bad_card_url()
    {
        var card = new AgentCard
        {
            Name = "bad-url",
            SupportedInterfaces = [new AgentInterface { Url = "not a url", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" }],
        };
        var ex = Assert.Throws<NotSupportedException>(() => A2AClientFactory.Create(card, new A2AClientOptions()));
        Assert.Contains("GRPC", ex.Message);
        Assert.Contains("not a url", ex.Message);
    }

    [Fact]
    public void Factory_throws_NotSupported_listing_bindings_when_nothing_matches()
    {
        var card = new AgentCard
        {
            Name = "odd",
            SupportedInterfaces = [new AgentInterface { Url = "http://localhost:1/x", ProtocolBinding = "SMOKE-SIGNALS", ProtocolVersion = "1.0" }],
        };
        var ex = Assert.Throws<NotSupportedException>(() => A2AClientFactory.Create(card, new A2AClientOptions()));
        Assert.Contains("SMOKE-SIGNALS", ex.Message);

        var empty = Assert.Throws<NotSupportedException>(() => A2AClientFactory.Create(new AgentCard { Name = "none" }, new A2AClientOptions()));
        Assert.Contains("no supported interfaces", empty.Message);
    }
}
