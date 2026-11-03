using Ambinder.A2A.Integration.Tests.Fixtures;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Integration.Tests.Transport;

/// <summary>
/// Raw generated <see cref="Proto.A2AService.A2AServiceClient"/> over h2c against the real Kestrel gRPC listeners of
/// <see cref="MinimalServer"/>. Agent routing is checked without server-side extras: the echo port serves the echo agent (its
/// extended card says so), the protected port serves the protected agent (policy challenge, no extended card).
/// </summary>
[Collection(MinimalServerCollection.Name)]
public sealed class GrpcTransportTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly List<GrpcChannel> _channels = [];

    public void Dispose()
    {
        foreach (var channel in _channels) channel.Dispose();
    }

    private Proto.A2AService.A2AServiceClient Client(int port)
    {
        var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{port}");
        _channels.Add(channel);
        return new Proto.A2AService.A2AServiceClient(channel);
    }

    private static Metadata Headers(string? user = null, string? roles = null, string? version = "1.0")
    {
        var m = new Metadata();
        if (version is not null) m.Add("a2a-version", version);
        if (user is not null) m.Add(MinimalServer.UserHeader.ToLowerInvariant(), user);
        if (roles is not null) m.Add(MinimalServer.RolesHeader.ToLowerInvariant(), roles);
        return m;
    }

    private static Proto.SendMessageRequest Send(string text, bool returnImmediately = false)
    {
        var request = new Proto.SendMessageRequest
        {
            Message = new Proto.Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Proto.Role.User,
                Parts = { new Proto.Part { Text = text } },
            },
        };
        if (returnImmediately) request.Configuration = new Proto.SendMessageConfiguration { ReturnImmediately = true };
        return request;
    }

    private static ErrorInfo ErrorInfoOf(RpcException ex)
        => ex.GetRpcStatus()!.Details.Single().Unpack<ErrorInfo>();

    // ---------- happy path ----------

    [Fact]
    public async Task SendMessage_over_grpc_returns_task()
    {
        var client = Client(MinimalServer.EchoGrpcPort);

        var response = await client.SendMessageAsync(Send("hello"), Headers(), cancellationToken: Ct);

        Assert.Equal(Proto.SendMessageResponse.PayloadOneofCase.Task, response.PayloadCase);
        Assert.Equal(Proto.TaskState.Completed, response.Task.Status.State);
        Assert.Equal("echo: hello", response.Task.Artifacts.Single().Parts.Single().Text);
    }

    [Fact]
    public async Task SendStreamingMessage_over_grpc_streams_task_then_updates()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        using var call = client.SendStreamingMessage(Send("stream"), Headers(), cancellationToken: Ct);

        var events = new List<Proto.StreamResponse>();
        await foreach (var e in call.ResponseStream.ReadAllAsync(Ct)) events.Add(e);

        Assert.Equal(Proto.StreamResponse.PayloadOneofCase.Task, events[0].PayloadCase);
        Assert.Contains(events, e => e.PayloadCase == Proto.StreamResponse.PayloadOneofCase.ArtifactUpdate);
        var last = events[^1];
        Assert.Equal(Proto.StreamResponse.PayloadOneofCase.StatusUpdate, last.PayloadCase);
        Assert.Equal(Proto.TaskState.Completed, last.StatusUpdate.Status.State);
        Assert.Equal(events[0].Task.Id, last.StatusUpdate.TaskId);
    }

    [Fact]
    public async Task GetTask_and_ListTasks_return_the_task()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        var sent = await client.SendMessageAsync(Send("find me"), Headers("lena"), cancellationToken: Ct);

        var got = await client.GetTaskAsync(new Proto.GetTaskRequest { Id = sent.Task.Id }, Headers("lena"), cancellationToken: Ct);
        var list = await client.ListTasksAsync(new Proto.ListTasksRequest { ContextId = sent.Task.ContextId }, Headers("lena"), cancellationToken: Ct);

        Assert.Equal(sent.Task.Id, got.Id);
        Assert.Equal(sent.Task.Id, list.Tasks.Single().Id);
    }

    [Fact]
    public async Task SubscribeToTask_streams_until_terminal()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        var sent = await client.SendMessageAsync(Send("slow:800", returnImmediately: true), Headers("sue"), cancellationToken: Ct);

        using var call = client.SubscribeToTask(new Proto.SubscribeToTaskRequest { Id = sent.Task.Id }, Headers("sue"), cancellationToken: Ct);
        var events = new List<Proto.StreamResponse>();
        await foreach (var e in call.ResponseStream.ReadAllAsync(Ct)) events.Add(e);

        Assert.Equal(Proto.StreamResponse.PayloadOneofCase.Task, events[0].PayloadCase);
        Assert.Equal(Proto.TaskState.Completed, events[^1].StatusUpdate.Status.State);
    }

    [Fact]
    public async Task Push_config_round_trip_and_delete_returns_empty()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        var sent = await client.SendMessageAsync(Send("push"), Headers("pat"), cancellationToken: Ct);

        var created = await client.CreateTaskPushNotificationConfigAsync(
            new Proto.TaskPushNotificationConfig { TaskId = sent.Task.Id, Url = "http://127.0.0.1:9/hook", Token = "t" }, Headers("pat"), cancellationToken: Ct);
        Assert.NotEmpty(created.Id);
        var got = await client.GetTaskPushNotificationConfigAsync(
            new Proto.GetTaskPushNotificationConfigRequest { TaskId = sent.Task.Id, Id = created.Id }, Headers("pat"), cancellationToken: Ct);
        var list = await client.ListTaskPushNotificationConfigsAsync(
            new Proto.ListTaskPushNotificationConfigsRequest { TaskId = sent.Task.Id }, Headers("pat"), cancellationToken: Ct);
        var deleted = await client.DeleteTaskPushNotificationConfigAsync(
            new Proto.DeleteTaskPushNotificationConfigRequest { TaskId = sent.Task.Id, Id = created.Id }, Headers("pat"), cancellationToken: Ct);

        Assert.Equal("http://127.0.0.1:9/hook", got.Url);
        Assert.Single(list.Configs);
        Assert.IsType<Empty>(deleted);
    }

    // ---------- routing ----------

    [Fact]
    public async Task Each_agent_port_routes_to_its_agent()
    {
        // echo port: the echo agent, which has an extended card.
        var card = await Client(MinimalServer.EchoGrpcPort)
            .GetExtendedAgentCardAsync(new Proto.GetExtendedAgentCardRequest(), Headers("rita"), cancellationToken: Ct);
        Assert.Equal("echo", card.Name);
        Assert.Equal("Extended echo card", card.Description);

        // protected port: the protected agent. Its policy turns anonymous callers away and it does not offer an extended card.
        var protectedClient = Client(MinimalServer.ProtectedGrpcPort);
        var anonymous = await Assert.ThrowsAsync<RpcException>(async () =>
            await protectedClient.SendMessageAsync(Send("hi"), Headers(), cancellationToken: Ct));
        Assert.Equal(StatusCode.Unauthenticated, anonymous.StatusCode);

        var noCard = await Assert.ThrowsAsync<RpcException>(async () =>
            await protectedClient.GetExtendedAgentCardAsync(new Proto.GetExtendedAgentCardRequest(), Headers("rita", MinimalServer.InvokerRole), cancellationToken: Ct));
        Assert.Equal(StatusCode.FailedPrecondition, noCard.StatusCode);
        Assert.Equal("UNSUPPORTED_OPERATION", ErrorInfoOf(noCard).Reason); // capability not declared (spec 3.3.4)

        var allowed = await protectedClient.SendMessageAsync(Send("hi"), Headers("rita", MinimalServer.InvokerRole), cancellationToken: Ct);
        Assert.Equal(Proto.TaskState.Completed, allowed.Task.Status.State);
    }

    [Fact]
    public async Task Port_without_an_agent_is_not_found()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client(MinimalServer.UnmappedGrpcPort).GetTaskAsync(new Proto.GetTaskRequest { Id = "x" }, Headers(), cancellationToken: Ct));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Card_interface_url_matches_the_listener()
    {
        using var http = new HttpClient { BaseAddress = new Uri(MinimalServer.BaseUrl + "/") };
        var card = System.Text.Json.Nodes.JsonNode.Parse(await http.GetStringAsync("agents/echo/.well-known/agent-card.json", Ct))!;
        var grpc = card["supportedInterfaces"]!.AsArray().Single(i => i!["protocolBinding"]!.GetValue<string>() == "GRPC")!;

        Assert.Equal($"127.0.0.1:{MinimalServer.EchoGrpcPort}", grpc["url"]!.GetValue<string>());
        var reply = await Client(MinimalServer.EchoGrpcPort).SendMessageAsync(Send("via card url"), Headers(), cancellationToken: Ct);
        Assert.Equal(Proto.TaskState.Completed, reply.Task.Status.State);
    }

    // ---------- errors ----------

    [Fact]
    public async Task Error_has_error_info_detail()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client(MinimalServer.EchoGrpcPort).GetTaskAsync(new Proto.GetTaskRequest { Id = "no-such-task" }, Headers(), cancellationToken: Ct));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        var info = ErrorInfoOf(ex);
        Assert.Equal("TASK_NOT_FOUND", info.Reason);
        Assert.Equal("a2a-protocol.org", info.Domain);
    }

    [Fact]
    public async Task Missing_version_metadata_fails_precondition()
    {
        var client = Client(MinimalServer.EchoGrpcPort);

        foreach (var version in new string?[] { null, "0.3", "" })
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.SendMessageAsync(Send("x"), Headers(version: version), cancellationToken: Ct));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Equal("VERSION_NOT_SUPPORTED", ErrorInfoOf(ex).Reason);
        }
    }

    [Fact]
    public async Task SendMessage_without_message_is_invalid_argument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client(MinimalServer.EchoGrpcPort).SendMessageAsync(new Proto.SendMessageRequest(), Headers(), cancellationToken: Ct));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Empty(ex.GetRpcStatus()!.Details); // InvalidParams has no ErrorInfo reason
    }

    [Fact]
    public async Task Required_ids_are_checked()
    {
        var client = Client(MinimalServer.EchoGrpcPort);

        var getTask = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.GetTaskAsync(new Proto.GetTaskRequest(), Headers(), cancellationToken: Ct));
        var cancel = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.CancelTaskAsync(new Proto.CancelTaskRequest(), Headers(), cancellationToken: Ct));
        var delete = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.DeleteTaskPushNotificationConfigAsync(new Proto.DeleteTaskPushNotificationConfigRequest { TaskId = "t" }, Headers(), cancellationToken: Ct));
        using var subscribe = client.SubscribeToTask(new Proto.SubscribeToTaskRequest(), Headers(), cancellationToken: Ct);
        var sub = await Assert.ThrowsAsync<RpcException>(async () => await subscribe.ResponseStream.MoveNext(Ct));

        Assert.All(new[] { getTask, cancel, delete, sub }, e => Assert.Equal(StatusCode.InvalidArgument, e.StatusCode));
    }

    [Fact]
    public async Task Streaming_error_before_first_message_is_the_call_status()
    {
        using var call = Client(MinimalServer.EchoGrpcPort).SubscribeToTask(new Proto.SubscribeToTaskRequest { Id = "nope" }, Headers(), cancellationToken: Ct);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(Ct));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        Assert.Equal("TASK_NOT_FOUND", ErrorInfoOf(ex).Reason);
    }

    [Fact]
    public async Task Cancel_of_a_finished_task_has_reason()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        var sent = await client.SendMessageAsync(Send("done"), Headers("cal"), cancellationToken: Ct);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.CancelTaskAsync(new Proto.CancelTaskRequest { Id = sent.Task.Id }, Headers("cal"), cancellationToken: Ct));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Equal("TASK_NOT_CANCELABLE", ErrorInfoOf(ex).Reason);
    }

    // ---------- authorization ----------

    [Fact]
    public async Task Auth_failures_map_to_unauthenticated_and_permission_denied()
    {
        var client = Client(MinimalServer.ProtectedGrpcPort);

        var wrongRole = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.SendMessageAsync(Send("x"), Headers("bob", "other"), cancellationToken: Ct));
        Assert.Equal(StatusCode.PermissionDenied, wrongRole.StatusCode);

        var anonymousCard = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client(MinimalServer.EchoGrpcPort).GetExtendedAgentCardAsync(new Proto.GetExtendedAgentCardRequest(), Headers(), cancellationToken: Ct));
        Assert.Equal(StatusCode.Unauthenticated, anonymousCard.StatusCode);
    }

    [Fact]
    public async Task Tasks_are_private_to_their_owner()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        var sent = await client.SendMessageAsync(Send("mine"), Headers("owner1"), cancellationToken: Ct);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.GetTaskAsync(new Proto.GetTaskRequest { Id = sent.Task.Id }, Headers("owner2"), cancellationToken: Ct));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ---------- cancellation ----------

    [Fact]
    public async Task Cancelled_call_does_not_cancel_task()
    {
        var client = Client(MinimalServer.EchoGrpcPort);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var call = client.SendStreamingMessage(Send("slow:700"), Headers("carl"), cancellationToken: cts.Token);
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));
        var taskId = call.ResponseStream.Current.Task.Id;

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () => await call.ResponseStream.MoveNext(cts.Token));

        Proto.Task task;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            await Task.Delay(100, Ct);
            task = await client.GetTaskAsync(new Proto.GetTaskRequest { Id = taskId }, Headers("carl"), cancellationToken: Ct);
        }
        while (task.Status.State is Proto.TaskState.Submitted or Proto.TaskState.Working && DateTime.UtcNow < deadline);

        Assert.Equal(Proto.TaskState.Completed, task.Status.State);
    }
}
