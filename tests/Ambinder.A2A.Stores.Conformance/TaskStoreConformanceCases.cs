using System.Buffers;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;
using Ambinder.A2A.Server.Stores;
using static Ambinder.A2A.Testing.Conformance.StoreTestData;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>
/// Framework-neutral Task store conformance cases (plan Task 6; spec 05 §7.3, §7.4, §7.9, §7.10). Case ids are final: package 12 publishes them.
/// Every case uses a fresh <see cref="StoreSet"/>, reads time only from <c>s.Time</c> and never waits on the real clock.
/// </summary>
public static class TaskStoreConformanceCases
{
    // Stores such as PostgreSQL keep microseconds, so the smallest time step a case relies on is one millisecond.
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    /// <summary>Every case, in a stable order.</summary>
    public static IReadOnlyList<ConformanceCase> All { get; } =
    [
        // crud
        new() { Id = "crud.create-then-get", Run = CreateThenGet },
        new() { Id = "crud.get-missing-returns-null", Run = GetMissingReturnsNull },
        new() { Id = "crud.create-existing-key-fails", Run = CreateExistingKeyFails },
        new()
        {
            Id = "crud.create-existing-key-other-owner-fails",
            Run = CreateExistingKeyOtherOwnerFails,
            SkipReason = c => c.KeyUniqueAcrossOwners ? null : "The store allows the same key under different owners (KeyUniqueAcrossOwners is false).",
        },
        new() { Id = "crud.all-fields-round-trip", Run = AllFieldsRoundTrip },
        new() { Id = "crud.update-returns-new-version", Run = UpdateReturnsNewVersion },
        new() { Id = "crud.update-missing-fails-not-found", Run = UpdateMissingFailsNotFound },
        new() { Id = "crud.delete-returns-true-then-false", Run = DeleteReturnsTrueThenFalse },
        new() { Id = "crud.get-with-wrong-owner-hint-still-finds", Run = GetWithWrongOwnerHintStillFinds },
        new() { Id = "crud.agent-is-part-of-key", Run = AgentIsPartOfKey },
        new() { Id = "crud.ids-are-ordinal", Run = IdsAreOrdinal },

        // tenant
        new() { Id = "tenant.get-other-tenant-null", Run = TenantGetOtherNull },
        new() { Id = "tenant.update-other-tenant-not-found", Run = TenantUpdateOtherNotFound },
        new() { Id = "tenant.delete-other-tenant-false", Run = TenantDeleteOtherFalse },
        new() { Id = "tenant.list-other-tenant-empty", Run = TenantListOtherEmpty },
        new() { Id = "tenant.empty-tenant-is-own-partition", Run = TenantEmptyIsOwnPartition },

        // list
        new() { Id = "list.owner-scoped", Run = ListOwnerScoped },
        new() { Id = "list.order-timestamp-desc-then-id-desc", Run = ListOrder },
        new() { Id = "list.cursor-exclusive", Run = ListCursorExclusive },
        new() { Id = "list.total-size-ignores-cursor", Run = ListTotalSizeIgnoresCursor },
        new() { Id = "list.exactly-page-size-has-no-next", Run = ListExactlyPageSizeHasNoNext },
        new() { Id = "list.filter-context", Run = ListFilterContext },
        new() { Id = "list.filter-context-not-other-owner", Run = ListFilterContextNotOtherOwner },
        new() { Id = "list.filter-state", Run = ListFilterState },
        new() { Id = "list.timestamp-from-inclusive", Run = ListTimestampFromInclusive },
        new() { Id = "list.filters-combine", Run = ListFiltersCombine },
        new() { Id = "list.empty", Run = ListEmpty },

        // shape
        new() { Id = "shape.history-length-null-keeps-all", Run = ShapeHistoryNull },
        new() { Id = "shape.history-length-zero-none", Run = ShapeHistoryZero },
        new() { Id = "shape.history-length-trims-to-last-n", Run = ShapeHistoryTrims },
        new() { Id = "shape.artifacts-omitted-by-default", Run = ShapeArtifactsOmitted },
        new() { Id = "shape.get-returns-full-task", Run = ShapeGetReturnsFull },

        // concurrency
        new() { Id = "concurrency.stale-version-conflict", Run = StaleVersionConflict },
        new() { Id = "concurrency.racing-writers", Run = RacingWriters },

        // dedupe
        new() { Id = "dedupe.same-message-id-returns-existing", Run = DedupeSame },
        new() { Id = "dedupe.other-owner-not-deduped", Run = DedupeOtherOwner },
        new() { Id = "dedupe.other-tenant-not-deduped", Run = DedupeOtherTenant },
        new() { Id = "dedupe.other-agent-not-deduped", Run = DedupeOtherAgent },
        new() { Id = "dedupe.null-message-id-never-deduped", Run = DedupeNullNever },
        new() { Id = "dedupe.after-delete-creates-new", Run = DedupeAfterDelete },

        // delete
        new() { Id = "delete.removes-push-configs-and-outbox", Run = DeleteCascades },

        // purge
        new() { Id = "purge.removes-expired-only", Run = PurgeRemovesExpiredOnly },
        new() { Id = "purge.respects-max", Run = PurgeRespectsMax },
        new() { Id = "purge.expiry-equal-now-is-expired", Run = PurgeEqualNowIsExpired },

        // runner
        new() { Id = "runner.list-by-runner", Run = RunnerListByRunner },
        new() { Id = "runner.list-by-runner-respects-max", Run = RunnerListByRunnerRespectsMax },
        new() { Id = "runner.list-by-runner-across-tenants-and-owners", Run = RunnerAcrossTenantsAndOwners },
        new() { Id = "runner.cancel-requests", Run = RunnerCancelRequests },

        // released
        new() { Id = "released.only-null-runner-submitted-or-working", Run = ReleasedOnlyNullRunner },
        new() { Id = "released.older-than-cutoff-exclusive", Run = ReleasedCutoffExclusive },
        new() { Id = "released.oldest-first", Run = ReleasedOldestFirst },
        new() { Id = "released.across-owners-and-tenants", Run = ReleasedAcrossOwnersAndTenants },
        new() { Id = "released.respects-limit", Run = ReleasedRespectsLimit },

        // caller
        new() { Id = "caller.round-trips", Run = CallerRoundTrips },
        new() { Id = "caller.has-no-token-field", Run = CallerHasNoTokenField },

        // outbox atomicity on the task store
        new() { Id = "outbox.create-writes-rows", Run = OutboxCreateWritesRows },
        new() { Id = "outbox.update-writes-rows", Run = OutboxUpdateWritesRows },
        new() { Id = "outbox.conflict-writes-no-rows", Run = OutboxConflictWritesNoRows },
        new()
        {
            Id = "outbox.atomic-with-task",
            Run = OutboxAtomicWithTask,
            SkipReason = c => c.FaultInjection ? null : "The store cannot inject a failure between the task part and the outbox part (FaultInjection is false).",
        },

        // large values
        new() { Id = "large.one-mb-artifact", Run = LargeOneMbArtifact },
        new() { Id = "large.thousand-history-messages", Run = LargeThousandHistory },

        // sequence
        new() { Id = "sequence.round-trips", Run = SequenceRoundTrips },
    ];

    // ---- helpers ----

    private static TaskQuery Q(string tenant, string agent, string owner, int pageSize = 50) =>
        new() { Scope = new OwnerScope(tenant, agent, owner), PageSize = pageSize };

    private static IReadOnlyList<string> Ids(TaskPage page) => page.Tasks.Select(t => t.Id).ToList();

    private static IReadOnlyList<string> Ids(IEnumerable<StoredTask> tasks) => tasks.Select(t => t.Key.TaskId).ToList();

    private static async Task<IReadOnlyList<string>> ListIds(StoreSet s, TaskQuery q, CancellationToken ct) =>
        Ids(await s.Tasks.ListAsync(q, ct).ConfigureAwait(false));

    private static IReadOnlyList<string> Sorted(IEnumerable<string> ids) => ids.OrderBy(i => i, StringComparer.Ordinal).ToList();

    private static async Task<IReadOnlyList<StoredTask>> CreateMany(StoreSet s, CancellationToken ct, params StoredTask[] tasks)
    {
        var created = new List<StoredTask>(tasks.Length);
        foreach (var t in tasks) created.Add(await CreateAsync(s, t, ct).ConfigureAwait(false));
        return created;
    }

    // ---- crud ----

    private static async Task CreateThenGet(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s, tenant: "t1", owner: "alice");
        var created = await s.Tasks.CreateAsync(new TaskWrite { Task = task }, ct);
        ConformanceAssert.True(created.Created, "CreateAsync must return Created = true for a new key.");
        ConformanceAssert.NotNull(created.Task.Version, "CreateAsync must return a Version.");
        var read = await s.Tasks.GetAsync(task.Key, task.Owner, ct);
        ConformanceAssert.TaskEquivalent(created.Task, read);
    }

    private static async Task GetMissingReturnsNull(StoreSet s, CancellationToken ct)
    {
        var read = await s.Tasks.GetAsync(new TaskKey("t1", "agent", "no-such-task"), "alice", ct);
        ConformanceAssert.Null(read, "GetAsync of an unknown key must return null.");
    }

    private static async Task CreateExistingKeyFails(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s);
        var first = await CreateAsync(s, task, ct);
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.CreateAsync(new TaskWrite { Task = task }, ct),
            StoreErrorKind.AlreadyExists, "Creating an existing key (same owner, no OriginMessageId) must fail.");
        ConformanceAssert.TaskEquivalent(first, await s.Tasks.GetAsync(task.Key, task.Owner, ct));
    }

    private static async Task CreateExistingKeyOtherOwnerFails(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s, owner: "alice");
        var first = await CreateAsync(s, task, ct);
        var other = task with { Owner = "bob" };
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.CreateAsync(new TaskWrite { Task = other }, ct),
            StoreErrorKind.AlreadyExists, "Creating an existing key under another owner must fail.");
        ConformanceAssert.TaskEquivalent(first, await s.Tasks.GetAsync(task.Key, null, ct));
    }

    private static async Task AllFieldsRoundTrip(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        var baseTask = NewTask(s, tenant: "t1", owner: "tid:T/user:alice", state: TaskState.AuthRequired, history: 3, artifacts: 2, originMessageId: "origin-1");
        var task = baseTask with
        {
            Task = baseTask.Task with
            {
                Metadata = Json("""{"counter":7,"nested":{"k":"v"},"list":[1,2,3]}"""),
                Status = baseTask.Task.Status with { Message = NewMessage("status-msg", "please sign in", Role.Agent) },
            },
            Sequence = 9,
            Runner = NewRunner("instance-1", now.AddSeconds(-5)),
            CancelRequestedAt = now.AddSeconds(-1),
            RunAttempt = 2,
            ContinuationGrant = new TaskContinuationGrant
            {
                AllowedOwners = [OwnerKey.ForUser("tenant-x", "user-1"), OwnerKey.ForApp("tenant-x", "app-1")],
                ExpiresAt = now.AddHours(1),
            },
            LastRunnerInstanceId = "instance-0",
            Caller = new StoredCaller
            {
                Owner = "tid:T/user:alice",
                EntraTenantId = "T",
                AppId = "app-1",
                UserObjectId = "alice",
                Roles = ["reader", "writer"],
                Scopes = ["a2a.read", "a2a.write"],
            },
            ExpiresAt = now.AddDays(30),
        };

        var created = await CreateAsync(s, task, ct);
        ConformanceAssert.TaskEquivalent(task, created, ignoreVersion: true);
        ConformanceAssert.TaskEquivalent(created, await s.Tasks.GetAsync(task.Key, task.Owner, ct));
    }

    private static async Task UpdateReturnsNewVersion(StoreSet s, CancellationToken ct)
    {
        var v1 = await CreateAsync(s, NewTask(s), ct);
        s.Time.Advance(TimeSpan.FromSeconds(1));
        var next = WithState(v1, TaskState.Working, Now(s)) with { UpdatedAt = Now(s) };
        var v2 = await UpdateAsync(s, v1, next, ct);
        ConformanceAssert.NotNull(v2.Version, "UpdateAsync must return a Version.");
        ConformanceAssert.True(v2.Version != v1.Version, "UpdateAsync must return a Version that differs from the previous one.");
        ConformanceAssert.TaskEquivalent(next, v2, ignoreVersion: true);
        ConformanceAssert.TaskEquivalent(v2, await s.Tasks.GetAsync(v1.Key, v1.Owner, ct));
    }

    private static async Task UpdateMissingFailsNotFound(StoreSet s, CancellationToken ct)
    {
        // Use a real version the store issued, so the failure can only be "the task is gone", never "the version is malformed".
        var created = await CreateAsync(s, NewTask(s), ct);
        ConformanceAssert.True(await s.Tasks.DeleteAsync(created.Key, ct), "Setup: delete must succeed.");
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.UpdateAsync(new TaskWrite { Task = created }, created.Version!, ct),
            StoreErrorKind.NotFound, "Updating a deleted task (with the version it last had) must fail with NotFound.");
        ConformanceAssert.Null(await s.Tasks.GetAsync(created.Key, created.Owner, ct), "A failed update must not recreate the task.");
    }

    private static async Task DeleteReturnsTrueThenFalse(StoreSet s, CancellationToken ct)
    {
        var task = await CreateAsync(s, NewTask(s), ct);
        ConformanceAssert.True(await s.Tasks.DeleteAsync(task.Key, ct), "DeleteAsync of an existing task must return true.");
        ConformanceAssert.Null(await s.Tasks.GetAsync(task.Key, task.Owner, ct), "A deleted task must not be returned.");
        ConformanceAssert.False(await s.Tasks.DeleteAsync(task.Key, ct), "DeleteAsync of a missing task must return false.");
    }

    private static async Task GetWithWrongOwnerHintStillFinds(StoreSet s, CancellationToken ct)
    {
        // §7.3: "A store must still find the task when the hint is wrong or null."
        var task = await CreateAsync(s, NewTask(s, owner: "alice"), ct);
        ConformanceAssert.TaskEquivalent(task, await s.Tasks.GetAsync(task.Key, "someone-else", ct));
        ConformanceAssert.TaskEquivalent(task, await s.Tasks.GetAsync(task.Key, null, ct));
    }

    private static async Task AgentIsPartOfKey(StoreSet s, CancellationToken ct)
    {
        var a = NewTask(s, agent: "agent-a", taskId: "same-id", contextId: "ctx-a");
        var b = NewTask(s, agent: "agent-b", taskId: "same-id", contextId: "ctx-b");
        var createdA = await CreateAsync(s, a, ct);
        var createdB = await CreateAsync(s, b, ct);
        ConformanceAssert.TaskEquivalent(createdA, await s.Tasks.GetAsync(a.Key, a.Owner, ct));
        ConformanceAssert.TaskEquivalent(createdB, await s.Tasks.GetAsync(b.Key, b.Owner, ct));
        ConformanceAssert.True(await s.Tasks.DeleteAsync(a.Key, ct), "Deleting agent-a's task must succeed.");
        ConformanceAssert.NotNull(await s.Tasks.GetAsync(b.Key, b.Owner, ct), "Deleting agent-a's task must not delete agent-b's.");
    }

    private static async Task IdsAreOrdinal(StoreSet s, CancellationToken ct)
    {
        // Ids, owners and tenants are compared ordinally: values that differ only by case are different keys and different scopes.
        var upper = await CreateAsync(s, NewTask(s, tenant: "T1", owner: "Alice", taskId: "Task", contextId: "ctx-upper"), ct);
        var lower = await CreateAsync(s, NewTask(s, tenant: "t1", owner: "alice", taskId: "task", contextId: "ctx-lower"), ct);
        ConformanceAssert.TaskEquivalent(upper, await s.Tasks.GetAsync(upper.Key, upper.Owner, ct));
        ConformanceAssert.TaskEquivalent(lower, await s.Tasks.GetAsync(lower.Key, lower.Owner, ct));

        var upperPage = await s.Tasks.ListAsync(Q("T1", "agent", "Alice"), ct);
        ConformanceAssert.SequenceEqual(new[] { "Task" }, Ids(upperPage), "Listing tenant T1 / owner Alice must return only 'Task'.");
        ConformanceAssert.Equal(1, upperPage.TotalSize, "TotalSize for T1 / Alice.");
        var lowerPage = await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct);
        ConformanceAssert.SequenceEqual(new[] { "task" }, Ids(lowerPage), "Listing tenant t1 / owner alice must return only 'task'.");
        ConformanceAssert.Equal(1, lowerPage.TotalSize, "TotalSize for t1 / alice.");
        ConformanceAssert.Equal(0, (await s.Tasks.ListAsync(Q("T1", "agent", "alice"), ct)).Tasks.Count, "Owner 'alice' must not match owner 'Alice'.");
        ConformanceAssert.Equal(0, (await s.Tasks.ListAsync(Q("t1", "agent", "Alice"), ct)).Tasks.Count, "Tenant 't1' must not match 'T1' for owner 'Alice'.");

        // Ids that differ only by case under one tenant and owner are two tasks too.
        var t1 = await CreateAsync(s, NewTask(s, tenant: "t2", owner: "bob", taskId: "T1", contextId: "ctx-a"), ct);
        var t1Lower = await CreateAsync(s, NewTask(s, tenant: "t2", owner: "bob", taskId: "t1", contextId: "ctx-b"), ct);
        ConformanceAssert.TaskEquivalent(t1, await s.Tasks.GetAsync(t1.Key, t1.Owner, ct));
        ConformanceAssert.TaskEquivalent(t1Lower, await s.Tasks.GetAsync(t1Lower.Key, t1Lower.Owner, ct));
        ConformanceAssert.SequenceEqual(new[] { "T1", "t1" }, Sorted(await ListIds(s, Q("t2", "agent", "bob"), ct)), "Task ids 'T1' and 't1' must both be listed.");
        ConformanceAssert.True(await s.Tasks.DeleteAsync(t1.Key, ct), "Deleting 'T1' must succeed.");
        ConformanceAssert.NotNull(await s.Tasks.GetAsync(t1Lower.Key, t1Lower.Owner, ct), "Deleting 'T1' must not delete 't1'.");
    }

    // ---- tenant ----

    private static async Task TenantGetOtherNull(StoreSet s, CancellationToken ct)
    {
        var a = await CreateAsync(s, NewTask(s, tenant: "A"), ct);
        var read = await s.Tasks.GetAsync(a.Key with { Tenant = "B" }, a.Owner, ct);
        ConformanceAssert.Null(read, "Tenant B must not read tenant A's task by id.");
    }

    private static async Task TenantUpdateOtherNotFound(StoreSet s, CancellationToken ct)
    {
        var a = await CreateAsync(s, NewTask(s, tenant: "A"), ct);
        var asB = a with { Key = a.Key with { Tenant = "B" } };
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.UpdateAsync(new TaskWrite { Task = asB }, a.Version!, ct),
            StoreErrorKind.NotFound, "Tenant B must not update tenant A's task (even with A's version).");
        ConformanceAssert.TaskEquivalent(a, await s.Tasks.GetAsync(a.Key, a.Owner, ct));
    }

    private static async Task TenantDeleteOtherFalse(StoreSet s, CancellationToken ct)
    {
        var a = await CreateAsync(s, NewTask(s, tenant: "A"), ct);
        ConformanceAssert.False(await s.Tasks.DeleteAsync(a.Key with { Tenant = "B" }, ct), "Tenant B must not delete tenant A's task.");
        ConformanceAssert.TaskEquivalent(a, await s.Tasks.GetAsync(a.Key, a.Owner, ct));
    }

    private static async Task TenantListOtherEmpty(StoreSet s, CancellationToken ct)
    {
        var a = await CreateAsync(s, NewTask(s, tenant: "A", owner: "alice"), ct);
        var page = await s.Tasks.ListAsync(Q("B", a.Key.Agent, "alice"), ct);
        ConformanceAssert.Equal(0, page.Tasks.Count, "Tenant B must list no tasks of tenant A.");
        ConformanceAssert.Equal(0, page.TotalSize, "TotalSize must be 0 for another tenant.");
    }

    private static async Task TenantEmptyIsOwnPartition(StoreSet s, CancellationToken ct)
    {
        var empty = await CreateAsync(s, NewTask(s, tenant: "", taskId: "same-id", contextId: "ctx-empty"), ct);
        var x = await CreateAsync(s, NewTask(s, tenant: "x", taskId: "same-id", contextId: "ctx-x"), ct);
        ConformanceAssert.TaskEquivalent(empty, await s.Tasks.GetAsync(empty.Key, empty.Owner, ct));
        ConformanceAssert.TaskEquivalent(x, await s.Tasks.GetAsync(x.Key, x.Owner, ct));
        ConformanceAssert.True(await s.Tasks.DeleteAsync(empty.Key, ct), "Deleting the empty-tenant task must succeed.");
        ConformanceAssert.NotNull(await s.Tasks.GetAsync(x.Key, x.Owner, ct), "Deleting the empty-tenant task must not delete tenant x's task.");
    }

    // ---- list ----

    private static async Task ListOwnerScoped(StoreSet s, CancellationToken ct)
    {
        await CreateMany(s, ct,
            NewTask(s, owner: "alice", taskId: "a1"), NewTask(s, owner: "alice", taskId: "a2"), NewTask(s, owner: "alice", taskId: "a3"),
            NewTask(s, owner: "bob", taskId: "b1"), NewTask(s, owner: "bob", taskId: "b2"));
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct);
        ConformanceAssert.SequenceEqual(new[] { "a1", "a2", "a3" }, Sorted(Ids(page)), "Alice must list exactly her own tasks.");
        ConformanceAssert.Equal(3, page.TotalSize, "TotalSize must count alice's tasks only.");
    }

    private static async Task ListOrder(StoreSet s, CancellationToken ct)
    {
        // §7.4: Task.Status.Timestamp descending, then TaskId descending (ordinal).
        var t = Now(s);
        await CreateMany(s, ct,
            NewTask(s, taskId: "a", timestamp: t.AddSeconds(1)),
            NewTask(s, taskId: "newest", timestamp: t.AddSeconds(2)),
            NewTask(s, taskId: "b", timestamp: t.AddSeconds(1)));
        ConformanceAssert.SequenceEqual(new[] { "newest", "b", "a" }, await ListIds(s, Q("t1", "agent", "alice"), ct), "Order must be timestamp descending, then id descending.");
    }

    private static async Task<(List<string> Ids, List<TaskPage> Pages)> Walk(StoreSet s, TaskQuery first, CancellationToken ct)
    {
        var ids = new List<string>();
        var pages = new List<TaskPage>();
        var q = first;
        for (var guard = 0; guard < 50; guard++)
        {
            var page = await s.Tasks.ListAsync(q, ct);
            pages.Add(page);
            ids.AddRange(Ids(page));
            if (page.Next is not { } next) return (ids, pages);
            q = q with { After = next };
        }

        throw new ConformanceFailedException("Paging did not end after 50 pages.");
    }

    private static async Task<List<StoredTask>> CreateFiveForPaging(StoreSet s, CancellationToken ct)
    {
        var t = Now(s);
        // Two share a timestamp so the keyset cursor must break ties by id.
        var made = await CreateMany(s, ct,
            NewTask(s, taskId: "p1", timestamp: t),
            NewTask(s, taskId: "p2", timestamp: t.AddSeconds(1)),
            NewTask(s, taskId: "p3", timestamp: t.AddSeconds(1)),
            NewTask(s, taskId: "p4", timestamp: t.AddSeconds(2)),
            NewTask(s, taskId: "p5", timestamp: t.AddSeconds(3)));
        return made.ToList();
    }

    private static async Task ListCursorExclusive(StoreSet s, CancellationToken ct)
    {
        var made = await CreateFiveForPaging(s, ct);
        var expected = made
            .OrderByDescending(m => m.Task.Status.Timestamp!.Value)
            .ThenByDescending(m => m.Key.TaskId, StringComparer.Ordinal)
            .Select(m => m.Key.TaskId).ToList();
        var (ids, pages) = await Walk(s, Q("t1", "agent", "alice", pageSize: 2), ct);
        ConformanceAssert.SequenceEqual(new[] { 2, 2, 1 }, pages.Select(p => p.Tasks.Count), "Page sizes over 5 tasks with page size 2.");
        ConformanceAssert.SequenceEqual(expected, ids, "Paging must return every task once, in order, none twice and none missing.");
        ConformanceAssert.Null(pages[^1].Next, "The last page must have Next = null.");
    }

    private static async Task ListTotalSizeIgnoresCursor(StoreSet s, CancellationToken ct)
    {
        await CreateFiveForPaging(s, ct);
        var first = await s.Tasks.ListAsync(Q("t1", "agent", "alice", pageSize: 2), ct);
        ConformanceAssert.True(first.Next is not null, "The first page of 5 with size 2 must have a Next cursor.");
        var second = await s.Tasks.ListAsync(Q("t1", "agent", "alice", pageSize: 2) with { After = first.Next }, ct);
        ConformanceAssert.Equal(5, first.TotalSize, "TotalSize on the first page.");
        ConformanceAssert.Equal(5, second.TotalSize, "TotalSize must ignore the cursor.");
    }

    private static async Task ListExactlyPageSizeHasNoNext(StoreSet s, CancellationToken ct)
    {
        await CreateMany(s, ct, NewTask(s, taskId: "e1"), NewTask(s, taskId: "e2"));
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice", pageSize: 2), ct);
        ConformanceAssert.Equal(2, page.Tasks.Count, "Both tasks fit on the page.");
        ConformanceAssert.Null(page.Next, "Next must be null when no task follows the page.");
    }

    private static async Task ListFilterContext(StoreSet s, CancellationToken ct)
    {
        await CreateMany(s, ct,
            NewTask(s, taskId: "c1-a", contextId: "c1"), NewTask(s, taskId: "c1-b", contextId: "c1"),
            NewTask(s, taskId: "c2-a", contextId: "c2"));
        var q = Q("t1", "agent", "alice") with { ContextId = "c1" };
        ConformanceAssert.SequenceEqual(new[] { "c1-a", "c1-b" }, Sorted(await ListIds(s, q, ct)), "Only the matching context must be listed.");
    }

    private static async Task ListFilterContextNotOtherOwner(StoreSet s, CancellationToken ct)
    {
        // Spec 03 D9 / plan 11: one owner cannot see another owner's tasks by using the same context id.
        await CreateMany(s, ct,
            NewTask(s, owner: "alice", taskId: "alice-1", contextId: "c1"),
            NewTask(s, owner: "bob", taskId: "bob-1", contextId: "c1"));
        var q = Q("t1", "agent", "alice") with { ContextId = "c1" };
        ConformanceAssert.SequenceEqual(new[] { "alice-1" }, await ListIds(s, q, ct), "A context id must never reach another owner's tasks.");
    }

    private static async Task ListFilterState(StoreSet s, CancellationToken ct)
    {
        await CreateMany(s, ct,
            NewTask(s, taskId: "w1", state: TaskState.Working), NewTask(s, taskId: "w2", state: TaskState.Working),
            NewTask(s, taskId: "done", state: TaskState.Completed));
        var q = Q("t1", "agent", "alice") with { State = TaskState.Working };
        ConformanceAssert.SequenceEqual(new[] { "w1", "w2" }, Sorted(await ListIds(s, q, ct)), "Only the requested state must be listed.");
    }

    private static async Task ListTimestampFromInclusive(StoreSet s, CancellationToken ct)
    {
        // §7.4: StatusTimestampFrom is inclusive (>=), a2a.proto:695-697.
        var t = Now(s);
        await CreateAsync(s, NewTask(s, taskId: "at-t", timestamp: t), ct);
        var atT = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { StatusTimestampFrom = t }, ct);
        ConformanceAssert.SequenceEqual(new[] { "at-t" }, Ids(atT), "A task whose timestamp equals StatusTimestampFrom must be included.");
        var after = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { StatusTimestampFrom = t + Step }, ct);
        ConformanceAssert.Equal(0, after.Tasks.Count, "A task 1 ms before StatusTimestampFrom must be excluded.");
        ConformanceAssert.Equal(0, after.TotalSize, "TotalSize must follow the same inclusive rule.");
    }

    private static async Task ListFiltersCombine(StoreSet s, CancellationToken ct)
    {
        var t = Now(s);
        await CreateMany(s, ct,
            NewTask(s, taskId: "hit", contextId: "c1", state: TaskState.Working, timestamp: t.AddSeconds(10)),
            NewTask(s, taskId: "too-old", contextId: "c1", state: TaskState.Working, timestamp: t.AddSeconds(-10)),
            NewTask(s, taskId: "wrong-state", contextId: "c1", state: TaskState.Completed, timestamp: t.AddSeconds(10)),
            NewTask(s, taskId: "wrong-context", contextId: "c2", state: TaskState.Working, timestamp: t.AddSeconds(10)));
        var q = Q("t1", "agent", "alice") with { ContextId = "c1", State = TaskState.Working, StatusTimestampFrom = t };
        var page = await s.Tasks.ListAsync(q, ct);
        ConformanceAssert.SequenceEqual(new[] { "hit" }, Ids(page), "Filters must combine as an intersection.");
        ConformanceAssert.Equal(1, page.TotalSize, "TotalSize must count the intersection.");
    }

    private static async Task ListEmpty(StoreSet s, CancellationToken ct)
    {
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct);
        ConformanceAssert.Equal(0, page.Tasks.Count, "An empty store lists no tasks.");
        ConformanceAssert.Null(page.Next, "Next must be null.");
        ConformanceAssert.Equal(0, page.TotalSize, "TotalSize must be 0.");
    }

    // ---- shape ----

    private static async Task<StoredTask> CreateShaped(StoreSet s, CancellationToken ct) =>
        await CreateAsync(s, NewTask(s, taskId: "shaped", history: 5, artifacts: 2), ct);

    private static async Task ShapeHistoryNull(StoreSet s, CancellationToken ct)
    {
        var task = await CreateShaped(s, ct);
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct);
        ConformanceAssert.Equal(1, page.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.SequenceEqual(task.Task.History.Select(m => m.MessageId), page.Tasks[0].History.Select(m => m.MessageId), "HistoryLength null must keep the whole history.");
    }

    private static async Task ShapeHistoryZero(StoreSet s, CancellationToken ct)
    {
        await CreateShaped(s, ct);
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { HistoryLength = 0 }, ct);
        ConformanceAssert.Equal(1, page.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.Equal(0, page.Tasks[0].History.Count, "HistoryLength 0 must return no history.");
    }

    private static async Task ShapeHistoryTrims(StoreSet s, CancellationToken ct)
    {
        var task = await CreateShaped(s, ct);
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { HistoryLength = 2 }, ct);
        ConformanceAssert.Equal(1, page.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.SequenceEqual(task.Task.History.TakeLast(2).Select(m => m.MessageId), page.Tasks[0].History.Select(m => m.MessageId), "HistoryLength 2 must keep the last 2 messages in order.");
    }

    private static async Task ShapeArtifactsOmitted(StoreSet s, CancellationToken ct)
    {
        var task = await CreateShaped(s, ct);
        var without = await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct);
        ConformanceAssert.Equal(1, without.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.Equal(0, without.Tasks[0].Artifacts.Count, "Artifacts must be empty unless IncludeArtifacts is set.");
        var with = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { IncludeArtifacts = true }, ct);
        ConformanceAssert.Equal(1, with.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.SequenceEqual(task.Task.Artifacts.Select(a => a.ArtifactId), with.Tasks[0].Artifacts.Select(a => a.ArtifactId), "IncludeArtifacts must return all artifacts.");
    }

    private static async Task ShapeGetReturnsFull(StoreSet s, CancellationToken ct)
    {
        var task = await CreateShaped(s, ct);
        _ = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { HistoryLength = 1 }, ct);
        var read = await s.Tasks.GetAsync(task.Key, task.Owner, ct);
        ConformanceAssert.TaskEquivalent(task, read);
        ConformanceAssert.Equal(5, read!.Task.History.Count, "GetAsync must return the full history.");
        ConformanceAssert.Equal(2, read.Task.Artifacts.Count, "GetAsync must return all artifacts.");
    }

    // ---- concurrency ----

    private static async Task StaleVersionConflict(StoreSet s, CancellationToken ct)
    {
        var v1 = await CreateAsync(s, NewTask(s), ct);
        s.Time.Advance(TimeSpan.FromSeconds(1));
        var v2 = await UpdateAsync(s, v1, WithState(v1, TaskState.Working, Now(s)), ct);
        var stale = WithState(v1, TaskState.Completed, Now(s));
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.UpdateAsync(new TaskWrite { Task = stale }, v1.Version!, ct),
            StoreErrorKind.Conflict, "Updating with a stale version must fail with Conflict.");
        var read = await s.Tasks.GetAsync(v1.Key, v1.Owner, ct);
        ConformanceAssert.TaskEquivalent(v2, read);
    }

    private static JsonElement CounterJson(int a, int b)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("a", a);
            w.WriteNumber("b", b);
            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }

    private static async Task RacingWriters(StoreSet s, CancellationToken ct)
    {
        // Review focus 1 / Goal 2: two writers, 1,000 increments each; every loser gets Conflict; no lost update.
        const int Rounds = 1000;
        var initial = NewTask(s, taskId: "race");
        initial = initial with { Task = initial.Task with { Metadata = CounterJson(0, 0) } };
        var created = await CreateAsync(s, initial, ct);
        var key = created.Key;

        async Task Writer(string field)
        {
            for (var i = 0; i < Rounds; i++)
            {
                while (true)
                {
                    var current = await s.Tasks.GetAsync(key, created.Owner, ct).ConfigureAwait(false)
                        ?? throw new ConformanceFailedException("The racing task disappeared.");
                    var meta = current.Task.Metadata!.Value;
                    var a = meta.GetProperty("a").GetInt32();
                    var b = meta.GetProperty("b").GetInt32();
                    var updated = current with { Task = current.Task with { Metadata = CounterJson(field == "a" ? a + 1 : a, field == "a" ? b : b + 1) } };
                    try
                    {
                        await s.Tasks.UpdateAsync(new TaskWrite { Task = updated }, current.Version!, ct).ConfigureAwait(false);
                        break;
                    }
                    catch (A2AStoreException ex) when (ex.Kind == StoreErrorKind.Conflict)
                    {
                        // The loser re-reads and retries.
                    }
                }
            }
        }

        await Task.WhenAll(Task.Run(() => Writer("a"), ct), Task.Run(() => Writer("b"), ct));
        var final = await s.Tasks.GetAsync(key, created.Owner, ct);
        ConformanceAssert.NotNull(final, "The racing task must still exist.");
        var fm = final.Task.Metadata!.Value;
        ConformanceAssert.Equal(Rounds, fm.GetProperty("a").GetInt32(), "Writer A's counter must reach 1000 (no lost update).");
        ConformanceAssert.Equal(Rounds, fm.GetProperty("b").GetInt32(), "Writer B's counter must reach 1000 (no lost update).");
    }

    // ---- dedupe (§7.3 CreateAsync, §7.10; scope is (tenant, agent, owner, messageId)) ----

    private static async Task DedupeSame(StoreSet s, CancellationToken ct)
    {
        var first = await CreateAsync(s, NewTask(s, taskId: "first", originMessageId: "m1"), ct);
        var second = NewTask(s, taskId: "second", originMessageId: "m1");
        var again = await s.Tasks.CreateAsync(new TaskWrite { Task = second, Outbox = [NewOutbox(second, s, 0)] }, ct);
        ConformanceAssert.False(again.Created, "A repeat of the same (scope, messageId) must return Created = false.");
        ConformanceAssert.TaskEquivalent(first, again.Task);
        ConformanceAssert.Equal(0L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "A deduped create must write no outbox rows.");
        ConformanceAssert.Null(await s.Tasks.GetAsync(new TaskKey("t1", "agent", "second"), "alice", ct), "The deduped create must not store the second task.");
    }

    private static async Task DedupeOtherOwner(StoreSet s, CancellationToken ct)
    {
        await CreateAsync(s, NewTask(s, owner: "alice", taskId: "alice-task", originMessageId: "m1"), ct);
        var bob = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, owner: "bob", taskId: "bob-task", originMessageId: "m1") }, ct);
        ConformanceAssert.True(bob.Created, "Another owner's create with the same messageId must not be deduped.");
        ConformanceAssert.Equal("bob-task", bob.Task.Key.TaskId, "The new task must be bob's.");
    }

    private static async Task DedupeOtherTenant(StoreSet s, CancellationToken ct)
    {
        await CreateAsync(s, NewTask(s, tenant: "A", taskId: "task-a", originMessageId: "m1"), ct);
        var b = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, tenant: "B", taskId: "task-b", originMessageId: "m1") }, ct);
        ConformanceAssert.True(b.Created, "Another tenant's create with the same messageId must not be deduped.");
    }

    private static async Task DedupeOtherAgent(StoreSet s, CancellationToken ct)
    {
        await CreateAsync(s, NewTask(s, agent: "agent-a", taskId: "task-a", originMessageId: "m1"), ct);
        var b = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, agent: "agent-b", taskId: "task-b", originMessageId: "m1") }, ct);
        ConformanceAssert.True(b.Created, "Another agent's create with the same messageId must not be deduped.");
    }

    private static async Task DedupeNullNever(StoreSet s, CancellationToken ct)
    {
        var one = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, taskId: "n1") }, ct);
        var two = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, taskId: "n2") }, ct);
        ConformanceAssert.True(one.Created && two.Created, "Creates without OriginMessageId must never be deduped.");
        ConformanceAssert.Equal(2, (await s.Tasks.ListAsync(Q("t1", "agent", "alice"), ct)).TotalSize, "Both tasks must exist.");
    }

    private static async Task DedupeAfterDelete(StoreSet s, CancellationToken ct)
    {
        var first = await CreateAsync(s, NewTask(s, taskId: "first", originMessageId: "m1"), ct);
        ConformanceAssert.True(await s.Tasks.DeleteAsync(first.Key, ct), "Deleting the first task must succeed.");
        var again = await s.Tasks.CreateAsync(new TaskWrite { Task = NewTask(s, taskId: "second", originMessageId: "m1") }, ct);
        ConformanceAssert.True(again.Created, "After the first task is deleted, the same messageId must create a new task.");
        ConformanceAssert.Equal("second", again.Task.Key.TaskId, "The new task must be returned.");
    }

    // ---- delete cascade (§7.3: removes the task, its push configs and its outbox rows) ----

    private static async Task DeleteCascades(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s, taskId: "cascade");
        var created = await CreateAsync(s, task, ct, NewOutbox(task, s, 1, lane: "c1"), NewOutbox(task, s, 2, lane: "c2"));
        var tref = new TaskRef(created.Key, created.Owner);
        await s.PushConfigs.UpsertAsync(tref, NewPushConfig("c1", "cascade"), null, null, ct);
        await s.PushConfigs.UpsertAsync(tref, NewPushConfig("c2", "cascade"), null, null, ct);
        ConformanceAssert.Equal(2, await s.PushConfigs.CountAsync(tref, ct), "Setup: two push configs.");
        ConformanceAssert.Equal(2L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "Setup: two pending outbox rows.");

        ConformanceAssert.True(await s.Tasks.DeleteAsync(created.Key, ct), "DeleteAsync must return true.");
        ConformanceAssert.Equal(0, await s.PushConfigs.CountAsync(tref, ct), "Delete must remove the task's push configs.");
        ConformanceAssert.Equal(0L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "Delete must remove the task's outbox rows.");
    }

    // ---- purge (§7.3: deletes up to max tasks with ExpiresAt <= now; a no-op with native TTL) ----

    private static async Task<bool> PurgeNoOpForNativeTtl(StoreSet s, CancellationToken ct)
    {
        if (!s.Capabilities.NativeTtl) return false;
        var purged = await s.Tasks.PurgeExpiredAsync(Now(s), 100, ct);
        ConformanceAssert.Equal(0, purged, "A store with native TTL must return 0 from PurgeExpiredAsync.");
        return true;
    }

    private static async Task PurgeRemovesExpiredOnly(StoreSet s, CancellationToken ct)
    {
        if (await PurgeNoOpForNativeTtl(s, ct)) return;
        var now = Now(s);
        for (var i = 1; i <= 3; i++) await CreateAsync(s, NewTask(s, taskId: $"expired-{i}", expiresAt: now.AddMinutes(-i)), ct);
        for (var i = 1; i <= 2; i++) await CreateAsync(s, NewTask(s, taskId: $"future-{i}", expiresAt: now.AddHours(i)), ct);
        await CreateAsync(s, NewTask(s, taskId: "never"), ct);

        ConformanceAssert.Equal(3, await s.Tasks.PurgeExpiredAsync(now, 100, ct), "PurgeExpiredAsync must return the number of expired tasks removed.");
        var left = await ListIds(s, Q("t1", "agent", "alice"), ct);
        ConformanceAssert.SequenceEqual(new[] { "future-1", "future-2", "never" }, Sorted(left), "Only the unexpired tasks must remain.");
    }

    private static async Task PurgeRespectsMax(StoreSet s, CancellationToken ct)
    {
        if (await PurgeNoOpForNativeTtl(s, ct)) return;
        var now = Now(s);
        for (var i = 1; i <= 5; i++) await CreateAsync(s, NewTask(s, taskId: $"expired-{i}", expiresAt: now.AddMinutes(-i)), ct);

        var counts = new List<int>();
        for (var i = 0; i < 4; i++) counts.Add(await s.Tasks.PurgeExpiredAsync(now, 2, ct));
        ConformanceAssert.SequenceEqual(new[] { 2, 2, 1, 0 }, counts, "Successive purges with max 2 over 5 expired tasks.");
    }

    private static async Task PurgeEqualNowIsExpired(StoreSet s, CancellationToken ct)
    {
        if (await PurgeNoOpForNativeTtl(s, ct)) return;
        var now = Now(s);
        await CreateAsync(s, NewTask(s, taskId: "edge", expiresAt: now), ct);

        ConformanceAssert.Equal(1, await s.Tasks.PurgeExpiredAsync(now, 100, ct), "ExpiresAt == now is expired (§7.3: ExpiresAt <= now).");
        ConformanceAssert.Null(await s.Tasks.GetAsync(new TaskKey("t1", "agent", "edge"), "alice", ct), "The task must be gone.");
    }

    // ---- runner and cancel queries (§7.3) ----

    private static async Task RunnerListByRunner(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        await CreateMany(s, ct,
            NewTask(s, taskId: "i1-working", state: TaskState.Working, runner: NewRunner("i1", now)),
            NewTask(s, taskId: "i1-completed", state: TaskState.Completed, runner: NewRunner("i1", now)),
            NewTask(s, taskId: "i1-input", state: TaskState.InputRequired, runner: NewRunner("i1", now)),
            NewTask(s, taskId: "i2-working", state: TaskState.Working, runner: NewRunner("i2", now)),
            NewTask(s, taskId: "no-runner", state: TaskState.Working));
        var found = await s.Tasks.ListByRunnerAsync("i1", 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "i1-working" }, Ids(found), "Only non-final tasks run by i1 must be returned (terminal and interrupted excluded).");
    }

    private static async Task RunnerListByRunnerRespectsMax(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        for (var i = 1; i <= 5; i++)
            await CreateAsync(s, NewTask(s, taskId: $"r{i}", state: TaskState.Working, runner: NewRunner("i1", now)), ct);
        ConformanceAssert.Equal(3, (await s.Tasks.ListByRunnerAsync("i1", 3, ct)).Count, "ListByRunnerAsync must return at most max tasks.");
    }

    private static async Task RunnerAcrossTenantsAndOwners(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        await CreateMany(s, ct,
            NewTask(s, tenant: "t1", owner: "alice", taskId: "x1", state: TaskState.Working, runner: NewRunner("i1", now)),
            NewTask(s, tenant: "t1", owner: "bob", taskId: "x2", state: TaskState.Working, runner: NewRunner("i1", now)),
            NewTask(s, tenant: "t2", owner: "alice", taskId: "x3", state: TaskState.Working, runner: NewRunner("i1", now)),
            NewTask(s, tenant: "t2", owner: "bob", taskId: "x4", state: TaskState.Submitted, runner: NewRunner("i1", now)));
        var found = await s.Tasks.ListByRunnerAsync("i1", 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "x1", "x2", "x3", "x4" }, Sorted(Ids(found)), "The runner query spans tenants and owners.");
    }

    private static async Task RunnerCancelRequests(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        var wanted = await CreateAsync(s, NewTask(s, taskId: "cancel-me", state: TaskState.Working, runner: NewRunner("i1", now), cancelRequestedAt: now), ct);
        await CreateMany(s, ct,
            NewTask(s, taskId: "no-cancel", state: TaskState.Working, runner: NewRunner("i1", now)),
            NewTask(s, taskId: "other-runner", state: TaskState.Working, runner: NewRunner("i2", now), cancelRequestedAt: now));
        // §7.3: only non-final tasks count, so a final task that still carries CancelRequestedAt is left out.
        await CreateAsync(s, NewTask(s, taskId: "cancel-but-final", state: TaskState.Completed, runner: NewRunner("i1", now), cancelRequestedAt: now), ct);
        var keys = await s.Tasks.ListCancelRequestsAsync("i1", ct);
        ConformanceAssert.SequenceEqual(new[] { wanted.Key }, keys, "Only i1's non-final task with CancelRequestedAt must be returned.");
    }

    // ---- released tasks (§7.3, B4: Runner == null, SUBMITTED or WORKING, UpdatedAt < olderThan, oldest first) ----

    private static async Task ReleasedOnlyNullRunner(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        var old = now.AddMinutes(-10);
        await CreateMany(s, ct,
            NewTask(s, taskId: "released-submitted", state: TaskState.Submitted, updatedAt: old),
            NewTask(s, taskId: "released-working", state: TaskState.Working, updatedAt: old),
            NewTask(s, taskId: "completed", state: TaskState.Completed, updatedAt: old),
            NewTask(s, taskId: "input-required", state: TaskState.InputRequired, updatedAt: old),
            NewTask(s, taskId: "has-runner", state: TaskState.Working, runner: NewRunner("i1", old), updatedAt: old));
        var found = await s.Tasks.ListReleasedAsync(now, 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "released-submitted", "released-working" }, Sorted(Ids(found)), "Only tasks without a runner in SUBMITTED or WORKING are released.");
    }

    private static async Task ReleasedCutoffExclusive(StoreSet s, CancellationToken ct)
    {
        var cutoff = Now(s);
        await CreateMany(s, ct,
            NewTask(s, taskId: "at-cutoff", state: TaskState.Working, updatedAt: cutoff),
            NewTask(s, taskId: "just-before", state: TaskState.Working, updatedAt: cutoff - Step));
        var found = await s.Tasks.ListReleasedAsync(cutoff, 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "just-before" }, Ids(found), "UpdatedAt == olderThan is excluded; 1 ms earlier is included.");
    }

    private static async Task ReleasedOldestFirst(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        await CreateMany(s, ct,
            NewTask(s, taskId: "t3", state: TaskState.Working, updatedAt: now.AddMinutes(-1)),
            NewTask(s, taskId: "t1", state: TaskState.Working, updatedAt: now.AddMinutes(-3)),
            NewTask(s, taskId: "t2", state: TaskState.Working, updatedAt: now.AddMinutes(-2)));
        var found = await s.Tasks.ListReleasedAsync(now, 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "t1", "t2", "t3" }, Ids(found), "Released tasks must come oldest first.");
    }

    private static async Task ReleasedAcrossOwnersAndTenants(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        await CreateMany(s, ct,
            NewTask(s, tenant: "t1", owner: "alice", taskId: "y1", state: TaskState.Working, updatedAt: now.AddMinutes(-4)),
            NewTask(s, tenant: "t1", owner: "bob", taskId: "y2", state: TaskState.Working, updatedAt: now.AddMinutes(-3)),
            NewTask(s, tenant: "t2", owner: "alice", taskId: "y3", state: TaskState.Submitted, updatedAt: now.AddMinutes(-2)));
        var found = await s.Tasks.ListReleasedAsync(now, 10, ct);
        ConformanceAssert.SequenceEqual(new[] { "y1", "y2", "y3" }, Ids(found), "The released query spans tenants and owners.");
    }

    private static async Task ReleasedRespectsLimit(StoreSet s, CancellationToken ct)
    {
        var now = Now(s);
        for (var i = 1; i <= 5; i++)
            await CreateAsync(s, NewTask(s, taskId: $"z{i}", state: TaskState.Working, updatedAt: now.AddMinutes(-10 + i)), ct);
        var found = await s.Tasks.ListReleasedAsync(now, 2, ct);
        ConformanceAssert.SequenceEqual(new[] { "z1", "z2" }, Ids(found), "ListReleasedAsync must return the limit oldest tasks.");
    }

    // ---- reduced caller (B8) ----

    private static async Task CallerRoundTrips(StoreSet s, CancellationToken ct)
    {
        var caller = new StoredCaller
        {
            Owner = "tid:T/user:u1",
            EntraTenantId = "T",
            AppId = "app-9",
            UserObjectId = "u1",
            Roles = ["admin", "reader"],
            Scopes = ["a2a.read", "a2a.write"],
        };
        var created = await CreateAsync(s, NewTask(s) with { Caller = caller }, ct);
        var read = await s.Tasks.GetAsync(created.Key, created.Owner, ct);
        ConformanceAssert.NotNull(read, "The task must be readable.");
        ConformanceAssert.NotNull(read.Caller, "Caller must round-trip.");
        ConformanceAssert.Equal(caller.Owner, read.Caller.Owner, "Caller.Owner.");
        ConformanceAssert.Equal(caller.EntraTenantId, read.Caller.EntraTenantId, "Caller.EntraTenantId.");
        ConformanceAssert.Equal(caller.AppId, read.Caller.AppId, "Caller.AppId.");
        ConformanceAssert.Equal(caller.UserObjectId, read.Caller.UserObjectId, "Caller.UserObjectId.");
        ConformanceAssert.SequenceEqual(caller.Roles, read.Caller.Roles, "Caller.Roles.");
        ConformanceAssert.SequenceEqual(caller.Scopes, read.Caller.Scopes, "Caller.Scopes.");
    }

    private static Task CallerHasNoTokenField(StoreSet s, CancellationToken ct)
    {
        // B8 / §7.2: StoredCaller never holds an access token. Guards the contract against later additions.
        var banned = new[] { "Token", "Assertion", "Secret" };
        var offenders = typeof(StoredCaller).GetProperties()
            .Select(p => p.Name)
            .Where(n => banned.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        ConformanceAssert.True(offenders.Count == 0, $"StoredCaller must not have token-like properties; found: {string.Join(", ", offenders)}.");
        return Task.CompletedTask;
    }

    // ---- outbox atomicity on the task store (§7.6: rows are written only through TaskWrite.Outbox) ----

    private static async Task<IReadOnlyList<OutboxEntry>> ClaimAll(StoreSet s, CancellationToken ct) =>
        await s.Outbox.ClaimDueAsync("conformance", Now(s), TimeSpan.FromMinutes(1), 100, ct);

    private static async Task OutboxCreateWritesRows(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s);
        var r1 = NewOutbox(task, s, 1, lane: "l1");
        var r2 = NewOutbox(task, s, 1, lane: "l2");
        await CreateAsync(s, task, ct, r1, r2);
        var claimed = await ClaimAll(s, ct);
        ConformanceAssert.SequenceEqual(Sorted(new[] { r1.Id, r2.Id }), Sorted(claimed.Select(c => c.Id)), "Both rows written by CreateAsync must be claimable.");
    }

    private static async Task OutboxUpdateWritesRows(StoreSet s, CancellationToken ct)
    {
        var v1 = await CreateAsync(s, NewTask(s), ct);
        ConformanceAssert.Equal(0, (await ClaimAll(s, ct)).Count, "Setup: no rows before the update.");
        var r1 = NewOutbox(v1, s, 1, lane: "l1");
        var r2 = NewOutbox(v1, s, 1, lane: "l2");
        await UpdateAsync(s, v1, WithState(v1, TaskState.Working, Now(s)) with { Sequence = 1 }, ct, r1, r2);
        var claimed = await ClaimAll(s, ct);
        ConformanceAssert.SequenceEqual(Sorted(new[] { r1.Id, r2.Id }), Sorted(claimed.Select(c => c.Id)), "Both rows written by UpdateAsync must be claimable.");
    }

    private static async Task OutboxConflictWritesNoRows(StoreSet s, CancellationToken ct)
    {
        var task = NewTask(s);
        var v1 = await CreateAsync(s, task, ct, NewOutbox(task, s, 0));
        await UpdateAsync(s, v1, WithState(v1, TaskState.Working, Now(s)), ct);
        var stale = WithState(v1, TaskState.Completed, Now(s));
        await ConformanceAssert.StoreErrorAsync(
            async () => await s.Tasks.UpdateAsync(new TaskWrite { Task = stale, Outbox = [NewOutbox(v1, s, 2)] }, v1.Version!, ct),
            StoreErrorKind.Conflict, "A stale update must fail with Conflict.");
        ConformanceAssert.Equal(1L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "A conflicting update must write no outbox rows.");
    }

    private static async Task OutboxAtomicWithTask(StoreSet s, CancellationToken ct)
    {
        // Review focus 4 / Goal 9: a failure between the task part and the outbox part leaves neither.
        var faults = s.Faults ?? throw new ConformanceFailedException("StoreSet.Faults must be set when Capabilities.FaultInjection is true.");

        var v1 = await CreateAsync(s, NewTask(s, taskId: "atomic-update"), ct);
        faults.FailNextWrite(StoreFaultPoint.AfterTaskBeforeOutbox);
        var next = WithState(v1, TaskState.Working, Now(s));
        await ConformanceAssert.ThrowsAnyAsync(
            async () => await s.Tasks.UpdateAsync(new TaskWrite { Task = next, Outbox = [NewOutbox(v1, s, 1)] }, v1.Version!, ct),
            "An update failing after the task part must throw.");
        ConformanceAssert.TaskEquivalent(v1, await s.Tasks.GetAsync(v1.Key, v1.Owner, ct));
        ConformanceAssert.Equal(0L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "A failed update must leave no outbox row.");

        var fresh = NewTask(s, taskId: "atomic-create");
        faults.FailNextWrite(StoreFaultPoint.AfterTaskBeforeOutbox);
        await ConformanceAssert.ThrowsAnyAsync(
            async () => await s.Tasks.CreateAsync(new TaskWrite { Task = fresh, Outbox = [NewOutbox(fresh, s, 0)] }, ct),
            "A create failing after the task part must throw.");
        ConformanceAssert.Null(await s.Tasks.GetAsync(fresh.Key, fresh.Owner, ct), "A failed create must leave no task.");
        ConformanceAssert.Equal(0L, (await s.Outbox.GetPendingStatsAsync(ct)).Pending, "A failed create must leave no outbox row.");
    }

    // ---- large values ----

    private static async Task LargeOneMbArtifact(StoreSet s, CancellationToken ct)
    {
        var big = new string('x', 1024 * 1024);
        var task = NewTask(s, taskId: "big");
        task = task with { Task = task.Task with { Artifacts = [NewArtifact("big-artifact", big)] } };
        var created = await CreateAsync(s, task, ct);
        var read = await s.Tasks.GetAsync(created.Key, created.Owner, ct);
        ConformanceAssert.TaskEquivalent(created, read);
        ConformanceAssert.Equal(big.Length, read!.Task.Artifacts[0].Parts[0].Text!.Length, "The 1 MB artifact text must round-trip.");
    }

    private static async Task LargeThousandHistory(StoreSet s, CancellationToken ct)
    {
        var task = await CreateAsync(s, NewTask(s, taskId: "long", history: 1000), ct);
        var read = await s.Tasks.GetAsync(task.Key, task.Owner, ct);
        ConformanceAssert.TaskEquivalent(task, read);
        ConformanceAssert.Equal(1000, read!.Task.History.Count, "All 1,000 history messages must round-trip.");
        var page = await s.Tasks.ListAsync(Q("t1", "agent", "alice") with { HistoryLength = 10 }, ct);
        ConformanceAssert.Equal(1, page.Tasks.Count, "The list must return the one task.");
        ConformanceAssert.SequenceEqual(task.Task.History.TakeLast(10).Select(m => m.MessageId), page.Tasks[0].History.Select(m => m.MessageId), "HistoryLength 10 must return the last 10 messages.");
    }

    // ---- sequence ----

    private static async Task SequenceRoundTrips(StoreSet s, CancellationToken ct)
    {
        var created = await CreateAsync(s, NewTask(s) with { Sequence = 42 }, ct);
        ConformanceAssert.Equal(42L, created.Sequence, "The store must not change Sequence on create.");
        var updated = await UpdateAsync(s, created, created with { Sequence = 43 }, ct);
        ConformanceAssert.Equal(43L, updated.Sequence, "The store must not change Sequence on update.");
        ConformanceAssert.Equal(43L, (await s.Tasks.GetAsync(created.Key, created.Owner, ct))!.Sequence, "Sequence must round-trip.");
    }
}
