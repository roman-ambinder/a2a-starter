using System.Text.Json;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Protocol;
using BenchmarkDotNet.Attributes;

namespace Ambinder.A2A.Benchmarks.Json;

/// <summary>Serialize and deserialize through the typed accessors, which is what production does.</summary>
[BenchmarkCategory(Categories.Json)]
public class JsonBenchmarks
{
    [Params(0, 20, 200)]
    public int History { get; set; }

    private AgentTask _task = null!;
    private byte[] _taskBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _task = Payloads.TaskWithHistory(History);
        _taskBytes = Payloads.TaskBytes(History);
        var back = JsonSerializer.Deserialize(_taskBytes, A2AJson.AgentTask);
        if (back is null || back.History.Count != History || back.Id != _task.Id)
        {
            throw new InvalidOperationException($"AgentTask round trip failed for history {History}.");
        }
    }

    [Benchmark]
    public byte[] SerializeAgentTask() => JsonSerializer.SerializeToUtf8Bytes(_task, A2AJson.AgentTask);

    [Benchmark]
    public AgentTask? DeserializeAgentTask() => JsonSerializer.Deserialize(_taskBytes, A2AJson.AgentTask);
}

/// <summary>The payloads that do not vary with the history size.</summary>
[BenchmarkCategory(Categories.Json)]
public class JsonPayloadBenchmarks
{
    [GlobalSetup]
    public void Setup()
    {
        var request = JsonSerializer.Deserialize(Payloads.SendMessageRequestBytes, A2AJson.SendMessageRequest);
        if (request?.Message.Parts.Count != 1 || request.Message.Parts[0].Text != Payloads.SendMessageRequest.Message.Parts[0].Text)
        {
            throw new InvalidOperationException("SendMessageRequest round trip failed.");
        }

        var status = JsonSerializer.Deserialize(Payloads.StatusEventBytes, A2AJson.StreamResponse);
        if (status?.StatusUpdate?.Status.State != TaskState.Working)
        {
            throw new InvalidOperationException("Status event round trip failed.");
        }

        var chunk = JsonSerializer.Deserialize(Payloads.ArtifactChunkBytes, A2AJson.StreamResponse);
        if (chunk?.ArtifactUpdate?.Artifact.Parts[0].Text?.Length != Payloads.ChunkBytes)
        {
            throw new InvalidOperationException("Artifact chunk round trip failed.");
        }

        var card = JsonSerializer.Deserialize(Payloads.AgentCardBytes, A2AJson.AgentCard);
        if (card is null || card.Skills.Count != 6 || card.SupportedInterfaces.Count != 3 || card.SecuritySchemes?.Count != 1)
        {
            throw new InvalidOperationException("AgentCard round trip failed.");
        }

        var large = JsonSerializer.Deserialize(Payloads.LargeRawRequestBytes, A2AJson.SendMessageRequest);
        var raw = large?.Message.Parts[0].Raw;
        if (raw is null || !raw.AsSpan().SequenceEqual(Payloads.LargeRawRequest.Message.Parts[0].Raw))
        {
            throw new InvalidOperationException("1 MB Raw part round trip failed.");
        }
    }

    [Benchmark]
    public byte[] SerializeSendMessageRequest() => JsonSerializer.SerializeToUtf8Bytes(Payloads.SendMessageRequest, A2AJson.SendMessageRequest);

    [Benchmark]
    public SendMessageRequest? DeserializeSendMessageRequest() => JsonSerializer.Deserialize(Payloads.SendMessageRequestBytes, A2AJson.SendMessageRequest);

    [Benchmark]
    public byte[] SerializeStatusEvent() => JsonSerializer.SerializeToUtf8Bytes(Payloads.StatusEvent, A2AJson.StreamResponse);

    [Benchmark]
    public StreamResponse? DeserializeStatusEvent() => JsonSerializer.Deserialize(Payloads.StatusEventBytes, A2AJson.StreamResponse);

    [Benchmark]
    public byte[] SerializeArtifactChunk() => JsonSerializer.SerializeToUtf8Bytes(Payloads.ArtifactChunk, A2AJson.StreamResponse);

    [Benchmark]
    public StreamResponse? DeserializeArtifactChunk() => JsonSerializer.Deserialize(Payloads.ArtifactChunkBytes, A2AJson.StreamResponse);

    [Benchmark]
    public byte[] SerializeAgentCard() => JsonSerializer.SerializeToUtf8Bytes(Payloads.AgentCard, A2AJson.AgentCard);

    [Benchmark]
    public AgentCard? DeserializeAgentCard() => JsonSerializer.Deserialize(Payloads.AgentCardBytes, A2AJson.AgentCard);

    [Benchmark]
    public byte[] SerializeLargeRawPart() => JsonSerializer.SerializeToUtf8Bytes(Payloads.LargeRawRequest, A2AJson.SendMessageRequest);

    [Benchmark]
    public SendMessageRequest? DeserializeLargeRawPart() => JsonSerializer.Deserialize(Payloads.LargeRawRequestBytes, A2AJson.SendMessageRequest);
}
