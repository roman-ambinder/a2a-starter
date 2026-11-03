using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.1.6 (TCK STREAM-SUB-002): SubscribeToTask works on any task that is not terminal, including an interrupted one,
/// and its stream ends when the task reaches a terminal state.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class SubscribeLifecycleTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendMessageRequest Send(string text, string? taskId = null, string? contextId = null) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"), TaskId = taskId, ContextId = contextId, Role = Role.User, Parts = [Part.FromText(text)],
        },
    };

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Subscribe_to_input_required_task_stays_open_until_terminal_state(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var task = (await client.SendMessageAsync(Send("book Paris"), Ct)).Task!;
        Assert.Equal(TaskState.InputRequired, task.Status.State);

        var events = new List<StreamResponse>();
        await using (var stream = client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = task.Id }, Ct).GetAsyncEnumerator(Ct))
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal(TaskState.InputRequired, stream.Current.Task!.Status.State);

            // The subscription is registered before the snapshot is sent, so the continuation's events reach it.
            var done = (await client.SendMessageAsync(Send("2027-03-15", task.Id, task.ContextId), Ct)).Task!;
            Assert.Equal(TaskState.Completed, done.Status.State);

            while (await stream.MoveNextAsync()) events.Add(stream.Current);
        }

        Assert.NotEmpty(events);
        var last = events[^1];
        var state = last.StatusUpdate?.Status.State ?? last.Task?.Status.State;
        Assert.Equal(TaskState.Completed, state);
    }
}
