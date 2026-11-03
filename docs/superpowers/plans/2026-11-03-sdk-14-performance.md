# SDK 14: Performance and Benchmarks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Every subagent runs on model `sonnet` (see "Execution notes" at the end). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make work-plan Principle 4 ("performance efficient") real and checked. This plan builds a BenchmarkDotNet suite for the paths product teams care about and wires it into CI with gates that hold up on noisy shared runners. It writes the performance rules P1 to P15 and fixes the Phase 1 debts that package 14 owns (PD1, PD2, PD4, and PD5 if the numbers call for it). It commits the M1 allocation baseline and turns on budgets in M2.

**Spec (binding):** `docs/superpowers/specs/2026-11-03-sdk-14-performance-design.md` (cited below as "spec §N").
**Work plan (binding):** `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md`, including B12 (shared helpers are linked internal source files, and `A2AJson` exposes typed `JsonTypeInfo<T>` accessors).
**Names and layout:** package 01's package map, `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md` Design §1.

**Architecture:**
- `benchmarks/Ambinder.A2A.Benchmarks` (not packable, in the solution) holds every BenchmarkDotNet scenario. They are grouped by `[BenchmarkCategory]` (`Json`, `Grpc`, `Request`, `Stream`, `Pipeline`, `Store`, `Db`, `Push`, `Client`) and layered: micro, server-only, client-only, end to end, and control.
- `benchmarks/Ambinder.A2A.Benchmarks.HelloAgent` (not packable) is a stand-alone hello agent for the `Startup` and `Size` scripts.
- `tests/Ambinder.A2A.Perf.Testing` (not packable) holds `AllocationBudgetAttribute` and the measuring helpers. `tests/Ambinder.A2A.Perf.Tests` holds the deterministic allocation unit tests, which run on every PR.
- `eng/perf/PerfTool` is one console tool with these subcommands: `gate` (PR A/B and allocation gate), `nightly` (trend alert and summary), `update-baseline`, `startup`, `size-report`, `lint-exceptions`, and `docs-page` (M2). `eng/perf/update-baseline` (bash) and `eng/perf/update-baseline.ps1` are thin wrappers. `eng/perf/hot-paths.txt` lists the folders that trigger the PR job.
- `perf/` holds `alloc-baseline.json` (M1), `budgets.json` (report-only in M1, enforced in M2) and `exceptions.md`.
- `.github/workflows/perf.yml` has four modes: nightly, PR comparison, `workflow_call` for release (M2), and `workflow_dispatch` for a baseline refresh.

**Tech stack:** .NET 10 (SDK from `global.json`), BenchmarkDotNet (newest stable at the time of work, pinned in `Directory.Packages.props`), `System.Text.Json` source generation, `System.IO.Pipelines`, `ArrayPool<byte>`, `Microsoft.AspNetCore.TestHost`, OpenTelemetry in-memory exporter (benchmarks only), xUnit v3, System.CommandLine (already pinned at 2.0.14), `benchmark-action/github-action-benchmark` (pinned by SHA).

---

## Assumption: package 01 has landed

This plan is written **after package 01 (rename to `Ambinder.A2A.*`) has landed**. Every path below uses 01's names. Phase 1 paths are given in brackets only so the code can be found by symbol, for example `src/Ambinder.A2A.AspNetCore/Transports/RequestBody.cs` [Phase 1: `src/A2A.Server/Transports/RequestBody.cs`].

Line numbers in the spec are from an earlier snapshot. They already differ from the Phase 1 code (for example the spec gives `ClientSupport.cs:123`, but the typed-metadata call is at `src/A2A.Client/Transports/ClientSupport.cs:185` today). **Always find code by symbol name, never by line number.**

### Steps that depend on package 01

| Step | What it needs from 01 |
|---|---|
| Task 0 (preflight) | the whole map below |
| Task 1 (benchmark project) | `benchmarks/` folder convention and its `Directory.Build.props` with `IsPackable=false`; solution `Ambinder.A2A.slnx`; `BenchmarkDotNet.Artifacts/` in `.gitignore` |
| Task 1, Task 4, Task 6 | the server split: transports in `Ambinder.A2A.AspNetCore`, core in `Ambinder.A2A.Server`, gRPC in `Ambinder.A2A.AspNetCore.Grpc` and `Ambinder.A2A.Client.Grpc` |
| Task 4 (gRPC scenarios), Task 19 (PD5) | `src/Grpc.Shared/` with `ProtoMapper` compiled `internal` into both gRPC packages (01 rule 4) |
| Task 1, Task 4, Task 6, Task 7 | rule 6: `InternalsVisibleTo` only to tests and to `Ambinder.A2A.Benchmarks` |
| Task 11 (telemetry variants) | renamed sources and meters `Ambinder.A2A.Server` and `Ambinder.A2A.Client` |
| Task 13 (size report) | pack output in `artifacts/packages`, and the pack job's size and dependency-count data (01 §6, Goal 4 of 01 "Performance") |
| Task 12, Task 13 | MinVer versions (the size report keys on the version) |
| Task 14, Task 25, Task 28 (CI) | 01's hook job for benchmarks in `ci.yml`, and `release.yml` (01 §6, §7); third-party actions pinned by SHA; pinned runner approach. See the note below this table: 01's plan names the hook `benchmarks`, and this plan renames it to `perf`. |
| Task 22 (rules) | `CONTRIBUTING.md`, `.github/pull_request_template.md` (with its "perf evidence" item), `.github/CODEOWNERS` |
| Task 21 (bans) | `eng/` folder; `eng/BannedSymbols.txt` (created by package 02 on top of 01's layout) |

If Task 0 finds that any row is not true, **stop and report to the owner**. Do not rename anything yourself, because that is package 01's job. The one exception is the hook rename in the note below.

**Hook name: `perf`, not `benchmarks`.** Spec 01 says 01 provides "a `perf` hook job" to this package, and spec 14 names the workflow `perf.yml`. Package 01's plan (builder Task 8) instead creates:
- a hook job `benchmarks` in `ci.yml` that calls a stub reusable workflow `.github/workflows/benchmarks.yml`, which prints "not yet implemented (package 14)";
- no benchmark job in `release.yml`, where the hooks run only inside the `verify` job, which calls `ci.yml`.

This plan follows the specs and owns the rename:
- Task 14 renames the `ci.yml` job to `perf`, deletes the stub `benchmarks.yml`, and creates `perf.yml`.
- Task 28 adds the `perf` job to `release.yml`.

Task 0 accepts either name. If 01 has already used `perf` or `perf.yml`, keep that and skip the rename steps.

### Other packages this plan touches

| Package | What this plan needs | Where | If it has not landed |
|---|---|---|---|
| 02 | typed `JsonTypeInfo<T>` accessors on `A2AJson` (B12), `A2AJson.TypeInfoResolver`, `eng/BannedSymbols.txt` mechanism, `InternalsVisibleTo` for benchmarks, CA1848/CA2254/CA2007 as errors | Task 15b, Task 21, Task 1 | Task 15b and the P1 part of Task 21 stop and report. Task 15a still runs. Do **not** add the accessors yourself, because they are 02's public API. |
| 03 | golden JSON tests (`testdata/`), proto round-trip tests, TCK job | every PD task | PD tasks still run against the Phase 1 golden tests (`ModelJsonTests`, `ProtoRoundTripTests`). The PR description must say the TCK was not yet available. |
| 05 | store contract, EF Core stores, SQLite/PostgreSQL/Cosmos | Task 8 | Task 8 ships the in-memory scenarios only. It adds the EF and `Db` scenarios when 05 lands (an M1 acceptance item stays open until then). |
| 06 | `ITaskEventBus` | Task 5 | Use today's `TaskEventBroker` through `InternalsVisibleTo`, then switch to the interface when 06 lands (M2). |
| 07 | owner resolution, the anonymous rules (C8), `AllowAnonymous()` | Task 6 | Task 6 uses today's anonymous default. When 07 lands, the bench host gets a fixed caller (Task 6 Step 1, review item 2). |
| 09 | gRPC opt-in (`AddA2AGrpc()`), gRPC routing by request `tenant` instead of local port, Development defaults for public URLs and TLS | Task 6 | Before 09 Task 8, gRPC routes agents by local port, and `TestServer` has none. So the gRPC rows of the server-only layer wait for 09 (review item 1), and gRPC runs end to end on Kestrel only. |
| 08 | push outbox | Task 9 | Measure today's `PushNotificationSender`. 08 re-runs the same scenarios after its redesign. |
| 09 | heartbeat timer | Task 18 | Task 18 only adds the heartbeat write method. 09 owns the timer. |
| 10 | `IHttpClientFactory` client (R27), new SSE reader (PD3), card cache, interceptors | Task 10, Task 21 (P10) | Task 10 measures today's client. The P10 ban and the 1,000-client socket test wait for 10 (see Task 21). |
| 12 | removal of `tests/Shared` | Task 2 | Do not reference `tests/Shared` from new projects. |
| 13 | docs page "Performance and sizing" | Task 30 | 14 provides the JSON and the generator. 13 places the page. |

---

## Global constraints (every task)

- **The wire must not change.** Every PD fix runs the golden JSON tests, the proto round-trip tests, and the TCK (when it exists) before it is committed. Error messages are on the wire too, so they must stay byte-identical (spec Left to the builder: "output behavior is identical").
- **Characterization tests come first.** Before you change a hot path, add tests that pin today's exact output bytes. They must pass on the old code. They are what proves "identical".
- **No number, no merge (P15).** Every PD PR description has a before/after table (ns/op and bytes/op from the same machine and the same BenchmarkDotNet job).
- **Baselines come after the fixes** (spec Anti-goals). `perf/alloc-baseline.json` is generated after PD1, PD2 and PD4 land (Task 20), never before.
- **Builds:** `TreatWarningsAsErrors` stays on. `ConfigureAwait(false)` on every await in `src/`. Central package versions in `Directory.Packages.props`, newest stable only. New third-party GitHub actions are pinned by commit SHA.
- **Scope:** do not micro-tune code off the paths in spec §1 (spec Non-goals). Do not touch PD3, PD6, PD7 or PD8 code, because packages 10, 05, 08 and 05 own them.
- **Commit style:** match the repo (`feat:`, `fix:`, `perf:`, `test:`, `ci:`, `docs:`), one concern per commit, and end each message with the session's attribution lines.
- Benchmarks that check correctness (event order, delivery to all subscribers) **throw** on a wrong result, so a wrong result fails the run (spec "How it meets the principles" 1).

## Suggested PR grouping (M1)

| PR | Tasks | Notes |
|---|---|---|
| A: suite and helpers | 0–13 | no `src/` behavior change; may add `InternalsVisibleTo` lines |
| B: CI wiring (report-only) | 14 | needs the owner to create the `run-perf` label |
| C: PD4 | 15a, 15b | "before" numbers come from PR A's suite |
| D: PD1 | 16, 17 | |
| E: PD2 | 18 | |
| F: PD5 decision, allocation tests, bans, rules, baseline | 19–24 | the baseline is committed here, last |

---

# Milestone M1: suite, hot-path fixes, baseline

## Task 0: Preflight (no code)

**Files:** none (read-only)

- [ ] **Step 1: Confirm package 01 landed.** Run:

```bash
git grep -nE 'namespace A2A\.|<(Root|Assembly)Name>A2A\.' -- src tests samples || echo "rename OK"
ls Ambinder.A2A.slnx src/Ambinder.A2A.Protocol src/Ambinder.A2A.Server src/Ambinder.A2A.AspNetCore src/Ambinder.A2A.Client src/Grpc.Shared benchmarks
cat benchmarks/Directory.Build.props
grep -n 'BenchmarkDotNet.Artifacts' .gitignore
ls CONTRIBUTING.md .github/pull_request_template.md .github/CODEOWNERS
grep -nE 'workflow_call|perf' .github/workflows/*.yml
```

Expected: "rename OK", every path exists, `IsPackable` is false under `benchmarks/`, and `ci.yml` has 01's benchmark hook job, named `perf` or `benchmarks` (see the note above). Record which name and which reusable workflow file (`perf.yml` or `benchmarks.yml`) it uses; Task 14 needs it. If any check fails, stop and report which row of the "Steps that depend on package 01" table is not true.

- [ ] **Step 2: Record what the other packages provide.** Find each symbol and write the result in your task report:

```bash
git grep -nE 'public static JsonTypeInfo<AgentTask>' src/Ambinder.A2A.Protocol || echo "02 accessors: MISSING"
ls eng/BannedSymbols.txt || echo "02 banned symbols: MISSING"
git grep -nE 'ITaskEventBus' src || echo "06 bus: MISSING"
git grep -nE 'new (HttpClient|SocketsHttpHandler)\b' src
ls tests/*/testdata 2>/dev/null; git grep -lE 'tck' .github/workflows || echo "03 TCK job: MISSING"
```

- [ ] **Step 3: Locate the hot-path files after the rename.** For each item, record its new path: `RequestBody`, `JsonRpcEndpoint`, `JsonRpcMessages`, `TransportErrors.Deserialize`, `RestEndpoints` (`ParseBody`, `BuildRequest`, `Deserialize<T>`, `StreamAsync`), `SseStreaming`, `SseWriter`, `A2AJson` (`OmitEmptyLists`), the client `JsonRpcA2AClient`, `RestA2AClient`, `ClientSupport`, `SseReader`, `ProtoMapper`, `TaskEventBroker`, `InMemoryTaskStore`, `PushNotificationSender`, `AgentCardEndpoints`, `RestQuery`. Later tasks refer to these by symbol.

- [ ] **Step 4: Check whether `SseWriter` is still public.** Phase 1 has `public static class SseWriter`. If package 02 has not made it internal yet, Task 18 must keep its public method working (see Task 18, Step 1).

---

## Task 1: Benchmark project skeleton

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Ambinder.A2A.Benchmarks.csproj`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Program.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/A2ABenchmarkConfig.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Categories.cs`
- Modify: `Ambinder.A2A.slnx` (add the project under a `/benchmarks/` folder)
- Modify: `Directory.Packages.props` (add `BenchmarkDotNet`, `Microsoft.AspNetCore.TestHost`, `OpenTelemetry.Exporter.InMemory`)
- Modify: `src/*/*.csproj` only to add `<InternalsVisibleTo Include="Ambinder.A2A.Benchmarks" />` where a scenario needs internals (01 rule 6, 02). Expected: `Ambinder.A2A.Protocol`, `Ambinder.A2A.Server`, `Ambinder.A2A.AspNetCore`, `Ambinder.A2A.AspNetCore.Grpc`, `Ambinder.A2A.Client`, `Ambinder.A2A.Client.Grpc`.

- [ ] **Step 1: Add package versions.** Use the newest stable `BenchmarkDotNet` on nuget.org. For `Microsoft.AspNetCore.TestHost`, use the same 10.0.x patch as `Microsoft.AspNetCore.Authentication.JwtBearer`. For `OpenTelemetry.Exporter.InMemory`, use the same version as `OpenTelemetry`, and first check on nuget.org that this version exists.

- [ ] **Step 2: Create the csproj.**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <TieredPGO>true</TieredPGO>
    <!-- BenchmarkDotNet generates code that needs this -->
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="BenchmarkDotNet" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
    <PackageReference Include="OpenTelemetry" />
    <PackageReference Include="OpenTelemetry.Exporter.InMemory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Ambinder.A2A.Protocol/Ambinder.A2A.Protocol.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Server/Ambinder.A2A.Server.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.AspNetCore/Ambinder.A2A.AspNetCore.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.AspNetCore.Grpc/Ambinder.A2A.AspNetCore.Grpc.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Client/Ambinder.A2A.Client.csproj" />
    <!-- Both gRPC packages compile the same internal ProtoMapper and generated types from src/Grpc.Shared (01 rule 4).
         With InternalsVisibleTo from both, the names clash (CS0433). The alias keeps the client copy apart. -->
    <ProjectReference Include="../../src/Ambinder.A2A.Client.Grpc/Ambinder.A2A.Client.Grpc.csproj" Aliases="clientgrpc" />
  </ItemGroup>
</Project>
```

If the build still reports CS0433 for the server copy, give the server gRPC reference an alias too (`Aliases="servergrpc"`). Use `extern alias` only in the gRPC benchmark files.

- [ ] **Step 3: Write `Categories.cs`.** Put the category names in one place:

```csharp
namespace Ambinder.A2A.Benchmarks;

internal static class Categories
{
    public const string Json = "Json";
    public const string Grpc = "Grpc";
    public const string Request = "Request";
    public const string Stream = "Stream";
    public const string Pipeline = "Pipeline";
    public const string Store = "Store";
    public const string Db = "Db";       // needs Docker; nightly weekly and on request
    public const string Push = "Push";
    public const string Client = "Client";
}
```

- [ ] **Step 4: Write `A2ABenchmarkConfig.cs`.** Use `MemoryDiagnoser`, workstation GC, and these exporters: JSON *full* (needed for per-iteration samples in the gate) and GitHub markdown. Add two jobs, chosen by an environment variable so CI can choose:

```csharp
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;

namespace Ambinder.A2A.Benchmarks;

internal static class A2ABenchmarkConfig
{
    // A2A_PERF_JOB=short → PR job. Anything else → default job (nightly, release).
    public static IConfig Create()
    {
        var shortJob = string.Equals(Environment.GetEnvironmentVariable("A2A_PERF_JOB"), "short", StringComparison.OrdinalIgnoreCase);
        var job = shortJob
            // 5 iterations, not 3: with 3 per side a Mann-Whitney test can never reach p < 0.05 (min p = 0.1).
            ? Job.Default.WithWarmupCount(3).WithIterationCount(5).WithId("Short")
            : Job.Default.WithId("Default");
        return ManualConfig.CreateEmpty()
            .AddJob(job.WithGcServer(false))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.Full)
            .AddExporter(MarkdownExporter.GitHub)
            .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance)
            .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
            .WithOptions(ConfigOptions.JoinSummary);
    }
}
```

Check the method names against the BenchmarkDotNet version you pinned, because the builder API has moved between versions. The intent must not change: Short = 3 warmup and 5 iterations; full JSON; markdown; memory diagnoser; `ServerGC` off.

- [ ] **Step 5: Write `Program.cs`.**

```csharp
using BenchmarkDotNet.Running;

namespace Ambinder.A2A.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, A2ABenchmarkConfig.Create());
}
```

- [ ] **Step 6: Add a smoke benchmark** so the project runs before the other tasks: `Json/SmokeBenchmarks.cs` with one `[Benchmark]` serializing an `AgentCard` through `A2AJson.Options`. Task 3 deletes it.

- [ ] **Step 7: Build and list.**

```bash
dotnet build Ambinder.A2A.slnx -c Release
dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --list flat
```

Expected: the build is clean and the list shows the smoke benchmark.

- [ ] **Step 8: Commit** `feat(perf): add Ambinder.A2A.Benchmarks project skeleton`.

---

## Task 2: Allocation test helper and test project

**Files:**
- Create: `tests/Ambinder.A2A.Perf.Testing/Ambinder.A2A.Perf.Testing.csproj` (library, `IsPackable=false`)
- Create: `tests/Ambinder.A2A.Perf.Testing/AllocationBudgetAttribute.cs`
- Create: `tests/Ambinder.A2A.Perf.Testing/Allocations.cs`
- Create: `tests/Ambinder.A2A.Perf.Tests/Ambinder.A2A.Perf.Tests.csproj` (xUnit v3, references Perf.Testing and the `src/` projects, with `InternalsVisibleTo` from the `src/` projects as in Task 1)
- Create: `tests/Ambinder.A2A.Perf.Tests/AssemblyInfo.cs`
- Create: `tests/Ambinder.A2A.Perf.Testing.Tests/AllocationsTests.cs` (tests the helper itself; may live in `Ambinder.A2A.Perf.Tests` under `Helper/`, which is the writer's call)
- Modify: `Ambinder.A2A.slnx`

Design (*plan writer's call*; spec "Left to the builder: how the AllocationBudget helper is written"):
- The limit lives on the test method as `[AllocationBudget(maxBytes)]`, next to the test (spec §4).
- Synchronous work is measured with `GC.GetAllocatedBytesForCurrentThread()` (spec §4).
- Async work can hop threads, so it is measured with `GC.GetTotalAllocatedBytes(precise: true)`. To keep the process quiet, the test assembly turns off xUnit parallelization. This is why allocation tests have their own project.
- Each measure does a warm-up call first, then measures `iterations` calls and returns bytes per call (rounded down).

- [ ] **Step 1: Write the failing helper tests first.**

```csharp
public sealed class AllocationsTests
{
    [Fact]
    public void Sync_measure_of_no_allocation_is_zero()
        => Assert.Equal(0, Allocations.PerCall(static () => { }, iterations: 1000));

    [Fact]
    public void Sync_measure_sees_an_array()
        => Assert.InRange(Allocations.PerCall(static () => GC.KeepAlive(new byte[100]), iterations: 100), 100, 160);

    [Fact]
    public async Task Async_measure_sees_an_array()
        => Assert.InRange(await Allocations.PerCallAsync(static () => { GC.KeepAlive(new byte[1000]); return ValueTask.CompletedTask; }, iterations: 100), 1000, 1100);

    [Fact]
    [AllocationBudget(0)]
    public void Budget_passes_at_limit()
        => AllocationBudget.Assert<AllocationsTests>(0);

    [Fact]
    [AllocationBudget(10)]
    public void Budget_fails_over_limit()
        => Assert.ThrowsAny<Exception>(() => AllocationBudget.Assert<AllocationsTests>(11));
}
```

- [ ] **Step 2: Run them and see them fail** (the types do not exist yet).

```bash
dotnet test tests/Ambinder.A2A.Perf.Tests -c Release
```

- [ ] **Step 3: Implement.**

```csharp
namespace Ambinder.A2A.Perf.Testing;

/// <summary>The most bytes one call of the measured hot path may allocate. Set 10% above the value measured after the fix (spec §4).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllocationBudgetAttribute(long maxBytes) : Attribute
{
    public long MaxBytes { get; } = maxBytes;
}

public static class AllocationBudget
{
    /// <summary>Fails when <paramref name="measuredBytesPerCall"/> is above the budget on the calling test method.</summary>
    public static void Assert<TTest>(long measuredBytesPerCall, [System.Runtime.CompilerServices.CallerMemberName] string testMethod = "")
    {
        var method = typeof(TTest).GetMethod(testMethod)
            ?? throw new InvalidOperationException($"Test method {typeof(TTest).Name}.{testMethod} not found.");
        var budget = (AllocationBudgetAttribute?)Attribute.GetCustomAttribute(method, typeof(AllocationBudgetAttribute))
            ?? throw new InvalidOperationException($"{typeof(TTest).Name}.{testMethod} has no [AllocationBudget].");
        if (measuredBytesPerCall > budget.MaxBytes)
            throw new AllocationBudgetExceededException(
                $"{typeof(TTest).Name}.{testMethod}: {measuredBytesPerCall} bytes per call, budget {budget.MaxBytes}. " +
                "If this growth is intended, raise the budget in the same PR and give the reason (rule P15).");
    }
}

public sealed class AllocationBudgetExceededException(string message) : Exception(message);

public static class Allocations
{
    public static long PerCall(Action work, int iterations = 1000)
    {
        work(); // warm-up: JIT, static init, pools
        work();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) work();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    public static async Task<long> PerCallAsync(Func<ValueTask> work, int iterations = 1000)
    {
        await work();
        await work();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++) await work();
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
    }
}
```

`tests/Ambinder.A2A.Perf.Tests/AssemblyInfo.cs`:

```csharp
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
```

If `Assert<TTest>` with `CallerMemberName` names the wrong method inside an async test (it should give the test method name, not `MoveNext`), keep the design and add a test that proves it works for an `async Task` test.

- [ ] **Step 4: Run the tests and see them pass.** If the async case is flaky because of background allocations (for example timers), raise `iterations` and widen only that test's `InRange`. Do not turn off the check.

- [ ] **Step 5: Commit** `test(perf): add AllocationBudget helper and Perf.Tests project`.

---

## Task 3: Data set and `Json` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Data/Payloads.cs` (generated from a fixed seed)
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Json/JsonBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Json/TypeInfoPathBenchmarks.cs`
- Delete: `benchmarks/Ambinder.A2A.Benchmarks/Json/SmokeBenchmarks.cs`

- [ ] **Step 1: Write `Payloads`.** Use `new Random(20261103)`. The data must look like real traffic (spec §1 "Data"): an LLM-style English reply split into 200-byte chunks, task ids as GUID strings, timestamps, and `metadata` with a few keys. Provide:
  - `SendMessageRequest` with one text part
  - `AgentTask` with 0, 20 and 200 history messages (alternating user and agent, 1 to 3 parts each)
  - `StreamResponse` status event, and a `StreamResponse` artifact chunk (200 B text)
  - `AgentCard` with 6 skills, 3 interfaces and 1 security scheme
  - a message with one 1 MB `Raw` part (base64 on the wire)
  - `ArtifactChunks(int n)`, which returns n artifact-update events for one task
  - the serialized UTF-8 bytes of each, built once in a static constructor

  Keep the generated data in code (`Data/` is the folder spec §1 names). Only commit binary fixtures if the code-generated data is too slow to build at start (*writer's call*).

- [ ] **Step 2: `JsonBenchmarks`.** Use `[BenchmarkCategory(Categories.Json)]` and `[MemoryDiagnoser]` (inherited from the config). Add one serialize and one deserialize `[Benchmark]` per payload. Use `[Params]` for the history size (0, 20, 200). Serialize with `A2AJson.Options` through the typed accessor when package 02 has landed, and through `A2AJson.Options.GetTypeInfo(typeof(T))` cached in a static field when it has not. The scenario must measure what production does.

- [ ] **Step 3: `TypeInfoPathBenchmarks` (proves PD4).** For `AgentTask` (history 20) and `StreamResponse`, compare:
  1. `JsonSerializer.SerializeToUtf8Bytes(value, A2AJson.Options.GetTypeInfo(typeof(T)))`, which is the Phase 1 hot-caller pattern (marked `Baseline = true`)
  2. `JsonSerializer.SerializeToUtf8Bytes(value, A2AJson.AgentTask)`, which is the typed accessor (only once 02 has landed; until then, a `static readonly JsonTypeInfo<T>` cast once from `Options`)

  **Guard:** in `[GlobalSetup]`, check that both paths give byte-identical output and throw if they do not. Do **not** compare against `A2AJsonContext.Default.AgentTask`. That metadata does not have the `OmitEmptyLists` modifier, so it writes empty lists and gives different bytes.

- [ ] **Step 4: Run the category.**

```bash
dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*Json*"
```

Expected: results appear, there are no exceptions, and the 1 MB case shows its allocation.

- [ ] **Step 5: Commit** `feat(perf): Json scenarios and seeded payloads`.

---

## Task 4: `Grpc` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Grpc/ProtoMapperBenchmarks.cs`

- [ ] **Step 1:** For the same models as Task 3, add model→proto, proto→model, proto `ToByteArray()` and `Parser.ParseFrom` benchmarks. Use the server copy of `ProtoMapper`. If Task 1 needed an alias, use `extern alias` and say which copy you used in a file comment. One copy is enough because both copies are the same source.
- [ ] **Step 2:** Run `--filter "*Grpc*"`. Record bytes/op against the message's serialized size, because Task 19 (PD5) uses that ratio.
- [ ] **Step 3: Commit** `feat(perf): Grpc mapper scenarios`.

---

## Task 5: `Pipeline` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Pipeline/RequestHandlerBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Pipeline/EventBusBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Agents/BenchAgents.cs` (hello agent that replies from memory; an agent that does N status and artifact updates)

- [ ] **Step 1: Bench agents.** These are in-memory only, with no LLM, database or network (spec §7). `HelloAgent` replies with a message (no task). `TaskAgent` creates a task, emits `n` updates (status and artifact, alternating) and completes it.
- [ ] **Step 2: `RequestHandlerBenchmarks`.** Build a `ServiceProvider` with `AddA2AServer` (the same calls the docs tell users to make, no debug logging) and resolve `IA2ARequestHandler`. Benchmark a `SendMessageAsync` call to `TaskAgent` with 10 updates. Use `OperationsPerInvoke = 1`.
- [ ] **Step 3: `EventBusBenchmarks`.** Publish with 0, 1 and 10 subscribers (`[Params(0, 1, 10)]`). Use `TaskEventBroker` through `InternalsVisibleTo`, or `ITaskEventBus` once package 06 has landed. Subscribers drain on background tasks that are started in `[IterationSetup]`. Check the received count in `[IterationCleanup]` and throw if it is wrong.
- [ ] **Step 4:** Run `--filter "*Pipeline*"`. With 0 subscribers, the result should be close to zero ns and 0 B. Write down the actual numbers. Do not "fix" them here.
- [ ] **Step 5: Commit** `feat(perf): Pipeline scenarios`.

---

## Task 6: `Request` scenarios (server-only, client-only, end to end, control)

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Hosts/BenchServer.cs` (builds the app once; `TestServer` mode and Kestrel loopback mode)
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Hosts/ControlEndpoint.cs` (hand-written minimal API control)
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Hosts/CannedHandler.cs` (fake `HttpMessageHandler` that returns pre-built bytes)
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Request/ServerOnlyBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Request/ClientOnlyBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Request/EndToEndBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Request/ControlBenchmarks.cs`

Layers (spec §1). Each layer has `[Params(Transport.JsonRpc, Transport.Rest, Transport.Grpc)]` where the transport matters.

- [ ] **Step 1: `BenchServer`.** Use one `WebApplication` with `AddA2AServer` + `MapA2A` + `MapA2AGrpc`, registering `HelloAgent`, `TaskAgent` and a store pre-filled with 1,000 tasks (for `ListTasks`).
  - **Environment and gRPC (review item 1).** Run the host with `EnvironmentName = Development`. That way, 09's startup checks (public URL, TLS) and 07's "no scheme registered" check do not fail the host, and nothing has to be configured.
    - If 09 has landed, call `.AddA2AGrpc()`, because `MapA2AGrpc()` throws without it (09 plan Task 7). gRPC then routes by the request `tenant`, so the gRPC client takes the tenant from the card's gRPC interface.
    - If 09 has not landed, gRPC routes by local port (Phase 1 `GrpcPorts`), which does not work on `TestServer`. Run the gRPC rows end to end on Kestrel only, and list the gRPC server-only rows as "waits for 09" in the task report.
  - **Caller (review item 2).** With package 07, an anonymous caller's `ListTasks` returns an empty page by design (07 plan Task 5), so "a page of 50 out of 1,000" would measure an empty list, or fail the response check below.
    - Register a small benchmark-only authentication scheme that signs in one fixed app caller (`tid` and `azp` claims, as 07 reads them), make it the default scheme, and pre-fill the 1,000 tasks with that caller's owner key (`OwnerKey.ForApp(tid, appId)`).
    - Do not mark the agents `AllowAnonymous()`: that would measure a path production does not use.
    - Before 07 lands, today's anonymous owner is used, and the same pre-fill works.
  - **Server-only:** `UseTestServer()`. Requests are pre-built: the body bytes are ready, and a fresh `HttpRequestMessage` with `ByteArrayContent` over the same array is made per call. The client side then costs almost nothing.
  - **gRPC server-only:** `GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions { HttpHandler = server.CreateHandler() })` with the generated client. Use the alias from Task 1 if needed.
  - **End to end:** Kestrel on `127.0.0.1:0`, reading the bound port from `IServerAddressesFeature`, with the real `Ambinder.A2A.Client`. Use one client instance per benchmark class, created in `[GlobalSetup]`.
- [ ] **Step 2: Scenarios** (spec §1 `Request` rows):
  - `SendMessage` blocking, agent replies with a message
  - `SendMessage` blocking, agent creates a task and completes it
  - `GetTask`
  - `ListTasks`, page of 50 out of 1,000
  - `GetAgentCard` (cached)
  - `SendMessage` with `returnImmediately`, then `GetTask`

  Each one checks the response (status 200 and the expected id or text) and throws if it is wrong.
- [ ] **Step 3: Client-only.** Use the real client with `CannedHandler`, which returns pre-serialized response bytes for each method, and a fake gRPC `CallInvoker` for gRPC. Name the JSON-RPC `SendMessage` benchmark `ClientSendMessage`. Package 10's acceptance criteria cite that name.
- [ ] **Step 4: Control.** Write a minimal API `MapPost("/control", ...)` that reads the body with `JsonSerializer.DeserializeAsync(request.Body, A2AJson.<SendMessageRequest accessor>)` and writes a canned `SendMessageResponse` with `JsonSerializer.SerializeAsync`. It runs on the same `TestServer` with the same payload. Name it so the markdown table shows "Control". The gate (Task 14) reports server-only `SendMessage` as a ratio to the control.
- [ ] **Step 5:** Run `--filter "*Request*"`. Expected: every layer and transport runs, and no check throws.
- [ ] **Step 6: Commit** `feat(perf): Request scenarios with server-only, client-only, end-to-end and control layers`.

---

## Task 7: `Stream` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Stream/StreamingBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Stream/SseMicroBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Stream/SlowConsumerBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Stream/TimeToFirstEventColumn.cs` (optional; see Step 2)

- [ ] **Step 1: `StreamingBenchmarks`.** An agent emits N = 1,000 artifact chunks (200 B each) and then completes. Measure end to end per transport with `OperationsPerInvoke = 1000`, so the unit is "per event" (spec §7). Use `[Params(1, 10)]` subscribers: subscriber 1 streams with `SendStreamingMessage`, and the others call `SubscribeToTask` before the agent is released (use a gate the agent awaits). Each subscriber checks that events arrive in order and that all of them arrive, and throws if not (spec Constraints: spec 3.1.2, 3.5).
- [ ] **Step 2: Events per second and time to first event** (spec Goal 2).
  - Events per second = 1e9 / (ns/op), because ns/op is already per event. Task 14's nightly summary works it out from the JSON, so no extra code is needed in the benchmark.
  - Time to first event is a separate benchmark, `TimeToFirstEvent`: send, read until the first event, then cancel.
- [ ] **Step 3: `SseMicroBenchmarks`** (these prove PD2 and PD3):
  - `SseWriteFrame`: write one `StreamResponse` artifact-chunk frame into a null pipe. Use a `DefaultHttpContext` whose response body feature is a `StreamPipeWriter` over `Stream.Null`, or a `Pipe` drained by a reader loop. Call the current `SseWriter` + frame delegate, then the new writer after Task 18.
  - `SseParse`: parse 10,000 frames from a `MemoryStream` with the client's `SseReader` (internal). Package 10's acceptance criteria cite the name `SseParse`.
- [ ] **Step 4: `SlowConsumerBenchmarks`.** One subscriber never reads, so its bounded channel fills and it is dropped. One fast subscriber reads everything. Measure the agent's emit time for 1,000 events. Check that the fast subscriber received all of them and that the slow one ended. The number must be close to the single-subscriber case (the "publish never waits" rule in `TaskEventBroker`). If it is not, write it in the task report, because this is a finding and not a fix for this task.
- [ ] **Step 5:** Run `--filter "*Stream*"`. **Commit** `feat(perf): Stream scenarios including SSE micro and slow consumer`.

---

## Task 8: `Store` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Store/InMemoryStoreBenchmarks.cs`
- Create (when package 05 has landed): `benchmarks/Ambinder.A2A.Benchmarks/Store/EfSqliteStoreBenchmarks.cs`, `Store/DbStoreBenchmarks.cs` (`[BenchmarkCategory(Categories.Store, Categories.Db)]`)

- [ ] **Step 1: In-memory** (spec §1, PD6). Benchmark: save a new task; save an update (history +1) at history 0, 20 and 200; get; list a page of 50 out of 10,000. Use the public store interface (`ITaskStore`, or the 05 contract), so a replacement store can run the same class (spec principle 5).
- [ ] **Step 2: When package 05 has landed:**
  - EF Core on SQLite (in-memory and file) runs always, with the same operations, plus a concurrent write that hits a version conflict and retries.
  - The "2,000 streamed chunks into one task" scenario runs on SQLite always and on PostgreSQL in `Db` (05 §7.10).
  - PostgreSQL uses Testcontainers and Cosmos uses the emulator, both in category `Db` only. When Docker is missing, `[GlobalSetup]` throws an exception whose message starts with `Db category needs Docker:` (spec acceptance: skipped "with a clear message").
  - Before then, add a `// Package 05 adds EF/Db scenarios here` note in the task report, **not** in the code, and leave the M1 acceptance item open.
- [ ] **Step 3:** Run `--filter "*Store*"`. **Commit** `feat(perf): Store scenarios`.

---

## Task 9: `Push` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Push/PushBenchmarks.cs`

- [ ] **Step 1: `PushEnqueue`** (the name is cited by package 08). This is the enqueue cost on the event path, with no delivery: call the sender's event hook (`OnTaskEventAsync`-style, the current internal method on `PushNotificationSender`) with 1 and 3 configs per task. Package 08 deletes `PushNotificationSender`. Its plan (Task 20) rewrites the body of this benchmark to measure the outbox write in the task writer, and keeps the names `PushEnqueue` and `PushDeliveryThroughput`, so the before and after numbers line up (review item 3).
- [ ] **Step 2: `PushDeliveryThroughput`** (cited by 08). Deliver to a loopback Kestrel receiver that answers 200, with `[Params(1, 10)]` configs per task and JWT signing on (an ephemeral key). Use `OperationsPerInvoke` = deliveries. The receiver counts deliveries, and the benchmark throws if the count is wrong.
- [ ] **Step 3:** Run `--filter "*Push*"`. **Commit** `feat(perf): Push scenarios`.

---

## Task 10: `Client` scenarios

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Client/ClientFeatureBenchmarks.cs`

- [ ] **Step 1:** Write these scenarios:
  - Card resolver cached lookup (should allocate nothing).
  - Conditional refresh answered with 304, using `CannedHandler` returning 304 with an `ETag`.
  - Credential provider with a cached token (`BearerTokenCredentialProvider`).
  - `ClientCreatePerCall`: create a client, make one call and dispose it, against `CannedHandler`. This shows today's per-client handler cost (R27).
  - Empty interceptor chain: add it when package 10 lands. Leave the M1 item noted in the report.
- [ ] **Step 2:** Run `--filter "*Client*"`. **Commit** `feat(perf): Client scenarios`.

---

## Task 11: Telemetry variants

**Files:**
- Modify: every `Request/*Benchmarks.cs` and `Stream/StreamingBenchmarks.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks/Hosts/TelemetrySwitch.cs`

- [ ] **Step 1:** Add `[Params(false, true)] public bool Telemetry;` to those classes. When the value is `true`, `[GlobalSetup]` builds an OpenTelemetry `TracerProvider` and `MeterProvider` with the in-memory exporter, listening to `Ambinder.A2A.Server` and `Ambinder.A2A.Client` (the names from 01). Dispose them in `[GlobalCleanup]`. When the value is `false`, attach no listener.
- [ ] **Step 2:** Run one `Request` class both ways. With `Telemetry=false`, the numbers must not be worse than without the parameter. If they are, Task 20 records it as a finding (rule P8).
- [ ] **Step 3: Commit** `feat(perf): telemetry on/off variants`.

---

## Task 12: PerfTool skeleton, `Startup` scenario and HelloAgent host

**Files:**
- Create: `benchmarks/Ambinder.A2A.Benchmarks.HelloAgent/Ambinder.A2A.Benchmarks.HelloAgent.csproj` (web, `IsPackable=false`)
- Create: `benchmarks/Ambinder.A2A.Benchmarks.HelloAgent/Program.cs`
- Create: `eng/perf/PerfTool/PerfTool.csproj` (console, `IsPackable=false`, System.CommandLine)
- Create: `eng/perf/PerfTool/Program.cs`, `eng/perf/PerfTool/StartupCommand.cs`
- Create: `tests/Ambinder.A2A.PerfTool.Tests/` (xUnit v3, references PerfTool)
- Modify: `Ambinder.A2A.slnx`

- [ ] **Step 1: HelloAgent.** This is the getting-started style agent: `AddA2AServer`, one agent that echoes, and `MapA2A`. Copy it from `samples/GettingStarted.Agent` if package 01 or 13 created it, otherwise write the roughly 20-line equivalent. Add one extra endpoint, `GET /perf/heap`, which runs `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` and returns `GC.GetGCMemoryInfo().HeapSizeBytes` as text. The extra endpoint stays in this benchmark project only, never in `samples/`.
- [ ] **Step 2: `PerfTool startup --runs 10 --project <HelloAgent csproj> --out startup.json`.**
  1. `dotnet publish -c Release` once.
  2. For each run: start the published app with `ASPNETCORE_URLS=http://127.0.0.1:0` and `Logging__LogLevel__Default=Warning`.
  3. Read stdout until `Now listening on:` and parse the URL. Keep the hosting lifetime log at `Information` so this line is printed.
  4. Poll `GET /.well-known/agent-card.json` until it returns 200. Find the real card path in the code, and if it is per-agent, use the HelloAgent's path.
  5. Record the elapsed ms from `Process.Start` to that response. Read `/perf/heap`, then kill the process.

  Output: median, p90, min and max ms, the median heap bytes, and the machine info.
- [ ] **Step 3: Test** the stdout URL parser and the median/p90 math in `PerfTool.Tests`, using fixed inputs.
- [ ] **Step 4:** Run `dotnet run -c Release --project eng/perf/PerfTool -- startup --runs 3 ...`. Expected: a JSON file with 3 samples.
- [ ] **Step 5: Commit** `feat(perf): Startup scenario and PerfTool skeleton`.

---

## Task 13: `Size` report

**Files:**
- Create: `eng/perf/PerfTool/SizeReportCommand.cs`
- Create: `benchmarks/Ambinder.A2A.Benchmarks.HelloClient/` (console, about 5-line client call, `IsPackable=false`)
- Test: `tests/Ambinder.A2A.PerfTool.Tests/SizeReportTests.cs`

- [ ] **Step 1: `PerfTool size-report --packages artifacts/packages --out size.json`.** For each `*.nupkg` (not `.snupkg`), record the ID, the version, the file size in bytes, and the dependency count from the nuspec (`<dependencies>/<group>/<dependency>`, counting distinct IDs). Also run `dotnet publish -c Release` (framework-dependent) for HelloAgent and HelloClient and record the total size of each publish folder. Reuse 01's pack-job data if it already writes the same numbers (01 "Performance" check). In that case read that file instead of re-deriving the numbers, and say so in a comment.
- [ ] **Step 2: Test** the parsing with a tiny nupkg built in the test (a zip with a nuspec).
- [ ] **Step 3: Commit** `feat(perf): package size and dependency report`.

---

## Task 14: CI wiring (M1: report-only time, strict allocations)

**Files:**
- Create: `eng/perf/hot-paths.txt`
- Create: `eng/perf/PerfTool/BdnReport.cs` (reads BenchmarkDotNet full JSON)
- Create: `eng/perf/PerfTool/MannWhitney.cs`
- Create: `eng/perf/PerfTool/GateCommand.cs`, `eng/perf/PerfTool/NightlyCommand.cs`, `eng/perf/PerfTool/LintExceptionsCommand.cs`
- Create: `perf/budgets.json` (defaults, `"enforceTime": false`)
- Create: `perf/exceptions.md` (header and an empty table)
- Create: `.github/workflows/perf.yml`
- Modify: `.github/workflows/ci.yml`. Fill 01's hook with the compile check, `--list flat`, the allocation tests (they run with the normal tests) and `lint-exceptions`. If 01 named the job `benchmarks`, rename the job id to `perf`, and update any `needs:` or `if:` that names it, in `ci.yml` and in `release.yml`. Replace its `uses: ./.github/workflows/benchmarks.yml` with the inline steps of Step 9.
- Delete: `.github/workflows/benchmarks.yml` (01's stub), if it exists. `git rm` it in the same commit that creates `perf.yml`, so no commit is left without a hook.
- Modify: `.github/CODEOWNERS` (`/perf/ @roman-ambinder`, `/eng/perf/ @roman-ambinder`)
- Tests: `tests/Ambinder.A2A.PerfTool.Tests/{BdnReportTests,MannWhitneyTests,GateTests,ExceptionsLintTests}.cs`

Choices *(plan writer's call; spec "Left to the builder" allows replacing `ResultsComparer`)*: one in-repo `PerfTool gate` does the statistical test, budgets, exceptions and allocation check. A separate `ResultsComparer` checkout would still need our own budgets and exceptions logic, so one tool is simpler. The statistical test is Mann-Whitney U, two-sided, α = 0.05, exact for small samples.

- [ ] **Step 1: `hot-paths.txt`.** List one glob per line, taken from spec §5 and §4, using the paths you found in Task 0 Step 3. Expected shape:

```
src/Ambinder.A2A.Protocol/Json/**
src/Ambinder.A2A.AspNetCore/Transports/**
src/Grpc.Shared/**
src/Ambinder.A2A.Server/Tasks/**
src/Ambinder.A2A.Server/Stores/**
src/Ambinder.A2A.Server/Push/**
src/Ambinder.A2A.Client/Transports/**
src/Ambinder.A2A.Stores.*/**
Directory.Packages.props
```

Make sure the event bus folder and the SSE folders are covered, wherever they now live.

- [ ] **Step 2: `BdnReport`.** First run any benchmark with the Full JSON exporter and **open the produced `*-report-full.json`**. Write a test fixture from a real file, trimmed to 2 benchmarks, under `tests/Ambinder.A2A.PerfTool.Tests/Fixtures/`. Then parse into this shape:
  - `FullName`
  - the workload-result per-iteration ns per op
  - mean and median
  - allocated bytes per op
  - host environment (OS, CPU, runtime and SDK versions)

  Use the field names you see in the file. Do not guess them.

- [ ] **Step 3: `MannWhitney` with tests first.**

```csharp
public static class MannWhitney
{
    /// <summary>Two-sided p-value. Exact when there are no ties and n1+n2 ≤ 40; otherwise the normal approximation with tie correction.</summary>
    public static double PValue(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n1 = a.Count, n2 = b.Count;
        var all = a.Select(v => (v, g: 0)).Concat(b.Select(v => (v, g: 1))).OrderBy(x => x.v).ToArray();
        var ranks = new double[all.Length];
        var ties = false;
        double tieTerm = 0;
        for (int i = 0; i < all.Length;)
        {
            int j = i;
            while (j + 1 < all.Length && all[j + 1].v == all[i].v) j++;
            var r = (i + j + 2) / 2.0;
            for (int k = i; k <= j; k++) ranks[k] = r;
            int t = j - i + 1;
            if (t > 1) { ties = true; tieTerm += (double)t * t * t - t; }
            i = j + 1;
        }
        double r1 = 0;
        for (int k = 0; k < all.Length; k++) if (all[k].g == 0) r1 += ranks[k];
        double u1 = r1 - n1 * (n1 + 1) / 2.0;
        double u = Math.Min(u1, (double)n1 * n2 - u1);

        if (!ties && n1 + n2 <= 40)
        {
            // c[i,j,k] = arrangements of i items of a and j of b with U = k: c(i,j,k) = c(i-1,j,k-j) + c(i,j-1,k)
            var counts = ExactCounts(n1, n2);
            double total = counts.Sum(), cdf = 0;
            for (int k = 0; k <= (int)u; k++) cdf += counts[k];
            return Math.Min(1.0, 2 * cdf / total);
        }

        int n = n1 + n2;
        double mean = n1 * (double)n2 / 2;
        double sd = Math.Sqrt(n1 * (double)n2 / 12 * ((n + 1) - tieTerm / (n * (double)(n - 1))));
        if (sd == 0) return 1.0;
        double z = (Math.Abs(u1 - mean) - 0.5) / sd;
        return Math.Min(1.0, 2 * (1 - NormalCdf(z)));
    }
    // ExactCounts: dynamic programming over (i, j) as in the comment; NormalCdf: erf-based (Abramowitz–Stegun 7.1.26 is enough).
}
```

Tests:
- `a = {1,2,3,4,5}`, `b = {6,7,8,9,10}` gives p = 2/252 ≈ 0.00794 (within 1e-6)
- identical samples give p = 1
- `a = {1,3,5,7,9}`, `b = {2,4,6,8,10}` gives p > 0.5
- a tied large-sample case gives the same result as a reference value you work out by hand in the test comment

- [ ] **Step 4: Files in `perf/`.**

`perf/budgets.json`:

```json
{
  "schema": 1,
  "enforceTime": false,
  "defaults": { "maxTimeRatio": 1.20, "maxAllocRatio": 1.05, "minAllocDeltaBytes": 64 },
  "nightlyAlert": { "timeRatio": 1.20, "allocRatio": 1.10 },
  "scenarios": {},
  "targets": {}
}
```

`scenarios` is keyed by BenchmarkDotNet `FullName`, with optional `maxTimeRatio`, `maxAllocRatio` and `maxBytes`. `targets` is filled in M2 (Task 27).

`perf/exceptions.md`:

```markdown
# Accepted performance regressions

The perf gate reads this table. One row per accepted regression. The owner approves (O2). Expired rows fail the lint.

| Scenario (FullName or package ID) | Kind (time, alloc, size, dependency) | Accepted ratio or bytes | Reason | Approved by | Expires (release) |
|---|---|---|---|---|---|
```

- [ ] **Step 5: `LintExceptionsCommand`.** Every row must have all 6 cells non-empty, a known kind, a version in `Expires` that parses as SemVer, and an "Approved by" value that is a GitHub handle (`@name`). With `--current-version X`, a row whose `Expires` ≤ X fails. Test each failure case.

- [ ] **Step 6: `GateCommand`** (`PerfTool gate --base1 <dir> --head <dir> --base2 <dir> --budgets perf/budgets.json --exceptions perf/exceptions.md --alloc-baseline perf/alloc-baseline.json --out summary.md`). Rules (spec §4):

| Check | Fails when | Notes |
|---|---|---|
| Allocation (strict from M1) | head bytes/op > baseline bytes/op + max(baseline × (maxAllocRatio − 1), minAllocDeltaBytes) | `maxBytes` (when set) is also an absolute ceiling. A scenario with no baseline row fails with "run eng/perf/update-baseline". Head lower than baseline passes with a hint to update the baseline. |
| Time | median(head)/median(base1) > maxTimeRatio **and** Mann-Whitney p < 0.05 **and** no drift | It is reported only while `enforceTime` is `false` (M1). |
| Drift | base2 vs base1: p < 0.05 or a median ratio outside [1/1.10, 1.10] | When there is drift, the time result is "inconclusive (runner drift)" and never fails. Allocation checks still apply. |
| Exception | a matching row in `exceptions.md` (scenario and kind), within its ratio or bytes, and not expired | It turns that failure into "accepted". |

  The output is a markdown table with these columns: scenario, base, head, ratio, p, alloc base, alloc head, verdict. The server-only `SendMessage` row also shows "overhead over control" (head / Control). The exit code is non-zero only for enforced failures. Test each row of the rules table with synthetic `BdnReport` inputs.

- [ ] **Step 7: `NightlyCommand`** (`PerfTool nightly --current <dir> --previous <dir> --startup startup.json --size size.json --out nightly-summary.json --md nightly-summary.md`).
  - It compares with the previous nightly run using `nightlyAlert` (more than 20% slower or more than 10% extra allocation, spec Goal 4). With no `--previous` (the first run), it skips the comparison.
  - It writes a summary with events per second for the `Stream` category, the startup and size numbers, and the machine info. This is the JSON that package 13 consumes (spec Goal 11).
  - It sets exit code 2 when there is an alert, so the workflow can comment.

- [ ] **Step 8: `.github/workflows/perf.yml`.** Skeleton (fill in action SHAs and match 01's reusable-workflow conventions):

```yaml
name: perf
on:
  schedule:
    - cron: '17 2 * * *'          # nightly
  workflow_dispatch:
    inputs:
      mode: { type: choice, options: [nightly, update-baseline], default: nightly }
      db: { type: boolean, default: false }
  pull_request:
    types: [opened, synchronize, reopened, labeled]
  workflow_call:                    # release.yml uses this in M2 (Task 28)
    inputs:
      baseline-tag: { type: string, required: true }

permissions:
  contents: read

jobs:
  decide:                           # PR only: label run-perf OR a changed file matches eng/perf/hot-paths.txt
    if: github.event_name == 'pull_request'
    runs-on: ubuntu-24.04
    outputs: { run: ${{ steps.d.outputs.run }} }
    steps:
      - uses: actions/checkout@v7
        with: { fetch-depth: 0 }
      - id: d
        run: dotnet run -c Release --project eng/perf/PerfTool -- should-run --base ${{ github.event.pull_request.base.sha }} --head ${{ github.event.pull_request.head.sha }} --labels '${{ toJson(github.event.pull_request.labels.*.name) }}' >> "$GITHUB_OUTPUT"

  pr-compare:
    needs: decide
    if: needs.decide.outputs.run == 'true'
    runs-on: ubuntu-24.04
    timeout-minutes: 90
    permissions: { contents: read, pull-requests: write }
    env: { A2A_PERF_JOB: short }
    steps:
      # checkout merge-base into ./base and PR head into ./head (git worktree);
      # run the suite (all categories except Db): base → results/base1, head → results/head, base → results/base2;
      # PerfTool gate ... ; write summary to $GITHUB_STEP_SUMMARY;
      # post/update one PR comment (skip when the PR comes from a fork: the token is read-only there);
      # upload results as an artifact (14 days).

  nightly:
    if: github.event_name == 'schedule' || (github.event_name == 'workflow_dispatch' && inputs.mode == 'nightly')
    runs-on: ubuntu-24.04
    timeout-minutes: 180
    permissions: { contents: write }   # write the trend data branch
    steps:
      # full job, all categories except Db; plus Db on Sundays or when inputs.db;
      # PerfTool startup, PerfTool size-report (after dotnet pack);
      # download the previous nightly artifact (actions/download-artifact with run-id lookup, or the gh-pages data file);
      # PerfTool nightly ...; upload BDN JSON + markdown + nightly-summary.* as an artifact, retention-days: 90;
      # benchmark-action/github-action-benchmark (tool: benchmarkdotnet) for the chart, auto-push to the data branch,
      #   comment-on-alert: true, alert-threshold: '120%', fail-on-alert: false;
      # on PerfTool exit code 2, comment on the commit with the alert table.

  update-baseline:
    if: github.event_name == 'workflow_dispatch' && inputs.mode == 'update-baseline'
    runs-on: ubuntu-24.04
    steps:
      # run the suite with the default job; PerfTool update-baseline; upload perf/alloc-baseline.json as an artifact
```

Notes for this step:
- `should-run` is a small PerfTool subcommand. It matches `git diff --name-only base...head` against `hot-paths.txt` (glob) and the `run-perf` label. Test it.
- Check whether `github-action-benchmark`'s `benchmarkdotnet` parser keeps allocations. If it does not, the chart shows time only and `PerfTool nightly` stays the allocation alert (spec Goal 4 is met by PerfTool either way).
- The trend data branch: by default the action uses `gh-pages`, and package 13 also deploys the docs site to Pages. Use `benchmark-data-dir-path: perf/nightly` on that branch and write the arrangement in the task report for package 13. If 13 deploys Pages from an artifact rather than a branch, use a separate `perf-data` branch.

- [ ] **Step 9: `ci.yml` perf hook (every PR).** The job id is `perf` (see the hook-name note under the 01 table). If branch protection already lists a required check named `benchmarks`, add an owner action to the task report: replace it with `perf`.
  - The build already includes the benchmark project, because it is in the solution.
  - Add `dotnet run -c Release --no-build --project benchmarks/Ambinder.A2A.Benchmarks -- --list flat` (spec principle 3 check).
  - Run `dotnet run -c Release --project eng/perf/PerfTool -- lint-exceptions perf/exceptions.md`.
  - `tests/Ambinder.A2A.Perf.Tests` runs with `dotnet test` on the solution.

- [ ] **Step 10:** Run all PerfTool tests and the solution tests locally. Run `perf.yml` with `workflow_dispatch` (mode `nightly`) on the branch once. **Owner action:** create the label `run-perf`. **Commit** `ci(perf): nightly, PR comparison and allocation gate (time report-only)`.

The allocation gate needs `perf/alloc-baseline.json`, which Task 20 creates. Until then, `gate` runs with `--alloc-baseline` missing and treats the allocation check as "report only: no baseline yet". Test this case. Task 20 removes the leniency (Step 4 there).

---

## Task 15a: PD4, part A: `OmitEmptyLists` without an enumerator allocation

**Files:**
- Modify: `src/Ambinder.A2A.Protocol/Json/A2AJson.cs` (`OmitEmptyLists`, `HasAny`)
- Test: `tests/Ambinder.A2A.Protocol.Tests/Json/OmitEmptyListsTests.cs`
- Test: `tests/Ambinder.A2A.Perf.Tests/Json/SerializeAllocationTests.cs`

Today the `ShouldSerialize` delegate does `value is IEnumerable items && HasAny(items)`. `HasAny` uses `foreach` over the non-generic `IEnumerable`, which boxes `List<T>.Enumerator` (one allocation per list property per object).

- [ ] **Step 1: Record "before".**

```bash
dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*Json*" --exporters json
```

Copy the markdown table into `artifacts/perf-before/pd4.md` (not committed; it goes in the PR description).

- [ ] **Step 2: Characterization tests (they must pass before the change).** For each model with an `IReadOnlyList<>` property (`AgentTask.History`, `AgentTask.Artifacts`, `Message.Parts`, `AgentCard.Skills`, and the others found by reflection over `A2AJsonContext`), serialize these cases and compare with the golden bytes produced by the old code:
  - an empty `List<T>`
  - an empty `T[]`
  - `[]` (collection expression)
  - one item in each of those forms
  - `null`

  Add a reflection test that lists every `IReadOnlyList<>` property type and the concrete runtime types the deserializer and the models produce, so the fast path covers them.

- [ ] **Step 3: Allocation test (fails before the change).**

```csharp
[Fact]
[AllocationBudget(/* set in Step 6 */ 0)]
public void Serialize_task_with_empty_lists_has_no_enumerator_allocation()
{
    var task = Payloads.TaskWithEmptyLists();            // shared helper; history and artifacts empty List<T>
    var buffer = new ArrayBufferWriter<byte>(4096);
    using var writer = new Utf8JsonWriter(buffer);
    var bytes = Allocations.PerCall(() => { buffer.ResetWrittenCount(); writer.Reset(buffer); JsonSerializer.Serialize(writer, task, TaskInfo); });
    AllocationBudget.Assert<SerializeAllocationTests>(bytes);
}
```

`TaskInfo` is the typed accessor (02), or before 02 a `static readonly JsonTypeInfo<AgentTask>` cast once from `A2AJson.Options`. Share `Payloads` with the benchmarks through a linked file or a small shared test-data project (*writer's call*).

- [ ] **Step 4: Implement.**

```csharp
property.ShouldSerialize = static (_, value) => value switch
{
    null => false,
    System.Collections.ICollection c => c.Count > 0,     // List<T>, T[], ReadOnlyCollection<T>, compiler collection-expression types
    System.Collections.IEnumerable items => HasAny(items), // rare fallback; allocates
    _ => false,
};
```

Do not use `MakeGenericMethod` or other reflection to build per-type delegates, because the package is `IsAotCompatible`. If Step 2's reflection test finds a runtime list type that does not implement `ICollection`, add a type check for it before the fallback and add it to the test.

- [ ] **Step 5: Run the tests.** Expected: the characterization tests and every golden JSON test are byte-identical, and the proto round trip is green.

```bash
dotnet test Ambinder.A2A.slnx -c Release
```

- [ ] **Step 6: Set the budget.** Measure, then set `[AllocationBudget]` to the measured value + 10% (rounded up). Use 0 only if the measured value is 0 **and** this rule says "no allocation". It does not here, so the minimum is the measured value + 10%.

- [ ] **Step 7: Record "after"** with the same command as Step 1, on the same machine. **Commit** `perf(json): count list items without allocating an enumerator (PD4)`.

---

## Task 15b: PD4, part B: typed `JsonTypeInfo<T>` in every hot caller (B12)

**Precondition:** package 02's typed accessors exist (Task 0 Step 2). If they do not, **stop and report**. Do not add them yourself.

**Files** (find by symbol):
- Client: `JsonRpcA2AClient` (the request serialize `(JsonTypeInfo<TRequest>)A2AJson.Options.GetTypeInfo(typeof(TRequest))`), `RestA2AClient` (`SerializeToElement(request, (JsonTypeInfo<TRequest>)...)`, `SerializeToUtf8Bytes(body, A2AJson.Options.GetTypeInfo(body.GetType()))`), `ClientSupport` (`element.Deserialize((JsonTypeInfo<T>)A2AJson.Options.GetTypeInfo(typeof(T)))`), `A2ACardResolver` (`A2AJsonContext.Default.AgentCard`, which becomes the accessor, because 02 makes the context internal and the context metadata lacks the modifier)
- AspNetCore: `JsonRpcEndpoint`'s `Method` table (`Type ParamsType`, `Type? ResultType`), `JsonRpcMessages.Result` (`GetTypeInfo(resultType)`), `TransportErrors.Deserialize(JsonElement, Type)`, `RestEndpoints` (result write `GetTypeInfo(type)`, stream frame `GetTypeInfo(typeof(StreamResponse))`), `AgentCardEndpoints` (`GetTypeInfo(typeof(AgentCard))`)
- Test: `tests/Ambinder.A2A.Perf.Tests/Json/TypedMetadataTests.cs`

- [ ] **Step 1: Record "before"** for `Json`, `Request` (client-only and server-only) and `Client`.

- [ ] **Step 2: Change the generic helpers to take metadata, not look it up.** Callers are not generic at the top, so they know the type.

```csharp
// before
private async Task<TResponse> SendAsync<TRequest, TResponse>(string method, TRequest request, CancellationToken ct)
// after
private async Task<TResponse> SendAsync<TRequest, TResponse>(string method, TRequest request,
    JsonTypeInfo<TRequest> requestInfo, JsonTypeInfo<TResponse> responseInfo, CancellationToken ct)
// call site
=> SendAsync("SendMessage", request, A2AJson.SendMessageRequest, A2AJson.SendMessageResponse, ct);
```

Use the accessor names exactly as package 02 shipped them.

- [ ] **Step 3: Server method table.** Replace `Type ParamsType` / `Type? ResultType` with `JsonTypeInfo ParamsInfo` / `JsonTypeInfo? ResultInfo`. Set them once in the static table from the typed accessors (`JsonTypeInfo<T>` derives from `JsonTypeInfo`). Change `TransportErrors.Deserialize` and `JsonRpcMessages.Result` to take a `JsonTypeInfo`. In `RestEndpoints`, change the `(result, type)` tuple to `(result, info)`.

- [ ] **Step 4: Test that no hot caller looks up by `Type`.** Add a source scan test in `Perf.Tests`. It greps `src/` (outside `Ambinder.A2A.Protocol`) for `GetTypeInfo(typeof(` and `GetTypeInfo(` + `.GetType()`, and expects none. Task 21 adds the analyzer ban, and this test catches the issue earlier.

- [ ] **Step 5:** Run the full solution tests, golden and TCK (the TCK when it exists). Record "after". **Commit** `perf: typed JsonTypeInfo<T> in hot callers instead of per-call Type lookup (PD4, B12)`.

---

## Task 16: PD1, JSON-RPC: read the body once, scan the envelope, deserialize from the span

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/RequestBody.cs` (add the pooled reader and keep the old method until Task 17 moves REST)
- Create: `src/Ambinder.A2A.AspNetCore/Transports/PooledBody.cs`
- Create: `src/Ambinder.A2A.AspNetCore/Transports/JsonRpc/JsonRpcEnvelope.cs`
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/JsonRpc/JsonRpcEndpoint.cs` (`HandleAsync`, `DispatchAsync`)
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/JsonRpc/JsonRpcModels.cs` (`JsonRpcRequest`, `JsonRpcMessages.TryParse` becomes the scanner's checks)
- Test: `tests/Ambinder.A2A.AspNetCore.Tests/JsonRpc/EnvelopeCharacterizationTests.cs` (or whichever test project holds JSON-RPC transport tests after 01 and 12)
- Test: `tests/Ambinder.A2A.Perf.Tests/Transports/RequestParseAllocationTests.cs`

Today's flow: `MemoryStream` + a 16 KB chunk array, then `ToArray()`, then `JsonDocument.Parse`, then `TryGetProperty` + `Clone()` for `id` and `params`, then `JsonSerializer.Deserialize(JsonElement, …)`. Target (spec PD1): read once into a pooled buffer, find `method` and the `params` range with `Utf8JsonReader` (`params` may come before `method`, which is fine because the buffer is whole), deserialize the typed request from the span, and keep the size limit and the 413 behavior.

- [ ] **Step 1: Record "before"** for `Request` server-only (JSON-RPC), with the 1 KB and 1 MB bodies.

- [ ] **Step 2: Characterization tests (they must pass on the old code).** Over `TestServer`, POST each body below and assert the **exact response bytes** (status, content type and body):

| Case | Body |
|---|---|
| empty body | `` |
| whitespace only | `   ` |
| invalid JSON | `{"jsonrpc":` |
| trailing garbage | `{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{"id":"x"}} x` |
| UTF-8 BOM before a valid body | `EF BB BF` + valid |
| batch | `[{"jsonrpc":"2.0","id":1,"method":"GetTask"}]` |
| root not an object | `"hi"`, `42`, `null` |
| bad id type | `{"jsonrpc":"2.0","id":{},"method":"GetTask"}` |
| id escaped string | `{"jsonrpc":"2.0","id":"Ab","method":"GetTask","params":{"id":"missing"}}` (the response must echo the id bytes exactly as today) |
| id number forms | `1`, `1.0`, `-0`, `1e2` |
| missing / wrong jsonrpc | no member; `"1.0"`; `2` |
| missing / empty / non-string method | |
| unknown method | `"Nope"` |
| params before method | `{"params":{"id":"x"},"id":1,"method":"GetTask","jsonrpc":"2.0"}` |
| params absent / null | |
| duplicate members | `"method"` twice with different values; `"params"` twice; `"id"` twice (pin which one wins today) |
| invalid params (wrong type) | `"params":{"id":5}`, with pretty-printed (multi-line) params so the line and byte positions show in the message |
| deep nesting | 65 levels of `[` in params |
| too large | Content-Length over the limit; chunked over the limit |

These tests pin current behavior, including the duplicate-member choice. Do not change any expected value later.

- [ ] **Step 3: Allocation tests (fail before the change).** Server-only JSON-RPC `GetTask` parse with a 1 KB and a 1 MB body (one text part of 1 MB). Measure only the endpoint's parse-and-dispatch path, which you can call through `TestServer` with the handler stubbed. The budget is set in Step 7. P2 says "a 1 MB body is read with bounded extra allocation": the 1 MB case must not allocate a second body-sized array.

- [ ] **Step 4: `PooledBody` and `RequestBody.ReadPooledAsync`.**

```csharp
/// <summary>A request body in a rented array. Dispose returns it (cleared: bodies can hold secrets, rule P9).</summary>
internal struct PooledBody : IDisposable
{
    private byte[]? _array;
    public int Length { get; }
    public PooledBody(byte[] array, int length) { _array = array; Length = length; }
    public readonly ReadOnlySpan<byte> Span => _array.AsSpan(0, Length);
    public readonly ReadOnlyMemory<byte> Memory => _array.AsMemory(0, Length);
    public void Dispose()
    {
        if (_array is { } a) { _array = null; ArrayPool<byte>.Shared.Return(a, clearArray: true); }
    }
}
```

`ReadPooledAsync(HttpContext http, long maxBytes)` returns `PooledBody?`, with null meaning 413. It keeps all three of today's checks: the `Content-Length` precheck, lowering `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`, and catching the `BadHttpRequestException` 413. The size limit is enforced while reading (P14):
- Rent `min(ContentLength ?? 16 KB, maxBytes + 1)`.
- When the buffer is full, rent double the size (capped at `maxBytes + 1`), copy, and return the old buffer.
- Once `length > maxBytes`, return every rented buffer and return null.

- [ ] **Step 5: `JsonRpcEnvelope.TryScan`.**

```csharp
internal readonly record struct JsonRpcRequest(JsonElement Id, string Method, int ParamsStart, int ParamsLength); // ParamsLength 0 = absent or null

internal static class JsonRpcEnvelope
{
    /// <summary>
    /// One pass over the whole body with Utf8JsonReader. Validates that the body is one JSON value (same reader options as
    /// JsonDocument.Parse: no comments, no trailing commas, max depth 64). Records the top-level members it needs.
    /// Returns ParseError for invalid JSON, InvalidRequest in exactly the order and with exactly the messages of the old TryParse.
    /// </summary>
    public static bool TryScan(ReadOnlySpan<byte> body, out JsonRpcRequest request, out JsonElement badId, out A2AException? error);
}
```

  Implementation rules:
  - **Validation:** read the root. If it is `StartArray`, report "Batch requests are not supported." only after the whole body has been checked as valid JSON. The old code parsed the whole document first, so an invalid batch is a -32700 parse error, not -32600. Do the same for every check: finish validation (`reader.Skip()` over each value) before reporting envelope errors.
  - **Duplicate members:** follow what Step 2 pinned. If `JsonElement.TryGetProperty` returned the last occurrence, keep the last. If it returned the first, keep the first.
  - **`id`:** at the id token, `JsonElement.ParseValue(ref reader)` (this keeps the exact bytes, including escapes, as today's `Clone()` does). Check its kind exactly as `TryParse` does.
  - **`jsonrpc` and `method`:** `reader.ValueTextEquals("2.0"u8)`, and `GetString()` for `method` (one string per request, which the dispatch dictionary needs).
  - **`params`:** record `TokenStartIndex` at its value. After `reader.Skip()`, the end is `BytesConsumed`. When the value is JSON `null`, treat it as absent (as today: `Undefined or Null` → `{}`).
  - **BOM:** follow what Step 2 pinned. If `JsonDocument.Parse` accepted a leading BOM, slice it off before scanning. If it rejected it, reject it.

- [ ] **Step 6: Use it in `JsonRpcEndpoint`.**
  - `using var body = await RequestBody.ReadPooledAsync(...)`. The `using` must cover the whole dispatch, **including the stream**. Streaming methods deserialize their params before the first await on the event stream, so the buffer may be returned after deserialization. Make that explicit: deserialize, then dispose the body before `SseStreaming.RunAsync`, and add a test that a streaming request still works after the buffer is cleared.
  - Deserialize the params with `JsonSerializer.Deserialize(body.Span.Slice(start, length), method.ParamsInfo)`. When params are absent, use `"{}"u8`.
  - Keep the `TransportErrors.Deserialize` error wrapping and messages, by adding a span overload next to the `JsonElement` one.
  - If Step 2's "invalid params, pretty-printed" case gives a different message (line or byte positions), use the fallback: `JsonElement.ParseValue` on the params range, then the existing `JsonElement` overload. This is still one body read and no body copy. Write which path was taken in the PR description.

- [ ] **Step 7: Run everything.** Characterization, golden, transport, TCK and the full solution must pass. Set the allocation budgets (measured + 10%). Record "after". **Commit** `perf(jsonrpc): read the body once into a pooled buffer and parse the envelope with Utf8JsonReader (PD1)`.

---

## Task 17: PD1, REST: one pooled read, no `ToArray`, no re-serialize to `JsonElement`

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/Rest/RestEndpoints.cs` (body read, `ParseBody`, `Deserialize<T>`)
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/RequestBody.cs` (delete the old `byte[]` `ReadAsync` when no caller is left)
- Test: `tests/Ambinder.A2A.AspNetCore.Tests/Rest/BodyCharacterizationTests.cs`
- Test: extend `tests/Ambinder.A2A.Perf.Tests/Transports/RequestParseAllocationTests.cs`

Today the flow is: `ReadAsync`, then `byte[]`, then `ParseBody` (`bytes.All(...)` LINQ whitespace check, `JsonNode.Parse`), then `PathWins` edits the `JsonObject`, then `JsonSerializer.SerializeToElement(body)` (reflection-based, no type info), then `Deserialize(JsonElement, Type)`.

**Keep `JsonNode` for the REST path** *(plan writer's call)*. `PathWins` needs to know whether a body member is present, non-string, or empty. Today's error messages come from deserializing a re-serialized, compact `JsonObject`, so their line and byte positions refer to the compact form. A typed deserialize straight from the original bytes would change those messages, and that breaks "output behavior identical". So the fix removes the copies around the node, not the node itself.

- [ ] **Step 1: Record "before"** for REST `SendMessage` server-only (1 KB, 1 MB).
- [ ] **Step 2: Characterization tests (they must pass on the old code).** Exact response bytes for:
  - an empty body on send (required) and on cancel (optional)
  - whitespace-only bodies
  - invalid JSON
  - a non-object root
  - `tenant` in the body equal to, different from, empty, a number, or `null` versus the path tenant
  - `taskId` the same checks, on create-push
  - cancel with and without `id` in the body
  - invalid params in pretty-printed bodies (to pin the messages)
  - too large (Content-Length and chunked)
- [ ] **Step 3: Implement.**
  - `using var body = await RequestBody.ReadPooledAsync(...)`.
  - Do the whitespace check with a span loop, with no LINQ.
  - Parse the node with `JsonNode.Parse(body.Span)` (the `ReadOnlySpan<byte>` overload), and dispose the body right after this parse.
  - Leave `PathWins` unchanged.
  - Change `Deserialize<T>(JsonObject body, JsonTypeInfo<T> info)` to use `body.Deserialize(info)`, which deserializes from the node with typed metadata and replaces `SerializeToElement(body)`. Keep the `TransportErrors` exception wrapping and messages.
  - If Step 2 shows that `body.Deserialize(info)` gives a different message from today's `SerializeToElement` + `Deserialize(JsonElement)` path, use `JsonSerializer.SerializeToElement(body, A2AJson.<JsonNode or JsonElement accessor>)` with typed metadata. That keeps the old shape and still drops the reflection-based overload, which Task 21's P1 ban needs anyway.
- [ ] **Step 4:** Run everything, set the budgets, and record "after". **Commit** `perf(rest): one pooled body read and typed node deserialize (PD1)`.

---

## Task 18: PD2: SSE frames serialized straight into the response `PipeWriter`

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/Sse/SseWriter.cs`
- Create: `src/Ambinder.A2A.AspNetCore/Transports/Sse/SseFrameWriter.cs`
- Create: `src/Ambinder.A2A.AspNetCore/Transports/Sse/ISseFrameFormat.cs` (with `JsonRpcFrameFormat` and `RestFrameFormat` implementations)
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/Sse/SseStreaming.cs`
- Modify: `JsonRpcEndpoint` (stream branch), `RestEndpoints.StreamAsync`, `JsonRpcMessages` (add `WriteResult(Utf8JsonWriter, JsonElement id, …)` and `WriteError(...)` that write into a given writer)
- Test: `tests/Ambinder.A2A.AspNetCore.Tests/Sse/SseWireCharacterizationTests.cs`
- Test: `tests/Ambinder.A2A.Perf.Tests/Sse/SseWriteAllocationTests.cs`, plus the P5 heap test

Today each event goes through a `Func<StreamResponse, byte[]>`, which gives a `byte[]` per event. Then come three `Body.WriteAsync` calls and a `FlushAsync` per event. The target (spec PD2, P3):
- Serialize with `Utf8JsonWriter` into `HttpResponse.BodyWriter`.
- Use no `byte[]` per event and no closure per event.
- Flush once when the next event is not ready yet.
- Provide a heartbeat write for package 09.

- [ ] **Step 1: Public surface.** If Task 0 found `SseWriter` still public, keep `SseWriter.StartAsync` and `SseWriter.WriteEventAsync(HttpResponse, ReadOnlyMemory<byte>, CancellationToken)` with today's behavior, because package 02 owns the public API. The SDK's own streaming stops calling `WriteEventAsync`. If `SseWriter` is already internal, you may change it freely.
- [ ] **Step 2: Record "before"** for `Stream` (`SseWriteFrame` micro, and 1,000 chunks end to end per transport).
- [ ] **Step 3: Characterization tests (they must pass on the old code).** Over `TestServer`, stream 3 and 50 events, with an error after event 2, for both JSON-RPC and REST. Assert:
  - the exact concatenated response bytes, including the final error frame
  - the headers: `Content-Type`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`
  - that an error before the first event is a plain error response (not SSE)
  - that an empty stream commits SSE headers and ends
  - that a client disconnect ends quietly

  Compare bytes, not flush timing.
- [ ] **Step 4: `ISseFrameFormat` and the writer.**

```csharp
internal interface ISseFrameFormat
{
    void WriteEvent(Utf8JsonWriter json, StreamResponse e);     // the JSON payload of one data frame
    void WriteError(Utf8JsonWriter json, A2AException error);
}

// JSON-RPC: {"jsonrpc":"2.0","id":<id>,"result":<StreamResponse>} — one instance per stream holds the id (no per-event closure)
internal sealed class JsonRpcFrameFormat(JsonElement id) : ISseFrameFormat { ... uses JsonRpcMessages.WriteResult/WriteError ... }
// REST: <StreamResponse> / RestErrorWriter's error shape written into the writer
internal sealed class RestFrameFormat : ISseFrameFormat { public static readonly RestFrameFormat Instance = new(); ... }

/// <summary>Writes SSE frames into the response PipeWriter. Single writer: callers (the stream loop, package 09's heartbeat) must not call it concurrently.</summary>
internal sealed class SseFrameWriter
{
    private static ReadOnlySpan<byte> Prefix => "data: "u8;
    private static ReadOnlySpan<byte> Suffix => "\n\n"u8;
    private static ReadOnlySpan<byte> Heartbeat => ":\n\n"u8;   // SSE comment; package 09 decides the exact text and the timer
    private const int FlushThresholdBytes = 16 * 1024;          // plan writer's call: never hold more than this unflushed

    private readonly PipeWriter _out;
    private readonly Utf8JsonWriter _json;
    private long _unflushed;

    public SseFrameWriter(HttpResponse response)
    {
        _out = response.BodyWriter;
        _json = new Utf8JsonWriter(_out);   // one per stream; Reset() per frame
    }

    public void WriteEvent(ISseFrameFormat format, StreamResponse e) { Write(Prefix); _json.Reset(_out); format.WriteEvent(_json, e); _json.Flush(); _unflushed += _json.BytesCommitted; Write(Suffix); }
    public void WriteError(ISseFrameFormat format, A2AException error) { /* same shape, format.WriteError */ }
    public void WriteHeartbeat() => Write(Heartbeat);
    public bool ShouldFlush => _unflushed >= FlushThresholdBytes;
    public async ValueTask<bool> FlushAsync(CancellationToken ct)
    {
        _unflushed = 0;
        var r = await _out.FlushAsync(ct).ConfigureAwait(false);
        return !r.IsCompleted && !r.IsCanceled;   // false: the client is gone, stop the loop
    }
    private void Write(ReadOnlySpan<byte> bytes) { _out.Write(bytes); _unflushed += bytes.Length; }
}
```

Make sure `_json.Flush()` and `BytesCommitted` count each frame's bytes once and only once. `Reset` sets `BytesCommitted` back to 0. Write a unit test for that.

- [ ] **Step 5: The loop in `SseStreaming.RunAsync`.**

```csharp
await using var e = events.GetAsyncEnumerator(ct);
var hasFirst = await e.MoveNextAsync().ConfigureAwait(false);
await SseWriter.StartAsync(http.Response, ct).ConfigureAwait(false);    // flush headers through BodyWriter
if (!hasFirst) return;
var writer = new SseFrameWriter(http.Response);
try
{
    while (true)
    {
        writer.WriteEvent(format, e.Current);
        var next = e.MoveNextAsync();
        if (!next.IsCompleted || writer.ShouldFlush)                      // flush only when the next event is not ready yet
            if (!await writer.FlushAsync(ct).ConfigureAwait(false)) return;
        if (!await next.ConfigureAwait(false)) break;
    }
    await writer.FlushAsync(ct).ConfigureAwait(false);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
catch (Exception ex)
{
    writer.WriteError(format, TransportErrors.ToA2AException(ex, logger));
    await writer.FlushAsync(ct).ConfigureAwait(false);
}
```

`StartAsync` must flush through `BodyWriter.FlushAsync`, not `Body.FlushAsync`, so that the two APIs are not mixed on one stream. Events stay in order with one writer (spec 3.1.2). The bounded channel upstream, plus `FlushThresholdBytes`, keeps memory per stream O(1) (P5).

- [ ] **Step 6: Allocation tests.**
  - `SseFrameWriter` frame write: after warm-up, the budget is measured + 10%. The target is close to 0, because the `Utf8JsonWriter` is reused and the pipe memory is pooled.
  - **P5 heap check:** stream 100,000 events through `TestServer` to one reader. Measure `GC.GetTotalAllocatedBytes(true)` per event for the first 10,000 events and for the full 100,000. They must agree within 5%. Also, `GC.GetTotalMemory(forceFullCollection: true)` after 100,000 events must not exceed the value after 10,000 by more than 256 KB (*plan writer's call* on the numbers; tune them once with a measurement and write the measured values in the test comment).

- [ ] **Step 7:** Run everything (characterization, golden, transports, TCK, the full solution). Record "after" for `Stream`. **Commit** `perf(sse): serialize frames into the response PipeWriter and flush when idle (PD2)`.

---

## Task 19: PD5 decision (gRPC mapper)

**Files:** possibly `src/Grpc.Shared/ProtoMapper.*.cs`, plus a note in the PR description.

- [ ] **Step 1:** From Task 4's numbers, compute bytes/op ÷ serialized message size for each mapper direction and model.
- [ ] **Step 2:** If every ratio is ≤ 3, change nothing. Record the table in the PR description and the task report only (spec PD5: "record and leave"). Do not add it to `perf/exceptions.md`, because it is not a regression.
- [ ] **Step 3:** If any ratio is > 3, fix that conversion in M1. Do not use an intermediate JSON or object copy (P13). Keep the proto round-trip tests green, and record before and after.
- [ ] **Step 4:** If the M1 numbers show **another** path above 5% of a request's time or allocation, add a PD row to the spec's table in a docs commit, with the owner package proposed (spec §3: "debts are written down"). Do not fix it here unless 14 owns it.
- [ ] **Step 5: Commit** (only if code changed) `perf(grpc): <what> (PD5)`.

---

## Task 20: Allocation unit tests (M1 list) and the committed allocation baseline

**Files:**
- Tests in `tests/Ambinder.A2A.Perf.Tests/` (some already exist from Tasks 15–18)
- Create: `eng/perf/PerfTool/UpdateBaselineCommand.cs`, `eng/perf/update-baseline`, `eng/perf/update-baseline.ps1`
- Create: `perf/alloc-baseline.json`

- [ ] **Step 1: Complete the M1 list** (spec acceptance M1). Each item is a `[Fact]` with `[AllocationBudget]` set to measured + 10%:

| Test | Source |
|---|---|
| `SseWriter` frame write | Task 18 |
| SSE read per event (today's client `SseReader`; package 10 replaces it and lowers the budget) | new |
| JSON-RPC request parse, 1 KB body and 1 MB body | Task 16 |
| Event bus publish with no subscriber | new |
| Cached card response (second call onward, P11) | new |
| Serialize `StreamResponse` with no telemetry listener attached (P8) | new |
| 100,000-event stream heap check (P5) | Task 18 |
| Store get/save at history 200 (PD6) | **when package 05 lands**; until then, a note in the task report and an open acceptance item, not a skipped test |

Budgets of 0 only where a rule says "no allocation". `budgets.json` uses `maxBytes: 0` for the P11 cached card and the P8 listener-off cases (spec §4 "Time budget"). Their unit tests may still carry measured + 10% if the measured value is not 0, and the task report then names it as a finding against P8 or P11.

- [ ] **Step 2: `PerfTool update-baseline`.**
  1. Run the suite with the default job (all categories except `Db`).
  2. Read the full JSON.
  3. Write `perf/alloc-baseline.json`:

```json
{
  "schema": 1,
  "commit": "<git rev-parse HEAD>",
  "date": "<UTC ISO-8601>",
  "machine": { "os": "...", "cpu": "...", "cores": 4, "runtime": ".NET 10.0.x", "sdk": "10.0.x", "benchmarkDotNet": "x.y.z" },
  "scenarios": { "<FullName>": { "bytesPerOp": 1234 } }
}
```

  Sort the keys so diffs are stable. The `eng/perf/update-baseline` (bash) and `.ps1` wrappers call `dotnet run -c Release --project eng/perf/PerfTool -- update-baseline`. Test the writer with a fixture.

- [ ] **Step 3: Generate the baseline after PD1, PD2 and PD4 have landed.** Use `perf.yml`'s `workflow_dispatch` (mode `update-baseline`), so the machine info is the CI runner's. Download the artifact and commit it. Write the run URL in the PR description.
- [ ] **Step 4:** Remove the "no baseline yet" leniency from `gate` (Task 14 Step 10). From now on, a missing baseline row fails.
- [ ] **Step 5: Commit** `perf: allocation unit tests for the M1 hot paths` and `perf: commit M1 allocation baseline`.

---

## Task 21: Banned symbols for P1 and P10

**Files:**
- Modify: `eng/BannedSymbols.txt` (package 02's file; add a `# Package 14 performance rules` section)
- Modify: offending call sites (listed below)
- Test: `tests/Ambinder.A2A.Perf.Tests/Rules/BannedSymbolsTests.cs` (asserts the entries exist)

**P1 part** (precondition: 02's accessors and the banned-symbols mechanism are in place).

- [ ] **Step 1: Add the bans.** Ban the `JsonSerializer` overloads that take only `JsonSerializerOptions` or nothing, for `Serialize`, `SerializeAsync`, `SerializeToUtf8Bytes`, `SerializeToElement`, `SerializeToDocument`, `SerializeToNode`, `Deserialize` and `DeserializeAsync`, plus `JsonSerializerOptions.GetTypeInfo(System.Type)`.
  - Write the doc-comment IDs (`M:System.Text.Json.JsonSerializer.Serialize``1(``0,System.Text.Json.JsonSerializerOptions)`, and so on) by checking the actual overload list of the .NET 10 `System.Text.Json`. Do not write them from memory: one wrong ID is silently ignored.
  - Add a test that builds a tiny source file using each banned overload and expects RS0030. Run the analyzer through `CSharpCompilation.WithAnalyzers`, or put a "should fail" sample project behind a test that runs `dotnet build` and expects failure (*writer's call*).
  - `GetTypeInfo(Type)` is used inside `Ambinder.A2A.Protocol` to build the accessors. Allow it there with `[SuppressMessage("ApiDesign", "RS0030", Justification = "P1: builds the typed accessors once")]` on that one member, and nowhere else.
- [ ] **Step 2: Fix every offender the build reports.** Known Phase 1 sites, found by symbol:
  - `PushNotificationSender`: `SerializeToUtf8Bytes(item.Event, A2AJson.Options)` becomes the typed accessor. This is a one-line call change that leaves PD7 to 08.
  - `RestQuery`: `SerializeToElement(obj)` with no options is reflection-based. Make it typed. If the object is a `Dictionary<string, …>`, add the dictionary type to a source-generated context local to that package, or write it with `Utf8JsonWriter`.
  - `ClientSupport`: `SerializeToElement(pairs.ToDictionary(...))` gets the same treatment.
  - Any `GetTypeInfo(typeof(...))` left after Task 15b.

  Each fix keeps the bytes the same. The golden and transport tests prove it.
- [ ] **Step 3:** Check spec acceptance: "No hot path outside `Ambinder.A2A.Protocol` uses a reflection-based `JsonSerializer` overload". The ban plus a clean build is the proof. **Commit** `build(perf): ban reflection-based JsonSerializer overloads (P1)`.

**P10 part** (precondition: package 10 has replaced the per-client `SocketsHttpHandler` (R27). Task 0 Step 2 lists the remaining `new SocketsHttpHandler` / `new HttpClient` sites in `src/`.)

- [ ] **Step 4:** If `JsonRpcA2AClient` and `RestA2AClient` still construct their own handler or `HttpClient`, **stop the P10 part and report**. It is blocked on package 10, and the M1 acceptance item stays open. Do not fix R27 here.
- [ ] **Step 5:** Otherwise, ban `M:System.Net.Http.SocketsHttpHandler.#ctor` and every `M:System.Net.Http.HttpClient.#ctor*`. The `new HttpClient()` ban may already be in package 02's list, so do not add it twice. Allow these only at the documented exception sites, each with `[SuppressMessage("ApiDesign", "RS0030", Justification = "P10: <why>")]`:
  - package 10's one process-wide handler for non-DI use
  - the primary handlers handed to `IHttpClientFactory` (for example the push sender's `ConfigurePrimaryHttpMessageHandler` in `PushServiceCollectionExtensions`)

  Add a test that counts the RS0030 suppressions in `src/` and compares the count to a list in the test, so a new exception shows up as a diff.
- [ ] **Step 6: Socket test.** `tests/Ambinder.A2A.Perf.Tests/Client/HttpClientReuseTests`: create 1,000 clients through package 10's `AddA2AClient` (resolved per request) against Kestrel on loopback. Count accepted connections with `ListenOptions.Use(next => ctx => { Interlocked.Increment(ref count); return next(ctx); })`. Expect `count ≤ 4`, a small number that allows HTTP/2 multiple connections (*writer's call*; write the measured value). **Commit** `build(perf): ban per-call HttpClient and handler construction (P10)`.

---

## Task 22: Rules P1 to P15 in `CONTRIBUTING.md` and the PR template item

**Files:**
- Modify: `CONTRIBUTING.md` (add a "Performance rules" section)
- Modify: `.github/pull_request_template.md`

- [ ] **Step 1:** Copy the P1–P15 table from spec §2 **verbatim** into `CONTRIBUTING.md`, under "Performance rules (package 14)". Add one line per enforced rule that names its check: the test project, the analyzer, or the gate. Add the "how to run" commands:

```bash
dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*Streaming*"
```

```bash
dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --list flat
```

```bash
eng/perf/update-baseline
```

  Also write how to add an `exceptions.md` row, and that `Db` needs Docker.
- [ ] **Step 2:** Package 01 created the PR template with a "perf evidence when a hot path changed" item. Make sure it reads: "Hot path changed (see `eng/perf/hot-paths.txt`): before/after table from the benchmark suite, or the `perf` job comment link (rule P15)." Add the item if it is missing.
- [ ] **Step 3: Commit** `docs: performance rules P1–P15 and PR perf evidence item`.

---

## Task 23: Startup and size in the nightly summary

**Files:** `.github/workflows/perf.yml` (nightly job), `eng/perf/PerfTool/NightlyCommand.cs`

- [ ] **Step 1:** The nightly job runs `PerfTool startup --runs 10` and `dotnet pack`, then `PerfTool size-report`, and passes both files to `PerfTool nightly`. The summary markdown has a "Startup" table (median and p90 ms, heap) and a "Size" table (package, size, dependencies, plus the two publish sizes).
- [ ] **Step 2:** Trigger the nightly once by hand and check that the job summary shows both tables. **Commit** `ci(perf): startup and size in the nightly summary`.

---

## Task 24: M1 close-out

- [ ] **Step 1:** Go through the spec's M1 acceptance list and write the evidence for each item: a PR link, a run link, or a test name.

| Spec M1 item | Evidence |
|---|---|
| suite builds; every category has a scenario; `Db` skipped with a clear message | Tasks 1–11, CI run |
| PD1, PD2, PD4 fixed with before and after; golden and TCK green | PRs C, D, E |
| `perf/alloc-baseline.json` committed with machine info and commit | Task 20 |
| nightly green for seven days, JSON stored, trend page exists | **wait 7 days** after Task 14 lands; link 7 runs and the chart |
| allocation unit tests (list) | Task 20 table; the store item open until 05 |
| bans for P1 and P10 active, build clean | Task 21 (the P10 part may be blocked on 10) |
| no reflection-based `JsonSerializer` on hot paths outside Protocol (B12) | Task 21 Step 3 |
| P1–P15 in CONTRIBUTING; PR template item | Task 22 |
| `Startup` and `Size` in the nightly summary | Task 23 |

- [ ] **Step 2:** Report open items to the owner with the package that unblocks each one (05, 10, 03).

---

# Milestone M2: budgets enforced

M2 starts after the M1 baseline is committed and the nightly has run for at least seven days, so the variance of each scenario is known.

## Task 25: Turn on time budgets

**Files:** `perf/budgets.json`, `eng/perf/PerfTool/GateCommand.cs` (no logic change expected)

- [ ] **Step 1:** From at least 7 nightly runs, compute each scenario's coefficient of variation. Give any scenario with a CV under 3% a stricter `maxTimeRatio` (for example 1.10) in `scenarios`. A scenario with a CV over 15% keeps the default and gets a note in the PR description (spec §4: "a scenario can have a stricter value if the baseline shows it is stable").
- [ ] **Step 2:** Make sure every benchmark `FullName` in the suite has a row in `scenarios` (it may be empty `{}` to use the defaults), so "a row per scenario" (M2 acceptance) is checkable. Add a PerfTool test that compares the rows with `--list flat` output from the suite.
- [ ] **Step 3:** Set `"enforceTime": true`. **Commit** `perf: enforce time budgets from M2`.

## Task 26: Prove the gates with test PRs (do not merge them)

Ask the owner before opening these. They are throwaway PRs and get closed without merging.

- [ ] **Step 1: A slowdown PR.** Add `Thread.SpinWait` to the JSON-RPC envelope scan so that server-only `SendMessage` runs about 2x slower. Expected: the `perf` job runs (hot path) and fails on time.
- [ ] **Step 2: A docs-only PR.** Change `README.md`. Expected: the `perf` job does not run (the decide step says false), or it passes.
- [ ] **Step 3: An allocation PR.** Add a `new byte[64]` per frame in `SseFrameWriter.WriteEvent`. Expected: the allocation gate fails on the SSE scenarios and `SseWriteAllocationTests` fails too.
- [ ] **Step 4:** Link the three runs in the M2 report, then close the PRs and delete their branches.

## Task 27: Absolute targets (owner decision)

**Files:** `perf/budgets.json` (`targets`)

- [ ] **Step 1:** From the nightly JSON, propose numbers to the owner for:
  - (a) server-only `SendMessage` overhead over the control (as a ratio)
  - (b) bytes per streamed event
  - (c) startup to first card response, in ms

  Use the hardware the nightly runner reports (spec §4 "Absolute targets").
- [ ] **Step 2:** After the owner agrees, write them in this form:

```json
"targets": {
  "date": "YYYY-MM-DD",
  "hardware": "<from machine info>",
  "serverOverheadOverControl": { "scenario": "<FullName>", "maxRatio": 0.0 },
  "bytesPerStreamedEvent":     { "scenario": "<FullName>", "maxBytes": 0 },
  "startupToFirstCardMs":      { "maxMedianMs": 0 }
}
```

  Teach `gate` and `nightly` to check `targets` (a test per target).
- [ ] **Step 3: Commit** `perf: absolute targets agreed with the owner`.

## Task 28: Release gate against the previous release

**Files:** `.github/workflows/perf.yml` (`workflow_call` job), `.github/workflows/release.yml`, `eng/perf/PerfTool/GateCommand.cs`.

`release.yml` changes as follows:
- 01's plan gives it no benchmark job, so add a job `perf` with `needs: pack` that calls `./.github/workflows/perf.yml` (`workflow_call`, input `baseline-tag`).
- Make `publish` need `perf` as well, so a perf break blocks the release.
- If 01 already added a perf hook job there, fill that job instead.

- [ ] **Step 1: The `workflow_call` job.**
  1. Download `perf-results.zip` from the GitHub Release of `inputs.baseline-tag`. That is the previous release, which the caller finds with `git describe --tags --abbrev=0 <tag>^`.
  2. Run the default job on this commit.
  3. Run `PerfTool gate --base1 <previous> --head <current>` with no `base2`. The machines differ across runs, so the time check uses only ratios of the **control-normalized** values (scenario ÷ Control of the same run). Allocation is compared directly. Also apply `targets`.
  4. Fail on any enforced break without an exception.
  - Unit-test the control normalization.
- [ ] **Step 2:** The release job uploads this run's `perf-results.zip` to the new GitHub Release. Package 01's `github-release` job attaches files, so add it to the list there.
- [ ] **Step 3:** The first release has no previous asset. In that case the job only uploads, and says so in the summary.
- [ ] **Step 4:** Do a dry run with 01's `workflow_dispatch dry_run=true`. **Commit** `ci(perf): release gate against the previous release`.

## Task 29: Size and dependency budget in the release

**Files:** `eng/perf/PerfTool/SizeReportCommand.cs` (add `--previous`), `release.yml`

- [ ] **Step 1:** Compare with the previous release's `size.json` asset. Fail when a package grew by more than 15%, or gained a dependency, without a `size` or `dependency` row in `exceptions.md` (spec §6). Write the table to the release summary. Unit-test both failure cases and the exception case.
- [ ] **Step 2:** Upload `size.json` as a release asset. **Commit** `ci(perf): package size and dependency budget`.

## Task 30: Docs page data

**Files:** `eng/perf/PerfTool/DocsPageCommand.cs`

- [ ] **Step 1:** `PerfTool docs-page --nightly nightly-summary.json --out <path given by package 13>` renders markdown with:
  - the latest numbers per category
  - the hardware named
  - the date and commit
  - the startup and size tables
  - a fixed "these are not promises" sentence (spec Non-goals)

  Package 13 decides where it lives and runs it in its docs build. Test it with a fixture.
- [ ] **Step 2: Commit** `feat(perf): docs page generator from nightly JSON`.

## Task 31: Seam coverage and closure of the other packages' debts

**Files:** `perf/seams.json`, `tests/Ambinder.A2A.PerfTool.Tests/SeamCoverageTests.cs`

- [ ] **Step 1: Seam coverage.** List every public seam that has a default implementation, from package 02's public API files and the architecture doc: the store contracts, `ITaskEventBus`, push sender and outbox, card resolver, credential providers, middleware pipeline, client interceptors, and the others. Map each one to a benchmark `FullName`. A test checks that every mapped benchmark exists in the suite. For a seam with no scenario, add the scenario (spec principle 5, M2 acceptance).
- [ ] **Step 2: Debt closure.** Check that PD3 (10), PD6 (05), PD7 (08) and PD8 (05) each closed with the `Stream`, `Store` and `Push` numbers in their PRs. After 05 lands, add the PD8 allocation test ("100,000 distinct tasks leave no residue") and the PD6 store allocation test if 05 did not add them. Report any missing item to the owner, because they are not 14's code to fix.
- [ ] **Step 3: Switch the event bus.** If 06 has landed, move the `Pipeline` event-bus scenarios to `ITaskEventBus`.
- [ ] **Step 4: Commit** `test(perf): seam coverage map`.

## Task 32: M2 close-out

- [ ] **Step 1:** Write the evidence per spec M2 acceptance item:
  - the `budgets.json` rows and targets (Tasks 25, 27)
  - the gate test PRs (Task 26)
  - the release gate dry run (Task 28)
  - PD3/6/7/8 closure (Task 31)
  - seam coverage (Task 31)
  - the size budget (Task 29)
  - the docs page (Task 30, together with 13)
- [ ] **Step 2:** Report to the owner.

---

## Owner actions (cannot be done by code)

| # | Action | When |
|---|---|---|
| 1 | Create the label `run-perf` | before Task 14 is merged |
| 2 | Allow the nightly job to push trend data (branch protection exception for the data branch; agree the branch with package 13) | Task 14 |
| 3 | Approve each `perf/` change through CODEOWNERS (O2: no second reviewer) | always |
| 4 | Approve or reject each `exceptions.md` row | always |
| 5 | Agree the absolute targets (a)–(c) | Task 27 |
| 6 | Allow the three throwaway gate PRs | Task 26 |

---

## Execution notes

- **Executor:** run this plan with **superpowers:subagent-driven-development**: a fresh subagent per task, then a review subagent after each task, then the next task.
- **Model:** **every subagent runs on model `sonnet`.** This covers implementers, reviewers and fix-up agents. Pass `model: "sonnet"` on every Agent call. The owner's rule is that planning is done on Opus and execution on Sonnet.
- **Order:** do M1 in task order, 0 → 24. Tasks 3–11 only add benchmark files, so they may run in parallel subagents once Tasks 1 and 2 are merged. Tasks 15a, 15b, 16, 17 and 18 change `src/` and must run **one at a time**, each with its own "before" and "after" on the same machine. M2 (25–32) starts only after Task 24 and seven nightly runs.
- **What each subagent gets:** this plan's task text, the spec path (`docs/superpowers/specs/2026-11-03-sdk-14-performance-design.md`), the Global constraints section, and the Task 0 results (paths after the rename, and which of 02, 03, 05, 06, 08, 10 have landed).
- **Stop conditions to report, not to work around:**
  - any package 01 precondition is missing (Task 0)
  - package 02's accessors are missing (Task 15b, Task 21 P1)
  - package 10 has not landed (Task 21 P10)
  - a characterization test cannot be kept byte-identical
  - a benchmark correctness check fails
- **Reviewer checklist per PD task:**
  - the characterization tests were added **before** the change and are unchanged after it
  - golden, proto and TCK are green
  - before and after numbers come from the same machine and job
  - the budget is set to measured + 10%
  - no wire change
  - no LINQ, capturing closures or `params` arrays in per-event code (P9)
- **Benchmarks are slow.** Use `--filter` per category while developing. Full runs belong to the nightly. Set `A2A_PERF_JOB=short` for local A/B work.
