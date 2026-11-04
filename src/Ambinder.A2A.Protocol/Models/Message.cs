namespace Ambinder.A2A.Protocol;

/// <summary>One turn of communication between user and agent.</summary>
public sealed record Message
{
    /// <summary>The ID of the message. The sender sets it and it must be unique.</summary>
    public string MessageId { get => field ?? ""; init; }
    /// <summary>The ID of the context (conversation) of the message, or <see langword="null"/> to start a new one.</summary>
    public string? ContextId { get; init; }
    /// <summary>The ID of the task this message belongs to, or <see langword="null"/>.</summary>
    public string? TaskId { get; init; }
    /// <summary>Who sent the message.</summary>
    public Role Role { get; init; }
    /// <summary>The content of the message. It needs at least one part.</summary>
    public IReadOnlyList<Part> Parts { get => field ?? []; init; }
    /// <summary>Extra data attached to the message, as a JSON value.</summary>
    public JsonElement? Metadata { get; init; }
    /// <summary>The URIs of the extensions this message uses.</summary>
    public IReadOnlyList<string> Extensions { get => field ?? []; init; }
    /// <summary>The IDs of other tasks this message refers to.</summary>
    public IReadOnlyList<string> ReferenceTaskIds { get => field ?? []; init; }
}
