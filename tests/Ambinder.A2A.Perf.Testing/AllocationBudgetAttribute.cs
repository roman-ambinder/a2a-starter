namespace Ambinder.A2A.Perf.Testing;

/// <summary>The most bytes one call of the measured hot path may allocate. Set 10% above the value measured after the fix (spec §4).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllocationBudgetAttribute(long maxBytes) : Attribute
{
    public long MaxBytes { get; } = maxBytes;
}
