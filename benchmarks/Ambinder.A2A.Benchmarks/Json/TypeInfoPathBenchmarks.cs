using System.Text.Json;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Protocol;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Ambinder.A2A.Benchmarks.Json;

/// <summary>
/// Proves PD4: the Phase 1 hot-caller pattern (resolve the type info from the options on every call) against the
/// typed accessor. Both must give the same bytes. Do not compare with <c>A2AJsonContext.Default.*</c>: that metadata
/// has no OmitEmptyLists modifier, so it writes empty lists and the bytes differ.
/// </summary>
[BenchmarkCategory(Categories.Json)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class TypeInfoPathBenchmarks
{
    private AgentTask _task = null!;
    private StreamResponse _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _task = Payloads.Task20;
        _event = Payloads.ArtifactChunk;
        Guard(JsonSerializer.SerializeToUtf8Bytes(_task, A2AJson.Options.GetTypeInfo(typeof(AgentTask))),
            JsonSerializer.SerializeToUtf8Bytes(_task, A2AJson.AgentTask), nameof(AgentTask));
        Guard(JsonSerializer.SerializeToUtf8Bytes(_event, A2AJson.Options.GetTypeInfo(typeof(StreamResponse))),
            JsonSerializer.SerializeToUtf8Bytes(_event, A2AJson.StreamResponse), nameof(StreamResponse));
    }

    private static void Guard(byte[] viaOptions, byte[] viaAccessor, string type)
    {
        if (!viaOptions.AsSpan().SequenceEqual(viaAccessor))
        {
            throw new InvalidOperationException($"{type}: GetTypeInfo(typeof(T)) and the typed accessor give different bytes.");
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("AgentTask")]
    public byte[] AgentTask_GetTypeInfo() => JsonSerializer.SerializeToUtf8Bytes(_task, A2AJson.Options.GetTypeInfo(typeof(AgentTask)));

    [Benchmark]
    [BenchmarkCategory("AgentTask")]
    public byte[] AgentTask_Accessor() => JsonSerializer.SerializeToUtf8Bytes(_task, A2AJson.AgentTask);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("StreamResponse")]
    public byte[] StreamResponse_GetTypeInfo() => JsonSerializer.SerializeToUtf8Bytes(_event, A2AJson.Options.GetTypeInfo(typeof(StreamResponse)));

    [Benchmark]
    [BenchmarkCategory("StreamResponse")]
    public byte[] StreamResponse_Accessor() => JsonSerializer.SerializeToUtf8Bytes(_event, A2AJson.StreamResponse);
}
