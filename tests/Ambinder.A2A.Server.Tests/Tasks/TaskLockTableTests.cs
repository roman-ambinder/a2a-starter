using Ambinder.A2A.Server.Stores;
using Ambinder.A2A.Server.Tasks;

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

    [Fact]
    public async Task Already_cancelled_token_throws_and_leaves_no_entry()
    {
        var table = new TaskLockTable();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await table.LockAsync(Key("a"), cts.Token));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public async Task Queued_waiter_acquires_after_cancelled_waiter_ahead_of_it()
    {
        var table = new TaskLockTable();
        var holder = await table.LockAsync(Key("a"), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiterA = table.LockAsync(Key("a"), cts.Token).AsTask();
        var waiterB = table.LockAsync(Key("a"), CancellationToken.None).AsTask();
        Assert.False(waiterA.IsCompleted);
        Assert.False(waiterB.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterA);
        Assert.False(waiterB.IsCompleted);

        holder.Dispose();
        var b = await waiterB;
        Assert.Equal(1, table.Count);
        b.Dispose();
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public async Task Same_agent_and_task_id_under_different_tenants_do_not_contend()
    {
        var table = new TaskLockTable();
        var first = await table.LockAsync(new TaskKey("t1", "agent", "x"), CancellationToken.None);
        var secondPending = table.LockAsync(new TaskKey("t2", "agent", "x"), CancellationToken.None).AsTask();
        Assert.True(secondPending.IsCompleted);
        var second = await secondPending;
        Assert.Equal(2, table.Count);

        first.Dispose();
        second.Dispose();
        Assert.Equal(0, table.Count);
    }
}
