using System.Text.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Converts messages and parts between the 0.3 models and the 1.0 models, and holds the metadata helpers the other
/// converters use. Every field with a 1.0 home is carried (plan 15, "Lossless conversion"); the ones without are listed in
/// docs/spec-decisions.md. A 0.3 value that cannot be converted throws <see cref="A2AException"/> with
/// <see cref="A2AErrorCode.InvalidParams"/>; a 1.0 value that cannot be written as 0.3 throws it with <see cref="A2AErrorCode.InvalidAgentResponse"/>.
/// </summary>
internal static class V03MessageConverter
{
    /// <summary>Converts a 0.3 message to a 1.0 message. <c>kind</c> is dropped; a missing role reads as <see cref="Role.Unspecified"/>.</summary>
    public static Message ToV1(V03Message message) => new()
    {
        MessageId = message.MessageId,
        ContextId = message.ContextId,
        TaskId = message.TaskId,
        Role = V03Enums.ToRole(message.Role),
        Parts = ToV1Parts(message.Parts),
        Metadata = ToV1Metadata(message.Metadata),
        Extensions = message.Extensions?.ToArray() ?? [],
        ReferenceTaskIds = message.ReferenceTaskIds?.ToArray() ?? [],
    };

    /// <summary>Converts a 1.0 message to a 0.3 message (the model writes <c>kind: "message"</c>). Empty lists become absent members.</summary>
    public static V03Message ToV03(Message message) => new()
    {
        MessageId = message.MessageId,
        ContextId = message.ContextId,
        TaskId = message.TaskId,
        Role = V03Enums.ToV03Role(message.Role),
        Parts = ToV03Parts(message.Parts),
        Metadata = ToV03Metadata(message.Metadata),
        Extensions = NullIfEmpty(message.Extensions),
        ReferenceTaskIds = NullIfEmpty(message.ReferenceTaskIds),
    };

    /// <summary>
    /// Converts 0.3 parts to 1.0 parts. A part list that is absent on the wire is <see langword="null"/> at run time
    /// (the 0.3 models do not apply their initializers when a member is missing), and gives an empty array here.
    /// </summary>
    public static Part[] ToV1Parts(IReadOnlyList<V03Part>? parts)
    {
        if (parts is null)
        {
            return [];
        }

        var result = new Part[parts.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ToV1(parts[i]);
        }

        return result;
    }

    /// <summary>Converts 1.0 parts to 0.3 parts.</summary>
    public static V03Part[] ToV03Parts(IReadOnlyList<Part> parts)
    {
        var result = new V03Part[parts.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ToV03(parts[i]);
        }

        return result;
    }

    /// <summary>
    /// Converts a 0.3 part. A file with <c>bytes</c> gives <see cref="Part.Raw"/>, a file with <c>uri</c> gives <see cref="Part.Url"/>;
    /// <c>mimeType</c> becomes <see cref="Part.MediaType"/> and <c>name</c> becomes <see cref="Part.Filename"/>. Part <c>metadata</c> is copied.
    /// </summary>
    /// <exception cref="A2AException">The file has both or neither of <c>bytes</c> and <c>uri</c>, or <c>bytes</c> is not base64.</exception>
    public static Part ToV1(V03Part part)
    {
        JsonElement? metadata = ToV1Metadata(part.Metadata);
        switch (part)
        {
            case V03TextPart text:
                return new Part { Text = text.Text, Metadata = metadata };
            case V03DataPart data:
                return new Part { Data = ObjectToElement(data.Data), Metadata = metadata };
            case V03FilePart { File: var file }:
                if ((file.Bytes is null) == (file.Uri is null))
                {
                    throw new A2AException(A2AErrorCode.InvalidParams, "A file part needs exactly one of 'bytes' and 'uri'.");
                }

                byte[]? raw = null;
                if (file.Bytes is not null)
                {
                    try
                    {
                        raw = Convert.FromBase64String(file.Bytes);
                    }
                    catch (FormatException)
                    {
                        throw new A2AException(A2AErrorCode.InvalidParams, "The 'bytes' of a file part is not valid base64.");
                    }
                }

                return new Part { Raw = raw, Url = file.Uri, MediaType = file.MimeType, Filename = file.Name, Metadata = metadata };
            default:
                throw new A2AException(A2AErrorCode.InvalidParams, "Unknown part type.");
        }
    }

    /// <summary>
    /// Converts a 1.0 part. <see cref="Part.Raw"/> gives a file with <c>bytes</c>, <see cref="Part.Url"/> a file with <c>uri</c>.
    /// <see cref="Part.MediaType"/> and <see cref="Part.Filename"/> of a text or data part have no 0.3 home and are dropped;
    /// a data value that is not a JSON object is wrapped as <c>{"value": ...}</c> (implementer default, see docs/spec-decisions.md).
    /// </summary>
    /// <exception cref="A2AException">The part does not have exactly one of text, raw, url and data.</exception>
    public static V03Part ToV03(Part part)
    {
        if (!part.IsValid)
        {
            throw new A2AException(A2AErrorCode.InvalidAgentResponse, "A part needs exactly one of text, raw, url and data.");
        }

        IReadOnlyDictionary<string, JsonElement>? metadata = ToV03Metadata(part.Metadata);
        if (part.Text is not null)
        {
            return new V03TextPart(part.Text) { Metadata = metadata };
        }

        if (part.Data is { } data)
        {
            return new V03DataPart(ToV03DataObject(data)) { Metadata = metadata };
        }

        return new V03FilePart(new V03FileContent
        {
            Name = part.Filename,
            MimeType = part.MediaType,
            Bytes = part.Raw is null ? null : Convert.ToBase64String(part.Raw),
            Uri = part.Url,
        })
        { Metadata = metadata };
    }

    /// <summary>Turns a 0.3 metadata object into the 1.0 JSON value. <see langword="null"/> stays <see langword="null"/>; an empty object stays <c>{}</c>.</summary>
    public static JsonElement? ToV1Metadata(IReadOnlyDictionary<string, JsonElement>? metadata)
        => metadata is null ? null : ObjectToElement(metadata);

    /// <summary>
    /// Turns the 1.0 metadata value into a 0.3 metadata object. 0.3 metadata is always an object, so a value that is not
    /// an object has no 0.3 home and is dropped (listed in docs/spec-decisions.md).
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement>? ToV03Metadata(JsonElement? metadata)
        => metadata is { ValueKind: JsonValueKind.Object } obj ? ElementToObject(obj) : null;

    /// <summary>Returns <see langword="null"/> for an empty list, so the 0.3 writer omits the member.</summary>
    public static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list.ToArray();

    internal static JsonElement ObjectToElement(IReadOnlyDictionary<string, JsonElement> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in values)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static Dictionary<string, JsonElement> ElementToObject(JsonElement obj)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var property in obj.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ToV03DataObject(JsonElement data)
        => data.ValueKind == JsonValueKind.Object
            ? ElementToObject(data)
            : new Dictionary<string, JsonElement> { ["value"] = data.Clone() };
}
