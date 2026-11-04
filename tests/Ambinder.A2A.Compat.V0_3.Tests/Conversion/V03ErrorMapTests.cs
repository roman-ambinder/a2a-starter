using System.Text.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03ErrorMapTests
{
    // The 12 codes of inventory.json (errorCodes.errors): number, 1.0 member.
    public static TheoryData<int, A2AErrorCode> SharedCodes() => new()
    {
        { -32700, A2AErrorCode.ParseError },
        { -32600, A2AErrorCode.InvalidRequest },
        { -32601, A2AErrorCode.MethodNotFound },
        { -32602, A2AErrorCode.InvalidParams },
        { -32603, A2AErrorCode.InternalError },
        { -32001, A2AErrorCode.TaskNotFound },
        { -32002, A2AErrorCode.TaskNotCancelable },
        { -32003, A2AErrorCode.PushNotificationNotSupported },
        { -32004, A2AErrorCode.UnsupportedOperation },
        { -32005, A2AErrorCode.ContentTypeNotSupported },
        { -32006, A2AErrorCode.InvalidAgentResponse },
        { -32007, A2AErrorCode.ExtendedAgentCardNotConfigured },
    };

    [Theory]
    [MemberData(nameof(SharedCodes))]
    public void Shared_codes_are_identity_both_ways(int code, A2AErrorCode v1)
    {
        Assert.Equal(code, V03ErrorMap.ToV03Code(v1));
        Assert.Equal(v1, V03ErrorMap.FromV03Code(code));
        Assert.Equal(code, (int)v1);
    }

    [Fact]
    public void Authenticated_extended_card_not_configured_is_an_exact_counterpart_not_invalid_request()
        => Assert.Equal(-32007, V03ErrorMap.ToV03Code(A2AErrorCode.ExtendedAgentCardNotConfigured));

    // Plan writer's call (plan 15, Task 6 Step 2), awaiting owner review.
    [Theory]
    [InlineData(A2AErrorCode.ExtensionSupportRequired)]
    [InlineData(A2AErrorCode.VersionNotSupported)]
    public void One_point_zero_only_codes_map_to_invalid_request(A2AErrorCode code)
        => Assert.Equal(-32600, V03ErrorMap.ToV03Code(code));

    [Fact]
    public void Every_1_0_code_maps_to_a_code_that_0_3_defines()
    {
        foreach (var code in Enum.GetValues<A2AErrorCode>())
        {
            Assert.NotNull(V03ErrorMap.FromV03Code(V03ErrorMap.ToV03Code(code)));
        }
    }

    [Theory]
    [InlineData(-32000)]
    [InlineData(-32008)]
    [InlineData(-32009)]
    [InlineData(-32099)]
    [InlineData(-32604)]
    [InlineData(0)]
    [InlineData(1)]
    public void A_code_that_0_3_does_not_define_has_no_1_0_counterpart(int code) => Assert.Null(V03ErrorMap.FromV03Code(code));

    [Fact]
    public void Unknown_0_3_code_gives_an_internal_error_with_message_and_data_kept()
    {
        A2AException ex = V03ErrorMap.FromV03(new V03JsonRpcError { Code = -32050, Message = "custom", Data = ParseJson("""{"a":1}""") });
        Assert.Equal(A2AErrorCode.InternalError, ex.Code);
        Assert.Equal("custom", ex.Message);
        Assert.Equal(1, ex.Data!.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public void Unknown_1_0_enum_value_maps_to_internal_error()
        => Assert.Equal(-32603, V03ErrorMap.ToV03Code((A2AErrorCode)(-32050)));

    [Fact]
    public void Data_is_the_exception_data_as_is_and_not_an_error_info_array()
    {
        var ex = new A2AException(A2AErrorCode.TaskNotFound, "Task not found", ParseJson("""{"taskId":"t-1"}"""));
        V03JsonRpcError error = V03ErrorMap.ToV03(ex);
        Assert.Equal(-32001, error.Code);
        Assert.Equal("Task not found", error.Message);
        Assert.Equal(JsonValueKind.Object, error.Data!.Value.ValueKind);
        Assert.Equal("t-1", error.Data.Value.GetProperty("taskId").GetString());

        Assert.Null(V03ErrorMap.ToV03(new A2AException(A2AErrorCode.InternalError, "boom")).Data);
        Assert.Equal("""{"code":-32603,"message":"boom"}""", JsonSerializer.Serialize(V03ErrorMap.ToV03(new A2AException(A2AErrorCode.InternalError, "boom")), Ctx.V03JsonRpcError));
    }

    [Fact]
    public void One_point_zero_only_code_keeps_message_and_data_under_invalid_request()
    {
        V03JsonRpcError error = V03ErrorMap.ToV03(new A2AException(A2AErrorCode.VersionNotSupported, "bad version", ParseJson("""{"supported":"1.0"}""")));
        Assert.Equal(-32600, error.Code);
        Assert.Equal("bad version", error.Message);
        Assert.Equal("1.0", error.Data!.Value.GetProperty("supported").GetString());
    }

    public static TheoryData<string> ErrorFixtures() => Data("errors", f => !f.Contains("rest-problem-details", StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(ErrorFixtures))]
    public void Fixture_error_round_trips_through_the_exception(string rel)
    {
        V03JsonRpcError original = JsonSerializer.Deserialize(Member(rel, "error"), Ctx.V03JsonRpcError)!;
        A2AException ex = V03ErrorMap.FromV03(original);
        Assert.Equal(original.Code, (int)ex.Code);
        V03JsonRpcError back = V03ErrorMap.ToV03(ex);
        Assert.Equal(JsonSerializer.Serialize(original, Ctx.V03JsonRpcError), JsonSerializer.Serialize(back, Ctx.V03JsonRpcError));
    }
}
