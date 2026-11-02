using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Flows;

[Collection(A2ATestHostCollection.Name)]
public sealed class SecureFlowTests(A2ATestHost host)
{
    private const string Audit = "https://example.com/a2a/ext/audit/v1";
    private const string AuthRequiredText = "Role Reports.Approve required. Retry with an approver token.";
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

    private Task<IA2AClient> ClientAsync(string binding, TestAuth auth, bool audit = true)
        => host.CreateClientAsync("secure", binding, auth, audit ? [Audit] : null, Ct);

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_without_audit_extension_fails_extension_required(string binding)
    {
        await using var client = await ClientAsync(binding, TestAuth.Approver, audit: false);

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.SendMessageAsync(Send("approve r-1"), Ct));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, ex.Code);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_client_token_pauses_auth_required(string binding)
    {
        await using var client = await ClientAsync(binding, TestAuth.Client);

        var task = (await client.SendMessageAsync(Send("approve r-2"), Ct)).Task!;

        Assert.Equal(TaskState.AuthRequired, task.Status.State);
        Assert.Equal(AuthRequiredText, task.Status.Message!.Parts.Single().Text);
        Assert.Empty(task.Artifacts);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_approver_token_resumes_and_completes(string binding)
    {
        await using var weak = await ClientAsync(binding, TestAuth.Client);
        await using var strong = await ClientAsync(binding, TestAuth.Approver);
        var paused = (await weak.SendMessageAsync(Send("approve r-3"), Ct)).Task!;
        Assert.Equal(TaskState.AuthRequired, paused.Status.State);

        // Same caller still lacks the role: paused again with the same message.
        var again = (await weak.SendMessageAsync(Send("retry", paused.Id, paused.ContextId), Ct)).Task!;
        Assert.Equal(TaskState.AuthRequired, again.Status.State);
        Assert.Equal(AuthRequiredText, again.Status.Message!.Parts.Single().Text);

        var done = (await strong.SendMessageAsync(Send("approved, go", paused.Id, paused.ContextId), Ct)).Task!;

        Assert.Equal(paused.Id, done.Id);
        Assert.Equal(TaskState.Completed, done.Status.State);
        var artifact = Assert.Single(done.Artifacts);
        Assert.Equal("approval", artifact.ArtifactId);
        var data = artifact.Parts.Single().Data!.Value;
        Assert.Equal("r-3", data.GetProperty("id").GetString());
        Assert.Equal("a2a-approver", data.GetProperty("approvedBy").GetString());
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_approver_can_approve_directly(string binding)
    {
        await using var client = await ClientAsync(binding, TestAuth.Approver);

        var task = (await client.SendMessageAsync(Send("approve r-4"), Ct)).Task!;

        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal("r-4", task.Artifacts.Single().Parts.Single().Data!.Value.GetProperty("id").GetString());
    }
}
