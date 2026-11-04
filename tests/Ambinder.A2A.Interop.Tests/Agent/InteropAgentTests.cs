using System.Text;
using System.Text.Json.Nodes;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>One interop server for the whole class; every test runs on JSON-RPC, REST and gRPC.</summary>
public sealed class InteropHostFixture : IAsyncLifetime
{
    public InteropServerHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Host = await InteropServerHost.StartAsync();

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>The .NET interop agent, in process over our host on loopback (contract: <c>interop/contract.md</c>).</summary>
public sealed class InteropAgentTests(InteropHostFixture fixture) : IClassFixture<InteropHostFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private InteropServerHost Host => fixture.Host;

    public static TheoryData<string> AllBindings => Bindings.Theory();

    private static SendMessageRequest Send(string text, string? taskId = null, string? contextId = null, bool returnImmediately = false, IReadOnlyList<string>? references = null)
        => new()
        {
            Message = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.User,
                TaskId = taskId,
                ContextId = contextId,
                Parts = [Part.FromText(text)],
                ReferenceTaskIds = references ?? [],
            },
            Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
        };

    private Task<IA2AClient> Client(string binding, string agent = ContractPayloads.MainAgent, string? token = null, IReadOnlyList<string>? extensions = null)
        => TestClients.CreateAsync(agent == ContractPayloads.MainAgent ? Host.CardUrl : agent == ContractPayloads.TenantAgent ? Host.TenantCardUrl : Host.SecondAgentCardUrl,
            binding, token, extensions, Ct);

    private static async Task<List<StreamResponse>> Collect(IAsyncEnumerable<StreamResponse> stream, CancellationToken ct)
    {
        var all = new List<StreamResponse>();
        await foreach (var e in stream.WithCancellation(ct)) all.Add(e);
        return all;
    }

    /// <summary>A token that only fails a test that hangs; it never decides a result.</summary>
    private static CancellationTokenSource Deadline()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        return cts;
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Card_lists_the_binding_streaming_push_and_extended_card(string binding)
    {
        var card = await TestClients.GetCardAsync(Host.CardUrl, Ct);

        Assert.Equal([A2AConstants.BindingJsonRpc, A2AConstants.BindingRest, A2AConstants.BindingGrpc], card.SupportedInterfaces.Select(i => i.ProtocolBinding));
        Assert.Contains(card.SupportedInterfaces, i => i.ProtocolBinding == binding);
        Assert.All(card.SupportedInterfaces, i => Assert.Null(i.Tenant));
        Assert.True(card.Capabilities.Streaming);
        Assert.True(card.Capabilities.PushNotifications);
        Assert.True(card.Capabilities.ExtendedAgentCard);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Echo_returns_a_message_and_no_task(string binding)
    {
        await using var client = await Client(binding);

        var response = await client.SendMessageAsync(Send("echo hi"), Ct);

        Assert.Null(response.Task);
        Assert.Equal("hi", Assert.Single(response.Message!.Parts).Text);
        Assert.Equal(Role.Agent, response.Message.Role);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Task_completes_with_one_artifact(string binding)
    {
        await using var client = await Client(binding);

        var response = await client.SendMessageAsync(Send("task hi"), Ct);

        Assert.Equal(TaskState.Completed, response.Task!.Status.State);
        Assert.Equal("hi", Assert.Single(Assert.Single(response.Task.Artifacts).Parts).Text);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Input_pauses_and_the_follow_up_completes_with_its_text(string binding)
    {
        await using var client = await Client(binding);

        var first = (await client.SendMessageAsync(Send("input"), Ct)).Task!;
        Assert.Equal(TaskState.InputRequired, first.Status.State);

        var second = (await client.SendMessageAsync(Send("more", first.Id, first.ContextId), Ct)).Task!;
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContextId, second.ContextId);
        Assert.Equal(TaskState.Completed, second.Status.State);
        Assert.Equal("more", Assert.Single(Assert.Single(second.Artifacts).Parts).Text);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Input_follow_up_with_another_context_is_rejected(string binding)
    {
        await using var client = await Client(binding);
        var first = (await client.SendMessageAsync(Send("input"), Ct)).Task!;

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.SendMessageAsync(Send("more", first.Id, "another-context"), Ct));

        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Slow_stays_working_until_release_names_the_task(string binding)
    {
        using var deadline = Deadline();
        await using var client = await Client(binding);
        var slow = (await client.SendMessageAsync(Send("slow", returnImmediately: true), deadline.Token)).Task!;
        Assert.False(IsTerminal(slow.Status.State));

        await using var second = await Client(binding);
        await using var events = second.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = slow.Id }, deadline.Token).GetAsyncEnumerator(deadline.Token);
        Assert.True(await events.MoveNextAsync());
        Assert.NotNull(events.Current.Task); // the current task comes first
        Assert.False(IsTerminal(events.Current.Task!.Status.State));
        Assert.False(IsTerminal((await client.GetTaskAsync(new GetTaskRequest { Id = slow.Id }, deadline.Token)).Status.State));

        var released = await client.SendMessageAsync(Send($"release {slow.Id}"), deadline.Token);
        Assert.Null(released.Task);

        TaskState? last = null;
        while (await events.MoveNextAsync()) last = events.Current.StatusUpdate?.Status.State ?? last;
        Assert.Equal(TaskState.Completed, last);
        Assert.Equal(TaskState.Completed, (await client.GetTaskAsync(new GetTaskRequest { Id = slow.Id }, deadline.Token)).Status.State);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Release_may_name_the_task_in_reference_task_ids(string binding)
    {
        using var deadline = Deadline();
        await using var client = await Client(binding);
        var slow = (await client.SendMessageAsync(Send("slow", returnImmediately: true), deadline.Token)).Task!;

        await client.SendMessageAsync(Send("release", references: [slow.Id]), deadline.Token);

        Assert.Equal(TaskState.Completed, await WaitTerminal(client, slow.Id, deadline.Token));
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Slow_is_canceled_by_cancel(string binding)
    {
        using var deadline = Deadline();
        await using var client = await Client(binding);
        var slow = (await client.SendMessageAsync(Send("slow", returnImmediately: true), deadline.Token)).Task!;

        var canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = slow.Id }, deadline.Token);

        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        Assert.Equal(TaskState.Canceled, (await client.GetTaskAsync(new GetTaskRequest { Id = slow.Id }, deadline.Token)).Status.State);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Chunks_stream_one_artifact_in_n_parts_then_complete(string binding)
    {
        using var deadline = Deadline();
        await using var client = await Client(binding);

        var events = await Collect(client.SendStreamingMessageAsync(Send("chunks 3"), deadline.Token), deadline.Token);

        Assert.NotNull(events[0].Task); // the task comes first
        var chunks = events.Select(e => e.ArtifactUpdate).OfType<TaskArtifactUpdateEvent>().ToList();
        Assert.Equal(3, chunks.Count);
        Assert.Equal([false, true, true], chunks.Select(c => c.Append));
        Assert.Equal([false, false, true], chunks.Select(c => c.LastChunk));
        Assert.Single(chunks.Select(c => c.Artifact.ArtifactId).Distinct());
        Assert.Equal(["chunk-1", "chunk-2", "chunk-3"], chunks.Select(c => Assert.Single(c.Artifact.Parts).Text));
        Assert.Equal(TaskState.Completed, events[^1].StatusUpdate!.Status.State); // the stream ends at the terminal state
        Assert.True(events.Last(e => e.ArtifactUpdate is not null) != events[^1]); // artifacts come before completion
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Fail_ends_failed_with_a_status_message(string binding)
    {
        await using var client = await Client(binding);

        var task = (await client.SendMessageAsync(Send("fail"), Ct)).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
        Assert.Equal("interop failure", Assert.Single(task.Status.Message!.Parts).Text);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Data_returns_the_contract_payload_exactly(string binding)
    {
        await using var client = await Client(binding);

        var message = (await client.SendMessageAsync(Send("data"), Ct)).Message!;

        var data = Assert.Single(message.Parts).Data!.Value;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ContractPayloads.DataJson), JsonNode.Parse(data.GetRawText())));
        // Integers stay integers (no ".0" or exponent), the largest safe integer survives.
        Assert.Equal("42", data.GetProperty("integer").GetRawText());
        Assert.Equal("0", data.GetProperty("zero").GetRawText());
        Assert.Equal(9007199254740991L, data.GetProperty("maxSafeInteger").GetInt64());
        Assert.Equal(-9007199254740991L, data.GetProperty("minSafeInteger").GetInt64());
        Assert.Equal("héllo wörld ✓ 日本語 😀", data.GetProperty("unicode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("null").ValueKind);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Raw_returns_the_contract_bytes_and_file_name(string binding)
    {
        await using var client = await Client(binding);

        var part = Assert.Single((await client.SendMessageAsync(Send("raw"), Ct)).Message!.Parts);

        Assert.Equal(ContractPayloads.RawBytes.ToArray(), part.Raw);
        Assert.Equal(ContractPayloads.RawFilename, part.Filename);
        Assert.Equal(ContractPayloads.RawMediaType, part.MediaType);
    }

    [Fact]
    public void Raw_base64_literal_matches_the_bytes()
        => Assert.Equal(ContractPayloads.RawBase64, Convert.ToBase64String(ContractPayloads.RawBytes));

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Unknown_task_gives_task_not_found(string binding)
    {
        await using var client = await Client(binding);

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = "no-such-task" }, Ct));

        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    // ---------- second agent: required extension ----------

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Second_agent_rejects_a_call_without_the_required_extension(string binding)
    {
        await using var client = await Client(binding, ContractPayloads.RequiredExtensionAgent);

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.SendMessageAsync(Send("ext"), Ct));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, ex.Code);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Second_agent_completes_with_the_required_extension(string binding)
    {
        await using var client = await Client(binding, ContractPayloads.RequiredExtensionAgent, extensions: [ContractPayloads.RequiredExtension]);

        var task = (await client.SendMessageAsync(Send("ext"), Ct)).Task!;

        Assert.Equal(TaskState.Completed, task.Status.State);
    }

    [Fact]
    public async Task Second_agent_echoes_the_activated_extension_in_the_response_header()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Host.BaseUrl}/agents/{ContractPayloads.RequiredExtensionAgent}/jsonrpc")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"ext"}]}}}""",
                Encoding.UTF8, A2AConstants.ContentType),
        };
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Add(A2AConstants.ExtensionsHeader, ContractPayloads.RequiredExtension);

        using var response = await http.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal([ContractPayloads.RequiredExtension], response.Headers.GetValues(A2AConstants.ExtensionsHeader));
    }

    // ---------- tenant agent ----------

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Tenant_agent_declares_the_tenant_on_every_interface_and_serves_it(string binding)
    {
        var card = await TestClients.GetCardAsync(Host.TenantCardUrl, Ct);
        Assert.Equal(3, card.SupportedInterfaces.Count);
        Assert.All(card.SupportedInterfaces, i => Assert.Equal(ContractPayloads.Tenant, i.Tenant));

        await using var client = await Client(binding, ContractPayloads.TenantAgent);
        var response = await client.SendMessageAsync(Send("echo tenant"), Ct); // the client sends the interface tenant

        Assert.Equal("tenant", Assert.Single(response.Message!.Parts).Text);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Tenant_agent_rejects_another_tenant(string binding)
    {
        await using var client = await Client(binding, ContractPayloads.TenantAgent);

        // The client refuses to send a tenant that differs from the interface tenant.
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendMessageAsync(Send("echo x") with { Tenant = "other-tenant" }, Ct));
    }

    // ---------- extended card ----------

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Extended_card_needs_the_bearer_token(string binding)
    {
        await using var anonymous = await Client(binding);
        await Assert.ThrowsAsync<A2AAuthException>(() => anonymous.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct));

        await using var wrongToken = await Client(binding, token: "wrong");
        await Assert.ThrowsAsync<A2AAuthException>(() => wrongToken.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct));

        await using var authorized = await Client(binding, token: ContractPayloads.BearerToken);
        var card = await authorized.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct);
        Assert.Equal("Interop test agent (.NET), extended card.", card.Description);
    }

    [Theory, MemberData(nameof(AllBindings))]
    public async Task Public_operations_need_no_token_and_ignore_a_bad_one(string binding)
    {
        await using var anonymous = await Client(binding);
        Assert.NotNull((await anonymous.SendMessageAsync(Send("echo hi"), Ct)).Message);

        await using var wrongToken = await Client(binding, token: "wrong");
        // A token that does not validate leaves the caller anonymous; only operations that need a caller (extended card) refuse it.
        Assert.NotNull((await wrongToken.SendMessageAsync(Send("echo hi"), Ct)).Message);
    }

    private static bool IsTerminal(TaskState s) => s is TaskState.Completed or TaskState.Failed or TaskState.Canceled or TaskState.Rejected;

    /// <summary>Polls GetTask without delay until the task is terminal. The deadline token only fails a test that hangs.</summary>
    private static async Task<TaskState> WaitTerminal(IA2AClient client, string id, CancellationToken ct)
    {
        while (true)
        {
            var state = (await client.GetTaskAsync(new GetTaskRequest { Id = id }, ct)).Status.State;
            if (IsTerminal(state)) return state;
            await Task.Yield();
        }
    }
}
