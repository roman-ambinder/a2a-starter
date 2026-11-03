# SDK 12: Testing package and quality

Date: 2026-11-03
Status: draft for approval
Milestone: M2 (`1.0.0`). An internal slice of the test harness is built in M1 as repo test code (B2), and the store conformance suite starts in M1 inside package 05; this package makes both public in M2.
Size: L (1–3 weeks), including the repo test migration *(Claude's call, C13; the work plan table is updated)*.

## Intent

Two jobs.

1. **Ship test tools for product teams.** A team that builds an agent or calls agents needs a fast in-memory host, fake agents, fake and recording clients, assertions for task events, a push receiver double, and a suite that proves a custom store is correct. Today these exist only inside this repo, tied to samples and fixed ports. The package `Ambinder.A2A.Testing` (and two small siblings) makes them public.
2. **Raise the quality bar of the repo itself.** Turn on analyzers, security scanning and coverage gates. Fix the test structure (client tests inside the integration project, tests that go through samples, fixed ports, fixed sleeps). Move the repo's own tests onto the new package, so the package is tested by real use.

## Findings addressed

- R38: no testing package for product teams.
- R39: test structure and coverage gaps (no client test project, library tested through samples, no coverage gate, no load/soak/fuzz/multi-instance tests).
- R40: no analyzers or security scanning beyond defaults.
- R41 (part): `TestResults/` folders not ignored.
- R14 (part): store conformance suite as a public package. The cases are written by package 05.
- Pain point 21 (testing and mocking agents): in-memory host, fake agents, `WebApplicationFactory`-style helpers, a TCK step in CI (the TCK job itself is package 03).
- Pain point 12 (persistent store, push): "test fakes" for store authors and push receivers.
- Pain point 20 (observability): telemetry collectors made public.

## Goals

1. `dotnet add package Ambinder.A2A.Testing` gives a test of an agent with no setup: start an in-memory host, send a message with a real client over any of the three bindings, assert the result. A hello-world test is at most 12 lines.
2. Two levels of host: `AgentHarness` (the full `TaskManager`, no HTTP, for logic tests) and `A2ATestServer` (full ASP.NET Core pipeline on `TestServer`, JSON-RPC, REST and gRPC, no sockets, no ports).
3. A scripted fake agent, a fake remote agent (client side), a recording client, a scripted chat client, and a push receiver double. All are deterministic and need no sleeps.
4. Assertion helpers check task event sequences and spec stream rules, with failure messages that print the whole event sequence.
5. Waiting is event-driven. No helper in the package uses a fixed delay. Time is a `FakeTimeProvider` the test controls.
6. `Ambinder.A2A.Testing.Conformance` ships the store conformance suite (cases from package 05). A third-party store passes or fails it with one derived class.
7. The package has no dependency on a test framework (xunit, NUnit, MSTest). Only the conformance package depends on xunit v3.
8. Analyzer, security and coverage gates run in CI on every PR, and the nightly job runs the vulnerability audit, stress and fuzz tests.
9. Test projects follow one structure (section 5). No test project outside `Samples.Tests` references anything under `samples/`. No test uses a fixed port or `Task.Delay`/`Thread.Sleep` for waiting.
10. The repo's existing tests use the package. The old copies (`TestAgents.cs`, `tests/Shared`, `MinimalServer`, `A2ATestHost` for library tests) are deleted.

## Non-goals

- The store cases themselves and the store contract (05). This package packages, names and publishes them.
- The TCK job and its harness (03), cross-SDK interop (16), benchmarks (14). This package defines how CI is split and where those jobs plug in.
- A fake Entra tenant for product teams. `MockEntra` (and 07's `FakeEntraAuthority`) stays a repo-only test tool *(Claude's call, C13)*. Product teams use `TestIdentity` to skip token validation.
- A UI or CLI test tool (inspector, A2A CLI). Out of scope.
- Verifying a team's own LLM quality (evals). `ScriptedChatClient` only makes tests deterministic.
- Browser or Playwright tests.
- Release pipeline, SBOM, signing of packages (01).

## Anti-goals

- A testing package that forces xunit, FluentAssertions-style licenses, or a specific mocking library on users.
- Helpers that hide real behavior. The fake host must run the real pipeline (auth, validation, serialization), so a green test means the agent works on the wire.
- Tests that pass because they sleep long enough.
- Coverage numbers that are met by excluding code or by tests without assertions.
- A gate that breaks every PR because of an unrelated new advisory or analyzer rule (see section 6).
- Two different fake-agent systems (one in the repo, one public).

## Constraints

- Names: `Ambinder.A2A.Testing`, `Ambinder.A2A.Testing.AI`, `Ambinder.A2A.Testing.Conformance`; .NET 10; Apache 2.0.
- xunit v3 4.0.2 with Microsoft.Testing.Platform stays the repo's runner (`global.json`, `Directory.Packages.props`).
- The package versions move together with the SDK version (01). The public API is tracked (02). The conformance package may add cases in minor versions; each addition is listed in the changelog.
- Spec rules that the stream assertions check: a stream is exactly one Message then close, or a Task first then zero or more status/artifact events, closing at a terminal or interrupted state; events are ordered; `lastChunk` follows `append` rules; a task never leaves a terminal state (research-spec §3, spec 3.1.2, 3.5).
- Hosting must support `TestServer`: no fixed-port assumptions in server code (package 09 removes gRPC routing by listening port, R21), and every time-dependent service reads `TimeProvider` from DI.
- One builder, no fixed dates (owner decision O2). Merge gates are automated required checks; there is no second human reviewer.
- Dependencies of the packages: `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), `Microsoft.Extensions.AI.Abstractions` (Testing.AI only), `xunit.v3.extensibility.core` and the Testing package (Testing.Conformance only). Versions go into `Directory.Packages.props`.

## Design

### 1. Packages

| Package | Contents | Depends on |
|---|---|---|
| `Ambinder.A2A.Testing` | `AgentHarness`, `A2ATestServer`, `ScriptedAgent`, `FakeRemoteAgent`, `RecordingA2AClient`, `PushReceiverDouble`, `TestIdentity`, assertions, telemetry collectors, waiters | Server, Client, Protocol, AspNetCore (`A2ATestServer` uses ASP.NET Core TestServer), ASP.NET Core TestHost, TimeProvider.Testing |
| `Ambinder.A2A.Testing.AI` | `ScriptedChatClient`, `FakeChatClient` | Testing, Extensions.AI, Microsoft.Extensions.AI.Abstractions |
| `Ambinder.A2A.Testing.Conformance` | The four suites from package 05: `TaskStoreConformanceTests`, `PushConfigStoreConformanceTests`, `OutboxStoreConformanceTests`, `InstanceStoreConformanceTests`, and their case catalogs | Testing, `xunit.v3.extensibility.core` |

Why three *(writer's call)*: the core Testing package must not pull in xunit or an LLM abstraction; teams take only what they use.

Failures use `A2ATestAssertException : Exception` so every test framework reports them.

### 1a. M1 internal slice *(Claude's call, B2)*

Packages 05 and 09 need a harness in M1, before this package ships. So a slice is built in M1 as repo-internal test code (a non-packable project under `tests/`, for example `tests/Ambinder.A2A.TestHarness`):

- `A2ATestServer` on `TestServer` (no ports), JSON-RPC and REST first; gRPC when 09's routing lands.
- `FakeTimeProvider` wiring (`server.Time`).
- `TaskEventLog` and its event-driven waiters (`WaitForAsync`, `WaitForTaskStateAsync`).
- `StoreSet` (the store bundle the conformance suite uses, 05 §7.13).

05 and 09 use it in M1. In M2 this package moves it to `src/` and publishes it in `Ambinder.A2A.Testing`, without renaming the types. From M1 on, no new test is written on fixed ports or with sleeps; the existing ones are migrated in M2 (section 7).

### 2. `AgentHarness` (logic level)

Runs a handler through the real `TaskManager`, store, broker and sink with no HTTP. It replaces `TestHost` in `tests/A2A.Server.Tests/TestAgents.cs`.

```csharp
await using var h = AgentHarness.For<MyAgent>(services => services.AddSingleton<IReportService, FakeReports>());
var result = await h.SendTextAsync("write a report on cats");   // blocking send
result.Task.ShouldBeCompleted().ShouldHaveArtifact("report.md").WithText("cats");
```

- `For<THandler>(configureServices?, card?)`, `ForAgent(IAgentHandler)`, `ForDelegate(...)`.
- Methods: `SendTextAsync`, `SendAsync(SendMessageRequest)`, `StreamTextAsync` (returns a `TaskRun`, see section 4), `GetAsync`, `CancelAsync`, `SubscribeAsync`, `Call(owner, tenant, extensions)` to act as another caller.
- Exposes `Store` (in-memory `ITaskStore`), `Time` (`FakeTimeProvider`), `Events` (every task event, section 4), `Services`.
- Default card: text in/out, streaming and push enabled *(writer's call)*; override with a parameter.
- Default caller: `owner = "test-owner"`.

### 3. `A2ATestServer` (wire level)

A real ASP.NET Core pipeline hosted on `TestServer`. No socket, no port. Replaces `MinimalServer` and the library-test use of `A2ATestHost`.

```csharp
await using var server = await A2ATestServer.StartAsync(a2a => a2a.AddAgent<MyAgent>("my", card));
var client = server.CreateClient();                    // default binding: JSON-RPC
foreach (var binding in A2ABinding.All) { ... server.CreateClient(binding) ... }
```

- `StartAsync(Action<A2AServerBuilder>, Action<IServiceCollection>? services = null, Action<A2ATestServerOptions>? options = null)`.
- `CreateClient(A2ABinding binding = JsonRpc, TestIdentity? as = null, string? agent = null)` returns a real `IA2AClient` from the SDK client factory, wired to the in-memory handler (gRPC uses `GrpcChannel` over the `TestServer` handler). So client and server code both run for real.
- `CreateHttpClient()` for raw wire tests (headers, status codes, SSE).
- `A2ABinding.All` and `TransportMatrix` (an xunit/other `IEnumerable<object[]>` data source) replace `TransportMatrix.cs`.
- Mode `Kestrel`: `options.UseKestrel()` listens on port 0 and reports the real address. For tests that need real sockets (SSE flush behavior, HTTP/2 over cleartext, graceful shutdown, a push receiver on a real port). Never a fixed port, so tests run in parallel *(writer's call)*.
- Auth: a test authentication scheme is registered when the app has none. `CreateClient(as: TestIdentity.App(tenant, appId, roles...))`, `TestIdentity.User(tenant, userId, scopes..., roles...)`, `TestIdentity.Anonymous`. It sends the identity in a header only the test scheme reads and builds the claims package 07 expects (tenant, app id or user id, `scp`, `roles`), so the real owner resolver and the real authorization policies run. It does not validate tokens; testing token validation is package 07's job with its `FakeEntraAuthority` (built from `MockEntra`'s token issuer, repo-only, C13).
- Time: `server.Time` is the `FakeTimeProvider` registered as `TimeProvider` (push backoff, leases, card cache and similar read it).
- Push: the named `HttpClient` that package 08's `PushDeliveryService` uses (`"a2a-push"`) gets its primary handler replaced by `server.PushReceiver` (section 4), and webhook URL checks allow the double's host. No sockets. Real-socket variants use `Kestrel` mode plus a receiver built on `Ambinder.A2A.Push.Receiver` (08).
- Observation: `server.Events` (every task event, in order), `server.Store`, `server.Handler` (`IA2ARequestHandler`), `server.Services`.
- Cluster: `A2ATestServer.StartClusterAsync(n, ...)` starts `n` servers that share one store and one fake time source, for package 06's tests. 06 adds the forwarding wiring; this package gives only the shared-store harness.
- Startup cost target: in-memory start under 150 ms p50 on a CI runner. Tests share a server through the framework's class/collection fixture.

### 4. Fakes, recorders and doubles

**`ScriptedAgent`** (replaces `AgentScripts`/`ScriptedAgent` in `tests/A2A.Server.Tests/TestAgents.cs`). A fluent script on top of `TaskUpdater` (package 11):

```csharp
var agent = new ScriptedAgent()
    .OnTurn(1, s => s.Working("thinking").Artifact("answer", "42").Complete())
    .OnTurn(2, s => s.RequireInput("which city?"))
    .OnCancel(s => s.Cancel());
```

- Steps: `Reply(text)`, `Working(text?)`, `Artifact(id, text | chunks...)`, `RequireInput`, `RequireAuth`, `Complete`, `Fail(text)`, `Reject`, `Cancel`, `Throw(Exception)`, `Delay(TimeSpan)` (uses `TimeProvider`, so a fake clock moves it), `WaitFor(Gate)`, `Signal(Signal)`, `Run(delegate)` for anything else.
- Selectors: `OnTurn(n)`, `OnMessage(predicate)`, `Default`. Unmatched input fails the task with a clear message listing the selectors.
- Records `Received` (every `AgentRequestContext`), `CancelCalls`, and the token state. `Gate` and `Signal` move here from `TestAgents.cs`.
- Register: `a2a.AddScriptedAgent("name", agent)`. It is a singleton, shared by all executions, and thread-safe.
- `FakeAgents.Echo` for the trivial case.

**`FakeRemoteAgent`** (client-side fake of an agent a team calls): implements `IA2AClient` with scripted responses and a card. `.OnSend(text => reply)`, `.Responds(SendMessageResponse)`, `.Streams(params StreamResponse[])`, `.RequiresInputThenCompletes(question, answer)`, `.Fails(error)`, `.Throws(A2AException)`. Unscripted operations throw `NotScriptedException` that names the operation and shows how to script it. `services.AddFakeA2AClient("travel", fake)` replaces the client registered by package 10's `AddA2AClient("travel", ...)` *(10 names the seam)*. Teams can also host a `ScriptedAgent` on an `A2ATestServer` when they want the real wire.

**`RecordingA2AClient`**: wraps any `IA2AClient`. Keeps `Calls`: operation, request, response or exception, stream events, and duration (from `TimeProvider`). Can be put in front of a client created by `server.CreateClient(...)` (`server.CreateClient(record: true)`).

**`PushReceiverDouble`**: an `HttpMessageHandler` plus an optional Kestrel listener. Records each delivery: URL, headers, raw body bytes, parsed `StreamResponse`, and the result of the receiver-side verification from package 08 (valid, invalid signature, wrong task id). Scripts the responses: `.Respond(500, 500, 200)` to test retry and backoff with the fake clock. `WaitForAsync(count | predicate, timeout)`. Replaces the sample `WebhookReceiver` in library tests.

**Event log** (`TaskEventLog`): an `ITaskEventObserver` registered by the harness and server. `Events` is a snapshot list. `WaitForAsync(Func<TaskEvent, bool>, timeout)` completes when an event matches, using a per-waiter `TaskCompletionSource` (no polling, no sleeping). `host.WaitForTaskStateAsync(taskId, state, timeout)` uses it. This replaces polling loops such as `ActivityCollector.WaitForAsync` and the fixed delays found in `PushTests`, `TaskManagerTests`, `ReportFlowTests`, and others.

**Assertion helpers** (extension methods; all throw `A2ATestAssertException`):

- On `AgentTask`: `ShouldBeInState(TaskState)`, `ShouldBeCompleted()` (and `Failed`, `Canceled`, `InputRequired`), `ShouldHaveArtifact(id|name)` → `ArtifactAssert` with `WithText`, `WithPartCount`, `WithMediaType`, `ShouldHaveStatusMessageContaining(text)`, `ShouldHaveHistory(n)`.
- On a stream: `TaskRun` (from `StreamTextAsync`/`client.StreamAsync(...)`) collects all events with a timeout, then: `ShouldHaveStates(Submitted, Working, Completed)` (the status sequence), `ShouldBeSingleMessage()`, `ShouldEndIn(state)`, `ShouldFollowStreamRules()`.
- `ShouldFollowStreamRules()` checks the spec stream rules in Constraints, plus: all events carry the same task and context ids, a final artifact chunk is not followed by an append to the same artifact, no event after the final state.
- `ShouldBeValidTask()` checks required fields and the oneof rule on parts (`Part.IsValid`).
- Failure text prints the full event sequence in a compact table, so a failing test needs no debugger.

**Telemetry collectors**: `ActivityCollector` and `MetricCollector` from `tests/Shared` move into the package namespace `Ambinder.A2A.Testing.Telemetry`. Changes: no `Xunit.Assert`/`TestContext` dependency (use `A2ATestAssertException` and a `CancellationToken` parameter), `WaitFor…` uses an activity-stopped signal instead of a 10 ms poll *(writer's call)*. They keep their trace-id isolation design, so parallel tests do not see each other's spans.

**`ScriptedChatClient`** (Testing.AI): an `IChatClient` that returns scripted answers by turn or by predicate, streams them word by word (like the sample `FakeChatClient`, `samples/AgentServer/Llm/FakeChatClient.cs`), can throw, can wait on a `Gate`, and records the `ChatMessage`s it received. `FakeChatClient` (echo-style) is moved here; the sample keeps its own small copy so it needs no test package.

### 5. Store conformance suite

- Cases are defined by package 05 in M1 (a project under `tests/`, used by the in-memory, EF Core and Cosmos stores). In M2 this package takes the project over and publishes it as `Ambinder.A2A.Testing.Conformance` (package 05 agrees, 05 §4 and §7.13).
- Shape (package 05 §7.13 defines it; this package keeps it unchanged): framework-neutral case catalogs (`TaskStoreConformanceCases`, `PushConfigStoreConformanceCases`, `OutboxStoreConformanceCases`, `InstanceStoreConformanceCases`: named `Func<StoreSet, CancellationToken, Task>` cases), plus one xunit v3 abstract class per catalog that turns each case into one named test:

```csharp
public sealed class MyStoreTests : TaskStoreConformanceTests
{
    protected override ValueTask<StoreSet> CreateStoresAsync() => ...;   // fresh, empty stores and the suite's FakeTimeProvider
}
```

- Teams using NUnit or MSTest call the case list from their own wrapper.
- The suite covers what the current `InMemoryTaskStoreTests` check (list order, tie-breaks, owner/context/status filters, artifact omission, history shaping, page size and token errors, copy isolation, agent keying) plus 05's additions (owner/tenant isolation, lease fields, conditional writes, outbox rows). `InMemoryTaskStoreTests` is rebuilt as a subclass; only in-memory-specific tests stay in it.
- Each case has a stable id string. A skipped case needs a reason that shows up in the test output.

### 6. Quality gates

**Analyzers (R40).** Package 02 owns the analyzer settings, the banned-API file and the XML-doc rules for `src/` (02 §6, §7). The list below restates them so this spec's gates are complete; where it differs, 02 wins. This package owns only the test-side rules (the tests' banned-symbol file, `AnalysisMode Default` for tests and samples, assertion-free test checks). *(Reconciled: both specs defined `src/` analyzers.)* Put in `Directory.Build.props` and `.editorconfig`:

- `AnalysisLevel` `latest`, `AnalysisMode` `Recommended` for `src/` (stricter than today); `Default` for tests and samples *(writer's call)*.
- `EnforceCodeStyleInBuild` true; `.editorconfig` gains code style rules for `var`, braces, `using` placement, naming (private fields `_camel`), file-scoped namespaces. Keep the existing whitespace rules.
- Errors in `src/`: CA2007 (`ConfigureAwait`, the code already follows it), CA1848/CA2254 (use `LoggerMessage` source generation: faster, principle 4), CA1062 not needed (nullable is on), CA1303 off, CA2016 (pass `CancellationToken`), CA1031 as warning with justification comments, `CS1591` (missing XML docs on public members) as error in `src/`. `GenerateDocumentationFile` on for every packable project.
- Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `eng/BannedSymbols.txt` for `src/`: `DateTime.Now/UtcNow` and `DateTimeOffset.Now/UtcNow` (use `TimeProvider`), `Thread.Sleep`, `new HttpClient()` (use the factory), and the `JsonSerializer` overloads that take no `JsonTypeInfo`/context (source-generated JSON only, principle 4). Tests get a second file banning `Thread.Sleep` and `Task.Delay` outside the package's own helpers.
- Public API analyzers (`Microsoft.CodeAnalysis.PublicApiAnalyzers`) and the API baseline are package 02's. This package sets nothing there.
- Trim/AOT analyzers (`IsAotCompatible` true) for every package marked "yes" in package 01's map (Protocol, Server, AspNetCore, Client, Extensions.AI, Compat.V0_3, Compat.V0_3.AspNetCore). Stores (EF Core, Cosmos) are excluded. Findings that cannot be fixed get a justified `UnconditionalSuppressMessage` *(writer's call)*.
- `TreatWarningsAsErrors` stays on. New analyzer rules that fire on existing code are fixed in the same PR that turns the rule on, never baselined silently.

**Security scanning (R40).**

- `.github/workflows/security.yml`:
  - **CodeQL** (`github/codeql-action`) for C#, manual build, `security-extended` queries; on PR, on push to master, weekly. SARIF goes to the Security tab.
  - **Dependency review** (`actions/dependency-review-action`) on PR: fail on high severity vulnerabilities and on licenses that are not compatible with Apache 2.0 (GPL/AGPL family) *(writer's call)*.
  - **Nightly vulnerability audit:** `dotnet list package --vulnerable --include-transitive`; fails on any finding and opens an issue.
  - **OpenSSF Scorecard** weekly *(writer's call)*.
- `NuGetAudit` true, `NuGetAuditMode` `all` (direct and transitive) in `Directory.Build.props`. The PR-build audit level is package 01's (it owns the audit gate, Goal 12): High and Critical advisories (NU1903, NU1904) fail a PR; Low and Moderate (NU1901, NU1902) are warnings on a PR *(Claude's call, C1)*. So a new low advisory never blocks unrelated PRs. The nightly and release builds fail on all levels.
- Secret scanning and push protection are switched on in the repository settings (a manual step, package 01 owner action 7, recorded in the acceptance list). `SECURITY.md` is created by package 01; package 13 sets its content rules.
- Actions are pinned to a full commit SHA; `permissions` stays minimal per job; Dependabot (`.github/dependabot.yml`, exists) also covers `github-actions` and groups NuGet updates.

**Coverage (R39).**

- Collect with the Microsoft.Testing.Platform code coverage extension (`--coverage`, Cobertura output), merge with `dotnet-coverage`, render with ReportGenerator. The HTML and a Markdown summary are uploaded as artifacts and written to the job summary.
- Excluded: generated code (`[GeneratedCode]`, proto output, source-generated JSON), `samples/`, `tests/`.
- Gates on PR *(writer's call)*:

| Scope | Line | Branch |
|---|---|---|
| `Ambinder.A2A.Protocol`, `Ambinder.A2A.Server` (task logic, validation, auth) | 90% | 80% |
| `Ambinder.A2A.Client`, `Ambinder.A2A.Extensions.AI`, `Ambinder.A2A.Testing` | 85% | 75% |
| Stores (EF Core, Cosmos), cluster | 80% | 70% |

  A PR also fails if total coverage of the packages it touches drops by more than 1 point against master. Coverage counts only tests that assert (the gate script rejects an assertion-free test method found by a simple analyzer check: `xUnit2013`-style and empty-body rules are on).
- Nightly *(advisory, not a gate)*: mutation testing with Stryker.NET on `Protocol` and `Server` task logic; the score goes in the job summary.

**CI layout.** `ci.yml` is split into jobs (all on PR unless marked):

| Job | What | Owner |
|---|---|---|
| `build-test` | restore, build (analyzers), unit + library integration tests, coverage + gates | 12 |
| `samples` | build samples, `Samples.Tests` | 12 |
| `security` | CodeQL, dependency review | 12 |
| `tck` | TCK on all three bindings | 03 |
| `interop` | cross-SDK suite | 16 |
| `bench` | benchmarks and budgets | 14 |
| `nightly` | vulnerability audit, stress, fuzz, mutation | 12 |

Jobs run in parallel. Required checks: `build-test` and `security` from the start; `tck` and `interop` are advisory for the first two weeks after each lands, then required *(Claude's call, C13)*. There is no second human reviewer; these checks and the public API diff (02) are the merge gate (owner decision O2). Package 01's packaging jobs (pack, allow-list, samples on packages, coexistence, reproducibility, AOT smoke) run beside these. Runner OS: Linux unit tests on every PR; the Windows unit-test leg (package 01) runs nightly and on release *(Claude's call, C1)*.

### 7. Test structure and migration of the repo's tests

Today (`tests/`): `A2A.Protocol.Tests` (31 test methods), `A2A.Server.Tests` (96), `A2A.Integration.Tests` (198), `A2A.MockEntra.Tests` (7). That is 332 `[Fact]`/`[Theory]` methods. Theories expand into one case per data row, so the suite runs 801 test cases.

Target:

| Project | Content | Uses |
|---|---|---|
| `Ambinder.A2A.Protocol.Tests` | models, JSON, proto mapping, errors | none |
| `Ambinder.A2A.Server.Tests` | task manager, sink, push, auth, telemetry (no HTTP) | `AgentHarness`, `ScriptedAgent`, collectors |
| `Ambinder.A2A.Client.Tests` (new, R39) | client transports against fake handlers, credential providers, hardening (size limits, malformed SSE) | `FakeRemoteAgent`, fake `HttpMessageHandler` |
| `Ambinder.A2A.Extensions.AI.Tests` (new) | mapping, chat agent, tool | `ScriptedChatClient`, `A2ATestServer` |
| `Ambinder.A2A.Testing.Tests` (new) | the package itself, including a self-test of every assertion and the stream-rule checks | |
| `Ambinder.A2A.Integration.Tests` | library over the wire: transports, parity across bindings, hosting wiring, startup validation, auth, push end to end, telemetry across hops | `A2ATestServer`, `PushReceiverDouble`, `MockEntra` |
| `Ambinder.A2A.Stores.*.Tests` (05) | conformance subclasses per store | `Testing.Conformance` |
| `Ambinder.A2A.Samples.Tests` | flows of the sample apps, console flow runner, sample telemetry | the real samples on real Kestrel |
| `Ambinder.A2A.Repo.Tests` | compose file, docs checks, project-reference rules | |
| `Ambinder.A2A.Stress.Tests` (new, nightly) | soak and fuzz | `A2ATestServer` |

Moves *(each one file-for-file, listed so the builder can plan)*:

| From | To |
|---|---|
| `tests/A2A.Server.Tests/TestAgents.cs` (`AgentScripts`, `ScriptedAgent`, `Gate`, `Signal`, `FixedTimeProvider`, `TestHost`) | `ScriptedAgent`, `Gate`, `Signal`, `FakeTimeProvider`, `AgentHarness` |
| `tests/Shared/ActivityCollector.cs`, `MetricCollector.cs` | `Ambinder.A2A.Testing.Telemetry`; remove `<Compile Include="../Shared/*.cs" />` |
| `tests/A2A.Integration.Tests/Fixtures/MinimalServer.cs` | `A2ATestServer` (no ports; the `Protected` agent becomes a test with `TestIdentity`) |
| `tests/A2A.Integration.Tests/Fixtures/TransportMatrix.cs` | `A2ABinding.All` / `TransportMatrix` in the package |
| `tests/A2A.Integration.Tests/Fixtures/A2ATestHost.cs` (samples on fixed ports) | stays, in `Samples.Tests` only |
| `tests/A2A.Integration.Tests/Client/*` | `Client.Tests` (unit parts) and `Integration.Tests` (`TransportParityTests` over `A2ATestServer`) |
| `tests/A2A.Integration.Tests/Flows/*`, `Samples/*` | `Samples.Tests` |
| `tests/A2A.Integration.Tests/Repo/*` | `Repo.Tests` |
| `tests/A2A.Server.Tests/Tasks/InMemoryTaskStoreTests.cs` | subclass of `TaskStoreConformanceTests` |
| Fixed sleeps/`Task.Delay` (18 occurrences in 12 files under `tests/`) | `Events.WaitForAsync`, `FakeTimeProvider.Advance`, `Gate`/`Signal` |
| `A2A_TEST_PORT_BASE` ports (9 files) | in-memory host or port 0 (samples tests keep a port base) |

Rules, enforced by `Repo.Tests` and the banned-symbol file:

1. No `ProjectReference` to `samples/` outside `Samples.Tests` (a test parses the `.csproj` files).
2. No fixed port outside `Samples.Tests`.
3. No `Thread.Sleep`/`Task.Delay` in tests outside the package helpers.
4. Telemetry tests that need a global listener stay in the `Telemetry` collection (existing `TelemetryCollection.cs`); the collector isolation lets all others run in parallel.
5. A test file lives in the folder of the area it tests; a test class has one fixture; names say behavior (`Cancel_of_canceled_task_returns_task`), as now.

**New kinds of tests (R39).**

- **Soak/leak** (`Stress.Tests`, nightly): create and finish 10,000 tasks, 1,000 subscribed streams that are opened and dropped, and 1,000 push configs on a long-lived host; after a forced GC, managed memory and the sizes of the lock table, running-task table, broker and push queue are back near baseline (growth under 10% above warm-up). This is the check for R15 (package 05/09/08 fix the leaks; this test proves it).
- **Fuzz/property:** FsCheck properties *(writer's call)*: JSON round trip for every protocol model; the SSE reader gives the same events whatever way the byte stream is cut (split a valid stream at random positions, 1-byte chunks included); malformed input never throws anything but `A2AException` and never allocates beyond the client's size limits (R30). A SharpFuzz/libFuzzer harness is left for later.
- **Multi-instance:** the cluster harness `StartClusterAsync` is provided; 06 owns the tests.
- **Spec golden files:** the JSON golden files shared with the TCK (03) live in one `testdata/` folder; `Protocol.Tests` reads them.

### 8. Repo hygiene piece

`.gitignore` gets `TestResults/`, `coverage/`, `*.trx`. The existing `TestResults` folders are removed from the working tree.

## How it meets the six principles

1. **Protocol.** The stream-rule and task-rule assertions encode the spec, and `ShouldFollowStreamRules()` runs on every streaming test in the repo. Check: a mutation of `TaskManager` (a duplicated terminal event) makes a `Server.Tests` stream test fail through the helper.
2. **.NET enabler.** `TestServer`, `FakeTimeProvider`, `TimeProvider`, DI replacement, `IChatClient` fake, xunit v3 conformance classes, no forced framework. Check: the docs example test (12 lines) compiles and passes under xunit and, in `Testing.Tests`, under a minimal custom runner calling the same helpers (proves framework neutrality).
3. **Easy.** Check: hello-world test ≤ 12 lines; the "Testing your agent" docs page (13) has a copy-paste test that passes in a fresh project in under 5 minutes; every `NotScriptedException` and assertion failure says what to do.
4. **Performance.** Check: in-memory server start under 150 ms p50 (measured in `Testing.Tests` with a loose 1 s ceiling to avoid flakes, and tracked by package 14); full unit + library integration run under 3 minutes on a CI runner after the migration (a CI step prints the time and fails above 6 minutes); no helper allocates per poll because none polls.
5. **Modular and extendable.** Three packages with separate dependencies; the conformance suite is how third parties plug a store in; `ScriptedAgent` and `FakeRemoteAgent` are plain classes users can derive from. Check: dependency-graph test (`Testing` must not reference xunit, NUnit, MSTest or `Extensions.AI`).
6. **Integration pains.** Pain point 21 (testing and mocking agents), pain point 12 (store/push test fakes), pain point 20 (telemetry collectors), pain point 24 (spec examples that mislead: golden files plus rule checks). Check: each item in the acceptance list that cites a pain point.

## Dependencies

On other packages:

- **01:** package IDs, versions, and the repo rename. Solution/project names above use the new prefix.
- **02:** API tracking for the three packages; `PublicApiAnalyzers`.
- **03:** golden files, the TCK job, the TCK-in-CI wiring; confirmation that harness-created hosts need no TCK-specific code.
- **05:** the case catalog and its project (M1); an `ITaskStore` that is easy to fake; agreement that 12 takes over and publishes `Testing.Conformance` in M2. 05 uses the M1 internal harness slice (B2).
- **09:** uses the M1 internal harness slice (B2).
- **06:** shared-store cluster harness is a base for its tests.
- **07:** the claims and owner rules that `TestIdentity` produces; a test authentication scheme must not weaken the real one (it registers only in test hosts).
- **08:** the receiver-side verifier used by `PushReceiverDouble`; the push `HttpClient` name stays stable.
- **09:** no gRPC routing by port (R21); `TimeProvider` used everywhere; a hosting entry point that works with `TestServer`.
- **10:** `AddA2AClient` registration seam for `AddFakeA2AClient`, and client factory use inside `CreateClient`.
- **11:** `TaskUpdater`, `AgentRequestContext.Create`, `ChatClientAgentHandler`, `A2AAgentTool`.
- **13:** docs page "Testing your agent" with the 12-line example.
- **14:** benchmark of start time; budgets do not apply to the Testing packages' runtime.
- **16:** none; interop uses real SDK processes.

Provides (exact names):

- To 05 and 09 in M1 (repo-internal, B2): `A2ATestServer` on `TestServer`, `FakeTimeProvider` wiring, `TaskEventLog` waiters, `StoreSet`.
- To all packages' tests: `AgentHarness`, `A2ATestServer`, `A2ATestServerOptions`, `A2ABinding`, `TestIdentity`, `ScriptedAgent`, `Gate`, `Signal`, `FakeRemoteAgent`, `RecordingA2AClient`, `PushReceiverDouble`, `TaskEventLog`, `TaskRun`, assertion extension classes (`TaskAssertions`, `StreamAssertions`), `ActivityCollector`, `MetricCollector`, `ScriptedChatClient`, `TaskStoreConformanceTests`, `A2ATestAssertException`.
- To 13: the test examples above.
- CI workflow files: `ci.yml`, `security.yml`, `nightly.yml`.

## Acceptance criteria

- [ ] The three packages build, pack and are listed in the API tracking files. `Testing` has no reference to a test framework.
- [ ] `AgentHarness`, `A2ATestServer` (all three bindings, `TestIdentity`, fake time, push double) have their own tests in `Testing.Tests`, including failure-message tests for each assertion.
- [ ] A hello-world test of 12 lines or fewer is in the docs and compiled by `Testing.Tests`.
- [ ] `ShouldFollowStreamRules` rejects each broken stream case: two messages, task after update, event after final, mixed ids, append after last chunk.
- [ ] `TaskStoreConformanceTests` runs against in-memory, EF Core and Cosmos stores in CI (the last two via 05), and a deliberately broken store (in `Testing.Tests`) fails specific named cases.
- [ ] All repo tests are migrated as in section 7. `grep` rules: no `tests/Shared`, no `TestAgents.cs`, no `MinimalServer`; the structure rules (no sample refs, no fixed ports, no sleeps) are enforced by `Repo.Tests`/analyzers and pass.
- [ ] `Client.Tests` exists and the client tests moved out of the integration project.
- [ ] Analyzer settings from section 6 are on; the build is clean; `CS1591` is an error in `src/`.
- [ ] `security.yml` runs CodeQL, dependency review, and the nightly audit; a test PR with a High or Critical vulnerable package is blocked (shown once, recorded in the PR).
- [ ] Coverage is collected and gated at the table values; the report is a CI artifact.
- [ ] Soak and fuzz tests exist and pass nightly three times in a row.
- [ ] CI runs `build-test`, `samples`, `security`, `tck`, `interop`, `bench`, `nightly` as laid out. Required checks are set in the repo settings; `tck` and `interop` become required two weeks after each lands (C13).
- [ ] The M1 internal harness slice exists under `tests/` and is used by 05 and 09; no M1 test added after it uses a fixed port or a sleep (B2).
- [ ] A PR with a new Low or Moderate advisory passes with a warning; a High or Critical one fails; the nightly build fails on both (C1).
- [ ] Secret scanning and push protection are enabled (screenshot or settings export in the PR).
- [ ] `TestResults/` ignored and removed.
- [ ] Run time of `build-test` is reported and under 6 minutes.

## Left to the builder

- Exact assertion method names beyond those listed; fluent style details.
- Internal event-log data structures.
- ReportGenerator configuration and the gate script language (PowerShell or bash, keep one).
- Code style rule details in `.editorconfig`, as long as the repo builds clean.
- Which hosted-runner OS images to use.
- Which analyzer findings get a justified suppression, with a comment each.
- Whether to add the Stryker mutation job in M2 or just after.
- Package icons and README for the three packages (01 sets the template).

## Open questions

None. MockEntra (Q1), required checks (Q2) and size (Q3) are Claude's call C13 (Non-goals, section 6, header).
