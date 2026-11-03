using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.4 / design D9 on the wire: the server owns every <c>taskId</c>; a follow-up takes its <c>contextId</c> from the task;
/// a client <c>contextId</c> is kept or rejected, never replaced. Runs against the booking sample (it pauses at INPUT_REQUIRED).
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class TaskIdRulesTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendMessageRequest Send(string text, string? taskId = null, string? contextId = null) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = contextId,
            Role = Role.User,
            Parts = [Part.FromText(text)],
        },
    };

    private static async Task<A2AException> Rejected(Func<Task> call) => await Assert.ThrowsAsync<A2AException>(call);

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Unknown_task_id_is_task_not_found(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var error = await Rejected(() => client.SendMessageAsync(Send("2027-03-15", Guid.NewGuid().ToString()), Ct));

        Assert.Equal(A2AErrorCode.TaskNotFound, error.Code);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Task_id_of_another_owner_is_task_not_found_like_an_unknown_one(string binding)
    {
        await using var owner = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        await using var other = await host.CreateClientAsync("booking", binding, TestAuth.Approver, ct: Ct);
        var first = (await owner.SendMessageAsync(Send("Book Paris"), Ct)).Task!;
        Assert.Equal(TaskState.InputRequired, first.Status.State);

        var foreign = await Rejected(() => other.SendMessageAsync(Send("2027-03-15", first.Id, first.ContextId), Ct));
        var unknownId = Guid.NewGuid().ToString();
        var unknown = await Rejected(() => other.SendMessageAsync(Send("2027-03-15", unknownId, first.ContextId), Ct));

        Assert.Equal(A2AErrorCode.TaskNotFound, foreign.Code);
        Assert.Equal(unknown.Code, foreign.Code);
        // No leak: the message must not tell a foreign id from an unknown one (the ids themselves differ, so mask them).
        Assert.Equal(unknown.Message.Replace(unknownId, "<id>"), foreign.Message.Replace(first.Id, "<id>"));
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Follow_up_with_only_a_task_id_takes_the_context_id_from_the_task(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var first = (await client.SendMessageAsync(Send("Book Paris"), Ct)).Task!;

        var second = (await client.SendMessageAsync(Send("2027-03-15", first.Id), Ct)).Task!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContextId, second.ContextId);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Task_id_and_context_id_that_differ_are_invalid_params(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var first = (await client.SendMessageAsync(Send("Book Paris"), Ct)).Task!;

        var error = await Rejected(() => client.SendMessageAsync(Send("2027-03-15", first.Id, "another-context"), Ct));

        Assert.Equal(A2AErrorCode.InvalidParams, error.Code);
        // The task is untouched and still waits for its answer.
        var again = await client.GetTaskAsync(new GetTaskRequest { Id = first.Id }, Ct);
        Assert.Equal(TaskState.InputRequired, again.Status.State);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Client_context_id_on_a_new_task_is_kept(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var contextId = new string('c', 256); // the longest accepted

        var task = (await client.SendMessageAsync(Send("Book Paris", contextId: contextId), Ct)).Task!;

        Assert.Equal(contextId, task.ContextId);
    }

    public static TheoryData<string, string> BadContextIds()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in TransportMatrix.Bindings)
        {
            data.Add(binding, new string('c', 257));
            data.Add(binding, "ctx\n1");
        }
        return data;
    }

    [Theory, MemberData(nameof(BadContextIds))]
    public async Task Too_long_or_control_character_context_id_is_invalid_params(string binding, string contextId)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var error = await Rejected(() => client.SendMessageAsync(Send("Book Paris", contextId: contextId), Ct));

        Assert.Equal(A2AErrorCode.InvalidParams, error.Code);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task New_task_without_context_id_gets_one_in_the_response(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var task = (await client.SendMessageAsync(Send("Book Paris"), Ct)).Task!;

        Assert.False(string.IsNullOrEmpty(task.ContextId));
        Assert.False(string.IsNullOrEmpty(task.Id));
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Follow_up_on_a_terminal_task_is_unsupported_operation(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var first = (await client.SendMessageAsync(Send("Book Paris"), Ct)).Task!;
        var done = (await client.SendMessageAsync(Send("2027-03-15", first.Id, first.ContextId), Ct)).Task!;
        Assert.Equal(TaskState.Completed, done.Status.State);

        var error = await Rejected(() => client.SendMessageAsync(Send("again", first.Id, first.ContextId), Ct));

        Assert.Equal(A2AErrorCode.UnsupportedOperation, error.Code);
    }
}
