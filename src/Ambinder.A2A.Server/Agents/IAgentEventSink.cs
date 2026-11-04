using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>
/// Where an agent reports results. Invalid use throws <see cref="A2AException"/> with
/// <see cref="A2AErrorCode.InvalidAgentResponse"/>.
/// </summary>
public interface IAgentEventSink
{
    /// <summary>
    /// Answers with a single message instead of a task. Valid only as the agent's first and only event
    /// on a new message; no task is stored.
    /// </summary>
    ValueTask ReplyAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Sets the task status. The optional message is stored as the status message and appended to history.
    /// A terminal or interrupted state ends this execution: later events are rejected.
    /// </summary>
    ValueTask UpdateStatusAsync(TaskState state, Message? message, CancellationToken ct);

    /// <summary>
    /// Adds or replaces an artifact (by <see cref="Artifact.ArtifactId"/>), or appends its parts to the
    /// existing artifact when <paramref name="append"/> is true.
    /// </summary>
    ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, CancellationToken ct);
}
