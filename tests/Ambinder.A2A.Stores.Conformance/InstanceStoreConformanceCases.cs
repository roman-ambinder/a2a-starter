namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Framework-neutral Instance store conformance cases. Task 6/7 add the real catalog.</summary>
public static class InstanceStoreConformanceCases
{
    /// <summary>Every case, in a stable order.</summary>
    public static IReadOnlyList<ConformanceCase> All { get; } =
    [
        new() { Id = "placeholder.instance", Run = Placeholder },
    ];

    private static Task Placeholder(StoreSet s, CancellationToken ct)
    {
        ConformanceAssert.NotNull(s.Instances, "The Instances store must be provided.");
        return Task.CompletedTask;
    }
}
