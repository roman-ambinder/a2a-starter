namespace Ambinder.A2A.Protocol;

/// <summary>An output produced by a task.</summary>
public sealed record Artifact
{
    /// <summary>The ID of the artifact. It is unique within the task.</summary>
    public string ArtifactId { get => field ?? ""; init; }
    /// <summary>A short name for the artifact.</summary>
    public string? Name { get; init; }
    /// <summary>What the artifact contains.</summary>
    public string? Description { get; init; }
    /// <summary>The content of the artifact.</summary>
    public IReadOnlyList<Part> Parts { get => field ?? []; init; }
    /// <summary>Extra data attached to the artifact, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
    /// <summary>The URIs of the extensions this artifact uses.</summary>
    public IReadOnlyList<string> Extensions { get => field ?? []; init; }
}
