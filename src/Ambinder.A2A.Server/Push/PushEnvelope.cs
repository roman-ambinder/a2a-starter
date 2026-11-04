using System.Text.Json;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// What push stores in <c>OutboxEntry.Payload</c>: <c>{ "v": 1, "configId", "isFinal", "contentType", "body" }</c>, with <c>body</c> in base64.
/// It carries no URL, token or credentials. The dispatcher reads those from the push config at send time.
/// </summary>
internal readonly record struct PushEnvelope(int V, string ConfigId, bool IsFinal, string ContentType, ReadOnlyMemory<byte> Body)
{
    internal const int CurrentVersion = 1;

    internal static byte[] Write(in PushEnvelope envelope)
    {
        if (envelope.V != CurrentVersion) throw new InvalidDataException($"Cannot write push envelope version {envelope.V}.");
        using var stream = new MemoryStream(envelope.Body.Length + envelope.Body.Length / 2 + 128);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", envelope.V);
            writer.WriteString("configId", envelope.ConfigId);
            writer.WriteBoolean("isFinal", envelope.IsFinal);
            writer.WriteString("contentType", envelope.ContentType);
            writer.WriteBase64String("body", envelope.Body.Span);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <exception cref="InvalidDataException">The bytes are not an envelope, or its version is not 1.</exception>
    internal static PushEnvelope Read(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes.Span);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("Push envelope is not a JSON object.");
            int? v = null;
            string? configId = null, contentType = null;
            bool? isFinal = null;
            byte[]? body = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "v": v = reader.GetInt32(); break;
                    case "configId": configId = reader.GetString(); break;
                    case "isFinal": isFinal = reader.GetBoolean(); break;
                    case "contentType": contentType = reader.GetString(); break;
                    case "body": body = reader.GetBytesFromBase64(); break;
                    default: reader.Skip(); break;
                }
            }
            if (v != CurrentVersion) throw new InvalidDataException($"Unsupported push envelope version {(v is null ? "(missing)" : v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}.");
            if (configId is null || isFinal is null || contentType is null || body is null)
                throw new InvalidDataException("Push envelope is missing a required member.");
            return new PushEnvelope(CurrentVersion, configId, isFinal.Value, contentType, body);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            throw new InvalidDataException("Push envelope is malformed.", e);
        }
    }
}
