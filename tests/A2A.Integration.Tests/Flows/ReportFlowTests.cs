using System.Text;
using System.Text.Json;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Flows;

[Collection(A2ATestHostCollection.Name)]
public sealed class ReportFlowTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendMessageRequest Send(string text, string? contextId = null, bool returnImmediately = false, Part? extra = null) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
            Role = Role.User,
            Parts = extra is null ? [Part.FromText(text)] : [Part.FromText(text), extra],
        },
        Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
    };

    private Task<IA2AClient> ClientAsync(string binding) => host.CreateClientAsync("report", binding, TestAuth.ApiKey, ct: Ct);

    private static string Text(Artifact artifact) => string.Concat(artifact.Parts.Select(p => p.Text));

    private static void AssertCompletedReport(AgentTask task, string topic)
    {
        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal(["report", "summary", "data"], task.Artifacts.Select(a => a.ArtifactId));

        var report = task.Artifacts[0];
        Assert.Equal("report.md", report.Name);
        Assert.Equal(3, report.Parts.Count);
        Assert.All(report.Parts, p => Assert.Equal("text/markdown", p.MediaType));
        Assert.Contains(topic, Text(report));

        var summary = task.Artifacts[1].Parts.Single().Data!.Value;
        Assert.Equal(topic, summary.GetProperty("topic").GetString());
        Assert.Equal(3, summary.GetProperty("sections").GetInt32());

        var data = task.Artifacts[2].Parts.Single();
        Assert.Equal(("text/csv", "data.csv"), (data.MediaType, data.Filename));
        var csv = Encoding.UTF8.GetString(data.Raw!);
        Assert.StartsWith("topic,metric,value\n", csv);
        Assert.Equal(4, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_blocking_send_returns_completed_with_three_artifacts(string binding)
    {
        await using var client = await ClientAsync(binding);

        var response = await client.SendMessageAsync(Send("Q3 sales"), Ct);

        AssertCompletedReport(Assert.IsType<AgentTask>(response.Task), "Q3 sales");
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_stream_emits_working_chunks_and_completed_in_order(string binding)
    {
        await using var client = await ClientAsync(binding);
        var events = new List<StreamResponse>();
        await foreach (var e in client.SendStreamingMessageAsync(Send("Streaming topic"), Ct)) events.Add(e);

        Assert.NotNull(events[0].Task);
        var updates = events.Skip(1).ToList();
        Assert.All(updates, e => Assert.True(e.StatusUpdate is not null || e.ArtifactUpdate is not null));

        var states = updates.Where(e => e.StatusUpdate is not null).Select(e => e.StatusUpdate!.Status.State).ToList();
        Assert.Equal([TaskState.Working, TaskState.Completed], states);
        Assert.Equal(TaskState.Working, updates[0].StatusUpdate!.Status.State);
        Assert.Equal("Collecting data", updates[0].StatusUpdate!.Status.Message!.Parts.Single().Text);
        Assert.Equal(TaskState.Completed, updates[^1].StatusUpdate!.Status.State);

        var artifacts = updates.Where(e => e.ArtifactUpdate is not null).Select(e => e.ArtifactUpdate!).ToList();
        Assert.Equal(["report", "report", "report", "summary", "data"], artifacts.Select(a => a.Artifact.ArtifactId));
        Assert.Equal([false, true, true], artifacts.Take(3).Select(a => a.Append));
        Assert.Equal([false, false, true], artifacts.Take(3).Select(a => a.LastChunk));
        Assert.All(artifacts.Take(3), a => Assert.Equal("report.md", a.Artifact.Name));
        Assert.Equal("text/csv", artifacts[4].Artifact.Parts.Single().MediaType);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_return_immediately_then_get_task_until_completed(string binding)
    {
        await using var client = await ClientAsync(binding);

        var first = (await client.SendMessageAsync(Send("Async topic", returnImmediately: true), Ct)).Task!;
        Assert.NotEqual(TaskState.Completed, first.Status.State);

        var task = first;
        while (task.Status.State != TaskState.Completed)
        {
            await Task.Delay(10, Ct);
            task = await client.GetTaskAsync(new GetTaskRequest { Id = first.Id }, Ct);
            Assert.NotEqual(TaskState.Failed, task.Status.State);
        }

        AssertCompletedReport(task, "Async topic");
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_list_tasks_filters_by_context_and_status(string binding)
    {
        await using var client = await ClientAsync(binding);
        var contextId = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();
        var ok = (await client.SendMessageAsync(Send("List ok", contextId), Ct)).Task!;
        var bad = (await client.SendMessageAsync(Send("List fail", contextId), Ct)).Task!;
        await client.SendMessageAsync(Send("List other", other), Ct);

        var inContext = await client.ListTasksAsync(new ListTasksRequest { ContextId = contextId }, Ct);
        Assert.Equal(new[] { ok.Id, bad.Id }.Order(), inContext.Tasks.Select(t => t.Id).Order());

        var completed = await client.ListTasksAsync(new ListTasksRequest { ContextId = contextId, Status = TaskState.Completed }, Ct);
        Assert.Equal([ok.Id], completed.Tasks.Select(t => t.Id));
        var failed = await client.ListTasksAsync(new ListTasksRequest { ContextId = contextId, Status = TaskState.Failed }, Ct);
        Assert.Equal([bad.Id], failed.Tasks.Select(t => t.Id));
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_cancel_mid_task_ends_canceled(string binding)
    {
        await using var client = await ClientAsync(binding);
        string? taskId = null;
        var canceled = false;

        await foreach (var e in client.SendStreamingMessageAsync(Send("Cancel me"), Ct))
        {
            taskId ??= e.Task?.Id;
            if (e.ArtifactUpdate is not null && !canceled)
            {
                canceled = true;
                var result = await client.CancelTaskAsync(new CancelTaskRequest { Id = taskId! }, Ct);
                Assert.Equal(TaskState.Canceled, result.Status.State);
            }
        }

        Assert.True(canceled);
        var task = await client.GetTaskAsync(new GetTaskRequest { Id = taskId! }, Ct);
        Assert.Equal(TaskState.Canceled, task.Status.State);
        Assert.True(task.Artifacts.Count < 3);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_cancel_completed_task_not_cancelable(string binding)
    {
        await using var client = await ClientAsync(binding);
        var task = (await client.SendMessageAsync(Send("Done topic"), Ct)).Task!;

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct));

        Assert.Equal(A2AErrorCode.TaskNotCancelable, ex.Code);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_fail_trigger_ends_failed(string binding)
    {
        await using var client = await ClientAsync(binding);

        var task = (await client.SendMessageAsync(Send("please FAIL now"), Ct)).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
        Assert.Equal("Report generation failed for topic 'please FAIL now'", task.Status.Message!.Parts.Single().Text);
        Assert.Empty(task.Artifacts);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_rejects_image_input_with_content_type_not_supported(string binding)
    {
        await using var client = await ClientAsync(binding);
        var image = Part.FromRaw([0x89, 0x50, 0x4E, 0x47], "image/png", "pic.png");

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.SendMessageAsync(Send("topic", extra: image), Ct));

        Assert.Equal(A2AErrorCode.ContentTypeNotSupported, ex.Code);
    }
}
