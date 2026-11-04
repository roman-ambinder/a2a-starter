namespace Ambinder.A2A.Server.Stores;

/// <summary>Stores tasks. Implemented by the in-memory, EF Core and Cosmos DB store packages.</summary>
/// <remarks>
/// Guarantees an implementer must give:
/// <list type="bullet">
/// <item><description>Thread-safe. Called concurrently for different keys and for the same key.</description></item>
/// <item><description><see cref="UpdateAsync"/> replaces the task only when the stored version equals <c>expectedVersion</c>. Otherwise it throws <see cref="A2AStoreException"/> with <see cref="StoreErrorKind.Conflict"/> and never overwrites. It returns the stored record with a new <see cref="StoredTask.Version"/>.</description></item>
/// <item><description><see cref="CreateAsync"/> with <see cref="StoredTask.OriginMessageId"/> set dedupes on (Tenant, Agent, Owner, OriginMessageId): a repeat returns the first task with <see cref="CreateResult.Created"/> false.</description></item>
/// <item><description>Tenant is part of every key. Never return, change or delete a task of another tenant.</description></item>
/// <item><description><see cref="ListAsync"/> orders by <c>Task.Status.Timestamp</c> descending, then <c>TaskId</c> descending (ordinal). <see cref="TaskQuery.After"/> is exclusive. <see cref="TaskPage.Next"/> is null on the last page. <see cref="TaskPage.TotalSize"/> counts all matches and ignores the cursor. <see cref="TaskQuery.StatusTimestampFrom"/> is inclusive. History is trimmed to <see cref="TaskQuery.HistoryLength"/> (0 means none). Artifacts are empty unless <see cref="TaskQuery.IncludeArtifacts"/> is true.</description></item>
/// <item><description>The task write and the rows in <see cref="TaskWrite.Outbox"/> are atomic: both are stored or neither is.</description></item>
/// <item><description>No protocol validation. The core has already validated every input. Throw only <see cref="A2AStoreException"/> and <see cref="OperationCanceledException"/>.</description></item>
/// <item><description><c>History</c> and <c>Artifacts</c> handed in are immutable collections (<see cref="System.Collections.Immutable.ImmutableList{T}"/>) built by the core. A store may keep the references and return the same instance to every caller.</description></item>
/// <item><description>Keep every field of <see cref="StoredTask"/> and of the protocol task (including <c>Metadata</c>) unchanged.</description></item>
/// </list>
/// </remarks>
public interface ITaskStore
{
    /// <summary>Gets a task by key.</summary>
    /// <param name="key">The task key.</param>
    /// <param name="ownerHint">The caller's owner key, or null. Lets owner-partitioned stores do a point read. A store must still find the task when the hint is wrong or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The task, or null when it does not exist.</returns>
    ValueTask<StoredTask?> GetAsync(TaskKey key, string? ownerHint, CancellationToken ct);

    /// <summary>Creates the task. Fails with <see cref="StoreErrorKind.AlreadyExists"/> if the key exists. If the write's <c>OriginMessageId</c> is set and the same scope and message id already created a task, returns that task with <see cref="CreateResult.Created"/> false.</summary>
    /// <param name="write">The task and its outbox rows.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The stored task and whether it was created by this call.</returns>
    ValueTask<CreateResult> CreateAsync(TaskWrite write, CancellationToken ct);

    /// <summary>Replaces the task only if its version equals <paramref name="expectedVersion"/>. Fails with <see cref="StoreErrorKind.Conflict"/> on a version mismatch and <see cref="StoreErrorKind.NotFound"/> if it was deleted.</summary>
    /// <param name="write">The new task and its outbox rows.</param>
    /// <param name="expectedVersion">The version the caller read.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The stored record, with its new version.</returns>
    ValueTask<StoredTask> UpdateAsync(TaskWrite write, string expectedVersion, CancellationToken ct);

    /// <summary>Lists tasks of one owner scope, newest status first.</summary>
    /// <param name="query">The filter and paging.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>One page.</returns>
    ValueTask<TaskPage> ListAsync(TaskQuery query, CancellationToken ct);

    /// <summary>Removes the task, its push configs and its outbox rows.</summary>
    /// <param name="key">The task key.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>False when the task did not exist.</returns>
    ValueTask<bool> DeleteAsync(TaskKey key, CancellationToken ct);

    /// <summary>Lists non-final tasks whose <c>Runner.InstanceId</c> equals <paramref name="instanceId"/>. Used by recovery (09) and the cluster (06).</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="max">The most tasks to return.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The tasks.</returns>
    ValueTask<IReadOnlyList<StoredTask>> ListByRunnerAsync(string instanceId, int max, CancellationToken ct);

    /// <summary>Lists keys of non-final tasks with <c>CancelRequestedAt</c> set that <paramref name="instanceId"/> runs. Polled by the runner pod (06).</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The task keys.</returns>
    ValueTask<IReadOnlyList<TaskKey>> ListCancelRequestsAsync(string instanceId, CancellationToken ct);

    /// <summary>Lists released tasks: <c>Runner</c> is null, state is SUBMITTED or WORKING and <c>UpdatedAt</c> is before <paramref name="olderThan"/>. Oldest first. Used by the recovery sweep (09) and the cluster (06).</summary>
    /// <param name="olderThan">Only tasks last updated before this time.</param>
    /// <param name="limit">The most tasks to return.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The tasks, oldest first.</returns>
    ValueTask<IReadOnlyList<StoredTask>> ListReleasedAsync(DateTimeOffset olderThan, int limit, CancellationToken ct);

    /// <summary>Deletes up to <paramref name="max"/> tasks with <c>ExpiresAt</c> at or before <paramref name="now"/>. A no-op for stores with native TTL.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="max">The most tasks to delete.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The number of tasks deleted.</returns>
    ValueTask<int> PurgeExpiredAsync(DateTimeOffset now, int max, CancellationToken ct);
}

/// <summary>A task to store and the outbox rows to store with it, in one atomic write.</summary>
public sealed record TaskWrite
{
    /// <summary>The task to store.</summary>
    public required StoredTask Task { get; init; }

    /// <summary>Outbox rows to store in the same transaction or batch as the task.</summary>
    public IReadOnlyList<OutboxEntry> Outbox { get; init; } = [];
}

/// <summary>The result of <see cref="ITaskStore.CreateAsync"/>.</summary>
/// <param name="Task">The stored task: the new one, or the earlier one on a dedupe hit.</param>
/// <param name="Created">True when this call created the task; false on a dedupe hit.</param>
public readonly record struct CreateResult(StoredTask Task, bool Created);
