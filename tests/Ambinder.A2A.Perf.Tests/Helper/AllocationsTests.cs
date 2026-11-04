using Ambinder.A2A.Perf.Testing;

namespace Ambinder.A2A.Perf.Tests.Helper;

public sealed class AllocationsTests
{
    [Fact]
    public void Sync_measure_of_no_allocation_is_zero()
        => Assert.Equal(0, Allocations.PerCall(static () => { }, iterations: 1000));

    [Fact]
    public void Sync_measure_sees_an_array()
        => Assert.InRange(Allocations.PerCall(static () => GC.KeepAlive(new byte[100]), iterations: 100), 100, 160);

    [Fact]
    public async Task Async_measure_sees_an_array()
        => Assert.InRange(await Allocations.PerCallAsync(static () => { GC.KeepAlive(new byte[1000]); return ValueTask.CompletedTask; }, iterations: 100), 1000, 1100);

    [Fact]
    [AllocationBudget(0)]
    public void Budget_passes_at_limit()
        => AllocationBudget.Assert<AllocationsTests>(0);

    [Fact]
    [AllocationBudget(10)]
    public void Budget_fails_over_limit()
        => Assert.ThrowsAny<Exception>(() => AllocationBudget.Assert<AllocationsTests>(11));

    [Fact]
    [AllocationBudget(5)]
    public async Task Budget_resolves_the_method_name_in_an_async_test()
    {
        await Task.Yield();
        AllocationBudget.Assert<AllocationsTests>(5);
        await Assert.ThrowsAsync<AllocationBudgetExceededException>(async () =>
        {
            await Task.Yield();
            AllocationBudget.Assert<AllocationsTests>(6);
        });
    }
}
