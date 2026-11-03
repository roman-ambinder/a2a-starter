using A2A.Protocol;
using A2A.Server.Agents;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// The TCK SUT must claim exactly the behaviours it implements (<c>tck/sut-behaviors.yaml</c>), and a claimed-but-missing
/// behaviour must fail the TCK test rather than pass or skip.
/// </summary>
public sealed class TckSutBehaviorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One row per yaml entry: id, response kind, terminal state.</summary>
    public static TheoryData<string, string, string> Entries()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var e in ReadYaml())
            data.Add(e.Id, e.Response, e.Terminal);
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
    public async Task Each_listed_behavior_reaches_its_terminal_state(string id, string response, string terminal)
    {
        var sink = new RecordingSink();
        await new TckAgent().ExecuteAsync(ContextFor(id + "-1"), sink, Ct);

        Assert.Equal(response == "message", sink.Reply is not null);
        if (terminal == "none")
        {
            Assert.NotNull(sink.Reply);
            Assert.DoesNotContain(TaskState.Failed, sink.States);
            return;
        }

        Assert.Null(sink.Reply);
        var expected = terminal switch
        {
            "completed" => TaskState.Completed,
            "input-required" => TaskState.InputRequired,
            "rejected" => TaskState.Rejected,
            _ => throw new ArgumentException(terminal),
        };
        Assert.Equal(expected, sink.States[^1]);
    }

    [Fact]
    public async Task Unknown_behavior_fails()
    {
        var sink = new RecordingSink();
        await new TckAgent().ExecuteAsync(ContextFor("tck-not-a-behavior-1"), sink, Ct);

        Assert.Equal(TaskState.Failed, sink.States[^1]);
        var text = sink.StatusMessages[^1]!.Parts[0].Text!;
        Assert.StartsWith("behaviour not implemented: tck-not-a-behavior-1", text, StringComparison.Ordinal);
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

    private sealed record Entry(string Id, string Response, string Terminal);

    private static List<Entry> ReadYaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "A2A.Reference.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var entries = new List<Entry>();
        string? id = null, response = null, terminal = null;
        void Flush()
        {
            if (id is not null) entries.Add(new Entry(id, response!, terminal!));
            id = response = terminal = null;
        }
        foreach (var raw in File.ReadAllLines(Path.Combine(dir.FullName, "tck", "sut-behaviors.yaml")))
        {
            var line = raw.Trim().TrimStart('-').Trim();
            if (line.StartsWith("id:", StringComparison.Ordinal)) { Flush(); id = line[3..].Trim(); }
            else if (line.StartsWith("response:", StringComparison.Ordinal)) response = line[9..].Trim();
            else if (line.StartsWith("terminal:", StringComparison.Ordinal)) terminal = line[9..].Trim();
        }
        Flush();
        return entries;
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
