using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Converts stream events between the 0.3 models and the 1.0 <see cref="StreamResponse"/>.
/// <c>final</c> on a status update is NOT set here: 0.3 requires it, 1.0 has no such member, and the 0.3 stream writer
/// (plan 15, Task 10) decides it from the task state. The converter leaves it at <see langword="false"/>.
/// </summary>
internal static class V03EventConverter
{
    /// <summary>Converts a 0.3 stream event (task, message, status-update, artifact-update) to a 1.0 <see cref="StreamResponse"/>. <c>final</c> is dropped.</summary>
    /// <exception cref="A2AException">The event is of an unknown type.</exception>
    public static StreamResponse ToV1(IV03StreamEvent streamEvent) => streamEvent switch
    {
        V03Task task => new StreamResponse { Task = V03TaskConverter.ToV1(task) },
        V03Message message => new StreamResponse { Message = V03MessageConverter.ToV1(message) },
        V03TaskStatusUpdateEvent status => new StreamResponse { StatusUpdate = ToV1(status) },
        V03TaskArtifactUpdateEvent artifact => new StreamResponse { ArtifactUpdate = ToV1(artifact) },
        _ => throw new A2AException(A2AErrorCode.InvalidParams, "Unknown stream event type."),
    };

    /// <summary>
    /// Converts a 1.0 <see cref="StreamResponse"/> to a 0.3 stream event with the right <c>kind</c>. A status update has
    /// <c>final</c> = <see langword="false"/>; the stream writer sets it.
    /// </summary>
    /// <exception cref="A2AException">Not exactly one member of the response is set.</exception>
    public static IV03StreamEvent ToV03(StreamResponse response)
    {
        int set = (response.Task is null ? 0 : 1) + (response.Message is null ? 0 : 1)
            + (response.StatusUpdate is null ? 0 : 1) + (response.ArtifactUpdate is null ? 0 : 1);
        if (set != 1)
        {
            throw new A2AException(A2AErrorCode.InvalidAgentResponse, "A stream response holds exactly one of task, message, status update and artifact update.");
        }

        if (response.Task is not null)
        {
            return V03TaskConverter.ToV03(response.Task);
        }

        if (response.Message is not null)
        {
            return V03MessageConverter.ToV03(response.Message);
        }

        return response.StatusUpdate is not null ? ToV03(response.StatusUpdate) : ToV03(response.ArtifactUpdate!);
    }

    /// <summary>Converts a 0.3 status update. <see cref="V03TaskStatusUpdateEvent.Final"/> has no 1.0 home and is dropped.</summary>
    public static TaskStatusUpdateEvent ToV1(V03TaskStatusUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Status = update.Status is { } status ? V03TaskConverter.ToV1(status) : new AgentTaskStatus(),
        Metadata = V03MessageConverter.ToV1Metadata(update.Metadata),
    };

    /// <summary>Converts a 1.0 status update. <c>final</c> stays <see langword="false"/> (set by the stream writer).</summary>
    public static V03TaskStatusUpdateEvent ToV03(TaskStatusUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Status = V03TaskConverter.ToV03(update.Status),
        Metadata = V03MessageConverter.ToV03Metadata(update.Metadata),
    };

    /// <summary>Converts a 0.3 artifact update. A missing <c>append</c> or <c>lastChunk</c> reads as <see langword="false"/>.</summary>
    public static TaskArtifactUpdateEvent ToV1(V03TaskArtifactUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Artifact = update.Artifact is { } artifact ? V03TaskConverter.ToV1(artifact) : new Artifact(),
        Append = update.Append ?? false,
        LastChunk = update.LastChunk ?? false,
        Metadata = V03MessageConverter.ToV1Metadata(update.Metadata),
    };

    /// <summary>Converts a 1.0 artifact update. <c>append</c> and <c>lastChunk</c> are always written, also when <see langword="false"/>.</summary>
    public static V03TaskArtifactUpdateEvent ToV03(TaskArtifactUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Artifact = V03TaskConverter.ToV03(update.Artifact),
        Append = update.Append,
        LastChunk = update.LastChunk,
        Metadata = V03MessageConverter.ToV03Metadata(update.Metadata),
    };
}
