# SDK 06: Multi-instance cluster layer

Date: 2026-11-03
Status: draft for approval
Milestone: M2 (`1.0.0`)
Size: XL

Items marked *(writer's call)* were chosen by the spec writer, not the owner. Items marked *(Claude's call)* were decided by Claude after review.

## 1. Intent

Teams on AKS run at least two replicas. Today streaming, resubscribe and cancel break silently behind a load balancer, because:

- the event broker is in process memory
- the running agent and its cancel token are in process memory
- per-task locks are in process memory

This package makes a group of pods behave like one A2A server. The owner chose HTTP owner-pod forwarding for v1:

- The store records which pod runs a task.
- Other pods forward cancel, subscribe and continue requests to that pod over internal HTTP.
- Leases handle a pod that dies.

No Redis or message bus is needed. The layer is abstract, so a Redis backend can plug in after 1.0 behind the same interfaces.

## 2. Findings addressed

- **R13** Single-instance only:
  - in-memory broker, run registry and cancel map (`TaskManager.cs:28,32`, `TaskManager.cs:116-125`)
  - in-process locks (`TaskWriter.cs:20-32`)
  - `TaskEventBroker` is sealed, with no interface (`TaskEventBroker.cs:14`)
- **R15 / PD8** (cluster part): no per-task state may leak on any pod when tasks move between pods (the lock-table fix itself is package 05).
- Pain point **"Streaming (SSE) robustness: buffering, reconnect, slow consumers, lost events"** (B):
  - one slow subscriber must not stall others (a2a-go #395)
  - no lost events (a2a-python #768)
- Pain point **"Persistent task store, multi-instance servers, push-notification delivery"** (A), multi-instance part. In other SDKs, queues across instances are fragile (python #768). a2a-dotnet #463 says "task live coordination is process-local across server instances" (research-sdks).
- Pain point **"Cancellation semantics"** (B): a cancel that lands on another pod must reach the running agent.

## 3. Goals

1. With 3 pods behind a round-robin load balancer, every A2A operation gives the same results as on one pod. Tested by running the full TCK and the integration flow suite against a 3-pod test cluster with random routing.
2. A cancel that arrives on any pod stops the agent on the pod that runs it within 1 s (fast path), or within 15 s if the internal call fails (durable path). Tested.
3. A `SubscribeToTask` on any pod gets the snapshot and then every later event of the task in order, with no gap or duplicate. Tested with 1,000 events across pods and an order check.
4. A subscriber either receives every event in order, or its stream ends with an error that tells the client to resubscribe. It never ends early without an error and never skips an event. Tested with fault injection (owner pod killed, internal connection cut, proxy slow).
5. One slow subscriber, local or proxied, never delays events to other subscribers of the same or other tasks. Tested: one subscriber stops reading; 50 others on 2 pods keep receiving within 100 ms of each event.
6. When a pod dies, a surviving pod takes over its tasks within 30 s (package 09's recovery policy), and proxied streams of those tasks end with the resubscribe error. Tested by killing a pod process.
7. Graceful scale-down of a pod moves no task into a stuck state, and open proxied streams on other pods end cleanly. Tested with a rolling restart of 3 pods during 20 streaming tasks.
8. Internal calls are authenticated. A request to the internal port without valid pod credentials gets 401 and is never executed. Tested.
9. The core depends only on interfaces for the event bus, routing and forwarding. A test double backend (in-process "fake Redis") passes the cluster test suite without the HTTP forwarder. This proves a Redis backend fits.
10. A single-pod deployment pays nothing for this layer: without `AddA2ACluster()` there is no internal port, no extra store call per operation, and benchmarks match package 14's single-instance budgets.

## 4. Non-goals

- The Redis backend. After 1.0 (work plan). Only the interfaces must allow it.
- Store fields, instance leases and conditional writes. Package 05 defines them; this package uses them.
- Graceful shutdown, the recovery sweep and recovery policies. Package 09 builds them in M1; this package adds cluster steps to them.
- Push delivery across pods. Package 08's outbox already claims rows safely from any pod.
- Replay of missed events after a stream drop (`Last-Event-ID`). The spec defines none (research-spec §3 "Streaming", issue #1635). The client resubscribes and gets a snapshot (package 10).
- Moving a running agent between pods without restarting it. A run lives and dies on one pod.
- Sticky sessions at the ingress. They are not needed and not relied on.
- Cross-region or cross-cluster forwarding. One Kubernetes cluster, one namespace.

## 5. Anti-goals

- A cluster that works only when the ingress happens to send related calls to the same pod.
- A forwarded call that skips any check the receiving pod would make for a direct call (authorization, owner, tenant, version, limits).
- Forwarding loops, or forwarding chains longer than one hop.
- A dead pod that leaves subscribers hanging forever with an open but silent stream.
- A design where the slowest client sets the speed for everyone.
- An internal endpoint that is reachable from outside the cluster, or that trusts the network alone.
- Hidden Kubernetes API calls (no RBAC needed to list pods).

## 6. Constraints

- Work plan decision "Multi-instance":
  - an abstract cluster layer, with HTTP owner-pod forwarding as the v1 default
  - the task store records which pod runs a task
  - other pods forward cancel, subscribe and continue requests to it over internal HTTP
  - leases handle crash recovery
  - no Redis or message bus needed; Redis is a later option behind the same interface
- Work plan dependencies:
  - "05 → 06": owner (runner) and lease fields
  - "09 → 06": graceful shutdown and task recovery come first
- Milestone M2. Exit criterion: "multi-instance runs on AKS".
- Package IDs from package 01's map: `Ambinder.A2A.Cluster` ("Cluster abstraction and owner-pod HTTP forwarding", depends on Server and AspNetCore, M2) and `Ambinder.A2A.Cluster.Entra` (Entra workload identity pod auth, M2, C6).
- M1 deployments are single-replica only (Claude's call, B1). Scale-out arrives with this package in M2. Until 1.0 the docs say "do not scale out before 1.0".
- No cloud resources for now (owner decision O3). The real AKS and live Entra tests are written but run only when their secrets exist, and are skipped visibly otherwise. Until then `kind`, fakes and the manual pre-release checklist meet the exit criteria.
- Package 02:
  - `Add…` methods in namespace `Microsoft.Extensions.DependencyInjection`
  - after 1.0, new interface members need default implementations
- Work plan "Auth": Entra ID only, out of the box. This applies to pod-to-pod authentication: only Entra workload identity ships (C6, §7.8).
- A2A v1.0.1 streaming (research-spec §3 "Streaming"):
  - `SubscribeToTask` MUST send the current Task first
  - events MUST be delivered in order to all active streams
  - closing one stream MUST NOT affect others
  - subscribing to a terminal task is an error
- A2A v1.0.1: cancel is idempotent; "MUST NOT reveal existence of unauthorized resources".
- Principle 5: no hidden dependency on Azure or a database in the core.

## 7. Design

### 7.1 Assemblies

| Package | Contents |
|---|---|
| `Ambinder.A2A.Server` (core) | The cluster seams the task manager calls: `ITaskEventBus`, `ITaskRouter`, `ITaskForwarder`, `ClusterCallContext`. The default single-pod implementations: a local event bus (today's `TaskEventBroker` behind `ITaskEventBus`) and a router that always says "local". |
| `Ambinder.A2A.Cluster` | `AddA2ACluster()`; HTTP owner-pod forwarding (`HttpTaskForwarder`, store-backed `ClusterTaskRouter`); instance registry use; the internal endpoints; the pod-auth seam `IClusterPeerAuthentication` (§7.8); the streaming proxy; the cluster health check; cancel polling. Depends on `Ambinder.A2A.Server` and `Ambinder.A2A.AspNetCore`. No Azure dependency. |
| `Ambinder.A2A.Cluster.Entra` | `AddEntraPodAuthentication()`: pod-to-pod auth with Entra workload identity, the only shipped mode *(Claude's call, C6)*. Depends on `Ambinder.A2A.Cluster`, `Ambinder.A2A.AspNetCore.Entra`, `Ambinder.A2A.Client.Entra` and `Azure.Identity`. |
| Later: `Ambinder.A2A.Cluster.Redis` | Not in this package. Implements `ITaskEventBus` and `ITaskRouter` over Redis. |

The seams live in the core because `TaskManager` calls them, and the core may not depend on the cluster package. The four seams (`ITaskEventBus`, `ITaskRouter`, `ITaskForwarder`, `ClusterCallContext`, with their small helper types) sit in `Ambinder.A2A.Server`; `Ambinder.A2A.Cluster` holds the HTTP forwarding backend and the cluster-wide types (peer registry, options, wire format). Package 01's map confirms this split.

### 7.2 Seams in the core

```csharp
namespace Ambinder.A2A.Server.Cluster;

// Live events of a task. Local default: today's TaskEventBroker, made internal behind this interface.
public interface ITaskEventBus
{
    // Never blocks the caller. Called under the task's local lock, after the store write.
    void Publish(TaskKey task, long sequence, StreamResponse evt);
    // Registers now, before returning, so no event published after the call is missed.
    TaskSubscription Subscribe(TaskKey task, SubscriptionOptions options, CancellationToken ct);
}

public abstract class TaskSubscription : IAsyncDisposable
{
    public abstract IAsyncEnumerable<StreamResponse> ReadAllAsync(CancellationToken ct);
}

// Where must this operation run?
public interface ITaskRouter
{
    ValueTask<TaskRoute> RouteAsync(StoredTask task, A2AOperation operation, ClusterCallContext call, CancellationToken ct);
}

public readonly record struct TaskRoute(TaskRouteKind Kind, PeerInstance? Peer);  // Local | Forward
public sealed record PeerInstance(string InstanceId, Uri InternalAddress);

// Runs an operation on another instance. Only the HTTP backend uses it.
public interface ITaskForwarder
{
    ValueTask<AgentTask> CancelAsync(PeerInstance peer, ClusterCallContext call, CancelTaskRequest request, CancellationToken ct);
    IAsyncEnumerable<StreamResponse> SubscribeAsync(PeerInstance peer, ClusterCallContext call, SubscribeToTaskRequest request, CancellationToken ct);
    ValueTask<SendMessageResponse> SendAsync(PeerInstance peer, ClusterCallContext call, SendMessageRequest request, CancellationToken ct);
    IAsyncEnumerable<StreamResponse> SendStreamingAsync(PeerInstance peer, ClusterCallContext call, SendMessageRequest request, CancellationToken ct);
}
```

- Every seam takes a context or record argument, so later inputs are new properties, not new members (package 02 rule). Any member added after 1.0 gets a default body.
- `ClusterCallContext` is the serializable part of `A2ACallContext`:
  - agent, tenant, protocol version, requested extensions, transport
  - the caller, as package 07's reduced caller: owner key, kind, Entra tenant id, app id, user object id, roles and scopes (the same fields as 05's `StoredCaller`) *(Claude's call, B8)*
  - the original request id and trace context
  - `HopCount`
  - It never carries the raw access token *(writer's call)*. So on-behalf-of works only on the pod that received the client request, during that request's run. A continuation forwarded to the previous runner gets no user assertion; the docs say so (07, B8).

How a Redis backend would fit (design check, not built):

- `ITaskEventBus` publishes to a Redis stream per task, so any pod can subscribe.
- `ITaskRouter` returns `Local` for subscribe and continue.
- Cancel uses the store's durable cancel signal (§7.6) or a Redis pub/sub notice.
- `ITaskForwarder` is not used.

Goal 9's fake backend proves this shape.

### 7.3 Configuration

```csharp
builder.Services.AddA2AServer(...)
    .AddEntityFrameworkStores(o => o.UseA2APostgreSql(cs))
    .AddA2ACluster(o => o.InternalPort = 8090)
    .AddEntraPodAuthentication(builder.Configuration.GetSection("A2A:Entra"));   // Ambinder.A2A.Cluster.Entra
```

`A2AClusterOptions` (section `A2A:Cluster`) *(defaults are writer's call)*:

| Option | Default | Meaning |
|---|---|---|
| `InternalPort` | 8090 | HTTP/2 cleartext (h2c) listener for internal calls only. Not in the public Service or ingress. |
| `InternalAddress` | `http://{POD_IP}:{InternalPort}` | What other pods use to reach this one. Written to the instance lease (05 `InstanceLease.InternalAddress`). `POD_IP` comes from the downward API. A template like `http://{POD_NAME}.{HEADLESS_SERVICE}.{NAMESPACE}.svc.cluster.local:8090` is also allowed (StatefulSet, or pods with `subdomain` set). |
| (pod auth) | none (must be registered) | An `IClusterPeerAuthentication`. The only shipped one is Entra workload identity (`AddEntraPodAuthentication()`, package `Ambinder.A2A.Cluster.Entra`, §7.8). Startup fails without it. |
| `ForwardTimeout` | 10 s | Time limit for a unary forward, including connect. |
| `StreamIdleTimeout` | 3 × heartbeat (45 s) | No bytes from the owner pod for this long means the upstream is dead. |
| `CancelPollInterval` | 2 s | How often a pod checks the store for durable cancel requests on its own runs. |
| `ContinuationAffinity` | `PreferPreviousRunner` | Where a continuation runs (§7.5). `Local` runs it on the receiving pod. Default kept *(Claude's call, C7)*. |
| `MaxForwardRetries` | 1 | Re-routes after a "not the runner" answer or a connection failure. |

Startup checks (each fails with what to do):

- The store must be shared and durable, and in 1.0 it must be the EF Core store *(Claude's call, B3)*. With `InMemoryTaskStore`: "A2A cluster mode needs a shared store. Add AddEntityFrameworkStores." With the Cosmos store: "Cluster mode needs the EF Core store in 1.0; Cosmos cluster support comes after 1.0." Cosmos cluster support is after 1.0 (05).
- `InternalAddress` must resolve to a non-loopback address outside Development.
- A pod authentication (`IClusterPeerAuthentication`) must be registered.
- `ITaskEventBus` must not be replaced by a backend that already distributes events, if HTTP forwarding is also on (the two would duplicate events).

The cluster uses the instance identity, lease heartbeat (5 s), lease length (20 s), recovery sweep and drain states from package 09 (`A2AInstanceOptions`, `TaskRecoveryService`). It adds no second heartbeat.

### 7.4 Routing rules

`ClusterTaskRouter` decides per operation, after the receiving pod has loaded the task from the store and checked owner and tenant (so a forward never reveals that a task exists):

| Operation | Routing |
|---|---|
| `GetTask`, `ListTasks`, push config CRUD, `GetExtendedAgentCard` | Always local. They read the shared store. |
| `SendMessage` / `SendStreamingMessage`, new task | Always local. The receiving pod becomes the runner. |
| `SendMessage` / `SendStreamingMessage`, continuation of an interrupted task | Per `ContinuationAffinity` (§7.5). |
| `CancelTask` | Forward to `Runner` if set and its lease is live. Otherwise local (§7.6). |
| `SubscribeToTask` | Forward to `Runner` if set and live. Otherwise local: an interrupted task returns its snapshot and closes, as today (`TaskManager.cs:402`); a task with an expired runner returns "task is being recovered, resubscribe" (§7.7). |

How a pod decides the runner is live:

- It reads the runner's instance lease from a small cache of the instance table, refreshed every heartbeat interval (5 s).
- A runner is live when its lease is unexpired and its status is `Active` or `Draining`.
- The runner being this pod means local.

Loop and race rules:

- A forwarded request carries `HopCount = 1`. A pod that gets a forwarded request never forwards it again.
- If it is not the runner (any more), it answers `409` with a `not-runner` body that names the runner it sees. The sender re-reads the task and retries up to `MaxForwardRetries` times, then answers the client `InternalError` "The task moved between servers; retry."
- If the task became terminal meanwhile, the receiving pod gives the normal answer (for example `UnsupportedOperation` on subscribe to a terminal task), the same as a single pod would.

### 7.5 Continue

A continuation (`SendMessage` with the `taskId` of an `INPUT_REQUIRED` or `AUTH_REQUIRED` task) has no live run: the earlier run ended at the interrupted state. Any pod could run it. The owner's decision lists continue among forwarded calls, and agents often keep pod-local state (a cache, a model session). So the default is affinity:

- `PreferPreviousRunner` (default, kept *(Claude's call, C7)*):
  - If the task's last runner instance is alive and `Active`, forward the whole send (streaming or not) to it. When the core clears `Runner` at an interrupted state, it keeps the id in `StoredTask.LastRunnerInstanceId` (package 05).
  - If it is gone or `Draining`, run locally.
- `Local`: always run on the receiving pod.

Either way the claim is a conditional update (05). Two continuations racing on two pods: one wins, and the other gets `UnsupportedOperation` "Task is WORKING; only a task waiting for input can be continued", the same as a single pod (`TaskManager.cs:260-261`).

### 7.6 Cancel

1. **Fast path.** Pod B receives `CancelTask`, loads and authorizes the task, sees runner A is live, and forwards. Pod A runs today's cancel logic (`TaskManager.cs:103-160`) unchanged and returns the task. B returns it to the client.
2. **Durable path.** It is used if the forward fails (connection error, timeout) while A's lease is still live:
   1. B writes `CancelRequestedAt = now` with a conditional update (05).
   2. A polls `ITaskStore.ListCancelRequestsAsync(self)` every `CancelPollInterval` and runs the same cancel logic for each hit.
   3. B waits for the task to reach a terminal state, polling the store with backoff (200 ms to 2 s), up to `ForwardTimeout + 2 × CancelPollInterval`.
   4. If the state is `CANCELED`, B returns it. If it is another terminal state, B returns `TaskNotCancelable` (as today, `TaskManager.cs:157-158`).
   5. If there is still no final state, B returns `InternalError` "Cancel is in progress; check the task with GetTask." The flag stays set.
3. **No live runner** (lease expired, recovery pending). B claims the task (conditional update: `Runner` = B), commits `CANCELED`, and returns it. The recovery sweep then finds nothing to do for that task.

A cancel and a recovery claim that race are settled by the version check. The loser re-reads and acts on the new state.

### 7.7 Subscribe and the streaming proxy

Flow for a client on pod B and a task running on pod A:

1. B forwards `SubscribeToTask` to A's internal endpoint.
2. A runs today's `OpenSubscriptionAsync` (`TaskManager.cs:392-405`). The snapshot and the live subscription are taken under the task lock, so nothing is lost or repeated between them.
3. A streams the snapshot and the events to B over the internal connection. The framing is internal SSE: each frame carries the event and its task `Sequence`, with heartbeats as in package 09.
4. B passes each event to the client through its normal transport writer (JSON-RPC SSE, REST SSE or gRPC). B writes heartbeats to the client on its own timer (package 09), independent of A.

Ordering and loss checks on B:

- B checks that `Sequence` values only go up by one after the snapshot's sequence.
- A gap or repeat (it should never happen) ends the client stream with the resubscribe error and logs an error with both sequence numbers.

Upstream failure:

- If A's stream ends without a final event, errors, or is idle past `StreamIdleTimeout`, B does not try to splice a new upstream into the same client stream *(writer's call: splicing would need a second snapshot mid-stream, which the protocol does not allow)*.
- B re-reads the task:
  - **Final now:** B sends the client one `TaskStatusUpdateEvent` with the stored final status, then closes normally. The client gets the end it would have got.
  - **Not final:** B ends the client stream with an error frame (SSE) or status (gRPC). The error has reason `TASK_STREAM_INTERRUPTED` and the message "The connection to the task was lost. Call SubscribeToTask again." Package 10's client resubscribes automatically and gets a fresh snapshot. This is the v1 behavior; the proxy does not hide the failover and there is no protocol extension for it *(Claude's call, C7)*.
- This is Goal 4: every event or an explicit error, never a silent gap.

Streaming send:

- A forwarded `SendStreamingMessage` (a continuation with affinity) works the same way.
- The first frame from A is the task (or the reply message), then events.

Backpressure (Goal 5), stage by stage:

| Stage | Rule |
|---|---|
| On A, publisher → subscriber | Today's bounded channel per subscriber (`TaskEventBroker.cs:64-69`, capacity 256). `Publish` uses `TryWrite` and never waits. A full channel drops only that subscriber (`TaskEventBroker.cs:44-47`). Its stream ends with the resubscribe error (reason `SUBSCRIBER_TOO_SLOW`), not a bare `InternalError`. |
| A → B, internal HTTP/2 | Each proxied subscription is its own HTTP/2 stream. HTTP/2 flow control is per stream, so a slow stream fills only its own window. `SocketsHttpHandler.EnableMultipleHttp2Connections = true`. |
| On B, proxy → client | B reads A's stream into a bounded channel per client (capacity 256). When full, B drops that client (resubscribe error) and cancels the upstream request. A then drops its subscriber. |
| Per task and per pod | `MaxSubscribersPerTask` and `MaxActiveStreams` from package 09 count proxied streams on both pods. |

No stage ever blocks a publisher or a different subscriber. This is the a2a-go #395 failure mode, now tested directly. The local event bus keeps these rules when it moves behind `ITaskEventBus`; the cluster test suite runs the same cases against the fake Redis backend.

### 7.8 Internal endpoints and pod-to-pod authentication

Endpoints:

- Mapped only on `InternalPort`, with `RequireHost("*:8090")` *(writer's call)*.
- `MapA2A` never exposes them on public ports. A test checks that a request to the public port for an internal path is 404.

| Endpoint | Purpose |
|---|---|
| `POST /_a2a/cluster/v1/cancel` | forwarded cancel |
| `POST /_a2a/cluster/v1/subscribe` | forwarded subscribe (internal SSE response) |
| `POST /_a2a/cluster/v1/send` | forwarded continuation, unary |
| `POST /_a2a/cluster/v1/send-stream` | forwarded continuation, streaming |

The body is `{ call: ClusterCallContext, request: <A2A request model> }`, serialized with source-generated JSON. A version prefix in the path allows a rolling upgrade: a pod accepts the current and the previous wire version.

What the receiving pod checks:

1. The pod credential (below). Any failure is 401 and the request is never executed.
2. `HopCount` is 1.
3. It rebuilds `A2ACallContext` from `ClusterCallContext` and marks it `IsForwarded = true`.

Middleware on forwarded calls *(Claude's call, B5)*:

- The full `IA2AMiddleware` pipeline (package 09 §7.11: authorization, owner, tenant, version, extensions, limits) runs only on the pod that received the client request. Forwarding happens after the pipeline, as the request handler's last step.
- The owner pod runs only peer authentication, tracing and the request handler. It does not run the pipeline again. Handlers and middleware can read `IsForwarded`.
- Response metadata (for example the `A2A-Extensions` echo) comes from the receiving pod, which computed it in its pipeline.
- This keeps the anti-goal: no check is skipped, because every check ran once on the receiving pod.

Pod credential *(Claude's call, C6)*: cluster mode ships Entra workload identity only, in the separate package `Ambinder.A2A.Cluster.Entra`. `Ambinder.A2A.Cluster` holds only the seam `IClusterPeerAuthentication` (attach a credential to an outgoing internal call; validate an incoming one), so it has no Azure dependency.

- **Entra workload identity** (`AddEntraPodAuthentication()`, `Ambinder.A2A.Cluster.Entra`):
  - Each pod gets a token from Azure.Identity `WorkloadIdentityCredential` for the agent's own app ID URI, with a dedicated app role `A2A.Cluster.Peer`.
  - The receiver validates it with package 07's validation and requires that role.
  - Tokens are cached and refreshed by the credential.
- **`SharedKey`** exists only in test code (the cluster test harness, §7.13), as a test implementation of `IClusterPeerAuthentication`: an HMAC-SHA256 over method, path, timestamp and body hash. It does not ship. Local multi-instance tests use it. A team without Entra cannot run cluster mode in 1.0 unless it writes its own `IClusterPeerAuthentication`.

Network:

- The sample's `NetworkPolicy` allows `InternalPort` only from pods with the same app label.
- Transport is cleartext h2c inside the cluster by default. If the cluster runs a service mesh with mTLS, that covers encryption. Otherwise `InternalTls` (certificate from a Secret) can be turned on.
- With pod-IP addresses, a TLS certificate must name the IPs or the receiving side must skip hostname checks against a pinned certificate. The docs explain both. *(writer's call: the default stays mesh or cleartext plus authenticated requests)*

### 7.9 Crash recovery

The mechanism is package 09's `TaskRecoveryService`. It already runs on every pod and claims each orphaned task (runner lease expired) and each released task (`ListReleasedAsync`, `UpdatedAt` older than the lease length, B4) with a conditional update, so exactly one pod recovers it. A recovered run gets the reduced caller stored with the task (05 `StoredTask.Caller`, B8), never a token. This package adds:

- After a takeover, the new runner's id is in the task. Later cancels and subscribes route to it.
- Proxied streams of the dead pod's tasks end through §7.7's upstream-failure path. They re-read the task, find it either final (`Fail` policy) or running elsewhere (`Restart` policy), and answer the client accordingly.
- Fencing: if the "dead" pod was only paused and wakes up, its next write fails on the version check. Package 05's writer rule then stops its run. Its open subscriptions end with the resubscribe error.
- Takeover time: up to lease length (20 s) + sweep interval (10 s) = 30 s worst case (Goal 6).

### 7.10 Graceful scale-down

This extends package 09's shutdown sequence through its `IA2AShutdownParticipant` hook:

1. Package 09 marks the instance `Draining`. Other pods stop sending new continuations to it (§7.5), but still forward cancel and subscribe for tasks it runs.
2. Proxied streams for which this pod is the proxy (client here, task elsewhere) end at once with the resubscribe error, because the client should reconnect through a pod that stays. The upstream requests are cancelled.
3. Proxied streams for which this pod is the runner stay open while its runs finish within the grace period.
4. When 09 releases or fails the remaining runs, the subscribers on other pods get either the final event (`Fail`) or the stream end and the re-read rule (`Restart`). A released task has `Runner = null` and stays `WORKING`. The recovery sweep on any pod picks it up with package 05's `ListReleasedAsync` once its `UpdatedAt` is older than the lease length, the same rule as 09 *(Claude's call, B4)*.
5. The internal listener closes last, after the runs are settled. Then package 09 deletes the instance lease.

The sample sets a `PodDisruptionBudget` and a `preStop` sleep of 5 s matching 09's `DrainDelay`. The docs give the timing formula from package 09.

### 7.11 Health and telemetry

- `a2a-cluster` health check (tag `ready`, via 09): unhealthy when this pod's own lease heartbeat failed for more than half the lease length. Degraded when more than half of the known peers have expired leases.
- Metrics (meter `Ambinder.A2A.Server`, the name after package 01's rename):
  - `a2a.server.cluster.forwards` (counter; tags `a2a.operation`, `outcome`: `ok`, `not_runner`, `failed`)
  - `a2a.server.cluster.proxied_streams` (up-down counter)
  - `a2a.server.cluster.stream_interrupts` (counter; tag `reason`)
  - `a2a.server.cluster.takeovers` (counter)
- Spans: a forwarded call is a client span `a2a.cluster.forward <Operation>` on the sending pod. W3C trace context goes in the internal request, so one trace covers both pods. The existing "never recorded" rules (`docs/production.md` §6) apply: no tokens and no message content.

### 7.12 Changes to current code

- `TaskEventBroker` becomes the internal local implementation of `ITaskEventBus`. `Publish` takes the task `Sequence`. The class stops being public (R6).
- `TaskManager`:
  - calls `ITaskRouter` for cancel, subscribe and continuation
  - calls `ITaskForwarder` when the route is `Forward`
  - calls the local path otherwise
  - Its `_running` map stays local (a run lives on one pod), and `_cancelling` stays local plus the store flag.
- `TaskWriter.CommitAsync` publishes through `ITaskEventBus` with the sequence from package 05's write.
- Without `AddA2ACluster()`:
  - the router is `LocalTaskRouter` (always local, no store reads)
  - the forwarder is not registered
  - nothing else changes (Goal 10)
- `docs/production.md` §8 ("Run exactly one instance") is replaced by the cluster docs.

### 7.13 Test cluster

`ClusterTestHarness` (in the test projects; package 12 may publish it later; it uses the test-only `SharedKey` pod auth, C6) is built on package 12's `A2ATestServer.StartClusterAsync` in Kestrel mode (port 0), which gives the shared-store hosts; this package adds the forwarding wiring. It starts N in-process hosts on Kestrel with random ports. They share:

- a PostgreSQL container (or SQLite file for the fast suite)
- a `FakeTimeProvider` where possible

The harness offers:

- a random-routing HTTP client
- fault injection: kill a host, cut one internal connection, slow one subscriber

The TCK runs against it through a small round-robin proxy. A nightly job runs the `deploy/aks/overlays/cluster` sample on `kind` with 3 replicas and a chaos step (delete a pod every 30 s during the flow suite). The same job on a real AKS cluster is written but runs only when its secrets exist (owner decision O3); until then the `kind` job plus the manual pre-release checklist is the M2 exit run.

## 8. How it meets the six principles

1. **Protocol.** Subscribe gives snapshot-then-events in order on any pod. Closing one stream never affects others. Cancel is idempotent. Unauthorized tasks stay invisible because routing happens after the owner check. Where the spec is silent (stream interruption), the SDK documents its error reason. Check: the full TCK passes against the 3-pod harness with random routing.
2. **.NET enabler.** `AddA2ACluster()`, Options pattern, `IHttpClientFactory` for internal calls, Azure.Identity for pod tokens (in `Cluster.Entra` only), health checks, OpenTelemetry spans and metrics. Check: API review (02); XML docs on every public member.
3. **Easy.** One call plus a shared store and one auth setting. Every startup error names the fix. The sample overlay works with `kubectl apply -k`. Check: the docs "scale out" section is at most 10 lines of code and config changes from the single-pod sample.
4. **Performance.** Reads stay local. Forwarding adds one internal hop only for cancel, subscribe and continue. Per-stream flow control. Nothing at all without `AddA2ACluster()`. Check (package 14 scenarios):
   - forwarded cancel p50 under 5 ms on the local harness
   - proxied stream adds under 2 ms per event p95 and no per-event allocation beyond the frame buffer
   - single-pod scenarios unchanged *(targets are writer's call)*
5. **Modular.** Four seams in the core with local defaults; HTTP forwarding is one package; Redis can replace it. Check: the fake-Redis backend passes the cluster suite (Goal 9); a dependency test shows the core does not reference `Ambinder.A2A.Cluster`.
6. **Integration pains.** Cross-pod cancel and streaming, slow consumers, lost events and pod crashes are handled in the SDK, not by each team. Check: named tests for a2a-go #395 (slow subscriber), a2a-python #768 (lost events), and cancel on a non-runner pod.

## 9. Dependencies

Needs:

- **05**:
  - `StoredTask.Runner`, `CancelRequestedAt`, `Sequence`
  - `ITaskStore.ListByRunnerAsync`, `ListCancelRequestsAsync`, `ListReleasedAsync` (B4)
  - `StoredTask.Caller` (reduced caller, B8)
  - the EF Core store (cluster mode is EF Core only in 1.0, B3)
  - `IInstanceStore` and `InstanceLease.InternalAddress` and `Status`
  - conditional writes with `A2AStoreException(Conflict)` as fencing
  - `StoredTask.LastRunnerInstanceId`, for continuation affinity
  - the reference-counted lock table (PD8)
- **09**:
  - `A2AInstanceOptions`, instance heartbeat and `Draining` status
  - `TaskRecoveryService`, `RecoveryPolicy`
  - `IA2AShutdownParticipant`
  - the middleware pipeline (it runs on the receiving pod only; forwarding happens after it, B5)
  - the rolling-restart acceptance test, moved here from 09's M1 list (B1)
  - the `ready` health tag
  - heartbeats
  - `deploy/aks/` base
  - error reasons for busy and shutdown
- **07**: the reduced caller (serialized into `ClusterCallContext`, B8); token validation and app roles for Entra pod auth (in `Ambinder.A2A.Cluster.Entra`).
- **10**: automatic resubscribe on `TASK_STREAM_INTERRUPTED` and `SUBSCRIBER_TOO_SLOW`, so clients recover without code.
- **01**: packages `Ambinder.A2A.Cluster` and `Ambinder.A2A.Cluster.Entra` (01's map confirms the four seams sit in the core).
- **12**: `A2ATestServer.StartClusterAsync` as the base of `ClusterTestHarness`; may publish `ClusterTestHarness`.
- **14**: `Pipeline` scenarios through `ITaskEventBus`; forwarded-call scenarios.

Provides:

- **Core** (all packages): `ITaskEventBus`, `TaskSubscription`, `ITaskRouter`, `ITaskForwarder`, `TaskRoute`, `PeerInstance`, `ClusterCallContext`, `A2ACallContext.IsForwarded` (B5).
- **10**: stream error reasons `TASK_STREAM_INTERRUPTED`, `SUBSCRIBER_TOO_SLOW` (in `ErrorInfo`).
- **13**: "Scale out" and "Cluster troubleshooting" docs input.
- **Later Redis package**: the seams and the cluster test suite to pass.

## 10. Acceptance criteria

- [ ] Seams exist in the core as in §7.2. `TaskEventBroker` is internal behind `ITaskEventBus`. Without `AddA2ACluster()`, package 14's single-instance scenarios are within budget.
- [ ] `Ambinder.A2A.Cluster` ships `AddA2ACluster()`, internal endpoints on `InternalPort` only, the `IClusterPeerAuthentication` seam, the streaming proxy, durable cancel polling and the `a2a-cluster` health check. `Ambinder.A2A.Cluster.Entra` ships Entra workload identity pod auth. `SharedKey` exists only in test code (C6).
- [ ] Cluster mode with the Cosmos store fails at startup with the documented message (B3).
- [ ] A forwarded call runs the middleware pipeline once, on the receiving pod; the owner pod runs only peer auth, tracing and the handler, with `IsForwarded = true`; the `A2A-Extensions` echo comes from the receiving pod (B5).
- [ ] A task released at shutdown is recovered by the sweep after its `UpdatedAt` is older than the lease length, not before (B4).
- [ ] Cluster suite on the 3-pod harness (PostgreSQL), each test also on the fake-Redis backend where it applies:
  - [ ] TCK passes with random routing
  - [ ] cancel on a non-runner pod (fast path; durable path with the internal connection cut)
  - [ ] subscribe on a non-runner pod: 1,000 events in order, no gap or duplicate
  - [ ] slow subscriber: one stalled client, 50 others on 2 pods get each event within 100 ms
  - [ ] runner killed mid-stream: proxied clients get the final event (`Fail`) or `TASK_STREAM_INTERRUPTED` (`Restart`), within 30 s
  - [ ] paused runner (fencing): its later write fails; its run stops; no double completion
  - [ ] continuation affinity: goes to the previous runner when alive, local when it is draining or gone
  - [ ] racing continuations on two pods: exactly one runs
  - [ ] rolling restart of 3 pods during 20 streaming tasks: no task left non-final without a live runner after 60 s
  - [ ] internal endpoint without credentials → 401, not executed; internal path on a public port → 404
  - [ ] forward loop impossible: `HopCount` 2 is rejected
- [ ] Nightly `kind` job with 3 replicas and pod deletion every 30 s passes the flow suite.
- [ ] M2 exit: the `kind` job passes, and the real-AKS job with Entra workload identity pod auth is written. It runs only when its secrets exist and is skipped visibly otherwise; until then the manual pre-release checklist covers it (O3). When it runs, the result is linked in the release notes.
- [ ] `deploy/aks/overlays/cluster/`: 3 replicas with a rolling update strategy, headless Service, internal port, `NetworkPolicy`, `preStop`, PDB, HPA, downward-API env (09's M1 base is single-replica, B1).
- [ ] Docs (13): "Scale out on AKS", "How the cluster routes calls", "Streams across pods and what clients see", "Pod-to-pod authentication", "Cluster troubleshooting". Until 1.0 the hosting docs say "do not scale out before 1.0" (B1); this package replaces that line with the scale-out guide. `docs/production.md` §8 is replaced.

## 11. Left to the builder

- The internal wire format details (field names, frame layout), as long as it is versioned and source-generated.
- Instance-table cache structure and its refresh timing within the heartbeat interval.
- Whether B's re-read after an upstream failure uses a short backoff before deciding "not final".
- HTTP client settings for the internal client (connection lifetime, ping), within `IHttpClientFactory`.
- Log event ids and messages, following the "never recorded" rules.
- The fake-Redis backend's internals (an in-process shared bus is enough).

## 12. Open questions

None. Pod-to-pod auth and where it lives (Q1, Q4) are Claude's call C6 (§7.1, §7.8). Continuation affinity and stream interruption (Q2, Q3) are Claude's call C7 (§7.5, §7.7).
