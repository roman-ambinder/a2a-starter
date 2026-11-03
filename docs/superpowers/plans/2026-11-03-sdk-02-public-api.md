# SDK 02: Public API and versioning, implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. All subagents run on model `sonnet` (see [Execution notes](#execution-notes)). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the public surface of every shipped `Ambinder.A2A.*` package small, deliberate, documented and tracked. Hide the plumbing types, track the API in `PublicAPI.*.txt` files that fail the build on drift, make XML docs complete and linted, turn on the analyzer set and the banned-API list, move the registration methods to the conventional namespaces, and write the versioning policy.

**Spec:** `docs/superpowers/specs/2026-11-03-sdk-02-public-api-design.md` (binding), under the work plan `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md`. Package map: `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md`, Design section 1. Overview: `docs/architecture/sdk-architecture.md`, sections 2 and 3.

**Milestone scope:** M1 only (internals hidden, API tracking on). The freeze steps for `1.0.0-rc.1` and `1.0.0` are written down at the end under [Deferred tasks](#deferred-tasks-not-executed-in-m1). They are **not** executed with this plan.

**Architecture:**
- Build-time checks live in MSBuild files that package 01 creates (`src/Directory.Build.props`, `src/Directory.Build.targets`) and in `.editorconfig`. They apply to every project under `src/`, so packages added later get them for free.
- Run-time checks live in one new test project, `tests/Ambinder.A2A.PublicApi.Tests`. It references every shipped project and has **no** `InternalsVisibleTo`, so it sees exactly what a consumer sees. It holds `PublicSurfaceTests`, `XmlDocQualityTests` and `ApiFileTests`.
- Text rules (versioning, deprecation, experimental API, "what counts as API") go into `docs/policies/versioning.md`, linked from `README.md` and `CONTRIBUTING.md`.

**Tech stack:**
- .NET 10 SDK (from `global.json`), C# `latest`, nullable on, `TreatWarningsAsErrors` on
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` and `Microsoft.CodeAnalysis.BannedApiAnalyzers`: newest stable version on nuget.org at execution time, pinned in `Directory.Packages.props`
- xUnit v3 (already pinned: `xunit.v3` 4.0.2, `xunit.runner.visualstudio` 4.0.1, `Microsoft.NET.Test.Sdk` 18.11.0)
- Reflection over the referenced assemblies, no extra loader package (spec "Left to the builder": how the test loads assemblies)

---

## Precondition: package 01 has landed

This plan assumes package 01 (rename and split to `Ambinder.A2A.*`) is merged into `master` before execution starts. Every path below uses 01's names. Package 01 was being built in another session while this plan was written, so its exact output was not visible. The table lists every assumption about that output. **Task 0 checks each one.** The last column names the steps that depend on it.

| # | Assumption about package 01's output | Source in spec 01 | Steps that depend on it |
|---|---|---|---|
| A1 | The solution file is `Ambinder.A2A.slnx` at the repo root. | Design 3, step 1 | All build and test commands |
| A2 | These shipped projects exist: `src/Ambinder.A2A.Protocol`, `src/Ambinder.A2A.Server`, `src/Ambinder.A2A.AspNetCore`, `src/Ambinder.A2A.AspNetCore.Grpc`, `src/Ambinder.A2A.Client`, `src/Ambinder.A2A.Client.Grpc`, `src/Ambinder.A2A.Client.Entra`. Other rows of the package map may not exist yet. Folder = `AssemblyName` = `RootNamespace` = `PackageId`. | Design 1, rule 3; Design 2 | 1.3, 1.5, 3, 4, 9, 10 |
| A3 | Type names did not change. Namespaces are the Phase 1 ones with the `Ambinder.` prefix (for example `Ambinder.A2A.Server.Agents`), possibly moved into the new assembly that now holds the file (transports and hosting into `Ambinder.A2A.AspNetCore`, gRPC hosting into `Ambinder.A2A.AspNetCore.Grpc`, gRPC transport into `Ambinder.A2A.Client.Grpc`, `AzureIdentityCredentialProvider` into `Ambinder.A2A.Client.Entra`). | Design 2, 3 | 3, 4, 5 (every file is found by type name with `git grep`, never by a hard-coded path) |
| A4 | `src/Grpc.Shared/` holds `ProtoMapper` and is compiled into both gRPC packages with `<Protobuf ... Access="Internal">`. `ProtoMapper` may already be `internal`. `src/A2A.Protocol.Grpc` is gone. | Design 1, rule 4; acceptance "generated protobuf types and ProtoMapper are internal" | 3.4, 4.3 |
| A5 | `src/Shared/` holds `PageToken`, `TaskStateRules` and `RequestValidation` as linked files compiled `internal`. They may already be `internal`. | Design 1, rule 10 | 3.2 |
| A6 | `src/Directory.Build.props` exists and sets `GenerateDocumentationFile=true` and `EnablePackageValidation=true`. Because `TreatWarningsAsErrors` is on, 01 must have suppressed CS1591 somewhere (for example `<NoWarn>$(NoWarn);CS1591</NoWarn>`) to keep the build green. `src/Directory.Build.targets` may or may not exist. | Design 4 | 1.4, 8.1, 9 |
| A7 | `InternalsVisibleTo` entries were renamed. The test assemblies are named `Ambinder.A2A.<Package>.Tests`, plus `Ambinder.A2A.Integration.Tests`, `Ambinder.A2A.MockEntra.Tests` and the TCK SUT. | Design 2, "Test projects" | 3, 4 |
| A8 | `eng/package-dependencies.json` exists and lists every shipped package ID. | Design 6 | 1.5 |
| A9 | `.github/CODEOWNERS` has an entry for `PublicAPI.*.txt`; `.github/pull_request_template.md` has an "API files reviewed" checkbox; `CONTRIBUTING.md` and `CHANGELOG.md` (Keep a Changelog, "Breaking changes" heading) exist. | Design 5 | 11, 12 |
| A10 | `.github/workflows/ci.yml` still has a build-and-test job on Ubuntu that restores, builds and tests `Ambinder.A2A.slnx`. | Design 6 | 12 |
| A11 | The gRPC and proto tests that use `ProtoMapper` and `Lf.A2a.V1` types were moved by 01 to test projects that can see those internals (or use `extern alias`, because both gRPC packages contain the same generated types). | Design 1, rule 4 | 3.4, 4.3 |
| A12 | `samples/GettingStarted.Agent` and `samples/GettingStarted.Client` may or may not exist. (01's release smoke job uses `samples/GettingStarted.Agent`; package 13 owns the content.) | Design 7, step 8 | 5.4 |
| A13 | The public API is otherwise unchanged from Phase 1: the same public types as listed in spec 02 section 3, now under the new names. | Design 3: "no behavior change" | 1, 3, 4 |
| A14 | **`AddA2AServerCore` (owner decision, 2026-11-03).** 01 split the registration: `AddA2AServerCore(...)` in `Ambinder.A2A.Server` registers everything host-neutral and returns `A2AServerBuilder`; `AddA2AServer(...)` in `Ambinder.A2A.AspNetCore` has today's signature, calls `AddA2AServerCore` and adds the ASP.NET Core parts (authorization, `AgentAuthorizer`, the startup filter). The owner accepted this deviation from spec 01's package map, which lists `AddA2AServer` under `Ambinder.A2A.Server`. Server apps still call `AddA2AServer`. | 01 plan, builder decision 1 | 3, 5, 9, 10, 12 |

If an assumption is false in a way that a step below does not already cover, **stop and ask the owner.** Do not invent a workaround.

## Facts from the current code (Phase 1, before 01)

These were checked in the repo when this plan was written. They drive several steps.

- `ProtoEnumConverter<T>` is the enum converter (`src/A2A.Protocol/Json/ProtoEnumConverter.cs:15`). Spec 02 calls it `StrictEnumConverter<T>`; that type does not exist. This plan treats the spec row as meaning `ProtoEnumConverter<T>`. It is attached to the public enums `TaskState` and `Role` by `[JsonConverter(typeof(ProtoEnumConverter<...>))]` (`src/A2A.Protocol/Models/Enums.cs:6,21`).
- `A2AJsonContext` is used across assemblies in one place: `src/A2A.Client/A2ACardResolver.cs:41` (`A2AJsonContext.Default.AgentCard`). Everything else uses `A2AJson.Options`.
- Positional public records: `StoredTask` (`src/A2A.Server/Tasks/ITaskStore.cs:6`), `A2ACallContext` (`src/A2A.Server/A2ACallContext.cs:14`), and also `AgentRegistration` (`src/A2A.Server/Agents/AgentRegistration.cs:11`), `AgentRequestContext` (`src/A2A.Server/Agents/AgentRequestContext.cs:15`) and `A2AErrorInfo` (`src/A2A.Protocol/Errors/A2AError.cs:8`). The spec names only the first two. This plan does not convert any of them (their owner specs do; spec 02 section 4, "Records and models"). It lists all five in the API review checklist (Task 11).
- `A2AClientFactory` (`src/A2A.Client/A2AClientFactory.cs:7`) is public and is not in the spec 02 table. `samples/AgentClient` and the integration tests use it. It stays public (package 10 designs the client entry points). Its XML doc names `TracingA2AClient.Inner`, which becomes internal, so the doc text changes (Task 4).
- `A2AAuthException` is `public class` (not sealed) and derives from `Exception`, not `A2AException` (`src/A2A.Protocol/Errors/A2AAuthException.cs:7`). This plan does not change it; it goes on the API review checklist.
- `new HttpClient(` appears 3 times in `src/`: `Auth/ClientCredentialsProvider.cs:53`, `Transports/JsonRpcA2AClient.cs:36`, `Transports/RestA2AClient.cs:41` (all in the client).
- `GetAwaiter().GetResult()` appears once: `src/A2A.Server/Hosting/A2AStartupValidation.cs:37`, inside a synchronous `IStartupFilter`.
- 14 direct `Log*()` calls (no `[LoggerMessage]` yet): `Push/PushNotificationSender.cs` (5), `Tasks/TaskManager.cs` (6), `Tasks/TaskWriter.cs` (2), `Transports/TransportErrors.cs` (1).
- 220 lines with `await` in `src/`, 213 with `ConfigureAwait(false)`. The rest need checking under CA2007 (some are `await foreach` / `await using`).
- Tests that use types this plan makes internal: `tests/A2A.Server.Tests` (`TaskManager`, `TaskEventBroker`, `PushNotificationSender`, `PushSigningKey`, `TracingRequestHandler`, `InMemoryTaskStore`, `PageToken`, `WebhookUrlValidator`, `DefaultOwnerResolver`, `AgentAuthorizer`), `tests/A2A.Integration.Tests` (`TaskManager`, `PushNotificationSender`, `PushSigningKey`, `TracingRequestHandler`, `AgentCardInterfaces`, `SseReader`, the three transport clients, `TracingA2AClient`, `Lf.A2a.V1`), `tests/A2A.Protocol.Tests` (`ProtoMapper`, `Lf.A2a.V1`).
- Samples do not use any type this plan makes internal. (`samples/AgentClient` uses `A2AClientFactory` and the `ListTasksRequest.PageToken` property, which stay public.)

## Global constraints

- **Branch:** create `feat/sdk-02-public-api` from `master` before Task 0. One PR for the whole plan, or one PR per task group if the owner prefers; each commit must build and pass all tests on its own.
- **No behavior change on the wire.** The golden JSON tests and proto round-trip tests stay byte-for-byte the same. Telemetry names, config keys, header names, error codes and REST paths do not change.
- **No `[EditorBrowsable(Never)]` and no `.Internal` namespace** as a stand-in for `internal` (spec anti-goal).
- **No `InternalsVisibleTo` between shipped assemblies.** `InternalsVisibleTo` only to test assemblies and to `Ambinder.A2A.Benchmarks`. The one exception is `Ambinder.A2A.Integration.Tests`, allowed by owner decision on 2026-11-03 (Task 3, step 3.1).
- **Never regenerate an API file wholesale.** Add lines with the analyzer fix, read the diff, commit.
- **Build rules stay:** `TreatWarningsAsErrors`, nullable on, central package management. Every new package version goes into `Directory.Packages.props`.
- **Docs style:** short, plain sentences, no jargon (the repo style). This applies to XML docs too.
- **Every made-internal or moved public type gets two records:** a line under "Breaking changes" in `CHANGELOG.md` (preview section), and a row in `docs/migration/phase1-map.csv` (format from spec 13 section 5: `old symbol,new symbol,package,change`; create the file with that header line if it does not exist yet).
- **Package 03 runs alongside.** The package 03 plan (`docs/superpowers/plans/2026-11-03-sdk-03-spec-conformance.md`, section 0.4) follows this plan's rules for its new public types (`IA2AVersionDispatcher`, `A2AAgentRoute`, `IAgentCardContributor`, `AgentCardContext`, `A2AException.Details`, `A2AServerOptions.InterfaceTenants`). Either plan may land first. Where both change the same thing (`ProtoEnumConverter<T>` internal, `TaskStateRules` internal in `src/Shared`), do the change only if it is not already done, and keep the checks of both plans.
- **Commands:** build with `dotnet build Ambinder.A2A.slnx -c Release`; test with `dotnet test Ambinder.A2A.slnx -c Release`. A step that says "build" means this command passes with zero warnings.

## Review focus

The reviewer of each task checks these first.

1. **A consumer's own source-generated JSON context that includes `AgentTask` or `Message`.** Making `ProtoEnumConverter<T>` internal must not break a consumer's `JsonSerializerContext` that lists our models. Task 2 adds a test assembly without `InternalsVisibleTo` that proves it compiles and round-trips. If the source generator cannot use an internal converter named in a `[JsonConverter]` attribute, stop and ask the owner (do not make the converter public without a decision).
2. **The client card read after `A2AJsonContext` becomes internal.** `A2ACardResolver` must switch to the new typed accessor. The accessor comes from `A2AJson.Options`, which adds an "omit empty lists" modifier on write only. Reading must give the same `AgentCard` as before (Task 2 test).
3. **A test or sample that only compiled because a type was public.** After Tasks 3 and 4 the whole solution builds, including `samples/` and `tests/A2A.Tck.Sut`, with no new `InternalsVisibleTo` beyond the allowed ones.
4. **A secret in `ToString()`.** `AuthenticationInfo.Credentials` and `TaskPushNotificationConfig.Token` (and its nested `Authentication`) never appear in `ToString()` output (Task 6).
5. **An API file that was regenerated instead of edited.** Every line in each `PublicAPI.Unshipped.txt` was read once in Task 9. The list of types matches the disposition table in spec 02 section 3.
6. **EventIds are API.** `[LoggerMessage]` IDs added in Task 7 are fixed and unique per assembly.

---

### Task 0: Verify package 01's output

No code changes. The output is a short "Package 01 facts" section for the execution PR description.

**Files:** none changed.

- [ ] **0.1** `git fetch origin && git checkout master && git pull`. Confirm package 01's PR is merged (`git log --oneline -20`). If it is not merged, stop: this plan must not run before 01.
- [ ] **0.2** For each assumption A1 to A13, run the check and write the result (true, or what is different) into the "Package 01 facts" notes:
  - A1: `ls *.slnx`
  - A2: `ls src/` and `git grep -n "<PackageId>\|<AssemblyName>\|<RootNamespace>" -- 'src/**/*.csproj'`
  - A3: `git grep -nE "^namespace " -- 'src/**/*.cs' | awk -F'namespace ' '{print $2}' | sort | uniq -c`
  - A4: `ls src/Grpc.Shared && git grep -n "Protobuf" -- 'src/**/*.csproj' && git grep -n "class ProtoMapper" -- src`
  - A5: `ls src/Shared && git grep -nE "(public|internal) static class (PageToken|TaskStateRules|RequestValidation)" -- src`
  - A6: `cat src/Directory.Build.props; ls src/Directory.Build.targets; git grep -n "CS1591" -- '*.props' '*.targets' '*.csproj' .editorconfig`
  - A7: `git grep -n "InternalsVisibleTo" -- '*.csproj' '*.props' '*.cs'` and `ls tests/`
  - A8: `cat eng/package-dependencies.json`
  - A9: `cat .github/CODEOWNERS .github/pull_request_template.md; ls CONTRIBUTING.md CHANGELOG.md`
  - A10: `cat .github/workflows/ci.yml`
  - A11: `git grep -ln "ProtoMapper\|Lf.A2a.V1" -- tests` and the `ProjectReference`/`Aliases` lines of those test projects
  - A12: `ls samples/`
  - A13: `git grep -nE '^\s*public (static |sealed |abstract |partial |readonly )*(class|record|interface|enum|struct)\b' -- 'src/**/*.cs'` and compare with spec 02 section 3
- [ ] **0.3** `dotnet build Ambinder.A2A.slnx -c Release && dotnet test Ambinder.A2A.slnx -c Release`. Both must pass before any change. Record the test count.
- [ ] **0.4** If any assumption is false and the matching step does not already say what to do, stop and ask the owner.

---

### Task 1: The public surface guard test (red first)

Creates `tests/Ambinder.A2A.PublicApi.Tests` with `PublicSurfaceTests`. The test fails now (plumbing is still public). Tasks 2 to 5 make it pass.

**Files:**
- Create: `tests/Ambinder.A2A.PublicApi.Tests/Ambinder.A2A.PublicApi.Tests.csproj`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/ShippedAssemblies.cs`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/ShippedAssembliesTests.cs`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/PublicSignatures.cs`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/PublicSurfaceTests.cs`
- Modify: `Ambinder.A2A.slnx` (add the project)

- [ ] **1.1** Create the project. Copy the shape of an existing test project from 01 (`tests/Ambinder.A2A.Protocol.Tests/*.csproj`). Add a `ProjectReference` to **every** project under `src/` that is shipped (A2). No `InternalsVisibleTo` points at this assembly, ever.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
  <ItemGroup>
    <!-- One line per shipped project. ShippedAssembliesTests.All_shipped_projects_are_referenced fails when one is missing. -->
    <ProjectReference Include="../../src/Ambinder.A2A.Protocol/Ambinder.A2A.Protocol.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Server/Ambinder.A2A.Server.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.AspNetCore/Ambinder.A2A.AspNetCore.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.AspNetCore.Grpc/Ambinder.A2A.AspNetCore.Grpc.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Client/Ambinder.A2A.Client.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Client.Grpc/Ambinder.A2A.Client.Grpc.csproj" />
    <ProjectReference Include="../../src/Ambinder.A2A.Client.Entra/Ambinder.A2A.Client.Entra.csproj" />
  </ItemGroup>
</Project>
```

Match `OutputType` and any other property to what 01's test projects use (xUnit v3 on Microsoft.Testing.Platform, `global.json` `test.runner`).

- [ ] **1.2** Add the project to `Ambinder.A2A.slnx` under the tests folder.

- [ ] **1.3** Write `ShippedAssemblies.cs`. It finds the repo root, lists the shipped projects, and loads their assemblies from the test output folder.

```csharp
using System.Reflection;
using System.Xml.Linq;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>The shipped assemblies, as a consumer sees them (this test assembly has no InternalsVisibleTo).</summary>
internal static class ShippedAssemblies
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Shipped project names: every src/*/*.csproj that is not IsPackable=false.</summary>
    public static IReadOnlyList<string> ProjectNames { get; } = Directory
        .GetFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(IsShipped)
        .Select(Path.GetFileNameWithoutExtension)
        .Select(n => n!)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<Assembly> All { get; } = ProjectNames
        .Select(n => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, n + ".dll")))
        .ToArray();

    private static bool IsShipped(string csproj)
    {
        var packable = XDocument.Load(csproj).Descendants("IsPackable").LastOrDefault()?.Value;
        return !string.Equals(packable, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Ambinder.A2A.slnx not found above the test output folder.");
    }
}
```

If 01 marks shipped projects differently (for example `IsPackable=true` set only in `src/Directory.Build.props` and shared-source folders under `src/` that have no `.csproj`), adjust `IsShipped` and say so in the PR. `src/Shared` and `src/Grpc.Shared` must not count as shipped projects.

- [ ] **1.4** Write `PublicSurfaceTests.cs`. Three checks from spec 02 section 3, plus two that keep the test honest.

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ambinder.A2A.PublicApi.Tests;

public sealed class PublicSurfaceTests
{
    /// <summary>
    /// Assembly-name prefixes each package may expose in a public or protected signature, besides the BCL and our own packages.
    /// The rows follow spec 02 section 3 and the package map in spec 01. A new shipped project without a row fails the test.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedForeign = new(StringComparer.Ordinal)
    {
        ["Ambinder.A2A.Protocol"] = [],
        ["Ambinder.A2A.Server"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Client"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.AspNetCore"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Microsoft.Net.Http.Headers"],
        // gRPC packages: Grpc.AspNetCore / Grpc.Net.Client types only on their own Add.../Map... entry points (checked below), never Google.Protobuf.
        ["Ambinder.A2A.AspNetCore.Grpc"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Grpc.AspNetCore", "Grpc.Net.Client"],
        ["Ambinder.A2A.Client.Grpc"] = ["Microsoft.Extensions.", "Grpc.Net.Client"],
        ["Ambinder.A2A.Client.Entra"] = ["Microsoft.Extensions.", "Azure.Core"],
        ["Ambinder.A2A.AspNetCore.Entra"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Azure.Core"],
        ["Ambinder.A2A.Azure.KeyVault"] = ["Microsoft.Extensions.", "Azure.Core"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.PostgreSql"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore", "Npgsql"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.Sqlite"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.SqlServer"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.Cosmos"] = ["Microsoft.Extensions.", "Microsoft.Azure.Cosmos"],
        ["Ambinder.A2A.Push.Receiver"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Cluster"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Cluster.Entra"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Azure.Core"],
        ["Ambinder.A2A.Extensions.AI"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Extensions.AI.AgentFramework"] = ["Microsoft.Extensions.", "Microsoft.Agents.AI"],
        ["Ambinder.A2A.Compat.V0_3"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Compat.V0_3.AspNetCore"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Testing"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Testing.AI"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Testing.Conformance"] = ["Microsoft.Extensions.", "xunit."],
    };

    /// <summary>Spec 02 section 3, "internal" rows. A type here that is public fails the test by name.</summary>
    private static readonly string[] MustBeInternal =
    [
        "TaskManager", "TaskEventBroker", "PushNotificationSender", "PushSigningKey", "TracingRequestHandler",
        "PageToken", "TaskStateRules", "Releaser", "RequestValidation", "ServiceParameters", "RestErrorWriter",
        "GrpcErrorMapper", "SseWriter", "AgentCardInterfaces", "WebhookUrlValidator", "A2AGrpcService", "SseReader",
        "RestA2AClient", "GrpcA2AClient", "JsonRpcA2AClient", "TracingA2AClient", "ProtoMapper",
        "ProtoEnumConverter`1", "UtcTimestampConverter", "A2AJsonContext",
        // Default implementations (spec 02 section 3, writer's call as a default):
        "InMemoryTaskStore", "InMemoryPushConfigStore", "DefaultOwnerResolver", "AgentAuthorizer",
    ];

    /// <summary>Allowed InternalsVisibleTo targets besides "&lt;assembly&gt;.Tests". Integration.Tests: owner decision 2026-11-03 (Task 3, step 3.1).</summary>
    private static readonly string[] ExtraFriendAssemblies = ["Ambinder.A2A.Benchmarks", "Ambinder.A2A.Integration.Tests"];

    public static IEnumerable<object[]> Assemblies() => ShippedAssemblies.ProjectNames.Select(n => new object[] { n });

    [Fact]
    public void Every_shipped_project_has_an_allow_list_row()
    {
        var missing = ShippedAssemblies.ProjectNames.Where(n => !AllowedForeign.ContainsKey(n)).ToArray();
        Assert.True(missing.Length == 0, $"Add a row to AllowedForeign for: {string.Join(", ", missing)}");
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void No_forbidden_type_in_a_public_signature(string assemblyName)
    {
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        var allowed = AllowedForeign[assemblyName];
        var violations = PublicSignatures.Walk(assembly)
            .Where(use => !IsAllowed(use.Type.Assembly.GetName().Name!, allowed))
            .Select(use => $"{use.Member}: {use.Type.FullName} ({use.Type.Assembly.GetName().Name})")
            .Distinct()
            .ToArray();
        Assert.True(violations.Length == 0, "Forbidden types in public signatures:\n" + string.Join("\n", violations));
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void No_public_type_in_an_Internal_namespace(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => t.Namespace?.Split('.').Contains("Internal") == true).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void Named_plumbing_types_are_not_public(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => MustBeInternal.Contains(t.Name)).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void No_protobuf_type_is_public(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => t.Namespace?.StartsWith("Lf.A2a", StringComparison.Ordinal) == true).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void InternalsVisibleTo_only_to_tests_and_benchmarks(string assemblyName)
    {
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        var friends = assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName.Split(',')[0].Trim());
        var bad = friends.Where(f => f != assemblyName + ".Tests" && !ExtraFriendAssemblies.Contains(f)).ToArray();
        Assert.True(bad.Length == 0, $"{assemblyName} has InternalsVisibleTo for: {string.Join(", ", bad)}");
    }

    private static IEnumerable<Type> PublicTypes(string assemblyName)
        => ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName).GetTypes().Where(PublicSignatures.IsVisible);

    private static bool IsAllowed(string name, string[] allowedForeign)
        => name.StartsWith("Ambinder.A2A.", StringComparison.Ordinal)
           || IsBcl(name)
           || allowedForeign.Any(p => name.StartsWith(p, StringComparison.Ordinal));

    private static bool IsBcl(string name)
        => name is "System.Private.CoreLib" or "netstandard" or "mscorlib"
           || name.StartsWith("System.", StringComparison.Ordinal)
           || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
}
```

Notes for the implementer:
- `Microsoft.Extensions.` is allowed as a prefix for every non-Protocol package. Spec 02 says "`Microsoft.Extensions.*` abstractions". If the walk finds a non-abstractions `Microsoft.Extensions.*` assembly in a Server or Client signature, list it in the PR and ask the owner whether to narrow the prefix.
- The gRPC rows: spec 02 allows `Grpc.Net.Client` / `Grpc.AspNetCore` types "only on their own `Add...` options". `MapA2AGrpc` returns `GrpcServiceEndpointConventionBuilder` (from `Grpc.AspNetCore.Server`; Phase 1 `src/A2A.Server/Hosting/GrpcHostingExtensions.cs:43`). Treat the entry-point extension class as allowed and add a second assertion in the gRPC theory: any `Grpc.*` type in a gRPC package's public signature must be on a member of a static class whose name ends in `Extensions` or on a type whose name ends in `Options`. Write that check in the same file.
- `Ambinder.A2A.Integration.Tests` in `ExtraFriendAssemblies` is the owner's decision of 2026-11-03 (Task 3, step 3.1).

- [ ] **1.5** Write `PublicSignatures.cs` in the same project: the signature walker.

```csharp
using System.Reflection;

namespace Ambinder.A2A.PublicApi.Tests;

internal static class PublicSignatures
{
    public readonly record struct Use(string Member, Type Type);

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static bool IsVisible(Type t)
        => t.IsPublic || (t.IsNestedPublic && IsVisible(t.DeclaringType!)) || (t.IsNestedFamily && IsVisible(t.DeclaringType!)) || (t.IsNestedFamORAssem && IsVisible(t.DeclaringType!));

    private static bool IsVisible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    public static IEnumerable<Use> Walk(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(IsVisible))
        {
            var name = type.FullName!;
            if (type.BaseType is { } baseType) foreach (var u in Expand(name, baseType)) yield return u;
            foreach (var i in type.GetInterfaces()) foreach (var u in Expand(name, i)) yield return u;

            foreach (var m in type.GetMethods(Declared).Where(IsVisible))
            {
                foreach (var u in Expand($"{name}.{m.Name}", m.ReturnType)) yield return u;
                foreach (var p in m.GetParameters()) foreach (var u in Expand($"{name}.{m.Name}", p.ParameterType)) yield return u;
            }

            foreach (var c in type.GetConstructors(Declared).Where(IsVisible))
                foreach (var p in c.GetParameters()) foreach (var u in Expand($"{name}..ctor", p.ParameterType)) yield return u;

            foreach (var f in type.GetFields(Declared).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                foreach (var u in Expand($"{name}.{f.Name}", f.FieldType)) yield return u;

            // Properties and events are covered by their accessor methods above.
        }
    }

    private static IEnumerable<Use> Expand(string member, Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            foreach (var u in Expand(member, type.GetElementType()!)) yield return u;
            yield break;
        }

        if (type.IsGenericParameter)
        {
            foreach (var c in type.GetGenericParameterConstraints()) foreach (var u in Expand(member, c)) yield return u;
            yield break;
        }

        yield return new Use(member, type);
        if (type.IsGenericType)
            foreach (var arg in type.GetGenericArguments()) foreach (var u in Expand(member, arg)) yield return u;
    }
}
```

- [ ] **1.6** Add `ShippedAssembliesTests.All_shipped_projects_are_referenced`: for each name in `ShippedAssemblies.ProjectNames`, assert `File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll"))`, with the message "Add a ProjectReference to tests/Ambinder.A2A.PublicApi.Tests for {name}."

- [ ] **1.7** Run `dotnet test tests/Ambinder.A2A.PublicApi.Tests -c Release`. Expected: the allow-list row test and the reference test pass; `Named_plumbing_types_are_not_public` fails, listing the types from spec 02 section 3 that are still public; `No_forbidden_type_in_a_public_signature` fails for the gRPC packages if 01 left `ProtoMapper` public. Write the failure list into the PR notes; it is the to-do list for Tasks 2 to 4.

- [ ] **1.8** Keep every commit green: mark the failing theories `Skip = "Until SDK 02 Task 4 hides the internals"` and remove the skip in Task 4.4. Commit: `test(api): add public surface guard test`.

---

### Task 2: Protocol package: hide the JSON plumbing, add typed accessors (B12)

**Files:**
- Modify: `src/Ambinder.A2A.Protocol/Json/A2AJson.cs`
- Modify: `src/Ambinder.A2A.Protocol/Json/ProtoEnumConverter.cs`, `src/Ambinder.A2A.Protocol/Json/UtcTimestampConverter.cs`
- Modify: the `A2ACardResolver.cs` file (in `src/Ambinder.A2A.Client`, found by `git grep -l "A2AJsonContext" -- src`)
- Create: `tests/Ambinder.A2A.Protocol.Tests/Json/A2AJsonAccessorTests.cs`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/ConsumerJsonContextTests.cs`

- [ ] **2.1** Write `A2AJsonAccessorTests` first (red):
  - `Every_public_protocol_type_has_a_typed_accessor`: reflect over `typeof(A2AJson).Assembly`, take every public `sealed record` and public `enum` in namespace `Ambinder.A2A.Protocol`, plus every public type in `Ambinder.A2A.Protocol.Signing` that has a `[JsonSerializable]` line in `A2AJsonContext` (package 04 adds `JsonWebKeyPublic` and `JsonWebKeySet`, which the client and the card JWKS endpoint serialize), and assert `A2AJson` has a public static property of type `JsonTypeInfo<T>` for each. Exclude the error and constant types (`A2AException`, `A2AAuthException`, `A2AErrorCode`, `A2AErrorInfo`, `A2AError`, `A2AConstants`, `A2ATelemetryNames`); the exclusion list is in the test with a comment.
  - `Accessors_use_the_shared_options`: for each accessor, `Assert.Same(A2AJson.Options, accessor.Options)`.
  - `TypeInfoResolver_resolves_protocol_types`: `A2AJson.TypeInfoResolver.GetTypeInfo(typeof(AgentTask), new JsonSerializerOptions())` is not null.
  - `Card_read_is_unchanged`: deserialize a card JSON from the existing golden samples with `A2AJson.AgentCard` and compare with the result of `JsonSerializer.Deserialize(json, A2AJson.Options)` (record equality on the parsed card, and equal JSON when written back with `A2AJson.Options`).
- [ ] **2.2** In `A2AJson`:
  - Keep `public static JsonSerializerOptions Options { get; }`.
  - Add `public static IJsonTypeInfoResolver TypeInfoResolver { get; }`: the same resolver the options use (`JsonTypeInfoResolver.Combine(A2AJsonContext.Default).WithAddedModifier(OmitEmptyLists)`), built once and shared by `Create()`.
  - Add one `public static JsonTypeInfo<T> <TypeName> { get; }` per public protocol type, initialized from `Options`: `(JsonTypeInfo<AgentTask>)Options.GetTypeInfo(typeof(AgentTask))`. Name each property after the type (`A2AJson.AgentTask`, `A2AJson.Message`; spec leaves the exact names to the builder). Initialize lazily or in the static constructor after `Options`; static field order matters.
  - Add `[JsonSerializable]` lines to `A2AJsonContext` for any public protocol type the test finds missing (for example the security scheme and OAuth flow records that are not listed today). Run the golden JSON tests after this; they must not change.
  - XML doc on each accessor: one sentence, "Source-generated metadata for <see cref="AgentTask"/>, using <see cref="Options"/>."
- [ ] **2.3** Make `A2AJsonContext`, `ProtoEnumConverter<T>` and `UtcTimestampConverter` `internal`. The package 03 plan (Task 9) may already have made `ProtoEnumConverter<T>` internal; then leave it, but still run the step 2.5 test. Keep `[JsonConverter(typeof(ProtoEnumConverter<TaskState>))]` on the public enums.
- [ ] **2.4** Replace `A2AJsonContext.Default.AgentCard` in `A2ACardResolver` with `A2AJson.AgentCard`. Search for any other use outside the Protocol project: `git grep -n "A2AJsonContext" -- src tests samples`. Tests that use it move to the accessors.
- [ ] **2.5** Write `ConsumerJsonContextTests` in `tests/Ambinder.A2A.PublicApi.Tests` (this assembly has no `InternalsVisibleTo`, so it is a real consumer):

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>A consumer's own source-generated context that lists our models.</summary>
[JsonSerializable(typeof(AgentTask))]
[JsonSerializable(typeof(Message))]
internal sealed partial class ConsumerContext : JsonSerializerContext;

public sealed class ConsumerJsonContextTests
{
    [Fact]
    public void Consumer_context_combined_with_A2AJson_round_trips_enums_by_name()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(A2AJson.TypeInfoResolver, ConsumerContext.Default) };
        var task = new AgentTask { Id = "t1", ContextId = "c1", Status = new AgentTaskStatus { State = TaskState.Working } };
        var json = JsonSerializer.Serialize(task, options.GetTypeInfo(typeof(AgentTask)));
        Assert.Contains("TASK_STATE_WORKING", json);
        Assert.Equal(task.Status.State, ((AgentTask)JsonSerializer.Deserialize(json, options.GetTypeInfo(typeof(AgentTask)))!).Status.State);
    }

    [Fact]
    public void Consumer_context_alone_compiles_and_writes_enums_by_name()
    {
        var json = JsonSerializer.Serialize(new AgentTaskStatus { State = TaskState.Working }, ConsumerContext.Default.Options.GetTypeInfo(typeof(AgentTaskStatus)));
        Assert.Contains("TASK_STATE_WORKING", json);
    }
}
```

Adjust property names and the enum wire string to the actual models and to what the golden JSON tests show. Check: if `ConsumerContext` does not compile, or the second test writes a number instead of the name, the internal converter broke consumers. **Stop and ask the owner** (Review focus 1). Do not make the converter public on your own.

- [ ] **2.6** Build and test. Golden JSON tests unchanged. Commit: `feat(protocol): hide JSON plumbing, add A2AJson typed accessors`.
- [ ] **2.7** Records: `CHANGELOG.md` "Breaking changes" lines for `A2AJsonContext`, `ProtoEnumConverter<T>`, `UtcTimestampConverter` now internal, and "Added" for `A2AJson.TypeInfoResolver` and the accessors. Rows in `docs/migration/phase1-map.csv`, for example `A2A.Protocol.A2AJsonContext,Ambinder.A2A.Protocol.A2AJson.<Type>,Ambinder.A2A.Protocol,replaced by A2AJson typed accessors`.

---

### Task 3: Server, AspNetCore and AspNetCore.Grpc: hide internals

Each type below is found by name (`git grep -nE "public (sealed |static |partial )*(class|struct|record) <Name>\b" -- src`), because 01 may have moved files (A3).

**Files:** the files that declare these types, the `.csproj` files that need `InternalsVisibleTo`, test files that break.

| Type (spec 02 section 3) | Expected package after 01 | Change |
|---|---|---|
| `TaskManager` | Server | `internal` |
| `TaskEventBroker` | Server | `internal` |
| `TaskWriter.Releaser` | Server | `internal` (nested) |
| `PushNotificationSender` | Server | `internal` |
| `PushSigningKey` | Server | `internal` |
| `TracingRequestHandler` | Server | `internal` |
| `PageToken`, `TaskStateRules`, `RequestValidation` | `src/Shared` (linked) | `internal` if 01 did not already (A5) |
| `ServiceParameters`, `RestErrorWriter`, `SseWriter`, `AgentCardInterfaces`, `WebhookUrlValidator` | AspNetCore or Server | `internal` |
| `GrpcErrorMapper`, `A2AGrpcService` | AspNetCore.Grpc | `internal` |
| `ProtoMapper` (all partials), generated `Lf.A2a.V1.*` | `src/Grpc.Shared` | `internal` if 01 did not already (A4) |
| `InMemoryTaskStore`, `InMemoryPushConfigStore`, `DefaultOwnerResolver`, `AgentAuthorizer` | Server | `internal` (spec: default implementations become internal unless a user must construct them; users get them through `Add...`) |

Types that stay public in this task (owned by 05, 07, 08, 11; spec 02 section 3): `IAgentHandler`, `IAgentEventSink`, `AgentRequestContext`, `AgentRegistration`, `AgentCatalog`, `A2AServerBuilder`, `A2AServerOptions`, `PushOptions`, `ITaskStore`, `IPushConfigStore`, `StoredTask`, `IOwnerResolver`, `CallerIdentity`, `IAgentAuthorizer`, `ITaskEventObserver`, `IA2ARequestHandler`, `A2ACallContext`, and the extension classes.

- [ ] **3.1** **Owner decision (2026-11-03): `InternalsVisibleTo` to `Ambinder.A2A.Integration.Tests` is allowed.** Spec 02 (Constraints) allows `InternalsVisibleTo` "only to the matching `*.Tests` assembly and to `Ambinder.A2A.Benchmarks`". The owner added this one exception. Reason: Phase 1 already gives the client's internals to `A2A.Integration.Tests` (`src/A2A.Client/A2A.Client.csproj:12`), the integration tests use about a dozen of the types made internal here (see Facts), and the assembly is a test assembly that is never shipped, so the rule's purpose (no `InternalsVisibleTo` between shipped assemblies, spec 01 rule 6) still holds. Rewriting those tests against public API stays with packages 10 (client) and 12 (test migration, C13). `PublicSurfaceTests` lists it in `ExtraFriendAssemblies`. Add the `InternalsVisibleTo` item for it to each shipped project whose internals the integration tests use, and to no other assembly. Record the exception in `CONTRIBUTING.md` (Task 13.3) next to the `InternalsVisibleTo` rule.
- [ ] **3.2** Change the declarations in the table to `internal`. For a type in `src/Shared` or `src/Grpc.Shared`, first check (A4, A5) whether 01 already did it.
- [ ] **3.2a** **Types used by two shipped assemblies.** `ServiceParameters` and `RequestValidation` (in the table), and the already-internal `TransportRequest` and `TransportErrors`, are used by both the JSON-RPC/REST endpoints (`Ambinder.A2A.AspNetCore`) and the gRPC service (`Ambinder.A2A.AspNetCore.Grpc`). In Phase 1, `A2AGrpcService` and `GrpcErrorMapper` call them. Check how package 01 shares them. If they are linked files in `src/Shared/` compiled into each assembly, making them `internal` is safe. If 01 left them as public types in `Ambinder.A2A.AspNetCore`, move them to `src/Shared/` as linked files (spec 01 rule 10) and link them into both projects; then make them `internal`. Never keep them public and never add `InternalsVisibleTo` between the two packages. Apply the same check to any other type in the table that a second shipped assembly uses (`git grep -lw <Type> -- src`).
- [ ] **3.3** Add `InternalsVisibleTo` items only where a test needs them, using the item form in the `.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Ambinder.A2A.Server.Tests" />
  <InternalsVisibleTo Include="Ambinder.A2A.Integration.Tests" />
  <InternalsVisibleTo Include="Ambinder.A2A.Benchmarks" />
</ItemGroup>
```

Add the `Benchmarks` line only if a benchmarks project exists (package 14 adds it otherwise).
- [ ] **3.4** gRPC tests (A11). If `ProtoMapper` and `Lf.A2a.V1` types are now internal in two assemblies (`AspNetCore.Grpc`, `Client.Grpc`), a test that sees both through `InternalsVisibleTo` gets "type exists in both assemblies" errors (CS0433). Follow whatever 01 did (A11). If 01 did nothing because the types were still public: give `InternalsVisibleTo` from `Ambinder.A2A.AspNetCore.Grpc` to `Ambinder.A2A.AspNetCore.Grpc.Tests` and from `Ambinder.A2A.Client.Grpc` to `Ambinder.A2A.Client.Grpc.Tests`, move the proto round-trip and mapper tests into those projects (create them, same shape as other test projects), and in `Ambinder.A2A.Integration.Tests` reference the gRPC types through an `extern alias` on one of the two project references (`<ProjectReference ... Aliases="grpcserver" />` and `extern alias grpcserver;` in the files that use `Lf.A2a.V1`). The proto round-trip tests must stay byte-for-byte the same.
- [ ] **3.5** Fix public XML docs that `cref` a type that is now internal (for example the `AddA2AServer` summary names `TaskManager` and `TracingRequestHandler`). Rewrite the sentence in terms of what the caller gets (`IA2ARequestHandler`), not the internal class.
- [ ] **3.6** Build the whole solution, including `samples/` and `tests/A2A.Tck.Sut` (renamed by 01). Run all tests. They pass with only access-modifier and `using` changes.
- [ ] **3.7** Records: one `CHANGELOG.md` "Breaking changes" line per type made internal (group by package), and the matching rows in `docs/migration/phase1-map.csv` with `change` = `now internal` (or `replaced by <X>` when the spec names the replacement, for example `TaskEventBroker` → `replaced by ITaskEventBus (package 06)`, `PushSigningKey` → `replaced by IPushSigningKeySource (package 08)`).
- [ ] **3.8** Commit: `feat(server): hide server and transport internals (R6)`.

---

### Task 4: Client, Client.Grpc and Client.Entra: hide internals (R32 part)

**Files:** the files that declare the types below; `A2AClientFactory.cs`; the client test projects.

| Type | Change |
|---|---|
| `RestA2AClient`, `JsonRpcA2AClient` | `internal` |
| `GrpcA2AClient` | `internal` (in `Client.Grpc`) |
| `TracingA2AClient` (and its `Inner`) | `internal` |
| `SseReader` | `internal` |

Stay public (reviewed by 10 and 07): `IA2AClient`, `A2AClientOptions`, `A2ACardResolver`, `A2AClientFactory`, `IA2ACredentialProvider`, `ApiKeyCredentialProvider`, `BearerTokenCredentialProvider`, `ClientCredentialsProvider`, `AzureIdentityCredentialProvider` (in `Client.Entra`, A3).

- [ ] **4.1** Make the types internal. Add `InternalsVisibleTo` to `Ambinder.A2A.Client.Tests` (if it exists) and `Ambinder.A2A.Integration.Tests` (decision 3.1), and the same for `Client.Grpc`.
- [ ] **4.2** Rewrite the `A2AClientFactory.Create` XML doc so it does not name `TracingA2AClient` or `Inner`. Say what the caller gets: "an `IA2AClient` for the chosen interface; each call creates one client span."
- [ ] **4.3** If `A2AClientFactory` (in `Client`) creates `GrpcA2AClient` (now in `Client.Grpc`, per A3), 01 must already have solved how the factory reaches the gRPC transport. Do not add `InternalsVisibleTo` between `Client` and `Client.Grpc` (forbidden). If 01's solution needs a public type in `Client.Grpc` (for example a registration hook), leave it public and list it for the API review (Task 11).
- [ ] **4.4** Build and test. `PublicSurfaceTests` is now green except for anything Task 5 covers. If the Task 1 theories were skipped, remove the skips now.
- [ ] **4.5** Records: `CHANGELOG.md` lines and `docs/migration/phase1-map.csv` rows, for example `A2A.Client.Transports.RestA2AClient,,Ambinder.A2A.Client,replaced by A2AClientFactory / AddA2AClient (package 10)`.
- [ ] **4.6** Commit: `feat(client): hide transport clients (R32)`.

---

### Task 5: Namespaces: registration methods and day-one types (spec 02 section 5, Goal 9)

**Files:**
- Modify: the files that declare `A2AServiceCollectionExtensions`, `A2AEndpointRouteBuilderExtensions`, `GrpcHostingExtensions` (and any other `AddA2A...`/`MapA2A...` class 01 created). This includes both registration classes of A14: the one in `Ambinder.A2A.Server` with `AddA2AServerCore` and the one in `Ambinder.A2A.AspNetCore` with `AddA2AServer`. Both go in `namespace Microsoft.Extensions.DependencyInjection`. They must have different class names (for example `A2AServerCoreServiceCollectionExtensions` and `A2AServerServiceCollectionExtensions`), because two public static classes with the same full name in two assemblies would clash for a consumer who references both.
- Modify: the files that declare `IAgentHandler`, `IAgentEventSink`, `AgentRequestContext`
- Modify: `.editorconfig`
- Modify: every `using` that breaks in `src/`, `samples/`, `tests/`
- Create (only if A12 says they do not exist): `samples/GettingStarted.Agent/`, `samples/GettingStarted.Client/`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/GettingStartedTests.cs`

- [ ] **5.1** Put every static class whose methods are `AddA2A...` extensions on `IServiceCollection` in `namespace Microsoft.Extensions.DependencyInjection;`. Put every `MapA2A...` extension class on `IEndpointRouteBuilder` in `namespace Microsoft.AspNetCore.Builder;`. Other extension methods in those classes (for example `ConfigureA2AKestrel` on `WebApplicationBuilder`) move with their class; `WebApplicationBuilder` is in `Microsoft.AspNetCore.Builder` too. Do not move files between folders.
- [ ] **5.1a** `AddA2AServerCore` (A14) is public API of `Ambinder.A2A.Server`: it gets XML docs that say "Use this in a host without ASP.NET Core; web apps call `AddA2AServer` from `Ambinder.A2A.AspNetCore`, which calls this method." Its line goes in Server's `PublicAPI.Unshipped.txt` (Task 9). `AddA2AServer`'s XML docs say it calls `AddA2AServerCore`. Add a test in `tests/Ambinder.A2A.PublicApi.Tests` that `AddA2AServer` resolves every service `AddA2AServerCore` registers (same service types), so the two cannot drift.
- [ ] **5.2** Turn off IDE0130 for those files only, with a comment, in `.editorconfig`:

```ini
# Registration and mapping extensions live in the namespaces the web SDK imports implicitly (spec 02 section 5).
[src/**/{*ServiceCollectionExtensions,*EndpointRouteBuilderExtensions,*HostingExtensions}.cs]
dotnet_diagnostic.IDE0130.severity = none
```

Adjust the file-name globs to the actual file names.
- [ ] **5.3** Move `IAgentHandler`, `IAgentEventSink` and `AgentRequestContext` to `namespace Ambinder.A2A.Server;` (spec 02 section 5: "the types a developer meets on day one ... live in `Ambinder.A2A.Server`"). Leave all other namespaces as they are; their owner packages (05, 07, 08, 11) collapse them when they list their types. Fix the `using` lines that break.
- [ ] **5.4** Getting-started samples (A12). If 01 created them, use them. If not, create two minimal projects (package 13 replaces the content later):
  - `samples/GettingStarted.Agent`: `Microsoft.NET.Sdk.Web`, `ImplicitUsings` on, references `src/Ambinder.A2A.AspNetCore`. `Program.cs` registers one agent that echoes the text it gets, with `AddA2AServer` and `MapA2A`, at most 20 lines, with at most two `using Ambinder.A2A.*` lines.
  - `samples/GettingStarted.Client`: console, `ImplicitUsings` on, references `src/Ambinder.A2A.Client`. `Program.cs` resolves the card and sends one message, with the call itself in about 5 lines, with at most two `using Ambinder.A2A.*` lines.
  - Both: `IsPackable=false`, added to `Ambinder.A2A.slnx`.
- [ ] **5.5** Write `GettingStartedTests` (in `tests/Ambinder.A2A.PublicApi.Tests`): for each of the two `Program.cs` files, count lines that start with `using Ambinder.A2A` and assert `<= 2`. The samples compile as part of the solution build, which is the other half of the check (spec Goal 9). If 01's samples use `global using` in another file, count those too.
- [ ] **5.6** Build and test. Commit: `feat(api): conventional namespaces for AddA2A/MapA2A and day-one server types`.
- [ ] **5.7** Records: `CHANGELOG.md` and `docs/migration/phase1-map.csv` rows with `change` = `moved` for each moved type and extension class.

---

### Task 6: Secret-holding records (spec 02 section 4)

**Files:**
- Modify: the file that declares `AuthenticationInfo` and `TaskPushNotificationConfig` (Phase 1: `src/A2A.Protocol/Models/PushNotification.cs`)
- Create: `tests/Ambinder.A2A.Protocol.Tests/Models/SecretRecordTests.cs`

- [ ] **6.1** Write the tests first (red):

```csharp
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Protocol.Tests.Models;

public sealed class SecretRecordTests
{
    private const string Secret = "s3cr3t-value";

    [Fact]
    public void AuthenticationInfo_ToString_hides_credentials()
    {
        var text = new AuthenticationInfo { Scheme = "Bearer", Credentials = Secret }.ToString();
        Assert.DoesNotContain(Secret, text);
        Assert.Contains("Bearer", text);
    }

    [Fact]
    public void TaskPushNotificationConfig_ToString_hides_token_and_credentials()
    {
        var config = new TaskPushNotificationConfig
        {
            Url = "https://example.test/hook",
            Token = Secret,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = Secret + "-2" },
        };
        var text = config.ToString();
        Assert.DoesNotContain(Secret, text);
    }
}
```

- [ ] **6.2** Override `PrintMembers` in both records. Print secret members as `***` when set and `null` when not set, so a log still shows whether a credential is present. Example for `AuthenticationInfo`:

```csharp
/// <summary>Writes the members for <see cref="object.ToString"/>. <see cref="Credentials"/> is never written.</summary>
/// <param name="builder">The builder to write to.</param>
/// <returns><see langword="true"/> when members were written.</returns>
private bool PrintMembers(System.Text.StringBuilder builder)
{
    builder.Append("Scheme = ").Append(Scheme).Append(", Credentials = ").Append(Credentials is null ? "null" : "***");
    return true;
}
```

For a `sealed record`, `PrintMembers` must be `private`. In `TaskPushNotificationConfig`, print `Authentication` through its own `ToString()` (which hides the credential) and `Token` as `***`. Keep the URL as is: the URL is not a credential by the spec's list. If the URL can carry a query secret, that is package 08's call; leave a comment pointing to 08.
- [ ] **6.3** Equality is not changed (records compare all members; `PrintMembers` only affects `ToString`). Build and test. Commit: `fix(protocol): never print push credentials in ToString`.

---

### Task 7: Analyzer level, code style and logging rules (spec 02 section 7)

**Files:**
- Modify: `src/Directory.Build.props`
- Modify: `.editorconfig`
- Modify: the source files that the new rules flag

- [ ] **7.1** In `src/Directory.Build.props` add:

```xml
<PropertyGroup>
  <AnalysisLevel>latest</AnalysisLevel>
  <AnalysisMode>Recommended</AnalysisMode>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

These apply to `src/` only (spec), not to tests or samples.
- [ ] **7.2** Replace `.editorconfig` with the current whitespace rules (keep every line from the Phase 1 file, `.editorconfig:1-15`) plus a `[src/**/*.cs]` section:

```ini
[src/**/*.cs]
# Spec 02 section 7: errors in shipped code.
dotnet_diagnostic.IDE0005.severity = error   # unused usings
csharp_style_namespace_declarations = file_scoped:error
dotnet_diagnostic.IDE0161.severity = error   # file-scoped namespaces
dotnet_style_readonly_field = true:error
dotnet_diagnostic.IDE0044.severity = error   # readonly fields
dotnet_diagnostic.CA2007.severity = error    # ConfigureAwait(false) on every await
dotnet_diagnostic.CA1848.severity = error    # use [LoggerMessage]
dotnet_diagnostic.CA2254.severity = error    # constant log templates

# Exceptions. Each line says why.
# (add one line per rule you turn down, with the reason)
```

IDE0005 in the build needs `GenerateDocumentationFile=true` (already on, A6). Do not add `var` rules (spec).
- [ ] **7.3** Build. Fix every new diagnostic in `src/`:
  - CA2007: add `.ConfigureAwait(false)` (including `await foreach (... .ConfigureAwait(false))` and `await using (... .ConfigureAwait(false))`). The Facts line counts suggest only a few sites; the analyzer lists them all.
  - CA1848 / CA2254: convert the 14 log calls (see Facts) to `[LoggerMessage]` partial methods in a `static partial class Log` per file or per area, `internal`. **EventIds are API** (spec 02 section 1, item 7). First check spec 14 and `docs/` for an EventId scheme; if none exists, use one block per assembly: `Ambinder.A2A.Server` 1000–1999, `Ambinder.A2A.AspNetCore` 2000–2999, `Ambinder.A2A.AspNetCore.Grpc` 3000–3999, `Ambinder.A2A.Client` 4000–4999, with event names equal to the method names. Keep log levels and message text the same. Write the scheme into `docs/policies/versioning.md` (Task 10) so later packages follow it.
  - Other `Recommended` rules: fix them. When a fix would change a public name or signature (for example CA1716, CA1711, CA1720), do **not** change it. Suppress it in `.editorconfig` under "Exceptions" with the reason "public name, decided in the API review", and add the rule and member to the API review checklist (Task 11).
- [ ] **7.4** Build and test. Commit: `build: analyzer level latest/Recommended, ConfigureAwait and LoggerMessage rules`.

---

### Task 8: Banned-API list (spec 02 section 7)

**Files:**
- Create: `eng/BannedSymbols.txt`
- Modify: `Directory.Packages.props`, `src/Directory.Build.props`
- Modify: the source files the analyzer flags

- [ ] **8.1** Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` to `Directory.Packages.props` (newest stable). In `src/Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)../eng/BannedSymbols.txt" Link="BannedSymbols.txt" />
</ItemGroup>
```

- [ ] **8.2** Write `eng/BannedSymbols.txt` with the general bans from spec 02 section 7. Each line has a message that says what to use:

```text
# General bans (package 02). Performance bans are added by package 14 in this file.
P:System.DateTime.Now;Use TimeProvider (inject it; default TimeProvider.System).
P:System.DateTime.UtcNow;Use TimeProvider (inject it; default TimeProvider.System).
P:System.DateTimeOffset.Now;Use TimeProvider (inject it; default TimeProvider.System).
P:System.DateTimeOffset.UtcNow;Use TimeProvider (inject it; default TimeProvider.System).
M:System.Threading.Thread.Sleep(System.Int32);Do not block. Use await Task.Delay with a TimeProvider.
M:System.Threading.Thread.Sleep(System.TimeSpan);Do not block. Use await Task.Delay with a TimeProvider.
M:System.Threading.Tasks.Task.Wait;Do not block on async code. Await it.
P:System.Threading.Tasks.Task`1.Result;Do not block on async code. Await it.
M:System.Runtime.CompilerServices.TaskAwaiter.GetResult;Do not block on async code. Await it.
M:System.Runtime.CompilerServices.TaskAwaiter`1.GetResult;Do not block on async code. Await it.
M:System.Net.Http.HttpClient.#ctor;Use IHttpClientFactory.
M:System.Net.Http.HttpClient.#ctor(System.Net.Http.HttpMessageHandler);Use IHttpClientFactory.
M:System.Net.Http.HttpClient.#ctor(System.Net.Http.HttpMessageHandler,System.Boolean);Use IHttpClientFactory.
T:System.Console;Do not write to the console from a library. Use ILogger.
```

Add every `Task.Wait` overload the analyzer needs (check the doc-ID form with one deliberate violation). `TaskAwaiter.GetResult` bans the explicit `.GetAwaiter().GetResult()` call only; compiler-generated `await` code is not flagged.
- [ ] **8.3** Build. Fix the violations (see Facts):
  - The three `new HttpClient(` sites in the client. Spec: "the client package owns a single documented exception". Route all three through one `internal static` helper in the client (for example in `ClientSupport`) that holds the only `#pragma warning disable RS0030` in `src/`, with a comment: "Single documented exception (spec 02 section 7). Package 10 replaces it with IHttpClientFactory." No behavior change.
  - `A2AStartupValidation` `GetAwaiter().GetResult()`: an `IStartupFilter` is synchronous. Keep the call, add `#pragma warning disable RS0030` around that one line with a comment ("synchronous startup check; the default policy provider completes synchronously; package 09 moves startup checks"), and add it to the API review checklist. Do not change when the check runs.
- [ ] **8.4** Prove the ban works: add a temporary `DateTime.UtcNow` in `src/Ambinder.A2A.Server`, build, see RS0030, remove it. Note the result in the PR.
- [ ] **8.5** Build and test. Commit: `build: banned-API analyzer with general bans`.

---

### Task 9: Public API files (spec 02 section 2)

Comes after the hide-internals pass (Tasks 2 to 5), so the first files are small (spec: "The hide-internals pass runs *before* the API files are first generated").

**Files:**
- Modify: `Directory.Packages.props`, `src/Directory.Build.props`, `src/Directory.Build.targets` (create if A6 says it does not exist), `.editorconfig`
- Create: `src/<each shipped project>/PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/ApiFileTests.cs`

- [ ] **9.1** Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` to `Directory.Packages.props` (newest stable). In `src/Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
</ItemGroup>
```

- [ ] **9.2** In `src/Directory.Build.targets`, add the files as `AdditionalFiles` and create them when missing:

```xml
<Project>
  <ItemGroup>
    <AdditionalFiles Include="$(MSBuildProjectDirectory)/PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="$(MSBuildProjectDirectory)/PublicAPI.Unshipped.txt" />
  </ItemGroup>

  <!-- Spec 02 section 2: create the API files when missing. Each file starts with #nullable enable. -->
  <Target Name="A2AEnsurePublicApiFiles" BeforeTargets="BeforeBuild;CoreCompile">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/PublicAPI.Shipped.txt" Lines="#nullable enable" Condition="!Exists('$(MSBuildProjectDirectory)/PublicAPI.Shipped.txt')" />
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/PublicAPI.Unshipped.txt" Lines="#nullable enable" Condition="!Exists('$(MSBuildProjectDirectory)/PublicAPI.Unshipped.txt')" />
  </Target>
</Project>
```

If 01's `src/Directory.Build.targets` exists, add to it; if a root `Directory.Build.targets` exists, make sure the `src/` one imports it (`<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" Condition="..." />`).
- [ ] **9.3** In `.editorconfig`, `[src/**/*.cs]` section, set the diagnostics from spec 02 section 2 to error:

```ini
dotnet_diagnostic.RS0016.severity = error   # public API not declared
dotnet_diagnostic.RS0017.severity = error   # declared API not found
dotnet_diagnostic.RS0022.severity = error
dotnet_diagnostic.RS0025.severity = error
dotnet_diagnostic.RS0026.severity = error   # no multiple public overloads with optional parameters
dotnet_diagnostic.RS0036.severity = error   # nullability annotations in the API files
dotnet_diagnostic.RS0037.severity = error
```

- [ ] **9.4** Build once. Expected: RS0016 errors for every public member. Fill the `Unshipped` files with the analyzer's fix. Spec 02 leaves the command to the builder. Try, from the repo root:

```bash
dotnet format analyzers Ambinder.A2A.slnx --diagnostics RS0016 RS0037 --severity info
```

If that does not write the files, stop and report the output. Do not write API lines by hand, and do not use a script that copies the diagnostic messages; the line format must be the analyzer's.
- [ ] **9.5** Build again: zero RS diagnostics. **Read every line** of every `PublicAPI.Unshipped.txt` (Review focus 5). Compare the list of types with spec 02 section 3 ("public" rows plus the types other packages already added). Any type that should not be public goes back to Task 3 or 4. Write the per-package type counts into the PR.
- [ ] **9.6** `PublicAPI.Shipped.txt` stays `#nullable enable` only (preview rule, spec 02 section 2).
- [ ] **9.7** Write `ApiFileTests` in `tests/Ambinder.A2A.PublicApi.Tests`:
  - `Every_shipped_project_has_both_api_files`: for each `ShippedAssemblies.ProjectNames`, both files exist in `src/<name>/` and their first line is `#nullable enable`.
  - `Shipped_files_are_empty_during_previews`: every `PublicAPI.Shipped.txt` has no line other than `#nullable enable` and blank lines. The message says: "During 1.0.0-preview.N all API lines stay in PublicAPI.Unshipped.txt (spec 02 section 2). This test is removed at 1.0.0-rc.1."
- [ ] **9.8** Prove the gate (acceptance criterion 1). On a scratch branch, not committed: (a) delete one line from one `PublicAPI.Unshipped.txt` and build: RS0016 error; (b) add an undeclared public method and build: RS0016 error; (c) change a declared signature and build: RS0016 and RS0017 errors. Paste the three error lines into the PR.
- [ ] **9.9** Commit: `build: track public API with PublicApiAnalyzers (R4)`.

---

### Task 10: XML documentation (spec 02 section 6, R5, R36)

The largest task. Split it across subagents by package (10a Protocol, 10b Server and AspNetCore, 10c AspNetCore.Grpc and the three client packages). Each subagent gets the rules below and only its package's files.

**Files:** every `.cs` file in `src/` with a public or protected member; `src/Directory.Build.props` (or wherever 01 put the CS1591 suppression, A6).

**Rules (from spec 02 section 6), for every public and protected type and member:**
- `<summary>`: what it is or does, in one or two plain sentences. Never just the name again ("Gets or sets the name" is not allowed).
- `<param>` for every parameter, `<typeparam>` for every type parameter, `<returns>` for every non-void return (including `Task<T>`, `ValueTask<T>`, `Task` and `ValueTask`). Say the valid range or allowed values where there is a limit.
- `<exception>` for every exception a caller should expect: `A2AException` with the `A2AErrorCode`, and the argument exceptions the method throws.
- `<remarks>` on every implementer-facing interface: threading, lifetime, ordering, and what an implementer must guarantee. Required texts:
  - `IAgentHandler`: a new instance is created per execution and per cancel; `HttpContext` is not available (R36).
  - `ITaskStore`: the concurrency and ordering contract **as the code implements it today**, in a few sentences, with "Package 05 replaces this contract text." in the remarks. Package 05 owns the final text.
  - `IPushConfigStore`, `IOwnerResolver`, `IAgentAuthorizer`, `ITaskEventObserver`, `IA2ACredentialProvider`, `IA2ARequestHandler`: the lifetime they are registered with and whether calls can be concurrent.
- `<example>` on the entry points: `AddA2AServer`, `AddA2AServerCore` (a console or worker host, A14), `A2AServerBuilder.AddAgent`, `MapA2A`, `IAgentHandler`, `IA2AClient.SendMessageAsync`, `IA2AClient.SendStreamingMessageAsync`. (`AddA2AClient` gets one when package 10 adds it.) Each example is short, in a `<code>` block, and uses only public API. The code should compile when pasted into the getting-started sample; package 13's docs build checks it later.
- Options properties: state the default and the unit in the `<summary>` or a `<value>`.
- Every public enum: add the line "More values can be added in later versions; handle unknown values." in `<remarks>`. Do not rely on numeric values.
- `Add...` methods: say which services they register, their lifetimes, and which are `TryAdd` (replaceable) (spec 02 section 1, item 5).
- Implementations of documented members use `<inheritdoc/>`.
- Text style: short, plain sentences.

- [ ] **10.1** Remove the CS1591 suppression that 01 added (A6). Keep `GenerateDocumentationFile=true`. Build: CS1591 errors list every undocumented member.
- [ ] **10a / 10b / 10c** For the subagent's packages: write the docs until the build has no CS1591 and no CS1570–CS1574, CS1584, CS1587 (malformed docs; they are errors through `TreatWarningsAsErrors`). Do not change code, only comments. Build and run all tests after each package. Commit per package: `docs(<package>): complete XML docs`.
- [ ] **10.2** Check that the `.xml` file is in each `.nupkg`: `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages` and list each package (`unzip -l <file>.nupkg | grep "lib/net10.0/.*\.xml"`). If 01's pack job already checks this, note that and skip the manual check.

---

### Task 11: XML doc lint test and the API review checklist

**Files:**
- Create: `tests/Ambinder.A2A.PublicApi.Tests/DocIds.cs`
- Create: `tests/Ambinder.A2A.PublicApi.Tests/XmlDocQualityTests.cs`
- Create: `docs/policies/api-review-checklist.md`

- [ ] **11.1** Write `DocIds.cs`: maps a reflected member to its XML documentation ID (`T:`, `M:`, `P:`, `F:`, `E:`). Rules: nested types use `.`; generic types use `` `N ``; generic method type parameters in a signature are ``` ``N ```, type parameters of the declaring type are `` `N ``; constructors are `#ctor`; by-ref parameters end in `@`; arrays are `[]`; generic arguments in parameter types are `{...}`; no parentheses for a method with no parameters. Add a self-test with three known IDs, taken from the generated `.xml` of `Ambinder.A2A.Protocol` (for example a constructor, a generic method, a method with a `CancellationToken`).
- [ ] **11.2** Write `XmlDocQualityTests`. For each shipped assembly, load `<name>.xml` from `AppContext.BaseDirectory` (project references copy it there), and fail with a list of `member: problem` lines on:
  1. a `<summary>` that is missing or empty (whitespace only);
  2. a `<summary>` equal to the member name split into words, ignoring case and a leading "Gets or sets the" / "Gets the" (for example `MaxRequestBodyBytes` → "max request body bytes");
  3. a public method or constructor without a `<param>` for each parameter name (taken from reflection, matched by `DocIds`), or a `<param>` whose name does not exist;
  4. a non-void method without `<returns>`;
  5. a generic type or method without a `<typeparam>` for each type parameter;
  6. an `<inheritdoc/>` without `cref` on a member that neither overrides nor implements an interface member (nothing to inherit from);
  7. any `cref="!:` (a reference the compiler could not resolve);
  8. a missing `<example>` on the entry points listed in Task 10 (by doc ID; skip names that do not exist yet, such as `AddA2AClient`, with the list of skipped names in the test output).
  Members with `<inheritdoc/>` skip checks 1 to 5.
- [ ] **11.3** Run it. Fix the docs it flags (in the source comments). Build and test.
- [ ] **11.4** Write `docs/policies/api-review-checklist.md`, used by the review before `rc.1` (spec 02 section 9). Content: the review items from spec 02 section 9 (names, sealing, nullability, async shape, options, namespaces, XML docs, the forbidden-type test) and the evolution rules from section 4 as checkboxes, plus the open items found by this plan:
  - positional public records: `StoredTask`, `A2ACallContext`, `AgentRegistration`, `AgentRequestContext`, `A2AErrorInfo` (owner specs convert them; the spec names only the first two);
  - `A2AAuthException` is not sealed and does not derive from `A2AException`;
  - the `#pragma warning disable RS0030` sites from Task 8;
  - analyzer suppressions for public names from Task 7;
  - any public type 01 needed in `Client.Grpc` (Task 4.3);
  - the `Ambinder.A2A.Integration.Tests` `InternalsVisibleTo` exception (owner decision 2026-11-03, Task 3.1): check it is still needed once packages 10 and 12 have reworked those tests;
  - no new static singletons in public API (spec 02, "How it meets the six principles", item 6);
  - names that spec 03 leaves to package 02 and that the package 03 plan chose: `A2AAgentRoute` (a property record with `Name` and `Card`) and `A2AException.Details` (next to the existing `A2AException.Data`). Decide whether both `Data` and `Details` stay public;
  - the server signer seam from the package 04 plan (decision P18, owner decision 2026-11-03): `IAgentCardSigner` and `SignedAgentCard` in `Ambinder.A2A.Server.Signing`.
- [ ] **11.5** Commit: `test(api): XML doc quality lint and API review checklist`.

---

### Task 12: Options validation messages say what to do (principle 3 check)

Spec 02 "How it meets the six principles", item 3: each options validation failure message contains a "set" or "add" instruction.

**Files:**
- Modify: the file with the `.Validate(...)` messages and any `IValidateOptions<T>` in `src/`. After 01 (A14), the options validation belongs in `AddA2AServerCore` in `Ambinder.A2A.Server`, so it runs for hosts with and without ASP.NET Core; move it there if 01 left it in `AddA2AServer`, and test it through `AddA2AServerCore`.
- Create: `tests/Ambinder.A2A.Server.Tests/Hosting/OptionsValidationMessageTests.cs` (or the project 01 put hosting tests in)

- [ ] **12.1** Test first: for each validation in `src/` (Phase 1 has two in `AddA2AServer`: `PublicBaseUrl`, `MaxRequestBodyBytes`), build a service provider with a bad value, resolve the options, catch `OptionsValidationException`, and assert the message names the property, contains the bad value, and contains `Set ` or `Add `.
- [ ] **12.2** Rewrite the messages, for example: "A2AServerOptions.PublicBaseUrl is 'ftp://x', which is not an absolute http(s) URL. Set A2A:PublicBaseUrl to the URL clients use to reach this server, for example https://agents.contoso.com." Use `.Validate(Func<T,bool>, string)` only where the message can be static; otherwise implement `IValidateOptions<A2AServerOptions>` (`internal sealed`) so the bad value can be in the message.
- [ ] **12.3** Build and test. Commit: `fix(server): options validation messages say how to fix the value`.

---

### Task 13: Versioning policy page and contributor docs (spec 02 sections 1, 4, 8)

**Files:**
- Create: `docs/policies/versioning.md`
- Modify: `README.md`, `CONTRIBUTING.md`, `CHANGELOG.md`

- [ ] **13.1** Write `docs/policies/versioning.md` from spec 02. Sections, in order:
  1. Numbers, patch, minor, major, previews, release candidates: section 8 bullets as written.
  2. What is breaking and what is not: section 8 bullets.
  3. What counts as API: section 1, items 1 to 7, and the "Not API" line.
  4. Interfaces, classes, records, enums, methods, options, dependencies in the API: section 4 rules, short.
  5. Experimental API: section 4 text, plus an empty table `| Id | Feature | Since | Planned exit |` starting at `A2AEXP001`.
  6. Deprecation: section 4 text, plus a table `| Id | Obsolete API | Replacement | Since | Removal |`. If package 03's Task 16 has landed, the table starts with `A2AOBS001` (`ImplicitOAuthFlow`, `OAuthFlows.Implicit`) and `A2AOBS002` (`PasswordOAuthFlow`, `OAuthFlows.Password`), both "none: protocol field, kept while the A2A spec has it". Otherwise it starts empty at `A2AOBS001`, and package 03 adds those rows.
  7. Spec relation: section 8 bullet.
  8. Support: section 8 bullet (C2).
  9. Log EventId blocks per assembly (from Task 7).
  10. Timeline to 1.0: section 9 table.
- [ ] **13.2** Link it from `README.md` (one line near the install section) and `CONTRIBUTING.md`.
- [ ] **13.3** In `CONTRIBUTING.md`, add or complete the "Public API" section (01 created the file and may have a stub, A9):
  - the API-file workflow: write the code, build (it fails), run the command from Task 9.4, read the diff, commit; never regenerate a file wholesale;
  - previews keep every line in `Unshipped`; a removal or change during previews needs a "Breaking changes" line in `CHANGELOG.md`;
  - the evolution rules in one short list, with a link to the policy page;
  - XML doc rules (Task 10), the banned-API list (`eng/BannedSymbols.txt`), and the EventId blocks.
  - `InternalsVisibleTo`: only to the matching `*.Tests` assembly, `Ambinder.A2A.Benchmarks`, and `Ambinder.A2A.Integration.Tests` (owner decision 2026-11-03); never between shipped assemblies.
- [ ] **13.4** Check `CHANGELOG.md`: the unreleased/preview section lists every "Breaking changes" line from Tasks 2 to 6 and "Added" lines for `A2AJson.TypeInfoResolver`, the typed accessors, and the API tracking.
- [ ] **13.5** If 01 has a docs test (Phase 1 had `tests/A2A.Integration.Tests/Repo/DocsTests.cs`), extend it: the README and CONTRIBUTING link to `docs/policies/versioning.md`, and the link resolves.
- [ ] **13.6** Commit: `docs: versioning policy and public API contributor guide`.

---

### Task 14: CI: public API diff check and review routing (spec 02 section 2, O2)

**Files:**
- Modify: `.github/workflows/ci.yml`
- Check (01 creates them, A9): `.github/CODEOWNERS`, `.github/pull_request_template.md`

- [ ] **14.1** Add a job to `ci.yml`. It runs on every pull request (a required check must always report), and writes the diff of all `PublicAPI.*.txt` files to the job summary:

```yaml
  public-api-diff:
    if: github.event_name == 'pull_request'
    runs-on: ubuntu-latest
    timeout-minutes: 5
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - name: Public API diff
        env:
          BASE_SHA: ${{ github.event.pull_request.base.sha }}
        run: |
          set -euo pipefail
          diff=$(git diff "$BASE_SHA"...HEAD -- '*PublicAPI.Shipped.txt' '*PublicAPI.Unshipped.txt')
          {
            echo "## Public API diff"
            if [ -z "$diff" ]; then
              echo "No public API change."
            else
              echo '```diff'
              echo "$diff"
              echo '```'
            fi
          } >> "$GITHUB_STEP_SUMMARY"
```

Match the `actions/checkout` version to the one in `ci.yml` at execution time (Phase 1 uses `actions/checkout@v7`). The job shows the diff; the build job is what fails on undeclared API. Making this job a required check is owner action 7 in spec 01 (branch protection); list it in the PR as an owner action.
- [ ] **14.2** Check `CODEOWNERS` has a line routing `PublicAPI.*.txt` to the API owner (for example `**/PublicAPI.*.txt @roman-ambinder`; use the owner handle 01 used). Check the PR template has the "API files reviewed" checkbox. Add either one only if 01 left it out.
- [ ] **14.3** Make sure the new test project runs in the existing build-and-test job (it does if it is in `Ambinder.A2A.slnx`).
- [ ] **14.4** Commit: `ci: public API diff in the job summary`.

---

### Task 15: Final check against the acceptance criteria

- [ ] **15.1** Clean build and full test run: `git clean -xdf -e .vs -e .idea && dotnet build Ambinder.A2A.slnx -c Release && dotnet test Ambinder.A2A.slnx -c Release`. Zero warnings. Test count is at least the Task 0 count plus the new tests.
- [ ] **15.2** Go through the M1 rows of the coverage table below. For each, write in the PR description where it is proven (test name, command output or file).
- [ ] **15.3** The PR description has: "Package 01 facts" (Task 0), "Decisions for the owner" (any stop-and-ask items; Task 3.1 is already decided), the API gate proof (Task 9.8), the per-package public type counts (Task 9.5), and the owner actions (making `public-api-diff` a required check).

---

## Acceptance criteria coverage

Each acceptance criterion of spec 02 and where this plan meets it.

| Spec 02 acceptance criterion | M1 or later | Task |
|---|---|---|
| Each shipped project has both API files; removing a line fails the build; adding an undeclared member fails the build | M1 | 9.2, 9.7, 9.8 |
| No type in the "internal" rows is public; `Lf.A2a.V1` types not public | M1 | 2, 3, 4; `PublicSurfaceTests` (1.4) |
| `PublicSurfaceTests` passes (no forbidden dependency, no `Internal` namespace, none of the named types public) | M1 | 1, 2–5 |
| `GenerateDocumentationFile` on; zero CS1591; `XmlDocQualityTests` passes; each `.nupkg` contains its `.xml` | M1 | 10, 11, 10.2 |
| `XmlDocQualityTests` checks the `<example>` entry points | M1 | 11.2 (check 8) |
| Analyzer set (Recommended, banned symbols, CA2007, CA1848, CA2254) on for `src/`; clean build | M1 | 7, 8 |
| Secret-holding record test for `AuthenticationInfo` and `TaskPushNotificationConfig` | M1 | 6 |
| `AddA2A...` / `MapA2A...` in conventional namespaces; hello agent compiles with no extra `using` for them | M1 | 5 |
| `docs/policies/versioning.md` exists, matches section 8, linked from README and CONTRIBUTING | M1 | 13 |
| `CODEOWNERS` routes `PublicAPI.*.txt`; a required CI check shows the public API diff | M1 (required flag: owner action) | 14 |
| `A2AJson` has a typed accessor for every public protocol type; a test checks none is missing | M1 | 2 |
| Before `rc.1`: API review list closed; `Unshipped` merged into `Shipped` | later | D1, D2 |
| At `1.0.0`: `PackageValidationBaselineVersion=1.0.0`; a PR that removes a public member fails the pack job | later | D3, D4 |

Goals 1 to 10 of spec 02 map to the same tasks: Goal 1 → 9; Goal 2 → 2–4; Goal 3 → 1; Goal 4 → 10, 11; Goal 5 → 10.2; Goal 6 → 13; Goal 7 → 9.6, 9.7, 13 (previews), D2–D3 (freeze); Goal 8 → 13 (rules written), 11.4 (review); Goal 9 → 5; Goal 10 → 7.

## Deferred tasks (not executed in M1)

These are spec 02 steps for later milestones. They are listed so the plan covers the whole spec. Do not run them with this plan; each needs the named trigger.

- **D1. API review before `1.0.0-rc.1`** (trigger: all M2 packages have landed). One pass by the builder over every package using `docs/policies/api-review-checklist.md` and the API files. Output: a list of fixes, all merged before `rc.1`.
- **D2. Freeze at `1.0.0-rc.1`** (trigger: D1 closed). In every shipped project, move all lines from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt` (leaving `Unshipped` as `#nullable enable`). Delete `ApiFileTests.Shipped_files_are_empty_during_previews`. Update `CONTRIBUTING.md`: removals now need a `*REMOVED*` line and the deprecation policy.
- **D3. Baseline at `1.0.0`** (trigger: `1.0.0` published). Set `PackageValidationBaselineVersion` to `1.0.0` in `src/Directory.Build.props`. After each later stable release, update it to the previous stable version.
- **D4. Prove the gate** (trigger: D3). Open a throwaway PR that removes one public member and show that the pack job fails on package validation. Close it.
- **D5. Publish the policy page** (trigger: package 13's docs site). Package 13 publishes `docs/policies/versioning.md`.

---

## Execution notes

- **How to execute:** use the superpowers:subagent-driven-development skill. One fresh implementer subagent per task, then a review step for that task before the next one starts.
- **Model:** every subagent (implementers and reviewers) runs on model **`sonnet`**. Planning was done on Opus; execution is on Sonnet (owner rule).
- **Order:** Task 0 first, alone. Then Tasks 1 → 2 → 3 → 4 → 5 in order (each depends on the one before). Tasks 6, 7 and 8 come next, in that order; they touch different files but all change the build, so run them one at a time. Task 9 must come after Tasks 2 to 8 (the API files are generated once, after the surface and analyzer fixes are done). Task 10 comes after 9; its three parts (10a, 10b, 10c) can run as parallel subagents because they touch different packages, followed by one build of the whole solution. Then 11, 12, 13, 14, 15 in order.
- **What each subagent gets:** this plan's header (Precondition table, Facts, Global constraints, Review focus), its own task section, and the "Package 01 facts" notes from Task 0. Point it at spec 02 for the rule text; do not paste the whole spec.
- **Stop points that need the owner:** Task 0.4 (an assumption about 01 is false), Task 2.5 (an internal converter breaks a consumer context), Task 9.4 (no command fills the API files). A subagent that hits one stops and reports; it does not work around it.
- **Review of each task:** the reviewer checks the Review focus items that apply, that the build has zero warnings, that all tests pass, that no wire-format test changed, and that the CHANGELOG and migration-map records were added.
- **Deferred tasks D1 to D5 are not part of this execution.**
