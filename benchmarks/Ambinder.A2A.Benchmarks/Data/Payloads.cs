using System.Text.Json;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Benchmarks.Data;

/// <summary>
/// Seeded, code-generated traffic for the scenarios (spec §1 "Data"). Everything is built once in the static
/// constructor, so the same seed gives the same bytes on every machine.
/// </summary>
internal static class Payloads
{
    public const int Seed = 20261103;
    public const int ChunkBytes = 200;
    public const int RawPartBytes = 1024 * 1024;

    private static readonly Random Rng = new(Seed);

    private static readonly string[] Words =
    [
        "the", "agent", "can", "summarize", "your", "document", "and", "list", "the", "main", "points", "first",
        "then", "it", "checks", "each", "claim", "against", "the", "sources", "you", "gave", "me", "so", "that",
        "every", "answer", "is", "backed", "by", "a", "quote", "from", "the", "text", "if", "something", "is",
        "unclear", "I", "will", "ask", "a", "short", "question", "before", "I", "continue", "with", "the", "work",
    ];

    private static readonly DateTimeOffset Epoch = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    public static readonly SendMessageRequest SendMessageRequest;
    public static readonly AgentTask Task0;
    public static readonly AgentTask Task20;
    public static readonly AgentTask Task200;
    public static readonly StreamResponse StatusEvent;
    public static readonly StreamResponse ArtifactChunk;
    public static readonly AgentCard AgentCard;
    public static readonly SendMessageRequest LargeRawRequest;

    public static readonly byte[] SendMessageRequestBytes;
    public static readonly byte[] Task0Bytes;
    public static readonly byte[] Task20Bytes;
    public static readonly byte[] Task200Bytes;
    public static readonly byte[] StatusEventBytes;
    public static readonly byte[] ArtifactChunkBytes;
    public static readonly byte[] AgentCardBytes;
    public static readonly byte[] LargeRawRequestBytes;

    static Payloads()
    {
        SendMessageRequest = new SendMessageRequest
        {
            Message = new Message
            {
                MessageId = NewGuid(),
                ContextId = NewGuid(),
                Role = Role.User,
                Parts = [Part.FromText(Sentence(120))],
                Metadata = Meta(),
            },
        };
        Task0 = BuildTask(0);
        Task20 = BuildTask(20);
        Task200 = BuildTask(200);
        StatusEvent = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = NewGuid(),
                ContextId = NewGuid(),
                Status = new AgentTaskStatus { State = TaskState.Working, Timestamp = Epoch.AddSeconds(Rng.Next(3600)) },
                Metadata = Meta(),
            },
        };
        ArtifactChunk = ChunkEvent(NewGuid(), NewGuid(), "artifact-1", Sentence(ChunkBytes), append: true, last: false);
        AgentCard = BuildCard();
        LargeRawRequest = new SendMessageRequest
        {
            Message = new Message
            {
                MessageId = NewGuid(),
                ContextId = NewGuid(),
                Role = Role.User,
                Parts = [Part.FromRaw(RandomBytes(RawPartBytes), "application/octet-stream", "upload.bin")],
            },
        };

        SendMessageRequestBytes = JsonSerializer.SerializeToUtf8Bytes(SendMessageRequest, A2AJson.SendMessageRequest);
        Task0Bytes = JsonSerializer.SerializeToUtf8Bytes(Task0, A2AJson.AgentTask);
        Task20Bytes = JsonSerializer.SerializeToUtf8Bytes(Task20, A2AJson.AgentTask);
        Task200Bytes = JsonSerializer.SerializeToUtf8Bytes(Task200, A2AJson.AgentTask);
        StatusEventBytes = JsonSerializer.SerializeToUtf8Bytes(StatusEvent, A2AJson.StreamResponse);
        ArtifactChunkBytes = JsonSerializer.SerializeToUtf8Bytes(ArtifactChunk, A2AJson.StreamResponse);
        AgentCardBytes = JsonSerializer.SerializeToUtf8Bytes(AgentCard, A2AJson.AgentCard);
        LargeRawRequestBytes = JsonSerializer.SerializeToUtf8Bytes(LargeRawRequest, A2AJson.SendMessageRequest);
    }

    public static AgentTask TaskWithHistory(int history) => history switch
    {
        0 => Task0,
        20 => Task20,
        200 => Task200,
        _ => throw new ArgumentOutOfRangeException(nameof(history), history, "Use 0, 20 or 200."),
    };

    public static byte[] TaskBytes(int history) => history switch
    {
        0 => Task0Bytes,
        20 => Task20Bytes,
        200 => Task200Bytes,
        _ => throw new ArgumentOutOfRangeException(nameof(history), history, "Use 0, 20 or 200."),
    };

    /// <summary>n artifact-update events for one task: 200 B text chunks of one artifact, the last one flagged.</summary>
    public static StreamResponse[] ArtifactChunks(int n)
    {
        var rng = new Random(Seed + n);
        var taskId = Guid(rng);
        var contextId = Guid(rng);
        var events = new StreamResponse[n];
        for (var i = 0; i < n; i++)
        {
            events[i] = ChunkEvent(taskId, contextId, "artifact-1", Sentence(ChunkBytes, rng), append: i > 0, last: i == n - 1);
        }

        return events;
    }

    private static StreamResponse ChunkEvent(string taskId, string contextId, string artifactId, string text, bool append, bool last)
        => new()
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = taskId,
                ContextId = contextId,
                Artifact = new Artifact { ArtifactId = artifactId, Parts = [Part.FromText(text)] },
                Append = append,
                LastChunk = last,
            },
        };

    private static AgentTask BuildTask(int history)
    {
        var taskId = NewGuid();
        var contextId = NewGuid();
        var messages = new Message[history];
        for (var i = 0; i < history; i++)
        {
            var user = i % 2 == 0;
            var parts = new Part[1 + Rng.Next(3)];
            for (var p = 0; p < parts.Length; p++)
            {
                parts[p] = Part.FromText(Sentence(user ? 60 + Rng.Next(80) : 200 + Rng.Next(300)));
            }

            messages[i] = new Message
            {
                MessageId = NewGuid(),
                ContextId = contextId,
                TaskId = taskId,
                Role = user ? Role.User : Role.Agent,
                Parts = parts,
            };
        }

        return new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new AgentTaskStatus { State = TaskState.Completed, Timestamp = Epoch.AddSeconds(Rng.Next(3600)) },
            Artifacts = [new Artifact { ArtifactId = "artifact-1", Name = "answer", Parts = [Part.FromText(Sentence(ChunkBytes))] }],
            History = messages,
            Metadata = Meta(),
        };
    }

    private static AgentCard BuildCard()
    {
        var skills = new AgentSkill[6];
        for (var i = 0; i < skills.Length; i++)
        {
            skills[i] = new AgentSkill
            {
                Id = $"skill-{i}",
                Name = $"Skill {i}",
                Description = Sentence(100),
                Tags = ["research", "summary", $"tag{i}"],
                Examples = [Sentence(50), Sentence(60)],
                InputModes = ["text/plain"],
                OutputModes = ["text/plain", "application/json"],
            };
        }

        return new AgentCard
        {
            Name = "Benchmark Agent",
            Description = Sentence(160),
            Version = "1.0.0",
            Provider = new AgentProvider { Organization = "Example Corp", Url = "https://example.com" },
            DocumentationUrl = "https://example.com/docs",
            IconUrl = "https://example.com/icon.png",
            SupportedInterfaces =
            [
                new AgentInterface { Url = "https://agent.example.com/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "https://agent.example.com/rest", ProtocolBinding = "HTTP+JSON", ProtocolVersion = "1.0" },
                new AgentInterface { Url = "https://agent.example.com:8443", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" },
            ],
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true },
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["bearer"] = new() { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "Bearer", BearerFormat = "JWT" } },
            },
            SecurityRequirements =
            [
                new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["bearer"] = new() } },
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Skills = skills,
        };
    }

    private static JsonElement Meta()
        => JsonDocument.Parse($$"""{"source":"benchmark","trace":"{{NewGuid()}}","attempt":{{Rng.Next(1, 4)}},"priority":"normal"}""").RootElement.Clone();

    private static string NewGuid() => Guid(Rng);

    private static string Guid(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        Rng.NextBytes(bytes);
        return bytes;
    }

    private static string Sentence(int length) => Sentence(length, Rng);

    /// <summary>English-looking text of exactly <paramref name="length"/> characters (ASCII, so also bytes).</summary>
    private static string Sentence(int length, Random rng)
    {
        var sb = new System.Text.StringBuilder(length + 16);
        while (sb.Length < length)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(Words[rng.Next(Words.Length)]);
        }

        return sb.ToString(0, length);
    }
}
