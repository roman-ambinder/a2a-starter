namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Framework-neutral Outbox store conformance cases. Task 6/7 add the real catalog.</summary>
public static class OutboxStoreConformanceCases
{
    /// <summary>Every case, in a stable order.</summary>
    public static IReadOnlyList<ConformanceCase> All { get; } =
    [
        new() { Id = "placeholder.outbox", Run = Placeholder },
    ];

    private static Task Placeholder(StoreSet s, CancellationToken ct)
    {
        ConformanceAssert.NotNull(s.Outbox, "The Outbox store must be provided.");
        return Task.CompletedTask;
    }
}
