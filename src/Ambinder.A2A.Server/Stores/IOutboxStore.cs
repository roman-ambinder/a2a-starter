namespace Ambinder.A2A.Server.Stores;

/// <summary>A row in the transactional outbox. Written only through <see cref="TaskWrite.Outbox"/>.</summary>
public sealed record OutboxEntry
{
    /// <summary>A GUID, set by the writer.</summary>
    public required string Id { get; init; }

    /// <summary>The task the row belongs to.</summary>
    public required TaskKey Task { get; init; }

    /// <summary>The task's <see cref="StoredTask.Sequence"/> at write time. Gives per-task order.</summary>
    public required long Sequence { get; init; }

    /// <summary>The kind of work, for example <c>push</c>. Defined by package 08.</summary>
    public required string Kind { get; init; }

    /// <summary>The ordering lane inside the task, for example the push config id. Opaque to the store.</summary>
    public required string Lane { get; init; }

    /// <summary>The payload. Opaque to the store.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>When the row was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row is next due.</summary>
    public DateTimeOffset NextAttemptAt { get; init; }

    /// <summary>How many delivery attempts have failed.</summary>
    public int Attempts { get; init; }

    /// <summary>Who holds the claim, or null.</summary>
    public string? ClaimedBy { get; init; }

    /// <summary>When the claim expires, or null.</summary>
    public DateTimeOffset? ClaimExpiresAt { get; init; }

    /// <summary>Short text of the last failure. No secrets.</summary>
    public string? LastError { get; init; }

    /// <summary>When the row was dead-lettered, or null.</summary>
    public DateTimeOffset? DeadLetteredAt { get; init; }

    /// <summary>Opaque concurrency token. On a claimed row it is the lease token.</summary>
    public string? Version { get; init; }
}

/// <summary>Filter and paging for dead-lettered rows (package 08's operations).</summary>
public sealed record OutboxDeadLetterQuery
{
    /// <summary>Only rows of this kind.</summary>
    public string? Kind { get; init; }

    /// <summary>Only rows of this tenant.</summary>
    public string? Tenant { get; init; }

    /// <summary>Only rows of this agent.</summary>
    public string? Agent { get; init; }

    /// <summary>Only rows of this task.</summary>
    public string? TaskId { get; init; }

    /// <summary>Only rows dead-lettered before this time.</summary>
    public DateTimeOffset? DeadLetteredBefore { get; init; }

    /// <summary>Only rows dead-lettered after this time.</summary>
    public DateTimeOffset? DeadLetteredAfter { get; init; }

    /// <summary>The most rows to return.</summary>
    public int Max { get; init; } = 100;

    /// <summary>The cursor from the previous page, or null for the first page.</summary>
    public string? Cursor { get; init; }
}

/// <summary>One page of dead-lettered rows.</summary>
public sealed record OutboxEntryPage
{
    /// <summary>The rows on this page.</summary>
    public required IReadOnlyList<OutboxEntry> Items { get; init; }

    /// <summary>The cursor for the next page; null on the last page.</summary>
    public string? NextCursor { get; init; }
}

/// <summary>Counts of outbox rows.</summary>
/// <param name="Pending">Rows not yet delivered and not dead-lettered.</param>
/// <param name="OldestCreatedAt">The oldest <c>CreatedAt</c> among pending rows, or null when none.</param>
/// <param name="DeadLettered">Dead-lettered rows.</param>
public readonly record struct OutboxStats(long Pending, DateTimeOffset? OldestCreatedAt, long DeadLettered);

/// <summary>The transactional outbox. Rows are written with the task through <see cref="TaskWrite.Outbox"/>; this interface delivers and manages them.</summary>
/// <remarks>
/// Guarantees an implementer must give:
/// <list type="bullet">
/// <item><description>Thread-safe. A row is claimed by at most one caller at a time.</description></item>
/// <item><description><see cref="ClaimDueAsync"/> returns only due rows (<c>NextAttemptAt</c> at or before <c>now</c>), not claimed or with an expired claim, not dead-lettered, and only lane heads: a row is returned only when no row with a lower <c>Sequence</c> is pending in the same (Task, Lane). Each claim sets <c>ClaimedBy</c>, <c>ClaimExpiresAt</c> and a new <c>Version</c>.</description></item>
/// <item><description><see cref="CompleteAsync"/>, <see cref="RescheduleAsync"/> and <see cref="DeadLetterAsync"/> take the claimed row and throw <see cref="A2AStoreException"/> with <see cref="StoreErrorKind.Conflict"/> when the claim was lost (the version changed).</description></item>
/// <item><description><see cref="DeadLetterAsync"/> frees the lane for the next row.</description></item>
/// <item><description><see cref="RequeueDeadLetterAsync"/> sets <c>Attempts</c> to 0 and clears <c>LastError</c>, <c>ClaimedBy</c>, <c>ClaimExpiresAt</c> and <c>DeadLetteredAt</c>.</description></item>
/// <item><description>Rows are deleted with their task. Tenant is part of every key.</description></item>
/// <item><description>Throw only <see cref="A2AStoreException"/> and <see cref="OperationCanceledException"/>.</description></item>
/// </list>
/// </remarks>
public interface IOutboxStore
{
    /// <summary>Claims up to <paramref name="max"/> due lane-head rows for <paramref name="claimant"/>.</summary>
    /// <param name="claimant">Who claims the rows.</param>
    /// <param name="now">The current time.</param>
    /// <param name="claimFor">How long the claim lasts.</param>
    /// <param name="max">The most rows to claim.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The claimed rows.</returns>
    ValueTask<IReadOnlyList<OutboxEntry>> ClaimDueAsync(string claimant, DateTimeOffset now, TimeSpan claimFor, int max, CancellationToken ct);

    /// <summary>Claims one row by id if it is due, unclaimed (or its claim expired) and a lane head.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="claimant">Who claims the row.</param>
    /// <param name="now">The current time.</param>
    /// <param name="claimFor">How long the claim lasts.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The claimed row, or null when it could not be claimed.</returns>
    ValueTask<OutboxEntry?> TryClaimAsync(string id, string claimant, DateTimeOffset now, TimeSpan claimFor, CancellationToken ct);

    /// <summary>Deletes a claimed row after delivery. Fails with <see cref="StoreErrorKind.Conflict"/> when the claim was lost.</summary>
    /// <param name="claimed">The claimed row.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask CompleteAsync(OutboxEntry claimed, CancellationToken ct);

    /// <summary>Releases a claimed row for a later attempt. Fails with <see cref="StoreErrorKind.Conflict"/> when the claim was lost.</summary>
    /// <param name="claimed">The claimed row.</param>
    /// <param name="nextAttemptAt">When the row is next due.</param>
    /// <param name="lastError">Short text of the failure, no secrets.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask RescheduleAsync(OutboxEntry claimed, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken ct);

    /// <summary>Dead-letters a claimed row and frees its lane for the next row. Fails with <see cref="StoreErrorKind.Conflict"/> when the claim was lost.</summary>
    /// <param name="claimed">The claimed row.</param>
    /// <param name="reason">Short reason, stored as <c>LastError</c>.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask DeadLetterAsync(OutboxEntry claimed, string reason, CancellationToken ct);

    /// <summary>Lists dead-lettered rows, limited to an owner scope when given. For owner-scoped reads; use <see cref="QueryDeadLettersAsync"/> for operator queries.</summary>
    /// <param name="scope">The owner scope, or null for all.</param>
    /// <param name="max">The most rows to return.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The dead-lettered rows.</returns>
    ValueTask<IReadOnlyList<OutboxEntry>> ListDeadLettersAsync(OwnerScope? scope, int max, CancellationToken ct);

    /// <summary>Gets one dead-lettered row.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The row, or null when it does not exist or is not dead-lettered.</returns>
    ValueTask<OutboxEntry?> GetDeadLetterAsync(string id, CancellationToken ct);

    /// <summary>Queries dead-lettered rows by kind, tenant, agent, task and age, with a cursor.</summary>
    /// <param name="query">The filter and paging.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>One page.</returns>
    ValueTask<OutboxEntryPage> QueryDeadLettersAsync(OutboxDeadLetterQuery query, CancellationToken ct);

    /// <summary>Makes a dead-lettered row due again.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="nextAttemptAt">When the row is next due.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>False when the row does not exist or is not dead-lettered.</returns>
    ValueTask<bool> RequeueDeadLetterAsync(string id, DateTimeOffset nextAttemptAt, CancellationToken ct);

    /// <summary>Deletes up to <paramref name="max"/> rows dead-lettered before <paramref name="deadLetteredBefore"/>.</summary>
    /// <param name="deadLetteredBefore">Delete rows dead-lettered before this time.</param>
    /// <param name="max">The most rows to delete.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset deadLetteredBefore, int max, CancellationToken ct);

    /// <summary>Gets the pending count, the oldest pending <c>CreatedAt</c> and the dead-letter count.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The counts.</returns>
    ValueTask<OutboxStats> GetPendingStatsAsync(CancellationToken ct);
}
