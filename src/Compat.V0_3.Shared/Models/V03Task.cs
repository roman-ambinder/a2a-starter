using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>a2a.json <c>Task</c>.</summary>
internal sealed record V03Task : IV03Result, IV03StreamEvent
{
    [JsonPropertyName("kind")] public string Kind { get; } = "task";
    public string Id { get; init; } = "";
    public string ContextId { get; init; } = "";
    public V03TaskStatus Status { get; init; } = new();
    public IReadOnlyList<V03Artifact>? Artifacts { get; init; }
    public IReadOnlyList<V03Message>? History { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>TaskStatus</c>. <see cref="State"/> stays the wire string (a2a.json <c>TaskState</c>).</summary>
internal sealed record V03TaskStatus
{
    public string State { get; init; } = "";
    public V03Message? Message { get; init; }
    public string? Timestamp { get; init; }
}

/// <summary>a2a.json <c>Artifact</c> (<c>description</c> is also in a2a.proto <c>Artifact</c>).</summary>
internal sealed record V03Artifact
{
    public string ArtifactId { get; init; } = "";
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<V03Part> Parts { get; init; } = [];
    public IReadOnlyList<string>? Extensions { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>TaskStatusUpdateEvent</c>. <c>final</c> is required there.</summary>
internal sealed record V03TaskStatusUpdateEvent : IV03StreamEvent
{
    [JsonPropertyName("kind")] public string Kind { get; } = "status-update";
    public string TaskId { get; init; } = "";
    public string ContextId { get; init; } = "";
    public V03TaskStatus Status { get; init; } = new();
    public bool Final { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>TaskArtifactUpdateEvent</c>.</summary>
internal sealed record V03TaskArtifactUpdateEvent : IV03StreamEvent
{
    [JsonPropertyName("kind")] public string Kind { get; } = "artifact-update";
    public string TaskId { get; init; } = "";
    public string ContextId { get; init; } = "";
    public V03Artifact Artifact { get; init; } = new();
    public bool? Append { get; init; }
    public bool? LastChunk { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}
