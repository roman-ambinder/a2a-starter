# SDK 01 Naming, Packaging and Release: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task by task. Every subagent runs on model `sonnet` (see "Execution notes"). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Phase 1 reference repo into a set of `Ambinder.A2A.*` NuGet packages that can be installed, debugged and released:

- rename every project, namespace and assembly to `Ambinder.A2A.*`
- split the server and the client into the packages in 01's package map
- add NuGet metadata, license and community files, SourceLink and deterministic builds
- add CI gates: pack, dependency allow-list, samples on packages, coexistence, reproducibility, Windows leg, NuGet audit levels
- add a tag-driven release pipeline that tests, signs (stable only), attests, publishes after approval and smoke-tests

**Architecture:**

- Five PRs, in this order. Each one builds and passes all tests on its own:
  - **A rename:** mechanical, no behavior change.
  - **B split:** the package split, plus the small seams the split forces.
  - **C packaging:** props layout, metadata, files, MinVer, AOT flags, getting-started samples.
  - **D CI gates.**
  - **E release pipeline.**
- PRs A, B and C (and the allow-list from D) are what every other package plan assumes. So they come first.

**Tech stack:**

- .NET 10 (`global.json` pins SDK 10.0.401 with `rollForward: latestFeature`)
- MSBuild central package management
- MinVer
- GitHub Actions
- `sourcelink` (dotnet tool)
- Microsoft `sign` CLI with Azure Trusted Signing (stable releases only)
- `actions/attest-build-provenance`
- an SBOM tool (builder's choice)
- xUnit v3 on Microsoft.Testing.Platform

**Spec:** `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md` (approved 2026-11-03). The binding work plan is `2026-11-03-sdk-00-work-plan.md` (decisions O1 to O5, C1, B12).

---

## What the other plans expect from 01

Fifteen package plans (02 to 16) are already on `master`. Each assumes 01's output, and each stops in its Task 0 if that output is missing. This plan delivers every item below. Task 30 checks each one.

| # | Fact other plans check | Plans that check it | Delivered by |
|---|---|---|---|
| F1 | `Ambinder.A2A.slnx` at the root; no `namespace A2A.` and no `<RootName>`/`<AssemblyName>` starting `A2A.` in `src`, `tests`, `samples` | all | Tasks 2, 3 |
| F2 | `src/Ambinder.A2A.{Protocol,Server,AspNetCore,AspNetCore.Grpc,Client,Client.Grpc,Client.Entra}` exist; folder = `AssemblyName` = `RootNamespace` = `PackageId` | 02, 03, 04, 07, 09, 10, 14 | Tasks 2, 6–9 |
| F3 | `Ambinder.A2A.Server` and `Ambinder.A2A.Client` reference no ASP.NET Core, `Grpc.*`, `Google.Protobuf` or `Azure.*` | 04, 07, 09, 10 | Tasks 7, 9 |
| F4 | `src/Grpc.Shared/` holds `ProtoMapper`, compiled `internal` (with the generated types) into both gRPC packages; `src/A2A.Protocol.Grpc` is gone | 02, 03, 10, 14 | Task 6 |
| F5 | `src/Shared/` holds the helpers two shipped assemblies need, linked and `internal` (rule 10, B12) | 02, 03, 05, 07 | Task 8 |
| F6 | Test projects are `Ambinder.A2A.<Name>.Tests`. `InternalsVisibleTo` goes only to test assemblies (and, later, `Ambinder.A2A.Benchmarks`), never between shipped assemblies | 02, 10, 14 | Tasks 2, 10 |
| F7 | `src/Directory.Build.props` sets `GenerateDocumentationFile`, package metadata, `IsAotCompatible` per the map, and suppresses CS1591 until 02 turns the doc rules on. `tests/`, `samples/` and `benchmarks/` have `Directory.Build.props` with `IsPackable=false` | 02, 10, 12, 14 | Task 13 |
| F8 | `eng/package-dependencies.json` plus its allow-list test, runnable locally with one script | 02, 04, 05, 06, 07, 08, 10, 11 | Task 21 |
| F9 | Telemetry sources and meters are `Ambinder.A2A.Server` and `Ambinder.A2A.Client` (constants in `A2ATelemetryNames`); span, metric and attribute names are unchanged | 03, 09, 10, 14 | Task 4 |
| F10 | `CONTRIBUTING.md`, `CHANGELOG.md` (with a "Breaking changes" heading), `.github/CODEOWNERS` (with a `PublicAPI.*.txt` line), `.github/pull_request_template.md` (with "API files reviewed" and "perf evidence when a hot path changed" items) | 02, 14 | Task 17 |
| F11 | `ci.yml` keeps an Ubuntu `build-and-test` job on `Ambinder.A2A.slnx`, plus 01's jobs: pack, allow-list, samples on packages, coexistence, reproducibility, Windows leg, and hook jobs `tck`, `interop` and `perf` | 02, 12, 14, 16 | Tasks 20–26 |
| F12 | `release.yml` with a `docs` hook job that prints "not yet implemented" and a `perf` hook | 13, 14 | Task 27 |
| F13 | `.gitignore` has `TestResults/`, `*.nupkg`, `*.snupkg`, `artifacts/`, `BenchmarkDotNet.Artifacts/` | 14 | Task 4 |
| F14 | Tag `phase-1` on the last Phase 1 commit | 13 | Task 1 (owner) |
| F15 | `samples/GettingStarted.Agent` and `samples/GettingStarted.Client` exist (the release smoke job uses them; 02 and 13 refine them) | 02, 13, 14 | Task 18 |

---

## Facts about the current code (checked 2026-11-03 on `master` `4cf9f1a`)

- **Projects.** `src/A2A.Protocol`, `src/A2A.Protocol.Grpc`, `src/A2A.Server`, `src/A2A.Client`.
  - Tests: `tests/A2A.{Protocol,Server,Integration,MockEntra}.Tests` and `tests/A2A.Tck.Sut`.
  - Samples: `AgentClient`, `AgentServer`, `AppHost`, `MockEntra`, `SampleTelemetry`, `WebhookReceiver`.
  - Solution: `A2A.Reference.slnx`.
- **`A2A.Server`** has `FrameworkReference Microsoft.AspNetCore.App`. Its packages are `Microsoft.IdentityModel.JsonWebTokens`, `Grpc.AspNetCore` and `Grpc.StatusProto`. It references Protocol and Protocol.Grpc, and gives `InternalsVisibleTo A2A.Server.Tests`.
- **ASP.NET Core users in `A2A.Server`:**
  - `Auth/AgentAuthorizer.cs` (`Microsoft.AspNetCore.Authorization`)
  - every file under `Hosting/` except `A2AServerBuilder`, `A2AServerOptions` and `AgentCardInterfaces`
  - every file under `Transports/`
  - `A2AServiceCollectionExtensions` uses `Microsoft.AspNetCore.Hosting`, but only for `IStartupFilter`. It also calls `services.AddGrpc()` and `services.AddAuthorization()`.
- **No ASP.NET Core in:** `Agents/`, `Tasks/`, `Push/`, `Telemetry/`, `Auth/{CallerIdentity,DefaultOwnerResolver,IAgentAuthorizer,IOwnerResolver}`, `A2ACallContext`, `IA2ARequestHandler`.
  - `Push/PushSigningKey.cs` uses `Microsoft.IdentityModel.JsonWebTokens`.
  - The push code uses `AddHttpClient`, `IHttpClientFactory` and `BackgroundService`.
- **Helpers used by both the HTTP transports and the gRPC service:**
  - `TransportRequest` (internal), `TransportErrors` (internal), `ServiceParameters` (public), `RequestValidation` (public)
  - `ServiceParameters` calls `RequestValidator` (internal, in `Tasks/`, also used by `TaskManager`)
- **`A2A.Client`** has `Azure.Identity`, `Grpc.Net.Client` and `Grpc.StatusProto`, and gives `InternalsVisibleTo A2A.Integration.Tests`.
  - `Transports/GrpcA2AClient.cs` (public) uses `ClientSupport` (internal) and the proto types.
  - `Auth/AzureIdentityCredentialProvider.cs` is the only `Azure.*` user.
  - `A2AClientFactory.Create` news up `GrpcA2AClient` directly. Its internal `GrpcAddress` is tested by `ClientSelectionTests`.
- **`A2A.Protocol.Grpc`** compiles `proto/a2a.proto` (`GrpcServices="Both"`, public) plus `ProtoMapper.*.cs` (namespace `A2A.Protocol.Grpc`). The generated namespace is `Lf.A2a.V1`. `proto-drift` CI forbids editing `proto/a2a.proto`, so the namespace cannot change.
- **Tests that use proto types:**
  - `tests/A2A.Protocol.Tests/{ProtoJson.cs, Grpc/*.cs}`
  - `tests/A2A.Integration.Tests/{Transport/GrpcTransportTests.cs, Client/ClientHardeningTests.cs, Conformance/ExtensionNegotiationTests.cs}`. `ClientHardeningTests` hosts its own `BadService : Proto.A2AService.A2AServiceBase` with `services.AddGrpc()`.
- **Startup checks.** `A2AStartupValidation` (`IStartupFilter`) checks the unknown policy, the default agent and the gRPC port keys. `tests/A2A.Integration.Tests/Hosting/StartupValidationTests.cs` expects `app.StartAsync` to throw `InvalidOperationException`.
- **`TracingRequestHandler`** wraps `TaskManager` as `IA2ARequestHandler`. The telemetry constants `ServerSource = "A2A.Server"` and `ClientSource = "A2A.Client"` are in `src/A2A.Protocol/A2ATelemetryNames.cs`.
- **`DocsTests`** looks for `A2A.Reference.slnx` (`tests/A2A.Integration.Tests/Repo/DocsTests.cs`). `ComposeFileTests` checks the compose file.
- **Docker.** Four Dockerfiles under `docker/` and a `docker-compose.yml`. `run-all.sh` and `run-all.ps1` start the samples. `samples/AppHost` refers to the projects as `Projects.<CsprojName>`.
- **Build settings.** `Directory.Build.props` (root) sets only TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors and LangVersion. `Directory.Packages.props` pins Microsoft packages at `10.0.14`. There are no `Microsoft.Extensions.*` abstractions packages yet, because the server got them through the shared framework.
- **CI.** `ci.yml` has `build-and-test` (Ubuntu) and `proto-drift`, and uses `concurrency: ci-${{ github.ref }}` with cancel-in-progress.

---

## Global constraints

- **No behavior change in PR A.** Only names change. The golden JSON and proto round-trip tests must pass with byte-identical expected data (spec §3, "Check").
- **PR B changes behavior only where the split forces it.** Each forced change is listed in Task 11 and in the PR description:
  - gRPC services are added by `AddA2AGrpc()`.
  - The gRPC client transport is added by `AddGrpcTransport()`.
  - The unknown-policy check runs in `MapA2A()`.
- **Core means no host** (rule 2). After PR B:
  - `Ambinder.A2A.Protocol` has no package dependencies.
  - `Ambinder.A2A.Server` and `Ambinder.A2A.Client` have no `Microsoft.AspNetCore.*` (package or framework reference), `Grpc.*`, `Google.Protobuf`, `Azure.*` or database package.
- **No `InternalsVisibleTo` between shipped assemblies** (rule 6). It is allowed only to test assemblies. `Ambinder.A2A.Integration.Tests` may receive it from the projects whose internals it uses today. This is the owner's decision of 2026-11-03, recorded in the 02 plan, Task 3.1. The two gRPC packages give it to nobody in this plan. See Task 10.
- **Type names do not change** (01 §2). Only namespaces and assemblies change. The three exceptions come from 01 itself: generated proto types and `ProtoMapper` become internal (rule 4), plus the new seams in Task 11.
- **Wire names do not change:** `A2A-Version`, `A2A-Extensions`, `a2a.*` attributes, span names, proto package `lf.a2a.v1`, config section `A2A:` and environment variables `A2A__*`.
- **Builds:** `TreatWarningsAsErrors`, nullable on, central package versions. A new package version must be stable and at least two weeks old. Pin it exactly in `Directory.Packages.props`.
- **Third-party actions** (not `actions/*`) are pinned by commit SHA, with the version in a comment. Each job sets the least `permissions:` it needs.
- **No secrets in the repo.** Release secrets live only in the `nuget-release` GitHub Environment.
- **Run before each commit:**

```bash
dotnet build Ambinder.A2A.slnx -c Release
```

```bash
dotnet test Ambinder.A2A.slnx -c Release
```

  In PR A, use `A2A.Reference.slnx` until Task 2 renames it. Expected: green, with the same test count as Task 0, plus or minus only the tests a task says it adds.
- **Commits:** one or more per task, in the imperative. End each with the attribution lines the session gives you.
- **Docs style:** short, plain English sentences, as in the repo.

## Review focus (reviewers check these first)

1. **Nothing old survives.**
   - `git grep -nE '\bA2A\.(Protocol|Server|Client)\b'` finds only the history notes, the migration doc and the coexistence test (spec §3).
   - No `A2A.*.dll` without the `Ambinder.` prefix in any build output.
2. **The core has no host.** The allow-list test and the core-only publish test (Task 22) both pass. Check `Ambinder.A2A.Server.csproj` has no `FrameworkReference`.
3. **The gRPC types are internal in both gRPC packages, and the process still works with both loaded.** The integration tests load the server and client gRPC assemblies in one process and pass on all three transports.
4. **The release cannot publish by accident.** The tag must be on `master`, there must be a `CHANGELOG` section, approval must come from the environment, a stable release cannot ship unsigned, and fork PRs get no secrets.
5. **Samples run from packages.** The `samples-on-packages` job restores from `artifacts/packages` only.

---

## Task list

| # | Task | PR |
|---|---|---|
| 0 | Baseline | — |
| 1 | `phase-1` tag (owner) and history notes | A |
| 2 | Move projects and rename the solution | A |
| 3 | Rename namespaces, usings and references | A |
| 4 | Telemetry names, `.gitignore`, Docker, compose, scripts, AppHost, READMEs | A |
| 5 | PR A check and PR | A |
| 6 | `src/Grpc.Shared` and internal proto types | B |
| 7 | Split the server: `Server`, `AspNetCore`, `AspNetCore.Grpc` | B |
| 8 | `src/Shared` linked helpers | B |
| 9 | Split the client: `Client`, `Client.Grpc`, `Client.Entra` | B |
| 10 | Test projects after the split | B |
| 11 | Forced seams: `AddA2AGrpc`, `AddGrpcTransport`, startup checks | B |
| 12 | Dependency floors and transitive pinning; PR B check and PR | B |
| 13 | `Directory.Build.props` layout | C |
| 14 | Package metadata, MinVer, icon, per-package READMEs | C |
| 15 | SourceLink, symbols, deterministic builds | C |
| 16 | LICENSE, NOTICE, CHANGELOG | C |
| 17 | Community files and templates | C |
| 18 | Getting-started samples | C |
| 19 | AOT flags; PR C check and PR | C |
| 20 | NuGet audit levels (C1) | D |
| 21 | Dependency allow-list and its test | D |
| 22 | Pack job: package content checks, core-only publish, SourceLink test | D |
| 23 | Samples on packages | D |
| 24 | Coexistence project | D |
| 25 | Reproducibility job and Windows leg | D |
| 26 | Hook jobs; PR D check and PR | D |
| 27 | `release.yml` | E |
| 28 | Release dry run and runbook | E |
| 29 | Owner actions checklist | E |
| 30 | Final check against F1–F15 and spec acceptance | E |

---

### Task 0: Baseline

No code.

- [ ] **Step 1:** Fetch `master`, then build and run the tests:

```bash
git fetch origin master && git checkout -B feat/sdk-01-rename origin/master
```

```bash
dotnet test A2A.Reference.slnx -c Release
```

  Expected: green. Write down the test count per test project. Every later task compares against it.
- [ ] **Step 2:** Record the golden-file hashes:

```bash
git ls-files tests | grep -iE 'golden|\.json$' | xargs sha256sum > /tmp/golden-before.txt
```

  Task 5 compares them.
- [ ] **Step 3:** Confirm no other package's code has landed yet:

```bash
ls src
```

  Expected: only the four Phase 1 projects. If another package already added code, find it in the renamed tree in Task 3.

---

### Task 1: `phase-1` tag and history notes

**Owner action.** Pushing a tag is outward-facing. The executor asks the owner first, or the owner runs it.

- [ ] **Step 1:** The owner tags the commit Task 0 started from (the last Phase 1 commit on `master`):

```bash
git tag -a phase-1 -m "Last Phase 1 commit before the rename to Ambinder.A2A.*" <sha>
```

```bash
git push origin phase-1
```

  Record the SHA in the PR description. If the owner wants it later, continue. Task 30 checks that it exists.
- [ ] **Step 2:** Add one line at the top of `docs/superpowers/specs/2026-11-02-a2a-dotnet-reference-design.md` and `docs/superpowers/specs/2026-11-03-observability-design.md`, right under the title: `> Written before the rename to Ambinder.A2A.*.` Do the same for the two Phase 1 plans in `docs/superpowers/plans/` (`2026-11-02-...-phase1.md`, `2026-11-03-observability.md`).
- [ ] **Step 3:** Commit: `docs: mark Phase 1 specs and plans as written before the rename`.

---

### Task 2: Move projects and rename the solution

**Files:** every project folder and `.csproj` under `src/` and `tests/`; `A2A.Reference.slnx` → `Ambinder.A2A.slnx`.

| From | To |
|---|---|
| `src/A2A.Protocol/A2A.Protocol.csproj` | `src/Ambinder.A2A.Protocol/Ambinder.A2A.Protocol.csproj` |
| `src/A2A.Protocol.Grpc/A2A.Protocol.Grpc.csproj` | `src/Ambinder.A2A.Protocol.Grpc/Ambinder.A2A.Protocol.Grpc.csproj` (temporary for PR A; Task 6 removes it) |
| `src/A2A.Server/A2A.Server.csproj` | `src/Ambinder.A2A.Server/Ambinder.A2A.Server.csproj` (still the whole server; Task 7 splits it) |
| `src/A2A.Client/A2A.Client.csproj` | `src/Ambinder.A2A.Client/Ambinder.A2A.Client.csproj` (whole client; Task 9 splits it) |
| `tests/A2A.Protocol.Tests` | `tests/Ambinder.A2A.Protocol.Tests` |
| `tests/A2A.Server.Tests` | `tests/Ambinder.A2A.Server.Tests` |
| `tests/A2A.Integration.Tests` | `tests/Ambinder.A2A.Integration.Tests` |
| `tests/A2A.MockEntra.Tests` | `tests/Ambinder.A2A.MockEntra.Tests` |
| `tests/A2A.Tck.Sut` | `tests/Ambinder.A2A.Tck.Sut` (`RootNamespace`/`AssemblyName` `Ambinder.A2A.Tck.Sut`) |
| `A2A.Reference.slnx` | `Ambinder.A2A.slnx` |

Sample folders and `.csproj` file names stay as they are (01 §2). This keeps `Projects.AgentServer` and the other generated names in `samples/AppHost` working. Only each sample's `AssemblyName` becomes `Ambinder.A2A.Samples.<Name>`, and `SampleTelemetry` becomes `Ambinder.A2A.Samples.SampleTelemetry`. Keep each sample's `RootNamespace` (`AgentServer` and so on). The spec only names assemblies.

- [ ] **Step 1:** `git mv` each folder, then each `.csproj` inside it. Use `git mv` only, so history follows the files.
- [ ] **Step 2:** In each shipped `.csproj`, set nothing explicit: folder name = project name gives `AssemblyName` and `RootNamespace` by default. Remove any explicit `RootNamespace`/`AssemblyName` that still says `A2A.` (today only the TCK SUT has them).
- [ ] **Step 3:** Update every `ProjectReference` path. Rename `InternalsVisibleTo Include="A2A.Server.Tests"` → `Ambinder.A2A.Server.Tests`, and `A2A.Integration.Tests` → `Ambinder.A2A.Integration.Tests`.
- [ ] **Step 4:** `git mv A2A.Reference.slnx Ambinder.A2A.slnx`, then update its paths. Update `DocsTests` (it looks for `A2A.Reference.slnx`) and every doc or script that names the old file: `git grep -n "A2A.Reference.slnx"`.
- [ ] **Step 5:** Run `dotnet build Ambinder.A2A.slnx -c Release`. Expected: it builds. Namespaces are still `A2A.*`; that is fine until Task 3.
- [ ] **Step 6:** Commit: `refactor: rename projects and the solution to Ambinder.A2A.*`.

---

### Task 3: Rename namespaces, usings and references

- [ ] **Step 1:** Replace namespace and using lines in every `.cs` under `src/`, `tests/` and `samples/`:

```bash
git ls-files 'src/*.cs' 'tests/*.cs' 'samples/*.cs' | xargs sed -i -E 's/^namespace A2A\./namespace Ambinder.A2A./; s/^(global )?using A2A\./\1using Ambinder.A2A./; s/^using ([A-Za-z]+) = A2A\./using \1 = Ambinder.A2A./'
```

  `namespace A2A;` without a dot does not exist today. Confirm with `git grep -n "namespace A2A;"`. Do not touch `Lf.A2a.V1`.
- [ ] **Step 2:** Build. Fix every fully qualified `A2A.X` name the compiler reports (for example `global::A2A.`, `typeof(A2A.`, `cref="A2A.`, `<Using Include="A2A.` in a `.csproj`). Find the rest with:

```bash
git grep -nE '(^|[^.A-Za-z])A2A\.(Protocol|Server|Client|Integration|Tck)' -- src tests samples
```

- [ ] **Step 3:** String literals that hold type or assembly names, such as reflection tests and `DocsTests`: update them to the new names. Do **not** change strings that are wire or config names: `A2A-Version`, `A2A:` config keys, `A2A__` env vars, `a2a.` attributes.
- [ ] **Step 4:** Run the full test suite. Expected: green, same count as Task 0.
- [ ] **Step 5:** Commit: `refactor: rename namespaces to Ambinder.A2A.*`.

---

### Task 4: Telemetry names, `.gitignore`, Docker, compose, scripts, AppHost, READMEs

- [ ] **Step 1: Telemetry (01 §2).** In `A2ATelemetryNames`:
  - `ServerSource` and the server meter constant become `"Ambinder.A2A.Server"`.
  - `ClientSource` becomes `"Ambinder.A2A.Client"`.
  - If another source or meter constant exists (`git grep -n '"A2A\.' -- src`), rename it the same way, `A2A.X` → `Ambinder.A2A.X`.
  - Span, attribute and metric names do not change.
  - Update `docs/production.md` §6 and `samples/SampleTelemetry` (it subscribes to the sources by name; use the constants if it uses literals).
- [ ] **Step 2: `.gitignore`.** Add `TestResults/`, `*.nupkg`, `*.snupkg`, `artifacts/`, `BenchmarkDotNet.Artifacts/` (R41).
- [ ] **Step 3: Docker and scripts.**
  - In `docker/Dockerfile.*`, update project paths and the `ENTRYPOINT` DLL names (`AgentServer.dll` → `Ambinder.A2A.Samples.AgentServer.dll` and so on).
  - Also update `.dockerignore`, `docker-compose.yml`, `run-all.sh`, `run-all.ps1`, `.github/dependabot.yml` groups and `Directory.Packages.props` comments.
  - Find every hit with: `git grep -nE 'A2A\.(Protocol|Server|Client)|AgentServer\.dll|AgentClient\.dll|MockEntra\.dll|WebhookReceiver\.dll'`.
- [ ] **Step 4: READMEs and docs.** Update the root `README.md` tables, each `src/*/README.md` and `samples/*/README.md`, `docs/flows.md` and `docs/production.md`. Do not edit the files marked in Task 1, or `docs/superpowers/` specs and plans except those notes.
- [ ] **Step 5:** Run the full test suite. If Docker is available, also run `docker compose config`. Expected: green and valid. `ComposeFileTests` and `DocsTests` pass.
- [ ] **Step 6:** Commit: `refactor: rename telemetry sources, Docker files, scripts and docs to Ambinder.A2A.*`.

---

### Task 5: PR A check and PR

- [ ] **Step 1:** Check that no old names are left:

```bash
git grep -nE '\bA2A\.(Protocol|Server|Client)\b' -- . ':!docs/superpowers' ':!CHANGELOG.md'
```

  Expected: no output. The history notes are under `docs/superpowers`.

```bash
git grep -nE 'namespace A2A\.|<(Root|Assembly)Name>A2A\.' -- src tests samples
```

  Expected: no output.
- [ ] **Step 2:** Build, then list any output DLL that lacks the `Ambinder.` prefix:

```bash
dotnet build Ambinder.A2A.slnx -c Release && find . -path ./node_modules -prune -o -name 'A2A.*.dll' -print | grep -v '/Ambinder\.' || echo "OK"
```

  Expected: `OK`.
- [ ] **Step 3:** Compare the golden-file hashes with Task 0:

```bash
git ls-files tests | grep -iE 'golden|\.json$' | xargs sha256sum | diff /tmp/golden-before.txt - && echo "golden unchanged"
```

  Paths moved, so compare hashes only: strip the paths before you diff, or diff the sorted hash columns. Expected: identical hashes.
- [ ] **Step 4:** Push `feat/sdk-01-rename` and open PR A into `master`. Title: `refactor: rename to Ambinder.A2A.* (package 01, PR A)`. The body states: no behavior change, the `phase-1` tag SHA (or "owner to tag"), and the check outputs above.

---

### Task 6: `src/Grpc.Shared` and internal proto types

**PR B** starts on `feat/sdk-01-split`, created from `master` after PR A merges.

**Files:**

- Create `src/Grpc.Shared/` with the moved `ProtoMapper.*.cs`. The namespace becomes `Ambinder.A2A.Grpc` (**plan's call**: shared internal code, so no package namespace). There is no `.csproj` here. It is a source folder only.
- Delete `src/Ambinder.A2A.Protocol.Grpc/`.
- Create `src/Directory.Grpc.props`, imported by the two gRPC projects:

```xml
<Project>
  <PropertyGroup>
    <IncludeGoogleApiCommonProtos>true</IncludeGoogleApiCommonProtos>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)Grpc.Shared/**/*.cs" Link="Grpc.Shared/%(RecursiveDir)%(Filename)%(Extension)" />
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Google.Api.CommonProtos" />
    <PackageReference Include="Grpc.Tools" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- Each gRPC project adds its own `<Protobuf Include="$(MSBuildThisFileDirectory)../../proto/a2a.proto" ProtoRoot="$(MSBuildThisFileDirectory)../../proto" GrpcServices="Server|Client" Access="Internal" />`. Server is `GrpcServices="Server"` in `Ambinder.A2A.AspNetCore.Grpc` (Task 7). Client is `GrpcServices="Client"` in `Ambinder.A2A.Client.Grpc` (Task 9).
- Make `ProtoMapper` (every partial) `internal static`.

Rule 4 fallback: if two internal copies clash at run time (unlikely; C# protobuf has no global descriptor pool), or the size is unacceptable, **stop** and report. The fallback (`Ambinder.A2A.Grpc.Core`) changes the package map and needs the owner.

- [ ] **Step 1:** Do the move together with Tasks 7 and 9, because the projects that compile `Grpc.Shared` are created there. Commit Tasks 6, 7 and 9 as separate commits, but only after the solution builds. Order inside the branch: Task 6 files → Task 7 → Task 9 → build → commit each.
- [ ] **Step 2:** The proto tests move in Task 10.

---

### Task 7: Split the server

**Files:**

- Keep `src/Ambinder.A2A.Server/` (core). Remove its `FrameworkReference`, `Grpc.*` packages and Protocol.Grpc reference. Add `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Http`. These cover `TryAdd`, `ILogger`/`NullLogger`, `IOptions`/`OptionsBuilder.Validate`, `BackgroundService`/`IHostedService` and `AddHttpClient`. Keep `Microsoft.IdentityModel.JsonWebTokens` for `PushSigningKey` (package 08 removes it; noted in the allow-list).
- Create `src/Ambinder.A2A.AspNetCore/Ambinder.A2A.AspNetCore.csproj`: `FrameworkReference Microsoft.AspNetCore.App`, `ProjectReference` to Server.
- Create `src/Ambinder.A2A.AspNetCore.Grpc/Ambinder.A2A.AspNetCore.Grpc.csproj`: `ProjectReference` to AspNetCore; `Grpc.AspNetCore` and `Grpc.StatusProto`; `<Import Project="../Directory.Grpc.props" />`; `Protobuf ... GrpcServices="Server" Access="Internal"`.
- Move files with `git mv`. Keep the folder structure and keep each type's namespace. Namespaces follow the old folder (`Ambinder.A2A.Server.Transports.Rest`). Package 02 does the namespace review, so do not rename namespaces here.

| Files (post-PR-A paths under `src/Ambinder.A2A.Server/`) | Go to |
|---|---|
| `A2ACallContext.cs`, `IA2ARequestHandler.cs`, `Agents/*`, `Tasks/*`, `Push/*`, `Telemetry/*`, `Auth/{CallerIdentity,DefaultOwnerResolver,IAgentAuthorizer,IOwnerResolver}.cs`, `Hosting/{A2AServerBuilder,A2AServerOptions,AgentCardInterfaces,A2AServiceCollectionExtensions}.cs` | stay in `Ambinder.A2A.Server` |
| `Auth/AgentAuthorizer.cs`, `Hosting/{A2AEndpointRouteBuilderExtensions,AgentCardEndpoints,A2AStartupValidation}.cs`, `Transports/{JsonRpc,Rest,Sse}/*`, `Transports/RequestBody.cs` | `Ambinder.A2A.AspNetCore` |
| `Hosting/GrpcHostingExtensions.cs` | split: `MapA2AGrpc` → `Ambinder.A2A.AspNetCore.Grpc`; `ConfigureA2AKestrel` (Kestrel is ASP.NET Core, not gRPC) → `Ambinder.A2A.AspNetCore`, file `Hosting/A2AKestrelExtensions.cs` |
| `Transports/Grpc/*` | `Ambinder.A2A.AspNetCore.Grpc`; `A2AGrpcService` and `GrpcErrorMapper` become `internal` (a public class cannot derive from the now-internal generated base) |
| `Transports/{TransportRequest,TransportErrors,ServiceParameters,RequestValidation}.cs`, `Tasks/RequestValidator.cs` | `src/Shared/` (Task 8) |

- `A2AServiceCollectionExtensions.AddA2AServer` (stays in Server) loses `services.AddGrpc()`, `services.AddAuthorization()`, the `IAgentAuthorizer` → `AgentAuthorizer` registration and the `IStartupFilter` registration. Task 11 covers what replaces them.
- Namespace rule (01 §2): `Add…` extension classes go in namespace `Microsoft.Extensions.DependencyInjection`. `Map…`/`Configure…` extension classes go in `Microsoft.AspNetCore.Builder`. Change the namespace of `A2AServiceCollectionExtensions`, `A2AEndpointRouteBuilderExtensions`, the Kestrel extensions and the gRPC map extension to those. Callers then drop `using Ambinder.A2A.Server.Hosting;` where it was used only for these methods.

- [ ] **Step 1:** Create the projects and move the files. Build. Fix only:
  - access problems that Task 8's shared sources solve
  - the registrations that Task 11 moves
- [ ] **Step 2:** Run `git grep -n "Microsoft.AspNetCore\|Grpc\.\|Google\." -- src/Ambinder.A2A.Server`. Expected: no output.
- [ ] **Step 3:** Commit after Task 11 makes it build: `refactor: split the server into Server, AspNetCore and AspNetCore.Grpc`.

---

### Task 8: `src/Shared` linked helpers (rule 10, B12)

After Task 7, these types are needed by more than one shipped assembly:

- `TransportRequest`, `TransportErrors`, `ServiceParameters`, `RequestValidation`: AspNetCore and AspNetCore.Grpc.
- `RequestValidator`: Server (`TaskManager`), AspNetCore and AspNetCore.Grpc (through `ServiceParameters`).

**Files:**

- Create `src/Shared/AspNetCore/{TransportRequest,TransportErrors,ServiceParameters,RequestValidation}.cs` and `src/Shared/Server/RequestValidator.cs` with `git mv`. Keep the namespaces.
- `ServiceParameters` and `RequestValidation` are public today. They become `internal` now, because a public type compiled into two assemblies would clash for consumers. Note this in Task 11's list. Package 02 already plans it (02 plan 3.2a).
- Link with `<Compile Include="../Shared/AspNetCore/*.cs" LinkBase="Shared" />` in AspNetCore and AspNetCore.Grpc. Link `../Shared/Server/*.cs` into Server, AspNetCore and AspNetCore.Grpc.
- `PageToken` and `TaskStateRules` are used only by Server after the split. Leave them where they are. Packages 02, 03 and 05 move them if they need to (their plans allow either place).
- Add `src/Shared/README.md` (5 lines): the files here are linked into several shipped projects as `internal`. Never make them public. Never add `InternalsVisibleTo` between shipped projects (rule 6).

- [ ] **Step 1:** Move, link and build.
- [ ] **Step 2:** If a test uses one of these types through `InternalsVisibleTo` from two assemblies at once, it gets error CS0433 ("type exists in both"). Fix it by giving `InternalsVisibleTo` to the integration tests from one assembly only (AspNetCore). Never from AspNetCore.Grpc.
- [ ] **Step 3:** Commit: `refactor: share transport helpers between assemblies as linked internal sources`.

---

### Task 9: Split the client

**Files:**

- Keep `src/Ambinder.A2A.Client/` (core). Remove `Azure.Identity`, `Grpc.Net.Client`, `Grpc.StatusProto` and the Protocol.Grpc reference. Move `Transports/GrpcA2AClient.cs` and `Transports/GrpcErrorTranslator.cs` out, and `Auth/AzureIdentityCredentialProvider.cs` out.
- Create `src/Ambinder.A2A.Client.Grpc/Ambinder.A2A.Client.Grpc.csproj`: references Client; `Grpc.Net.Client` and `Grpc.StatusProto`; imports `../Directory.Grpc.props`; `Protobuf ... GrpcServices="Client" Access="Internal"`. It holds `GrpcA2AClient` (stays public in 01; 02 or 10 decide later), `GrpcErrorTranslator` (internal) and `A2AClientGrpcExtensions` (Task 11).
- Create `src/Ambinder.A2A.Client.Entra/Ambinder.A2A.Client.Entra.csproj`: references Client and `Azure.Identity`. It holds `AzureIdentityCredentialProvider` (public, same namespace as today).
- `ClientSupport` (internal) is used by `GrpcA2AClient`. Move it to `src/Shared/Client/ClientSupport.cs` and link it into Client and Client.Grpc.
- `A2AClientFactory.GrpcAddress` stays in the core client. It is a pure string function, and `ClientSelectionTests` tests it. Task 11 passes its result to the gRPC transport.
- Keep Client's `InternalsVisibleTo Ambinder.A2A.Integration.Tests`. Client.Grpc and Client.Entra give none.

- [ ] **Step 1:** Create the projects, move the files and build.
- [ ] **Step 2:** Run `git grep -n "Grpc\.\|Google\.\|Azure\." -- src/Ambinder.A2A.Client`. Expected: no output.
- [ ] **Step 3:** Commit after Task 11: `refactor: split the client into Client, Client.Grpc and Client.Entra`.

---

### Task 10: Test projects after the split

The proto types are now internal in two assemblies. Tests that use them compile **their own copy** of the proto and `Grpc.Shared`. This needs no `InternalsVisibleTo` and no `extern alias` (**plan's call**, simpler than the `extern alias` route in the 02 plan's step 3.4, which that plan says to skip "if 01 did something").

**Files:**

- Create `tests/Ambinder.A2A.Grpc.Tests/` (xUnit v3, same shape as `tests/Ambinder.A2A.Protocol.Tests`):
  - imports `../../src/Directory.Grpc.props`, adds `Protobuf ... GrpcServices="None" Access="Internal"`, references Protocol
  - move `tests/Ambinder.A2A.Protocol.Tests/Grpc/*.cs` and `ProtoJson.cs` here, with any sample data they need. If `Samples.cs` is shared by JSON tests, link it from Protocol.Tests with `<Compile Include="../Ambinder.A2A.Protocol.Tests/Samples.cs" Link="Samples.cs" />`
  - remove the Protocol.Grpc reference from Protocol.Tests
- `tests/Ambinder.A2A.Integration.Tests`:
  - references `Ambinder.A2A.AspNetCore`, `.AspNetCore.Grpc`, `.Client`, `.Client.Grpc` and `.Client.Entra` (plus the samples)
  - adds `<Protobuf Include="../../proto/a2a.proto" ProtoRoot="../../proto" GrpcServices="Both" Access="Internal" />` with `Grpc.Tools`, `Google.Protobuf`, `Google.Api.CommonProtos` and `Grpc.AspNetCore` (for `BadService`), so `GrpcTransportTests`, `ClientHardeningTests` and `ExtensionNegotiationTests` keep their own `Lf.A2a.V1` types
  - if these tests use `ProtoMapper`, link `src/Grpc.Shared/**/*.cs` too
  - neither gRPC assembly gives `InternalsVisibleTo` to this project; that would cause error CS0436 or CS0433
- `tests/Ambinder.A2A.Server.Tests` references Server, plus AspNetCore if a test uses `AgentAuthorizer`. `AgentAuthorizerTests` moves to a new `tests/Ambinder.A2A.AspNetCore.Tests`, which receives `InternalsVisibleTo` from AspNetCore, or it stays in Server.Tests with a reference to AspNetCore if `AgentAuthorizer` is public (it is today). Prefer staying: fewer projects.
- `tests/Ambinder.A2A.Tck.Sut` references AspNetCore and AspNetCore.Grpc.
- Add every new test project to `Ambinder.A2A.slnx`.

- [ ] **Step 1:** Make the moves. Run the full suite. Expected: the same test count as Task 0. Tests only moved between projects; the per-project counts change and the total does not.
- [ ] **Step 2:** Commit: `test: give gRPC tests their own proto copy after the types became internal`.

---

### Task 11: Forced seams

The split takes `AddGrpc()`, `AddAuthorization()`, the authorizer registration and the `IStartupFilter` out of the core `AddA2AServer` (01 rule 2, 5), and the direct `GrpcA2AClient` construction out of the core client factory. This task adds the smallest replacement for each. Packages 07, 09 and 10 build their full designs on top of these. The names match theirs, so their plans extend rather than replace.

**Server side:**

```csharp
// src/Ambinder.A2A.AspNetCore.Grpc/A2AGrpcServiceCollectionExtensions.cs  (namespace Microsoft.Extensions.DependencyInjection)
public static class A2AGrpcServiceCollectionExtensions
{
    /// <summary>Adds the gRPC services the A2A gRPC endpoint needs. Call it before MapA2AGrpc().</summary>
    public static A2AServerBuilder AddA2AGrpc(this A2AServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddGrpc();
        return builder;
    }
}
```

- Package 09's plan (Task 7) creates this file with options and size limits. When 09 runs, it extends this method (adds the `configure` parameter). Note this in the PR description for 09.
- `MapA2AGrpc()` is unchanged otherwise. Without `AddA2AGrpc`, ASP.NET Core's own error ("…calling 'IServiceCollection.AddGrpc'…") appears. Add a check at the start of `MapA2AGrpc`: if `GrpcMarkerService` is not registered, throw `InvalidOperationException("MapA2AGrpc() needs services.AddA2AServer(...).AddA2AGrpc(). Add that call before building the app.")`. Resolve `Grpc.AspNetCore.Server.Internal.GrpcMarkerService` by type name through `endpoints.ServiceProvider.GetService(...)`, or simply catch the framework's exception and rethrow with this message.
- **Authorizer.**
  - `IAgentAuthorizer` stays in Server. `AgentAuthorizer` moves to AspNetCore.
  - `TransportRequest.AuthorizeAsync` resolves `http.RequestServices.GetService<IAgentAuthorizer>() ?? AgentAuthorizer.Default(http.RequestServices)`.
  - `AgentAuthorizer.Default` resolves `IAuthorizationService` only when the agent has a policy. If it is missing, it throws `InvalidOperationException("Agent '{name}' uses authorization policy '{policy}'. Register it with services.AddAuthorization(...).")`.
  - Agents without a policy need no authorization services, as today.
  - An app with policies already calls `AddAuthorization`/`AddAuthorizationBuilder` to register them. The sample and the tests do: `AuthSetup.cs`, `MinimalServer.cs`, `StartupValidationTests.cs`.
- **Startup checks.**
  - The core part moves to `src/Ambinder.A2A.Server/Hosting/A2AServerStartupCheck.cs` (internal, `IHostedLifecycleService`, registered by `AddA2AServer`). In `StartingAsync` it resolves `AgentCatalog` (the default-agent check and the card factories) and checks the `GrpcPorts` keys.
  - `StartingAsync` runs before any hosted service's `StartAsync`, so it runs before Kestrel listens. The existing tests that expect `app.StartAsync` to throw stay as they are.
  - The policy check needs `IAuthorizationPolicyProvider` (ASP.NET Core). It stays in `A2AStartupValidation` (AspNetCore, now a static internal class). `MapA2A()` calls it, and `MapA2AGrpc()` calls it too when `MapA2A` was not called.
  - Change `StartupValidationTests.Unknown_authorization_policy_fails_startup` so that the throw is expected from `app.MapA2A()`. Keep the message assertion.
  - **Plan's call:** later plans (07 Task 5, 11) edit `A2AStartupValidation` by name. It still exists, now called from `MapA2A`.

**Client side:**

```csharp
// src/Ambinder.A2A.Client/A2AClientOptions.cs — new member
/// <summary>
/// Creates the gRPC transport. Set by AddGrpcTransport() in Ambinder.A2A.Client.Grpc; null means gRPC interfaces are not used.
/// Package 10 replaces this with transport factories; the AddGrpcTransport() call stays the same.
/// </summary>
public Func<Uri, AgentCard, A2AClientOptions, string?, IA2AClient>? GrpcTransport { get; set; }

// src/Ambinder.A2A.Client.Grpc/A2AClientGrpcExtensions.cs  (namespace Ambinder.A2A.Client)
public static class A2AClientGrpcExtensions
{
    public static A2AClientOptions AddGrpcTransport(this A2AClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.GrpcTransport = static (address, card, o, tenant) => new GrpcA2AClient(address, card, o, tenant);
        return options;
    }
}
```

- In `A2AClientFactory`:
  - GRPC counts as "supported" only when `options.GrpcTransport` is set.
  - When the card has a GRPC interface, no other interface is supported, and `GrpcTransport` is null, throw `NotSupportedException("The agent offers only GRPC; add Ambinder.A2A.Client.Grpc and call .AddGrpcTransport().")`. This is the exact text the 10 plan (Task 9) uses.
  - The chosen gRPC interface still goes through `GrpcAddress`, then `options.GrpcTransport(new Uri(address), card, options, tenant)`.
  - The `A2AClientOptions` copy and clone code, if any, copies the new property.
- Callers to update, adding `.AddGrpcTransport()` where options are built for gRPC: `samples/AgentClient`, the integration test fixtures and `TransportParityTests`/`ClientSelectionTests`. Find them with `git grep -n "new A2AClientOptions"`.

**Callers of the server seams:**

- Add `.AddA2AGrpc()` to `samples/AgentServer/AgentServerApp.cs`, `tests/Ambinder.A2A.Tck.Sut/Program.cs` and `tests/.../Fixtures/MinimalServer.cs`. Find them with `git grep -n "MapA2AGrpc"`.

- [ ] **Step 1: Write the failing tests** (in `tests/Ambinder.A2A.Integration.Tests/Hosting/SplitSeamTests.cs`):
  - `MapA2AGrpc_without_AddA2AGrpc_says_what_to_add`: the message.
  - `Agent_without_policy_needs_no_authorization_services`: `WebApplication.CreateSlimBuilder()` with no `AddAuthorization`, one agent without a policy, `MapA2A`. A JSON-RPC send on `TestServer` (or Kestrel on port 0) succeeds.
  - `Grpc_only_card_without_grpc_transport_names_the_package`: the exact message.
  - `AddGrpcTransport_selects_grpc`: a card with only GRPC plus `AddGrpcTransport()` gives an inner `GrpcA2AClient`.
  - `Unknown_default_agent_still_fails_at_StartAsync`: the existing test, unchanged.
- [ ] **Step 2:** Run. Expected: fail.
- [ ] **Step 3:** Implement, and update the callers.
- [ ] **Step 4:** Run the full suite. Expected: green. Test count = Task 0 + the new tests.
- [ ] **Step 5:** Commit: `feat: add AddA2AGrpc, AddGrpcTransport and host-free startup checks for the package split`.

---

### Task 12: Dependency floors, transitive pinning; PR B check and PR

- [ ] **Step 1: Central versions (rule 7).**
  - In `Directory.Packages.props`, set `CentralPackageTransitivePinningEnabled=true`.
  - Add central versions for the new `Microsoft.Extensions.*` packages at the newest `10.0.x` the repo already uses (`10.0.14`).
  - In the shipped projects (`src/Ambinder.A2A.*`), add `VersionOverride="10.0.0"` on each `Microsoft.*` package. Tests and samples use the central (newest) version.
  - If NuGet audit (Task 20) flags a 10.0.0 floor, raise that floor to the first fixed version and write why in a comment.
- [ ] **Step 2:** `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages`, then read each `.nuspec`. Expected dependencies:
  - Protocol: none
  - Server: Protocol plus the five `Microsoft.Extensions.*` packages plus `Microsoft.IdentityModel.JsonWebTokens`
  - Client: Protocol only
  - No `Grpc.*` or `Azure.*` in Server or Client
  - Test projects and samples are not packed yet. Task 13 sets `IsPackable`. For now, pack only `src/**`.
- [ ] **Step 3:** Run the full suite and `docker compose config`. Expected: green.
- [ ] **Step 4:** Push `feat/sdk-01-split` and open PR B into `master`. The body lists:
  - the forced behavior changes from Task 11: `AddA2AGrpc()`, `AddGrpcTransport()`, `MapA2A()` runs the policy check, `AddA2AServer` no longer adds authorization services, `ServiceParameters`/`RequestValidation`/`A2AGrpcService`/`GrpcErrorMapper`/`ProtoMapper` and the proto types are now internal
  - the per-package dependency lists
  - "Notes for 09 and 10" (who extends which seam)
- [ ] **Step 5:** Commit: `build: pin Microsoft package floors and enable transitive pinning`.

---

### Task 13: `Directory.Build.props` layout

**PR C** starts on `feat/sdk-01-packaging` from `master` after PR B merges.

**Files:** `Directory.Build.props` (root), `src/Directory.Build.props`, `src/Directory.Build.targets`, `tests/Directory.Build.props`, `samples/Directory.Build.props`, `benchmarks/Directory.Build.props` and `benchmarks/README.md`.

Root (all projects):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <!-- Task 20 adds the audit warning-as-error rules. -->
</Project>
```

`src/Directory.Build.props` imports the root one (`<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />`) and sets:

- `IsPackable=true`
- `GenerateDocumentationFile=true`, plus `<NoWarn>$(NoWarn);CS1591</NoWarn>` with the comment `<!-- Package 02 removes this when every public member has XML docs. -->`
- `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `DebugType=portable`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`
- `EnablePackageValidation=true`
- the metadata from Task 14

`src/Directory.Build.targets` is empty except the import of a root `.targets`, if one exists. Packages 02 and 14 add to it.

`tests/`, `samples/` and `benchmarks/` props import the root and keep `IsPackable=false`. `benchmarks/` has only this props file and a 3-line README ("benchmark projects go here; package 14"). Projects under `src/Shared` and `src/Grpc.Shared` do not exist: they are source folders.

- [ ] **Step 1:** Write the files. Remove the `IsPackable=false` lines the test `.csproj` files carry today (now inherited).
- [ ] **Step 2:** `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages`. Expected: exactly the 7 shipped packages and their `.snupkg` files. Nothing from `tests/` or `samples/`.
- [ ] **Step 3:** Commit: `build: split Directory.Build.props for src, tests, samples and benchmarks`.

---

### Task 14: Package metadata, MinVer, icon, per-package READMEs

**Files:** `src/Directory.Build.props`, `Directory.Packages.props` (MinVer), `eng/icon.png`, `src/*/README.md` (7), each shipped `.csproj` (`Description`, extra `PackageTags`).

Metadata (01 §4 table):

```xml
<PropertyGroup>
  <Authors>Roman Ambinder</Authors>
  <Copyright>Copyright (c) Roman Ambinder</Copyright>
  <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  <A2ARepositoryUrl>https://github.com/roman-ambinder/a2a-starter</A2ARepositoryUrl>
  <PackageProjectUrl>$(A2ARepositoryUrl)</PackageProjectUrl>
  <RepositoryUrl>$(A2ARepositoryUrl)</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageTags>a2a;agent2agent;agents;ai;json-rpc;grpc;aspnetcore</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageIcon>icon.png</PackageIcon>
  <PackageReleaseNotes>$(A2ARepositoryUrl)/releases/tag/v$(PackageVersion)</PackageReleaseNotes>
  <NeutralLanguage>en</NeutralLanguage>
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
  <MinVerTagPrefix>v</MinVerTagPrefix>
  <MinVerMinimumMajorMinor>1.0</MinVerMinimumMajorMinor>
  <MinVerDefaultPreReleaseIdentifiers>preview.0</MinVerDefaultPreReleaseIdentifiers>
</PropertyGroup>
<ItemGroup>
  <None Include="$(MSBuildProjectDirectory)/README.md" Pack="true" PackagePath="/" />
  <None Include="$(MSBuildThisFileDirectory)../eng/icon.png" Pack="true" PackagePath="/" Visible="false" />
  <None Include="$(MSBuildThisFileDirectory)../LICENSE" Pack="true" PackagePath="/" Visible="false" />
  <None Include="$(MSBuildThisFileDirectory)../NOTICE" Pack="true" PackagePath="/" Visible="false" />
  <PackageReference Include="MinVer" PrivateAssets="all" />
</ItemGroup>
```

- MinVer:
  - Pin the newest stable version in `Directory.Packages.props`.
  - CI checkouts that build packages need `fetch-depth: 0` (Tasks 22, 25, 27). Otherwise MinVer gives `0.0.0`-style versions and warns.
  - MinVer goes in `src/` only. Tests and samples do not need a version.
- Icon: `eng/icon.png`, 128×128 PNG. The artwork is the builder's choice. A plain generated placeholder is fine until the owner supplies one. Write it with a 20-line Python script that uses only `zlib` and `struct`; do not commit the script.
- READMEs: about 40 lines each (01 §4). What the package is, `dotnet add package <Id>`, a 10-line example, and a link to the repo docs. The content rules are package 13's; keep these short and factual.
  - Protocol: models and JSON.
  - Server: core, `AddA2AServer` without a web host.
  - AspNetCore: `AddA2AServer` + `MapA2A`.
  - AspNetCore.Grpc: `AddA2AGrpc` + `MapA2AGrpc`.
  - Client: `A2AClientFactory.Create`.
  - Client.Grpc: `AddGrpcTransport`.
  - Client.Entra: `AzureIdentityCredentialProvider`.
- `Description` per project: one sentence each, from the package map's "What it holds" column.

- [ ] **Step 1:** Write a failing test, `tests/Ambinder.A2A.Packaging.Tests/PackageContentTests.cs` (new project; Task 22 adds more). For each `artifacts/packages/Ambinder.A2A.*.nupkg` (not `.snupkg`), open it as a zip and assert:
  - entries `README.md`, `icon.png`, `LICENSE`, `NOTICE`, `lib/net10.0/<Id>.dll`, `lib/net10.0/<Id>.xml`
  - the nuspec has `<license type="expression">Apache-2.0</license>`, `<repository type="git" url="https://github.com/roman-ambinder/a2a-starter" commit="…" />`, `<authors>Roman Ambinder</authors>`, a non-empty `<description>`, and `<icon>icon.png</icon>`

  When `artifacts/packages` has no packages, the tests **skip visibly** with the message "Run eng/pack.sh first" (xUnit v3 `Assert.Skip`).
- [ ] **Step 2:** Add `eng/pack.sh` (and `eng/pack.ps1`, same steps):

```bash
#!/usr/bin/env bash
set -euo pipefail
rm -rf artifacts/packages
dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages -p:ContinuousIntegrationBuild=true
dotnet test --project tests/Ambinder.A2A.Packaging.Tests -c Release
```

- [ ] **Step 3:** Run `eng/pack.sh`. Expected: fails before the metadata exists.
- [ ] **Step 4:** Add the metadata, icon, READMEs and descriptions.
- [ ] **Step 5:** Run `eng/pack.sh`. Expected: pass. The LICENSE and NOTICE checks pass after Task 16, so do Task 16 first if needed.
- [ ] **Step 6:** Commit: `build: package metadata, MinVer versions, icon and package READMEs`.

---

### Task 15: SourceLink, symbols, deterministic builds

SourceLink is built into the .NET SDK for GitHub repos (01 §4). There is no package to add.

- [ ] **Step 1:** Install the tool:

```bash
dotnet tool install --global sourcelink
```

  Then run `sourcelink test` on every `.pdb` inside every `.snupkg`: unzip to a temp folder first. The SourceLink check needs the commit to be pushed. Locally it may report files as not found, so the job in Task 22 is the real gate.
- [ ] **Step 2:** Make sure `EmbedUntrackedSources` covers the generated proto files in the gRPC packages. If `sourcelink test` reports `obj/` files, set `<EmbedAllSources>true</EmbedAllSources>` only for the two gRPC projects, and note it.
- [ ] **Step 3:** Commit, if anything changed: `build: embed generated sources for SourceLink`.

---

### Task 16: LICENSE, NOTICE, CHANGELOG

- [ ] **Step 1: `LICENSE`.** The full Apache License 2.0 text, unchanged, from `https://www.apache.org/licenses/LICENSE-2.0.txt`. Then add the appendix line with "Copyright (c) Roman Ambinder" (O5).
- [ ] **Step 2: `NOTICE`.**

```text
Ambinder.A2A
Copyright (c) Roman Ambinder

This product includes the A2A protocol definition (proto/a2a.proto) from the A2A project,
https://github.com/a2aproject/A2A, licensed under the Apache License 2.0.
See proto/README.md for the source tag and hash.
```

  Check `proto/README.md` for the exact source URL and tag, and copy them.
- [ ] **Step 3: `CHANGELOG.md`.** Keep a Changelog format with an `## [Unreleased]` section. It has `### Breaking changes`, listing:
  - the rename to `Ambinder.A2A.*`
  - the telemetry source and meter rename
  - `A2A.Reference.slnx` → `Ambinder.A2A.slnx`
  - the PR B list from Task 12

  Then `### Added`. Package 02 and the others append to it.
- [ ] **Step 4:** Commit: `docs: add LICENSE, NOTICE and CHANGELOG`.

---

### Task 17: Community files and templates

**Files:** `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `.github/CODEOWNERS`, `.github/ISSUE_TEMPLATE/{bug,feature,question}.yml` (or `.md`), `.github/pull_request_template.md`.

- `SECURITY.md`:
  - Report privately through GitHub private vulnerability reporting. Owner action 7 turns it on.
  - Do not use public issues for vulnerabilities.
  - Supported versions: "latest minor, plus security fixes for the previous minor for 3 months" (C2, package 02).
  - Response targets: acknowledge within 5 working days (**plan's call**; the owner can change it).
- `CONTRIBUTING.md`:
  - build and test commands
  - "one change per PR"
  - DCO sign-off (`git commit -s`), no CLA
  - the rule on `InternalsVisibleTo` (rule 6), with the exception for `Ambinder.A2A.Integration.Tests` (owner decision 2026-11-03)
  - the `src/Shared` rule
  - placeholders headed "Public API files" (package 02 fills it) and "Performance rules" (package 14 fills it)
  - the release runbook (Task 28)
- `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1 text, with the contact set to the GitHub private reporting route (no email address).
- `.github/CODEOWNERS`:

```text
*                         @roman-ambinder
**/PublicAPI.*.txt        @roman-ambinder
/.github/workflows/       @roman-ambinder
/eng/                     @roman-ambinder
/proto/                   @roman-ambinder
```

- `.github/pull_request_template.md` checklist:
  - [ ] Tests added or updated
  - [ ] Docs updated
  - [ ] `CHANGELOG.md` updated
  - [ ] API files reviewed (`PublicAPI.*.txt`)
  - [ ] Perf evidence when a hot path changed
  - [ ] `eng/package-dependencies.json` updated if a package dependency changed
- Issue templates: bug (version, repro, expected, actual), feature, and question (points to Discussions if enabled, else issues).

- [ ] **Step 1:** Write the files.
- [ ] **Step 2:** Commit: `docs: add security, contributing, code of conduct, code owners and templates`.

---

### Task 18: Getting-started samples

The release smoke job (Task 27) runs the hello agent and the 5-line client (01 Goal 8, §7 step 8). Packages 02 and 13 refine these samples later; their plans check whether the samples exist.

**Files:**

- `samples/GettingStarted.Agent/GettingStarted.Agent.csproj` (`Microsoft.NET.Sdk.Web`, `AssemblyName` `Ambinder.A2A.Samples.GettingStarted.Agent`, references `src/Ambinder.A2A.AspNetCore`) and `Program.cs`
- `samples/GettingStarted.Client/GettingStarted.Client.csproj` (console, references `src/Ambinder.A2A.Client`) and `Program.cs`
- `samples/GettingStarted.Agent/README.md` and `samples/GettingStarted.Client/README.md`, 5 lines each
- `Ambinder.A2A.slnx`

`Program.cs` (agent): at most 20 lines. `AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5000")`, one echo agent built from today's `IAgentHandler`/`IAgentEventSink` API, `MapA2A()`, `app.Run("http://localhost:5000")`. The `PublicBaseUrl` line goes away when package 09 adds the zero-argument `AddA2AServer()`.

`Program.cs` (client): about 5 lines. Read the card (`A2ACardResolver`), `A2AClientFactory.Create(card, new A2AClientOptions())`, send "hello", print the reply text.

- [ ] **Step 1:** Write a test, `tests/Ambinder.A2A.Integration.Tests/Samples/GettingStartedTests.cs`. It starts the agent's app in-process on Kestrel port 0: expose a `static WebApplication Build(string[] args, string url)` in the agent's `Program` the same way the other samples do, or reuse its top-level program through `WebApplicationFactory`. The client logic sends "hello" and gets "hello" back.
- [ ] **Step 2:** Run. Expected: fail. Then write the samples. Run again. Expected: pass.
- [ ] **Step 3:** Commit: `docs: add the getting-started agent and client samples`.

---

### Task 19: AOT flags; PR C check and PR

- [ ] **Step 1:** Set `<IsAotCompatible>true</IsAotCompatible>` in `Ambinder.A2A.Protocol`, `.Server`, `.AspNetCore` and `.Client` (the map's "yes" rows). Leave the other three without it.
- [ ] **Step 2:** Build. The trim and AOT analyzers may report IL2xxx/IL3xxx warnings, which are errors here.
  - Fix the ones with a local fix: a `JsonTypeInfo` instead of `Type`, `[DynamicallyAccessedMembers]` on a `Type` parameter such as `AgentRegistration.HandlerType`.
  - For the rest, add `<WarningsNotAsErrors>$(WarningsNotAsErrors);ILxxxx</WarningsNotAsErrors>` in that one project only, with the comment `<!-- AOT: fixed before the M2 AOT smoke job (01 §6). -->`. Then list each code and location in `docs/aot-warnings.md`.
  - Do not suppress with `#pragma` or `UnconditionalSuppressMessage` unless the code is truly safe, and say why in the attribute's `Justification`.
- [ ] **Step 3:** Run `eng/pack.sh` and the full suite. Expected: green.
- [ ] **Step 4:** Push `feat/sdk-01-packaging` and open PR C. The body has the pack listing (IDs, sizes) and the AOT warning list, if any.
- [ ] **Step 5:** Commit: `build: mark Protocol, Server, AspNetCore and Client as AOT-compatible`.

---

### Task 20: NuGet audit levels (C1)

**PR D** starts on `feat/sdk-01-ci` from `master` after PR C merges.

- [ ] **Step 1:** In the root `Directory.Build.props`:

```xml
<PropertyGroup>
  <!-- C1: High/Critical advisories fail every build; Low/Moderate fail only when A2AStrictAudit=true (nightly, release). -->
  <WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>
  <WarningsNotAsErrors Condition="'$(A2AStrictAudit)' != 'true'">$(WarningsNotAsErrors);NU1901;NU1902</WarningsNotAsErrors>
</PropertyGroup>
```

  `TreatWarningsAsErrors` is on, so NU1901 and NU1902 would otherwise be errors on PRs too. `WarningsNotAsErrors` keeps them as warnings unless strict.
- [ ] **Step 2: Check the rule with a test package.** Pick a package version with a known Low or Moderate advisory, and one with High (look them up on the GitHub Advisory Database at the time). Add each to a scratch project, not committed. Then run each of these:

```bash
dotnet restore
```

```bash
dotnet restore -p:A2AStrictAudit=true
```

  Expected: High fails both; Low/Moderate warns in the first and fails in the second. Record the packages used in the PR description.
- [ ] **Step 3:** Commit: `build: NuGet audit levels for PR, nightly and release builds`.

---

### Task 21: Dependency allow-list and its test

**Files:** `eng/package-dependencies.json`, `tests/Ambinder.A2A.Packaging.Tests/DependencyAllowListTests.cs`.

`eng/package-dependencies.json`: one entry per shipped package. Each entry is the full set of NuGet dependency IDs from its nuspec and its framework references. Versions are not listed.

```json
{
  "$comment": "Allowed dependencies per shipped package (01 §6). A new dependency must be added here in the same PR. Rows are added by the package that creates the project.",
  "packages": {
    "Ambinder.A2A.Protocol":        { "dependencies": [], "frameworkReferences": [] },
    "Ambinder.A2A.Server":          { "dependencies": ["Ambinder.A2A.Protocol", "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Options", "Microsoft.Extensions.Hosting.Abstractions", "Microsoft.Extensions.Http", "Microsoft.IdentityModel.JsonWebTokens"], "frameworkReferences": [], "notes": "Microsoft.IdentityModel.JsonWebTokens is removed by package 08 (PushSigningKey)." },
    "Ambinder.A2A.AspNetCore":      { "dependencies": ["Ambinder.A2A.Server"], "frameworkReferences": ["Microsoft.AspNetCore.App"] },
    "Ambinder.A2A.AspNetCore.Grpc": { "dependencies": ["Ambinder.A2A.AspNetCore", "Grpc.AspNetCore", "Grpc.StatusProto", "Google.Protobuf", "Google.Api.CommonProtos"], "frameworkReferences": ["Microsoft.AspNetCore.App"] },
    "Ambinder.A2A.Client":          { "dependencies": ["Ambinder.A2A.Protocol"], "frameworkReferences": [] },
    "Ambinder.A2A.Client.Grpc":     { "dependencies": ["Ambinder.A2A.Client", "Grpc.Net.Client", "Grpc.StatusProto", "Google.Protobuf", "Google.Api.CommonProtos"], "frameworkReferences": [] },
    "Ambinder.A2A.Client.Entra":    { "dependencies": ["Ambinder.A2A.Client", "Azure.Identity"], "frameworkReferences": [] }
  },
  "forbiddenInCore": {
    "packages": ["Ambinder.A2A.Protocol", "Ambinder.A2A.Server", "Ambinder.A2A.Client"],
    "prefixes": ["Microsoft.AspNetCore.", "Grpc.", "Google.Protobuf", "Azure.", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Data.SqlClient", "Microsoft.Azure.Cosmos"]
  }
}
```

The exact dependency lists come from the real nuspecs after Task 12. Write what `dotnet pack` produces, then check that each list matches the map's "Key dependencies" column. A mismatch with the map is a bug to fix in the project, not in the JSON. If `Google.Api.CommonProtos` or `Grpc.StatusProto` turns out to be unneeded, remove it from the project instead.

Tests (in `Ambinder.A2A.Packaging.Tests`; skip visibly without packages, as in Task 14):

- `Pack_output_matches_the_allow_list_exactly`: the set of package IDs in `artifacts/packages` equals the keys of `packages`. Nothing missing, nothing extra (01 §7 step 3).
- `Each_package_has_exactly_its_allowed_dependencies`: for each nuspec, the dependency IDs (all groups) equal `dependencies`, and the framework references equal `frameworkReferences`. The failure message names the package, the extra or missing ID, and "add it to eng/package-dependencies.json in this PR if it is intended".
- `Core_packages_have_no_forbidden_dependencies`: no dependency of a `forbiddenInCore` package starts with a forbidden prefix, and none has a framework reference.

- [ ] **Step 1:** Write the tests. Run `eng/pack.sh`. Expected: fail (no JSON yet).
- [ ] **Step 2:** Write the JSON from the real nuspecs. Run again. Expected: pass.
- [ ] **Step 3: Prove the gate.** Temporarily add `<PackageReference Include="Azure.Identity" />` to `Ambinder.A2A.Client` and run `eng/pack.sh`. Expected: two tests fail with messages naming `Azure.Identity`. Revert.
- [ ] **Step 4:** Commit: `build: dependency allow-list for shipped packages`.

---

### Task 22: Pack job, content checks, core-only publish, SourceLink

**Files:** `.github/workflows/ci.yml` (job `pack`), `tests/Ambinder.A2A.CoreOnlyApp/` (console app, non-packable, **not** in `Ambinder.A2A.slnx` so normal builds skip it), `eng/check-core-only.sh`.

- `CoreOnlyApp` references only `src/Ambinder.A2A.Server` and `src/Ambinder.A2A.Client`. Its `Program.cs` builds a `ServiceCollection`, calls `AddA2AServer(...)` with one agent, resolves `IA2ARequestHandler`, sends one in-process message, and creates an `A2AClientFactory` client for a JSON-RPC card without calling it.
- `eng/check-core-only.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
out=$(mktemp -d)
dotnet publish tests/Ambinder.A2A.CoreOnlyApp -c Release -o "$out"
bad=$(ls "$out" | grep -E '^(Microsoft\.AspNetCore\.|Grpc\.|Google\.Protobuf|Azure\.)' || true)
if [ -n "$bad" ]; then echo "Core-only app pulled host DLLs:"; echo "$bad"; exit 1; fi
dotnet "$out/Ambinder.A2A.CoreOnlyApp.dll"
```

- The `pack` job (Ubuntu, every PR, `permissions: contents: read`):
  1. checkout with `fetch-depth: 0`
  2. setup-dotnet
  3. `eng/pack.sh`, which runs the content and allow-list tests
  4. `eng/check-core-only.sh`
  5. install the `sourcelink` tool, unzip every `.snupkg` and run `sourcelink test` on each `.pdb`
  6. write a job summary table with each package's ID, size and dependency count (01 §8.4; package 14 builds on this)
  7. upload `artifacts/packages` as a workflow artifact named `packages`

- [ ] **Step 1:** Write the app and the script. Run the script locally. Expected: the app prints the reply, and the script passes.
- [ ] **Step 2: Prove the gate.** Add a `FrameworkReference Microsoft.AspNetCore.App` to `Ambinder.A2A.Server`. The script fails, and so does Task 21's test. Revert.
- [ ] **Step 3:** Add the job, push, and check it runs green on the PR.
- [ ] **Step 4:** Commit: `ci: pack job with package content, allow-list, core-only and SourceLink checks`.

---

### Task 23: Samples on packages

**Files:** `samples/Directory.Build.targets`, `eng/samples-feed/NuGet.config`, `ci.yml` job `samples-on-packages`.

`samples/Directory.Build.targets`:

```xml
<Project>
  <!-- With -p:A2AUsePackages=true, samples use the packed Ambinder.A2A.* packages from artifacts/packages instead of src/ projects. -->
  <ItemGroup Condition="'$(A2AUsePackages)' == 'true'">
    <_A2ASrcRef Include="@(ProjectReference)" Condition="$([System.String]::Copy('%(Identity)').Replace('\','/').Contains('/src/Ambinder.A2A.'))" />
    <PackageReference Include="@(_A2ASrcRef->'%(Filename)')" VersionOverride="$(A2APackageVersion)" />
    <ProjectReference Remove="@(_A2ASrcRef)" />
  </ItemGroup>
</Project>
```

If the item transform does not evaluate as written, write the logic as a `Target` that runs `BeforeTargets="CollectPackageReferences"`. The behavior is what matters: no `src/` project reference is left, and every one becomes a package reference at `$(A2APackageVersion)`.

`eng/samples-feed/NuGet.config` has two sources: `../../artifacts/packages` (`local`) and nuget.org. Package source mapping maps `Ambinder.A2A.*` to `local` only.

The job runs after `pack` (`needs: pack`): it downloads the `packages` artifact and reads the version from a package file name. Then, for each sample a guide uses (`GettingStarted.Agent`, `GettingStarted.Client`, `AgentServer`, `AgentClient`), it runs:

```bash
dotnet build samples/<X> -c Release -p:A2AUsePackages=true -p:A2APackageVersion=<v> --configfile eng/samples-feed/NuGet.config
```

Then it runs the getting-started agent and client against each other (agent on port 0, or a fixed port inside the CI runner; this is not a test, so B2 does not apply).

- [ ] **Step 1:** Write the files. Locally, run `eng/pack.sh` and then the build commands. Expected: they build, and the client prints "hello".
- [ ] **Step 2: Prove the gate.** Remove `README.md` from a project's pack items and repack. Task 14's test fails. Revert.
- [ ] **Step 3:** Commit: `ci: build the guide samples against the packed packages`.

---

### Task 24: Coexistence project (R1)

**Files:** `tests/Ambinder.A2A.Coexistence/Ambinder.A2A.Coexistence.csproj` and `Program.cs`, `Directory.Packages.props` (the official `A2A` package).

- Pin `A2A` at the newest available preview (the spec names 1.0.0-preview4). Check nuget.org at execution time.
- The project references that package plus `src/Ambinder.A2A.Protocol`, `.Client` and `.Server`. `Program.cs` uses both `AgentCard` types:

```csharp
using Official = A2A;
using Ours = Ambinder.A2A.Protocol;

var theirs = new Official.AgentCard { Name = "official" };
var ours = new Ours.AgentCard { Name = "ours" };
Console.WriteLine($"{theirs.Name} {ours.Name}");
```

  Adjust the member names to the official package's actual API at the pinned version. The project must only compile.
- It is in `Ambinder.A2A.slnx`, so the normal build compiles it. It is `IsPackable=false` through `tests/Directory.Build.props`.
- If the official package's own dependencies trigger NuGet audit (Task 20), add a project-level `NuGetAuditSuppress` for that advisory with a comment and an expiry date (01 "Left to the builder").

- [ ] **Step 1:** Add the project and build. Expected: it compiles.
- [ ] **Step 2:** Commit: `test: compile the official A2A package next to Ambinder.A2A`.

---

### Task 25: Reproducibility job and Windows leg

- **`reproducibility` job** (Ubuntu, every PR):
  - check out the commit twice, into `a/` and `b/`, each with `fetch-depth: 0`
  - run `dotnet build -c Release -p:ContinuousIntegrationBuild=true` in each
  - compare the SHA-256 of every `src/*/bin/Release/net10.0/Ambinder.A2A.*.dll` between `a/` and `b/`
  - fail and list the differing files on any mismatch (01 Goal 6)
  - this compares DLLs only, never `.nupkg` files
- **Windows leg (C1).** A new workflow `.github/workflows/nightly.yml`:
  - triggers: `schedule` daily, `workflow_dispatch` and `workflow_call`
  - job `windows-unit`: `windows-latest`, `dotnet test` on `tests/Ambinder.A2A.Protocol.Tests`, `tests/Ambinder.A2A.Server.Tests` and the client unit tests (`tests/Ambinder.A2A.Client.Tests`, if it exists; else skip that path)
  - job `strict-audit`: Ubuntu, `dotnet restore Ambinder.A2A.slnx -p:A2AStrictAudit=true`
  - Packages 05 and 12 add jobs to this file. Their plans say "if it exists, else create it".

- [ ] **Step 1:** Write the job and the workflow.
- [ ] **Step 2:** Run the nightly workflow once with `workflow_dispatch` from the PR branch, if the repo allows dispatch on non-default branches. Otherwise run it once after merge.
- [ ] **Step 3:** Commit: `ci: reproducibility check and nightly Windows and strict audit jobs`.

---

### Task 26: Hook jobs; PR D check and PR

**Files:** `ci.yml`. It also becomes reusable: add `workflow_call:` to its `on:`, so `release.yml` can call it.

- The hook jobs are `tck` (package 03), `interop` (package 16) and `perf` (package 14). Each runs on Ubuntu with one step: `echo "::notice::Not implemented yet. Package NN fills this job."`. Packages 03, 14 and 16 replace the body with a call to their own reusable workflows.
- `ci.yml` must still have the `build-and-test` job, by that name, on `ubuntu-latest` (02's A10, 12).
- `concurrency`: keep `cancel-in-progress: true` for pull requests. For `push` to `master`, set `cancel-in-progress: false` instead (`cancel-in-progress: ${{ github.event_name == 'pull_request' }}`). On 2026-11-03, quick doc merges cancelled most `master` runs, so most `master` commits that day had no finished CI result. This is a **plan's call**, and a small one.
- Third-party actions pinned by SHA. The `actions/*` ones keep their major tags.

- [ ] **Step 1:** Add the jobs. Push. Expected, on the PR: all jobs green, and the hook jobs show their notice.
- [ ] **Step 2:** Open PR D. The body links one green run and lists the jobs.
- [ ] **Step 3:** Commit: `ci: hook jobs for TCK, interop and perf; keep master runs`.

---

### Task 27: `release.yml`

**PR E** starts on `feat/sdk-01-release` from `master` after PR D merges.

**Files:** `.github/workflows/release.yml`.

Triggers: `push` of tags `v*`; `workflow_dispatch` with inputs `tag` (string) and `dry_run` (boolean, default `true`).

The workflow has nine jobs. Each `needs:` the one before it. Top-level `permissions: contents: read`; each job widens only what it needs.

1. **`validate`**
   - The tag matches `^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$`.
   - The tagged commit is an ancestor of `origin/master`: `git merge-base --is-ancestor $GITHUB_SHA origin/master`, after `fetch-depth: 0`.
   - `CHANGELOG.md` has a `## [X.Y.Z…]` section for this version.
   - It outputs `version` and `prerelease` (true when the version has a `-` part).
2. **`verify`**: `uses: ./.github/workflows/ci.yml` (`workflow_call`) with `secrets: inherit`, then `uses: ./.github/workflows/nightly.yml` (Windows leg and strict audit). The hook jobs are part of `ci.yml`.
3. **`pack`**: `eng/pack.sh` (content and allow-list tests), `eng/check-core-only.sh`, `sourcelink test`. Then `dotnet pack` runs with `-p:A2AStrictAudit=true`. Upload `packages`.
4. **`sign`**:
   - If `vars.SIGN_PACKAGES == 'true'`: install the `sign` CLI and sign every `.nupkg` with Azure Trusted Signing, using `azure/login` with OIDC (`id-token: write`). The endpoint, account and profile come from repo variables. Then run `dotnet nuget verify --all` on the output.
   - Else, if `prerelease == 'true'`: print `::notice::Unsigned preview (owner decision O4)` and continue.
   - Else (stable, signing off): **fail** with "A stable release must be signed. Set SIGN_PACKAGES=true after owner action 6."
5. **`attest`**:
   - Generate an SBOM over `artifacts/packages`. Use `anchore/sbom-action` (pinned SHA) in SPDX JSON, or `Microsoft.Sbom.Targets`; builder's choice.
   - `actions/attest-build-provenance` on `artifacts/packages/*.nupkg` (`id-token: write`, `attestations: write`).
   - Upload the SBOM.
6. **`publish`**:
   - `environment: nuget-release` (the required reviewer is the owner, O2).
   - Skipped when `dry_run`.
   - Push with `NuGet/login` (trusted publishing, pinned SHA; `id-token: write`), then `dotnet nuget push artifacts/packages/*.nupkg --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate`.
   - `.snupkg` files go along automatically when they sit next to their `.nupkg`. Check this, else push them explicitly.
   - Fallback when trusted publishing is not available: the environment secret `NUGET_API_KEY`.
7. **`github-release`** (`contents: write`; skipped when `dry_run`):
   - The body is the `CHANGELOG` section, cut with `awk` between this version's heading and the next `## [`.
   - Mark it pre-release when `prerelease`.
   - Attach the packages, the SBOM and the attestation bundle.
   - Package 14 adds `perf-results.zip` to the attach list.
8. **`smoke`** (skipped when `dry_run`):
   - Poll `https://api.nuget.org/v3-flatcontainer/ambinder.a2a.aspnetcore/index.json` until the version appears, up to 20 minutes.
   - Then, in a temp folder: `dotnet new web`; `dotnet add package Ambinder.A2A.AspNetCore --version $v`; copy `samples/GettingStarted.Agent/Program.cs`; run it in the background.
   - Then `dotnet new console`; `dotnet add package Ambinder.A2A.Client --version $v`; copy `samples/GettingStarted.Client/Program.cs`; run it; assert the output contains "hello".
   - On failure: open an issue with `gh issue create` (`issues: write`) titled "Release smoke failed for v$v", with a link to the run, and fail.
9. **`docs`**: hook job, `echo "::notice::Not implemented yet. Package 13 fills this job."`. Package 13 fills it.

Fork safety: `release.yml` never runs on `pull_request`, so it is never exposed to fork PRs. The `nuget-release` environment holds the only secret.

- [ ] **Step 1:** Write the workflow.
- [ ] **Step 2:** Lint it with `actionlint`, if available.
- [ ] **Step 3:** Commit: `ci: tag-driven release pipeline with approval, signing, attestation and smoke test`.

---

### Task 28: Release dry run and runbook

- [ ] **Step 1:** On the PR branch, run `release.yml` with `workflow_dispatch`, `tag=v1.0.0-preview.0`, `dry_run=true`. Pushing a real tag is an owner decision, so do not push one. Expected: `validate` fails, because the commit is not on `master` and there is no CHANGELOG section. That proves the refusal. Record it.
- [ ] **Step 2:** After PR E merges, the **owner** runs the same dispatch on `master` with a `CHANGELOG` section `## [1.0.0-preview.0]` added in a throwaway branch, or accepts the step 1 evidence. A full rehearsal on a test fork (spec acceptance) is an owner action. List it in Task 29.
- [ ] **Step 3:** Add the runbook to `CONTRIBUTING.md` (01 §7, last paragraph):
  - merge to `master`, update `CHANGELOG`, tag `vX.Y.Z[-pre]`, push the tag, approve in `nuget-release`
  - if smoke fails: unlist the bad version (`dotnet nuget delete Ambinder.A2A.X <v> --non-interactive` unlists on nuget.org), fix it, and tag the next number
  - never re-tag
- [ ] **Step 4:** Commit: `docs: release runbook`.

---

### Task 29: Owner actions checklist

These cannot be done by code (01 §8). Put this list in PR E's description as a checklist, and in a new issue titled "Release setup (package 01 owner actions)". Creating the issue is outward-facing: ask the owner first, or leave the text in the PR description only.

1. Check that every ID in the package map is free on nuget.org. Do this before the first push; if one is taken, change the map first.
2. Choose the nuget.org account (an organization account is better).
3. Apply for the reserved ID prefix `Ambinder.`.
4. Done (O1): the repo stays at `roman-ambinder/a2a-starter`.
5. Create the GitHub Environment `nuget-release` with the owner as required reviewer. Add the trusted-publishing policy (`Ambinder.*`) or the `NUGET_API_KEY` secret.
6. Before `1.0.0`: set up Azure Trusted Signing, register the certificate on nuget.org, and set the repo variable `SIGN_PACKAGES=true` plus the signing variables (O4).
7. Turn on private vulnerability reporting, secret scanning and push protection. Set branch protection on `master`: required checks `build-and-test` and `pack`, and later the public API diff, TCK and interop; no force push; no second human review (O2).
8. Tag `phase-1` if it was not done in Task 1.
9. Optional: run a full release rehearsal on a test fork (spec acceptance).

---

### Task 30: Final check

- [ ] **Step 1:** Run each check in the "What the other plans expect from 01" table (F1–F15) and record pass or fail in PR E. These one-line checks cover most of it:

```bash
git grep -nE 'namespace A2A\.|<(Root|Assembly)Name>A2A\.' -- src tests samples || echo F1 ok
```

```bash
for p in Protocol Server AspNetCore AspNetCore.Grpc Client Client.Grpc Client.Entra; do test -f src/Ambinder.A2A.$p/Ambinder.A2A.$p.csproj || echo "missing $p"; done
```

```bash
test -d src/Grpc.Shared && test ! -d src/A2A.Protocol.Grpc && echo F4 ok
```

```bash
grep -q CS1591 src/Directory.Build.props && grep -q IsPackable tests/Directory.Build.props && grep -q IsPackable samples/Directory.Build.props && grep -q IsPackable benchmarks/Directory.Build.props && echo F7 ok
```

```bash
eng/pack.sh && eng/check-core-only.sh && echo F3 F8 ok
```

```bash
git grep -n '"Ambinder.A2A.Server"\|"Ambinder.A2A.Client"' -- src/Ambinder.A2A.Protocol/A2ATelemetryNames.cs && echo F9 ok
```

```bash
ls CONTRIBUTING.md CHANGELOG.md .github/CODEOWNERS .github/pull_request_template.md && grep -q 'PublicAPI' .github/CODEOWNERS && echo F10 ok
```

```bash
grep -nE '^  (build-and-test|pack|samples-on-packages|reproducibility|tck|interop|perf):' .github/workflows/ci.yml
```

```bash
grep -n 'docs:' .github/workflows/release.yml
```

```bash
grep -nE 'TestResults/|\*\.nupkg|\*\.snupkg|artifacts/|BenchmarkDotNet.Artifacts/' .gitignore
```

```bash
git ls-remote --tags origin phase-1
```

```bash
ls samples/GettingStarted.Agent samples/GettingStarted.Client
```

- [ ] **Step 2:** Map every spec 01 acceptance checkbox to a test, job or owner action. Two items are M2 and get "M2" with the job name: the AOT smoke job and Windows-leg-on-release. The Windows leg already runs in `verify`.
- [ ] **Step 3:** If an F row fails, fix it in PR E. If it needs another PR, say so in the PR description.

---

## Notes for the owner (found while planning)

1. **The split forces three user-visible changes** (Task 11). Each change uses the name the later package plans already use, so code written now keeps compiling when they land:
   - `.AddA2AGrpc()` before `MapA2AGrpc()`
   - `.AddGrpcTransport()` on client options for gRPC
   - the unknown-policy check now throws from `MapA2A()`, not from `StartAsync`
2. **`AddA2AServer` stays in `Ambinder.A2A.Server`, as the package map says.** The ASP.NET Core pieces it used to add (authorization services, the authorizer, the startup filter) can no longer be added from the core. So:
   - an agent with a policy relies on the app's own `AddAuthorization(...)` call, which an app that defines policies already makes
   - the default authorizer is created on demand
   - the core checks run in an `IHostedLifecycleService` before the server listens

   The alternative was moving `AddA2AServer` to `Ambinder.A2A.AspNetCore` and adding `AddA2AServerCore` to the core. That would change the map. This plan does not choose it.
3. **gRPC tests compile their own copy of the proto** (Task 10), instead of using `InternalsVisibleTo` plus `extern alias`. No shipped gRPC assembly exposes internals to any test.
4. **`ServiceParameters` and `RequestValidation` become internal in PR B**, because they must be compiled into two assemblies. Package 02 planned to make them internal anyway.
5. **`Microsoft.IdentityModel.JsonWebTokens` stays a dependency of `Ambinder.A2A.Server`** until package 08 replaces `PushSigningKey`. It is listed in the allow-list with a note.
6. **CI on `master`:** Task 26 stops `master` pushes from cancelling each other. On 2026-11-03, many quick doc merges left most `master` commits without a finished CI run. This is a small change to existing CI; say if you prefer to keep cancelling.
7. **AOT flags in M1:** analyzer warnings that need real work are listed in `docs/aot-warnings.md` and allowed as warnings in that project only. They must be fixed before the M2 AOT smoke job.
8. **Owner actions** (Task 29): the `phase-1` tag, the nuget.org account and prefix, the `nuget-release` environment, signing before `1.0.0`, and the repo security settings.

---

## Execution notes

- **How to run:** use superpowers:subagent-driven-development. One implementer subagent per task, then a spec-compliance review and a code-quality review per task, as that skill describes.
- **Model:** every subagent (implementers and reviewers) runs on model `sonnet`. Pass `model: "sonnet"` on each subagent call. Planning was done on Opus. Execution is on Sonnet, by owner rule.
- **Order:**
  - Tasks in number order.
  - Tasks 6, 7, 8, 9 and 11 must be implemented together before the solution builds again. Give them to **one** implementer as one unit, committed as separate commits at the end. Review them as one unit.
  - Do not run two implementers at the same time in PRs A and B: almost every file moves.
  - In PRs C and D, Tasks 15–18 and 23–25 can run in parallel (they touch different files).
- **Branches and PRs:**
  - A `feat/sdk-01-rename`, B `feat/sdk-01-split`, C `feat/sdk-01-packaging`, D `feat/sdk-01-ci`, E `feat/sdk-01-release`.
  - Each branch is created from `master` after the previous PR merges. Each PR builds and passes all tests on its own.
- **Context for each subagent:** give it this plan's task text, the "Facts about the current code", "Global constraints" and "Review focus" sections, and the spec 01 section the task cites. Do not paste whole specs.
- **Stop conditions:**
  - a golden file must change in PR A
  - the rule 4 fallback seems needed (Task 6)
  - a shipped package needs a dependency the map does not list
  - a forced change beyond Task 11's list
  - an outward action (tag, issue, release, environment) without the owner's go

  Report and wait. Do not work around it.
- **After 01 merges:** the other package plans start with their own Task 0 against the F1–F15 facts above.
