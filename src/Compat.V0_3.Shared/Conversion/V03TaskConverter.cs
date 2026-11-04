using System.Globalization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Converts tasks, task status, artifacts and <c>message/send</c> results between the 0.3 models and the 1.0 models.
/// </summary>
internal static class V03TaskConverter
{
    /// <summary>Converts a 0.3 task to a 1.0 task. A 0.3 <c>contextId</c> of <c>""</c> reads as <see langword="null"/>.</summary>
    public static AgentTask ToV1(V03Task task) => new()
    {
        Id = task.Id,
        ContextId = string.IsNullOrEmpty(task.ContextId) ? null : task.ContextId,
        Status = task.Status is { } status ? ToV1(status) : new AgentTaskStatus(),
        Artifacts = task.Artifacts?.Select(ToV1).ToArray() ?? [],
        History = task.History?.Select(V03MessageConverter.ToV1).ToArray() ?? [],
        Metadata = V03MessageConverter.ToV1Metadata(task.Metadata),
    };

    /// <summary>Converts a 1.0 task to a 0.3 task (the model writes <c>kind: "task"</c>). A missing context ID becomes <c>""</c>, because 0.3 requires the member.</summary>
    public static V03Task ToV03(AgentTask task) => new()
    {
        Id = task.Id,
        ContextId = task.ContextId ?? "",
        Status = ToV03(task.Status),
        Artifacts = task.Artifacts.Count == 0 ? null : task.Artifacts.Select(ToV03).ToArray(),
        History = task.History.Count == 0 ? null : task.History.Select(V03MessageConverter.ToV03).ToArray(),
        Metadata = V03MessageConverter.ToV03Metadata(task.Metadata),
    };

    /// <summary>Converts a 0.3 status. An unknown state string reads as <see cref="TaskState.Unspecified"/>.</summary>
    /// <exception cref="A2AException">The timestamp is not a date and time.</exception>
    public static AgentTaskStatus ToV1(V03TaskStatus status) => new()
    {
        State = V03Enums.ToTaskState(status.State),
        Message = status.Message is null ? null : V03MessageConverter.ToV1(status.Message),
        Timestamp = ParseTimestamp(status.Timestamp),
    };

    /// <summary>Converts a 1.0 status. The timestamp is written as UTC with a trailing <c>Z</c>.</summary>
    public static V03TaskStatus ToV03(AgentTaskStatus status) => new()
    {
        State = V03Enums.ToV03State(status.State),
        Message = status.Message is null ? null : V03MessageConverter.ToV03(status.Message),
        Timestamp = FormatTimestamp(status.Timestamp),
    };

    /// <summary>Converts a 0.3 artifact.</summary>
    public static Artifact ToV1(V03Artifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId,
        Name = artifact.Name,
        Description = artifact.Description,
        Parts = V03MessageConverter.ToV1Parts(artifact.Parts),
        Metadata = V03MessageConverter.ToV1Metadata(artifact.Metadata),
        Extensions = artifact.Extensions?.ToArray() ?? [],
    };

    /// <summary>Converts a 1.0 artifact.</summary>
    public static V03Artifact ToV03(Artifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId,
        Name = artifact.Name,
        Description = artifact.Description,
        Parts = V03MessageConverter.ToV03Parts(artifact.Parts),
        Metadata = V03MessageConverter.ToV03Metadata(artifact.Metadata),
        Extensions = V03MessageConverter.NullIfEmpty(artifact.Extensions),
    };

    /// <summary>Converts a 0.3 <c>message/send</c> result (task or message) to a 1.0 <see cref="SendMessageResponse"/>.</summary>
    /// <exception cref="A2AException">The result is neither a task nor a message.</exception>
    public static SendMessageResponse ToV1Result(IV03Result result) => result switch
    {
        V03Task task => new SendMessageResponse { Task = ToV1(task) },
        V03Message message => new SendMessageResponse { Message = V03MessageConverter.ToV1(message) },
        _ => throw new A2AException(A2AErrorCode.InvalidParams, "A message/send result is a task or a message."),
    };

    /// <summary>Converts a 1.0 <see cref="SendMessageResponse"/> to a 0.3 result: a task gives <c>kind: "task"</c>, a message gives <c>kind: "message"</c>.</summary>
    /// <exception cref="A2AException">Not exactly one of <see cref="SendMessageResponse.Task"/> and <see cref="SendMessageResponse.Message"/> is set.</exception>
    public static IV03Result ToV03Result(SendMessageResponse response)
    {
        if ((response.Task is null) == (response.Message is null))
        {
            throw new A2AException(A2AErrorCode.InvalidAgentResponse, "A send message response holds exactly one of task and message.");
        }

        return response.Task is not null
            ? ToV03(response.Task)
            : V03MessageConverter.ToV03(response.Message!);
    }

    /// <summary>
    /// Reads a 0.3 timestamp. A value without an offset counts as UTC (the same rule as the 1.0 reader).
    /// <see langword="null"/> and <c>""</c> read as <see langword="null"/>.
    /// </summary>
    internal static DateTimeOffset? ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            throw new A2AException(A2AErrorCode.InvalidParams, "The status timestamp is not a valid date and time.");
        }

        return value;
    }

    /// <summary>Writes a timestamp as ISO 8601 UTC with a trailing <c>Z</c> and no trailing zero digits (the same form as the 1.0 writer).</summary>
    internal static string? FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)
            .Replace(".Z", "Z", StringComparison.Ordinal);
}
