namespace Ambinder.A2A.Protocol;

/// <summary>One turn of communication between user and agent.</summary>
public sealed record Message
{
    public string MessageId { get => field ?? ""; init; }
    public string? ContextId { get; init; }
    public string? TaskId { get; init; }
    public Role Role { get; init; }
    public IReadOnlyList<Part> Parts { get => field ?? []; init; }
    public JsonElement? Metadata { get; init; }
    public IReadOnlyList<string> Extensions { get => field ?? []; init; }
    public IReadOnlyList<string> ReferenceTaskIds { get => field ?? []; init; }
}
