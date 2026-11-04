using Ambinder.A2A.Client;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Integration.Tests.Conformance;

/// <summary>Spec 03 D13 end to end, on every transport: who may move a task, and what happens on an illegal move.</summary>
[Collection(MinimalServerCollection.Name)]
public sealed class TaskStateTransitionTests(MinimalServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<IA2AClient> CreateAsync(string binding)
    {
        Assert.NotNull(server.App);
        using var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{MinimalServer.BaseUrl}/agents/{MinimalServer.StatesAgentName}"), Ct);
        return A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = binding });
    }

    private static SendMessageRequest Send(string prefix, string? taskId = null, string? contextId = null, bool returnImmediately = false) => new()
    {
        Message = new Message
        {
            MessageId = prefix + Guid.NewGuid().ToString("N"), TaskId = taskId, ContextId = contextId, Role = Role.User, Parts = [Part.FromText("x")],
        },
        Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
    };

    private static async Task<AgentTask> WaitForStateAsync(IA2AClient client, string id, TaskState state)
    {
        for (var i = 0; i < 200; i++)
        {
            var task = await client.GetTaskAsync(new GetTaskRequest { Id = id }, Ct);
            if (task.Status.State == state) return task;
            await Task.Delay(25, Ct);
        }
        throw new TimeoutException($"Task {id} never reached {state}.");
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Agent_moving_a_paused_task_in_the_same_run_fails_it(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("pause-then-complete-"), Ct)).Task!;

        Assert.Equal(TaskState.InputRequired, task.Status.State);
        await WaitForStateAsync(client, task.Id, TaskState.Failed);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Cancel_of_a_paused_task_ends_canceled(string binding)
    {
        await using var client = await CreateAsync(binding);
        foreach (var prefix in new[] { "pause-", "pause-auth-" })
        {
            var task = (await client.SendMessageAsync(Send(prefix), Ct)).Task!;

            var canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct);

            Assert.Equal(TaskState.Canceled, canceled.Status.State);
            Assert.Equal(TaskState.Canceled, (await client.GetTaskAsync(new GetTaskRequest { Id = task.Id }, Ct)).Status.State);
        }
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Illegal_cancel_handler_move_on_a_paused_task_is_rejected_and_the_task_ends_canceled(string binding)
    {
        await using var client = await CreateAsync(binding);
        foreach (var prefix in new[] { "pause-bad-cancel-", "pause-auth-bad-cancel-" })
        {
            var task = (await client.SendMessageAsync(Send(prefix), Ct)).Task!;

            var canceled = await client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct);

            Assert.Equal(TaskState.Canceled, canceled.Status.State);
            Assert.Equal(TaskState.Canceled, (await client.GetTaskAsync(new GetTaskRequest { Id = task.Id }, Ct)).Status.State);
        }
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Illegal_cancel_handler_move_on_a_working_task_fails_it(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("work-bad-cancel-", returnImmediately: true), Ct)).Task!;

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct));

        Assert.Equal(A2AErrorCode.TaskNotCancelable, ex.Code);
        await WaitForStateAsync(client, task.Id, TaskState.Failed);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Continuation_of_a_paused_task_goes_submitted_then_working(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("pause-"), Ct)).Task!;

        var states = new List<TaskState>();
        await foreach (var e in client.SendStreamingMessageAsync(Send("go-", task.Id, task.ContextId), Ct))
        {
            if (e.Task is { } t) states.Add(t.Status.State);
            else if (e.StatusUpdate is { } u) states.Add(u.Status.State);
        }

        Assert.Equal([TaskState.Submitted, TaskState.Working, TaskState.Completed], states);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Second_cancel_returns_the_canceled_task(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("pause-"), Ct)).Task!;
        await client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct);

        var again = await client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct);

        Assert.Equal(task.Id, again.Id);
        Assert.Equal(TaskState.Canceled, again.Status.State);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Cancel_of_a_completed_task_is_not_cancelable(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("complete-"), Ct)).Task!;
        Assert.Equal(TaskState.Completed, task.Status.State);

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, Ct));

        Assert.Equal(A2AErrorCode.TaskNotCancelable, ex.Code);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Agent_setting_submitted_fails_the_task(string binding)
    {
        await using var client = await CreateAsync(binding);
        var task = (await client.SendMessageAsync(Send("set-submitted-"), Ct)).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
    }
}
