using A2A.Protocol;
using A2A.Server.Tasks;

namespace A2A.Server.Tests.Tasks;

/// <summary>The agent rows of the spec D13 transition table, every (from, to) pair.</summary>
public class TaskStateRulesTests
{
    private static readonly TaskState Undefined = (TaskState)99;

    // D13: only SUBMITTED and WORKING have agent moves. Everything else (interrupted, terminal,
    // UNSPECIFIED, undefined) allows none; the server alone leaves an interrupted state.
    private static readonly TaskState[] FromRunning =
    [
        TaskState.Working, TaskState.InputRequired, TaskState.AuthRequired,
        TaskState.Completed, TaskState.Failed, TaskState.Rejected, TaskState.Canceled,
    ];

    private static readonly Dictionary<TaskState, TaskState[]> AgentMoves = new()
    {
        [TaskState.Submitted] = FromRunning,
        [TaskState.Working] = FromRunning,
    };

    public static TheoryData<TaskState, TaskState, bool> AllPairs()
    {
        var states = Enum.GetValues<TaskState>().Append(Undefined).ToArray();
        var data = new TheoryData<TaskState, TaskState, bool>();
        foreach (var from in states)
            foreach (var to in states)
                data.Add(from, to, AgentMoves.TryGetValue(from, out var allowed) && allowed.Contains(to));
        return data;
    }

    [Theory, MemberData(nameof(AllPairs))]
    public void CanAgentTransition_follows_the_D13_table(TaskState from, TaskState to, bool expected)
        => Assert.Equal(expected, TaskStateRules.CanAgentTransition(from, to));
}
