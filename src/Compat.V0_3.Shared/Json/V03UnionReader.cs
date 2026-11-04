using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>
/// Shared read/write for the two 0.3 unions that mix tasks, messages and stream events. The type is picked by
/// <c>kind</c> (anywhere in the object); without <c>kind</c> by a ProtoJSON wrapper member (a2a.proto
/// <c>StreamResponse</c>: <c>task</c>, <c>message</c>, <c>statusUpdate</c>, <c>artifactUpdate</c>, and the original proto
/// names), and last by the members the shape implies.
/// </summary>
internal static class V03UnionReader
{
    // Index order matters: wrappers 0..6 (object valued), then shape members.
    private static readonly string[] Members =
    [
        "task", "message", "msg", "statusUpdate", "status_update", "artifactUpdate", "artifact_update",
        "artifact", "messageId", "role", "parts", "content", "taskId", "id", "status",
    ];

    private const int WrapperBits = 0b1111111;

    public static IV03StreamEvent Read(ref Utf8JsonReader reader, JsonSerializerOptions options, bool allowEvents)
    {
        string? kind = V03JsonReading.Scan(reader, Members, WrapperBits, out int present);

        if (kind is null)
        {
            if ((present & WrapperBits) != 0)
            {
                return ReadWrapped(ref reader, options, allowEvents);
            }

            kind = Infer(present);
        }

        return ReadByKind(ref reader, options, kind, allowEvents);
    }

    private static string Infer(int present)
    {
        const int artifact = 1 << 7, messageShape = (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11), taskId = 1 << 12, idOrStatus = (1 << 13) | (1 << 14);
        if ((present & artifact) != 0)
        {
            return "artifact-update";
        }

        if ((present & messageShape) != 0)
        {
            return "message";
        }

        if ((present & taskId) != 0)
        {
            return "status-update";
        }

        if ((present & idOrStatus) != 0)
        {
            return "task";
        }

        throw new JsonException("Cannot tell the type of this object: no 'kind' and no members that imply one.");
    }

    private static IV03StreamEvent ReadByKind(ref Utf8JsonReader reader, JsonSerializerOptions options, string kind, bool allowEvents)
    {
        switch (kind)
        {
            case "task":
                return Deserialize<V03Task>(ref reader, options);
            case "message":
                return Deserialize<V03Message>(ref reader, options);
            case "status-update" when allowEvents:
                return Deserialize<V03TaskStatusUpdateEvent>(ref reader, options);
            case "artifact-update" when allowEvents:
                return Deserialize<V03TaskArtifactUpdateEvent>(ref reader, options);
            case "status-update" or "artifact-update":
                throw new JsonException($"A '{kind}' is a stream event, not a task or message result.");
            default:
                throw new JsonException($"Unknown kind '{kind}'.");
        }
    }

    private static IV03StreamEvent ReadWrapped(ref Utf8JsonReader reader, JsonSerializerOptions options, bool allowEvents)
    {
        IV03StreamEvent? result = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string? kind = null;
            if (reader.ValueTextEquals("task"))
            {
                kind = "task";
            }
            else if (reader.ValueTextEquals("message") || reader.ValueTextEquals("msg"))
            {
                kind = "message";
            }
            else if (reader.ValueTextEquals("statusUpdate") || reader.ValueTextEquals("status_update"))
            {
                kind = "status-update";
            }
            else if (reader.ValueTextEquals("artifactUpdate") || reader.ValueTextEquals("artifact_update"))
            {
                kind = "artifact-update";
            }

            if (kind is null || result is not null)
            {
                V03JsonReading.SkipValue(ref reader);
                continue;
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            result = ReadByKind(ref reader, options, kind, allowEvents);
        }

        return result ?? throw new JsonException("No task, message or event in the wrapper object.");
    }

    private static T Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        where T : class
        => JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))
            ?? throw new JsonException($"Expected a {typeof(T).Name} object.");

    public static void Write(Utf8JsonWriter writer, IV03StreamEvent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case V03Task t:
                JsonSerializer.Serialize(writer, t, (JsonTypeInfo<V03Task>)options.GetTypeInfo(typeof(V03Task)));
                break;
            case V03Message m:
                JsonSerializer.Serialize(writer, m, (JsonTypeInfo<V03Message>)options.GetTypeInfo(typeof(V03Message)));
                break;
            case V03TaskStatusUpdateEvent s:
                JsonSerializer.Serialize(writer, s, (JsonTypeInfo<V03TaskStatusUpdateEvent>)options.GetTypeInfo(typeof(V03TaskStatusUpdateEvent)));
                break;
            case V03TaskArtifactUpdateEvent a:
                JsonSerializer.Serialize(writer, a, (JsonTypeInfo<V03TaskArtifactUpdateEvent>)options.GetTypeInfo(typeof(V03TaskArtifactUpdateEvent)));
                break;
            default:
                throw new JsonException($"Unknown stream event type {value.GetType().Name}.");
        }
    }
}
