using Ambinder.A2A.Server.Stores;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>
/// Per-task in-process locks, reference-counted: an entry exists only while someone holds or waits for it (PD8).
/// </summary>
internal sealed class TaskLockTable
{
    private readonly Dictionary<TaskKey, Entry> _entries = [];
    private readonly object _sync = new();

    /// <summary>Number of live entries; for tests.</summary>
    internal int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    public async ValueTask<Releaser> LockAsync(TaskKey key, CancellationToken ct)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }
            entry.RefCount++;
        }
        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, releaseGate: false);
            throw;
        }
        return new Releaser(this, key, entry);
    }

    private void Release(TaskKey key, Entry entry, bool releaseGate)
    {
        lock (_sync)
        {
            if (releaseGate) entry.Gate.Release();
            if (--entry.RefCount == 0)
            {
                _entries.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int RefCount;
    }

    internal readonly struct Releaser(TaskLockTable table, TaskKey key, object entry) : IDisposable
    {
        public void Dispose() => table.Release(key, (Entry)entry, releaseGate: true);
    }
}
