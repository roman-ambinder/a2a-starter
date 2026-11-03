using A2A.Protocol;

namespace A2A.Server.Push;

/// <summary>One task event waiting for webhook delivery. <paramref name="IsFinal"/>: the task is terminal, so its configs go after delivery.</summary>
internal sealed record PushWorkItem(string AgentName, string TaskId, StreamResponse Event, bool IsFinal);
