using System.Net.Http.Json;
using System.Text.Json;
using A2A.Protocol;

namespace AgentClient;

public sealed partial class FlowRunner
{
    /// <summary>Booking agent: input-required, wrong input, then the answer on the same task.</summary>
    public Task<FlowResult> MultiTurnAsync(CancellationToken ct = default) => RunFlowAsync("multi-turn", ct, async flow =>
    {
        var agent = AgentOr(Booking);
        await using var client = await ClientAsync(agent, ct: ct);
        var first = (await client.SendMessageAsync(Request("Book pArIs"), ct)).Task ?? throw new FlowException("no task returned");
        flow.Check(first.Status.State == TaskState.InputRequired, $"turn 1: {first.Status.State}, agent asks '{TextOf(first.Status.Message)}'");

        var bad = (await client.SendMessageAsync(Request("2027-13-45", first.Id, first.ContextId), ct)).Task!;
        flow.Check(bad.Status.State == TaskState.InputRequired, $"turn 2 (bad date): {bad.Status.State}, agent says '{TextOf(bad.Status.Message)}'");

        var done = (await client.SendMessageAsync(Request("2027-03-15", first.Id, first.ContextId), ct)).Task!;
        flow.Check(done.Id == first.Id && done.ContextId == first.ContextId, "turn 3 continues the same task and context");
        flow.Check(done.Status.State == TaskState.Completed && done.Artifacts.Count == 1, $"turn 3: {done.Status.State}");
        var data = done.Artifacts[0].Parts.Single().Data;
        flow.Step($"booking: {data}");
    });

    /// <summary>Push notifications: register with the receiver, send with a push config, read what the receiver got, manage configs.</summary>
    public Task<FlowResult> PushAsync(CancellationToken ct = default) => RunFlowAsync("push", ct, async flow =>
    {
        var agent = AgentOr(Report);
        var receiver = _options.ReceiverUrl.TrimEnd('/');
        var credentials = Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        var webhook = receiver + "/webhook";

        // The task id is unknown before the first send, so the receiver is told to accept any one task ("*").
        using (var subscribed = await _http.PostAsJsonAsync(receiver + "/api/subscriptions", new { taskId = "*", token, credentials }, ct))
            flow.Check(subscribed.StatusCode == System.Net.HttpStatusCode.Created, $"receiver subscription registered (HTTP {(int)subscribed.StatusCode})");

        await using var client = await ClientAsync(agent, ct: ct);
        var config = new TaskPushNotificationConfig
        {
            Url = webhook,
            Token = token,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = credentials },
        };
        var task = (await client.SendMessageAsync(Request("Pushed topic", push: config), ct)).Task ?? throw new FlowException("no task returned");
        flow.Step($"sent with push config to {webhook}: task {task.Id} is {task.Status.State}");

        var received = await WaitForNotificationsAsync(receiver, task.Id, ct);
        flow.Step($"receiver stored {received.Count} notification(s): " + string.Join(", ", received.Select(Describe)));
        flow.Check(received.Any(e => e.Task is not null) && received.Any(e => e.ArtifactUpdate?.Artifact.ArtifactId == "report"), "receiver got the task and the report artifact");
        flow.Check(received.Count(e => e.StatusUpdate?.Status.State == TaskState.Completed) == 1, "receiver got exactly one COMPLETED update");

        var created = await client.CreateTaskPushNotificationConfigAsync(config with { TaskId = task.Id }, ct);
        flow.Check(!string.IsNullOrEmpty(created.Id), $"config created with id {created.Id}");
        var got = await client.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, ct);
        flow.Check(got.Url == webhook, "config read back");
        var list = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, ct);
        flow.Check(list.Configs.Any(c => c.Id == created.Id), $"config listed ({list.Configs.Count} for this task)");
        await client.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, ct);
        var after = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, ct);
        flow.Check(after.Configs.All(c => c.Id != created.Id), "config deleted");
    });

    private static string Describe(StreamResponse e)
        => e.Task is not null ? "task"
            : e.StatusUpdate is { } s ? s.Status.State.ToString()
            : e.ArtifactUpdate is { } a ? "artifact:" + a.Artifact.ArtifactId
            : "message";

    private async Task<List<StreamResponse>> WaitForNotificationsAsync(string receiver, string taskId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            var elements = await _http.GetFromJsonAsync<List<JsonElement>>($"{receiver}/api/notifications?taskId={Uri.EscapeDataString(taskId)}", timeout.Token) ?? [];
            var list = elements.Select(e => e.Deserialize<StreamResponse>(A2AJson.Options)!).ToList();
            if (list.Any(e => e.StatusUpdate?.Status.State == TaskState.Completed)) return list;
            await Task.Delay(50, timeout.Token);
        }
    }

    /// <summary>Secure agent: step-up from a client token to an approver token, plus the 401 and 403 cases. Always uses the mock identities.</summary>
    public Task<FlowResult> AuthAsync(CancellationToken ct = default) => RunFlowAsync("auth", ct, async flow =>
    {
        var weak = await ClientAsync(Secure, Identity.Client, [AuditExtension], forceMock: true, ct: ct);
        await using var weakDispose = weak;
        var paused = (await weak.SendMessageAsync(Request("approve r-100"), ct)).Task!;
        flow.Check(paused.Status.State == TaskState.AuthRequired, $"a2a-client token: {paused.Status.State}, agent says '{TextOf(paused.Status.Message)}'");

        await using var strong = await ClientAsync(Secure, Identity.Approver, [AuditExtension], forceMock: true, ct: ct);
        var done = (await strong.SendMessageAsync(Request("approved, go", paused.Id, paused.ContextId), ct)).Task!;
        flow.Check(done.Id == paused.Id && done.Status.State == TaskState.Completed, $"a2a-approver token on the same task: {done.Status.State}");
        flow.Step($"approval: {done.Artifacts.SingleOrDefault()?.Parts.SingleOrDefault()?.Data}");

        await using var anonymous = await ClientAsync(Report, anonymous: true, ct: ct);
        var e401 = await Assert401Or403Async(() => anonymous.SendMessageAsync(Request("Auth demo"), ct));
        flow.Check(e401 == 401, $"no credentials on the report agent: HTTP {e401}");

        await using var noRoles = await ClientAsync(Report, Identity.NoRoles, forceMock: true, ct: ct);
        var e403 = await Assert401Or403Async(() => noRoles.SendMessageAsync(Request("Auth demo"), ct));
        flow.Check(e403 == 403, $"a2a-noroles token on the report agent: HTTP {e403}");
    });

    private static async Task<int> Assert401Or403Async(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (A2AAuthException e)
        {
            return e.HttpStatus;
        }
        return 200;
    }

    /// <summary>Anonymous call to the extended card is refused; an authenticated one returns more skills.</summary>
    public Task<FlowResult> ExtendedCardAsync(CancellationToken ct = default) => RunFlowAsync("extended-card", ct, async flow =>
    {
        var agent = AgentOr(Report);
        var publicCard = await CardAsync(agent, ct);
        flow.Step($"public card of {agent}: {publicCard.Skills.Count} skill(s), extended card advertised = {publicCard.Capabilities.ExtendedAgentCard == true}");

        await using (var anonymous = await ClientAsync(agent, anonymous: true, ct: ct))
        {
            var status = await Assert401Or403Async(() => anonymous.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), ct));
            flow.Check(status == 401, $"without credentials: HTTP {status}");
        }

        await using var client = await ClientAsync(agent, ct: ct);
        var extended = await client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), ct);
        var extra = extended.Skills.Where(s => publicCard.Skills.All(p => p.Id != s.Id)).Select(s => s.Id).ToList();
        flow.Check(extra.Count > 0, $"extended card: {extended.Skills.Count} skill(s), extra: [{string.Join(", ", extra)}]");
    });

    /// <summary>Extensions: echo timestamp (off and on) and the secure agent's required audit extension.</summary>
    public Task<FlowResult> ExtensionsAsync(CancellationToken ct = default) => RunFlowAsync("extensions", ct, async flow =>
    {
        await using (var plain = await ClientAsync(Echo, ct: ct))
        {
            var reply = (await plain.SendMessageAsync(Request("hi"), ct)).Message ?? throw new FlowException("echo did not answer with a message");
            flow.Check(reply.Extensions.Count == 0, $"echo without extension: '{TextOf(reply)}', no extensions in the reply");
        }

        await using (var stamped = await ClientAsync(Echo, extensions: [TimestampExtension], ct: ct))
        {
            var reply = (await stamped.SendMessageAsync(Request("hi"), ct)).Message ?? throw new FlowException("echo did not answer with a message");
            var stamp = reply.Metadata is { } md && md.TryGetProperty(TimestampExtension, out var ext) && ext.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null;
            flow.Check(reply.Extensions.Contains(TimestampExtension) && stamp is not null, $"echo with the timestamp extension: reply timestamp {stamp}");
        }

        await using var audited = await ClientAsync(Secure, Identity.Approver, [AuditExtension], ct: ct);
        var task = (await audited.SendMessageAsync(Request("approve r-200"), ct)).Task!;
        flow.Check(task.Status.State is TaskState.Completed or TaskState.AuthRequired,
            $"secure agent with the audit extension is accepted: {task.Status.State}");
    });

    /// <summary>Each protocol error the demo servers can produce, and the code the client sees.</summary>
    public Task<FlowResult> ErrorsAsync(CancellationToken ct = default) => RunFlowAsync("errors", ct, async flow =>
    {
        async Task Expect(A2AErrorCode code, string what, Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (A2AException e) when (e.Code == code)
            {
                flow.Step($"{what}: {e.Code} ({(int)e.Code})");
                return;
            }
            throw new FlowException($"{what}: expected {code} but the call succeeded");
        }

        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);

        await Expect(A2AErrorCode.TaskNotFound, "GetTask with an unknown id",
            () => client.GetTaskAsync(new GetTaskRequest { Id = Guid.NewGuid().ToString("N") }, ct));

        var finished = (await client.SendMessageAsync(Request("Done topic"), ct)).Task!;
        await Expect(A2AErrorCode.TaskNotCancelable, "CancelTask on a finished task",
            () => client.CancelTaskAsync(new CancelTaskRequest { Id = finished.Id }, ct));

        await using (var old = await ClientAsync(agent, protocolVersion: "0.3", ct: ct))
            await Expect(A2AErrorCode.VersionNotSupported, "A2A-Version 0.3", () => old.SendMessageAsync(Request("hi"), ct));

        var image = Part.FromRaw([0x89, 0x50, 0x4E, 0x47], "image/png", "pic.png");
        await Expect(A2AErrorCode.ContentTypeNotSupported, "an image sent to the report agent",
            () => client.SendMessageAsync(Request("topic", extra: image), ct));

        await using (var noAudit = await ClientAsync(Secure, Identity.Approver, ct: ct))
            await Expect(A2AErrorCode.ExtensionSupportRequired, "secure agent without the audit extension",
                () => noAudit.SendMessageAsync(Request("approve r-300"), ct));

        await using var echo = await ClientAsync(Echo, ct: ct);
        await Expect(A2AErrorCode.UnsupportedOperation, "extended card of the echo agent",
            () => echo.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), ct));
    });
}
