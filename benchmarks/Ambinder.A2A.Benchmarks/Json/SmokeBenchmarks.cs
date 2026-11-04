using System.Text.Json;
using Ambinder.A2A.Protocol;
using BenchmarkDotNet.Attributes;

namespace Ambinder.A2A.Benchmarks.Json;

// Placeholder so the project runs before the real scenarios exist. Task 3 deletes it.
[BenchmarkCategory(Categories.Json)]
public class SmokeBenchmarks
{
    private readonly AgentCard _card = new() { Name = "smoke", Description = "smoke", Version = "1.0.0" };

    [Benchmark]
    public string SerializeAgentCard() => JsonSerializer.Serialize(_card, A2AJson.Options);
}
