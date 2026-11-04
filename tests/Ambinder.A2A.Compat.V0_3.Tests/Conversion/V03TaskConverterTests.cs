using System.Text.Json.Nodes;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03TaskConverterTests
{
    public static TheoryData<string, TaskState> StateFixtures() => new()
    {
        { "responses/tasks-get.task-state-submitted.json", TaskState.Submitted },
        { "responses/tasks-get.task-state-working.json", TaskState.Working },
        { "responses/tasks-get.task-state-input-required.json", TaskState.InputRequired },
        { "responses/tasks-get.task-state-completed.json", TaskState.Completed },
        { "responses/tasks-get.task-state-canceled.json", TaskState.Canceled },
        { "responses/tasks-get.task-state-failed.json", TaskState.Failed },
        { "responses/tasks-get.task-state-rejected.json", TaskState.Rejected },
        { "responses/tasks-get.task-state-auth-required.json", TaskState.AuthRequired },
        { "responses/tasks-get.task-state-unknown.json", TaskState.Unspecified },
    };

    [Theory]
    [MemberData(nameof(StateFixtures))]
    public void State_fixture_converts_and_converts_back_to_the_same_state_string(string rel, TaskState expected)
    {
        var task = Assert.IsType<V03Task>(Result(rel));
        AgentTask v1 = V03TaskConverter.ToV1(task);
        Assert.Equal(expected, v1.Status.State);
        Assert.Equal(task.Id, v1.Id);
        Assert.Equal(task.ContextId, v1.ContextId);
        Assert.Equal(task.Status.State, V03TaskConverter.ToV03(v1).Status.State);
    }

    public static TheoryData<string> TaskResults() => Data("responses", f => Result(f) is V03Task);

    public static TheoryData<string> PushTasks() => Data("push");

    [Theory]
    [MemberData(nameof(TaskResults))]
    public void Fixture_task_round_trips_0_3_to_1_0_to_0_3(string rel)
    {
        var original = Assert.IsType<V03Task>(Result(rel));
        V03Task back = V03TaskConverter.ToV03(V03TaskConverter.ToV1(original));
        Assert.True(JsonNode.DeepEquals(Normalized(original, Ctx.V03Task), Normalized(back, Ctx.V03Task)), rel + Normalized(original, Ctx.V03Task) + "\n" + Normalized(back, Ctx.V03Task));
    }

    [Theory]
    [MemberData(nameof(PushTasks))]
    public void Push_body_round_trips_0_3_to_1_0_to_0_3(string rel)
    {
        V03Task original = PushTask(rel);
        V03Task back = V03TaskConverter.ToV03(V03TaskConverter.ToV1(original));
        Assert.True(JsonNode.DeepEquals(Normalized(original, Ctx.V03Task), Normalized(back, Ctx.V03Task)), rel);
    }

    [Theory]
    [MemberData(nameof(TaskResults))]
    public void Fixture_task_round_trips_1_0_to_0_3_to_1_0(string rel)
    {
        AgentTask v1 = V03TaskConverter.ToV1(Assert.IsType<V03Task>(Result(rel)));
        AgentTask again = V03TaskConverter.ToV1(V03TaskConverter.ToV03(v1));
        Assert.Equal(V1(v1, A2AJson.AgentTask), V1(again, A2AJson.AgentTask));
    }

    [Fact]
    public void One_point_zero_task_with_every_field_round_trips_1_0_to_0_3_to_1_0()
    {
        var task = new AgentTask
        {
            Id = "t-1",
            ContextId = "c-1",
            Status = new AgentTaskStatus
            {
                State = TaskState.InputRequired,
                Timestamp = new DateTimeOffset(2025, 1, 15, 10, 0, 5, 123, TimeSpan.Zero).AddTicks(4567),
                Message = new Message { MessageId = "s-1", Role = Role.Agent, Parts = [Part.FromText("Which city?")], TaskId = "t-1", ContextId = "c-1" },
            },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a-1",
                    Name = "report",
                    Description = "the report",
                    Parts = [Part.FromRaw([1, 2, 3], "application/pdf", "r.pdf"), Part.FromUrl("https://x.example/r", "text/html", "r.html"), Part.FromData(ParseJson("""{"x":1}"""))],
                    Metadata = ParseJson("""{"owner":"me"}"""),
                    Extensions = ["https://example.com/ext/v1"],
                },
            ],
            History =
            [
                new Message
                {
                    MessageId = "m-1",
                    Role = Role.User,
                    Parts = [new Part { Text = "hi", Metadata = ParseJson("""{"p":1}""") }],
                    ReferenceTaskIds = ["r-1"],
                    Extensions = ["https://example.com/ext/v1"],
                    Metadata = ParseJson("""{"k":"v"}"""),
                },
            ],
            Metadata = ParseJson("""{"task":"meta"}"""),
        };

        AgentTask again = V03TaskConverter.ToV1(V03TaskConverter.ToV03(task));
        Assert.Equal(V1(task, A2AJson.AgentTask), V1(again, A2AJson.AgentTask));
    }

    [Fact]
    public void Task_without_context_id_writes_an_empty_context_id_and_reads_it_back_as_null()
    {
        V03Task v03 = V03TaskConverter.ToV03(new AgentTask { Id = "t", Status = new AgentTaskStatus { State = TaskState.Working } });
        Assert.Equal("", v03.ContextId);
        Assert.Equal("task", v03.Kind);
        Assert.Null(V03TaskConverter.ToV1(v03).ContextId);
        Assert.Null(v03.Artifacts);
        Assert.Null(v03.History);
    }

    [Fact]
    public void Status_timestamp_is_utc_with_z_and_a_missing_offset_counts_as_utc()
    {
        var local = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal("2025-01-15T10:00:00Z", V03TaskConverter.ToV03(new AgentTaskStatus { State = TaskState.Working, Timestamp = local }).Timestamp);

        AgentTaskStatus v1 = V03TaskConverter.ToV1(new V03TaskStatus { State = "working", Timestamp = "2025-04-02T16:59:25.331844" });
        Assert.Equal(TimeSpan.Zero, v1.Timestamp!.Value.Offset);
        Assert.Equal(new DateTime(2025, 4, 2, 16, 59, 25, DateTimeKind.Utc).AddTicks(3318440), v1.Timestamp.Value.UtcDateTime);
        Assert.Null(V03TaskConverter.ToV03(new AgentTaskStatus()).Timestamp);
        Assert.Null(V03TaskConverter.ToV1(new V03TaskStatus { State = "working" }).Timestamp);
    }

    [Fact]
    public void Invalid_timestamp_is_invalid_params()
    {
        var ex = Assert.Throws<A2AException>(() => V03TaskConverter.ToV1(new V03TaskStatus { State = "working", Timestamp = "yesterday" }));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Status_message_is_converted_both_ways()
    {
        var task = Assert.IsType<V03Task>(Result("responses/message-send.multi-turn.input-required.json"));
        AgentTaskStatus v1 = V03TaskConverter.ToV1(task.Status);
        Assert.Equal(TaskState.InputRequired, v1.State);
        Assert.NotNull(v1.Message);
        Assert.Equal(Role.Agent, v1.Message!.Role);
        Assert.Equal(task.Status.Message!.MessageId, V03TaskConverter.ToV03(v1).Message!.MessageId);
    }

    [Fact]
    public void Artifact_copies_every_field_both_ways()
    {
        var v03 = new V03Artifact
        {
            ArtifactId = "a",
            Name = "n",
            Description = "d",
            Parts = [new V03TextPart("x")],
            Extensions = ["e"],
            Metadata = Obj("""{"k":1}"""),
        };
        Artifact v1 = V03TaskConverter.ToV1(v03);
        Assert.Equal(("a", "n", "d"), (v1.ArtifactId, v1.Name, v1.Description));
        Assert.Equal(["e"], v1.Extensions);
        Assert.Equal(1, v1.Metadata!.Value.GetProperty("k").GetInt32());

        V03Artifact back = V03TaskConverter.ToV03(v1);
        Assert.Equal(("a", "n", "d"), (back.ArtifactId, back.Name, back.Description));
        Assert.Equal(["e"], back.Extensions);
        Assert.Equal(1, back.Metadata!["k"].GetInt32());
    }

    [Fact]
    public void Send_message_response_with_a_task_gives_a_0_3_task_with_kind_task()
    {
        var response = new SendMessageResponse { Task = new AgentTask { Id = "t", ContextId = "c", Status = new AgentTaskStatus { State = TaskState.Submitted } } };
        var task = Assert.IsType<V03Task>(V03TaskConverter.ToV03Result(response));
        Assert.Equal("task", task.Kind);
        Assert.Equal("t", task.Id);
        Assert.Contains("\"kind\":\"task\"", System.Text.Json.JsonSerializer.Serialize<IV03Result>(task, Ctx.IV03Result));
    }

    [Fact]
    public void Send_message_response_with_a_message_gives_a_0_3_message_with_kind_message()
    {
        var response = new SendMessageResponse { Message = new Message { MessageId = "m", Role = Role.Agent, Parts = [Part.FromText("hi")] } };
        var message = Assert.IsType<V03Message>(V03TaskConverter.ToV03Result(response));
        Assert.Equal("message", message.Kind);
        Assert.Contains("\"kind\":\"message\"", System.Text.Json.JsonSerializer.Serialize<IV03Result>(message, Ctx.IV03Result));
    }

    [Fact]
    public void Send_message_response_with_neither_or_both_is_an_invalid_agent_response()
    {
        foreach (var response in new[]
        {
            new SendMessageResponse(),
            new SendMessageResponse { Task = new AgentTask(), Message = new Message() },
        })
        {
            var ex = Assert.Throws<A2AException>(() => V03TaskConverter.ToV03Result(response));
            Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        }
    }

    [Fact]
    public void Result_from_0_3_gives_a_task_or_a_message_response()
    {
        Assert.NotNull(V03TaskConverter.ToV1Result(Result("responses/message-send.basic.task-result.json")).Task);
        SendMessageResponse message = V03TaskConverter.ToV1Result(Result("responses/message-send.basic.message-result.json"));
        Assert.NotNull(message.Message);
        Assert.Null(message.Task);
    }

    [Theory]
    [MemberData(nameof(AllResults))]
    public void Result_round_trips_0_3_to_1_0_to_0_3(string rel)
    {
        IV03Result original = Result(rel);
        IV03Result back = V03TaskConverter.ToV03Result(V03TaskConverter.ToV1Result(original));
        Assert.True(JsonNode.DeepEquals(Normalized(original, Ctx.IV03Result), Normalized(back, Ctx.IV03Result)), rel);
    }

    public static TheoryData<string> AllResults() => Data("responses");

    [Fact]
    public void Task_and_artifact_with_missing_members_convert_without_throwing()
    {
        AgentTask task = V03TaskConverter.ToV1(System.Text.Json.JsonSerializer.Deserialize("{}", Ctx.V03Task)!);
        Assert.Equal(TaskState.Unspecified, task.Status.State);
        Assert.Null(task.ContextId);
        Assert.Empty(V03TaskConverter.ToV1(System.Text.Json.JsonSerializer.Deserialize("{}", Ctx.V03Artifact)!).Parts);
    }
}
