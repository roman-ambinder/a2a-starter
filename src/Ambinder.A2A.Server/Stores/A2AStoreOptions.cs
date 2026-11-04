namespace Ambinder.A2A.Server.Stores;

/// <summary>Retention options for task stores. Bound from the configuration section <c>A2A:Store</c>.</summary>
public sealed class A2AStoreOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "A2A:Store";

    /// <summary>Time after a task reaches a terminal state until it is deleted. Null keeps tasks forever. Default 30 days.</summary>
    public TimeSpan? TerminalTaskTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>The same for INPUT_REQUIRED and AUTH_REQUIRED tasks, counted from the last update. Null keeps them forever. Default 30 days.</summary>
    public TimeSpan? InterruptedTaskTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the maintenance service purges expired tasks. Default 5 minutes.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Rows per purge call; the service loops until fewer are returned. Default 500.</summary>
    public int PurgeBatchSize { get; set; } = 500;
}
