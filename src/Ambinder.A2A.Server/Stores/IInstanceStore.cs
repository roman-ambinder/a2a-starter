namespace Ambinder.A2A.Server.Stores;

/// <summary>Stores one lease row per server instance. Used by recovery (09) and the cluster (06).</summary>
/// <remarks>
/// Guarantees an implementer must give:
/// <list type="bullet">
/// <item><description>Thread-safe.</description></item>
/// <item><description><see cref="HeartbeatAsync"/> inserts or updates the row and returns it with a new version. It throws <see cref="A2AStoreException"/> with <see cref="StoreErrorKind.Conflict"/> when the row was changed or taken over since the lease was read.</description></item>
/// <item><description><see cref="ListExpiredAsync"/> returns only rows that still exist and have <c>ExpiresAt</c> at or before <c>now</c>.</description></item>
/// <item><description><see cref="DeleteAsync"/> deletes only when the version matches; otherwise it throws <see cref="StoreErrorKind.Conflict"/>. Deleting a missing row is not an error.</description></item>
/// <item><description>Throw only <see cref="A2AStoreException"/> and <see cref="OperationCanceledException"/>.</description></item>
/// </list>
/// </remarks>
public interface IInstanceStore
{
    /// <summary>Upserts the row and pushes <c>ExpiresAt</c> forward. Fails with <see cref="StoreErrorKind.Conflict"/> if the row was taken over (epoch changed).</summary>
    /// <param name="lease">The lease to write.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The stored lease, with its new version.</returns>
    ValueTask<InstanceLease> HeartbeatAsync(InstanceLease lease, CancellationToken ct);

    /// <summary>Lists all lease rows.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The rows.</returns>
    ValueTask<IReadOnlyList<InstanceLease>> ListAsync(CancellationToken ct);

    /// <summary>Lists rows with <c>ExpiresAt</c> at or before <paramref name="now"/> that still exist. Recovery (09) handles their tasks, then deletes the row.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The expired rows.</returns>
    ValueTask<IReadOnlyList<InstanceLease>> ListExpiredAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Deletes a row if its version still matches.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="expectedVersion">The version the caller read.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask DeleteAsync(string instanceId, string expectedVersion, CancellationToken ct);
}

/// <summary>Whether an instance takes new work.</summary>
public enum InstanceStatus
{
    /// <summary>The instance takes new work.</summary>
    Active,

    /// <summary>The instance is shutting down and takes no new work.</summary>
    Draining,
}

/// <summary>The lease row of one server instance.</summary>
public sealed record InstanceLease
{
    /// <summary>The unique id of the server instance that owns this lease.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Pod-to-pod URL, set by package 06.</summary>
    public string? InternalAddress { get; init; }

    /// <summary>Whether the instance is active or draining.</summary>
    public InstanceStatus Status { get; init; }

    /// <summary>When the instance started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the lease expires if not renewed.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Opaque concurrency token. Null before the first heartbeat.</summary>
    public string? Version { get; init; }
}
