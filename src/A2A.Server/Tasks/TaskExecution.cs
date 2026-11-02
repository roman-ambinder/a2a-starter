using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>One background run of an agent for one message.</summary>
internal sealed class TaskExecution(string agentName, string taskId)
{
    public string AgentName { get; } = agentName;
    public string TaskId { get; } = taskId;

    /// <summary>The agent's own token source; cancelled only by CancelTask.</summary>
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>Set under the task lock by CancelTask before it cancels <see cref="Cts"/>.</summary>
    public volatile bool CancelRequested;

    /// <summary>Completes after the agent's first event (for returnImmediately): the reply, or the task after that event.</summary>
    public TaskCompletionSource<SendMessageResponse> FirstResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a blocking send may return: reply, final state, cancel, or the run ended.</summary>
    public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The task as of this execution's final event (terminal, interrupted or cancel). Returned by a blocking send.</summary>
    public AgentTask? FinalTask { get; set; }

    /// <summary>The direct reply, if the agent replied.</summary>
    public Message? Reply { get; set; }
}
