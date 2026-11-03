using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>Task state classification and the spec D13 transition table.</summary>
internal static class TaskStateRules
{
    /// <summary>COMPLETED, FAILED, CANCELED, REJECTED: the task can never change again.</summary>
    public static bool IsTerminal(TaskState state)
        => state is TaskState.Completed or TaskState.Failed or TaskState.Canceled or TaskState.Rejected;

    /// <summary>INPUT_REQUIRED, AUTH_REQUIRED: the task waits for the client.</summary>
    public static bool IsInterrupted(TaskState state)
        => state is TaskState.InputRequired or TaskState.AuthRequired;

    /// <summary>Terminal or interrupted: a blocking send returns and streams close.</summary>
    public static bool IsFinal(TaskState state) => IsTerminal(state) || IsInterrupted(state);

    /// <summary>
    /// True when <paramref name="by"/> may move a task from <paramref name="from"/> (null for a new task) to
    /// <paramref name="to"/> (spec D13). Only SUBMITTED and WORKING have agent moves. Nothing leaves a terminal
    /// state. Only the server leaves an interrupted state (SUBMITTED for a continuation), or a cancel request
    /// (CANCELED). UNSPECIFIED and undefined values are never valid, as source or target.
    /// </summary>
    public static bool CanTransition(TaskState? from, TaskState to, TransitionSource by)
    {
        if (to == TaskState.Unspecified || !Enum.IsDefined(to)) return false;
        if (from is null) return to == TaskState.Submitted && by == TransitionSource.Server;
        var f = from.Value;
        if (f == TaskState.Unspecified || !Enum.IsDefined(f) || IsTerminal(f)) return false;
        if (IsInterrupted(f))
        {
            return (to, by) is (TaskState.Submitted or TaskState.Working, TransitionSource.Server)
                            or (TaskState.Canceled, TransitionSource.CancelRequest);
        }
        // SUBMITTED or WORKING
        return by switch
        {
            TransitionSource.Agent => to is not TaskState.Submitted,
            TransitionSource.CancelRequest => to == TaskState.Canceled,
            TransitionSource.Recovery => to == TaskState.Failed,
            _ => false,
        };
    }

    /// <summary>The states <paramref name="by"/> may move a task to from <paramref name="from"/>, for error messages.</summary>
    public static IReadOnlyList<TaskState> AllowedTargets(TaskState from, TransitionSource by)
        => [.. Enum.GetValues<TaskState>().Where(to => CanTransition(from, to, by))];

    /// <summary>True when the event ends a stream: a message, or a task/status in a final state.</summary>
    public static bool IsFinal(StreamResponse evt)
        => evt.Message is not null
           || (evt.StatusUpdate is { } s && IsFinal(s.Status.State))
           || (evt.Task is { } t && IsFinal(t.Status.State));
}

/// <summary>Who triggers a task state change (spec D13).</summary>
internal enum TransitionSource
{
    /// <summary>The server: it creates a task as SUBMITTED, and a continuation sets SUBMITTED.</summary>
    Server,

    /// <summary>The agent, through its event sink while it runs.</summary>
    Agent,

    /// <summary>A cancel request: it may end a SUBMITTED, WORKING or paused task as CANCELED.</summary>
    CancelRequest,

    /// <summary>Recovery after a restart: it may end a SUBMITTED or WORKING task as FAILED.</summary>
    Recovery,
}
