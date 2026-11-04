namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Framework-neutral Task store conformance cases. Task 6/7 add the real catalog.</summary>
public static class TaskStoreConformanceCases
{
    /// <summary>Every case, in a stable order.</summary>
    public static IReadOnlyList<ConformanceCase> All { get; } =
    [
        new() { Id = "placeholder.task", Run = Placeholder },
    ];

    private static Task Placeholder(StoreSet s, CancellationToken ct)
    {
        ConformanceAssert.NotNull(s.Tasks, "The Tasks store must be provided.");
        return Task.CompletedTask;
    }
}
