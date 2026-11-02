using System.Text.RegularExpressions;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Flows;

[Collection(A2ATestHostCollection.Name)]
public sealed class BookingFlowTests(A2ATestHost host)
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

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Booking_multi_turn_completes_on_same_task_and_context(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var first = (await client.SendMessageAsync(Send("Book pArIs"), Ct)).Task!;
        Assert.Equal(TaskState.InputRequired, first.Status.State);
        Assert.Equal("Which date? (YYYY-MM-DD)", first.Status.Message!.Parts.Single().Text);

        var second = (await client.SendMessageAsync(Send("2027-03-15", first.Id, first.ContextId), Ct)).Task!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContextId, second.ContextId);
        Assert.Equal(TaskState.Completed, second.Status.State);
        var artifact = Assert.Single(second.Artifacts);
        Assert.Equal("booking", artifact.ArtifactId);
        var data = artifact.Parts.Single().Data!.Value;
        Assert.Equal("Paris", data.GetProperty("city").GetString());
        Assert.Equal("2027-03-15", data.GetProperty("date").GetString());
        Assert.Matches(new Regex("^BK-[0-9A-F]{6}$"), data.GetProperty("confirmation").GetString()!);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Booking_invalid_date_asks_again(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);
        var first = (await client.SendMessageAsync(Send("book tokyo"), Ct)).Task!;

        var bad = (await client.SendMessageAsync(Send("2027-13-45", first.Id, first.ContextId), Ct)).Task!;
        Assert.Equal(TaskState.InputRequired, bad.Status.State);
        Assert.Equal("Please use YYYY-MM-DD.", bad.Status.Message!.Parts.Single().Text);

        var good = (await client.SendMessageAsync(Send("2027-01-02", first.Id, first.ContextId), Ct)).Task!;
        Assert.Equal(TaskState.Completed, good.Status.State);
        Assert.Equal("Tokyo", good.Artifacts.Single().Parts.Single().Data!.Value.GetProperty("city").GetString());
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Booking_unknown_city_is_rejected(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var task = (await client.SendMessageAsync(Send("book Atlantis"), Ct)).Task!;

        Assert.Equal(TaskState.Rejected, task.Status.State);
        Assert.Equal("Sorry, we don't fly to Atlantis.", task.Status.Message!.Parts.Single().Text);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Booking_input_without_city_asks_where_to(string binding)
    {
        await using var client = await host.CreateClientAsync("booking", binding, TestAuth.Client, ct: Ct);

        var task = (await client.SendMessageAsync(Send("hello"), Ct)).Task!;

        Assert.Equal(TaskState.InputRequired, task.Status.State);
        Assert.Equal("Where to? Try 'book Paris'.", task.Status.Message!.Parts.Single().Text);
    }
}
