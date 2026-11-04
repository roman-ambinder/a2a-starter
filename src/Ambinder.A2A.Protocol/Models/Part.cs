using System.Text.Json.Serialization;

namespace Ambinder.A2A.Protocol;

/// <summary>Content container. Exactly one of Text, Raw, Url or Data must be set.</summary>
public sealed record Part
{
    /// <summary>Text content, or <see langword="null"/>.</summary>
    public string? Text { get; init; }
    /// <summary>Binary content; base64 in JSON.</summary>
    public byte[]? Raw { get; init; }
    /// <summary>A URL that points to the content, or <see langword="null"/>.</summary>
    public string? Url { get; init; }
    /// <summary>Structured content, as a JSON value, or <see langword="null"/>.</summary>
    public JsonElement? Data { get; init; }
    /// <summary>Extra data attached to the part, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
    /// <summary>A file name for <see cref="Raw"/> or <see cref="Url"/> content, or <see langword="null"/>.</summary>
    public string? Filename { get; init; }
    /// <summary>The media type of the content, for example <c>image/png</c>, or <see langword="null"/>.</summary>
    public string? MediaType { get; init; }

    /// <summary>Creates a text part.</summary>
    /// <param name="text">The text.</param>
    /// <param name="mediaType">The media type, or <see langword="null"/> for <c>text/plain</c>.</param>
    /// <returns>A part that holds the text.</returns>
    public static Part FromText(string text, string? mediaType = null) => new() { Text = text, MediaType = mediaType };
    /// <summary>Creates a part from bytes.</summary>
    /// <param name="raw">The content.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="filename">A file name, or <see langword="null"/>.</param>
    /// <returns>A part that holds the bytes.</returns>
    public static Part FromRaw(byte[] raw, string mediaType, string? filename = null) => new() { Raw = raw, MediaType = mediaType, Filename = filename };
    /// <summary>Creates a part that points to content by URL.</summary>
    /// <param name="url">The URL of the content.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="filename">A file name, or <see langword="null"/>.</param>
    /// <returns>A part that holds the URL.</returns>
    public static Part FromUrl(string url, string mediaType, string? filename = null) => new() { Url = url, MediaType = mediaType, Filename = filename };
    /// <summary>Creates a part from structured JSON data.</summary>
    /// <param name="data">The data.</param>
    /// <returns>A part that holds the data.</returns>
    public static Part FromData(JsonElement data) => new() { Data = data };

    /// <summary>True when exactly one content field is set.</summary>
    [JsonIgnore]
    public bool IsValid => (Text is not null ? 1 : 0) + (Raw is not null ? 1 : 0) + (Url is not null ? 1 : 0) + (Data is not null ? 1 : 0) == 1;

    /// <summary>The media type of the content: <c>text/plain</c> for text, <c>application/json</c> for data, otherwise <see cref="MediaType"/> or <c>application/octet-stream</c>.</summary>
    [JsonIgnore]
    public string EffectiveMediaType =>
        Text is not null ? "text/plain"
        : Data is not null ? "application/json"
        : MediaType ?? "application/octet-stream";
}
