using Ambinder.A2A.Server.Tasks;
using Ambinder.A2A.Server.Stores;

namespace Ambinder.A2A.Server.Tests.Tasks;

public class TaskLockTableTests
{
    private static TaskKey Key(string id) => new("t", "agent", id);

    [Fact]
    public async Task Distinct_keys_locked_and_released_leave_no_entries()
    {
        var table = new TaskLockTable();
        for (var i = 0; i < 100_000; i++)
        {
            using var _ = await table.LockAsync(Key(i.ToString()), CancellationToken.None);
        }
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public async Task Second_waiter_waits_until_first_releases()
    {
        var table = new TaskLockTable();
        var first = await table.LockAsync(Key("a"), CancellationToken.None);
        var secondPending = table.LockAsync(Key("a"), CancellationToken.None).AsTask();
        Assert.False(secondPending.IsCompleted);
        Assert.Equal(1, table.Count);

        first.Dispose();
        var second = await secondPending;
        Assert.Equal(1, table.Count);
        second.Dispose();
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public async Task Waiters_run_in_exclusion()
    {
        var table = new TaskLockTable();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inside = 0;
        var maxInside = 0;
        async Task Worker()
        {
            await gate.Task;
            using var _ = await table.LockAsync(Key("a"), CancellationToken.None);
            maxInside = Math.Max(maxInside, Interlocked.Increment(ref inside));
            await Task.Yield();
            Interlocked.Decrement(ref inside);
        }
        var workers = Enumerable.Range(0, 20).Select(_ => Task.Run(Worker)).ToArray();
        gate.SetResult();
        await Task.WhenAll(workers);
        Assert.Equal(1, maxInside);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public async Task Cancelled_waiter_leaves_no_entry_once_holder_releases()
    {
        var table = new TaskLockTable();
        var holder = await table.LockAsync(Key("a"), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiter = table.LockAsync(Key("a"), cts.Token).AsTask();
        Assert.False(waiter.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(1, table.Count);

        holder.Dispose();
        Assert.Equal(0, table.Count);
    }
}
