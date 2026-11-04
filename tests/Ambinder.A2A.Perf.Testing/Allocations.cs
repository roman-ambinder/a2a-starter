using System.Runtime.CompilerServices;

namespace Ambinder.A2A.Perf.Testing;

public static class AllocationBudget
{
    /// <summary>Fails when <paramref name="measuredBytesPerCall"/> is above the budget on the calling test method.</summary>
    public static void Assert<TTest>(long measuredBytesPerCall, [CallerMemberName] string testMethod = "")
    {
        var method = typeof(TTest).GetMethod(testMethod)
            ?? throw new InvalidOperationException($"Test method {typeof(TTest).Name}.{testMethod} not found.");
        var budget = (AllocationBudgetAttribute?)Attribute.GetCustomAttribute(method, typeof(AllocationBudgetAttribute))
            ?? throw new InvalidOperationException($"{typeof(TTest).Name}.{testMethod} has no [AllocationBudget].");
        if (measuredBytesPerCall > budget.MaxBytes)
            throw new AllocationBudgetExceededException(
                $"{typeof(TTest).Name}.{testMethod}: {measuredBytesPerCall} bytes per call, budget {budget.MaxBytes}. " +
                "If this growth is intended, raise the budget in the same PR and give the reason (rule P15).");
    }
}

public sealed class AllocationBudgetExceededException(string message) : Exception(message);

public static class Allocations
{
    /// <summary>Bytes allocated per call of a synchronous <paramref name="work"/> on the calling thread, rounded down, after a warm-up.</summary>
    public static long PerCall(Action work, int iterations = 1000)
    {
        work(); // warm-up: JIT, static init, pools
        work();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) work();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    /// <summary>Bytes allocated per call of <paramref name="work"/> across all threads (it may hop threads), rounded down, after a warm-up.</summary>
    public static async Task<long> PerCallAsync(Func<ValueTask> work, int iterations = 1000)
    {
        await work().ConfigureAwait(false);
        await work().ConfigureAwait(false);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++) await work().ConfigureAwait(false);
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
    }
}
