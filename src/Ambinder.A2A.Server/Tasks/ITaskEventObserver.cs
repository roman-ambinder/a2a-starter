using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>
/// Seam for side effects on task events (push delivery, Task 7). The task manager calls every
/// observer after each task event is saved and published, in event order, while holding the task's lock:
/// implementations must return quickly (queue the work) and must not call back into the manager.
/// Direct replies (no task) are not observed. Exceptions are logged and ignored.
/// </summary>
/// <remarks>
/// Register observers as singletons (<c>services.AddSingleton&lt;ITaskEventObserver, MyObserver&gt;()</c>). Events of one task reach an
/// observer one at a time, in order. Events of different tasks can reach it at the same time, so an implementation must be thread-safe.
/// </remarks>
public interface ITaskEventObserver
{
    /// <summary>Called after a task event is saved and published.</summary>
    /// <param name="task">The task as saved after the event.</param>
    /// <param name="evt">The event.</param>
    /// <param name="ct">A token for the call. The server passes <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that completes when the observer has queued its work.</returns>
    ValueTask OnTaskEventAsync(StoredTask task, StreamResponse evt, CancellationToken ct);
}
