# SDK 14: Performance and benchmarks

Date: 2026-11-03
Status: draft for approval
Milestone: M1 (baseline and the hot-path fixes), M2 (budgets enforced)
Size: M

## Intent

Principle 4 says: source-generated JSON, no per-call reflection, pooled buffers, streaming without buffering whole responses, a benchmark suite in CI, and no package past its budget. This package makes that real.

It does three things. It builds a BenchmarkDotNet suite that measures the paths product teams care about (request latency per transport, streaming, JSON, stores, push). It wires the suite into CI, with gates that are fair on noisy shared runners. And it writes the performance rules every other package must follow, with a list of known Phase 1 offenders and who fixes each.

The baseline is taken in M1 so later packages cannot hide a regression. Budgets are enforced from M2.

## Findings addressed

- R15 (part): unbounded growth; here the "O(1) memory per stream and per call" rules and the allocation tests (the store eviction itself is package 05).
- R17 (part): push delivery cost; here the push benchmarks (the outbox design is package 08).
- R24 (part): SSE write path (heartbeats are package 09; this spec fixes how frames are written).
- R27 (part): socket churn from a `SocketsHttpHandler` per client (`ClientSupport.cs:36-41`, `RestA2AClient.cs:42`, `JsonRpcA2AClient.cs:36`); here the rule "HttpClient only through the factory" and a benchmark that would show it.
- R30 (part): reading untrusted responses fully into memory; here the streaming-read rule and benchmark (limits themselves: package 10).
- R39 (part): no load or soak tests exist; the soak run itself is package 12, the benchmarks here feed it.
- Pain points: "Streaming (SSE) robustness: buffering, reconnect, slow consumers" (#10), "DI/host integration" (#22: HttpClient lifetime and socket exhaustion, a2a-dotnet #107), "JSON serialization drift" (#2: one source-generated serializer).

Phase 1 offenders found while reading the code (the "performance debts", see Design section 3):

- Request body is copied three times: `MemoryStream` then `ToArray` (`RequestBody.cs:18,33`), then `JsonDocument.Parse` (`JsonRpcEndpoint.cs:78`), then a second deserialize from the `JsonElement` (`JsonRpcEndpoint.cs:123`).
- Every SSE event is a `byte[]` made by a delegate (`SseStreaming.cs:15,28`), then written as three separate `WriteAsync` calls plus a `FlushAsync` (`SseWriter.cs:29-32`).
- The client reads SSE with `StreamReader.ReadLineAsync` (a string per line), a `StringBuilder` per event (`SseReader.cs:17-38`), then converts the string back to bytes (`JsonRpcA2AClient.cs:113`).
- `OmitEmptyLists` calls `foreach` on a boxed `IEnumerable` for every list property of every serialized object (`A2AJson.cs:82-93`), which allocates an enumerator each time.
- `InMemoryTaskStore` copies the history and artifact lists on every get and every save (`InMemoryTaskStore.cs:15,19,97-98`) and sorts all tasks on every list call (`InMemoryTaskStore.cs:39-43`).
- Typed JSON metadata is looked up by `Type` on each call (`A2AJson.Options.GetTypeInfo(typeof(T))` in `JsonRpcA2AClient.cs:154`, `ClientSupport.cs:123`, `RestA2AClient.cs:125,207`).

## Goals

1. A BenchmarkDotNet project `benchmarks/Ambinder.A2A.Benchmarks` with the scenarios in section 1. One command runs a category: `dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*Streaming*"`.
2. Every scenario reports time and allocated bytes per operation (`MemoryDiagnoser`). Streaming scenarios also report events per second and time to first event.
3. A committed allocation baseline `perf/alloc-baseline.json` exists from the M1 run, with the hardware, OS, .NET version and commit recorded in it.
4. A nightly job runs the full suite on `master`, stores the JSON, and shows the trend. A failure alert fires when a benchmark is more than 20% slower or allocates more than 10% extra against the previous nightly run.
5. A PR job (label `run-perf`, and automatic for changes under the hot paths listed in section 5) runs the quick suite on the merge-base and on the PR head, on the same runner, and fails on a statistically significant slowdown over the budget.
6. Allocation unit tests (not BenchmarkDotNet) run on every PR and fail when a listed hot path allocates more than its limit. They are fast and deterministic.
7. The performance rules P1 to P15 are written in `CONTRIBUTING.md`, and the ones a tool can enforce are enforced (banned symbols, analyzers).
8. The Phase 1 offenders PD1, PD2 and PD4 (and PD5 if the baseline shows it matters) are fixed in M1, each with before and after numbers in the PR description. PD3, PD6, PD7 and PD8 are fixed by the packages named in the table, and their acceptance criteria use the benchmarks from this package.
9. From M2, `perf/budgets.json` defines, per scenario, the allowed ratio to the last release and the absolute allocation limit. The release pipeline fails when a budget is broken, unless `perf/exceptions.md` has a signed entry for it.
10. A startup scenario measures time from process start to the first successful agent-card response for the hello agent, and a package-size report records the `.nupkg` size and dependency count per package.
11. The nightly results produce the data for the docs page "Performance and sizing" (package 13).

## Non-goals

- Soak, load and leak testing under sustained traffic: package 12. This spec's allocation tests and per-stream memory rules are the unit-level part of it.
- Choosing store schemas or indexes: package 05. This spec measures them.
- Designing the push outbox, the cluster layer, the client limits and heartbeats: packages 08, 06, 10, 09. They must meet the rules and have scenarios here.
- Capacity planning for a customer's cluster. The docs give per-core numbers from the nightly run, with the hardware named; they are not a promise.
- Comparing speed with other A2A SDKs as a gate or a marketing claim. A manual comparison project against the official `A2A` package may be kept in `benchmarks/Comparison/` for the maintainers (see Left to the builder); it is not run in CI.
- Micro-tuning code that is not on the paths below.

## Anti-goals

- A suite that is red or ignored because shared CI runners are noisy.
- Budgets that lock in today's slow code. Baselines are taken after the PD1, PD2 and PD4 fixes, not before.
- "Optimizations" without numbers, or a speed-up that changes the wire format or weakens a limit.
- Benchmarks that measure the test harness (a mock server that is slower than the SDK).
- Benchmarks that call internals in a way production never does, so the number means nothing.
- Allocation limits so tight that they fail on a new .NET patch and get disabled.
- A performance rule that needs a hand-written comment to defend it. Rules are in one list, with a check.

## Constraints

- Principle 4 in the work plan: source-generated JSON, no per-call reflection, pooled buffers, streaming without buffering whole responses, benchmark suite in CI, no package past its budget.
- .NET 10 only; the suite runs on the SDK in `global.json`. BenchmarkDotNet: newest stable at the time of work, version in `Directory.Packages.props`.
- GitHub-hosted runners are shared VMs: wall-clock numbers vary. Allocation counts do not. The gates are designed around that (section 4).
- The wire must not change: every PD fix runs through the golden JSON tests, the proto round-trip tests, and the TCK (package 03) in CI.
- `InternalsVisibleTo` to `Ambinder.A2A.Benchmarks` is allowed (package 02), because some scenarios call internal pieces (`SseWriter`, the proto mapper) that matter but are not public. Scenarios that are "request latency" use only the public path (host a server, call the client).
- Shared code without internals *(Claude's call, B12)*: hot paths in packages other than `Ambinder.A2A.Protocol` (AspNetCore transports, Client, stores, Push.Receiver) serialize through the public typed `JsonTypeInfo<T>` accessors on `A2AJson` (package 02), never through reflection or `InternalsVisibleTo`. Small shared helpers (page tokens, state rules, request validation) are linked source files compiled as `internal` into each package (package 01 rule 10).
- One builder, no second human reviewer (owner decision O2). Perf gates are automated; the owner approves releases and perf exceptions.
- Telemetry overhead: scenarios run with no listener attached, and a second set with an OpenTelemetry listener on, because `ActivitySource` costs nothing without a listener and the numbers show whether that stays true.
- The spec rules for streaming still hold: events in order, delivered to all active streams (spec 3.1.2, 3.5). Performance work must not buffer in a way that changes this.

## Design

### 1. The suite

Project: `benchmarks/Ambinder.A2A.Benchmarks/` (`IsPackable=false`, in the solution so it always compiles; `Program.cs` uses `BenchmarkSwitcher`). Config: `MemoryDiagnoser`, `[GcServer(false)]` off, JSON and GitHub-markdown exporters. Jobs: `Short` (3 warmup, 3 to 5 iterations) for PR, default for nightly and release.

Layers, so a number can be blamed on one side:

- **Micro:** one component, no network: serializer, SSE parse, mapper, store, event bus, push signing.
- **Server-only:** the real ASP.NET Core pipeline in-process with `TestServer`, driven by a pre-built `HttpRequestMessage` with the body bytes ready, so the client costs almost nothing. For gRPC, the same with the gRPC test handler.
- **Client-only:** the real client against a fake `HttpMessageHandler` that returns canned bytes (and a fake gRPC `CallInvoker`).
- **End to end:** real Kestrel on loopback, real client, one process, a hello agent. Shows the sum and the socket costs.
- **Control:** a hand-written ASP.NET Core minimal API endpoint that reads and writes the same JSON with `System.Text.Json`. The SDK's server-only number is reported as "overhead over control". This keeps the target meaningful across machines.

Scenarios (each runs for JSON-RPC, REST and gRPC where the transport matters). Categories are the BenchmarkDotNet `[BenchmarkCategory]` names:

| Category | Scenario | What it measures |
|---|---|---|
| `Json` | Serialize and deserialize `SendMessageRequest` (one text part), `AgentTask` with 0, 20 and 200 history messages, `StreamResponse` status event, `StreamResponse` artifact chunk (200 B text), `AgentCard` (6 skills), one 1 MB `Raw` part (base64) | bytes/op, ns/op; large-part case shows buffering |
| `Json` | Same, through `A2AJson.Options` vs the generated `JsonTypeInfo` directly | proves PD4 |
| `Grpc` | `ProtoMapper` round trip of the same models; proto serialize and parse | mapper cost per message |
| `Request` | `SendMessage` blocking, agent replies with a message (no task) | latency and bytes, per transport, layers: server-only, client-only, end to end |
| `Request` | `SendMessage` blocking, agent creates a task and completes it | adds store save, event publish, history |
| `Request` | `GetTask`, `ListTasks` (page of 50 out of 1,000 tasks), `GetAgentCard` (cached) | read paths |
| `Request` | `SendMessage` with `returnImmediately` then `GetTask` | the pattern long tasks use |
| `Stream` | Agent emits N = 1,000 artifact chunks (200 B each) then completes; one subscriber | events/s, bytes per event, time to first event, per transport |
| `Stream` | Same with 10 subscribers on one task | fan-out cost |
| `Stream` | `SseWriter` write of one frame into a null pipe; `SseReader` parse of 10,000 frames from memory | micro: PD2, PD3 |
| `Stream` | Slow consumer: reader stalls, the bounded channel fills, the subscriber is dropped | the agent is not slowed (checks the "publish never waits" rule, `TaskEventBroker.cs:9-13`) |
| `Pipeline` | `IA2ARequestHandler` direct call (no transport): send a message, agent does 10 status and artifact updates | task manager, per-task lock, store, event bus per event |
| `Pipeline` | Event bus publish with 0, 1 and 10 subscribers | steady-state publish cost; 0 subscribers must be near zero |
| `Store` | In-memory: save new task, save update (history +1) at history 0 / 20 / 200, get, list page of 50 out of 10,000 | PD6 |
| `Store` | EF Core on SQLite (in-memory and file) always; PostgreSQL via Testcontainers and Cosmos DB emulator only on nightly and on request (category `Db`) | same operations plus a concurrent write with a version conflict and retry |
| `Store` | 2,000 streamed artifact-chunk events into one task (one store write per event, package 05 §7.10), SQLite always, PostgreSQL in `Db` | the cost of 05's "no batching" decision; decides whether a write-behind mode is needed later |
| `Push` | Enqueue cost on the event path (`OnTaskEventAsync`-style call, no delivery) | must not slow the agent |
| `Push` | Delivery throughput to a loopback receiver: 1 and 10 configs per task; JWT signing per delivery | PD7; after package 08, includes the outbox write |
| `Client` | Card resolver: cached lookup (should allocate nothing), conditional refresh with a 304; credential provider with a cached token; empty interceptor chain | per-call overhead of the client features |
| `Startup` | Time from `dotnet` process start to first card response; managed heap after start | runs as a script that starts the sample agent N times, not as a BDN benchmark |
| `Size` | Size and dependency count of every `.nupkg`; size of a published console agent and client | script over package 01's pack output (R7) |

Telemetry variants: the `Request` and `Stream` scenarios each run with no listener and with an OpenTelemetry in-memory exporter attached (`[Params]`).

Data: payloads are generated from a fixed seed and live in `benchmarks/Ambinder.A2A.Benchmarks/Data/`. They look like real traffic (an LLM-style reply split into chunks), not zeros.

### 2. Performance rules (P1 to P15)

Every package follows these. "Check" says how the rule is enforced.

| # | Rule | Check |
|---|---|---|
| P1 | JSON: source-generated metadata only. Use the typed `JsonTypeInfo<T>` overloads. No `JsonSerializer` call that takes only `JsonSerializerOptions` or nothing. No `GetTypeInfo(typeof(T))` on a hot path; use the typed property of the context. | Banned symbols (the reflection-based overloads); the trim and AOT analyzers (package 01); alloc test on serialize |
| P2 | No whole-body buffering on the read side except one bounded read. Read a request or response body once into a pooled buffer or a `PipeReader` and parse with `Utf8JsonReader`. Never copy the body into a new array for a second parse. The size limit is enforced while reading, not after. | Alloc test: a 1 MB body is read with bounded extra allocation; `Request` benchmarks |
| P3 | Streams write straight to the output. Serialize the event with `Utf8JsonWriter` into the response `PipeWriter` (or an `IBufferWriter<byte>`); no `byte[]` per event; one flush when no further event is immediately ready, not one flush per write call. Heartbeats go through the same writer. | Alloc test on `SseWriter`; `Stream` benchmarks |
| P4 | Streams read straight from the input. Parse SSE and JSON lines from a `PipeReader` / `ReadOnlySequence<byte>`; no `string` per line; no string-to-bytes conversion. A maximum event size is enforced (package 10 sets the value). | Alloc test on `SseReader`; `Stream` benchmarks |
| P5 | Memory per stream and per call is O(1) in the number of events delivered. Nothing is retained per event after it has been written. Queues are bounded with an explicit full policy. | Alloc test: send 100,000 events, check the heap does not grow with the count |
| P6 | Use `ValueTask` where the common result is synchronous (store hit, publish with no subscriber, cached card). Do not wrap a pass-through call in a new `async` method. `ConfigureAwait(false)` everywhere in `src/` (package 02 enforces). No sync-over-async. | Banned symbols; benchmark review |
| P7 | Logging: `[LoggerMessage]` with fixed `EventId`s only; no string interpolation in log calls; guard expensive arguments with `IsEnabled`. | Analyzers CA1848, CA2254 as errors (package 02) |
| P8 | Telemetry costs nothing without a listener. `StartActivity` returns null then; do not build tags or strings before checking `activity is not null` / `IsAllDataRequested`. Metrics use static instruments and `TagList` (a struct), not a new array per call. | Alloc test with no listener; `Request` benchmark with and without listener |
| P9 | Buffers are pooled (`ArrayPool<byte>`, `ArrayBufferWriter<byte>` reused, `Pipe`). Always return them; clear them if they may hold secrets. No LINQ, no closures that capture, and no `params` arrays in per-event code (use `static` lambdas and spans). | Alloc tests; code review |
| P10 | `HttpClient` only through `IHttpClientFactory` (or one shared `SocketsHttpHandler` owned by DI). Never one handler per call or per client instance. | Banned symbol `new HttpClient` / `new SocketsHttpHandler` in `src/` (with one documented exception); `Client` benchmark creating 1,000 clients and counting sockets |
| P11 | Do once what can be done once. Static data is built at startup or first use and cached: the serialized agent card bytes and ETag (the card does not change unless the provider says so, R26), routing tables, the owner and policy lookups per agent. Per request, work scales with the request. | `GetAgentCard` benchmark allocates nothing after the first call; 304 path |
| P12 | Startup is explicit. No assembly scanning, no reflection-based registration. Hosted services start fast and finish their work in the background. | `Startup` scenario; trim and AOT analyzers |
| P13 | gRPC: convert model to proto once per message, with no intermediate JSON or object copy. Reuse the proto messages' builders only if the benchmark shows a gain; correctness first. | `Grpc` benchmarks |
| P14 | Limits are enforced on the stream, not after buffering: body size, SSE event size, parts per message, history length (limits themselves: packages 10, 09, 05). | Alloc test: an oversized body fails with bounded memory |
| P15 | A change made "for performance" ships with a benchmark or an allocation test that shows the gain. No number, no merge. | PR template item; reviewer |

### 3. Phase 1 performance debts

| ID | Where | Problem | Rule | Fixed by | Proved by |
|---|---|---|---|---|---|
| PD1 | `RequestBody.cs:18-33`; `JsonRpcEndpoint.cs:78,89,123`; the REST body read (`RestEndpoints.cs:110` and its callers) | Body copied into a `MemoryStream`, `ToArray`, `JsonDocument`, then deserialized again from the `JsonElement` | P2, P14 | **14**. Read once into a pooled buffer; find `method` and the `params` span with `Utf8JsonReader` (the buffer is whole, so `params` before `method` is fine); deserialize the typed request from the span. Keep the existing size limit and the 413 behavior. | `Request` server-only; alloc test |
| PD2 | `SseStreaming.cs:15,28`; `SseWriter.cs:29-32` | A `byte[]` per event, three writes and a flush per event | P3 | **14**. Replace the `Func<StreamResponse, byte[]>` frame builders with a writer that serializes into the response `PipeWriter`; one flush when the event queue is empty. The writer takes a heartbeat call for package 09. | `Stream`; alloc test |
| PD3 | `SseReader.cs:17-38`; `JsonRpcA2AClient.cs:113`; the REST stream path (`RestA2AClient.cs:195`) | String per line, `StringBuilder` per event, string to bytes | P4 | **10** (it rewrites the stream reader for reconnect and limits, R28, R30). Acceptance uses `Stream` | `Stream` micro |
| PD4 | `A2AJson.cs:82-93`; `JsonRpcA2AClient.cs:154`; `ClientSupport.cs:123`; `RestA2AClient.cs:125,207` | `OmitEmptyLists` enumerates a boxed `IEnumerable` per list property per object; `GetTypeInfo(typeof(T))` per call | P1, P8 | **14**. Replace the enumeration with a count check that does not allocate (`IReadOnlyCollection<T>` via a generic helper, or `ICollection` fast path); use typed `JsonTypeInfo<T>` properties of the context in all hot callers. Output must stay byte-identical (golden tests). | `Json` |
| PD5 | `ProtoMapper.*` | Not yet measured | P13 | **14** decides after the baseline. If a conversion allocates more than the message size times 3, fix it in M1; otherwise record and leave. | `Grpc` |
| PD6 | `InMemoryTaskStore.cs:15,19,39-43,97-98` | Copies history and artifacts on every get and save; sorts all tasks per list | P5, P9 | **05** (the in-memory store is part of the store contract). Use immutable snapshots shared between callers so get and save are O(1) in history length. List may stay O(n) but must be measured at 10,000 tasks and have a budget. | `Store` |
| PD7 | `PushNotificationSender.cs:24,33-36` (queue) and its delivery loop | Queue and per-task lanes in memory; signing and request building per delivery | P8, P9 | **08** (outbox redesign). Acceptance uses `Push` | `Push` |
| PD8 | `TaskWriter.cs:20-32` | Per-task semaphores never freed (R15) | P5 | **05** (the reference-counted lock table, 05 §7.10). 06 reuses it unchanged and only checks no residue across pods in its cluster suite. | alloc test: 100,000 distinct tasks leave no residue |

If the M1 baseline shows another path above 5% of a request's time or allocation, add a row; the rule is that debts are written down, not that this list is complete.

### 4. CI integration and gates

Workflows (hooks created by package 01):

- **`ci.yml` (every PR):**
  - `dotnet build` includes the benchmark project (it must compile).
  - The allocation unit tests run with the normal test suite. They use `GC.GetAllocatedBytesForCurrentThread()` around a warmed-up call, run on one thread, and compare to limits stored next to the test (`[AllocationBudget(maxBytes)]` helper in the non-packable project `tests/Ambinder.A2A.Perf.Testing`; package 12 removes `tests/Shared`; *(writer's call)*). Limits are set 10% above the measured value after the fix and never to zero unless the rule says "no allocation".
- **`perf.yml`:**
  - *Nightly* on `master` (cron), full jobs, all categories except `Db`, plus `Db` once a week. Pinned runner image (`ubuntu-24.04`, not `latest`). Uploads BenchmarkDotNet JSON and markdown as artifacts (90 days) and feeds `benchmark-action/github-action-benchmark` (tool `benchmarkdotnet`) to keep a history chart on the Pages site and to alert (comment on the commit) when a benchmark is worse than the previous run by the alert thresholds in Goal 4. *(writer's call: that action; the builder may use another)*
  - *PR comparison* on the label `run-perf`, and automatically when a PR changes a hot path: `src/**/Transports/**`, `src/**/Json/**`, `Tasks/`, `Stores/`, `Push/`, the SSE files, `Directory.Packages.props`. It checks out the merge-base into one folder and the PR head into another, runs the `Short` suite for both on the same runner one after the other (base first, then head, then base again to detect drift), and compares with a statistical test (the dotnet/performance `ResultsComparer` tool does this with a Mann-Whitney test and a threshold). The job fails when the head is slower than the budget ratio **and** the difference is significant **and** the second base run agrees with the first (so a noisy neighbor does not fail a PR). Otherwise it posts a table as a PR comment and in the job summary.
  - *Release:* `release.yml` calls the same job against the previous release tag's baseline (stored as a release asset) in M2.
- **Allocation gate (PR, from M1):** `perf/alloc-baseline.json` has bytes/op per scenario from the BenchmarkDotNet run. The PR comparison job also checks allocations: fail when bytes/op rise by more than 5% or 64 bytes, whichever is larger, unless an exception exists. Allocation numbers are stable across runners, so this gate is strict. Updating the baseline is a script (`eng/perf/update-baseline`) whose output shows as a diff on the PR, like an API file; CODEOWNERS routes `perf/` to the owner. No second reviewer is required (O2).
- **Time budget:** `perf/budgets.json` per scenario: `maxTimeRatio` (default 1.20 against the previous release or merge-base), `maxAllocRatio` (default 1.05), and `maxBytes` where a rule says so (for example 0 for P11 and P8 listener-off cases). Starts as defaults in M1 (report only) and is enforced in M2. A scenario can have a stricter value if the baseline shows it is stable.
- **Exceptions:** `perf/exceptions.md` has one entry per accepted regression: scenario, ratio, reason, who approved (the owner, O2), expiry release. The gate reads it. An entry without a reason fails a lint.
- **Absolute targets** are set after the first baseline, not now: the owner and the 14 builder agree numbers for (a) server-only `SendMessage` overhead over the control, (b) allocations per streamed event, (c) startup to first card response. Targets are written into `perf/budgets.json` with the date and hardware. Until then the relative rules above apply. *(writer's call: no absolute latency number is promised before it is measured)*
- **Runner caveats:** results from GitHub-hosted runners are good for A/B and trends, not for absolute claims. The docs page says which hardware produced its numbers. If the maintainers later get a dedicated runner, only `runs-on` changes.

### 5. Hot-path folders (trigger list)

`src/Ambinder.A2A.Protocol/Json/`, the JSON-RPC, REST and SSE transport folders in `Ambinder.A2A.AspNetCore`, the gRPC mapper, `Tasks/`, event bus, the stores, `Push/` in `Ambinder.A2A.Server`, and the transport and SSE code in `Ambinder.A2A.Client`. The builder keeps the list in `eng/perf/hot-paths.txt`.

### 6. Package size and dependency budget

Package 01's pack job writes each package's size and dependency count. The budget: a package may not grow more than 15% in size, or gain a dependency, between releases without an entry in `perf/exceptions.md`. A new dependency is also an allow-list change (package 01), so both checks agree. Reason: R7 shows size and dependency weight are part of the cost a team pays.

### 7. Measuring responsibly

- Release builds only; `ServerGC` off, `TieredPGO` on (the .NET 10 default); hosts configured the way the docs tell users to (no debug logging).
- Warm up before any allocation test.
- The hello agent used for end-to-end scenarios replies from memory; it does not call an LLM, a database or a network.
- BenchmarkDotNet's `OperationsPerInvoke` is used for streaming so the unit is "per event".
- Never compare numbers across different hardware. The JSON files carry the machine info for that reason.

## How it meets the six principles

1. **Protocol.** Perf changes must not change the wire. Check: golden JSON, proto round-trip and TCK jobs run on every PD fix PR; the streaming benchmarks assert event order and delivery to all subscribers (a benchmark that gets events out of order fails, not just slow).
2. **.NET enabler.** Uses the standard tools: BenchmarkDotNet, `System.Text.Json` source generation, `PipeReader`/`PipeWriter`, `ArrayPool`, `LoggerMessage`, `ActivitySource`/`Meter`, `TimeProvider`. Check: banned-symbol and analyzer rules in the build.
3. **Easy.** Everything runs with one command from a clean checkout, no Docker for the default suite (the `Db` category needs Docker and says so). Check: `dotnet run -c Release --project benchmarks/... -- --list flat` works on a fresh clone in CI. The results become the docs "Performance and sizing" page so teams get numbers without running anything.
4. **Performance.** This is the package. Checks: allocation tests on every PR; A/B time gate; nightly trend and alerts; budgets from M2; PD1, PD2, PD4 with before and after numbers.
5. **Modular and extendable.** Each package has its own scenarios and its own rows in `budgets.json`, so one package's regression is blamed on that package. Seams are measured through their interface (`Store`, `Push`, event bus) so a replacement store can run the same scenarios (package 12's `Testing` package exposes the store scenarios for implementers; builder decides how). Check: a scenario exists for every default implementation behind a public seam.
6. **Integration pains.** Streaming robustness: the slow-consumer scenario and the O(1)-per-stream rule. Socket exhaustion (a2a-dotnet #107): rule P10 and the 1,000-client scenario. Cold start on AKS: the `Startup` scenario. Heavy dependencies: the size budget.

## Dependencies

- **Needs from 01:** the `benchmarks/` folder and props, the workflow hooks and reusable workflows, the pack output for size reports, the pinned runner approach.
- **Needs from 02:** `BannedSymbols.txt` mechanism, analyzers (CA1848, CA2254, CA2007), `InternalsVisibleTo` for the benchmarks assembly.
- **Needs from 03:** the TCK and golden tests as the safety net for PD fixes. **From 05:** the store contract and in-memory store (PD6), the EF/Cosmos stores for `Store` scenarios. **From 08:** the push design (PD7). **From 10:** the client stream reader (PD3) and card cache. **From 06:** event bus interface (for `Pipeline` scenarios). **From 09:** heartbeat call into the SSE writer.
- **Provides to all:** performance rules P1 to P15, the allocation-test helper `AllocationBudget` in `tests/Ambinder.A2A.Perf.Testing`, the scenarios, `perf/` files, `eng/perf/*` scripts, the baseline.
- **Provides to 12:** scenarios reusable by the `Testing` package; the allocation helper; input for soak tests.
- **Provides to 13:** the nightly JSON for the performance page; rules text for CONTRIBUTING.
- **Provides to 01:** the `perf` job for the release gate.
- **Names created:** project `Ambinder.A2A.Benchmarks`; folder `perf/` with `alloc-baseline.json`, `budgets.json`, `exceptions.md`; test helper `AllocationBudgetAttribute`; scripts `eng/perf/update-baseline`, `eng/perf/hot-paths.txt`; workflow `perf.yml`.

## Acceptance criteria

M1:

- [ ] `benchmarks/Ambinder.A2A.Benchmarks` builds in CI and every category in section 1 has at least one scenario (the `Db` ones may be skipped without Docker, with a clear message).
- [ ] PD1, PD2 and PD4 are fixed; PR descriptions show before and after bytes/op and ns/op from the same machine. Wire output is byte-identical (golden tests green) and the TCK is green.
- [ ] `perf/alloc-baseline.json` is committed, with machine info and commit.
- [ ] The nightly job runs for seven days without a failure caused by the job itself, stores the JSON, and the trend page exists.
- [ ] Allocation unit tests exist and pass for: `SseWriter` frame write, SSE read per event, JSON-RPC request parse (body of 1 KB and of 1 MB), event bus publish with no subscriber, cached card response, serialize of `StreamResponse` with no listener attached, 100,000-event stream heap check (P5), and the store get/save at history 200 (PD6, when 05 lands).
- [ ] Banned symbols for P1 and P10 are active and the build is clean.
- [ ] No hot path outside `Ambinder.A2A.Protocol` uses a reflection-based `JsonSerializer` overload; they use `A2AJson`'s typed `JsonTypeInfo<T>` accessors (B12).
- [ ] Rules P1 to P15 are in `CONTRIBUTING.md`; the PR template has the "perf evidence" item.
- [ ] The `Startup` and `Size` scenarios produce numbers in the nightly summary.

M2:

- [ ] `perf/budgets.json` exists with a row per scenario; absolute targets (a) to (c) in section 4 are filled in and dated.
- [ ] The PR A/B job works (proven by a test PR that adds a deliberate 2x slowdown and a test PR that only touches docs): the first fails, the second does not run or passes.
- [ ] The allocation gate fails a test PR that adds an allocation to the SSE write path.
- [ ] `release.yml` runs the comparison against the previous release and blocks on a broken budget.
- [ ] PD3, PD6, PD7, PD8 are closed by their packages with the `Stream`, `Store`, `Push` scenarios in their acceptance results.
- [ ] Every default implementation behind a public seam has a scenario.
- [ ] The package size and dependency report is in the release summary and enforced as in section 6.
- [ ] The docs page "Performance and sizing" is generated from the latest nightly JSON.

## Left to the builder

- Which payload shapes go into the data set beyond the ones named, as long as they look like real traffic.
- BenchmarkDotNet job settings (iteration counts) and runner image details.
- The exact JSON-RPC envelope parse technique in PD1, as long as the body is read once, limits are kept, and output behavior is identical.
- Whether PD5 needs a change after measuring.
- Whether to keep a manual `benchmarks/Comparison/` project against the official `A2A` 1.0.0-preview4 package (it also proves both can be loaded together, package 01).
- The statistical test and threshold, if `ResultsComparer` is replaced.
- How the `AllocationBudget` helper is written and how it reports.
- Dashboard and chart tooling for the nightly trend.

## Open questions

None that change what gets built. (A dedicated benchmark runner would make time budgets sharper; the design works without one.)
