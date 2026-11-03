using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>
/// Required-field checks shared by JSON-RPC, REST and gRPC. A missing identifier is <see cref="A2AErrorCode.InvalidParams"/>,
/// never <c>TaskNotFound</c>. Call it with the typed request after deserializing (and after path values are merged in).
/// </summary>
public static class RequestValidation
{
    /// <summary>Throws <see cref="A2AException"/> (InvalidParams) when a required field of the request is missing or empty.</summary>
    public static void Validate(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request)
        {
            case SendMessageRequest r:
                if (r.Message.MessageId.Length == 0 || r.Message.Parts.Count == 0)
                    throw Missing("message (messageId and at least one part)");
                break;
            case GetTaskRequest r: Require(r.Id, "id"); break;
            case CancelTaskRequest r: Require(r.Id, "id"); break;
            case SubscribeToTaskRequest r: Require(r.Id, "id"); break;
            case TaskPushNotificationConfig r: Require(r.TaskId, "taskId"); break;
            case GetTaskPushNotificationConfigRequest r: Require(r.TaskId, "taskId"); Require(r.Id, "id"); break;
            case DeleteTaskPushNotificationConfigRequest r: Require(r.TaskId, "taskId"); Require(r.Id, "id"); break;
            case ListTaskPushNotificationConfigsRequest r: Require(r.TaskId, "taskId"); break;
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Missing(name);
    }

    private static A2AException Missing(string name)
        => new(A2AErrorCode.InvalidParams, $"Invalid params: '{name}' is required.");
}
