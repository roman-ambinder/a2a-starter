using System.Text.Json;
using System.Text.Json.Serialization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>
/// a2a.json <c>FileWithBytes</c> / <c>FileWithUri</c>. Reads the schema members (<c>bytes</c>, <c>uri</c>,
/// <c>mimeType</c>, <c>name</c>) and the ProtoJSON spellings of a2a.proto <c>FilePart</c> (<c>fileWithBytes</c>,
/// <c>fileWithUri</c>, <c>mimeType</c>, and the original proto names). Writes the schema form.
/// </summary>
internal sealed class V03FileContentConverter : JsonConverter<V03FileContent>
{
    public override V03FileContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, V03FileContent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Name is not null)
        {
            writer.WriteString("name", value.Name);
        }

        if (value.MimeType is not null)
        {
            writer.WriteString("mimeType", value.MimeType);
        }

        if (value.Bytes is not null)
        {
            writer.WriteString("bytes", value.Bytes);
        }

        if (value.Uri is not null)
        {
            writer.WriteString("uri", value.Uri);
        }

        writer.WriteEndObject();
    }

    internal static V03FileContent ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A file must be a JSON object.");
        }

        string? name = null, mimeType = null, bytes = null, uri = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("name"))
            {
                name = ReadNullableString(ref reader, "name");
            }
            else if (reader.ValueTextEquals("mimeType") || reader.ValueTextEquals("mime_type"))
            {
                mimeType = ReadNullableString(ref reader, "mimeType");
            }
            else if (reader.ValueTextEquals("bytes") || reader.ValueTextEquals("fileWithBytes") || reader.ValueTextEquals("file_with_bytes"))
            {
                bytes = ReadNullableString(ref reader, "bytes");
            }
            else if (reader.ValueTextEquals("uri") || reader.ValueTextEquals("fileWithUri") || reader.ValueTextEquals("file_with_uri"))
            {
                uri = ReadNullableString(ref reader, "uri");
            }
            else
            {
                V03JsonReading.SkipValue(ref reader);
            }
        }

        if (bytes is null && uri is null)
        {
            throw new JsonException("A file needs 'bytes' or 'uri' (a2a.json FileWithBytes / FileWithUri).");
        }

        return new V03FileContent { Name = name, MimeType = mimeType, Bytes = bytes, Uri = uri };
    }

    private static string? ReadNullableString(ref Utf8JsonReader reader, string member)
    {
        reader.Read();
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"'{member}' must be a string."),
        };
    }
}
