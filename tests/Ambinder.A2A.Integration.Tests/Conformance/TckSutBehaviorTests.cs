using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;

namespace Ambinder.A2A.Integration.Tests.Conformance;

/// <summary>
/// The TCK SUT must claim exactly the behaviours it implements (<c>tck/sut-behaviors.yaml</c>), and a claimed-but-missing
/// behaviour must fail the TCK test rather than pass or skip.
/// </summary>
public sealed class TckSutBehaviorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One row per yaml entry: id, response kind, terminal state (null for a message reply).</summary>
    public static TheoryData<string, string, string?> Entries()
    {
        var data = new TheoryData<string, string, string?>();
        foreach (var e in ReadYaml())
            data.Add(e.Id, e.Response, e.TerminalState);
        return data;
    }

    [Fact]
    public void Yaml_lists_exactly_the_dispatcher_behaviors()
    {
        var ids = ReadYaml().Select(e => e.Id).ToList();
        Assert.Equal(TckAgent.Behaviors.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory, MemberData(nameof(Entries))]
    public async Task Each_listed_behavior_reaches_its_terminal_state(string id, string response, string? terminal)
    {
        var sink = new RecordingSink();
        await new TckAgent().ExecuteAsync(ContextFor(id + "-1"), sink, Ct);

        Assert.Equal(response == "message", sink.Reply is not null);
        if (terminal is null)
        {
            Assert.NotNull(sink.Reply);
            Assert.DoesNotContain(TaskState.Failed, sink.States);
            return;
        }

        Assert.Null(sink.Reply);
        var expected = terminal switch
        {
            "TASK_STATE_COMPLETED" => TaskState.Completed,
            "TASK_STATE_INPUT_REQUIRED" => TaskState.InputRequired,
            "TASK_STATE_REJECTED" => TaskState.Rejected,
            _ => throw new ArgumentException(terminal),
        };
        Assert.Equal(expected, sink.States[^1]);
    }

    [Theory]
    [InlineData("tck-not-a-behavior-1")]
    [InlineData("test-not-a-behavior-1")]
    public async Task Unknown_tck_or_test_behavior_fails(string messageId)
    {
        var sink = new RecordingSink();
        await new TckAgent().ExecuteAsync(ContextFor(messageId), sink, Ct);

        Assert.Equal(TaskState.Failed, sink.States[^1]);
        Assert.Equal(
            $"SUT behaviour not implemented: {messageId}. Add it to tests/Ambinder.A2A.Tck.Sut and tck/sut-behaviors.yaml.",
            sink.StatusMessages[^1]!.Parts[0].Text);
    }

    [Fact]
    public async Task Other_messages_complete()
    {
        var sink = new RecordingSink();
        await new TckAgent().ExecuteAsync(ContextFor("hello-1"), sink, Ct);

        Assert.Equal(TaskState.Completed, sink.States[^1]);
    }

    [Fact]
    public void Card_declares_push_and_streaming()
    {
        Assert.True(TckAgent.Card.Capabilities.PushNotifications);
        Assert.True(TckAgent.Card.Capabilities.Streaming);
    }

    private static AgentRequestContext ContextFor(string messageId) => new(
        TckAgent.Name, "task", "ctx",
        new Message { MessageId = messageId, Role = Role.User, Parts = [Part.FromText("x")] },
        null, new System.Security.Claims.ClaimsPrincipal(), [], null);

    private sealed record Entry(string Id, string Scenario, string Response, string? TerminalState, string[] Requirements);

    /// <summary>The file is YAML written in JSON syntax, so System.Text.Json reads it.</summary>
    private static List<Entry> ReadYaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "tck", "sut-behaviors.yaml")));
        return doc.RootElement.GetProperty("behaviors").EnumerateArray()
            .Select(e => new Entry(
                e.GetProperty("id").GetString()!,
                e.GetProperty("scenario").GetString()!,
                e.GetProperty("response").GetString()!,
                e.GetProperty("terminalState").GetString(),
                [.. e.GetProperty("requirements").EnumerateArray().Select(r => r.GetString()!)]))
            .ToList();
    }

    private sealed class RecordingSink : IAgentEventSink
    {
        public Message? Reply { get; private set; }
        public List<TaskState> States { get; } = [];
        public List<Message?> StatusMessages { get; } = [];

        public ValueTask ReplyAsync(Message message, CancellationToken ct) { Reply = message; return ValueTask.CompletedTask; }

        public ValueTask UpdateStatusAsync(TaskState state, Message? message, CancellationToken ct)
        {
            States.Add(state);
            StatusMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
