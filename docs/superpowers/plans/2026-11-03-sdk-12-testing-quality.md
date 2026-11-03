# SDK package 12: Testing package and quality — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task by task, with every subagent on model `sonnet` (see "Execution notes" at the end). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Ambinder.A2A.Testing`, `Ambinder.A2A.Testing.AI` and `Ambinder.A2A.Testing.Conformance` (in-memory hosts, fakes, recorders, assertions, event-driven waiters, telemetry collectors, the store conformance suite), move the repo's own tests onto them, and add the repo's quality gates (test-side analyzers, security scanning, coverage, CI layout, soak and fuzz tests).

**Spec (binding):** `docs/superpowers/specs/2026-11-03-sdk-12-testing-quality-design.md` ("the spec"; "§N" means its Design section N). Work plan: `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md` (B2, C1, C13, O2). Package map: `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md` §1.

**Architecture:**
- **Promote, do not rewrite.** In M1, packages 05 and 09 built the repo-internal harness slice (`tests/Ambinder.A2A.TestHarness`, namespace `Ambinder.A2A.Testing`: `A2ATestServer`, `A2ATestServerOptions`, `A2ABinding`, `TaskEventLog`, `TaskEvent`, `StoreSet`) and 05 built the conformance project (`tests/Ambinder.A2A.Stores.Conformance`). This plan moves both into `src/` with `git mv`, keeps every type name, namespace and case id, and adds the rest of spec §2–§5 around them.
- `Ambinder.A2A.Testing` has no test-framework dependency. Failures throw `A2ATestAssertException`. Only `Ambinder.A2A.Testing.Conformance` references xunit v3.
- The repo's tests are then rebuilt on the package (spec §7), so the package is tested by real use, and structure rules are enforced by a `Repo.Tests` project and a banned-symbol file.
- Quality gates are added as CI jobs beside the ones 01, 03, 14 and 16 own.

**Tech Stack:** .NET 10, xunit v3 4.0.2 with Microsoft.Testing.Platform (repo runner), `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.AI.Abstractions` (Testing.AI only), `xunit.v3.extensibility.core` (Conformance only), Microsoft.Testing.Platform code coverage, `dotnet-coverage`, ReportGenerator, FsCheck, Stryker.NET (nightly, advisory), GitHub CodeQL, dependency-review and Scorecard actions. Versions come from `Directory.Packages.props`; tasks that add a package check nuget.org for the newest stable version and do not guess.

---

## 0. Assumptions and prerequisites

### 0.1 Package 01 has landed (assumed)

This plan assumes package 01's rename and split are merged into `master`. It uses 01's names:

| Thing | Name after 01 |
|---|---|
| Solution | `Ambinder.A2A.slnx` |
| Shipped projects | `src/Ambinder.A2A.<Name>/` (Protocol, Server, AspNetCore, AspNetCore.Grpc, Client, Client.Grpc, …) |
| New in this plan | `src/Ambinder.A2A.Testing/`, `src/Ambinder.A2A.Testing.AI/`, `src/Ambinder.A2A.Testing.Conformance/` (01 may have created empty projects; reuse them) |
| Tests | `tests/Ambinder.A2A.<Package>.Tests` (01 naming rule) |
| Props | `Directory.Build.props`, `src/Directory.Build.props` (packable, metadata), `tests/Directory.Build.props` and `samples/Directory.Build.props` (`IsPackable=false`) |
| Dependency allow-list | `eng/package-dependencies.json` and its test (01 §6) |
| CI | `.github/workflows/ci.yml` with 01's jobs (pack, allow-list, samples on packages, coexistence, reproducibility, Windows leg, AOT smoke) and its NuGet audit levels (C1) |

Phase 1 file names in the spec's §7 table (for example `tests/A2A.Server.Tests/TestAgents.cs`) are found after 01 with `git ls-files | grep <FileName>`.

**Steps that depend on package 01** (they use its names, layout, props or CI):
- Task 0 (checks 01's layout).
- Every task: project paths, `Ambinder.A2A.*` namespaces, test project names, `Ambinder.A2A.slnx` in commands.
- Task 1: creating the three `src/` projects per the package map (README from 01's template, description, tags, `IsAotCompatible` = no for all three, `eng/package-dependencies.json` entries, pack output listed in the map).
- Task 22: test-side analyzer settings go into 01's `tests/Directory.Build.props` and `samples/Directory.Build.props`.
- Task 23 and Task 25: they extend 01's `ci.yml` (job split) and keep 01's audit levels (C1) and Windows leg.
- Task 28: `.gitignore` entries that 01 did not add (01 adds `TestResults/`; this plan adds `coverage/` and `*.trx` if missing).

If Task 0 finds that 01 has **not** landed: stop and report. Do not do the rename here.

### 0.2 Other packages this plan needs

Package 12 is M2. Task 0 records which seams exist. **Gate rule (binding):** a task whose seam is missing is not started, and no stand-in for another package's contract is built. Report blocked tasks and continue with the others.

| Seam | Owner | Needed by | If missing |
|---|---|---|---|
| Harness slice `tests/Ambinder.A2A.TestHarness` (`A2ATestServer`, `A2ATestServerOptions`, `A2ABinding`/`A2ABindings.All`, `TaskEventLog`, `TaskEvent`, `StoreSet`) | 05, 09 (B2) | Task 1 | Create it in Task 1 with the names and shapes of the 09 plan (Task 1) and the 05 plan (Task 4). This is the one case where this plan builds it, because it is 12's own type set. |
| Conformance project `tests/Ambinder.A2A.Stores.Conformance` (case catalogs, abstract xunit classes) | 05 | Task 14 | Stop before Task 14. |
| `TaskUpdater` (`AgentRequestContext.Create`) | 11 | Tasks 5, 6 | Stop before Task 6; Task 5 uses `IAgentEventSink` directly and `AgentRequestContext.Create` only if present. |
| `ScriptedChatClient` test double in `tests/Ambinder.A2A.Extensions.AI.Tests/Fakes/` and `Ambinder.A2A.Extensions.AI` | 11 | Task 13 | Stop before Task 13. |
| `A2AClientOverride` (client factory override seam), `IA2AClientFactory`, `AddA2AClient`, `IA2AClient` with `A2ARequestOptions`; `tests/Ambinder.A2A.Client.Tests` | 10 | Tasks 3, 8, 9, 18 | Stop before Task 8. |
| Receiver-side verifier in `Ambinder.A2A.Push.Receiver`; push HTTP client name `"a2a-push"`; `PushDeliveryService` | 08 | Task 10 | Task 10 records deliveries and scripts responses, without the verification result; note it. |
| Claim and owner rules (`tid`, `azp`/`appid`, `oid`, `scp`, `roles`; `OwnerKey`) | 07 | Task 4 | Stop before Task 4. |
| gRPC routing without ports; `TimeProvider` everywhere; `UseA2AHosting` on `TestServer` | 09 | Task 3 | Task 3 ships JSON-RPC and REST only and lists gRPC as blocked. |
| `StartClusterAsync` (06 plan Task 16 adds it to the slice if missing) | 06 | Task 12 | Build the shared-store part (spec §3 "Cluster") in Task 12; 06 adds forwarding. |
| `src/` analyzer settings, `eng/BannedSymbols.txt`, `CS1591` as error, public API files | 02 | Task 22 | Task 22 does only the test-side rules and lists the `src/` items as "02 open" in the PR body. Do not set `src/` rules here (spec §6: "where it differs, 02 wins"). |
| TCK job (`tck`), interop job (`interop`), benchmark job (`bench`) | 03, 16, 14 | Task 25 | Leave their slots in the layout and list them as "not yet present". |
| Docs tree (`docs/agents/`, `docs/clients/`) | 13 | Task 27 | Create the pages at 13's paths anyway. |

### 0.3 Decisions this plan makes (plan-writer's calls)

1. **Promotion keeps the slice's shapes.** If the slice's `TaskEventLog` records `(StoredTask, StreamResponse)` (05's shape) or `TaskEvent(AgentName, TaskId, State, Event, Index)` (09's shape), keep it and add only what spec §4 needs. No type renames.
2. **Conformance dependencies.** Spec §1 lists `Testing` and `xunit.v3.extensibility.core` as the only dependencies of `Ambinder.A2A.Testing.Conformance`. The 05 plan's project also references `xunit.v3.assert`. Task 14 replaces those assert calls with `ConformanceAssert` (which throws `A2ATestAssertException`) and drops `xunit.v3.assert`. If a case cannot be expressed without it, stop and report.
3. **Leftovers in `tests/Shared`.** Helpers there that the spec does not publish (for example 07's `SecretScrubber.cs`, and 16's linked-source `tests/Shared/WireFixtures/` used by `Protocol.Tests` and `Interop.Tests`) move to a non-packable project `tests/Ambinder.A2A.TestSupport/`, so the rule "no `tests/Shared`" holds. Linked-source consumers switch to a project reference; run both of 16's consumers after the move (review item 11).
4. **Framework-neutrality proof** (spec principle 2): `Testing.Tests` has a test that runs the 12-line hello-world body through a tiny hand-written runner (a static method that calls the body and reports `A2ATestAssertException`), with no xunit types inside the body.
5. **Coverage tooling:** gate script in bash at `eng/coverage/gate.sh`, thresholds in `eng/coverage/thresholds.json` (spec table), one ReportGenerator config at `eng/coverage/reportgenerator.config`.
6. **Assertion-free test check:** xunit analyzers (shipped with xunit v3) on as errors for the empty-body and `xUnit2013`-style rules, plus a `Repo.Tests` source scan that flags a `[Fact]`/`[Theory]` method whose body contains none of: `Assert.`, `.Should`, `Verify`, `WaitFor`, `ThrowsAsync`, `Throws`. Each exception carries a `// no-assert: <reason>` comment.
7. **Golden files:** the JSON samples in `tests/…Protocol.Tests/Samples.cs` move to `testdata/protocol/*.json`; `Protocol.Tests` reads them. Golden files that 03 or 04 placed elsewhere move there too, with their tests updated.
8. **Owner-only steps** (cannot be done in code): required checks in branch protection, secret scanning and push protection. Task 28 lists them for the owner and records the evidence the spec asks for.
9. **`A2ABindings.All`, not `A2ABinding.All`.** The spec writes `A2ABinding.All`, but `A2ABinding` is an enum (09 plan Task 1), and a C# enum cannot have members. Keep 09's `public static class A2ABindings { All }` and document it.
10. **`CreateClient(..., TestIdentity? identity = null, ...)`.** The spec's parameter name `as` is a C# keyword. Use `identity`.
11. **Default identity of `A2ATestServer`.** 07 makes every agent `Authenticated` by default. The C8 Development fallback (anonymous when no scheme is registered) no longer applies once the harness registers its test scheme. So `CreateClient` with `identity: null` uses `TestIdentity.Default` = `TestIdentity.App("test-tenant", "test-app")`. Anonymous calls pass `TestIdentity.Anonymous` explicitly. Migrated tests therefore run authenticated, as in production.
12. **Push verification through 08's public receiver.** 08's `PushSignatureVerifier` is internal to `Ambinder.A2A.Push.Receiver` (08 plan), and 01 rule 6 forbids `InternalsVisibleTo` between shipped packages. `PushReceiverDouble` therefore hosts 08's real receiver endpoint and reads the verification flags from the public `A2APushNotification` it hands to an `IA2APushHandler`. This adds `Ambinder.A2A.Push.Receiver` to `Ambinder.A2A.Testing`'s dependencies (Push.Receiver needs only Client and ASP.NET Core, which Testing already has). Update 01's package-map row and `eng/package-dependencies.json` in the same PR. **Owner check:** if this dependency is not wanted, the double records deliveries without verification, and that acceptance part stays open.

### 0.4 Changes after the cross-plan review (2026-11-03)

This plan was checked against spec 12 and the 03, 04, 05, 06, 07, 08, 09, 11, 13, 14 and 16 plans on `master`. These edits came out of that review. Each is marked in its task:

| # | Gap | Fix | Task |
|---|---|---|---|
| 1 | `A2ABinding.All` cannot exist on an enum | §0.3 item 9 | 3, 18 |
| 2 | parameter name `as` is a C# keyword | §0.3 item 10 | 3, 4 |
| 3 | with 07's `Authenticated` default, a client without identity gets 401 | §0.3 item 11 | 4, 18 |
| 4 | 09's slice `TaskEventLog` throws `TimeoutException`; this plan wants `A2ATestAssertException` | update the slice's own tests in the same task | 2 |
| 5 | 08's verifier is internal | §0.3 item 12 | 10 |
| 6 | 05 already creates `tests/Ambinder.A2A.Stress.Tests` (trait `Category=Soak`) and may create `nightly.yml` | extend both; one trait | 23, 25, 26 |
| 7 | soak counters name types that 06 and 08 replaced | read 06's `LocalTaskEventBus` and 08's outbox stats | 26 |
| 8 | CI names: 16's jobs are `interop-smoke`/`interop-full` (C15); 05, 06 and 09 added jobs and workflows | keep them all; required check is `interop-smoke` | 25, 28 |
| 9 | the "no fixed port" rule would flag deployable host projects | explicit exclusion list | 19 |
| 10 | 04 and 16 already keep golden files under `tests/vectors/`, with tooling | one golden root, `tests/vectors/`; owner check | 21 |
| 11 | 16's `tests/Shared/WireFixtures/` is linked source used by two projects | moves with the other leftovers | §0.3 item 3, 18 |
| 12 | 05's conformance classes call `Assert.Skip` and `TestContext.Current`; 05 left the `ConformanceFailedException` → `A2ATestAssertException` swap to 12 | check the defining packages before dropping `xunit.v3.assert`; do the swap | 14 |
| 13 | 11's `ScriptedChatClient` arrives with a `TaskCompletionSource` wait hook and plain exceptions | swap in `Gate` and `A2ATestAssertException` when moving it | 13 |

---

## Global Constraints

- **Branch:** the session's branch, from the latest `master`. One commit per task at least. Push after every task.
- **Build rules:** `TreatWarningsAsErrors`, nullable on, central package management, XML docs on every public member of the three packages.
- **No test framework in `Ambinder.A2A.Testing` or `Ambinder.A2A.Testing.AI`** (Goal 7): no `Xunit.*`, `NUnit*`, `MSTest*`, `TestContext`. All failures throw `A2ATestAssertException`.
- **No sleeping, no polling** (Goal 5): every wait completes from an event (`TaskCompletionSource`) or a `FakeTimeProvider` advance. `Task.Delay` is allowed only inside the package where it takes the injected `TimeProvider` (for example `ScriptedAgent.Delay`).
- **No fixed ports** outside `Samples.Tests`. `Kestrel` mode binds port 0.
- **Real pipeline** (anti-goal "helpers that hide real behavior"): hosts run the real auth, validation, serialization and stores. The test auth scheme only builds a principal; the real owner resolver and policies run.
- **Gates fail for real reasons only** (anti-goal): new analyzer findings are fixed in the PR that turns the rule on; coverage is never reached by excluding code.
- **TDD per task:** failing test → see it fail → implement → see it pass → commit.

Commands from the repo root:

```bash
dotnet build Ambinder.A2A.slnx
```

```bash
dotnet test Ambinder.A2A.slnx
```

## Review Focus

1. **`ShouldFollowStreamRules`** rejects every broken case in the acceptance list and accepts every valid stream the repo produces (Tasks 7, 20).
2. **`TestIdentity` cannot weaken real auth:** the test scheme is registered only by `A2ATestServer`/`AgentHarness`, only when the app has none, and the real owner resolver and policies run (Task 4).
3. **Waiters never miss an event** that happened before the wait started (Task 2).
4. **Conformance case ids are unchanged** after the move, so third-party stores keep their test names (Task 14).
5. **The migration loses no test:** the number of test cases after Phase B is at least the number before, minus documented duplicates (Task 19).
6. **Coverage gates count only tests that assert** (Tasks 22, 24).
7. **The security workflow cannot be bypassed by forks** and uses least `permissions` per job (Task 23).

---

## File map

Created (`src/Ambinder.A2A.Testing/`): moved from the slice: `A2ATestServer.cs`, `A2ATestServerOptions.cs`, `A2ABinding.cs`, `TaskEventLog.cs`, `TaskEvent.cs`, `StoreSet.cs`; new: `A2ATestAssertException.cs`, `TestIdentity.cs`, `TestAuthenticationHandler.cs` (internal), `AgentHarness.cs`, `ScriptedAgent.cs`, `ScriptStep*.cs`, `FakeAgents.cs`, `Gate.cs`, `Signal.cs`, `NotScriptedException.cs`, `FakeRemoteAgent.cs`, `RecordingA2AClient.cs`, `PushReceiverDouble.cs`, `TaskRun.cs`, `Assertions/TaskAssertions.cs`, `Assertions/ArtifactAssert.cs`, `Assertions/StreamAssertions.cs`, `Assertions/EventTable.cs` (internal), `TransportMatrix.cs`, `ServiceCollectionExtensions.cs` (`AddScriptedAgent`, `AddFakeA2AClient`), `Telemetry/ActivityCollector.cs`, `Telemetry/MetricCollector.cs`, `README.md`.
Created: `src/Ambinder.A2A.Testing.AI/` (`ScriptedChatClient.cs`, `FakeChatClient.cs`, `README.md`); `src/Ambinder.A2A.Testing.Conformance/` (moved from `tests/Ambinder.A2A.Stores.Conformance`, plus `README.md`).
Created (tests): `tests/Ambinder.A2A.Testing.Tests/`, `tests/Ambinder.A2A.Samples.Tests/`, `tests/Ambinder.A2A.Repo.Tests/`, `tests/Ambinder.A2A.Stress.Tests/` (or extend 05's), `tests/Ambinder.A2A.TestSupport/` (if leftovers exist), `tests/BannedSymbols.Tests.txt`.
Created (eng/CI): `eng/coverage/{gate.sh,thresholds.json,reportgenerator.config}`, `.github/workflows/security.yml`, `.github/workflows/nightly.yml` (or extend 05's); `tests/vectors/protocol/`.
Modified: `ci.yml`, `tests/Directory.Build.props`, `samples/Directory.Build.props`, `.editorconfig` (test section), `Directory.Packages.props`, `eng/package-dependencies.json`, `Ambinder.A2A.slnx`, `.github/dependabot.yml`, `.gitignore`, every test project in `tests/`.
Deleted: `tests/Ambinder.A2A.TestHarness/` (moved), `tests/Ambinder.A2A.Stores.Conformance/` (moved), `tests/Shared/`, `TestAgents.cs`, `MinimalServer.cs`, `Fixtures/TransportMatrix.cs`.

---

## Phase A — The packages

### Task 0: Branch and inventory

**Files:** none. Results go into the PR body.

- [ ] **Step 1:** `git fetch origin && git checkout -B <session branch> origin/master`.
- [ ] **Step 2: Confirm 01.**

```bash
test -f Ambinder.A2A.slnx && ls src | grep -E '^Ambinder\.A2A\.(Server|AspNetCore|Client)$'
```

```bash
git grep -nE 'namespace A2A\.' -- src tests | head
```

Expected: present; the grep prints nothing. Otherwise stop.
- [ ] **Step 3: Inventory the slice and the suite.**

```bash
ls tests/Ambinder.A2A.TestHarness tests/Ambinder.A2A.Stores.Conformance 2>&1
```

```bash
git grep -nE 'class (A2ATestServer|A2ATestServerOptions|TaskEventLog|StoreSet)|record TaskEvent|enum A2ABinding|StartClusterAsync|UseKestrel' -- tests
```

```bash
git grep -n 'xunit.v3.assert' -- tests/Ambinder.A2A.Stores.Conformance
```

- [ ] **Step 4: Inventory the other seams of §0.2.**

```bash
git grep -nE 'class TaskUpdater|static AgentRequestContext Create|class A2AClientOverride|interface IA2AClientFactory|class ScriptedChatClient' -- src tests
```

```bash
git grep -nE '"a2a-push"|class .*PushSignature.*Verifier|AnalysisMode|BannedSymbols' -- src eng '*.props' | head -20
```

- [ ] **Step 5: Baseline numbers** (Review Focus 5). Record:

```bash
dotnet test Ambinder.A2A.slnx 2>&1 | tail -20
```

```bash
git grep -nE 'Task\.Delay|Thread\.Sleep' -- tests | wc -l; git grep -lE 'A2A_TEST_PORT_BASE|PortBase' -- tests
```

Record total test cases, the sleep occurrences and files, and the port-base files. (Before the rename the counts were 19 sleeps in 13 files and 3 port-base files; the spec's numbers are older.)
- [ ] **Step 6:** Write the gate table and the baseline into the PR body draft. No commit.

### Task 1: Create the three packages and promote the slice

**Files:**
- `git mv tests/Ambinder.A2A.TestHarness/*.cs src/Ambinder.A2A.Testing/`; create `src/Ambinder.A2A.Testing/Ambinder.A2A.Testing.csproj` (refs Server, Client, Protocol, AspNetCore; packages `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.TimeProvider.Testing`; packable per `src/Directory.Build.props`), `README.md`.
- Create empty `src/Ambinder.A2A.Testing.AI/` (refs Testing, Extensions.AI; package `Microsoft.Extensions.AI.Abstractions`) and `src/Ambinder.A2A.Testing.Conformance/` (filled in Task 14), each with README.
- Create `tests/Ambinder.A2A.Testing.Tests/` (move the slice's own tests here, for example `tests/Ambinder.A2A.TestHarness.Tests/*`).
- Modify: every project that referenced `tests/Ambinder.A2A.TestHarness` → reference `src/Ambinder.A2A.Testing`; `Ambinder.A2A.slnx`; `eng/package-dependencies.json`.
- Test: `tests/Ambinder.A2A.Testing.Tests/PackageGraphTests.cs`

- [ ] **Step 1: Write failing tests:** `Ambinder.A2A.Testing` (from its nuspec after `dotnet pack`, or its `.csproj` references) has no `xunit*`, `NUnit*`, `MSTest*`, `Microsoft.Extensions.AI*` dependency; `Ambinder.A2A.Testing.AI` has no xunit; `Ambinder.A2A.Testing.Conformance` depends only on Testing and `xunit.v3.extensibility.core` (spec principle 5 check).
- [ ] **Step 2:** Run `dotnet test tests/Ambinder.A2A.Testing.Tests --filter "FullyQualifiedName~PackageGraphTests"`. Expected: FAIL (projects missing).
- [ ] **Step 3:** Do the moves and project creation. Type names and namespace (`Ambinder.A2A.Testing`) stay. If the slice did not exist (§0.2), create it now with the 09 plan Task 1 and 05 plan Task 4 shapes.
- [ ] **Step 4:** `dotnet build Ambinder.A2A.slnx`, run the whole suite (all slice users still pass), run `dotnet pack src/Ambinder.A2A.Testing -o artifacts/packages` and the PackageGraph tests. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): Ambinder.A2A.Testing packages; promote the M1 harness slice`.

### Task 2: `A2ATestAssertException`, `Gate`, `Signal`, and `TaskEventLog` completeness (§1, §4)

**Files:** Create `A2ATestAssertException.cs`, `Gate.cs`, `Signal.cs` (move from `TestAgents.cs`, adapted to no xunit). Modify `TaskEventLog.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/TaskEventLogTests.cs`, `GateSignalTests.cs`.

- [ ] **Step 1: Write failing tests.**
  - `WaitForAsync(match, timeout)` completes for an event recorded **before** the call (Review Focus 3) and for one recorded after.
  - Concurrent writers and waiters: 1,000 events from 8 threads, 50 waiters for distinct events → all complete, none misses.
  - Timeout throws `A2ATestAssertException` whose message lists the events seen so far (compact table, §4 "Failure text").
  - `WaitForTaskStateAsync(taskId, state, timeout)` works.
  - The timeout uses a real `CancellationTokenSource` with the given span (a wait deadline is not task time, so it is not on the fake clock; documented in the XML doc).
  - `Gate.Release()` releases all waiters once; `Signal.WaitAsync` completes on `Set`.
  - `A2ATestAssertException` derives from `Exception` only.
  - 09's slice threw `TimeoutException` on a wait timeout (09 plan Task 1, `TaskEventLogTests.WaitTimesOutWithEventsInMessage`). Change that slice test in this task to expect `A2ATestAssertException`, and grep `tests/` for other `TimeoutException` expectations on waiters and update them too (review item 4).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): assert exception, gates and event-driven waiters`.

### Task 3: Complete `A2ATestServer` (§3)

**Files:** Modify `A2ATestServer.cs`, `A2ATestServerOptions.cs`, `A2ABinding.cs`; create `TransportMatrix.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/A2ATestServerTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - For each `A2ABinding` in `A2ABindings.All` (JSON-RPC, REST, gRPC; §0.3 item 9): `server.CreateClient(binding)` sends to an echo agent and gets the answer. gRPC goes over a `GrpcChannel` on the `TestServer` handler.
  - `CreateClient(agent: "b")` targets the second of two agents.
  - `CreateClient(record: true)` returns a `RecordingA2AClient` (Task 9; mark this one test as added in Task 9 if Task 9 is not done yet).
  - `CreateHttpClient()` reaches raw endpoints (status codes, SSE).
  - `options.UseKestrel()` binds port 0, reports the real address, and the same client test passes over a socket. Two Kestrel servers started at once both work (no fixed port).
  - `server.Time` is the DI `TimeProvider`; `server.Store`, `server.Handler`, `server.Services`, `server.Events` are the registered instances.
  - Push: the `"a2a-push"` named client's primary handler is `server.PushReceiver` (wired in Task 10; assert only the replacement here), and webhook URL checks allow the double's host.
  - `TransportMatrix` yields one row per binding (an `IEnumerable<object[]>`, usable from xunit `MemberData` and other frameworks).
  - Start time: 20 starts, p50 under 1 s (the loose ceiling of spec principle 4; record the measured p50 in the test output for package 14).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Signature: `CreateClient(A2ABinding binding = A2ABinding.JsonRpc, TestIdentity? identity = null, string? agent = null, bool record = false)` (§0.3 item 10). `CreateClient` uses 10's `IA2AClientFactory` (or `A2AClient.Create` with an `HttpClient` from `TestServer.CreateHandler()`), so real client code runs.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): A2ATestServer over all bindings, Kestrel port-0 mode`.

### Task 4: `TestIdentity` and the test authentication scheme (§3 Auth) — needs 07

**Files:** Create `TestIdentity.cs`, `TestAuthenticationHandler.cs` (internal). Modify `A2ATestServer.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/TestIdentityTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - `TestIdentity.App(tenant, appId, roles...)` → the agent sees `Caller.Kind == Application`, `Owner == OwnerKey.ForApp(tenant, appId).Value`, and the roles.
  - `TestIdentity.User(tenant, userId, scopes..., roles...)` → `Kind == User`, `Owner == OwnerKey.ForUser(tenant, userId).Value`, `scp` holds the scopes.
  - `TestIdentity.Anonymous` → 401 on an `Authenticated` agent (07's default), the real response shape.
  - `CreateClient()` with no identity uses `TestIdentity.Default` (`App("test-tenant", "test-app")`) and succeeds on an `Authenticated` agent (§0.3 item 11).
  - Two users of one app do not see each other's tasks (the real owner resolver runs).
  - A role-protected agent (07 card requirement or policy) gives 403 for an identity without the role.
  - Review Focus 2: when the app registered its own default scheme, `A2ATestServer` does not add the test scheme; a request with the test header is then not authenticated by it.
  - The identity header name is not `Authorization`, and the scheme is never registered by production code (a source scan: `TestAuthenticationHandler` is referenced only from `src/Ambinder.A2A.Testing`).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Claims: `tid`, `azp` and `appid` (app), `oid` (user), `scp` space-separated, `roles` one claim each — 07's names with `MapInboundClaims = false`.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): TestIdentity runs the real owner and auth rules`.

### Task 5: `AgentHarness` (§2)

**Files:** Create `AgentHarness.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/AgentHarnessTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - `AgentHarness.For<THandler>(services => …)` runs the handler through the real `TaskManager`, store and sink with no HTTP; `SendTextAsync` returns the blocking result.
  - `ForAgent(IAgentHandler)`, `ForDelegate(…)` work.
  - `StreamTextAsync` returns a `TaskRun` (Task 7) with all events.
  - `GetAsync`, `CancelAsync`, `SubscribeAsync` work; `Call(owner, tenant, extensions)` acts as another caller (another owner's task → `TaskNotFound`).
  - Default card: text in/out, streaming and push on; overridable. Default owner `test-owner`.
  - `Store`, `Time` (`FakeTimeProvider`), `Events`, `Services` exposed.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Build the same DI as `AddA2AServer` (call it), without `MapA2A`; call `IA2ARequestHandler` with an `A2ACallContext` whose `Caller` is built from the owner (07's types).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): AgentHarness for logic tests`.

### Task 6: `ScriptedAgent` and `FakeAgents` (§4) — needs 11

**Files:** Create `ScriptedAgent.cs`, `ScriptStep*.cs`, `FakeAgents.cs`, `ServiceCollectionExtensions.cs` (`AddScriptedAgent`). Test: `tests/Ambinder.A2A.Testing.Tests/ScriptedAgentTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - Every step: `Reply`, `Working`, `Artifact(id, text | chunks...)` (chunks → append false, true, …, `lastChunk`), `RequireInput`, `RequireAuth`, `Complete`, `Fail`, `Reject`, `Cancel`, `Throw`, `Delay(span)` (moves only when `Time.Advance` is called), `WaitFor(gate)`, `Signal(signal)`, `Run(delegate)`.
  - Selectors: `OnTurn(1)`/`OnTurn(2)` over a multi-turn task; `OnMessage(predicate)`; `Default`. Unmatched input fails the task with a message that lists the selectors.
  - `OnCancel(s => …)` runs on CancelTask; `CancelCalls` counts it.
  - `Received` records each `AgentRequestContext`; token state is recorded.
  - `a2a.AddScriptedAgent("name", agent)` registers a singleton shared by all executions; 50 parallel executions are thread-safe.
  - `FakeAgents.Echo` echoes text.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement on 11's `TaskUpdater`.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): ScriptedAgent`.

### Task 7: Assertions and `TaskRun` (§4)

**Files:** Create `TaskRun.cs`, `Assertions/TaskAssertions.cs`, `Assertions/ArtifactAssert.cs`, `Assertions/StreamAssertions.cs`, `Assertions/EventTable.cs` (internal). Test: `tests/Ambinder.A2A.Testing.Tests/AssertionTests.cs`, `StreamRuleTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "failure-message tests for each assertion"; "`ShouldFollowStreamRules` rejects each broken stream case"):
  - Each `AgentTask` assertion passes on a matching task and fails with `A2ATestAssertException` on a non-matching one; each failure message names the expected and actual value: `ShouldBeInState`, `ShouldBeCompleted`/`Failed`/`Canceled`/`InputRequired`, `ShouldHaveArtifact(id|name)` → `WithText`, `WithPartCount`, `WithMediaType`, `ShouldHaveStatusMessageContaining`, `ShouldHaveHistory`.
  - `TaskRun` collects all events with a timeout; `ShouldHaveStates(...)`, `ShouldBeSingleMessage()`, `ShouldEndIn(state)`.
  - `ShouldFollowStreamRules()` **rejects**: two messages; a task after an update; an event after the final state; mixed task or context ids; an append after `lastChunk` on the same artifact; a message followed by any event; a stream that does not end at a terminal or interrupted state; a task leaving a terminal state. It **accepts**: one message then close; task, status/artifact updates, terminal; task ending `INPUT_REQUIRED`.
  - `ShouldBeValidTask()` rejects missing required fields and a part breaking the oneof rule (`Part.IsValid`).
  - Every stream failure message contains the full event sequence as a table (index, kind, state, artifact id, append, lastChunk).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement as extension methods.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): task and stream assertions with spec stream rules`.

### Task 8: `FakeRemoteAgent` and `AddFakeA2AClient` (§4) — needs 10

**Files:** Create `FakeRemoteAgent.cs`, `NotScriptedException.cs`; extend `ServiceCollectionExtensions.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/FakeRemoteAgentTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - `.OnSend(text => reply)`, `.Responds(SendMessageResponse)`, `.Streams(events)`, `.RequiresInputThenCompletes(question, answer)` (two calls: input-required, then completed with the same task id), `.Fails(error)` (failed task), `.Throws(A2AException)`.
  - An unscripted operation throws `NotScriptedException` naming the operation and showing the method to call.
  - `Card` is the scripted card.
  - `services.AddFakeA2AClient("travel", fake)` after 10's `AddA2AClient("travel", …)` → `IA2AClientFactory.CreateClient("travel")` returns the fake (10's `A2AClientOverride`, last wins); 10's `SendAndWaitAsync` works against the fake.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement `IA2AClient` fully (all operations with `A2ARequestOptions?`).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): FakeRemoteAgent and AddFakeA2AClient`.

### Task 9: `RecordingA2AClient` (§4)

**Files:** Create `RecordingA2AClient.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/RecordingClientTests.cs`.

- [ ] **Step 1: Write failing tests:** wrapping any `IA2AClient`, `Calls` holds operation, request, response or exception, stream events and duration (from the injected `TimeProvider`: advance the fake clock inside a scripted fake and assert the duration); streams are recorded without being buffered (events reach the caller as they come); `server.CreateClient(record: true)` returns one.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): RecordingA2AClient`.

### Task 10: `PushReceiverDouble` (§4) — verification part needs 08

**Files:** Create `PushReceiverDouble.cs`; wire it in `A2ATestServer.cs`. Test: `tests/Ambinder.A2A.Testing.Tests/PushReceiverDoubleTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - A task with a push config on `A2ATestServer` → the double records URL, headers, raw body, parsed `StreamResponse`.
  - `.Respond(500, 500, 200)` → with the fake clock advanced past 08's backoff, three attempts are recorded and the third succeeds (no sleeps).
  - `WaitForAsync(count | predicate, timeout)` is event-driven.
  - Verification result per delivery from 08's receiver-side verifier: valid; invalid signature (tamper the body in a handler test); wrong task id. Read the flags from the public `A2APushNotification` delivered to an `IA2APushHandler` on 08's real receiver endpoint, never from internal types (§0.3 item 12).
  - Optional Kestrel listener (port 0) for real-socket tests.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): PushReceiverDouble`.

### Task 11: Telemetry collectors (§4)

**Files:** `git mv tests/Shared/ActivityCollector.cs tests/Shared/MetricCollector.cs src/Ambinder.A2A.Testing/Telemetry/`; namespace `Ambinder.A2A.Testing.Telemetry`. Remove `<Compile Include="../Shared/*.cs" />` from test projects. Move any other `tests/Shared` file to `tests/Ambinder.A2A.TestSupport/` (§0.3 item 3). Test: `tests/Ambinder.A2A.Testing.Tests/CollectorTests.cs`.

- [ ] **Step 1: Write failing tests:** no `Xunit` reference in the collectors (compile-level: the package has none); `WaitFor…` completes on an activity-stopped signal (no 10 ms poll: a test that would only pass by polling fails, for example the activity stops after the waiter registered, and the waiter completes without any timer tick on a fake clock); trace-id isolation: two parallel tests see only their own spans; a `CancellationToken` parameter stops a wait.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement the changes.
- [ ] **Step 4:** Run the whole suite (all telemetry tests still pass). Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): telemetry collectors in the package`.

### Task 12: `StartClusterAsync` shared-store harness (§3 Cluster)

**Files:** `A2ATestServer.cs` (only if 06 did not add it). Test: `tests/Ambinder.A2A.Testing.Tests/ClusterHarnessTests.cs`.

- [ ] **Step 1:** If `StartClusterAsync` exists (06), check its signature against spec §3 and keep it; add only a test that `n` servers share one store and one fake clock. Otherwise write that failing test.
- [ ] **Step 2:** Run. Expected: FAIL (or PASS if 06 built it).
- [ ] **Step 3:** Implement `StartClusterAsync(int n, Action<A2AServerBuilder>, Action<IServiceCollection>? = null, Action<A2ATestServerOptions>? = null)` → `IReadOnlyList<A2ATestServer>`. No forwarding (06's).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): shared-store cluster harness`.

### Task 13: `Ambinder.A2A.Testing.AI` (§4) — needs 11

**Files:** `git mv tests/Ambinder.A2A.Extensions.AI.Tests/Fakes/ScriptedChatClient.cs src/Ambinder.A2A.Testing.AI/`; create `FakeChatClient.cs` (moved from `samples/AgentServer/Llm/FakeChatClient.cs`; the sample keeps its own copy). Test: `tests/Ambinder.A2A.Testing.Tests/ChatClientTests.cs` (or a `Testing.AI.Tests` folder in that project).

- [ ] **Step 1: Write failing tests:** scripted answers by turn and by predicate; streaming word by word; throws; waits on a `Gate`; records received `ChatMessage`s; `FakeChatClient` echoes. 11's `Extensions.AI.Tests` now use the package type (delete the test-only copy).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement and switch 11's tests. 11's double arrives with namespace `Ambinder.A2A.Testing.AI` already (11 plan Task 15), so no rename. Replace its `TaskCompletionSource` wait hook with `Gate`, and its plain exceptions with `A2ATestAssertException` (review item 13).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(testing): Testing.AI with ScriptedChatClient and FakeChatClient`.

### Task 14: Publish the conformance suite (§5) — needs 05

**Files:** `git mv tests/Ambinder.A2A.Stores.Conformance/* src/Ambinder.A2A.Testing.Conformance/`; update all store test projects' references; README. Test: `tests/Ambinder.A2A.Testing.Tests/Conformance/BrokenStoreTests.cs`.

- [ ] **Step 1: Write failing tests:**
  - A deliberately broken in-memory store (for example: lists ascending instead of descending; ignores the tenant) fails the **named** cases `list.order-…` and the tenant-isolation case (assert by case id, Review Focus 4).
  - The case id list after the move equals the list before (snapshot test from Task 0's run).
  - A skipped case shows its reason in the test output.
  - NUnit/MSTest path: a plain loop over `TaskStoreConformanceCases.All` with a fresh `StoreSet` runs without xunit types.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Move; apply §0.3 item 2 (drop `xunit.v3.assert`). The in-memory, EF Core and Cosmos store test projects subclass from the package. Two checks first (review item 12):
  - 05's abstract classes call `Assert.Skip(reason)` (dynamic skip by capability) and `TestContext.Current.CancellationToken` (05 plan Task 5). Find which xunit v3 package defines each in the pinned version. If `Assert.Skip` lives only in `xunit.v3.assert`, keep that reference and record the deviation from spec §1 for the owner. Capability skips must keep showing their reason.
  - Swap 05's `ConformanceFailedException` for `A2ATestAssertException` (05 plan Task 5 left this to 12). Case ids do not change.
- [ ] **Step 4:** Run all store suites. Expected: PASS. `dotnet pack` the package.
- [ ] **Step 5:** Commit `feat(testing): publish Ambinder.A2A.Testing.Conformance`.

### Task 15: Hello-world test and framework-neutrality proof (Goal 1, principle 2)

**Files:** `tests/Ambinder.A2A.Testing.Tests/HelloWorldTests.cs`, `FrameworkNeutralityTests.cs`.

- [ ] **Step 1: Write the test** exactly as it will appear in the docs: at most 12 lines including the method signature (start `A2ATestServer` with `FakeAgents.Echo`, create a client, send, assert with `ShouldBeCompleted` or the reply text). A second test counts its non-blank lines from the source file region (marked with `// docs:hello-start` / `// docs:hello-end`) and fails above 12.
- [ ] **Step 2:** Framework-neutrality: §0.3 item 4.
- [ ] **Step 3:** Run. Expected: PASS.
- [ ] **Step 4:** Commit `test(testing): 12-line hello-world test and framework-neutral run`.

---

## Phase B — Migrate the repo's tests (§7)

### Task 16: New project structure

**Files:** Create `tests/Ambinder.A2A.Samples.Tests/`, `tests/Ambinder.A2A.Repo.Tests/`. Move `…Integration.Tests/Flows/*` and `…/Samples/*` to `Samples.Tests`; `…/Repo/*` to `Repo.Tests`; `Fixtures/A2ATestHost.cs` to `Samples.Tests` (it keeps its port base). If 10 did not create `tests/Ambinder.A2A.Client.Tests`, create it and move `…Integration.Tests/Client/*` unit parts there (`TransportParityTests` stays in Integration on `A2ATestServer`). Create `tests/Ambinder.A2A.Extensions.AI.Tests` only if 11 did not.

- [ ] **Step 1:** Moves only (no test changes). Update `.csproj` references: only `Samples.Tests` references `samples/`.
- [ ] **Step 2:** Run the whole suite. Expected: the same test-case count as Task 0 (Review Focus 5) and green.
- [ ] **Step 3:** Commit `test: split Samples.Tests, Repo.Tests and Client.Tests`.

### Task 17: `Server.Tests` onto the package

**Files:** `tests/Ambinder.A2A.Server.Tests/**`; delete `TestAgents.cs`.

- [ ] **Step 1:** Replace `TestHost` with `AgentHarness`, `AgentScripts`/`ScriptedAgent` with the package `ScriptedAgent`, `Gate`/`Signal` with the package ones, `FixedTimeProvider` with `FakeTimeProvider`.
- [ ] **Step 2:** `InMemoryTaskStoreTests` becomes a subclass of `TaskStoreConformanceTests`; keep only in-memory-specific tests in it.
- [ ] **Step 3:** Run. Expected: green; case count not lower except duplicates now covered by conformance cases (list them in the commit message).
- [ ] **Step 4:** Commit `test(server): use AgentHarness and ScriptedAgent; delete TestAgents.cs`.

### Task 18: `Integration.Tests` onto the package

**Files:** `tests/Ambinder.A2A.Integration.Tests/**`; delete `Fixtures/MinimalServer.cs`, `Fixtures/TransportMatrix.cs`, `tests/Shared/`.

- [ ] **Step 1:** Replace `MinimalServer` with `A2ATestServer`; the `Protected` agent becomes tests with `TestIdentity`. Replace `TransportMatrix.cs` with the package `TransportMatrix`/`A2ABindings.All`. Tests that relied on anonymous calls either pass `TestIdentity.Anonymous` to an `AllowAnonymous()` agent or use the default identity (§0.3 item 11). Library push tests use `PushReceiverDouble` instead of the sample `WebhookReceiver`.
- [ ] **Step 2:** Run. Expected: green, case count not lower.
- [ ] **Step 3:** Commit `test(integration): use A2ATestServer; delete MinimalServer and tests/Shared`.

### Task 19: Remove sleeps and fixed ports; enforce the rules

**Files:** every file from Task 0's sleep and port lists; `tests/BannedSymbols.Tests.txt`; `tests/Directory.Build.props` (add `Microsoft.CodeAnalysis.BannedApiAnalyzers` with that file for test projects); `tests/Ambinder.A2A.Repo.Tests/StructureRulesTests.cs`.

- [ ] **Step 1: Write failing `Repo.Tests`** (spec §7 rules 1–3): no `ProjectReference` to `samples/` outside `Samples.Tests` (parse every `.csproj`); no fixed port outside `Samples.Tests` (scan for `UseUrls`, `ListenLocalhost(\d+)`, `localhost:\d{4,5}`, `A2A_TEST_PORT_BASE`). Deployable hosts are not tests and are excluded by name: `tests/Ambinder.A2A.Tck.Sut` (03), `tests/Ambinder.A2A.Cluster.TckHost` and `tests/Ambinder.A2A.Cluster.KindHost` (06), and 16's `itk/` agent (review item 9); no `Thread.Sleep`/`Task.Delay` in tests outside `src/Ambinder.A2A.Testing`.
- [ ] **Step 2:** Run. Expected: FAIL listing the offenders.
- [ ] **Step 3:** Replace each sleep with `Events.WaitForAsync`, `FakeTimeProvider.Advance` or `Gate`/`Signal`; each fixed port with the in-memory host or port 0. Add the banned-symbol file (`T:System.Threading.Thread.Sleep`… and `M:System.Threading.Tasks.Task.Delay(…)` overloads, each with a message naming the replacement).
- [ ] **Step 4:** Run the whole suite three times in a row. Expected: green every time; Repo.Tests green; final case count ≥ Task 0 baseline minus documented duplicates.
- [ ] **Step 5:** Commit `test: no sleeps or fixed ports; structure rules enforced`.

### Task 20: Stream rules on every streaming test (principle 1)

**Files:** streaming tests in `Server.Tests`, `Integration.Tests`.

- [ ] **Step 1:** Add `.ShouldFollowStreamRules()` to every test that reads a stream (grep `SendStreamingMessage`, `StreamTextAsync`, `SubscribeToTask`, `StreamAsync`).
- [ ] **Step 2:** Mutation check: temporarily make `TaskManager` (or the writer) emit the terminal event twice; run `Server.Tests`; at least one stream test fails through the helper; revert. Record the failing test name in the commit message.
- [ ] **Step 3:** Run. Expected: green.
- [ ] **Step 4:** Commit `test: stream rule checks on every streaming test`.

### Task 21: `testdata/` golden files (§7)

**Files:** create `tests/vectors/protocol/*.json` from `tests/…Protocol.Tests/Samples.cs`; update readers.

Golden root (review item 10): 04 (`tests/vectors/card-signing/`) and 16 (`tests/vectors/wire/v1.0/`, plus `interop/promote-capture.py` and the fixtures CLI) already keep golden files under `tests/vectors/`. Moving those trees would touch 16's tooling and peer images for no gain. So the one golden root is `tests/vectors/`, not the spec's `testdata/`. Record this in the PR body as an owner check; if the owner wants `testdata/`, move all three trees and the tooling paths in one later commit.

- [ ] **Step 1:** Move each JSON sample to `tests/vectors/protocol/<constant name>.json`; `Protocol.Tests` reads them through a small `Golden.Read(name)` helper (path from `AppContext.BaseDirectory` with the files copied as `None` items).
- [ ] **Step 2:** Run. Expected: byte-identical results; green.
- [ ] **Step 3:** Commit `test: protocol golden files under tests/vectors/`.

---

## Phase C — Quality gates (§6) and new test kinds

### Task 22: Test-side analyzers and the assertion-free check

**Files:** `tests/Directory.Build.props`, `samples/Directory.Build.props` (`AnalysisLevel latest`, `AnalysisMode Default`), `.editorconfig` (a `[tests/**.cs]` section if needed), `tests/Ambinder.A2A.Repo.Tests/AssertionRulesTests.cs`.

- [ ] **Step 1:** Check 02's `src/` settings (Task 0). Do not change them. If any `src/` item from spec §6 is missing, list it as "02 open" in the PR body.
- [ ] **Step 2: Write failing test** (§0.3 item 6): the source scan finds a sample assertion-free `[Fact]` in a fixture file under `Repo.Tests/TestData/` and reports it.
- [ ] **Step 3:** Turn on the xunit analyzer rules as errors for empty test bodies and the `xUnit2013` family; fix every finding in the repo in this PR (spec: "fixed in the same PR, never baselined").
- [ ] **Step 4:** `dotnet build Ambinder.A2A.slnx` clean; run Repo.Tests. Expected: PASS.
- [ ] **Step 5:** Commit `build: test-side analyzers and assertion-free test check`.

### Task 23: `security.yml` and the nightly audit

**Files:** `.github/workflows/security.yml`; the audit job in `.github/workflows/nightly.yml` (created here or in Task 25); `.github/dependabot.yml`.

- [ ] **Step 1: Write the workflow:**
  - CodeQL (`github/codeql-action` init/analyze), language C#, manual build (`dotnet build Ambinder.A2A.slnx`), `queries: security-extended`; triggers `pull_request`, `push` to master, weekly `schedule`. `permissions: { contents: read, security-events: write }`.
  - Dependency review (`actions/dependency-review-action`) on `pull_request`: `fail-on-severity: high`; `deny-licenses` for the GPL/AGPL family (`GPL-2.0-only`, `GPL-2.0-or-later`, `GPL-3.0-only`, `GPL-3.0-or-later`, `AGPL-3.0-only`, `AGPL-3.0-or-later`). The spec does not say whether LGPL counts; leave LGPL allowed and list it as a question for the owner in the PR body.
  - OpenSSF Scorecard (`ossf/scorecard-action`) weekly, SARIF upload.
  - Every third-party action pinned to a full commit SHA (01 §6); `permissions` minimal per job; forks get no write tokens (`pull_request`, not `pull_request_target`; Review Focus 7).
  - Nightly audit job: `dotnet list Ambinder.A2A.slnx package --vulnerable --include-transitive`; fail on any finding; on failure open (or update) one issue titled "Nightly vulnerability audit failed" through `actions/github-script` with `issues: write` on that job only.
- [ ] **Step 2:** Dependabot: groups NuGet updates; `github-actions` ecosystem present (it exists; add groups if missing).
- [ ] **Step 3:** Validate YAML (`actionlint` if available, else a YAML parse). Push and confirm the workflow runs on the PR.
- [ ] **Step 4:** Acceptance "a test PR with a High or Critical vulnerable package is blocked (shown once, recorded in the PR)": open a throwaway draft PR that adds a known-vulnerable package version, record the failing check link in this package's PR body, close the throwaway PR without merging, delete its branch.
- [ ] **Step 5:** Commit `ci: CodeQL, dependency review, Scorecard and nightly audit`.

### Task 24: Coverage collection and gates

**Files:** `eng/coverage/gate.sh`, `eng/coverage/thresholds.json`, `eng/coverage/reportgenerator.config`, `tests/Directory.Build.props` (coverage extension package for Microsoft.Testing.Platform), `ci.yml` (`build-test` job steps).

- [ ] **Step 1: Write the gate script tests** (bash, run in CI before the gate): given a sample Cobertura file under `eng/coverage/testdata/`, the script fails when a scope is under its line or branch threshold and passes otherwise; it fails when the touched packages' total drops more than 1 point against a given master summary.
- [ ] **Step 2:** Run them. Expected: FAIL (no script).
- [ ] **Step 3: Implement:**
  - Run tests with `--coverage --coverage-output-format cobertura`; merge with `dotnet-coverage merge`; render HTML and Markdown summary with ReportGenerator; upload both as artifacts; append the Markdown to `$GITHUB_STEP_SUMMARY`.
  - Exclusions only: `[GeneratedCode]`, proto output, source-generated JSON, `samples/`, `tests/`.
  - Thresholds per spec table: Protocol and Server 90/80; Client, Extensions.AI, Testing 85/75; stores and cluster 80/70.
  - Master baseline: the `build-test` job on `push` to master uploads its summary as an artifact; PR runs download the latest one (`actions/download-artifact` with run lookup via `gh api` is acceptable) for the 1-point delta. If none exists yet, the delta check is skipped with a notice.
- [ ] **Step 4:** Run in CI. If a scope is under its threshold, add tests for real behavior in this PR (never exclusions). Record final numbers in the PR body.
- [ ] **Step 5:** Commit `ci: coverage collection and gates`.

### Task 25: CI layout and run time

**Files:** `.github/workflows/ci.yml`, `.github/workflows/nightly.yml`.

- [ ] **Step 1:** Split `ci.yml` into jobs per spec §6: `build-test` (restore, build with analyzers, unit + library integration tests, coverage + gates), `samples` (build samples, `Samples.Tests`), plus the slots that call 03's `tck`, 16's `interop-smoke` (and `interop-full` nightly, C15) and 14's `bench` reusable workflows when they exist. Keep every 01 job, and the jobs and workflows other packages added: 05's `stores-containers` job and its SQL-script and migration drift steps, 06's `tck-cluster` leg and container suite, 09's `aks-kind.yml`/`aks-live.yml` (review item 8). Jobs run in parallel.
- [ ] **Step 2:** `nightly.yml` (`schedule` + `workflow_dispatch`; 05 may have created it already — extend it, keep 05's soak and `Db` benchmark steps): the audit (Task 23), `Stress.Tests` (Task 26), the FsCheck properties with a higher iteration count, Stryker.NET on Protocol and Server task logic (advisory: score to the job summary, never fails), and a call to 01's Windows leg if 01 put it elsewhere. Nightly and release builds use 01's strict audit flag (C1).
- [ ] **Step 3:** `build-test` prints its test run time to the job summary and fails above 6 minutes.
- [ ] **Step 4:** Push and confirm all jobs run. Record the `build-test` time.
- [ ] **Step 5:** Commit `ci: job layout and nightly workflow`.

### Task 26: `Stress.Tests`: soak, leak and fuzz (§7 new kinds)

**Files:** `tests/Ambinder.A2A.Stress.Tests/` (05 plan Task 31 may have created it with its in-memory store soak test under trait `Category=Soak`; extend it. Use one trait: retag 05's test to `Category=Stress` and change 05's CI filter `Category!=Soak` to `Category!=Stress` in the same commit. Excluded from `build-test`; review item 6), `Directory.Packages.props` (FsCheck, newest stable), FsCheck properties in `Protocol.Tests` and `Client.Tests`.

- [ ] **Step 1: Write the soak test:** on one long-lived `A2ATestServer`: create and finish 10,000 tasks; open and drop 1,000 subscribed streams; create 1,000 push configs. After warm-up and after the run, force GC (`GC.Collect(2, Forced, true, true)` twice with `WaitForPendingFinalizers`) and compare managed memory and the sizes of the lock table (05's `TaskLockTable.Count`), running-task table (09's `AgentExecutionHost.Running`), event bus (06's `LocalTaskEventBus` key count; `TaskEventBroker` if 06 has not landed) and pending push work (08 replaced the in-memory queue with the outbox: `IOutboxStore.GetPendingStatsAsync().Pending`, which must return to 0 once deliveries finish; review item 7) (read through internal counters exposed to the test assembly by `InternalsVisibleTo` on `Ambinder.A2A.Server`, which 01 rule 6 allows for test assemblies). Growth must be under 10% above warm-up.
- [ ] **Step 2: Write the properties:** JSON round trip for every protocol model (generators from the model types, using `A2AJson` typed metadata); the SSE reader yields the same events for any split of a valid byte stream, including 1-byte chunks; malformed input throws only `A2AException` and never allocates beyond the client's size limits (measure with `GC.GetAllocatedBytesForCurrentThread` around the read).
- [ ] **Step 3:** Run locally: `dotnet test tests/Ambinder.A2A.Stress.Tests`. Expected: PASS (fix real leaks in the owning package's code only if the fix is small and obviously local; otherwise report to that package and mark the test as failing in the PR body — never loosen the 10% bound).
- [ ] **Step 4:** Commit `test: soak, leak and fuzz tests`. Acceptance "pass nightly three times in a row" is recorded in the PR body after three nightly runs.

### Task 27: Docs "Testing your agent" and "Testing your client"

**Files:** `docs/agents/test-your-agent.md`, `docs/clients/test-your-client.md` (13's tree, front matter with `owner: 12`).

- [ ] **Step 1:** Write the pages: the 12-line hello-world test (from Task 15's marked region, through 13's snippet mechanism if present), `AgentHarness` vs `A2ATestServer`, `ScriptedAgent`, `TestIdentity`, fake time, `PushReceiverDouble`, `FakeRemoteAgent`/`AddFakeA2AClient`, `RecordingA2AClient`, stream rule assertions, the conformance suite for store authors (one derived class), and the NUnit/MSTest loop.
- [ ] **Step 2:** If 13's `DocsLint` or the repo's docs tests exist, run them. Expected: PASS.
- [ ] **Step 3:** Commit `docs: testing your agent and your client`.

### Task 28: Hygiene, owner steps and final verification

- [ ] **Step 1:** `.gitignore`: ensure `TestResults/`, `coverage/`, `*.trx`. `git rm -r --cached` any tracked `TestResults` folder.
- [ ] **Step 2:** Grep rules (acceptance):

```bash
test ! -d tests/Shared && ! git ls-files | grep -E 'TestAgents\.cs|MinimalServer\.cs' && echo ok
```

- [ ] **Step 3:** `dotnet build Ambinder.A2A.slnx -c Release`, `dotnet test Ambinder.A2A.slnx`, `dotnet pack` of the three packages. Expected: green; packages listed in the API tracking files (02).
- [ ] **Step 4: Owner steps** (§0.3 item 8): ask the owner to set required checks (`build-test`, `security` now; `tck` and `interop-smoke` two weeks after each lands, C13 and C15) and to turn on secret scanning and push protection; record the evidence (settings export or screenshot) in the PR, as the acceptance list asks.
- [ ] **Step 5:** Walk the spec's acceptance list and tick each item in the PR body with the test, job or link that proves it, or "open: <reason>".
- [ ] **Step 6:** Open the PR. Body: Task 0 gate table and baseline, acceptance checklist, coverage numbers, `build-test` time, owner steps.

---

## Acceptance criteria → tasks

| Spec acceptance item | Task |
|---|---|
| Three packages build, pack, are API-tracked; `Testing` has no test framework | 1, 28 |
| `AgentHarness`, `A2ATestServer` (3 bindings, `TestIdentity`, fake time, push double) tested, incl. failure messages | 2, 3, 4, 5, 7, 10 (verification part: owner check, §0.3 item 12) |
| Hello-world test ≤ 12 lines, in docs and compiled | 15, 27 |
| `ShouldFollowStreamRules` rejects each broken case | 7 |
| Conformance suite on in-memory, EF Core, Cosmos; broken store fails named cases | 14 |
| All repo tests migrated; grep and structure rules enforced | 16–19, 28 |
| `Client.Tests` exists, client tests moved | 16 (or 10) |
| Analyzer settings on, build clean, `CS1591` error in `src/` | 22 (test side), 02 (`src/`) |
| `security.yml` with CodeQL, dependency review, nightly audit; vulnerable PR blocked once | 23 |
| Coverage collected and gated; report artifact | 24 |
| Soak and fuzz tests pass nightly three times | 26 |
| CI job layout; required checks set (C13) | 25, 28 |
| M1 slice exists and is used by 05 and 09 (B2) | 0 (verify), 1 |
| Low/Moderate advisory warns on PR; High/Critical fails; nightly fails on both (C1) | 23, 25 (01's levels kept) |
| Secret scanning and push protection on | 28 (owner) |
| `TestResults/` ignored and removed | 28 |
| `build-test` run time reported, under 6 minutes | 25 |

---

## Execution notes

- **Method:** execute this plan with **superpowers:subagent-driven-development**: one fresh implementer subagent per task, then that skill's spec-compliance review and code-quality review before the next task starts.
- **Model:** every subagent (implementers and both reviewers) runs on model **`sonnet`**. Pass `model: "sonnet"` on every subagent dispatch. This follows the owner's rule: planning on Opus, execution on Sonnet. If a task is too large on `sonnet`, split it; do not switch models.
- **Order:** Phase A in numbered order, then B, then C. Phase B must start only after Tasks 1–11 are done (the migration needs the helpers). Tasks 23–25 touch workflows and can run after Task 22 in any order, one at a time.
- **Gates:** §0.2. Stop before Task 4 without 07, Task 6 and Task 13 without 11, Task 8 without 10, Task 14 without 05's suite. Report what landed and what waits.
- **Each implementer's brief:** its task text; §0, Global Constraints and Review Focus; the Task 0 inventory and baseline; the spec path. Tell it the spec is binding, to keep the slice's and suite's names and case ids, to add no public API beyond spec §1–§5 (the "Provides" list), and to stop and report when another package's landed code differs from what this plan assumes.
- **Each reviewer's brief:** the task text, the spec sections it cites, and the diff. For Phase B, the reviewer also compares test-case counts before and after.
- **Push after every task**, so a reclaimed container loses nothing.
- **This plan file** is changed during execution only to tick checkboxes.
