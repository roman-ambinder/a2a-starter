using A2A.Protocol;

namespace A2A.Server.Agents;

/// <summary>
/// An agent's logic. A new instance is created for each execution (and for each cancel).
/// </summary>
public interface IAgentHandler
{
    /// <summary>
    /// Handles one user message. Report progress through <paramref name="sink"/>: either one
    /// <see cref="IAgentEventSink.ReplyAsync"/> (direct message, no task), or status and artifact
    /// updates ending in a terminal or interrupted state. <paramref name="ct"/> is cancelled when the
    /// task is cancelled; it is not the caller's request token.
    /// With <c>returnImmediately</c>, SendMessage returns at the agent's first event, so emit WORKING
    /// (or reply) promptly before any slow work.
    /// </summary>
    ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct);

    /// <summary>Called by CancelTask. The default sets the task to CANCELED.</summary>
    ValueTask CancelAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => sink.UpdateStatusAsync(TaskState.Canceled, null, ct);
}
