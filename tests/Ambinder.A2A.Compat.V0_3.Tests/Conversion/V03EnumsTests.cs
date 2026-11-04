using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03EnumsTests
{
    // The nine a2a.json TaskState strings, from inventory.json (taskStates.jsonEnumOrder).
    public static TheoryData<string, TaskState> States() => new()
    {
        { "submitted", TaskState.Submitted },
        { "working", TaskState.Working },
        { "input-required", TaskState.InputRequired },
        { "completed", TaskState.Completed },
        { "canceled", TaskState.Canceled },
        { "failed", TaskState.Failed },
        { "rejected", TaskState.Rejected },
        { "auth-required", TaskState.AuthRequired },
        { "unknown", TaskState.Unspecified },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void State_string_maps_to_1_0_state(string v03, TaskState v1) => Assert.Equal(v1, V03Enums.ToTaskState(v03));

    [Theory]
    [MemberData(nameof(States))]
    public void State_maps_to_0_3_string(string v03, TaskState v1) => Assert.Equal(v03, V03Enums.ToV03State(v1));

    [Theory]
    [InlineData("")]
    [InlineData("TASK_STATE_WORKING")]
    [InlineData("Working")]
    [InlineData("cancelled")]
    [InlineData(null)]
    public void Unrecognized_state_string_reads_as_unspecified(string? v03) => Assert.Equal(TaskState.Unspecified, V03Enums.ToTaskState(v03));

    [Fact]
    public void Every_1_0_state_has_a_distinct_0_3_string_except_unspecified_and_unknown_values()
    {
        var names = Enum.GetValues<TaskState>().Select(V03Enums.ToV03State).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("unknown", V03Enums.ToV03State((TaskState)99));
    }

    [Theory]
    [InlineData("user", Role.User)]
    [InlineData("agent", Role.Agent)]
    public void Role_maps_both_ways(string v03, Role v1)
    {
        Assert.Equal(v1, V03Enums.ToRole(v03));
        Assert.Equal(v03, V03Enums.ToV03Role(v1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("system")]
    [InlineData("ROLE_USER")]
    public void Unrecognized_role_reads_as_unspecified(string? v03) => Assert.Equal(Role.Unspecified, V03Enums.ToRole(v03));

    [Fact]
    public void Unspecified_role_has_no_0_3_string() => Assert.Null(V03Enums.ToV03Role(Role.Unspecified));
}
