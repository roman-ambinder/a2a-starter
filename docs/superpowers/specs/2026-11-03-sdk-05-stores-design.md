# SDK 05: Store contract and durable stores

Date: 2026-11-03
Status: approved by owner 2026-11-03
Milestone: M1 (`1.0.0-preview`)
Size: L

Items marked *(writer's call)* were chosen by the spec writer, not the owner. Items marked *(Claude's call)* were decided by Claude after review.

## 1. Intent

Today the SDK keeps tasks and push configs only in memory, and the store interfaces are not fit for a real database. This package fixes the store contract before v1, because changing it later is a breaking change. It then ships three stores that pass one shared conformance suite: in-memory (the default, now bounded), Entity Framework Core (PostgreSQL first, SQLite for local work and tests, SQL Server supported) and Azure Cosmos DB.

The contract also carries what later packages need: who runs a task and when that pod was last alive (package 06 and the recovery in 09), and outbox rows written in the same transaction as the task (package 08).

## 2. Findings addressed

- **R14** No durable stores; blind `SaveAsync`; no delete, TTL or retention; `ListAsync` has no tenant; protocol validation lives in the store; no conformance suite.
- **R15** Unbounded memory: tasks never evicted, per-task semaphores never freed, push configs per task unlimited, `ListAsync` sorts all tasks per call.
- **PD6** (package 14) The in-memory store copies history and artifacts on every get and save, and sorts all tasks per list.
- **PD8** (package 14) Per-task semaphores are never freed.
- **R18** Tenant is stored but not isolated (store side only; the tenant resolver is package 07).
- **R13** (store part only) Seams for multi-instance: optimistic concurrency instead of in-process locks, runner and instance-lease fields.
- **R17** (store part only) Durable outbox rows so push delivery survives restarts.
- Pain point **"Persistent task store, multi-instance servers, push-notification delivery"** (A): ship durable stores, user-scoped queries, test fakes.
- Pain point **"Streaming (SSE) robustness"** (store part): events must never be half-applied; ordering by a per-task sequence number.

## 3. Goals

1. `ITaskStore` and `IPushConfigStore` are replaced by a contract with optimistic concurrency, tenant-scoped keys, owner-scoped paging, delete, retention and typed queries. Tested by the conformance suite.
2. A concurrent update with a stale version always fails with a typed conflict error and never overwrites. Tested by a conformance test with 2 writers racing 1,000 times.
3. A task in tenant A is never returned, listed, updated or deleted through a key or query for tenant B. Tested by the conformance suite.
4. Stores do no protocol validation. Page size, history length and page tokens are checked once, in the core, before the store is called. Tested by a core unit test that calls a store spy with invalid input and sees no store call.
5. `ListTasks` reads only one page of rows plus one count query. It never loads all tasks of an agent. Tested by a benchmark (100,000 tasks, one owner with 10,000) and, for EF, by checking the SQL plan uses the list index.
6. The in-memory store has hard bounds (task count, retention of finished tasks, push configs per task) and frees per-task locks. Tested by a soak test: 1,000,000 tasks created and finished; process memory stays under a fixed ceiling.
7. EF Core stores work on PostgreSQL, SQLite and SQL Server with shipped migrations. Tested by the conformance suite on all three in CI (PostgreSQL and SQL Server in containers).
8. The Cosmos DB store runs `ListTasks` for one owner inside one logical partition, uses ETags for concurrency and native TTL for retention. Tested by the conformance suite against the Cosmos emulator, and RU cost per `ListTasks` page is recorded in the benchmark report.
9. A task write and its outbox rows are atomic in every durable store. Tested by a fault-injection conformance test (failure between the two parts leaves neither).
10. The conformance suite is a reusable test library that a team can run against its own store in under 10 lines. Tested by the three shipped stores using it exactly as a third party would.

## 4. Non-goals

- Resolving the tenant and owner from the caller. That is package 07. This package only stores and filters by the values it is given.
- Forwarding requests between pods, and the cluster router. That is package 06.
- The recovery sweep, graceful shutdown and the restart policy of interrupted runs. That is package 09 (it uses the fields defined here).
- Push delivery, retries, signing keys and the shape of outbox payloads. That is package 08. This package stores outbox rows as opaque payloads.
- The design of push credential protection. Package 08 owns `IPushCredentialProtector` and its default (Data Protection). This package's durable push-config stores only call it (§7.7).
- Redis, MongoDB, DynamoDB or other stores. After 1.0.
- Publishing the conformance suite as a NuGet package. This package writes the cases in M1, as a project under `tests/` that the shipped stores use. In M2, package 12 takes the project over and publishes it as `Ambinder.A2A.Testing.Conformance`.
- Batching several agent events into one store write. Each event stays one write (§7.10, "Artifact chunks").
- Event replay for `SubscribeToTask` (`Last-Event-ID`). The spec defines no replay (research-spec §3 "Streaming", issue #1635).

## 5. Anti-goals

- A store contract that leaks a database type (`DbContext`, `Container`, `IQueryable`) into the core.
- A contract so wide that a team needs a week to write their own store. The core store interface must stay under about 12 members.
- Any provider that "works" but scans every task of an agent per `ListTasks` call.
- A second, different validation of page tokens or page sizes in each store. Error messages must be the same whatever store is used.
- A default in-memory store that can still grow without limit on a long-running pod.
- Hidden auto-migration of a production database at app start.
- A dependency on EF Core, Npgsql or the Cosmos SDK from the core server package.

## 6. Constraints

- Work plan decision "Storage": an abstract store contract in the core; in v1 in-memory (default), EF Core and Cosmos DB, all passing one shared conformance suite.
- Work plan decision "EF Core database": PostgreSQL first, SQLite for local dev and tests, SQL Server through the same code but tested less.
- Work plan dependency "05 → 06, 08": owner (runner) and lease fields and outbox rows are part of this contract.
- Names: package IDs and namespaces `Ambinder.A2A.*`; .NET 10 only; Apache 2.0.
- Principle 5: no hidden dependency on Azure or a database in the core.
- A2A v1.0.1, `ListTasks` (research-spec §3 "ListTasks", proto `a2a.proto:676-713`):
  - only tasks visible to the caller; scoping applies before any lookup that could leak existence
  - sort by status timestamp, newest first
  - `nextPageToken` always present, `""` on the last page
  - `pageSize` 1–100, default 50
  - `statusTimestampAfter` means "greater than or equal" (`a2a.proto:695-697`)
  - artifacts omitted unless `includeArtifacts` is true
  - `totalSize` and `pageSize` are REQUIRED in the response (`a2a.proto:710-712`)
- A2A v1.0.1: "MUST NOT reveal existence of unauthorized resources" (research-spec §3 "Errors"). Another owner's or tenant's task is "not found".
- A2A v1.0.1: delete of a push config MUST be idempotent; cancel of a purged task MAY return TaskNotFound (research-spec §3 "Tasks/contexts").
- `ListTaskPushNotificationConfigsResponse.next_page_token` is empty when there are no more results (`a2a.proto:807-812`).
- Package 14 budgets apply once set; this package provides the baseline numbers.
- No cloud resources for now (owner decision O3). Tests against a real Cosmos account are written but run only when their secrets exist, and are skipped visibly otherwise. Until then the Cosmos emulator, plus the manual pre-release checklist, meets the exit criteria.
- Test harness (Claude's call, B2): in M1 this package uses the repo-internal slice of package 12's harness (`StoreSet`, `FakeTimeProvider` wiring, `TaskEventLog` waiters). No new M1 test uses fixed ports or sleeps.

## 7. Design

### 7.1 Assemblies

| Package / assembly | Contents | Depends on |
|---|---|---|
| `Ambinder.A2A.Server` (core, no web host; package map in 01) | Contract types (namespace `Ambinder.A2A.Server.Stores`, package 02's namespace rule), in-memory stores, store-facing core logic (validation, page tokens, retention service) | no database package |
| `Ambinder.A2A.Stores.EntityFrameworkCore` | Provider-neutral EF Core model and stores | EF Core 10 (`Microsoft.EntityFrameworkCore.Relational`) |
| `Ambinder.A2A.Stores.EntityFrameworkCore.PostgreSql` | `UseA2APostgreSql()`, migrations | Npgsql EF Core provider 10.x |
| `Ambinder.A2A.Stores.EntityFrameworkCore.Sqlite` | `UseA2ASqlite()`, migrations | `Microsoft.EntityFrameworkCore.Sqlite` 10.x |
| `Ambinder.A2A.Stores.EntityFrameworkCore.SqlServer` | `UseA2ASqlServer()`, migrations | `Microsoft.EntityFrameworkCore.SqlServer` 10.x |
| `Ambinder.A2A.Stores.Cosmos` | Cosmos stores | `Microsoft.Azure.Cosmos` 3.x |
| Conformance suite: M1 a project under `tests/` (this package); M2 published by 12 as `Ambinder.A2A.Testing.Conformance` | `TaskStoreConformanceCases` and the other case catalogs, plus `TaskStoreConformanceTests` and the other xUnit classes | xUnit v3 (the repo's test framework, `Directory.Packages.props`) |

The split into three EF provider packages keeps teams from pulling three database drivers *(writer's call)*. EF migrations are provider-specific, so each provider package carries its own. These are the "provider helpers" that package 01's map lists under `Ambinder.A2A.Stores.EntityFrameworkCore`.

Rules from package 02 that shape every interface here:

- `Add…` registration methods (`AddEntityFrameworkStores`, `AddCosmosStores`) live in namespace `Microsoft.Extensions.DependencyInjection`.
- After 1.0, a new member on a public interface must have a default implementation. So:
  - Optional abilities live on separate small interfaces: `IStoreHealthProbe`, `IStoreFaultInjector`, `StoreCapabilities`. They are not members of `ITaskStore`.
  - Every contract method takes a record or record struct (`TaskWrite`, `TaskQuery`) rather than a long parameter list, so new inputs are new init properties, not new overloads.
  - A member added later gets a default body that throws `NotSupportedException` with "This store does not support X; upgrade the store package". The conformance suite then skips the related cases by capability.

### 7.2 Keys and records

```csharp
namespace Ambinder.A2A.Server.Stores;

// Tenant is "" when the deployment is single-tenant. Never null.
public readonly record struct TaskKey(string Tenant, string Agent, string TaskId);

// What a caller-scoped query is limited to.
public readonly record struct OwnerScope(string Tenant, string Agent, string Owner);

public sealed record StoredTask
{
    public required TaskKey Key { get; init; }
    public required string Owner { get; init; }          // from 07's owner resolver
    public required AgentTask Task { get; init; }        // Task.Status.Timestamp is always set by the core
    public string? Version { get; init; }                // opaque ETag; null only before create
    public long Sequence { get; init; }                  // +1 per committed event; orders events and outbox rows
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }      // set by the core from retention options
    public TaskRunner? Runner { get; init; }             // who runs it now; null when no execution is live
    public DateTimeOffset? CancelRequestedAt { get; init; } // durable cancel signal (06, 09)
    public int RunAttempt { get; init; }                 // recovery restarts (09)
    public string? OriginMessageId { get; init; }        // messageId that created the task (dedupe)
    public TaskContinuationGrant? ContinuationGrant { get; init; } // package 07; set at AUTH_REQUIRED, cleared on leaving it
    public string? LastRunnerInstanceId { get; init; }   // kept when Runner is cleared; continuation affinity (06)
    public StoredCaller? Caller { get; init; }           // reduced caller for runs on other pods and recovery (07, B8)
}

public sealed record TaskRunner(string InstanceId, DateTimeOffset ClaimedAt);

// Reduced caller, persisted with the task (B8). Never holds an access token.
public sealed record StoredCaller
{
    public required string Owner { get; init; }          // OwnerKey string (07)
    public string? EntraTenantId { get; init; }
    public string? AppId { get; init; }
    public string? UserObjectId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Scopes { get; init; } = [];
}
```

- The task id is still a server GUID. The key is `(Tenant, Agent, TaskId)`. Tenant is part of every key, so tenant isolation is structural (R18).
- `Owner` is not part of `TaskKey`, because an identity named by the agent may continue an `AUTH_REQUIRED` task (work plan decision "Task owner"). The core checks the owner after a read, as today (`TaskManager.cs:407-414`).
- `Owner` is the string form of package 07's `OwnerKey` (`OwnerKey.ForApp(tenantId, appId)`, `OwnerKey.ForUser(tenantId, userObjectId)`). The string format is part of the public contract and frozen at 1.0, because it is stored in every row *(Claude's call, B7)*.
- `Caller` is the reduced caller *(Claude's call, B8)*: owner key, Entra tenant id, app id, user object id, roles and scopes. The core writes it at create. Runs on another pod (06) and recovered runs (09) rebuild `A2ACaller` from it. It never holds an access token, so on-behalf-of works only on the pod that received the request (07).
- `ContinuationGrant` is package 07's `TaskContinuationGrant` (allowed owner keys, expiry). The store saves it with the task and returns it unchanged. The core sets and clears it; package 07's `ITaskContinuationPolicy` reads it.
- `Version` is opaque to the core. EF uses a 64-bit counter written as a string; Cosmos uses `_etag`.
- "Runner" is the pod running the agent now. The word "owner" stays reserved for the caller who owns the task *(writer's call: avoids two meanings of "owner")*.
- Liveness is not stored per task. It is stored once per instance (§7.5). A task is orphaned when its runner's instance lease has expired *(writer's call: one heartbeat write per pod instead of one per running task; 1,000 running tasks would otherwise mean about 100 writes per second)*.

### 7.3 `ITaskStore`

```csharp
public interface ITaskStore
{
    ValueTask<StoredTask?> GetAsync(TaskKey key, string? ownerHint, CancellationToken ct);

    // Creates the task. Fails with StoreErrorKind.AlreadyExists if the key exists.
    // If write.OriginMessageId is set and the same (scope, messageId) already created a task,
    // returns that task with Created = false instead (dedupe of client retries).
    ValueTask<CreateResult> CreateAsync(TaskWrite write, CancellationToken ct);

    // Replaces the task only if its version equals expectedVersion. Returns the new record (new Version).
    // Fails with StoreErrorKind.Conflict on a version mismatch, NotFound if it was deleted.
    ValueTask<StoredTask> UpdateAsync(TaskWrite write, string expectedVersion, CancellationToken ct);

    ValueTask<TaskPage> ListAsync(TaskQuery query, CancellationToken ct);

    // Removes the task, its push configs and its outbox rows. Returns false when it did not exist.
    ValueTask<bool> DeleteAsync(TaskKey key, CancellationToken ct);

    // Non-final tasks whose Runner.InstanceId == instanceId. Used by recovery (09) and the cluster (06).
    ValueTask<IReadOnlyList<StoredTask>> ListByRunnerAsync(string instanceId, int max, CancellationToken ct);

    // Non-final tasks with CancelRequestedAt set, run by instanceId. Polled by the runner pod (06).
    ValueTask<IReadOnlyList<TaskKey>> ListCancelRequestsAsync(string instanceId, CancellationToken ct);

    // Released tasks: Runner == null, state SUBMITTED or WORKING, UpdatedAt < olderThan. Oldest first.
    // Used by the recovery sweep (09) and the cluster (06), which pass now minus the lease length (B4).
    ValueTask<IReadOnlyList<StoredTask>> ListReleasedAsync(DateTimeOffset olderThan, int limit, CancellationToken ct);

    // Deletes up to max tasks with ExpiresAt <= now. Returns the count. A no-op for stores with native TTL.
    ValueTask<int> PurgeExpiredAsync(DateTimeOffset now, int max, CancellationToken ct);
}

public sealed record TaskWrite(StoredTask Task, IReadOnlyList<OutboxEntry> Outbox);
public readonly record struct CreateResult(StoredTask Task, bool Created);
```

`ownerHint` lets a store that partitions by owner (Cosmos) do a point read. A store must still find the task when the hint is wrong or null *(writer's call)*. The core passes the caller's owner key.

### 7.4 Queries and paging

```csharp
public sealed record TaskQuery
{
    public required OwnerScope Scope { get; init; }
    public string? ContextId { get; init; }
    public TaskState? State { get; init; }
    public DateTimeOffset? StatusTimestampFrom { get; init; } // inclusive (>=)
    public required int PageSize { get; init; }              // already validated: 1..100
    public TaskCursor? After { get; init; }                  // already decoded
    public bool IncludeArtifacts { get; init; }
    public int? HistoryLength { get; init; }                 // already validated: null or >= 0
}

public readonly record struct TaskCursor(DateTimeOffset StatusTimestamp, string TaskId);

public sealed record TaskPage(IReadOnlyList<AgentTask> Tasks, TaskCursor? Next, int TotalSize);
```

Rules every store must follow (all in the conformance suite):

- Order: `Task.Status.Timestamp` descending, then `TaskId` descending (ordinal). Same as today (`InMemoryTaskStore.cs:41-42`).
- `After` returns items strictly after the cursor in that order.
- `Next` is null on the last page. A store may detect "more" by reading `PageSize + 1` rows.
- `TotalSize` counts all matches of the filter, ignoring the cursor. Required by the proto. Stores compute it with one count query.
- History is trimmed to the last `HistoryLength` messages (0 means none). Artifacts are an empty list unless `IncludeArtifacts`. Stores should not even read artifact data when it is not needed (EF keeps it in its own column; §7.8).
- `StatusTimestampFrom` is inclusive. Today's in-memory store uses `>` (`InMemoryTaskStore.cs:78`), which contradicts `a2a.proto:696`; the new suite fixes it.

The core owns page tokens (`Ambinder.A2A.Server.Stores.PageTokens`, internal; shared with other packages as a linked source file, package 01 rule 10, B12):

- Token = base64url of `v1:{utcTicks}:{taskId}:{filterHash}`, where `filterHash` is the first 8 bytes of SHA-256 over the canonical filter (context id, state, timestamp-from, include-artifacts) *(writer's call)*. A token used with a different filter is `InvalidParams` ("pageToken does not match this query; start again without pageToken").
- The core validates page size (1–100, default 50) and history length, and decodes the token, then builds `TaskQuery`. This replaces the checks now in `InMemoryTaskStore.cs:25-37` and `InMemoryPushConfigStore.cs:44-49`.
- The core sets `NextPageToken = ""` when `Next` is null, and `PageSize` to the size used.
- Tokens are not signed, on purpose *(Claude's call)*. The official .NET SDK HMAC-signs its push-config cursors and binds them to a revision (`InMemoryPushNotificationStore`), because its offsets are positional. Ours are keyset cursors, and `TaskQuery.Scope` comes from the call context, never from the token, so an edited token can only move the caller within their own data. A malformed token is `InvalidParams`. The same holds for push-config tokens (`v1:{sequence}`, §7.7). Recorded in `docs/spec-decisions.md`.

### 7.5 Instance leases

```csharp
public interface IInstanceStore
{
    // Upsert the row and push ExpiresAt forward. Fails with Conflict if the row was taken over (epoch changed).
    ValueTask<InstanceLease> HeartbeatAsync(InstanceLease lease, CancellationToken ct);
    ValueTask<IReadOnlyList<InstanceLease>> ListAsync(CancellationToken ct);
    // Rows with ExpiresAt <= now that still exist. Recovery (09) handles their tasks, then deletes the row.
    ValueTask<IReadOnlyList<InstanceLease>> ListExpiredAsync(DateTimeOffset now, CancellationToken ct);
    ValueTask DeleteAsync(string instanceId, string expectedVersion, CancellationToken ct);
}

public sealed record InstanceLease
{
    public required string InstanceId { get; init; }
    public string? InternalAddress { get; init; }   // pod-to-pod URL, set by 06
    public InstanceStatus Status { get; init; }     // Active | Draining
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string? Version { get; init; }
}
```

The heartbeat interval, lease length and recovery logic belong to 09 and 06. This package only stores the rows. The in-memory store implements it too, so single-instance recovery code runs the same path.

### 7.6 Outbox rows

```csharp
public sealed record OutboxEntry
{
    public required string Id { get; init; }            // GUID, set by the writer
    public required TaskKey Task { get; init; }
    public required long Sequence { get; init; }        // the task's Sequence at write time; per-task order
    public required string Kind { get; init; }          // e.g. "push"; defined by 08
    public required string Lane { get; init; }          // ordering lane inside the task, e.g. the push config id; opaque
    public required ReadOnlyMemory<byte> Payload { get; init; } // opaque to the store
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public int Attempts { get; init; }
    public string? ClaimedBy { get; init; }
    public DateTimeOffset? ClaimExpiresAt { get; init; }
    public string? LastError { get; init; }             // short text, no secrets
    public DateTimeOffset? DeadLetteredAt { get; init; }
    public string? Version { get; init; }
}

public interface IOutboxStore
{
    // Claims up to max due rows (NextAttemptAt <= now, not claimed or claim expired, not dead-lettered).
    // Lane heads only: a row is returned only if no row with a lower Sequence is pending in the same
    // (Task, Lane). So each lane is delivered in order. A row is claimed by at most one caller at a time.
    ValueTask<IReadOnlyList<OutboxEntry>> ClaimDueAsync(string claimant, DateTimeOffset now, TimeSpan claimFor, int max, CancellationToken ct);
    ValueTask<OutboxEntry?> TryClaimAsync(string id, string claimant, DateTimeOffset now, TimeSpan claimFor, CancellationToken ct);
    // The calls below take the claimed row. They fail with Conflict when the claim was lost
    // (Version changed, for example after the claim expired and another pod claimed it).
    ValueTask CompleteAsync(OutboxEntry claimed, CancellationToken ct);            // deletes the row
    ValueTask RescheduleAsync(OutboxEntry claimed, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken ct);
    ValueTask DeadLetterAsync(OutboxEntry claimed, string reason, CancellationToken ct); // frees the lane for the next row
    ValueTask<IReadOnlyList<OutboxEntry>> ListDeadLettersAsync(OwnerScope? scope, int max, CancellationToken ct);
    ValueTask<bool> RequeueDeadLetterAsync(string id, DateTimeOffset nextAttemptAt, CancellationToken ct);
    ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset deadLetteredBefore, int max, CancellationToken ct);
    ValueTask<OutboxStats> GetPendingStatsAsync(CancellationToken ct);         // count and oldest CreatedAt
}

public readonly record struct OutboxStats(long Pending, DateTimeOffset? OldestCreatedAt, long DeadLettered);
```

`Version` on a claimed row is the lease token 08 asks for. In-memory capacity behavior (dead-letter with reason `outbox_full` when full) is package 08's rule. The in-memory store gives 08 a `Capacity` option and `InsertDeadLettered` path to do it.

- Outbox rows are written only through `TaskWrite.Outbox`, in the same transaction or batch as the task. That is what makes the outbox safe.
- The in-memory store implements it too (not durable, but the same code path, so 08 has one delivery design).
- A seam lets 08 add rows to a write without the core knowing about push:

```csharp
public interface ITaskWriteContributor
{
    // Called by the core for each committed event, before the store write. May add outbox rows.
    ValueTask ContributeAsync(TaskWriteContext context, CancellationToken ct);
}
```

`TaskWriteContext` gives the old and new `StoredTask`, the `StreamResponse` event and `AddOutbox(OutboxEntry)`. This replaces the post-save `ITaskEventObserver` for durable side effects; observers stay for in-process notifications *(writer's call)*.

### 7.7 Push config store

```csharp
public interface IPushConfigStore
{
    // Inserts or replaces by (task, config id). The core assigns the id when empty. Returns what was stored.
    ValueTask<StoredPushConfig> UpsertAsync(TaskRef task, TaskPushNotificationConfig config, string? protocolVersion, CancellationToken ct);
    ValueTask<StoredPushConfig?> GetAsync(TaskRef task, string configId, CancellationToken ct);
    ValueTask<PushConfigPage> ListAsync(TaskRef task, int pageSize, long? afterSequence, CancellationToken ct);
    ValueTask<IReadOnlyList<StoredPushConfig>> GetAllAsync(TaskRef task, CancellationToken ct); // for delivery
    ValueTask<int> CountAsync(TaskRef task, CancellationToken ct);
    ValueTask DeleteAsync(TaskRef task, string configId, CancellationToken ct);              // idempotent
    ValueTask DeleteAllAsync(TaskRef task, CancellationToken ct);
}

public readonly record struct TaskRef(TaskKey Key, string Owner);   // from a loaded StoredTask
public sealed record StoredPushConfig(TaskPushNotificationConfig Config, long CreatedSequence, DateTimeOffset CreatedAt)
{
    // Opaque protocol version of the API that created the config ("1.0", or "0.3" from package 15).
    // Package 08 picks the payload formatter by it. Null means "1.0".
    public string? ProtocolVersion { get; init; }
}
public sealed record PushConfigPage(IReadOnlyList<StoredPushConfig> Configs, long? NextAfter);
```

`UpsertAsync` takes the protocol version as a nullable parameter (`null` means "1.0") and stores it unchanged. The core passes the call's `A2ACallContext.ProtocolVersion`, so a config created through package 15's 0.3 methods is stored with `"0.3"`. *(Reconciled: the text said "optional parameter", which cannot precede `ct`; the code block now shows the parameter.)*

- `TaskRef` carries the owner so Cosmos can keep configs in the task's partition. The core always loads the task first (as today, `TaskManager.cs:183,190`), so the owner is known.
- Order is creation order (`CreatedSequence`), as today. The core encodes the page token (`v1:{sequence}`) and returns `""` on the last page. Today's store returns `null` (`InMemoryPushConfigStore.cs:62`); the proto says empty.
- The core enforces package 08's `PushOptions.MaxConfigsPerTask` (default 10) with `CountAsync` before an insert *(writer's call: a small race can allow one extra config; documented)*. The option lives in `PushOptions` only, not in the store options (push owns the limit, package 08).
- Configs are deleted with the task, and their expiry follows the task's `ExpiresAt`.
- Durable stores (EF, Cosmos) call package 08's `IPushCredentialProtector` on `authentication.credentials` and `token`: `Protect` before every write, `Unprotect` after every read. The in-memory store does not, because nothing leaves the process. The conformance suite checks the stored bytes of a durable store never contain the plain credential (through a test protector).
- Tenant scoping comes from `TaskRef.Key.Tenant`, like tasks.

### 7.8 Errors

```csharp
public sealed class A2AStoreException : Exception
{
    public StoreErrorKind Kind { get; }      // Conflict, AlreadyExists, NotFound, Capacity, Unavailable
    public bool IsTransient { get; }          // true for Unavailable and throttling
}
```

- Stores translate provider errors into these kinds. They never throw `DbUpdateConcurrencyException` or `CosmosException` to the core.
- The core maps them:
  - `Conflict` → reload and retry (see §7.10)
  - `Capacity` → HTTP 503 with `Retry-After`, gRPC `UNAVAILABLE`, message "The task store is full. Retry later or raise InMemoryTaskStoreOptions.MaxTasks."
  - `Unavailable` → the same transport mapping, message "The task store is not reachable."
  - Anything else → `InternalError` with a correlation id
- The transport mapping for 503 is package 09's (it owns limits responses).

### 7.9 Retention

`A2AStoreOptions` (core, section `A2A:Store`):

| Option | Default | Meaning |
|---|---|---|
| `TerminalTaskTtl` | 30 days | Time after a task reaches a terminal state until it is deleted. `null` keeps tasks forever. |
| `InterruptedTaskTtl` | 30 days | The same for `INPUT_REQUIRED` / `AUTH_REQUIRED` tasks, counted from the last update. |
| `PurgeInterval` | 5 min | How often `A2AStoreMaintenanceService` (hosted service) calls `PurgeExpiredAsync`. |
| `PurgeBatchSize` | 500 | Rows per purge call; it loops until fewer are returned. |

The push-configs-per-task limit is `PushOptions.MaxConfigsPerTask` (package 08, default 10), not a store option. *(Reconciled: both 05 and 08 defined it; push owns it.)*

- The core sets `ExpiresAt` on every write, from the state *(writer's call: the store stays dumb)*. Working tasks have no expiry.
- Data minimization is the reason for a default TTL *(writer's call)*. The 30-day default for finished tasks on durable stores is kept; teams set `null` to keep tasks forever *(Claude's call, C5)*.

`InMemoryTaskStoreOptions`:

| Option | Default | Meaning |
|---|---|---|
| `MaxTasks` | 10,000 | Hard cap. At the cap, the oldest expired task is evicted, then the oldest terminal task. If none can be evicted, `CreateAsync` fails with `Capacity`. |
| `TerminalTaskTtl` | 1 h | Overrides the core default for memory. |
| `MaxPushConfigs` | 50,000 | Across all tasks. |

These defaults are *(writer's call)*. Two performance rules apply to the in-memory store; they close package 14's PD6:

- **Get and save are O(1) in history length.** Today every get and save copies history and artifacts (`InMemoryTaskStore.cs:15,19,97-98`). The new store keeps immutable snapshots: `AgentTask` lists are stored as immutable collections built by the core, and get returns the same snapshot to every caller. Callers cannot change it, so no copy is needed.
- **`ListAsync` uses an index.** The store keeps a sorted index per `OwnerScope` (by timestamp and id), so `ListAsync` no longer sorts all tasks (`InMemoryTaskStore.cs:39-43`). A page of 50 out of 10,000 is measured by 14's `Store` scenario and gets a budget.

### 7.10 Core changes (migration from current code)

- `TaskWriter.CommitAsync` (`TaskWriter.cs:35-59`) becomes:
  1. build the new record
  2. run the `ITaskWriteContributor`s
  3. call `UpdateAsync(write, current.Version)`, or `CreateAsync` for the first event
  4. publish to the event bus
  5. notify observers
- On `Conflict`, the writer reloads the task:
  - If `Runner.InstanceId` is no longer this instance, the run has been taken over. The writer stops the agent (cancels its token, logs a warning) and drops the event.
  - Otherwise, it re-applies the event to the fresh record and retries once.
  - A second conflict is `InternalError`.
- The per-task in-process lock stays for ordering inside one pod. It becomes reference-counted and is removed when no one holds or waits for it, which fixes the leak at `TaskWriter.cs:20-32` (R15, package 14's PD8). Package 14's allocation test checks it: 100,000 distinct tasks leave no lock entries behind. Package 06 reuses this lock table unchanged.
- `TaskManager` builds keys from `A2ACallContext.Tenant` (now required, `""` when none; 07 resolves it) instead of the agent name alone.
- `ListTasksAsync` (`TaskManager.cs:97-101`) validates, then calls `ListAsync`. `GetTaskAsync` shapes history in the core, as today (`RequestValidator.ShapeHistory`).
- `SendMessage` for a new task passes `OriginMessageId = message.MessageId`. A retried send with the same message id from the same owner returns the existing task instead of starting a second run (spec: SendMessage MAY dedupe by messageId, research-spec §3) *(writer's call; only for new-task messages, not continuations)*. This is the one dedupe rule all specs use:
  - Scope: `(tenant, agent, owner, messageId)`, for `SendMessage` and `SendStreamingMessage` without a `taskId`. On by default *(Claude's call, C5)*. A client that reuses message ids by mistake gets the earlier task back; the docs say so.
  - A repeat returns the existing task as it is now (`CreateResult.Created = false`). The agent handler is not run again (package 11).
  - A message with a `taskId` (a continuation) is never deduped.
  - Package 10's client keeps sends `NotSafe` by default, because other servers may not dedupe (the spec says MAY). `AllowSendRetry` is safe only for new-task sends to a server that dedupes, such as any `Ambinder.A2A` server with default settings.
- Artifact chunks: each agent event, including each streamed artifact chunk, stays one store write *(writer's call)*. Reasons:
  - The rule "saved before it is published" keeps a resubscribe snapshot consistent with the live stream.
  - Push (08) needs one outbox row per committed event.
  - Batching would also have to merge events on the wire.

  The cost is bounded in three ways:
  - Package 11 coalesces streamed tokens into about 40 chunk events per answer.
  - The core merges adjacent text parts on append (11).
  - EF keeps artifacts in their own column (§7.11), so a chunk does not rewrite history.

  Package 14 measures it: a benchmark streams 2,000 chunk events into one task on SQLite and PostgreSQL. If that misses its budget, a later minor release may add an opt-in write-behind mode. The contract does not change for that.
- `StoredTask` and the old interfaces are replaced, not kept as obsolete overloads. This is pre-1.0 and package 01 renames everything anyway.
- `PageToken` (public today, R6) becomes internal.

### 7.11 Entity Framework Core stores

Registration:

```csharp
builder.Services.AddA2AServer(...)
    .AddEntityFrameworkStores(o => o.UseA2APostgreSql(connectionString));
// or .UseA2ASqlite("Data Source=a2a.db") / .UseA2ASqlServer(cs)
// or .AddEntityFrameworkStores<MyDbContext>()   // tables inside the app's own DbContext
```

Schema (schema `a2a` on PostgreSQL and SQL Server, table prefix `a2a_` on SQLite) *(writer's call)*:

| Table | Key | Main columns | Indexes |
|---|---|---|---|
| `tasks` | `tenant, agent, task_id` | `owner`, `context_id`, `state`, `status_ts`, `sequence`, `version` (bigint, concurrency token), `created_at`, `updated_at`, `expires_at`, `runner_instance`, `runner_claimed_at`, `cancel_requested_at`, `run_attempt`, `origin_message_id`, `continuation_grant_json`, `last_runner_instance`, `caller_json` (B8), `task_json`, `artifacts_json` | see the list below |
| `push_configs` | `tenant, agent, task_id, config_id` | `created_seq` (identity), `created_at`, `protocol_version`, `config_json` (credentials protected) | `(tenant, agent, task_id, created_seq)` |
| `outbox` | `id` | `tenant, agent, task_id, lane, sequence, kind, payload` (bytea / BLOB / varbinary), `created_at`, `next_attempt_at`, `attempts`, `claimed_by`, `claim_expires_at`, `last_error`, `dead_lettered_at`, `version` | `(next_attempt_at)` where not dead-lettered; `(tenant, agent, task_id, lane, sequence)`; `(dead_lettered_at)` |
| `instances` | `instance_id` | `internal_address`, `status`, `started_at`, `expires_at`, `version` | `(expires_at)` |

Indexes on `tasks`:

- list: `(tenant, agent, owner, status_ts DESC, task_id DESC)`
- list by context: `(tenant, agent, owner, context_id, status_ts DESC, task_id DESC)`. Package 11's `GetConversationAsync` depends on it.
- dedupe: unique `(tenant, agent, owner, origin_message_id)` where not null
- runner: `(runner_instance)` where state is not final
- released: `(updated_at)` where `runner_instance` is null and state is `SUBMITTED` or `WORKING` (for `ListReleasedAsync`, B4)
- purge: `(expires_at)`

Rules:

- `task_json` holds the task without artifacts. `artifacts_json` holds artifacts, so a list without artifacts never reads them. On PostgreSQL both columns are `jsonb`; on the others `TEXT` / `nvarchar(max)`. They are serialized with the SDK's source-generated JSON context (`A2AJson`), not EF JSON mapping *(writer's call: one serializer, no reflection)*.
- Conditional update is one `UPDATE … WHERE key AND version = @expected` through `ExecuteUpdateAsync`. Zero rows means `Conflict` (or `NotFound` after a check).
- Writes that include outbox rows run in one database transaction.
- `ClaimDueAsync` uses `FOR UPDATE SKIP LOCKED` on PostgreSQL, `READPAST, UPDLOCK` on SQL Server, and a single conditional `UPDATE … RETURNING` on SQLite (one writer at a time). The builder may pick the exact SQL.
- Contexts are pooled (`AddPooledDbContextFactory`). Hot queries are compiled queries.
- EF Core is not trimming/AOT-safe today. The EF packages are marked not AOT-compatible; the core stays AOT-compatible.

Migrations:

- Each provider package ships EF migrations for its own context type (`A2APostgreSqlStoreContext` and so on).
- `await app.MigrateA2AStoreAsync()` applies them. `A2AEfStoreOptions.AutoMigrate` defaults to `true` only when the host environment is Development; otherwise `false`. If the schema is behind, startup fails with "The A2A store schema is at migration X, the package needs Y. Run `app.MigrateA2AStoreAsync()` or apply `a2a-store-<version>.sql`." *(writer's call)*
- Each release also ships an idempotent SQL script per provider as package content and a docs download. DBAs can run it, for example as a Kubernetes Job.
- In own-context mode, `modelBuilder.ApplyA2AStoreModel()` adds the tables to the app's model, and the app owns the migrations. The docs list every schema change per release.
- A schema version row (`a2a.schema_info`) lets the startup check work in both modes.

### 7.12 Cosmos DB stores

Registration:

```csharp
.AddCosmosStores(o =>
{
    o.Database = "a2a";
    o.Container = "tasks";          // one container for tasks, push configs, outbox
    o.InstancesContainer = "a2a-instances";
}); // uses a CosmosClient from DI, else builds one from o.AccountEndpoint + TokenCredential
```

Partitioning *(writer's call)*:

- Hierarchical partition key `/tenant`, `/agent`, `/owner` on the `tasks` container. `ListTasks` is always for one `(tenant, agent, owner)`, so it is a single-partition query.
- Push configs and outbox rows for a task live in the same logical partition, with a `type` property (`task`, `push`, `outbox`). A task write plus its outbox rows is one `TransactionalBatch`, which is atomic within a logical partition.
- `GetAsync` with the right `ownerHint` is a point read. Otherwise it queries by id with a `(tenant, agent)` prefix. Only a continuation by a different identity (AUTH_REQUIRED step-up) needs this.
- Limit: one logical partition holds at most 20 GB. That is one owner's tasks for one agent. Retention keeps it bounded. The docs state the limit and how to watch it. This partition key is accepted for 1.0; no hashed bucket level *(Claude's call, C5)*.
- Cluster mode: in 1.0, package 06's cluster mode is supported on the EF Core store only. With the Cosmos store, cluster mode fails fast at startup with a clear error ("Cluster mode needs the EF Core store in 1.0; Cosmos cluster support comes after 1.0."). Cosmos cluster support is after 1.0 *(Claude's call, B3)*.
- Instance leases live in a small separate container partitioned by `/instanceId`.

Concurrency, TTL, paging:

- `Version` is `_etag`. Updates use `IfMatchEtag`. HTTP 412 → `Conflict`, 409 on create → `AlreadyExists`, 429 and 503 → `Unavailable` with `IsTransient = true` (after the SDK's own retries).
- TTL: the container has default TTL on (`-1`), and each item sets `ttl` in seconds from `ExpiresAt`. `PurgeExpiredAsync` is a no-op.
- Paging uses the core cursor, not Cosmos continuation tokens, so tokens look the same for every store. Queries use `ORDER BY c.statusTs DESC, c.id DESC` with composite indexes:
  - `(statusTs DESC, id DESC)`
  - `(contextId ASC, statusTs DESC, id DESC)`
  - `(state ASC, statusTs DESC, id DESC)`
- `TotalSize` is a `SELECT VALUE COUNT(1)` in the same partition.
- `ListByRunnerAsync`, `ListCancelRequestsAsync`, `ListReleasedAsync` (`WHERE IS_NULL(c.runner) AND c.state IN (...) AND c.updatedAt < @olderThan ORDER BY c.updatedAt`, with a composite index `(state ASC, updatedAt ASC)`, B4) and `ClaimDueAsync` are cross-partition queries on indexed properties. Their frequency is set by 06, 08 and 09 *(writer's call: 08 should deliver from memory first and poll only as recovery, to keep RU cost low; see Dependencies)*.
- Every `StoredTask` field, including `continuationGrant`, `lastRunnerInstanceId`, `caller` and the push config `protocolVersion`, is a plain property of its item. Outbox items carry `lane`, and the lane-head rule in `ClaimDueAsync` is enforced per `(task, lane)`: the query returns candidate rows, and the store claims one only after a point check that no lower sequence of that lane is pending. The check and claim are an ETag-guarded batch in the task's partition *(writer's call; the builder may pick another way that passes the suite)*.
- Serialization uses a `CosmosSerializer` built on the SDK's source-generated `System.Text.Json` context.
- The store creates the containers and indexes at startup only when `o.CreateIfMissing` is true (default: Development only).

### 7.13 Conformance suite

- Shape agreed with package 12: framework-neutral case catalogs (`TaskStoreConformanceCases`, `PushConfigStoreConformanceCases`, `OutboxStoreConformanceCases`, `InstanceStoreConformanceCases`: named `Func<StoreSet, CancellationToken, Task>` cases), plus one abstract xUnit v3 class per catalog (`TaskStoreConformanceTests` and so on) that turns each case into one named test.
- A team derives one class and overrides `CreateStoresAsync()` to return a fresh, empty `StoreSet` (and a `FakeTimeProvider` the suite controls).
- Split of work: this package writes the cases and the project in M1, under `tests/Ambinder.A2A.Stores.Conformance`. The shipped stores' test projects use it through a project reference, exactly as a third party would through the package. In M2, package 12 moves the project to `src/` and publishes it as `Ambinder.A2A.Testing.Conformance`, without changing the case names.
- About 85 tests in groups:
  - CRUD and keys; every `StoredTask` field round-trips (including `ContinuationGrant`, `LastRunnerInstanceId`)
  - tenant isolation
  - owner scoping in `ListAsync`
  - order, cursors, `TotalSize`, filters (including inclusive timestamp)
  - history and artifact shaping
  - optimistic concurrency (stale version, racing writers)
  - create dedupe by origin message id
  - delete cascades
  - expiry and purge
  - runner queries and cancel requests
  - released tasks: `ListReleasedAsync` returns only `Runner == null` tasks in `SUBMITTED`/`WORKING` older than the cutoff, oldest first, across owners and tenants, and respects `limit` (B4)
  - reduced caller: `StoredCaller` round-trips and never contains a token (B8)
  - outbox atomicity (a fault hook on the store under test, `IStoreFaultInjector`, optional), claiming by one claimant at a time, lane-head order, lost-claim conflict, dead letters, requeue, purge, pending stats
  - push config order, idempotent delete, count, `ProtocolVersion` round trip, credentials protected at rest (durable stores)
  - `ListAsync` by context id within an owner (another owner's tasks with the same context id never appear)
  - instance heartbeats and expiry
  - large values (a 1 MB artifact, 1,000 history messages)
- Tests that need a fault hook or native TTL skip cleanly when the store declares it lacks them (`StoreCapabilities`).

## 8. How it meets the six principles

1. **Protocol.** The `ListTasks` and push-list rules come from proto field comments. Check: conformance tests cite `a2a.proto` lines, and the TCK `ListTasks` tests pass on every store (package 03 runs the TCK against SQLite as well as in-memory).
2. **.NET enabler.** `AddEntityFrameworkStores`, `AddCosmosStores`, Options pattern, `ILogger`, `TimeProvider`, pooled `DbContext`, `CosmosClient` from DI. Check: public API review in 02; XML docs on every public member (build fails on CS1591).
3. **Easy.** The in-memory store needs no setup; SQLite needs one line and migrates itself in Development. Check: the docs sample "add PostgreSQL" is at most 3 lines of code plus a connection string, and a store error message names the option or command to fix it (a test asserts the text).
4. **Performance.** One indexed page read plus one count per list; artifacts not read unless asked; source-generated JSON; pooled contexts; compiled queries. Check: benchmarks in package 14 for Get, Create, Update and List on in-memory and SQLite in CI, PostgreSQL and Cosmos emulator nightly. Baseline targets *(writer's call)*: in-memory Update p50 < 5 µs; PostgreSQL List (page 50 of 10,000 for one owner) p95 < 20 ms in the CI container.
5. **Modular.** The contract is in the core with no database dependency; every provider is its own package; teams can write a store and prove it with the suite. Check: a package-dependency test fails if the core references EF Core or Cosmos.
6. **Integration pains.** Durable, user-scoped stores with migrations, so teams stop writing their own. Check: `samples/AgentServer` runs on PostgreSQL through docker-compose with one setting changed.

## 9. Dependencies

Needs:

- **01**: final package names and the core assembly split.
- **02**: API tracking files for the new public types.
- **07**: the tenant value and owner key in `A2ACallContext` (`Tenant` non-null). Until 07 lands, the core passes `""` and today's owner key.
- **07**: `TaskContinuationGrant` (stored as `StoredTask.ContinuationGrant`), `OwnerKey` (B7) and the reduced-caller fields (B8).
- **12**: the internal test-harness slice built in M1 (B2: `StoreSet`, `FakeTimeProvider` wiring, `TaskEventLog` waiters).
- **08**: `IPushCredentialProtector` (called by durable push-config stores).
- **12**: takes over the conformance project in M2 and publishes it as `Ambinder.A2A.Testing.Conformance`.
- **14**: benchmark harness and budgets (including the 2,000-chunk benchmark in §7.10).

Provides:

- **06**:
  - `StoredTask.Runner`, `StoredTask.LastRunnerInstanceId`, `StoredTask.CancelRequestedAt`
  - `ITaskStore.ListByRunnerAsync`, `ITaskStore.ListCancelRequestsAsync`, `ITaskStore.ListReleasedAsync` (B4)
  - `StoredTask.Caller` (`StoredCaller`, B8)
  - `IInstanceStore`, `InstanceLease` (with `InternalAddress`, `Status`)
  - the rule that cluster mode is EF Core only in 1.0 (B3)
  - `A2AStoreException` with `Kind = Conflict` as the fencing signal
- **07**: `StoredTask.ContinuationGrant`, and tenant and owner isolation cases in the conformance suite.
- **08**:
  - `OutboxEntry` (with `Lane`), `IOutboxStore` (lane-head claims, requeue, purge, `OutboxStats`)
  - `ITaskWriteContributor`, `TaskWriteContext`
  - `TaskWrite.Outbox`, `StoredTask.Sequence`
  - `IPushConfigStore`, `StoredPushConfig` (with `ProtocolVersion`), `TaskRef`
  - Note for 08: Cosmos `ClaimDueAsync` is a cross-partition query; use it as the recovery path, not on every event.
- **09**:
  - `StoredTask.RunAttempt`, `StoredTask.Runner`, `StoredTask.Caller` (B8)
  - `ITaskStore.ListByRunnerAsync`, `ITaskStore.ListReleasedAsync` (B4), `IInstanceStore.ListExpiredAsync`
  - an optional store health probe interface `IStoreHealthProbe` (implemented by EF and Cosmos stores)
- **10**: `OriginMessageId` dedupe (rule in §7.10), so a client may retry a new-task `SendMessage` with the same message id when `AllowSendRetry` is set.
- **11**: an indexed `ListAsync` by `ContextId` within an `OwnerScope` (EF index "list by context", Cosmos composite index on `contextId`); `AgentTask.Metadata` kept by every store; the decision that chunk saves are not batched: one store write per artifact event (§7.10); the new-task dedupe rule (§7.10).
- **12**: the case catalogs and the `tests/Ambinder.A2A.Stores.Conformance` project (M1), to publish in M2.
- **15**: `StoredPushConfig.ProtocolVersion` (opaque).

## 10. Acceptance criteria

- [ ] New contract types exist as in §7.2–7.8. Old `ITaskStore`, `IPushConfigStore` and `StoredTask` shapes are gone. Public `PageToken` is internal.
- [ ] Core validates page size, history length and tokens. No store has protocol validation (a test with a store spy proves it).
- [ ] The conformance project exists under `tests/Ambinder.A2A.Stores.Conformance` (four case catalogs and four xUnit classes, about 85 cases) in the shape agreed with 12. It passes for:
  - [ ] in-memory
  - [ ] EF + SQLite
  - [ ] EF + PostgreSQL (container)
  - [ ] EF + SQL Server (container)
  - [ ] Cosmos (emulator; a real-account run only when its secrets exist, skipped visibly otherwise, O3; if the emulator lacks hierarchical keys, the manual pre-release checklist covers the real account)
- [ ] `ListReleasedAsync` exists with its conformance cases, the EF `released` index and the Cosmos query and composite index (B4).
- [ ] Starting with cluster mode and the Cosmos store fails at startup with the documented message (B3).
- [ ] No new M1 test uses fixed ports or sleeps (B2).
- [ ] Racing-writer test: 2 writers × 1,000 updates, no lost update, on every store.
- [ ] Outbox atomicity fault test passes on EF (all three providers) and Cosmos.
- [ ] In-memory soak test: 1,000,000 tasks created and finished; working set stays under 300 MB *(writer's call)*; lock table returns to 0 entries.
- [ ] PD6 closed: 14's `Store` scenario shows in-memory get and save allocation and time flat across history 0 / 20 / 200. List of 50 out of 10,000 has a recorded budget.
- [ ] PD8 closed: 14's allocation test (100,000 distinct tasks, no residue in the lock table) passes.
- [ ] EF migrations ship for three providers. An idempotent SQL script per provider ships with the package. `MigrateA2AStoreAsync` works, and the startup check fails with the documented message when the schema is old.
- [ ] Retention: tasks expire per options on EF (purge service) and Cosmos (native TTL).
- [ ] TCK passes with the server on SQLite and PostgreSQL stores (03's CI job gets a store matrix).
- [ ] Benchmarks for in-memory and SQLite run in CI, and the baseline is recorded (14).
- [ ] Docs sections (13): "Choose a store", "PostgreSQL", "SQLite for local work", "SQL Server", "Cosmos DB (partitioning and limits; not for cluster mode in 1.0)", "Migrations and DBAs", "Write your own store (run the conformance suite)", "Retention".
- [ ] `docs/production.md` §3 is replaced by a link to the store docs.

## 11. Left to the builder

- Exact SQL for claim queries and the shape of compiled queries.
- Internal data structures of the in-memory store (sorted sets, immutable snapshots, lock striping).
- Whether the conformance suite uses one store set per test or per class (it must be isolated per test).
- Column sizes and collations (ordinal comparison of ids is required).
- How the Cosmos store builds its own `CosmosClient` when none is in DI (it must accept `TokenCredential`; keys only through configuration).
- Log event ids and names.
- Whether `PurgeExpiredAsync` on EF uses `ExecuteDeleteAsync` in batches or a raw statement.

## 12. Open questions

None. Default retention, the Cosmos partition key and message-id dedupe are Claude's call C5 (§7.9, §7.12, §7.10).
