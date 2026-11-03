using System.Diagnostics;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>One task event waiting for webhook delivery. <paramref name="IsFinal"/>: the task is terminal, so its configs go after delivery.
/// <paramref name="Parent"/>: the span that committed the event; the delivery span is its child.</summary>
internal sealed record PushWorkItem(string AgentName, string TaskId, StreamResponse Event, bool IsFinal, ActivityContext Parent);
