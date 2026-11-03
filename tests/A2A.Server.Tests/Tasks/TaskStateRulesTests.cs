using A2A.Protocol;
using A2A.Server.Tasks;

namespace A2A.Server.Tests.Tasks;

/// <summary>The spec D13 transition table: every (from, to, source) combination.</summary>
public class TaskStateRulesTests
{
    private static readonly TaskState Undefined = (TaskState)99;

    // D13, copied by hand from the spec table. From null = a new task.
    private static readonly HashSet<(TaskState? From, TaskState To, TransitionSource By)> Allowed = Build();

    private static HashSet<(TaskState?, TaskState, TransitionSource)> Build()
    {
        var s = new HashSet<(TaskState?, TaskState, TransitionSource)> { (null, TaskState.Submitted, TransitionSource.Server) };
        foreach (var from in new[] { TaskState.Submitted, TaskState.Working })
        {
            foreach (var to in new[] { TaskState.Working, TaskState.InputRequired, TaskState.AuthRequired, TaskState.Completed, TaskState.Failed, TaskState.Rejected, TaskState.Canceled })
                s.Add((from, to, TransitionSource.Agent));
            s.Add((from, TaskState.Canceled, TransitionSource.CancelRequest));
            s.Add((from, TaskState.Failed, TransitionSource.Recovery));
        }
        foreach (var from in new[] { TaskState.InputRequired, TaskState.AuthRequired })
        {
            s.Add((from, TaskState.Submitted, TransitionSource.Server));
            s.Add((from, TaskState.Working, TransitionSource.Server));
            s.Add((from, TaskState.Canceled, TransitionSource.CancelRequest));
        }
        return s;
    }

    public static TheoryData<TaskState?, TaskState, int> AllCombinations()
    {
        var data = new TheoryData<TaskState?, TaskState, int>();
        TaskState?[] froms = [null, .. Enum.GetValues<TaskState>().Append(Undefined).Cast<TaskState?>()];
        foreach (var from in froms)
            foreach (var to in Enum.GetValues<TaskState>().Append(Undefined))
                foreach (var by in Enum.GetValues<TransitionSource>())
                    data.Add(from, to, (int)by);
        return data;
    }

    [Theory]
    [InlineData(TaskState.Unspecified)]
    [InlineData((TaskState)99)]
    public void Unknown_state_is_neither_terminal_nor_interrupted(TaskState state)
    {
        Assert.False(TaskStateRules.IsTerminal(state));
        Assert.False(TaskStateRules.IsInterrupted(state));
    }

    [Theory, MemberData(nameof(AllCombinations))]
    public void Transition_matches_the_spec_table(TaskState? from, TaskState to, int source)
    {
        // TransitionSource is internal, so xUnit gets it as an int.
        var by = (TransitionSource)source;
        Assert.Equal(Allowed.Contains((from, to, by)), TaskStateRules.CanTransition(from, to, by));
    }

    [Fact]
    public void Allowed_targets_list_the_agent_moves()
    {
        Assert.Empty(TaskStateRules.AllowedTargets(TaskState.InputRequired, TransitionSource.Agent));
        TaskState[] expected =
            [TaskState.Working, TaskState.Completed, TaskState.Canceled, TaskState.Failed, TaskState.Rejected, TaskState.InputRequired, TaskState.AuthRequired];
        Assert.Equal(expected.Order(), TaskStateRules.AllowedTargets(TaskState.Submitted, TransitionSource.Agent).Order());
    }
}
