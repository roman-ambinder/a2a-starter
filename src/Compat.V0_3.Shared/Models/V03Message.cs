using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>Marker for the two types a 0.3 <c>message/send</c> result can be (task or message).</summary>
internal interface IV03Result;

/// <summary>Marker for everything a 0.3 stream can carry (task, message, status-update, artifact-update).</summary>
internal interface IV03StreamEvent;

/// <summary>a2a.json <c>Message</c>. <see cref="Role"/> stays the wire string (<c>user</c> or <c>agent</c>).</summary>
internal sealed record V03Message : IV03Result, IV03StreamEvent
{
    [JsonPropertyName("kind")] public string Kind { get; } = "message";
    public string? Role { get; init; }
    public IReadOnlyList<V03Part> Parts { get; init; } = [];
    public string MessageId { get; init; } = "";
    public string? TaskId { get; init; }
    public string? ContextId { get; init; }
    public IReadOnlyList<string>? ReferenceTaskIds { get; init; }
    public IReadOnlyList<string>? Extensions { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Read-only alias for <see cref="Parts"/>: the 0.3 proto names the field <c>content</c> (a2a.proto <c>Message.content</c>). Never written.</summary>
    [JsonPropertyName("content")]
    public IReadOnlyList<V03Part>? Content
    {
        get => null;
        init
        {
            if (value is not null)
            {
                Parts = value;
            }
        }
    }
}
