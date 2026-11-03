# SDK 09: Hosting on AKS

Date: 2026-11-03
Status: approved by owner 2026-11-03
Milestone: M1 (`1.0.0-preview`)
Size: L

Items marked *(writer's call)* were chosen by the spec writer, not the owner. Items marked *(Claude's call)* were decided by Claude after review.

## 1. Intent

Today the server works on one machine on fixed ports. It breaks behind an ingress:

- gRPC picks the agent by listening port.
- Kestrel binds to the port of the public URL.
- Streams have no keep-alive.
- Rolling deployments orphan tasks.
- Nothing limits load.

This package makes one pod production-ready on AKS (Azure Kubernetes Service) and puts the tedious hosting work into library helpers:

- routing and URLs behind ingress
- graceful shutdown and task recovery
- SSE heartbeats
- limits and rate limiting
- health checks, TLS and forwarded headers
- dynamic and per-request agent cards
- a flexible route layout
- the general server middleware pipeline that principle 5 promises (the controller assigned it here; 03, 07 and 11 hook into it)

It also ships a sample AKS deployment for the docs. Scale-out across pods is package 06, which builds on the shutdown and recovery made here.

## 2. Findings addressed

- **R21** gRPC routes agents by listening port; Kestrel binding comes from the public URL; gRPC is h2c only; the card's gRPC URL is wrong behind ingress.
- **R22** No graceful shutdown or recovery of running tasks.
- **R23** No backpressure, concurrency limits or rate limiting; gRPC has no size limit; no limits on parts, history or artifacts.
- **R24** SSE streams send no heartbeats.
- **R25** Health checks, TLS, HSTS and forwarded headers are left to samples.
- **R26** Cards are static; route layout is fixed. (`Cache-Control` and `ETag` are package 03's D7; this package extends them to per-request card variants.)
- Principle 5 "middleware on the server": no pipeline exists today; `TracingRequestHandler` is a hand-written decorator (`A2AServiceCollectionExtensions.cs:54`). Package 11 asked for it (settled: this package owns it).
- **R7** (hosting part) `AddA2AServer` always calls `services.AddGrpc()` (`A2AServiceCollectionExtensions.cs:56`).
- Pain point **"Agent Card fetching, discovery, and per-request cards"** (B): per-request cards (a2a-dotnet #353), ingress and h2c surprises, caching.
- Pain point **"Streaming (SSE) robustness"** (B): heartbeats, buffering headers, clear end of stream.
- Pain point **"Version negotiation and mixed-version peers"** (B, card part): `Vary: A2A-Version` on the card.
- Pain point **"Binding differences … URL and tenant handling"** (B): agent routing from trusted state, interface tenant.

## 3. Goals

1. One gRPC listener serves every agent. The agent is chosen from the request (interface `tenant`, or host). Routing by listening port is removed from the server code (R21), so the server runs on `TestServer` with no ports (package 12). Tested by an integration test with 4 agents on 1 gRPC port, and by the same test on `TestServer`.
2. The pod's listening ports are independent of `PublicBaseUrl`. Tested by a test that listens on 8080/8081 while cards show `https://agents.contoso.com` and `agents-grpc.contoso.com:443`.
3. On SIGTERM, the pod:
   - stops taking new tasks after a drain delay
   - lets running tasks finish within a grace period
   - leaves no task stuck in `WORKING`, either by failing it or by releasing it for restart
   Tested by a test that stops the host mid-run and checks every task's final state.
4. After a crash, a restarted pod finds the tasks the dead process was running and applies the agent's recovery policy within 30 s. Tested with SQLite and a killed child process.
5. SSE streams send a comment heartbeat at least every 15 s, including before the first event. Tested with a fake clock: no idle gap above the interval.
6. Per-pod and per-agent execution limits, per-owner concurrency limits and ASP.NET Core rate limiting work on all three transports and give clear errors. Tested per transport.
7. `AddA2AHealthChecks()` and `MapA2AHealthChecks()` give liveness, readiness and startup probes. Readiness fails while draining or when the store is down. Tested.
8. Agent cards can be built per request (host, caller, tenant) through a per-request card factory. The result still runs through package 03's `IAgentCardContributor`s and caching, with one cached, signed variant per variant key. Tested.
9. Routes can be mapped under a prefix, per agent or with one agent at the root. Tested by route tests for each layout.
10. A sample AKS deployment (manifests) runs the sample server (1 replica, `strategy: Recreate` in M1, B1) with ingress for HTTP and gRPC, probes and workload identity. Tested by a CI job on a local `kind` cluster that applies the manifests and runs the client flows.
11. A server middleware pipeline runs around every A2A operation, the same on JSON-RPC, REST, gRPC and in-process calls, for unary and streaming operations. Tracing, limits, 07's authorization checks and 11's extension hooks are built on it. Tested by a test middleware that sees all 11 operations on all 3 transports in the same order.
12. Every time-dependent part of the server reads `TimeProvider` from DI:
    - heartbeats, leases, recovery, drain timers
    - card cache max-age
    - limits windows owned by the SDK
    - status timestamps (already, `TaskManager.cs:52`)
    Tested by running the shutdown, recovery and heartbeat tests with `FakeTimeProvider` and no real waits. Package 02's banned-API analyzer (`DateTime.UtcNow` and friends banned in `src/`) enforces it.

## 4. Non-goals

- Forwarding cancel, subscribe and continue to the pod that runs a task. That is package 06 (it reuses the shutdown and recovery from here).
- Entra authentication, owner and tenant resolution. That is package 07 (this package only reads the owner key for rate-limit partitions).
- Card signing. That is package 04 (it signs once per card version; here, once per variant).
- Card HTTP caching rules (`ETag`, `Cache-Control`, `Vary: A2A-Version`, `304`) and the `IAgentCardContributor` seam. Those are package 03 (D7). This package only adds variant keys and `Vary: Host`.
- A2A extension hooks. That is package 11; they run as middleware from this package.
- Push delivery during shutdown. That is package 08.
- Store contract and lease fields. That is package 05.
- Client-side retries and resubscribe. That is package 10.
- Hosting outside AKS as a tested target (work plan "Out of scope"). The helpers must not depend on AKS or Kubernetes APIs.
- A Helm chart *(writer's call: plain manifests with Kustomize overlays are easier to read in docs; a chart can come later)*.

## 5. Anti-goals

- Any helper that silently trusts every forwarded header or every host.
- A heartbeat or limit that changes A2A payloads (heartbeats are SSE comments only).
- A shutdown that waits longer than Kubernetes allows and then gets SIGKILLed with tasks still `WORKING`.
- Recovery that re-runs an agent that did not opt in to being re-run.
- Default settings that need edits for `dotnet run` on a laptop.
- Making the core server depend on gRPC, health check packages or Kubernetes client libraries.
- A second card hook that competes with 03's `IAgentCardContributor`.
- A middleware pipeline that behaves differently per transport, or that can be skipped by calling `TaskManager` directly from a transport.

## 6. Constraints

- Work plan dependency "09 → 06": graceful shutdown and task recovery come before cross-pod forwarding; package 06 extends them.
- Work plan out of scope: hosting outside AKS is not a tested target, but the SDK must not depend on AKS.
- Principle 3: nothing to configure for local development; every error says what to do.
- A2A v1.0.1, `AgentInterface` (`a2a.proto:336-355`):
  - `url` for gRPC is "hostname:port" (main comment, #1997, after v1.0.1)
  - `tenant` is "an opaque string used for routing requests to a specific agent or tenant when multiple agents are served behind a single A2A endpoint"
  - clients MUST send it in every request
- A2A v1.0.1 streaming (research-spec §3 "Streaming"):
  - SSE content type `text/event-stream`
  - events in order to all streams
  - closing one stream MUST NOT affect others
- A2A v1.0.1 agent card (research-spec §3 "Agent card"):
  - well-known path `/.well-known/agent-card.json`
  - SHOULD send `Cache-Control` max-age and `ETag`
  - clients SHOULD use `If-None-Match`
- A2A v1.0.1 security (research-spec §3 "Security"): TLS in production MUST; SHOULD rate-limit.
- A2A v1.0.1 `SendMessage`: blocking MUST wait until terminal or interrupted (research-spec §3 "SendMessage").
- Package 05 contract: `StoredTask.Runner`, `RunAttempt`, `CancelRequestedAt`, `IInstanceStore`, `ITaskStore.ListByRunnerAsync`.
- Known proxy limit: NGINX ingress cuts idle streams after 60 s by default (R24).
- M1 deployments are single-replica only *(Claude's call, B1)*. Scale-out is package 06 in M2. The docs say "do not scale out before 1.0".
- No cloud resources for now (owner decision O3). Tests that need a real AKS cluster or Entra tenant are written but run only when their secrets exist, and are skipped visibly otherwise. Until then `kind`, fakes and the manual pre-release checklist meet the exit criteria.
- Test harness *(Claude's call, B2)*: in M1 this package uses the repo-internal slice of package 12's harness (`A2ATestServer` on `TestServer`, `FakeTimeProvider` wiring, `TaskEventLog` waiters). No new M1 test uses fixed ports or sleeps.

## 7. Design

### 7.1 Assemblies (package IDs from 01's package map)

| Assembly | Contents |
|---|---|
| `Ambinder.A2A.Server` (core, no web host) | Execution host (limits, shutdown, recovery) on `Microsoft.Extensions.Hosting` abstractions, middleware pipeline, per-request card factory types, limit options. No ASP.NET Core reference; 01's dependency allow-list test enforces it. |
| `Ambinder.A2A.AspNetCore` | `MapA2A` layouts, SSE heartbeats, card endpoint variants, rate-limit integration, `AddA2AHealthChecks`, forwarded headers, TLS and Kestrel helpers, graceful-shutdown wiring. |
| `Ambinder.A2A.AspNetCore.Grpc` (opt-in) | `AddA2AGrpc()`, `MapA2AGrpc()`, gRPC agent routing. `AddA2AServer` no longer calls `AddGrpc()`. |
| `deploy/aks/` (repo, not a package) | Sample manifests. |

### 7.2 Binding versus public URLs (R21)

`A2AServerOptions` changes:

| Option | Default | Meaning |
|---|---|---|
| `PublicBaseUrl` | `""` | Public base for HTTP interface URLs, e.g. `https://agents.contoso.com`. Required outside Development unless `PublicUrlMode = FromRequest`. |
| `PublicUrlMode` | `Static` (Development: `FromRequest`) | `FromRequest` builds card URLs from the request's scheme and host (after forwarded headers), limited to `AllowedPublicHosts`. *(writer's call: makes `dotnet run` work with no config)* |
| `AllowedPublicHosts` | empty (Development: any loopback) | Hosts accepted in `FromRequest` mode. Any other host gets the `Static` URL or 400 if none is set. |
| `PublicUrls.JsonRpc` / `.Rest` / `.Grpc` | null | Per-interface overrides. `Grpc` is `host:port`, e.g. `agents-grpc.contoso.com:443`. |
| `GrpcUrlStyle` | `HostPort` | `HostPort` writes `host:port` (proto comment). `Url` writes `https://host:port` for peers that need it. *(writer's call; package 16 confirms against other SDKs)* |

`A2AHostingOptions` (new, section `A2A:Hosting`), used only by `ConfigureA2AKestrel()`:

| Option | Default | Meaning |
|---|---|---|
| `HttpPort` | 8080 | HTTP/1.1 + HTTP/2 listener (cleartext HTTP/2 only works with TLS + ALPN; see below). |
| `GrpcPort` | 8081 | gRPC listener. HTTP/2 only (h2c) when no certificate; HTTP/1.1+2 with TLS. One port for all agents. |
| `BindAddress` | `0.0.0.0` (Development: loopback) | Address to listen on. |
| `Certificate` | none | When set (Kestrel certificate config or a path), both listeners use TLS. |

- `ConfigureA2AKestrel()` becomes optional. Without it, the app's own Kestrel config or `ASPNETCORE_URLS` is used, and gRPC needs an HTTP/2 endpoint the app sets up.
- It never reads ports from `PublicBaseUrl`. This changes today's behavior at `GrpcHostingExtensions.cs:26-34`.
- `A2AServerOptions.GrpcPorts` is removed, and so is routing by `http.Connection.LocalPort` (`A2AGrpcService.cs:128-135`). No server code reads the local port. Pre-1.0, so there is no obsolete period *(writer's call)*.

### 7.3 gRPC agent routing (R21)

`A2AGrpcOptions.Routing` *(writer's call)*:

| Mode | How the agent is picked | Card |
|---|---|---|
| `Auto` (default) | One agent registered → that agent. More than one → `Tenant` mode. | as the chosen mode |
| `Tenant` | The request's `tenant` field must equal an agent's routing value (default: the agent name). | gRPC interface has `tenant = "<routing value>"`, the shared gRPC URL. |
| `Host` | `:authority` host is looked up in `GrpcAgentHosts` (`"echo-grpc.contoso.com" → "echo"`). | gRPC interface URL is that host; no tenant. |

- The spec designed `AgentInterface.tenant` for exactly this routing case (`a2a.proto:345-350`), so `Tenant` mode needs no non-standard metadata.
- The routing value only selects the agent. The data tenant comes from package 07's resolver (token `tid`, route or config), never from this value. This follows the a2a-dotnet #539 point in research-painpoints #7 ("pick agents from trusted routing state").
- A seam allows other schemes: `IGrpcAgentRouter.Resolve(ServerCallContext, string? requestTenant) → string? agentName`.
- Unknown agent → `NOT_FOUND`, message "No agent for tenant 'x'. Use the tenant from the agent card's GRPC interface." The order of checks stays as documented at `A2AGrpcService.cs:15-21`: agent → policy → version → map → validate.
- gRPC limits: `MaxReceiveMessageSize` = `A2AServerOptions.MaxRequestBodyBytes` (4 MB), `MaxSendMessageSize` = 16 MB *(writer's call)*. Today gRPC uses defaults (R23).
- With REST and JSON-RPC, agents stay in the path. The REST `/{tenant}` prefix keeps its current meaning (`RestEndpoints.cs:22-23`).

### 7.4 Route layout and per-request cards (R26)

`MapA2A(Action<A2ARouteOptions>? configure = null)`:

| Option | Default | Meaning |
|---|---|---|
| `Prefix` | `""` | Path prefix for all A2A routes, e.g. `/a2a`. |
| `Layout` | `PerAgent` | `PerAgent`: `{prefix}/agents/{agent}/jsonrpc`, `{prefix}/agents/{agent}/rest/...`, `{prefix}/agents/{agent}/.well-known/agent-card.json` (today's paths). `SingleAgent`: JSON-RPC at `POST {prefix}/`, REST at `{prefix}/message:send` and so on, card at `/.well-known/agent-card.json`. |
| `AgentSegment` | `agents` | The literal in `PerAgent` paths. |
| `WellKnownCardAgent` | `A2AServerOptions.DefaultAgent` | Agent whose card is at the root well-known path. |

- `SingleAgent` requires exactly one agent; otherwise startup fails with what to do.
- App routes such as `/health/*` take precedence over the REST catch-all, because ASP.NET Core routing prefers literal segments. A route test covers it.
- The push JWKS route moves to package 08.

Cards: one build pipeline, with per-request input. This package does not add a second card hook. It extends the existing card factory with request context, and keeps package 03's `IAgentCardContributor` as the only place where other packages change card JSON.

```csharp
// New AddAgent overload: the card factory gets the request context. The old Func<IServiceProvider, AgentCard> stays.
a2a.AddAgent<THandler>(name, (IServiceProvider sp, AgentCardRequest req) => AgentCard, o => o.CardVariesBy = CardVariesBy.Host | CardVariesBy.Tenant);

public sealed record AgentCardRequest(
    string AgentName, AgentCardKind Kind,            // Public | Extended
    A2ACaller? Caller,                                // package 07; null for anonymous public cards
    string? Tenant,                                   // from 07's ITenantResolver, when resolved
    Uri PublicBaseUrl,                                // after PublicUrlMode
    string Host);

[Flags] public enum CardVariesBy { None = 0, Host = 1, Tenant = 2, Caller = 4 }
```

Build steps for one card request *(writer's call on the order; 04 and 03 fix steps 4–6)*:

1. Compute the variant key from `CardVariesBy`: `(agent, kind, [host], [tenant], [caller owner])`. With `None` (the default, and the old overload) the key is just `(agent, kind, public base URL)`, which is today's static card.
2. On a cache hit, serve the cached bytes and ETag.
3. On a miss, call the agent's card factory with `AgentCardRequest`.
4. Fill `supportedInterfaces` from the public URLs (§7.2, §7.3), as today (`A2AServerBuilder.cs:47-48`).
5. Run the `IAgentCardContributor`s (03): security schemes from 07, 0.3 fields from 15, and others.
6. Sign (04: "once per card version", here once per variant), serialize, compute the ETag (03's rule: hash of the unsigned canonical card payload plus the signing key ids, so it is stable across pods and re-signing, B13).
7. Store in a bounded memory cache (1,000 variants, least recently used out *(writer's call)*). Entries expire after the card max-age, read through `TimeProvider`.

`AgentCardContext` (03's contributor input) gets the `AgentCardRequest` as a new nullable property, so a contributor can read the host or tenant if it needs to. The contributor rules of 03 stay: add members, do not change existing 1.0 members. Per-request changes to skills or descriptions belong in the card factory, not in a contributor.

HTTP headers follow 03 D7 (`ETag`, `Cache-Control: public, max-age=300`, `Vary: A2A-Version`, `304`). This package adds:

- `Vary: Host` when the key includes the host or `PublicUrlMode = FromRequest`.
- `Cache-Control: private` (same max-age) when the key includes the caller or tenant.
- The extended card keeps 03's `private, no-store` and is still cached in memory by variant.

Capability rules for the extended card stay as today (`TaskManager.cs:209-215`).

### 7.5 SSE heartbeats (R24)

`A2AServerOptions.Streaming`:

| Option | Default | Meaning |
|---|---|---|
| `HeartbeatInterval` | 15 s | Send `: keep-alive\n\n` when nothing was written for this long. `null` turns it off. |
| `CommitBeforeFirstEventAfter` | 15 s | If the first event has not come after this time, send SSE headers and a heartbeat anyway. A later error becomes an error frame instead of a plain error response. |
| `MaxStreamDuration` | null | Optional cap; the stream then ends with an error frame telling the client to resubscribe. |

- Today `SseStreaming.RunAsync` waits for the first event before it writes headers (`SseStreaming.cs:17-20`). A slow first LLM step would hit the 60 s ingress idle cut with no bytes sent. The new loop races the next event against a `PeriodicTimer` on one writer, so frames never interleave.
- The timer is `TimeProvider.CreateTimer` from DI, not `PeriodicTimer` on the system clock, so tests drive it with `FakeTimeProvider`.
- The headers stay: `Cache-Control: no-cache`, `X-Accel-Buffering: no` (`SseWriter.cs:19-21`).
- The clients already skip comment frames (`JsonRpcA2AClient.cs:106`, `RestA2AClient.cs:179`). A test proves both clients ignore heartbeats.
- gRPC has no A2A heartbeat message. Kestrel HTTP/2 keep-alive pings are set (`KeepAlivePingDelay` 30 s, `KeepAlivePingTimeout` 20 s) *(writer's call)*. The sample ingress raises the gRPC read timeout, because pings may not reset proxy idle timers. The docs say so plainly.

### 7.6 Limits, backpressure and rate limiting (R23)

`A2ALimitsOptions` (section `A2A:Limits`) *(defaults are writer's call)*:

| Option | Default | Enforced where | On breach |
|---|---|---|---|
| `MaxConcurrentExecutions` | 200 per pod *(Claude's call, C10)* | execution host | new task or continuation rejected: busy |
| `Agents[name].MaxConcurrentExecutions` | null | execution host | busy |
| `MaxConcurrentTasksPerOwner` | null (off) *(Claude's call, C10)*; configurable, the docs suggest 20 per pod as a start | execution host | busy |
| `MaxActiveStreams` | 2,000 per pod | streaming | busy |
| `MaxSubscribersPerTask` | 16 | broker | busy |
| `MaxPartsPerMessage` | 100 | core validation | `InvalidParams` |
| `MaxHistoryMessages` | 1,000 | core, on write | oldest messages trimmed from stored history; logged once per task |
| `MaxArtifactBytesPerTask` | 32 MB | core, on agent event | the event fails as `InvalidAgentResponse`; the task is failed |
| `MaxBlockingWait` | null | blocking `SendMessage` | when set: return the current (non-final) task. This is a documented deviation from the spec MUST, so it is off by default. |
| `MaxRequestBodyBytes` | 4 MB (existing) | all three transports | 413 / `RESOURCE_EXHAUSTED` |

"Busy" is rejected before any task is created:

- HTTP 503 with `Retry-After: 5` and a JSON-RPC error or REST `google.rpc.Status` body. Code `InternalError` is used, because A2A has no busy code. The `ErrorInfo` reason is `SERVER_BUSY`.
- gRPC `UNAVAILABLE` with the same `ErrorInfo`.
- The spec is silent on overload, so the docs record this as the SDK's choice (principle 1) *(writer's call)*.

Rate limiting:

- `AddA2ARateLimiting(o => o.PerOwner(permitLimit: 60, window: TimeSpan.FromMinutes(1)))` registers an ASP.NET Core rate limiter policy `"a2a"`.
- The partition key is computed in ASP.NET Core before the body is read, from token claims only: tenant id plus app id (app-only) or user object id (delegated), the same claims `OwnerKey` uses (package 07). Anonymous callers partition by client IP. It does not depend on `A2ACaller` or `ITenantResolver` *(Claude's call)*. ASP.NET Core's limiters keep their own clock; that is the one documented exception to Goal 12.
- `MapA2A` and `MapA2AGrpc` apply the policy when it is registered. The app adds `UseRateLimiter()` after `UseAuthentication()`, and startup warns when the order is wrong.
- Rejected calls get HTTP 429 with `Retry-After` and reason `RATE_LIMITED`, or gRPC `RESOURCE_EXHAUSTED`.
- Cards and health endpoints are excluded.
- Off by default *(writer's call: product teams choose numbers; the docs give a starting point)*.

### 7.7 Execution host, graceful shutdown and recovery (R22)

Execution host:

- A new internal `AgentExecutionHost` (registered as `IHostedService`) owns every agent run. It replaces `Task.Run(..., CancellationToken.None)` at `TaskManager.cs:296`.
- It tracks runs, applies the concurrency limits above, and gives each run a token linked to cancel and to shutdown.

Instance identity:

- `A2AInstanceOptions.InstanceId` defaults to `{POD_NAME or MachineName}-{6 random chars}`, new on each process start *(writer's call: a container restart in the same pod is a different instance)*.
- At start, the instance writes an `InstanceLease` (package 05) and renews it every `HeartbeatInterval` (5 s). The lease lasts `LeaseDuration` (20 s).
- New and continued runs set `StoredTask.Runner = (InstanceId, now)` in the same write that sets `SUBMITTED`/`WORKING`. On a final or interrupted state, the core clears `Runner`.

Recovery policy per agent:

```csharp
a2a.AddAgent<ReportAgent>("report", card, o => o.Recovery = RecoveryPolicy.Restart(maxAttempts: 2));
```

- `RecoveryPolicy.Fail` (default, kept *(Claude's call, C10)*) marks the task `FAILED` with an agent message: "The agent server stopped before this task finished. Send the message again."
- `RecoveryPolicy.Restart(maxAttempts)` runs `ExecuteAsync` again on the same task. `AgentRequestContext.Recovery` is set (`Attempt`, `PreviousInstanceId`) and `StoredTask.RunAttempt` goes up. When the attempts are used up, the task fails as above.
- `Restart` is opt-in per agent *(Claude's call, C10)*. An agent class marked with the `[IdempotentAgent]` attribute gets `Restart(1)`. An explicit `o.Recovery` on `AddAgent` wins over the attribute. Agents must be idempotent to use `Restart`. The docs say this plainly.
- A recovered run gets the reduced caller stored with the task (05 `StoredTask.Caller`: owner key, tenant id, app id, user object id, roles and scopes), never an access token. On-behalf-of does not work in a recovered run *(Claude's call, B8)*.

Shutdown sequence on `ApplicationStopping` (`A2AShutdownOptions`) *(timings are writer's call)*:

1. Mark the instance `Draining` (lease row). Readiness returns 503.
2. For `DrainDelay` (5 s), keep accepting new tasks, because Kubernetes removes the pod from endpoints a little after readiness fails. After that, new tasks and continuations get "busy" (503 / `UNAVAILABLE`). Reads, cancel and subscribe still work.
3. Wait up to `ExecutionGracePeriod` (20 s) for running executions to end.
4. For each run still going, apply the agent's policy:
   - `Fail`: cancel the token, commit `FAILED`.
   - `Restart`: cancel the token with reason "shutdown" and commit `Runner = null` (state stays `WORKING`). The task is now released. The recovery sweep (below) picks it up once its `UpdatedAt` is older than the lease length *(Claude's call, B4)*.
5. Streams of released tasks end:
   - SSE: one error frame (`InternalError`, reason `SERVER_SHUTTING_DOWN`, "resubscribe")
   - gRPC: status `UNAVAILABLE`
   - streams of failed tasks get the normal final event
6. Delete the instance lease.

Rules for these settings:

- Startup checks `DrainDelay + ExecutionGracePeriod + 3 s ≤ HostOptions.ShutdownTimeout`, and fails with what to change.
- The docs state the matching `terminationGracePeriodSeconds` (sample: 35 s; agents with long runs raise all three).
- The core sets `HostOptions.ShutdownTimeout` to 30 s unless the app has set it.

Recovery sweep (`TaskRecoveryService`, hosted, every `RecoveryInterval` = 10 s):

1. Read `IInstanceStore.ListExpiredAsync(now - ClockSkewAllowance)` (5 s allowance).
2. For each expired instance, page through `ITaskStore.ListByRunnerAsync(instanceId)`. Claim each task by a conditional update that sets `Runner` to self (or `null` for `Fail`) and applies the policy. A version conflict means another pod got there first; skip it.
3. Released tasks are also swept: `ITaskStore.ListReleasedAsync(now - LeaseDuration, limit)` (05) returns tasks with `Runner = null` in `SUBMITTED`/`WORKING` whose `UpdatedAt` is older than the lease length. Package 06 uses the same rule *(Claude's call, B4)*.
4. When an expired instance has no tasks left, delete its lease row.

With one pod, the sweep handles the previous process's tasks after a restart. With many pods (06), any pod can do it. The conditional update makes it safe. With the in-memory store, nothing survives a restart, so the sweep only covers released tasks.

Fencing: if a pod that was thought dead writes again, its write fails with `Conflict`, because the version changed. Package 05's writer rule then stops that run.

### 7.8 Health checks (R25)

`services.AddA2AHealthChecks()` registers checks with tags:

| Check | Tags | Healthy when |
|---|---|---|
| `a2a-startup` | `startup` | the agent catalog is built, the store schema check passed (05), the instance lease is written |
| `a2a-draining` | `ready` | the host is not stopping |
| `a2a-store` | `ready` | `IStoreHealthProbe` (05) answers within 2 s; in-memory store is always healthy |
| `a2a-capacity` | `ready` (Degraded only) | running executions are below 90 % of the limit |
| other packages | `ready` | 08 adds the outbox backlog check and 06 adds the cluster peer check, through the same tag |

- `app.MapA2AHealthChecks()` maps `/health/live` (no checks: the process answers), `/health/ready` and `/health/startup`. Paths are options.
- These endpoints skip auth, rate limiting and A2A version checks.
- This replaces the sample-only wiring at `samples/AgentServer/AgentServerApp.cs:59-64`.

### 7.9 TLS, HSTS and forwarded headers (R25)

`A2AHostingOptions.Tls` *(writer's call)*:

- `TerminatedAtProxy` (AKS default in the sample):
  - requires forwarded headers to be configured
  - requires `PublicBaseUrl` or every allowed public host to be `https`
  - enables HSTS (`max-age` 1 year)
  - leaves HTTPS redirection off (the proxy owns it)
- `Kestrel`: `Certificate` is required; enables HSTS and HTTPS redirection.
- `None`: allowed only in Development. In any other environment, startup fails with "A2A requires TLS in production (A2A spec §7). Set A2A:Hosting:Tls to TerminatedAtProxy or Kestrel."

`services.AddA2AForwardedHeaders(o => …)`, section `A2A:Hosting:ForwardedHeaders`:

- `TrustedProxies` (IP list) and `TrustedNetworks` (CIDR list). Empty means forwarded headers are not used. There is no "trust all" switch.
- It processes `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. The host is accepted only if it is in `AllowedPublicHosts`.
- `app.UseA2AHosting()` adds, in order: forwarded headers, HSTS / HTTPS redirection per the TLS mode, and a check that the request's scheme is `https` outside Development (else 400, "Use https").

### 7.10 Sample AKS deployment

`deploy/aks/` with Kustomize *(writer's call)*:

- `base/`:
  - `Deployment` for `agent-server`:
    - 1 replica with `strategy: Recreate` in M1 *(Claude's call, B1)*; ports 8080 (http) and 8081 (grpc)
    - probes on `/health/live`, `/health/ready`, `/health/startup`
    - `terminationGracePeriodSeconds: 35`
    - `POD_NAME` and `POD_IP` from the downward API
    - resource requests and limits
    - non-root user, read-only root file system
  - `ServiceAccount` with the workload identity annotation and label (`azure.workload.identity/use: "true"`) for Entra (07, 10)
  - `Service` (ClusterIP) for both ports
  - no `PodDisruptionBudget` and no `HorizontalPodAutoscaler` in M1; package 06's `overlays/cluster/` adds both with more replicas in M2 (B1)
  - `ConfigMap` for `A2A__*` settings
  - `NetworkPolicy`: ingress only from the ingress controller namespace
- `overlays/single-instance/`: in-memory store (M1 shape; the base is already single-replica).
- `overlays/postgres/`: connection string from a Secret (or Key Vault CSI), store set to PostgreSQL. Package 06 adds `overlays/cluster/` (cluster port, optional headless service).
- `overlays/otel/` (optional) *(Claude's call)*: sets `OTEL_EXPORTER_OTLP_ENDPOINT` to a collector the team runs. The SDK does not ship a collector. Package 13's production page points here.
- Data Protection keys *(Claude's call)*: not needed in M1 (single replica). In M2, `overlays/cluster/` configures a shared key ring (Azure Blob Storage, protected with a Key Vault key) so any pod can unprotect push credentials (package 08).
- Ingress manifests for two controllers, both in docs:
  - Gateway API: `HTTPRoute` for HTTP and `GRPCRoute` for gRPC. This is the primary, tested target: it is portable and works with Application Gateway for Containers *(Claude's call, C10)*
  - NGINX `Ingress` variant (the AKS application routing add-on uses NGINX), with:
    - `proxy-read-timeout: "3600"`
    - `proxy-buffering: "off"`
    - a second Ingress with `backend-protocol: "GRPC"` and a raised gRPC read timeout
  - The NGINX variant gets a "variant" section in the docs.
- TLS at the ingress with a certificate from Key Vault (docs show the steps; the manifests carry placeholders).
- `docs/aks.md` (written with 13): build and push the image, create the identity, apply the overlays, verify with the sample client, the line "do not scale out before 1.0" (B1), and a troubleshooting table (502 on gRPC means h2c/TLS mismatch; streams cut at 60 s means the timeout annotation is missing).

### 7.11 Server middleware pipeline

Principle 5 promises server middleware. This package owns it (controller decision), so 07 and 11 do not each build one.

```csharp
public delegate ValueTask<A2AOperationResult> A2AOperationDelegate(A2AOperationContext context);

public interface IA2AMiddleware
{
    ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next);
}

public sealed class A2AOperationContext
{
    public A2AOperation Operation { get; }            // SendMessage, SendStreamingMessage, GetTask, … (all 11)
    public A2ACallContext Call { get; }               // agent, caller, tenant, version, extensions, transport
    public object Request { get; set; }               // the typed request model; middleware may replace it
    public IDictionary<object, object?> Items { get; }
    public A2AResponseMetadata ResponseMetadata { get; } // headers (HTTP) / metadata (gRPC); written before the body
    public IServiceProvider Services { get; }         // request scope
    public CancellationToken RequestAborted { get; }
}

// Exactly one of Value (unary) or Events (streaming) is set.
public readonly struct A2AOperationResult
{
    public object? Value { get; }
    public IAsyncEnumerable<StreamResponse>? Events { get; }
}
```

Rules *(writer's call on the shape)*:

- **Where it runs.** After the transport has decoded the request, resolved the agent, run ASP.NET authentication and authorization and the version check (03's `IA2AVersionDispatcher` hands 0.3 requests to the same pipeline with `ProtocolVersion = "0.3"`). Before `TaskManager`. All three transports and in-process calls go through one `A2APipelineRequestHandler : IA2ARequestHandler`. No transport calls `TaskManager` directly.
- **Registration.** `a2a.UseMiddleware<T>()` adds it for all agents. `AddAgent<T>(…, o => o.UseMiddleware<T>())` adds it for one agent. It runs in registration order, global first. Middleware is resolved from the request scope.
- **Streaming.** Middleware sees streaming operations as `Events`. It may wrap the sequence (observe, map, stop with an `A2AException`) but must not buffer it. A throw before the first event becomes a plain error response. A throw after it becomes an error frame (as today, `SseStreaming.cs:34-37`).
- **Errors.** Middleware throws `A2AException` to reject. The transports map it as for any handler error.
- **Response metadata.** `ResponseMetadata` is written as HTTP response headers before the body or SSE headers, and as gRPC response headers. The extension middleware (03 D5, extended by 11) uses it to echo `A2A-Extensions`. Only allow-listed names (`A2A-*` and names the app registers) are sent.
- **Built-in middleware, in this order:**
  1. Tracing: today's `TracingRequestHandler` becomes middleware (no behavior change in spans).
  2. Limits: busy and per-owner checks (§7.6).
  3. 07's authorization checks, when 07 registers them: the agent-level requirement check, skill security requirements and the task continuation policy. Transports only authenticate (build the principal); they never authorize, so every check runs here once *(Claude's call)*.
  4. Extension negotiation: activation, the required-extension check and the echo (package 03 D5, in M1, from the card's declared extensions); package 11 extends this same step with `A2AExtension` request hooks in M2.
  5. App middleware.
- **Not for transport concerns.** Rate limiting and body size stay in ASP.NET Core, because they must run before the body is read.
- **Early rejections get no `a2a.server` span** *(Claude's call)*. A request rejected before the pipeline (authentication failure, `VersionNotSupported`) is not traced by step 1. It shows only as the ASP.NET Core request span with its HTTP status, enriched with `error.type` when telemetry is on.
- **Forwarded calls (06)** *(Claude's call, B5)*. The full pipeline runs only on the pod that received the client request. Forwarding to the owner pod happens after the pipeline. The owner pod runs only peer authentication, tracing and the request handler, and marks the context `A2ACallContext.IsForwarded = true`. Response metadata (for example the `A2A-Extensions` echo) comes from the receiving pod.
- **Cost.** With no app middleware the pipeline allocates nothing per call beyond the context object. Checked by package 14's benchmark against today's direct call: under 1 µs and one allocation added.

### 7.12 Migration from current code

- `ConfigureA2AKestrel()` no longer derives ports from `PublicBaseUrl`. `GrpcPorts` is gone: configs that set it fail at startup with "A2AServerOptions.GrpcPorts was removed. Use one gRPC port (A2A:Hosting:GrpcPort) and Tenant or Host routing."
- `TracingRequestHandler` becomes the first built-in middleware. `IA2ARequestHandler` is now `A2APipelineRequestHandler` around `TaskManager`.
- The card's gRPC URL changes from `{scheme}://{publicHost}:{internalPort}` (`AgentCardInterfaces.cs:21-27`) to the configured public gRPC URL.
- `samples/AgentServer` moves to `Tenant` routing on one gRPC port. `docker-compose.yml` exposes 5200 and 5201 only.
- `AgentCardInterfaces` (public today, R6) becomes internal.
- Code that uses `DateTimeOffset.UtcNow`, `Task.Delay` or `PeriodicTimer` without a `TimeProvider` moves to the DI `TimeProvider`. `TaskManager` already takes one (`TaskManager.cs:41,52`); `TaskEventBroker`, the push path (08) and the new hosted services must too.

## 8. How it meets the six principles

1. **Protocol.** Agent routing uses the spec's `AgentInterface.tenant`. Cards send `ETag` / `Cache-Control`. Heartbeats are SSE comments only. Overload responses are documented where the spec is silent. Check: the TCK passes with 4 agents behind one gRPC port in `Tenant` mode, and with the `SingleAgent` layout.
2. **.NET enabler.** Built on `IHostedService`, `IHostApplicationLifetime`, `HostOptions`, ASP.NET Core rate limiting, health checks, forwarded headers and Kestrel options. Check: API review (02); no custom middleware where an ASP.NET Core one exists.
3. **Easy.** In Development there is no config: request-derived URLs, loopback binding, TLS off. In production, every missing setting fails at startup with the fix. Check: the getting-started sample has no hosting config; a test asserts each startup error message names its option.
4. **Performance.** Heartbeats use one timer per stream; cards are cached as signed bytes per variant; limits reject before any work. Check (package 14):
   - card request p50 with a cache hit is under 50 µs
   - heartbeat overhead on 2,000 idle streams is under 1 % CPU on a 2-core pod *(writer's call)*
5. **Modular.** gRPC, health and rate limiting are opt-in. `IA2AMiddleware`, the per-request card factory and `IGrpcAgentRouter` are seams. Cards change only through 03's `IAgentCardContributor`. Nothing depends on Kubernetes APIs. Check: a package-dependency test asserts the core has no gRPC reference, and a test middleware sees all 11 operations on all transports.
6. **Integration pains.** Per-request cards (a2a-dotnet #353), ingress timeouts, h2c surprises and rolling-deploy orphans are handled in the library. Check: the `kind` CI job does a `Recreate` restart during a long stream and the client sees heartbeats and a clean end (or the documented shutdown error). The rolling-restart check moves to M2 with 06 (B1).

## 9. Dependencies

Needs:

- **01**: package IDs `Ambinder.A2A.Server`, `Ambinder.A2A.AspNetCore`, `Ambinder.A2A.AspNetCore.Grpc`, and the dependency allow-list test.
- **02**: `Add…` methods in namespace `Microsoft.Extensions.DependencyInjection`. After 1.0, new members on `IA2AMiddleware`, `IGrpcAgentRouter` and other public interfaces need default implementations, so both stay single-method and take context objects that can grow.
- **03**: `IAgentCardContributor` and `AgentCardContext` (this package adds the nullable `Request` property), card caching rules D7, `IA2AVersionDispatcher` (0.3 requests enter the same middleware pipeline).
- **04**: card signing "once per card version", called once per card variant.
- **05**:
  - `IInstanceStore`, `InstanceLease`
  - `StoredTask.Runner`, `RunAttempt`
  - `ITaskStore.ListByRunnerAsync`, `ITaskStore.ListReleasedAsync` (B4)
  - `StoredTask.Caller`, the reduced caller for recovered runs (B8)
  - `IStoreHealthProbe`
  - `A2AStoreException`
- **07**: `A2ACaller` (its `Owner` is the rate-limit partition key) and `ITenantResolver`, for `AgentCardRequest`; its authorization checks registered as middleware; workload identity guidance for the sample.
- **03 and 11**: extension negotiation (03 D5) as built-in middleware step 4, which uses `A2AOperationContext.ResponseMetadata` to echo `A2A-Extensions`; 11 adds its request hooks to the same step.
- **12**: the `TestServer` host relies on Goals 1 and 12 (no port routing, `TimeProvider` from DI). In M1 this package uses the repo-internal harness slice (B2).
- **02**: the banned-API analyzer that enforces Goal 12.
- **13**: the AKS and operations docs pages.
- **14**: card and streaming benchmarks.

Provides:

- **06**:
  - `AgentExecutionHost` (internal, with extension points 06 needs: run lookup by task, local cancel)
  - `TaskRecoveryService`
  - `A2AInstanceOptions` (`InstanceId`, `HeartbeatInterval`, `LeaseDuration`)
  - instance `Draining` status
  - `RecoveryPolicy`
  - the shutdown sequence hooks (`IA2AShutdownParticipant`, called at steps 1 and 5)
  - the `ready` health tag
  - `deploy/aks/` base to extend (06 adds replicas, PDB and HPA in `overlays/cluster/`)
  - the rolling-restart acceptance test, which moves to 06 in M2 (B1)
  - the middleware rule for forwarded calls and `A2ACallContext.IsForwarded` (B5)
- **08**: the shutdown hook to flush or stop delivery; the `ready` tag for the outbox backlog check.
- **10**: heartbeat format; error reasons `SERVER_BUSY`, `RATE_LIMITED`, `SERVER_SHUTTING_DOWN` (with `Retry-After`) so the client can retry or resubscribe.
- **11**: `AgentRequestContext.Recovery` for agents that opt in to restart; the `[IdempotentAgent]` attribute (C10).

## 10. Acceptance criteria

- [ ] gRPC: 4 agents on one port in `Tenant` mode, and 2 agents in `Host` mode, on Kestrel and on `TestServer`. Unknown tenant gives `NOT_FOUND` with the documented message. No server code reads `Connection.LocalPort` (a source test checks it). Setting `GrpcPorts` fails startup with the documented message.
- [ ] The card shows public URLs (including gRPC `host:port`) that differ from the bind ports; there is a test for each `PublicUrlMode`.
- [ ] Per-request card factory test (two hosts → two cards, two ETags). Contributors (03) run on each variant. `Vary: Host` and `Cache-Control: private` rules. The signed-card cache is keyed by variant (with 04).
- [ ] Middleware tests: a test middleware sees all 11 operations on JSON-RPC, REST, gRPC and in-process, in registration order (global then per agent). It can reject with `A2AException`, wrap a stream, and set a response header that reaches the client on each transport. Tracing spans are unchanged (existing telemetry tests pass).
- [ ] `TimeProvider` tests: heartbeat, shutdown and recovery tests run with `FakeTimeProvider` and no real delays.
- [ ] Route layout tests: `PerAgent` with prefix, `SingleAgent` at root, health routes not swallowed by the REST catch-all.
- [ ] SSE heartbeat tests with `FakeTimeProvider`: idle gap ≤ interval, heartbeat before the first event, both clients skip heartbeats.
- [ ] Limits tests per transport: busy (503 / `UNAVAILABLE` + `Retry-After`), rate limited (429 / `RESOURCE_EXHAUSTED`), body and gRPC message size, parts per message, artifact cap.
- [ ] Shutdown test:
  - start 10 runs (5 `Fail` agents, 5 `Restart` agents) and stop the host
  - all `Fail` tasks end `FAILED` with the documented message
  - all `Restart` tasks have `Runner = null`
  - nothing is left `WORKING` with a live runner
- [ ] Crash recovery test (SQLite, child process killed): a new process recovers all tasks within 30 s; `Restart` agents see `Recovery.Attempt = 1`; the recovered run sees the stored reduced caller and no token (B8).
- [ ] Released-task test: a `Restart` task released at shutdown is not picked up before its `UpdatedAt` is older than the lease length, and is picked up by the next sweep after that (B4).
- [ ] Recovery default: an agent with no setting gets `Fail`; an agent marked `[IdempotentAgent]` gets `Restart(1)`; an explicit `o.Recovery` wins (C10).
- [ ] Limits defaults: 200 executions per pod; no per-owner limit until set (C10).
- [ ] No new M1 test uses fixed ports or sleeps (B2).
- [ ] Health endpoints: readiness 503 while draining and when the store probe fails; liveness never depends on the store.
- [ ] TLS mode `None` fails startup outside Development with the documented message. Forwarded headers are ignored from untrusted addresses (test).
- [ ] `deploy/aks/` applies on `kind` in CI with 1 replica and `strategy: Recreate` (B1), using the Gateway API manifests (C10). The sample client flows pass through the ingress for JSON-RPC, REST and gRPC. A `Recreate` restart during a 2-minute report stream gives no lost terminal state. (The rolling-restart test with several replicas moves to M2 with 06, B1.)
- [ ] TCK passes in `Tenant` gRPC mode (03 CI matrix).
- [ ] Docs (13): "Run on AKS", "Ingress for SSE and gRPC", "Graceful shutdown and recovery", "Limits and rate limiting", "Health checks", "TLS and forwarded headers", "Per-request agent cards", "Route layout". `docs/production.md` §4 and §7 point to them.

## 11. Left to the builder

- The internal structure of `AgentExecutionHost` (channels, semaphores) as long as the limits and order rules hold.
- Names of log events and metrics added for drain, busy, rate-limited and recovery (they must follow the `Ambinder.A2A.Server` meter (renamed from `A2A.Server` by package 01) and the "never recorded" rules in `docs/production.md` §6).
- Exact Kustomize layout and resource numbers in the sample.
- Whether `UseA2AHosting()` is one call or several small ones.
- The memory-cache type for card variants.

## 12. Open questions

None. Ingress (Q1), default limits (Q2) and the recovery default (Q3) are Claude's call C10 (§7.10, §7.6, §7.7).
