using Ambinder.A2A.Protocol;

namespace AgentClient;

public sealed partial class FlowRunner
{
    /// <summary>Fetches the public card of every agent and the root card.</summary>
    public Task<FlowResult> DiscoverAsync(CancellationToken ct = default) => RunFlowAsync("discover", ct, async flow =>
    {
        foreach (var agent in new[] { Echo, Report, Booking, Secure })
        {
            var card = await CardAsync(agent, ct);
            var bindings = string.Join(",", card.SupportedInterfaces.Select(i => i.ProtocolBinding));
            var caps = $"streaming={card.Capabilities.Streaming == true} push={card.Capabilities.PushNotifications == true} extendedCard={card.Capabilities.ExtendedAgentCard == true}";
            var schemes = card.SecuritySchemes is { Count: > 0 } s ? string.Join(",", s.Keys) : "none";
            flow.Check(card.SupportedInterfaces.Count == 3 && card.Skills.Count > 0,
                $"{agent}: '{card.Name}' v{card.Version}, bindings [{bindings}], {card.Skills.Count} skill(s), {caps}, security [{schemes}]");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ServerUrl.TrimEnd('/') + A2AConstants.WellKnownCardPath);
        using var response = await _http.SendAsync(request, ct);
        flow.Check(response.IsSuccessStatusCode, $"root card {A2AConstants.WellKnownCardPath} answers HTTP {(int)response.StatusCode} (report agent)");
    });

    /// <summary>Blocking send: waits for the report to complete.</summary>
    public Task<FlowResult> SendAsync(CancellationToken ct = default) => RunFlowAsync("send", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        var task = (await client.SendMessageAsync(Request("Q3 sales"), ct)).Task
            ?? throw new FlowException("the agent answered with a message, not a task");
        flow.Step($"{agent}: task {task.Id} is {task.Status.State}");
        foreach (var a in task.Artifacts) flow.Step($"artifact '{a.ArtifactId}' ({a.Name ?? "unnamed"}), {a.Parts.Count} part(s): {Short(TextOf(a))}");
        flow.Check(task.Status.State == TaskState.Completed && task.Artifacts.Count > 0, "task completed with artifacts");
    });

    /// <summary>Streaming send: prints each event as it arrives.</summary>
    public Task<FlowResult> StreamAsync(CancellationToken ct = default) => RunFlowAsync("stream", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        var count = 0;
        TaskState? last = null;
        var artifacts = new List<string>();
        await foreach (var e in client.SendStreamingMessageAsync(Request("Streaming topic"), ct))
        {
            count++;
            if (e.Task is { } t) flow.Step($"event {count}: task {t.Id} {t.Status.State}");
            else if (e.Message is { } m) flow.Step($"event {count}: message '{Short(TextOf(m))}'");
            else if (e.StatusUpdate is { } s)
            {
                last = s.Status.State;
                flow.Step($"event {count}: status {s.Status.State}{(s.Status.Message is { } sm ? $" '{TextOf(sm)}'" : "")}");
            }
            else if (e.ArtifactUpdate is { } a)
            {
                artifacts.Add(a.Artifact.ArtifactId);
                flow.Step($"event {count}: artifact '{a.Artifact.ArtifactId}' append={a.Append} lastChunk={a.LastChunk}");
            }
        }
        flow.Check(last == TaskState.Completed && artifacts.Contains("report"), $"stream ended after {count} event(s) with the task completed");
    });

    /// <summary>Send without waiting, then poll GetTask; also asks for no history.</summary>
    public Task<FlowResult> GetAsync(CancellationToken ct = default) => RunFlowAsync("get", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        var first = (await client.SendMessageAsync(Request("Async topic", returnImmediately: true), ct)).Task
            ?? throw new FlowException("no task returned");
        flow.Step($"returnImmediately: task {first.Id} is {first.Status.State}");

        var done = await WaitForStateAsync(client, first.Id, IsTerminal, ct);
        flow.Check(done.Status.State == TaskState.Completed, $"GetTask polled until {done.Status.State}, {done.Artifacts.Count} artifact(s)");

        var noHistory = await client.GetTaskAsync(new GetTaskRequest { Id = first.Id, HistoryLength = 0 }, ct);
        flow.Check(noHistory.History.Count == 0, "GetTask with historyLength 0 returns no history");
        var full = await client.GetTaskAsync(new GetTaskRequest { Id = first.Id }, ct);
        flow.Check(full.History.Count > 0, $"GetTask without historyLength returns {full.History.Count} history message(s)");
    });

    /// <summary>Lists tasks of a context, filters by status and pages.</summary>
    public Task<FlowResult> ListAsync(CancellationToken ct = default) => RunFlowAsync("list", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        var context = Guid.NewGuid().ToString();
        var ok = (await client.SendMessageAsync(Request("List ok", contextId: context), ct)).Task!;
        var bad = (await client.SendMessageAsync(Request("List fail", contextId: context), ct)).Task!;
        flow.Step($"created tasks {ok.Id} ({ok.Status.State}) and {bad.Id} ({bad.Status.State}) in context {context}");

        var all = await client.ListTasksAsync(new ListTasksRequest { ContextId = context }, ct);
        flow.Check(all.Tasks.Count == 2, $"list by context: {all.Tasks.Count} task(s), total {all.TotalSize}");
        var completed = await client.ListTasksAsync(new ListTasksRequest { ContextId = context, Status = TaskState.Completed }, ct);
        flow.Check(completed.Tasks.Select(t => t.Id).SequenceEqual([ok.Id]), "list with status COMPLETED returns only the finished report");
        var failed = await client.ListTasksAsync(new ListTasksRequest { ContextId = context, Status = TaskState.Failed }, ct);
        flow.Check(failed.Tasks.Select(t => t.Id).SequenceEqual([bad.Id]), "list with status FAILED returns only the failed report");

        var page1 = await client.ListTasksAsync(new ListTasksRequest { ContextId = context, PageSize = 1 }, ct);
        flow.Check(page1.Tasks.Count == 1 && page1.NextPageToken.Length > 0, "page size 1 gives one task and a next page token");
        var page2 = await client.ListTasksAsync(new ListTasksRequest { ContextId = context, PageSize = 1, PageToken = page1.NextPageToken }, ct);
        flow.Check(page2.Tasks.Count == 1 && page2.Tasks[0].Id != page1.Tasks[0].Id, "the next page has the other task");
    });

    /// <summary>
    /// Starts a report (returnImmediately) and cancels it; GetTask must then show CANCELED. The agent may finish before the cancel
    /// arrives: then CancelTask answers TASK_NOT_CANCELABLE, the task is read again, and the flow passes only if it is now terminal.
    /// </summary>
    public Task<FlowResult> CancelAsync(CancellationToken ct = default) => RunFlowAsync("cancel", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        var task = (await client.SendMessageAsync(Request("Cancel me", returnImmediately: true), ct)).Task!;
        flow.Step($"task {task.Id} is {task.Status.State}");
        AgentTask canceled;
        try
        {
            canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, ct);
        }
        catch (A2AException e) when (e.Code == A2AErrorCode.TaskNotCancelable)
        {
            var now = (await client.GetTaskAsync(new GetTaskRequest { Id = task.Id }, ct)).Status.State;
            flow.Check(IsTerminal(now), $"TASK_NOT_CANCELABLE is only expected for a finished task; the task is {now}");
            flow.Step($"the task had already finished ({now}): TASK_NOT_CANCELABLE is the expected error for a finished task");
            return;
        }
        flow.Check(canceled.Status.State == TaskState.Canceled, $"CancelTask returned {canceled.Status.State}");
        var got = await WaitForStateAsync(client, task.Id, IsTerminal, ct);
        flow.Check(got.Status.State == TaskState.Canceled, $"GetTask confirms {got.Status.State}");
    });

    /// <summary>Drops a stream mid-task, then attaches again with SubscribeToTask.</summary>
    public Task<FlowResult> ResubscribeAsync(CancellationToken ct = default) => RunFlowAsync("resubscribe", ct, async flow =>
    {
        var agent = AgentOr(Report);
        await using var client = await ClientAsync(agent, ct: ct);
        string? taskId = null;
        var enumerator = client.SendStreamingMessageAsync(Request("Resubscribe topic"), ct).GetAsyncEnumerator(ct);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                taskId ??= enumerator.Current.Task?.Id;
                if (enumerator.Current.ArtifactUpdate is not null) break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        flow.Check(taskId is not null, $"stream opened for task {taskId} and dropped after the first artifact chunk");

        var observed = (await client.GetTaskAsync(new GetTaskRequest { Id = taskId! }, ct)).Status.State;
        var events = new List<StreamResponse>();
        try
        {
            await foreach (var e in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId! }, ct)) events.Add(e);
            flow.Step($"SubscribeToTask delivered {events.Count} event(s); first is {(events[0].Task is { } t ? "the task snapshot, " + t.Status.State : "an update")}");
            flow.Check(events[^1].StatusUpdate?.Status.State == TaskState.Completed, "the subscription ended with the task completed");
        }
        catch (A2AException e) when (e.Code == A2AErrorCode.UnsupportedOperation && IsTerminal(observed))
        {
            flow.Step("the task had already finished, so subscribing is refused (UNSUPPORTED_OPERATION); checking with GetTask");
        }
        var final = await WaitForStateAsync(client, taskId!, IsTerminal, ct);
        flow.Check(final.Status.State == TaskState.Completed && final.Artifacts.Count == 3, $"GetTask: {final.Status.State} with {final.Artifacts.Count} artifacts");
    });
}
