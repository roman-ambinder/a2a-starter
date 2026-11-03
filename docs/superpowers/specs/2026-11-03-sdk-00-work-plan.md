# Ambinder.A2A SDK: work plan

Date: 2026-11-03
Status: approved by owner 2026-11-03

This is the master plan for turning this repo into an official, open-source .NET SDK for the A2A (Agent2Agent) protocol. It lists the decisions, the principles every package must meet, and the work packages in order. Each package has its own design spec (`2026-11-03-sdk-NN-*-design.md`).

Items marked *(Claude's call)* were decided by Claude during brainstorming or after review, not by the owner. Rows marked *(owner decision)* were settled by the owner after review. Everything else was decided by the owner.

## Intent

Today this repo is a Phase 1 reference: it works on one machine, with mocks and samples. The goal is an **official .NET A2A SDK** that product teams use in production, both to **build agents** (server) and to **call agents** (client).

It should save every .NET team from re-solving the same A2A problems. The research shows those problems are real and common.

## Principles (every spec must meet all six)

1. **Follows the A2A protocol.**
   - The official TCK passes every MUST and SHOULD on all three transports (JSON-RPC, HTTP+JSON, gRPC).
   - The TCK runs in CI on every PR.
   - Where the spec is open, the SDK documents its choice instead of guessing silently.
2. **An enabler for .NET developers.** It feels like modern .NET:
   - `Add…()` on `IServiceCollection` and the Options pattern
   - `ILogger`, `IHttpClientFactory`, OpenTelemetry and Microsoft.Extensions.AI
   - nullable annotations and XML docs on every public member
3. **Easy to use.**
   - A working agent in about 20 lines and a client call in about 5.
   - Safe defaults, with nothing to configure for local development.
   - Every error says what to do.
   - A "getting started" that works in 5 minutes.
4. **Performance efficient.**
   - Source-generated JSON, no per-call reflection, pooled buffers, and streaming without buffering whole responses.
   - A benchmark suite runs in CI.
   - No package may regress past its budget (see package 14).
5. **Modular and extendable.**
   - Small packages, so teams take only what they use.
   - Every infrastructure seam is an interface with a default.
   - Middleware on the server and interceptors on the client.
   - A real framework for A2A extensions.
   - No hidden dependencies on Azure or a database in the core.
6. **Takes on the known A2A integration pains and tedious work**, so product teams don't each re-solve them. The evidence is in `docs/superpowers/research/research-painpoints.md`.

## Decisions

| Topic | Decision |
|---|---|
| Strategy | Keep and grow this SDK. Do not adopt or fork the official `a2aproject/a2a-dotnet` (still 1.0.0-preview4, in-memory only, no built-in auth or card signing). Look again when it reaches 1.0 GA. |
| Name | Package IDs and namespaces `Ambinder.A2A.*`. The NuGet ID `A2A` belongs to the official SDK. |
| Target | .NET 10 only. |
| Publishing | Public on nuget.org. License Apache 2.0. Reserve the `Ambinder` ID prefix. |
| Storage | An abstract store contract in the core. In v1: in-memory (default), EF Core and Cosmos DB, all passing one shared conformance suite. |
| EF Core database | PostgreSQL first. SQLite for local dev and tests. SQL Server is supported through the same code but tested less. *(Claude's call)* |
| Multi-instance | An abstract cluster layer. v1 default: **HTTP owner-pod forwarding**. The task store records which pod runs a task; other pods forward cancel, subscribe and continue requests to it over internal HTTP, with leases for crash recovery. No Redis or message bus needed. Redis is a later option behind the same interface. |
| Auth | Entra ID only, out of the box. Server: token validation and app roles. Client: Azure.Identity, including AKS workload identity. Other schemes plug in through extension points but do not ship. |
| Task owner | App-only token: tenant + app. Delegated token (`scp` claim): tenant + user. Anonymous is off by default. Only the same owner, or an identity the agent names, may continue an `AUTH_REQUIRED` task. The owner resolver is replaceable. *(Claude's call)* |
| A2A 0.3 | Separate packages, `Ambinder.A2A.Compat.V0_3` (models and client) and `Ambinder.A2A.Compat.V0_3.AspNetCore` (server, C14), shipped in 1.0 GA (owner moved it up from "after GA" because of the evidence). Until then, the core treats a missing `A2A-Version` header as 0.3 and answers with a clear `VersionNotSupported` error. *(Claude's call on the interim behavior)* |
| Spec version | A2A v1.0.1 (2026-05-28). Pin the proto to the `v1.0.1` release tag instead of a `main` commit. v1.1 roadmap items are out of scope. |
| Repo (O1) | The repo stays at `roman-ambinder/a2a-starter` for now. Package URLs use it. *(owner decision)* |
| People and dates (O2) | One builder, no fixed dates. Packages run in dependency order. Review gates are automated checks (public API diff, required CI, TCK, interop) plus the owner's admin approval for releases. No second human reviewer is required. *(owner decision)* |
| Cloud resources (O3) | None for now. Live Entra, Key Vault, Cosmos and multi-pod AKS tests are written but run only when their secrets exist, and are skipped visibly otherwise. Until then, those exit criteria are met by fakes, the Cosmos emulator and `kind`, plus a manual pre-release checklist. *(owner decision)* |
| Package signing (O4) | Previews ship unsigned. Setting up Azure Trusted Signing is an owner action required before `1.0.0`. *(owner decision)* |
| Author and copyright (O5) | "Roman Ambinder" in `Authors`, `Copyright`, `LICENSE` and `NOTICE`. *(owner decision)* |

## Work packages

| # | Package | Spec file | Milestone | Size |
|---|---|---|---|---|
| 01 | Naming, packaging and release | `2026-11-03-sdk-01-packaging-release-design.md` | M1 | M |
| 02 | Public API and versioning | `2026-11-03-sdk-02-public-api-design.md` | M1, frozen at M2 | M–L |
| 03 | Spec conformance and TCK | `2026-11-03-sdk-03-spec-conformance-design.md` | M1 | L |
| 04 | Agent card signing and verification | `2026-11-03-sdk-04-card-signing-design.md` | M1 | M |
| 05 | Store contract and durable stores | `2026-11-03-sdk-05-stores-design.md` | M1 | L |
| 06 | Multi-instance cluster layer | `2026-11-03-sdk-06-cluster-design.md` | M2 | XL |
| 07 | Security model and Entra | `2026-11-03-sdk-07-security-entra-design.md` | M1 | L |
| 08 | Push notifications and webhook receiver | `2026-11-03-sdk-08-push-design.md` | M1 | L |
| 09 | Hosting on AKS | `2026-11-03-sdk-09-hosting-design.md` | M1 | L |
| 10 | Production client | `2026-11-03-sdk-10-client-design.md` | M1 | L |
| 11 | Agent authoring and AI bridges | `2026-11-03-sdk-11-authoring-ai-design.md` | M2 | L |
| 12 | Testing package and quality | `2026-11-03-sdk-12-testing-quality-design.md` | M2 (internal harness slice and store suite in M1, B2) | L |
| 13 | Docs for product teams | `2026-11-03-sdk-13-docs-design.md` | M2 | M |
| 14 | Performance and benchmarks | `2026-11-03-sdk-14-performance-design.md` | M1 baseline, budgets in M2 | M |
| 15 | A2A 0.3 compatibility | `2026-11-03-sdk-15-compat-v03-design.md` | M2 | L |
| 16 | Cross-SDK interop test suite | `2026-11-03-sdk-16-interop-design.md` | M1 | M |

Sizes: S < 1 day, M = days, L = 1–3 weeks, XL > 3 weeks. Package 12 is L, including the repo test migration (C13).

This table lists work packages, not NuGet packages. The NuGet package IDs are in package 01's package map (`2026-11-03-sdk-01-packaging-release-design.md`, Design §1). Review added two: `Ambinder.A2A.Cluster.Entra` (06, C6) and `Ambinder.A2A.Compat.V0_3.AspNetCore` (15, C14).

## Milestones *(Claude's call)*

**M1, `1.0.0-preview`.** Early product teams can use it on a single instance in production-like settings.
- Packages 01, 02 (internals hidden, API tracking on), 03, 04, 05, 07, 08, 09, 10, 14 (baseline), 16, plus the repo-internal test-harness slice from 12 (B2).
- M1 deployments are single-replica only (B1). The 09 sample uses 1 replica with `strategy: Recreate`. The docs say "do not scale out before 1.0".
- Exit criteria: the TCK passes; interop with the Python, JS and Java reference SDKs passes; benchmarks have a baseline; packages are published as preview, unsigned (O4). Live Entra, Key Vault and Cosmos tests run when their secrets exist; otherwise fakes, the Cosmos emulator and `kind` meet the criteria, plus the manual pre-release checklist (O3).

**M2, `1.0.0`.** Production for every team, with scale-out.
- Packages 06, 11, 12, 13, 15, plus 02 (API frozen, semver from here) and 14 (budgets enforced).
- The rolling-restart acceptance test moves here from 09, with 06 (B1). Cluster mode is supported on the EF Core store only (B3).
- Exit criteria: multi-instance runs on `kind` with 3 replicas, and on a real AKS cluster when its secrets exist (otherwise the manual pre-release checklist covers it, O3); 0.3 peers work; the docs site covers getting started, agents, clients and operations; the public API is frozen; packages are signed with Azure Trusted Signing (O4).

**After 1.0.** Redis cluster backend, other stores, A2A v1.1 features, and other auth schemes as separate packages.

## Order and dependencies

- **01 → everything.** The rename to `Ambinder.A2A.*` comes first, so later work doesn't rename twice.
- **02** runs alongside the others. Each package adds its public API to the tracking files as it lands.
- **03 and 16** come early. They find protocol bugs that other packages would otherwise build on.
- **05 → 06, 08.** The cluster layer and the push outbox both use the store contract (the owner and lease fields, and outbox rows).
- **07 → 10, 11.** The client's auth retry and the authoring model's caller identity depend on the security model.
- **09 → 06.** Graceful shutdown and task recovery come before cross-pod forwarding.
- **12** collects test helpers built in the other packages (the store conformance suite from 05, fake agents and clients).
- **B2: internal test-harness slice in M1 → 05, 09.** A repo-internal slice of 12's harness (`A2ATestServer` on `TestServer`, `FakeTimeProvider` wiring, `TaskEventLog` waiters, `StoreSet`) is built early in M1 as test code. 05 and 09 use it. 12 publishes it in M2. No new M1 test uses fixed ports or sleeps.
- **13** is written last per area but started early. Each package adds its own section.
- **14** sets a baseline in M1. Every package after that must stay within budget.

## Claude's calls after review

Each line says where the decision now lives.

- C1: High/Critical NuGet advisories fail a PR; Low/Moderate warn; Windows unit tests run nightly and on release, Linux on every PR. [01](2026-11-03-sdk-01-packaging-release-design.md), [12](2026-11-03-sdk-12-testing-quality-design.md)
- C2: Support window: latest minor, plus security fixes for the previous minor for 3 months. [02](2026-11-03-sdk-02-public-api-design.md)
- C3: SHOULD failures caused by TCK bugs may go on a reviewed known-issues list with an upstream link and expiry; MUST failures may not. [03](2026-11-03-sdk-03-spec-conformance-design.md)
- C4: Client card verification defaults to `IfPresent` with `AcceptKnownCanonicalizationVariants = true` until A2A #2122 is settled; strict mode is opt-in. [04](2026-11-03-sdk-04-card-signing-design.md)
- C5: 30-day retention for finished tasks on durable stores; Cosmos partition key (tenant, agent, owner) accepted; messageId create-dedupe on by default. [05](2026-11-03-sdk-05-stores-design.md)
- C6: Cluster pod-to-pod auth ships Entra workload identity only, in `Ambinder.A2A.Cluster.Entra`; `SharedKey` is test code only. [06](2026-11-03-sdk-06-cluster-design.md), [01](2026-11-03-sdk-01-packaging-release-design.md)
- C7: Continuation affinity default `PreferPreviousRunner`; a runner crash ends the stream with an interruption error and the client resubscribes. [06](2026-11-03-sdk-06-cluster-design.md)
- C8: Delegated/OBO coverage by fake authority plus a manual device-code check per release; Development-only anonymous with a warning when no scheme is registered. [07](2026-11-03-sdk-07-security-entra-design.md)
- C9: Push signature extension is on when a key source is configured (ephemeral in Development), else off with standard `authentication` only; the receiver verifies a signature when present. [08](2026-11-03-sdk-08-push-design.md)
- C10: Sample ingress targets the Gateway API; 200 runs per pod, no per-owner limit by default; recovery default `Fail`, `Restart(1)` opt-in via `[IdempotentAgent]`. [09](2026-11-03-sdk-09-hosting-design.md)
- C11: `AllowSendRetry` is safe against our servers but stays opt-in; `CancelRemoteOnCallerCancel` defaults to false. [10](2026-11-03-sdk-10-client-design.md)
- C12: The Agent Framework bridge ships in 1.0, as a preview package if `Microsoft.Agents.AI` is not GA by M2. [11](2026-11-03-sdk-11-authoring-ai-design.md), [01](2026-11-03-sdk-01-packaging-release-design.md)
- C13: MockEntra stays repo-only (teams get `TestIdentity`); `tck` and `interop` are advisory for two weeks, then required; package 12 is size L. [12](2026-11-03-sdk-12-testing-quality-design.md)
- C14: 0.3 compat is split into `Ambinder.A2A.Compat.V0_3` (models, client) and `Ambinder.A2A.Compat.V0_3.AspNetCore` (server). [15](2026-11-03-sdk-15-compat-v03-design.md), [01](2026-11-03-sdk-01-packaging-release-design.md)
- C15: `interop-smoke` on every PR; `interop-full` nightly, on tags and before release. [16](2026-11-03-sdk-16-interop-design.md)
- B1: M1 is single-replica (`strategy: Recreate`); rolling-restart test moves to M2 with 06; "do not scale out before 1.0". [09](2026-11-03-sdk-09-hosting-design.md), [06](2026-11-03-sdk-06-cluster-design.md)
- B2: A repo-internal test-harness slice is built in M1 for 05 and 09; 12 publishes it in M2; no new M1 test on fixed ports or sleeps. [12](2026-11-03-sdk-12-testing-quality-design.md), [05](2026-11-03-sdk-05-stores-design.md), [09](2026-11-03-sdk-09-hosting-design.md)
- B3: Cluster mode in 1.0 is EF Core only; with Cosmos it fails fast at startup. [06](2026-11-03-sdk-06-cluster-design.md), [05](2026-11-03-sdk-05-stores-design.md)
- B4: Released tasks are found by the new store query `ListReleasedAsync(olderThan, limit)` and swept once `UpdatedAt` is older than the lease length. [05](2026-11-03-sdk-05-stores-design.md), [06](2026-11-03-sdk-06-cluster-design.md), [09](2026-11-03-sdk-09-hosting-design.md)
- B5: The full middleware pipeline runs only on the receiving pod; the owner pod runs peer auth, tracing and the handler, with `IsForwarded`. [06](2026-11-03-sdk-06-cluster-design.md), [09](2026-11-03-sdk-09-hosting-design.md)
- B6: A continuation-grant holder gets its own call's response but cannot Get/List/Subscribe/Cancel afterwards; the client wait stays on the continuation's stream. [07](2026-11-03-sdk-07-security-entra-design.md), [10](2026-11-03-sdk-10-client-design.md), [11](2026-11-03-sdk-11-authoring-ai-design.md)
- B7: Typed `OwnerKey.ForApp`/`ForUser`; the owner-key string format is frozen at 1.0; grants are built from `OwnerKey`. [07](2026-11-03-sdk-07-security-entra-design.md), [11](2026-11-03-sdk-11-authoring-ai-design.md), [05](2026-11-03-sdk-05-stores-design.md)
- B8: Runs on other pods and recovered runs get a persisted reduced caller (no token); OBO only on the receiving pod. [07](2026-11-03-sdk-07-security-entra-design.md), [05](2026-11-03-sdk-05-stores-design.md), [06](2026-11-03-sdk-06-cluster-design.md), [09](2026-11-03-sdk-09-hosting-design.md), [11](2026-11-03-sdk-11-authoring-ai-design.md)
- B9: `A2AAgentTool` uses the agent's app identity by default (OBO opt-in) and only accepts task and context ids it created for the parent caller's owner. [11](2026-11-03-sdk-11-authoring-ai-design.md), [07](2026-11-03-sdk-07-security-entra-design.md), [10](2026-11-03-sdk-10-client-design.md)
- B10: `IA2APushWaiter` and `PushSubscription` move to `Ambinder.A2A.Client`; the OBO provider takes a user-assertion delegate, adapted by `Ambinder.A2A.AspNetCore.Entra`. [08](2026-11-03-sdk-08-push-design.md), [10](2026-11-03-sdk-10-client-design.md), [07](2026-11-03-sdk-07-security-entra-design.md), [01](2026-11-03-sdk-01-packaging-release-design.md)
- B11: Extended cards are cached per credential identity; `UseExtendedCard = Auto` is off for per-call credentials without a cache key. [10](2026-11-03-sdk-10-client-design.md), [07](2026-11-03-sdk-07-security-entra-design.md)
- B12: Shared helpers are linked source files compiled as internal; `A2AJson` exposes typed `JsonTypeInfo<T>` accessors. [01](2026-11-03-sdk-01-packaging-release-design.md), [02](2026-11-03-sdk-02-public-api-design.md), [14](2026-11-03-sdk-14-performance-design.md)
- B13: Card ETag = hash of the unsigned canonical card payload plus the signing key ids. [03](2026-11-03-sdk-03-spec-conformance-design.md), [04](2026-11-03-sdk-04-card-signing-design.md), [09](2026-11-03-sdk-09-hosting-design.md), [10](2026-11-03-sdk-10-client-design.md)

## Evidence

The specs cite findings by ID:

- `R1`–`R42`: repo review, in `docs/superpowers/research/review-repo.md`
- spec requirements: `docs/superpowers/research/research-spec.md`
- other SDKs: `docs/superpowers/research/research-sdks.md`
- pain points (by title): `docs/superpowers/research/research-painpoints.md`

## Out of scope for this plan

- Building on or forking the official A2A .NET SDK.
- Targets older than .NET 10.
- Auth schemes other than Entra as shipped packages.
- A2A v1.1 features (task history mode, BiDi streaming, elicitation).
- Hosting outside AKS, as tested and documented targets. The SDK must not depend on AKS, though.
