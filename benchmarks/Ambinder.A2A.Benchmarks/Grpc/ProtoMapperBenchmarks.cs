// Copy used: the server one (Ambinder.A2A.AspNetCore.Grpc, InternalsVisibleTo Ambinder.A2A.Benchmarks). Both gRPC packages compile the
// same src/Grpc.Shared sources, so one copy is enough. The client copy stays behind the "clientgrpc" alias and is not used here.
using System.Text.Json;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Protocol.ProtoMapping;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Benchmarks.Grpc;

// The generated proto types are internal, so the benchmark methods return object (the reference is returned as is, no boxing).

/// <summary>Task mapping at the three history sizes.</summary>
[BenchmarkCategory(Categories.Grpc)]
public class ProtoMapperTaskBenchmarks
{
    [Params(0, 20, 200)]
    public int History { get; set; }

    private AgentTask _task = null!;
    private Proto.Task _proto = null!;
    private byte[] _wire = null!;

    [GlobalSetup]
    public void Setup()
    {
        _task = Payloads.TaskWithHistory(History);
        _proto = ProtoMapper.ToProto(_task);
        _wire = _proto.ToByteArray();
        var back = ProtoMapper.FromProto(Proto.Task.Parser.ParseFrom(_wire));
        RoundTrip.Check(_task, back, A2AJson.AgentTask, $"AgentTask history {History}");
        Console.WriteLine($"[size] Task history={History}: json={Payloads.TaskBytes(History).Length} B, proto={_wire.Length} B");
    }

    [Benchmark]
    public object ModelToProto() => ProtoMapper.ToProto(_task);

    [Benchmark]
    public AgentTask ProtoToModel() => ProtoMapper.FromProto(_proto);

    [Benchmark]
    public byte[] ProtoToByteArray() => _proto.ToByteArray();

    [Benchmark]
    public object ProtoParseFrom() => Proto.Task.Parser.ParseFrom(_wire);
}

/// <summary>The other payloads: request, stream events and the agent card.</summary>
[BenchmarkCategory(Categories.Grpc)]
public class ProtoMapperPayloadBenchmarks
{
    private Proto.SendMessageRequest _requestProto = null!;
    private byte[] _requestWire = null!;
    private Proto.StreamResponse _statusProto = null!;
    private byte[] _statusWire = null!;
    private Proto.StreamResponse _chunkProto = null!;
    private byte[] _chunkWire = null!;
    private Proto.AgentCard _cardProto = null!;
    private byte[] _cardWire = null!;

    [GlobalSetup]
    public void Setup()
    {
        _requestProto = ProtoMapper.ToProto(Payloads.SendMessageRequest);
        _requestWire = _requestProto.ToByteArray();
        RoundTrip.Check(Payloads.SendMessageRequest, ProtoMapper.FromProto(Proto.SendMessageRequest.Parser.ParseFrom(_requestWire)),
            A2AJson.SendMessageRequest, "SendMessageRequest");

        _statusProto = ProtoMapper.ToProto(Payloads.StatusEvent);
        _statusWire = _statusProto.ToByteArray();
        RoundTrip.Check(Payloads.StatusEvent, ProtoMapper.FromProto(Proto.StreamResponse.Parser.ParseFrom(_statusWire)),
            A2AJson.StreamResponse, "Status event");

        _chunkProto = ProtoMapper.ToProto(Payloads.ArtifactChunk);
        _chunkWire = _chunkProto.ToByteArray();
        RoundTrip.Check(Payloads.ArtifactChunk, ProtoMapper.FromProto(Proto.StreamResponse.Parser.ParseFrom(_chunkWire)),
            A2AJson.StreamResponse, "Artifact chunk");

        _cardProto = ProtoMapper.ToProto(Payloads.AgentCard);
        _cardWire = _cardProto.ToByteArray();
        RoundTrip.Check(Payloads.AgentCard, ProtoMapper.FromProto(Proto.AgentCard.Parser.ParseFrom(_cardWire)),
            A2AJson.AgentCard, "AgentCard");

        Console.WriteLine($"[size] SendMessageRequest: json={Payloads.SendMessageRequestBytes.Length} B, proto={_requestWire.Length} B");
        Console.WriteLine($"[size] StatusEvent: json={Payloads.StatusEventBytes.Length} B, proto={_statusWire.Length} B");
        Console.WriteLine($"[size] ArtifactChunk: json={Payloads.ArtifactChunkBytes.Length} B, proto={_chunkWire.Length} B");
        Console.WriteLine($"[size] AgentCard: json={Payloads.AgentCardBytes.Length} B, proto={_cardWire.Length} B");
    }

    [Benchmark]
    public object RequestToProto() => ProtoMapper.ToProto(Payloads.SendMessageRequest);

    [Benchmark]
    public SendMessageRequest RequestFromProto() => ProtoMapper.FromProto(_requestProto);

    [Benchmark]
    public byte[] RequestToByteArray() => _requestProto.ToByteArray();

    [Benchmark]
    public object RequestParseFrom() => Proto.SendMessageRequest.Parser.ParseFrom(_requestWire);

    [Benchmark]
    public object StatusEventToProto() => ProtoMapper.ToProto(Payloads.StatusEvent);

    [Benchmark]
    public StreamResponse StatusEventFromProto() => ProtoMapper.FromProto(_statusProto);

    [Benchmark]
    public byte[] StatusEventToByteArray() => _statusProto.ToByteArray();

    [Benchmark]
    public object StatusEventParseFrom() => Proto.StreamResponse.Parser.ParseFrom(_statusWire);

    [Benchmark]
    public object ArtifactChunkToProto() => ProtoMapper.ToProto(Payloads.ArtifactChunk);

    [Benchmark]
    public StreamResponse ArtifactChunkFromProto() => ProtoMapper.FromProto(_chunkProto);

    [Benchmark]
    public byte[] ArtifactChunkToByteArray() => _chunkProto.ToByteArray();

    [Benchmark]
    public object ArtifactChunkParseFrom() => Proto.StreamResponse.Parser.ParseFrom(_chunkWire);

    [Benchmark]
    public object AgentCardToProto() => ProtoMapper.ToProto(Payloads.AgentCard);

    [Benchmark]
    public AgentCard AgentCardFromProto() => ProtoMapper.FromProto(_cardProto);

    [Benchmark]
    public byte[] AgentCardToByteArray() => _cardProto.ToByteArray();

    [Benchmark]
    public object AgentCardParseFrom() => Proto.AgentCard.Parser.ParseFrom(_cardWire);
}

internal static class RoundTrip
{
    /// <summary>The model after model→proto→bytes→proto→model must serialize to the same JSON as the original.</summary>
    public static void Check<T>(T original, T back, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, string what)
    {
        var a = JsonSerializer.SerializeToUtf8Bytes(original, info);
        var b = JsonSerializer.SerializeToUtf8Bytes(back, info);
        if (!a.AsSpan().SequenceEqual(b))
        {
            throw new InvalidOperationException($"{what}: proto round trip changed the model (json {a.Length} B before, {b.Length} B after).");
        }
    }
}
