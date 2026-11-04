using System.Text.Json.Nodes;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03RequestConverterTests
{
    public static TheoryData<string> MessageRequests() => Data("requests", IsMessageRequest);

    private static V03MessageSendParams Params(bool? blocking, bool withConfiguration = true) => new()
    {
        Message = new V03Message { Role = "user", MessageId = "m", Parts = [new V03TextPart("x")] },
        Configuration = withConfiguration ? new V03MessageSendConfiguration { Blocking = blocking } : null,
    };

    [Fact]
    public void Blocking_true_gives_return_immediately_false()
        => Assert.False(V03RequestConverter.ToV1(Params(true)).Configuration!.ReturnImmediately);

    [Fact]
    public void Blocking_false_gives_return_immediately_true()
        => Assert.True(V03RequestConverter.ToV1(Params(false)).Configuration!.ReturnImmediately);

    // The vendored v0.3.0 files state no default for an absent 'blocking' (inventory.json, blockingDefaultStatedInFiles = false;
    // proto3 bool default is false). Implementer default awaiting owner review: absent == false, so ReturnImmediately = true.
    [Fact]
    public void Blocking_absent_equals_false_so_return_immediately_is_true()
        => Assert.True(V03RequestConverter.ToV1(Params(null)).Configuration!.ReturnImmediately);

    [Fact]
    public void Configuration_absent_equals_blocking_false_so_return_immediately_is_true()
        => Assert.True(V03RequestConverter.ToV1(Params(null, withConfiguration: false)).Configuration!.ReturnImmediately);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Return_immediately_gives_blocking_as_its_negation(bool returnImmediately, bool blocking)
    {
        var request = new SendMessageRequest { Configuration = new SendMessageConfiguration { ReturnImmediately = returnImmediately } };
        Assert.Equal(blocking, V03RequestConverter.ToV03(request).Configuration!.Blocking);
    }

    [Fact]
    public void Request_without_configuration_is_blocking_true_in_0_3()
        => Assert.True(V03RequestConverter.ToV03(new SendMessageRequest()).Configuration!.Blocking);

    [Fact]
    public void History_length_accepted_output_modes_and_push_config_map_directly()
    {
        var v03 = new V03MessageSendParams
        {
            Message = new V03Message { Role = "user", MessageId = "m", Parts = [new V03TextPart("x")] },
            Metadata = Obj("""{"trace":"abc"}"""),
            Configuration = new V03MessageSendConfiguration
            {
                Blocking = true,
                HistoryLength = 5,
                AcceptedOutputModes = ["text/plain", "application/json"],
                PushNotificationConfig = new V03PushNotificationConfig
                {
                    Id = "p-1",
                    Url = "https://client.example.com/hook",
                    Token = "tok",
                    Authentication = new V03PushNotificationAuthenticationInfo { Schemes = ["Bearer"], Credentials = "secret" },
                },
            },
        };

        SendMessageRequest v1 = V03RequestConverter.ToV1(v03);
        SendMessageConfiguration config = v1.Configuration!;
        Assert.Equal(5, config.HistoryLength);
        Assert.Equal(["text/plain", "application/json"], config.AcceptedOutputModes);
        TaskPushNotificationConfig push = config.TaskPushNotificationConfig!;
        Assert.Equal(("p-1", "https://client.example.com/hook", "tok"), (push.Id, push.Url, push.Token));
        Assert.Null(push.TaskId);
        Assert.Equal(("Bearer", "secret"), (push.Authentication!.Scheme, push.Authentication.Credentials));
        Assert.Equal("abc", v1.Metadata!.Value.GetProperty("trace").GetString());
        Assert.Equal("m", v1.Message.MessageId);
        Assert.Null(v1.Tenant);

        V03MessageSendParams back = V03RequestConverter.ToV03(v1);
        Assert.Equal(5, back.Configuration!.HistoryLength);
        Assert.Equal(["text/plain", "application/json"], back.Configuration.AcceptedOutputModes);
        Assert.True(back.Configuration.Blocking);
        Assert.Equal(["Bearer"], back.Configuration.PushNotificationConfig!.Authentication!.Schemes);
        Assert.Equal("secret", back.Configuration.PushNotificationConfig.Authentication.Credentials);
        Assert.Equal("tok", back.Configuration.PushNotificationConfig.Token);
        Assert.Equal("abc", back.Metadata!["trace"].GetString());
    }

    [Fact]
    public void History_length_zero_is_kept_and_differs_from_absent()
    {
        var v03 = Params(true);
        v03 = v03 with { Configuration = v03.Configuration! with { HistoryLength = 0 } };
        Assert.Equal(0, V03RequestConverter.ToV1(v03).Configuration!.HistoryLength);
        Assert.Null(V03RequestConverter.ToV1(Params(true)).Configuration!.HistoryLength);
    }

    [Fact]
    public void Several_push_schemes_keep_the_first_and_none_gives_an_empty_scheme()
    {
        var several = new V03PushNotificationConfig { Url = "https://x.example/h", Authentication = new V03PushNotificationAuthenticationInfo { Schemes = ["Bearer", "Basic"] } };
        Assert.Equal("Bearer", V03RequestConverter.ToV1(several, "t").Authentication!.Scheme);
        var none = several with { Authentication = new V03PushNotificationAuthenticationInfo() };
        TaskPushNotificationConfig v1 = V03RequestConverter.ToV1(none, "t");
        Assert.Equal("", v1.Authentication!.Scheme);
        Assert.Empty(V03RequestConverter.ToV03Config(v1).Authentication!.Schemes);
        Assert.Null(V03RequestConverter.ToV1(several with { Authentication = null }, "t").Authentication);
    }

    [Theory]
    [MemberData(nameof(MessageRequests))]
    public void Fixture_params_round_trip_0_3_to_1_0_to_0_3_with_blocking_made_explicit(string rel)
    {
        V03MessageSendParams original = SendParams(rel);
        V03MessageSendParams back = V03RequestConverter.ToV03(V03RequestConverter.ToV1(original));

        // The only intended differences: 'blocking' absent is written as an explicit false, and an absent configuration appears.
        V03MessageSendParams expected = original with
        {
            Configuration = (original.Configuration ?? new V03MessageSendConfiguration()) with { Blocking = original.Configuration?.Blocking ?? false },
        };
        Assert.True(JsonNode.DeepEquals(Normalized(expected, Ctx.V03MessageSendParams), Normalized(back, Ctx.V03MessageSendParams)), rel);
    }

    [Theory]
    [MemberData(nameof(MessageRequests))]
    public void Fixture_params_round_trip_1_0_to_0_3_to_1_0(string rel)
    {
        SendMessageRequest v1 = V03RequestConverter.ToV1(SendParams(rel));
        SendMessageRequest again = V03RequestConverter.ToV1(V03RequestConverter.ToV03(v1));
        Assert.Equal(V1(v1, A2AJson.SendMessageRequest), V1(again, A2AJson.SendMessageRequest));
    }

    [Fact]
    public void Push_config_fixture_converts_to_the_1_0_configuration()
    {
        SendMessageRequest v1 = V03RequestConverter.ToV1(SendParams("requests/message-send.push-config.json"));
        TaskPushNotificationConfig push = v1.Configuration!.TaskPushNotificationConfig!;
        Assert.Equal("https://client.example.com/webhook/a2a-notifications", push.Url);
        Assert.Equal("secure-client-token-for-task-aaa", push.Token);
        Assert.Equal("Bearer", push.Authentication!.Scheme);
        Assert.Null(push.Authentication.Credentials);
    }

    [Fact]
    public void File_upload_fixture_keeps_the_file_name_and_media_type()
    {
        SendMessageRequest v1 = V03RequestConverter.ToV1(SendParams("requests/message-send.file-upload.json"));
        Part file = v1.Message.Parts.Single(p => p.Raw is not null || p.Url is not null);
        Assert.NotNull(file.Filename);
        Assert.NotNull(file.MediaType);
    }

    [Fact]
    public void Task_query_params_map_both_ways_and_drop_metadata()
    {
        GetTaskRequest v1 = V03RequestConverter.ToV1(new V03TaskQueryParams { Id = "t", HistoryLength = 3, Metadata = Obj("""{"a":1}""") });
        Assert.Equal(("t", 3), (v1.Id, v1.HistoryLength));
        V03TaskQueryParams back = V03RequestConverter.ToV03(v1);
        Assert.Equal(("t", 3), (back.Id, back.HistoryLength));
        Assert.Null(V03RequestConverter.ToV1(new V03TaskQueryParams { Id = "t" }).HistoryLength);
    }

    [Fact]
    public void Cancel_params_map_both_ways_and_carry_metadata()
    {
        CancelTaskRequest v1 = V03RequestConverter.ToV1Cancel(new V03TaskIdParams { Id = "t", Metadata = Obj("""{"a":1}""") });
        Assert.Equal("t", v1.Id);
        Assert.Equal(1, v1.Metadata!.Value.GetProperty("a").GetInt32());
        V03TaskIdParams back = V03RequestConverter.ToV03(v1);
        Assert.Equal("t", back.Id);
        Assert.Equal(1, back.Metadata!["a"].GetInt32());
    }

    [Fact]
    public void Resubscribe_params_map_both_ways()
    {
        SubscribeToTaskRequest v1 = V03RequestConverter.ToV1Subscribe(new V03TaskIdParams { Id = "t" });
        Assert.Equal("t", v1.Id);
        Assert.Equal("t", V03RequestConverter.ToV03(v1).Id);
    }

    [Fact]
    public void Get_push_config_params_map_both_ways_and_a_bare_task_id_gives_an_empty_config_id()
    {
        GetTaskPushNotificationConfigRequest v1 = V03RequestConverter.ToV1(new V03GetTaskPushNotificationConfigParams { Id = "t", PushNotificationConfigId = "p" });
        Assert.Equal(("t", "p"), (v1.TaskId, v1.Id));
        Assert.Equal("p", V03RequestConverter.ToV03(v1).PushNotificationConfigId);

        GetTaskPushNotificationConfigRequest bare = V03RequestConverter.ToV1(new V03GetTaskPushNotificationConfigParams { Id = "t" });
        Assert.Equal("", bare.Id);
        Assert.Null(V03RequestConverter.ToV03(bare).PushNotificationConfigId);
    }

    [Fact]
    public void Delete_and_list_push_config_params_map_both_ways()
    {
        DeleteTaskPushNotificationConfigRequest delete = V03RequestConverter.ToV1(new V03DeleteTaskPushNotificationConfigParams { Id = "t", PushNotificationConfigId = "p" });
        Assert.Equal(("t", "p"), (delete.TaskId, delete.Id));
        V03DeleteTaskPushNotificationConfigParams deleteBack = V03RequestConverter.ToV03(delete);
        Assert.Equal(("t", "p"), (deleteBack.Id, deleteBack.PushNotificationConfigId));

        ListTaskPushNotificationConfigsRequest list = V03RequestConverter.ToV1(new V03ListTaskPushNotificationConfigParams { Id = "t" });
        Assert.Equal("t", list.TaskId);
        Assert.Null(list.PageSize);
        Assert.Null(list.PageToken);
        Assert.Equal("t", V03RequestConverter.ToV03(list with { PageSize = 10, PageToken = "x" }).Id);
    }

    [Fact]
    public void Task_push_config_maps_both_ways_with_task_id_and_config_id()
    {
        var v03 = new V03TaskPushNotificationConfig
        {
            TaskId = "t",
            PushNotificationConfig = new V03PushNotificationConfig { Id = "p", Url = "https://x.example/h", Token = "tok" },
        };
        TaskPushNotificationConfig v1 = V03RequestConverter.ToV1(v03);
        Assert.Equal(("t", "p", "https://x.example/h", "tok"), (v1.TaskId, v1.Id, v1.Url, v1.Token));
        V03TaskPushNotificationConfig back = V03RequestConverter.ToV03(v1 with { Tenant = "dropped" });
        Assert.Equal("t", back.TaskId);
        Assert.Null(back.Name);
        Assert.Equal(("p", "https://x.example/h", "tok"), (back.PushNotificationConfig.Id, back.PushNotificationConfig.Url, back.PushNotificationConfig.Token));
    }

    [Theory]
    [InlineData("tasks/t-1/pushNotificationConfigs/p-1", "t-1", "p-1")]
    [InlineData("tasks/t-1", "t-1", null)]
    [InlineData("other/t-1", null, null)]
    public void Proto_shaped_name_gives_task_id_and_config_id_when_the_json_members_are_absent(string name, string? taskId, string? configId)
    {
        TaskPushNotificationConfig v1 = V03RequestConverter.ToV1(new V03TaskPushNotificationConfig
        {
            Name = name,
            PushNotificationConfig = new V03PushNotificationConfig { Url = "https://x.example/h" },
        });
        Assert.Equal(taskId, v1.TaskId);
        Assert.Equal(configId, v1.Id);
    }

    [Fact]
    public void Task_id_wins_over_the_proto_shaped_name()
    {
        TaskPushNotificationConfig v1 = V03RequestConverter.ToV1(new V03TaskPushNotificationConfig
        {
            TaskId = "json",
            Name = "tasks/proto/pushNotificationConfigs/p",
            PushNotificationConfig = new V03PushNotificationConfig { Id = "own", Url = "https://x.example/h" },
        });
        Assert.Equal("json", v1.TaskId);
        Assert.Equal("own", v1.Id);
    }

    [Fact]
    public void List_result_maps_to_an_array_and_drops_the_next_page_token()
    {
        var page = new ListTaskPushNotificationConfigsResponse
        {
            Configs = [new TaskPushNotificationConfig { TaskId = "t", Id = "p", Url = "https://x.example/h" }],
            NextPageToken = "more",
        };
        V03TaskPushNotificationConfig item = Assert.Single(V03RequestConverter.ToV03(page));
        Assert.Equal(("t", "p"), (item.TaskId, item.PushNotificationConfig.Id));

        ListTaskPushNotificationConfigsResponse v1 = V03RequestConverter.ToV1([item]);
        Assert.Equal("p", Assert.Single(v1.Configs).Id);
        Assert.Null(v1.NextPageToken);
    }

    [Fact]
    public void Params_without_a_message_are_invalid_params()
    {
        var parameters = System.Text.Json.JsonSerializer.Deserialize("{}", Ctx.V03MessageSendParams)!;
        var ex = Assert.Throws<A2AException>(() => V03RequestConverter.ToV1(parameters));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }
}
