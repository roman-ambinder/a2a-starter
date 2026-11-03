namespace A2A.Protocol;

/// <summary>An output produced by a task.</summary>
public sealed record Artifact
{
    public string ArtifactId { get => field ?? ""; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Part> Parts { get => field ?? []; init; }
    public JsonElement? Metadata { get; init; }
    public IReadOnlyList<string> Extensions { get => field ?? []; init; }
}
