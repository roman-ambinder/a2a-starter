using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>A consumer's own source-generated context that lists our models.</summary>
[JsonSerializable(typeof(AgentTask))]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(AgentTaskStatus))]
internal sealed partial class ConsumerContext : JsonSerializerContext;

public sealed class ConsumerJsonContextTests
{
    [Fact]
    public void Consumer_context_combined_with_A2AJson_round_trips_enums_by_name()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(A2AJson.TypeInfoResolver, ConsumerContext.Default),
        };
        var task = new AgentTask { Id = "t1", ContextId = "c1", Status = new AgentTaskStatus { State = TaskState.Working } };
        var json = JsonSerializer.Serialize(task, options.GetTypeInfo(typeof(AgentTask)));
        Assert.Contains("TASK_STATE_WORKING", json);
        var back = (AgentTask)JsonSerializer.Deserialize(json, options.GetTypeInfo(typeof(AgentTask)))!;
        Assert.Equal(TaskState.Working, back.Status.State);
    }

    [Fact]
    public void Consumer_context_alone_compiles_and_writes_enums_by_name()
    {
        var json = JsonSerializer.Serialize(new AgentTaskStatus { State = TaskState.Working }, ConsumerContext.Default.Options.GetTypeInfo(typeof(AgentTaskStatus)));
        Assert.Contains("TASK_STATE_WORKING", json);
    }

    [Fact]
    public void Consumer_context_alone_reads_enums_by_name()
    {
        var status = (AgentTaskStatus)JsonSerializer.Deserialize("""{"State":"TASK_STATE_COMPLETED"}""", ConsumerContext.Default.Options.GetTypeInfo(typeof(AgentTaskStatus)))!;
        Assert.Equal(TaskState.Completed, status.State);
    }
}
