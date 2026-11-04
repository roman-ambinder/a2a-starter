using System.Text.Json;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>A 0.3 part. The JSON form is a <c>kind</c> union (a2a.json <c>Part</c>); see V03PartConverter.</summary>
internal abstract record V03Part
{
    /// <summary>Optional part metadata (a2a.json <c>PartBase.metadata</c>).</summary>
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>TextPart</c>.</summary>
internal sealed record V03TextPart(string Text) : V03Part;

/// <summary>a2a.json <c>FilePart</c>.</summary>
internal sealed record V03FilePart(V03FileContent File) : V03Part;

/// <summary>a2a.json <c>DataPart</c>.</summary>
internal sealed record V03DataPart(IReadOnlyDictionary<string, JsonElement> Data) : V03Part;

/// <summary>
/// a2a.json <c>FileWithBytes</c> or <c>FileWithUri</c>, discriminated by which of <see cref="Bytes"/> and
/// <see cref="Uri"/> is present. <see cref="Bytes"/> stays the base64 string as it came on the wire.
/// </summary>
internal sealed record V03FileContent
{
    public string? Name { get; init; }
    public string? MimeType { get; init; }
    public string? Bytes { get; init; }
    public string? Uri { get; init; }
}
