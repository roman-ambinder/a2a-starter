using System.Text.Json;
using System.Text.Json.Serialization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>
/// The 0.3 <c>Part</c> union (a2a.json <c>TextPart</c>, <c>FilePart</c>, <c>DataPart</c>). Reads <c>kind</c> wherever it
/// is. Without <c>kind</c> the type comes from the members (<c>text</c>, <c>file</c>, <c>data</c>), which is the
/// ProtoJSON shape of a2a.proto <c>Part</c>; there a data part is <c>{"data":{"data":{...}}}</c> and is unwrapped.
/// An unknown <c>kind</c> throws <see cref="JsonException"/>. Writes the schema form, <c>kind</c> first.
/// </summary>
internal sealed class V03PartConverter : JsonConverter<V03Part>
{
    private static readonly string[] Members = ["text", "file", "data"];

    public override V03Part Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? wireKind = V03JsonReading.Scan(reader, Members, 0, out int present);
        bool inferred = wireKind is null;
        string? kind = wireKind;
        if (kind is null)
        {
            kind = (present & 1) != 0 ? "text"
                : (present & 2) != 0 ? "file"
                : (present & 4) != 0 ? "data"
                : throw new JsonException("Cannot tell the part type: no 'kind', 'text', 'file' or 'data'.");
        }

        string? text = null;
        V03FileContent? file = null;
        Dictionary<string, JsonElement>? data = null;
        Dictionary<string, JsonElement>? metadata = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("text"))
            {
                text = V03JsonReading.ReadString(ref reader, "text");
            }
            else if (reader.ValueTextEquals("file"))
            {
                reader.Read();
                file = V03FileContentConverter.ReadValue(ref reader);
            }
            else if (reader.ValueTextEquals("data"))
            {
                data = V03JsonReading.ReadObject(ref reader, "data");
            }
            else if (reader.ValueTextEquals("metadata"))
            {
                metadata = V03JsonReading.ReadObject(ref reader, "metadata");
            }
            else
            {
                V03JsonReading.SkipValue(ref reader);
            }
        }

        switch (kind)
        {
            case "text":
                return new V03TextPart(text ?? "") { Metadata = metadata };
            case "file":
                return new V03FilePart(file ?? throw new JsonException("A file part needs 'file'.")) { Metadata = metadata };
            case "data":
                data ??= new Dictionary<string, JsonElement>();
                if (inferred && data.Count == 1 && data.TryGetValue("data", out var inner) && inner.ValueKind == JsonValueKind.Object)
                {
                    data = ToDictionary(inner);
                }

                return new V03DataPart(data) { Metadata = metadata };
            default:
                throw new JsonException($"Unknown part kind '{kind}'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, V03Part value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case V03TextPart t:
                writer.WriteString("kind", "text");
                writer.WriteString("text", t.Text);
                break;
            case V03FilePart f:
                writer.WriteString("kind", "file");
                writer.WritePropertyName("file");
                new V03FileContentConverter().Write(writer, f.File, options);
                break;
            case V03DataPart d:
                writer.WriteString("kind", "data");
                V03JsonReading.WriteObject(writer, "data", d.Data);
                break;
            default:
                throw new JsonException($"Unknown part type {value.GetType().Name}.");
        }

        V03JsonReading.WriteObject(writer, "metadata", value.Metadata);
        writer.WriteEndObject();
    }

    private static Dictionary<string, JsonElement> ToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = property.Value;
        }

        return result;
    }
}
