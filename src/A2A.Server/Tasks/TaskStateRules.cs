using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>Task state classification and the transitions an agent may make.</summary>
public static class TaskStateRules
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
    /// True when an agent may move a task from <paramref name="from"/> to <paramref name="to"/> (spec D13).
    /// Only SUBMITTED and WORKING have agent moves: to WORKING, INPUT_REQUIRED, AUTH_REQUIRED, COMPLETED,
    /// FAILED, REJECTED or CANCELED. Nothing leaves a terminal state. Only the server leaves an interrupted
    /// state (it sets SUBMITTED for a continuation, CANCELED for a cancel request). Agents never set
    /// UNSPECIFIED or SUBMITTED.
    /// </summary>
    public static bool CanAgentTransition(TaskState from, TaskState to)
        => from is TaskState.Submitted or TaskState.Working
           && to is TaskState.Working or TaskState.InputRequired or TaskState.AuthRequired
               or TaskState.Completed or TaskState.Failed or TaskState.Rejected or TaskState.Canceled;

    /// <summary>True when the event ends a stream: a message, or a task/status in a final state.</summary>
    public static bool IsFinal(StreamResponse evt)
        => evt.Message is not null
           || (evt.StatusUpdate is { } s && IsFinal(s.Status.State))
           || (evt.Task is { } t && IsFinal(t.Status.State));
}
