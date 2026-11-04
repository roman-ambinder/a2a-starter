using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>Task state classification and the transitions an agent may make.</summary>
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
    /// True when an agent may move a task from <paramref name="from"/> to <paramref name="to"/>.
    /// Nothing leaves a terminal state; agents cannot set UNSPECIFIED or SUBMITTED (the server sets SUBMITTED).
    /// </summary>
    public static bool CanAgentTransition(TaskState from, TaskState to)
        => !IsTerminal(from) && to is not (TaskState.Unspecified or TaskState.Submitted) && Enum.IsDefined(to);

    /// <summary>True when the event ends a stream: a message, or a task/status in a final state.</summary>
    public static bool IsFinal(StreamResponse evt)
        => evt.Message is not null
           || (evt.StatusUpdate is { } s && IsFinal(s.Status.State))
           || (evt.Task is { } t && IsFinal(t.Status.State));
}
