using System.Text;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Testing;

/// <summary>
/// Records every task event of a test server and lets a test wait for one without polling.
/// </summary>
/// <remarks>Thread-safe. Register it as an <see cref="ITaskEventObserver"/>; <see cref="A2ATestServer"/> does that.</remarks>
public sealed class TaskEventLog : ITaskEventObserver
{
    private readonly object _gate = new();
    private readonly List<TaskEvent> _events = [];
    private readonly List<Waiter> _waiters = [];

    /// <summary>A snapshot copy of the events seen so far.</summary>
    public IReadOnlyList<TaskEvent> Events
    {
        get { lock (_gate) return _events.ToArray(); }
    }

    /// <inheritdoc />
    public ValueTask OnTaskEventAsync(StoredTask task, StreamResponse evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(evt);
        List<Waiter>? done = null;
        TaskEvent entry;
        lock (_gate)
        {
            entry = new TaskEvent
            {
                AgentName = task.AgentName,
                TaskId = task.Task.Id,
                State = task.Task.Status.State,
                Event = evt,
                Index = _events.Count,
            };
            _events.Add(entry);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (!_waiters[i].Match(entry)) continue;
                (done ??= []).Add(_waiters[i]);
                _waiters.RemoveAt(i);
            }
        }
        if (done is not null)
            foreach (var w in done) w.Completion.TrySetResult(entry);
        return ValueTask.CompletedTask;
    }

    /// <summary>Waits for the first event that matches. Events already seen are checked first.</summary>
    /// <param name="match">The condition.</param>
    /// <param name="timeout">How long to wait before giving up. It guards against a hang; keep it at or under 10 seconds.</param>
    /// <returns>The first matching event.</returns>
    /// <exception cref="TimeoutException">No event matched in time. The message lists the last 10 events.</exception>
    public Task<TaskEvent> WaitForAsync(Func<TaskEvent, bool> match, TimeSpan timeout)
        => WaitForAsync(match, timeout, match.Method.Name);

    /// <summary>Waits until a task reaches a state.</summary>
    /// <param name="taskId">The task id.</param>
    /// <param name="state">The state to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>The first event that left the task in <paramref name="state"/>.</returns>
    /// <exception cref="TimeoutException">The task did not reach the state in time.</exception>
    public Task<TaskEvent> WaitForTaskStateAsync(string taskId, TaskState state, TimeSpan timeout)
        => WaitForAsync(e => e.TaskId == taskId && e.State == state, timeout, $"task '{taskId}' reaches state {state}");

    private async Task<TaskEvent> WaitForAsync(Func<TaskEvent, bool> match, TimeSpan timeout, string description)
    {
        ArgumentNullException.ThrowIfNull(match);
        Waiter waiter;
        lock (_gate)
        {
            foreach (var e in _events)
                if (match(e)) return e;
            waiter = new Waiter(match);
            _waiters.Add(waiter);
        }

        using var cts = new CancellationTokenSource(timeout);
        using var reg = cts.Token.Register(() =>
        {
            lock (_gate) _waiters.Remove(waiter);
            waiter.Completion.TrySetException(new TimeoutException(Describe(description, timeout)));
        });
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private string Describe(string description, TimeSpan timeout)
    {
        TaskEvent[] last;
        lock (_gate) last = _events.TakeLast(10).ToArray();
        var sb = new StringBuilder();
        sb.Append($"No task event matched '{description}' within {timeout}. ");
        if (last.Length == 0) return sb.Append("No events were seen.").ToString();
        sb.Append($"The last {last.Length} event(s):");
        foreach (var e in last) sb.Append($" [#{e.Index} agent={e.AgentName} task={e.TaskId} state={e.State?.ToString() ?? "none"}]");
        return sb.ToString();
    }

    private sealed class Waiter(Func<TaskEvent, bool> match)
    {
        public Func<TaskEvent, bool> Match { get; } = match;
        public TaskCompletionSource<TaskEvent> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
