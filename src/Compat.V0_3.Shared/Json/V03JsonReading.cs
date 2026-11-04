using System.Text.Json;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>Helpers for the hand-written <c>Utf8JsonReader</c> converters.</summary>
internal static class V03JsonReading
{
    /// <summary>
    /// Scans the object at <paramref name="reader"/> (a copy, so the caller's reader does not move) without
    /// parsing nested values. Returns the <c>kind</c> string, or null when there is none. <paramref name="present"/>
    /// gets bit i set when <c>names[i]</c> is a member; if bit i of <paramref name="objectOnly"/> is set, only when
    /// its value is an object.
    /// </summary>
    public static string? Scan(Utf8JsonReader reader, string[] names, int objectOnly, out int present)
    {
        present = 0;
        string? kind = null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isKind = reader.ValueTextEquals("kind");
            int index = -1;
            if (!isKind)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (reader.ValueTextEquals(names[i]))
                    {
                        index = i;
                        break;
                    }
                }
            }

            reader.Read();
            if (isKind)
            {
                kind = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Null => null,
                    _ => throw new JsonException("'kind' must be a string."),
                };
            }
            else if (index >= 0 && (((objectOnly >> index) & 1) == 0 || reader.TokenType == JsonTokenType.StartObject))
            {
                present |= 1 << index;
            }

            reader.Skip();
        }

        return kind;
    }

    public static string ReadString(ref Utf8JsonReader reader, string member)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException($"'{member}' must be a string.");
    }

    /// <summary>Reads the value at the next token as a JSON object into a dictionary (null for JSON null).</summary>
    public static Dictionary<string, JsonElement>? ReadObject(ref Utf8JsonReader reader, string member)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"'{member}' must be an object.");
        }

        var result = new Dictionary<string, JsonElement>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string key = reader.GetString()!;
            reader.Read();
            result[key] = JsonElement.ParseValue(ref reader);
        }

        return result;
    }

    public static void WriteObject(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, JsonElement>? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WriteStartObject(name);
        foreach (var pair in value)
        {
            writer.WritePropertyName(pair.Key);
            pair.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Skips the value of the property the reader is on.</summary>
    public static void SkipValue(ref Utf8JsonReader reader)
    {
        reader.Read();
        reader.Skip();
    }
}
