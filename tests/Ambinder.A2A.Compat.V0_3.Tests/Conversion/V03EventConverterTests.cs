using System.Text.Json.Nodes;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03EventConverterTests
{
    public static TheoryData<string> Events() => Data("events");

    [Theory]
    [MemberData(nameof(Events))]
    public void Fixture_event_round_trips_0_3_to_1_0_to_0_3_except_final(string rel)
    {
        IV03StreamEvent original = Event(rel);
        IV03StreamEvent back = V03EventConverter.ToV03(V03EventConverter.ToV1(original));
        Assert.True(JsonNode.DeepEquals(Normalized(original, Ctx.IV03StreamEvent), Normalized(back, Ctx.IV03StreamEvent)), rel);
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Fixture_event_round_trips_1_0_to_0_3_to_1_0(string rel)
    {
        StreamResponse v1 = V03EventConverter.ToV1(Event(rel));
        StreamResponse again = V03EventConverter.ToV1(V03EventConverter.ToV03(v1));
        Assert.Equal(V1(v1, A2AJson.StreamResponse), V1(again, A2AJson.StreamResponse));
    }

    [Fact]
    public void Status_update_gets_kind_status_update_and_final_is_not_set_here()
    {
        var update = new TaskStatusUpdateEvent
        {
            TaskId = "t",
            ContextId = "c",
            Status = new AgentTaskStatus { State = TaskState.Completed },
            Metadata = ParseJson("""{"k":1}"""),
        };

        var v03 = Assert.IsType<V03TaskStatusUpdateEvent>(V03EventConverter.ToV03(new StreamResponse { StatusUpdate = update }));
        Assert.Equal("status-update", v03.Kind);
        Assert.Equal(("t", "c"), (v03.TaskId, v03.ContextId));
        Assert.Equal("completed", v03.Status.State);
        Assert.Equal(1, v03.Metadata!["k"].GetInt32());
        Assert.False(v03.Final); // Task 10 (stream writer) sets final.
    }

    [Fact]
    public void Final_is_dropped_going_to_1_0()
    {
        foreach (string rel in new[] { "events/status-update.final-true.json", "events/status-update.final-false.json" })
        {
            var v03 = Assert.IsType<V03TaskStatusUpdateEvent>(Event(rel));
            TaskStatusUpdateEvent v1 = V03EventConverter.ToV1(v03);
            Assert.Equal(v03.TaskId, v1.TaskId);
            Assert.Equal(v03.ContextId, v1.ContextId);
            Assert.False(V03EventConverter.ToV03(v1).Final);
        }
    }

    [Theory]
    [InlineData("events/artifact-update.append-false.last-chunk-true.json", false, true)]
    [InlineData("events/artifact-update.append-true.last-chunk-false.json", true, false)]
    [InlineData("events/artifact-update.append-true.last-chunk-true.json", true, true)]
    [InlineData("events/artifact-update.no-flags.json", false, false)]
    public void Artifact_update_carries_append_and_last_chunk(string rel, bool append, bool lastChunk)
    {
        var v03 = Assert.IsType<V03TaskArtifactUpdateEvent>(Event(rel));
        TaskArtifactUpdateEvent v1 = V03EventConverter.ToV1(v03);
        Assert.Equal(append, v1.Append);
        Assert.Equal(lastChunk, v1.LastChunk);
        Assert.Equal(v03.Artifact.ArtifactId, v1.Artifact.ArtifactId);

        var back = Assert.IsType<V03TaskArtifactUpdateEvent>(V03EventConverter.ToV03(new StreamResponse { ArtifactUpdate = v1 }));
        Assert.Equal("artifact-update", back.Kind);
        Assert.Equal(append, back.Append);
        Assert.Equal(lastChunk, back.LastChunk);
        Assert.Equal(v03.TaskId, back.TaskId);
        Assert.Equal(v03.ContextId, back.ContextId);
    }

    [Fact]
    public void Artifact_update_flags_are_written_even_when_false()
    {
        var back = Assert.IsType<V03TaskArtifactUpdateEvent>(V03EventConverter.ToV03(new StreamResponse { ArtifactUpdate = new TaskArtifactUpdateEvent { TaskId = "t", ContextId = "c" } }));
        Assert.False(back.Append);
        Assert.False(back.LastChunk);
    }

    [Fact]
    public void Task_and_message_events_keep_their_kind()
    {
        var task = Assert.IsType<V03Task>(V03EventConverter.ToV03(new StreamResponse { Task = new AgentTask { Id = "t" } }));
        Assert.Equal("task", task.Kind);
        var message = Assert.IsType<V03Message>(V03EventConverter.ToV03(new StreamResponse { Message = new Message { MessageId = "m", Role = Role.Agent } }));
        Assert.Equal("message", message.Kind);

        Assert.NotNull(V03EventConverter.ToV1(task).Task);
        Assert.NotNull(V03EventConverter.ToV1(message).Message);
    }

    [Fact]
    public void Stream_response_with_none_or_several_members_is_an_invalid_agent_response()
    {
        foreach (var response in new[]
        {
            new StreamResponse(),
            new StreamResponse { Task = new AgentTask(), Message = new Message() },
        })
        {
            var ex = Assert.Throws<A2AException>(() => V03EventConverter.ToV03(response));
            Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        }
    }
}
