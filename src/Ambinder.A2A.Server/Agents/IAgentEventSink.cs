using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>
/// Where an agent reports results. Invalid use throws <see cref="A2AException"/> with
/// <see cref="A2AErrorCode.InvalidAgentResponse"/>.
/// </summary>
/// <remarks>The sink belongs to one execution. Call it from that execution only, one call at a time. Do not keep it after the handler returns.</remarks>
public interface IAgentEventSink
{
    /// <summary>
    /// Answers with a single message instead of a task. Valid only as the agent's first and only event
    /// on a new message; no task is stored.
    /// </summary>
    /// <param name="message">The reply.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the reply is delivered.</returns>
    /// <exception cref="A2AException">The call is not allowed here (<see cref="A2AErrorCode.InvalidAgentResponse"/>).</exception>
    ValueTask ReplyAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Sets the task status. The optional message is stored as the status message and appended to history.
    /// A terminal or interrupted state ends this execution: later events are rejected.
    /// </summary>
    /// <param name="state">The new state.</param>
    /// <param name="message">A message that explains the state, or <see langword="null"/>.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the update is saved and published.</returns>
    /// <exception cref="A2AException">The call is not allowed here (<see cref="A2AErrorCode.InvalidAgentResponse"/>).</exception>
    ValueTask UpdateStatusAsync(TaskState state, Message? message, CancellationToken ct);

    /// <summary>
    /// Adds or replaces an artifact (by <see cref="Artifact.ArtifactId"/>), or appends its parts to the
    /// existing artifact when <paramref name="append"/> is true.
    /// </summary>
    /// <param name="artifact">The artifact, or the next chunk of it.</param>
    /// <param name="append"><see langword="true"/> to append the parts to an existing artifact with the same ID.</param>
    /// <param name="lastChunk"><see langword="true"/> when this is the last chunk of the artifact.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the artifact is saved and published.</returns>
    /// <exception cref="A2AException">The call is not allowed here (<see cref="A2AErrorCode.InvalidAgentResponse"/>).</exception>
    ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, CancellationToken ct);
}
