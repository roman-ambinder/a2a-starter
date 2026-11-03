using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Hosting;

// System under test for the official A2A TCK (https://github.com/a2aproject/a2a-tck). Public SDK APIs only.
// Run:  dotnet run --project tests/A2A.Tck.Sut -- --port 9999 --grpc-port 10000
// Then: ./run_tck.py --sut-host http://localhost:9999   (from an a2a-tck checkout)
var port = Arg("--port", 9999);
var grpcPort = Arg("--grpc-port", port + 1);
var baseUrl = $"http://localhost:{port}";

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services
    .AddA2AServer(o =>
    {
        o.PublicBaseUrl = baseUrl;
        o.DefaultAgent = TckAgent.Name;
        o.GrpcPorts[TckAgent.Name] = grpcPort;
    })
    .AddAgent<TckAgent>(TckAgent.Name, _ => TckAgent.Card);
builder.ConfigureA2AKestrel();

var app = builder.Build();
app.MapA2A();
app.MapA2AGrpc();
app.Run();

int Arg(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}

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
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = false },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "tck", Name = "TCK Conformance", Description = "Handles TCK conformance test messages", Tags = ["tck"] }],
    };

    private static readonly TimeSpan StreamingTimeout = TimeSpan.FromSeconds(2);

    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var id = context.Message.MessageId;
        Artifact Text(string text) => new() { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(text)] };
        Artifact File() => new()
        {
            ArtifactId = Guid.NewGuid().ToString("N"),
            Parts = [new Part { Raw = "tck"u8.ToArray(), MediaType = "text/plain", Filename = "output.txt" }],
        };
        Message Agent(string text) => new() { MessageId = Guid.NewGuid().ToString("N"), Role = Role.Agent, Parts = [Part.FromText(text)] };

        async ValueTask Working() => await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        async ValueTask Complete(Message? m = null) => await sink.UpdateStatusAsync(TaskState.Completed, m, ct);
        async ValueTask Add(Artifact a) => await sink.AddArtifactAsync(a, false, true, ct);

        switch (id)
        {
            case var s when s.StartsWith("tck-stream-artifact-chunked", StringComparison.Ordinal):
                await Working();
                var chunkId = Guid.NewGuid().ToString("N");
                await sink.AddArtifactAsync(new Artifact { ArtifactId = chunkId, Parts = [Part.FromText("chunk-1 ")] }, true, false, ct);
                await sink.AddArtifactAsync(new Artifact { ArtifactId = chunkId, Parts = [Part.FromText("chunk-2")] }, true, true, ct);
                await Complete();
                return;
            case var s when s.StartsWith("test-resubscribe-message-id", StringComparison.Ordinal):
                await Working();
                await Task.Delay(StreamingTimeout * 2, ct);
                await Complete();
                return;
            case var s when s.StartsWith("tck-stream-artifact-text", StringComparison.Ordinal):
                await Working(); await Add(Text("Streamed text content")); await Complete(); return;
            case var s when s.StartsWith("tck-stream-artifact-file", StringComparison.Ordinal):
                await Working(); await Add(File()); await Complete(); return;
            case var s when s.StartsWith("tck-stream-ordering-001", StringComparison.Ordinal):
                await Working(); await Add(Text("Ordered output")); await Complete(); return;
            case var s when s.StartsWith("tck-artifact-file-url", StringComparison.Ordinal):
                await Add(new Artifact
                {
                    ArtifactId = Guid.NewGuid().ToString("N"),
                    Parts = [new Part { Url = "https://example.com/output.txt", MediaType = "text/plain", Filename = "output.txt" }],
                });
                await Complete();
                return;
            case var s when s.StartsWith("tck-message-response", StringComparison.Ordinal):
                await sink.ReplyAsync(Agent("Direct message response"), ct); return;
            case var s when s.StartsWith("tck-input-required", StringComparison.Ordinal):
                await sink.UpdateStatusAsync(TaskState.InputRequired, Agent("Input required"), ct); return;
            case var s when s.StartsWith("tck-complete-task", StringComparison.Ordinal):
                await Complete(Agent("Hello from TCK")); return;
            case var s when s.StartsWith("tck-artifact-text", StringComparison.Ordinal):
                await Add(Text("Generated text content")); await Complete(); return;
            case var s when s.StartsWith("tck-artifact-file", StringComparison.Ordinal):
                await Add(File()); await Complete(); return;
            case var s when s.StartsWith("tck-artifact-data", StringComparison.Ordinal):
                await Add(new Artifact
                {
                    ArtifactId = Guid.NewGuid().ToString("N"),
                    Parts = [new Part { Data = JsonDocument.Parse("""{"key":"value","count":42}""").RootElement.Clone() }],
                });
                await Complete();
                return;
            case var s when s.StartsWith("tck-reject-task", StringComparison.Ordinal):
                await sink.UpdateStatusAsync(TaskState.Rejected, Agent("rejected"), ct); return;
            case var s when s.StartsWith("tck-stream-001", StringComparison.Ordinal):
                await Working(); await Add(Text("Stream hello from TCK")); await Complete(); return;
            case var s when s.StartsWith("tck-stream-002", StringComparison.Ordinal):
                await Complete(); return;
            case var s when s.StartsWith("tck-stream-003", StringComparison.Ordinal):
                await Working(); await Add(Text("Stream task lifecycle")); await Complete(); return;
            default:
                await Complete(Agent("Unhandled messageId prefix: " + id)); return;
        }
    }
}
