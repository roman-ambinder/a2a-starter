using System.Collections.Concurrent;
using System.Diagnostics;
using A2A.Protocol;
using A2A.Server.Telemetry;
using Microsoft.Extensions.Logging;

namespace A2A.Server.Tasks;

/// <summary>
/// Per-task locking and the single write path for task changes: save, publish to the broker, notify observers.
/// Call <see cref="CommitAsync"/> only while holding the task's lock, so events stay in order.
/// </summary>
internal sealed class TaskWriter(
    ITaskStore store,
    TaskEventBroker broker,
    IReadOnlyList<ITaskEventObserver> observers,
    TimeProvider time,
    ILogger logger)
{
    // One semaphore per task, kept for the process lifetime (Phase 1 is in-memory only).
    private readonly ConcurrentDictionary<(string Agent, string TaskId), SemaphoreSlim> _locks = new();

    public ITaskStore Store => store;
    public TaskEventBroker Broker => broker;
    public DateTimeOffset Now => time.GetUtcNow();

    public async ValueTask<Releaser> LockAsync(string agent, string taskId, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd((agent, taskId), static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    /// <summary>Saves the task, then publishes the event and notifies observers. Not cancellable: a change is never half-applied.</summary>
    public async ValueTask CommitAsync(StoredTask task, StreamResponse evt)
    {
        await store.SaveAsync(task, CancellationToken.None).ConfigureAwait(false);
        try
        {
            RecordState(task, evt);
        }
        catch (Exception ex)
        {
            // A throwing listener must not stop the publish or the observers: the task is already saved.
            logger.LogDebug(ex, "Recording telemetry failed for task {TaskId}", task.Task.Id);
        }
        broker.Publish(task.AgentName, task.Task.Id, evt);
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnTaskEventAsync(task, evt, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Task event observer {Observer} failed for task {TaskId}", observer.GetType().Name, task.Task.Id);
            }
        }
    }

    /// <summary>Adds a state event to the current span, if it is recorded. A terminal state also counts once in the completed-task metric.</summary>
    private static void RecordState(StoredTask task, StreamResponse evt)
    {
        var state = evt.StatusUpdate?.Status.State ?? evt.Task?.Status.State;
        if (state is null) return;
        var value = A2ATelemetryNames.StateValue(state.Value);
        // Only on a recorded span: an unrecorded one (such as ASP.NET Core's logging activity) would just allocate the event.
        if (Activity.Current is { IsAllDataRequested: true } span)
            span.AddEvent(new ActivityEvent(A2ATelemetryNames.TaskStateEvent,
                tags: new ActivityTagsCollection { { A2ATelemetryNames.TaskState, value } }));
        if (TaskStateRules.IsTerminal(state.Value))
            ServerInstruments.TasksCompleted.Add(1,
                new KeyValuePair<string, object?>(A2ATelemetryNames.Agent, task.AgentName),
                new KeyValuePair<string, object?>(A2ATelemetryNames.TaskState, value));
    }

    /// <summary>Applies a status change (timestamp, message → history) and commits a status update event.</summary>
    public async ValueTask<StoredTask> CommitStatusAsync(StoredTask stored, TaskState state, Message? message)
    {
        var task = stored.Task;
        var status = new AgentTaskStatus { State = state, Message = message, Timestamp = Now };
        var updated = stored with
        {
            Task = task with
            {
                Status = status,
                History = message is null ? task.History : [.. task.History, message],
            },
        };
        await CommitAsync(updated, new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = task.Id, ContextId = task.ContextId ?? "", Status = status },
        }).ConfigureAwait(false);
        return updated;
    }

    /// <summary>A server-authored agent message, e.g. "Agent error".</summary>
    public static Message AgentMessage(string text, string taskId, string? contextId) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Role = Role.Agent,
        TaskId = taskId,
        ContextId = contextId,
        Parts = [Part.FromText(text)],
    };

    public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
