using System.Text.Json.Serialization;

namespace Ambinder.A2A.Protocol;

/// <summary>Content container. Exactly one of Text, Raw, Url or Data must be set.</summary>
public sealed record Part
{
    public string? Text { get; init; }
    /// <summary>Binary content; base64 in JSON.</summary>
    public byte[]? Raw { get; init; }
    public string? Url { get; init; }
    public JsonElement? Data { get; init; }
    public JsonElement? Metadata { get; init; }
    public string? Filename { get; init; }
    public string? MediaType { get; init; }

    public static Part FromText(string text, string? mediaType = null) => new() { Text = text, MediaType = mediaType };
    public static Part FromRaw(byte[] raw, string mediaType, string? filename = null) => new() { Raw = raw, MediaType = mediaType, Filename = filename };
    public static Part FromUrl(string url, string mediaType, string? filename = null) => new() { Url = url, MediaType = mediaType, Filename = filename };
    public static Part FromData(JsonElement data) => new() { Data = data };

    /// <summary>True when exactly one content field is set.</summary>
    [JsonIgnore]
    public bool IsValid => (Text is not null ? 1 : 0) + (Raw is not null ? 1 : 0) + (Url is not null ? 1 : 0) + (Data is not null ? 1 : 0) == 1;

    [JsonIgnore]
    public string EffectiveMediaType =>
        Text is not null ? "text/plain"
        : Data is not null ? "application/json"
        : MediaType ?? "application/octet-stream";
}
