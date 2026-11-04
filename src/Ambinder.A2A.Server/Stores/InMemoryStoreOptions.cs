namespace Ambinder.A2A.Server.Stores;

/// <summary>Options for the in-memory task and push config stores.</summary>
public sealed class InMemoryTaskStoreOptions
{
    /// <summary>Hard cap on tasks. At the cap the oldest expired task is evicted, then the oldest terminal task; if none can be evicted, create fails with <see cref="StoreErrorKind.Capacity"/>. Default 10,000.</summary>
    public int MaxTasks { get; set; } = 10_000;

    /// <summary>Caps the expiry of terminal tasks in memory. Null uses the core's <c>ExpiresAt</c> unchanged. Default 1 hour.</summary>
    public TimeSpan? TerminalTaskTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Hard cap on push configs across all tasks. Default 50,000.</summary>
    public int MaxPushConfigs { get; set; } = 50_000;
}

/// <summary>Options for the in-memory outbox. Used by package 08: at capacity it dead-letters with reason <c>outbox_full</c>.</summary>
public sealed class InMemoryOutboxOptions
{
    /// <summary>The most rows the outbox holds. Default 100,000.</summary>
    public int Capacity { get; set; } = 100_000;
}
