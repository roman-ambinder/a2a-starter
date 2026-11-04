using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>
/// Seam for side effects on task events (push delivery, Task 7). <see cref="TaskManager"/> calls every
/// observer after each task event is saved and published, in event order, while holding the task's lock:
/// implementations must return quickly (queue the work) and must not call back into the manager.
/// Direct replies (no task) are not observed. Exceptions are logged and ignored.
/// </summary>
public interface ITaskEventObserver
{
    ValueTask OnTaskEventAsync(StoredTask task, StreamResponse evt, CancellationToken ct);
}
