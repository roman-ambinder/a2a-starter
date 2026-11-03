using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;

/// <summary>Behaviors keyed by messageId prefix, as the TCK scenarios (scenarios/*.feature) define them.</summary>
internal sealed class TckAgent : IAgentHandler
{
    public const string Name = "tck";

    public static AgentCard Card { get; } = new()
    {
        Name = "A2A .NET SDK System Under Test",
        Description = "System under test for A2A TCK conformance",
        Version = "1.0.0",
        Provider = new AgentProvider { Organization = "Ambinder.A2A", Url = "https://github.com/roman-ambinder" },
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "tck", Name = "TCK Conformance", Description = "Handles TCK conformance test messages", Tags = ["tck"] }],
    };

    private static readonly TimeSpan StreamingTimeout = TimeSpan.FromSeconds(2);

    private delegate ValueTask Behavior(Helpers h);

    /// <summary>The behaviours, in match order: the first whose prefix the messageId starts with runs.</summary>
    private static readonly (string Prefix, Behavior Run)[] Table =
    [
        ("tck-stream-artifact-chunked", async h =>
        {
            await h.Working();
            var chunkId = Guid.NewGuid().ToString("N");
            await h.Sink.AddArtifactAsync(new Artifact { ArtifactId = chunkId, Parts = [Part.FromText("chunk-1 ")] }, true, false, h.Ct);
            await h.Sink.AddArtifactAsync(new Artifact { ArtifactId = chunkId, Parts = [Part.FromText("chunk-2")] }, true, true, h.Ct);
            await h.Complete();
        }),
        ("test-resubscribe-message-id", async h =>
        {
            await h.Working();
            await Task.Delay(StreamingTimeout * 2, h.Ct);
            await h.Complete();
        }),
        ("tck-stream-artifact-text", async h => { await h.Working(); await h.Add(h.Text("Streamed text content")); await h.Complete(); }),
        ("tck-stream-artifact-file", async h => { await h.Working(); await h.Add(h.File()); await h.Complete(); }),
        ("tck-stream-ordering-001", async h => { await h.Working(); await h.Add(h.Text("Ordered output")); await h.Complete(); }),
        ("tck-artifact-file-url", async h =>
        {
            await h.Add(new Artifact
            {
                ArtifactId = Guid.NewGuid().ToString("N"),
                Parts = [new Part { Url = "https://example.com/output.txt", MediaType = "text/plain", Filename = "output.txt" }],
            });
            await h.Complete();
        }),
        ("tck-message-response", h => h.Sink.ReplyAsync(h.Agent("Direct message response"), h.Ct)),
        ("tck-input-required", h => h.Sink.UpdateStatusAsync(TaskState.InputRequired, h.Agent("Input required"), h.Ct)),
        ("tck-complete-task", h => h.Complete(h.Agent("Hello from TCK"))),
        ("tck-artifact-text", async h => { await h.Add(h.Text("Generated text content")); await h.Complete(); }),
        ("tck-artifact-file", async h => { await h.Add(h.File()); await h.Complete(); }),
        ("tck-artifact-data", async h =>
        {
            await h.Add(new Artifact
            {
                ArtifactId = Guid.NewGuid().ToString("N"),
                Parts = [new Part { Data = JsonDocument.Parse("""{"key":"value","count":42}""").RootElement.Clone() }],
            });
            await h.Complete();
        }),
        ("tck-reject-task", h => h.Sink.UpdateStatusAsync(TaskState.Rejected, h.Agent("rejected"), h.Ct)),
        ("tck-stream-001", async h => { await h.Working(); await h.Add(h.Text("Stream hello from TCK")); await h.Complete(); }),
        ("tck-stream-002", h => h.Complete()),
        ("tck-stream-003", async h => { await h.Working(); await h.Add(h.Text("Stream task lifecycle")); await h.Complete(); }),
    ];

    /// <summary>The messageId prefixes this agent handles, in match order. Mirrors <c>tck/sut-behaviors.yaml</c>.</summary>
    public static IReadOnlyList<string> Behaviors { get; } = Table.Select(t => t.Prefix).ToArray();

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var id = context.Message.MessageId;
        var helpers = new Helpers(sink, ct);
        foreach (var (prefix, run) in Table)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
            {
                await run(helpers);
                return;
            }
        }
        // A behaviour the TCK expects but the SUT does not list must fail its test, never pass or skip.
        await sink.UpdateStatusAsync(TaskState.Failed, helpers.Agent("behaviour not implemented: " + id), ct);
    }

    private sealed class Helpers(IAgentEventSink sink, CancellationToken ct)
    {
        public IAgentEventSink Sink => sink;
        public CancellationToken Ct => ct;

        public Artifact Text(string text) => new() { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(text)] };

        public Artifact File() => new()
        {
            ArtifactId = Guid.NewGuid().ToString("N"),
            Parts = [new Part { Raw = "tck"u8.ToArray(), MediaType = "text/plain", Filename = "output.txt" }],
        };

        public Message Agent(string text) => new() { MessageId = Guid.NewGuid().ToString("N"), Role = Role.Agent, Parts = [Part.FromText(text)] };

        public ValueTask Working() => sink.UpdateStatusAsync(TaskState.Working, null, ct);
        public ValueTask Complete(Message? m = null) => sink.UpdateStatusAsync(TaskState.Completed, m, ct);
        public ValueTask Add(Artifact a) => sink.AddArtifactAsync(a, false, true, ct);
    }
}
