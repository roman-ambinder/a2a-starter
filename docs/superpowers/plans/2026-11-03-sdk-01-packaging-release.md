# SDK 01 (Naming, packaging and release): implementation plan

Date: 2026-11-03
Spec: [`2026-11-03-sdk-01-packaging-release-design.md`](../specs/2026-11-03-sdk-01-packaging-release-design.md) (approved; the authority)
Work plan: [`2026-11-03-sdk-00-work-plan.md`](../specs/2026-11-03-sdk-00-work-plan.md)
Milestone: M1

> For the executor: run this plan task by task. Each task is done by one implementer subagent, then checked by a reviewer
> subagent. Each task ends with a green `dotnet build -c Release` (0 warnings) and a green full test run, and with its own
> commits. Do not start a task before the previous one is reviewed.

## Goal

Turn the Phase 1 reference repo into the `Ambinder.A2A.*` SDK layout of spec 01 for M1: the rename, the package split of the
package map (only the rows that Phase 1 code fills), NuGet metadata, license and community files, deterministic and
debuggable builds, the CI additions and the tag-driven release workflow. No wire or behavior change.

## Architecture after this plan

```
src/
  Ambinder.A2A.Protocol/            models, JSON, errors, telemetry names           deps: none
  Ambinder.A2A.Server/              core: agents, tasks, push, auth contracts, DI   deps: Protocol, Microsoft.Extensions.*, Microsoft.IdentityModel.JsonWebTokens (push signing, until 08)
  Ambinder.A2A.AspNetCore/          JSON-RPC, REST, SSE, card endpoints, MapA2A,    deps: Server, FrameworkReference Microsoft.AspNetCore.App
                                    AddA2AServer, AgentAuthorizer, startup checks, ConfigureA2AKestrel
  Ambinder.A2A.AspNetCore.Grpc/     gRPC service, AddA2AGrpc, MapA2AGrpc            deps: AspNetCore, Grpc.AspNetCore, Google.Protobuf, Grpc.StatusProto, Google.Api.CommonProtos
  Ambinder.A2A.Client/              IA2AClient, factory, JSON-RPC + REST, SSE,      deps: Protocol
                                    tracing, card resolver, credential providers, IA2AClientTransport seam
  Ambinder.A2A.Client.Grpc/         gRPC transport                                  deps: Client, Grpc.Net.Client, Google.Protobuf, Grpc.StatusProto, Google.Api.CommonProtos
  Ambinder.A2A.Client.Entra/        AzureIdentityCredentialProvider                 deps: Client, Azure.Identity
  Grpc.Shared/                      ProtoMapper*.cs, linked into both gRPC packages and compiled internal (rule 4)
  Shared/                           small helpers linked into several packages, compiled internal (rule 10)
tests/   Ambinder.A2A.<X>.Tests, Ambinder.A2A.Tck.Sut, Ambinder.A2A.Coexistence, Ambinder.A2A.CoreOnly.Smoke
samples/ folders keep short names; assemblies Ambinder.A2A.Samples.<Name>; new GettingStarted.Agent and GettingStarted.Client
eng/     icon.png, package-dependencies.json, check-packages.cs, samples-feed/NuGet.config, scripts
```

The rows of the package map that later packages create (Stores.*, Azure.KeyVault, AspNetCore.Entra, Push.Receiver, Cluster*,
Extensions.AI*, Compat.*, Testing*) are not created here. `eng/package-dependencies.json` lists the packages that exist; each
later package adds its own entry (spec 01, Dependencies).

## Builder decisions (spec "Left to the builder", or needed to meet a rule)

These are not owner decisions. Each one is listed in the PR for the owner to see.

1. **`AddA2AServer` lives in `Ambinder.A2A.AspNetCore`; the core has `AddA2AServerCore`.** Today `AddA2AServer` registers ASP.NET
   Core authorization, `AgentAuthorizer` (over `IAuthorizationService`), an `IStartupFilter` and `AddGrpc()`. Rule 2 forbids
   ASP.NET Core and gRPC in the core, and the startup tests require the check to run at `StartAsync`, so those registrations
   cannot move into `MapA2A`. Following the ASP.NET convention (`AddMvcCore`/`AddMvc`), `Ambinder.A2A.Server` gets
   `AddA2AServerCore(...)` (everything host-neutral, returns `A2AServerBuilder`), and `Ambinder.A2A.AspNetCore` gets
   `AddA2AServer(...)` with the same signature as today, which calls the core and adds the ASP.NET Core parts. A server app
   still writes `AddA2AServer(...)`. The spec 01 map row puts `AddA2AServer` in Server; the PR flags this for the owner.
   Package 09 may revisit.
2. **gRPC is opt-in through `AddA2AGrpc()`** (named by spec 09 §7.1) on `A2AServerBuilder`, in `Ambinder.A2A.AspNetCore.Grpc`.
   `AddA2AServer` no longer calls `AddGrpc()`. `ConfigureA2AKestrel` (it only sets Kestrel listeners) is a hosting helper in
   `Ambinder.A2A.AspNetCore`.
3. **Client transport seam.** `A2AClientFactory` (core) builds JSON-RPC and HTTP+JSON clients. Other bindings come from
   `A2AClientOptions.Transports` (`IReadOnlyList<IA2AClientTransport>`, default empty). `Ambinder.A2A.Client.Grpc` provides
   `GrpcClientTransport` (`GrpcClientTransport.Instance`). Selection rules stay the same, but a binding counts as supported only
   when it is built in or in `Transports`. A card with only a gRPC interface and no gRPC transport throws
   `NotSupportedException` whose message names `Ambinder.A2A.Client.Grpc` (rule 5; package 10 owns the final text and may replace
   the seam). Minimal interface:
   ```csharp
   public interface IA2AClientTransport
   {
       string ProtocolBinding { get; }          // "GRPC"
       string TransportName { get; }            // telemetry value, A2ATelemetryNames.TransportGrpc
       IA2AClient Create(AgentInterface agentInterface, AgentCard card, A2AClientOptions options, string? tenant);
   }
   ```
   `Create` throws `NotSupportedException` for an invalid URL, with the same message the factory gives today.
4. **Generated protobuf code is `internal`** in both gRPC assemblies (`<Protobuf Access="Internal">`), required by rule 4 because
   both assemblies compile the same proto. `AspNetCore.Grpc` generates `GrpcServices="Server"`, `Client.Grpc` `GrpcServices="Client"`.
5. **Tests and internals.** `InternalsVisibleTo` only to test assemblies (rule 6). A test assembly may see the internal proto types
   of at most one gRPC assembly (otherwise `Lf.A2a.V1.*` is ambiguous). `Protocol.Tests` compiles `src/Grpc.Shared/*.cs` and the
   proto itself (no IVT). Tests that need `Client.Grpc` internals move to a new `tests/Ambinder.A2A.Client.Grpc.Tests`.
6. **No new public API to work around the split.** An internal member used across a new assembly boundary is handled by a linked
   source file in `src/Shared/` (compiled internal) or a local equivalent; it is not made public. Exceptions need the
   controller's approval and are listed in the PR.
7. **Telemetry names.** One server source/meter (`Ambinder.A2A.Server`) for every server package, one client source/meter
   (`Ambinder.A2A.Client`) for every client package, as in `A2ATelemetryNames`. No new source names.
8. **XML docs.** `GenerateDocumentationFile=true` for shipped projects with `CS1591` suppressed (one `NoWarn` line with a comment);
   package 02 owns doc enforcement.
9. **Dependency floors** (rule 7): `Directory.Packages.props` keeps the newest versions (tests and samples); shipped projects use
   `VersionOverride` with the lowest .NET 10 version (`10.0.0`) for `Microsoft.Extensions.*`; third-party packages keep the
   central version. `CentralPackageTransitivePinningEnabled=true`.
10. **MinVer** 8.0.0. `MinVerSkip=true` when the repo has no `.git` (Docker builds), so the build does not warn.
11. **SBOM**: `CycloneDX` .NET tool (6.3.1), pinned; **attestation**: `actions/attest-build-provenance@v4`; **trusted publishing**:
    `NuGet/login` pinned to `8d196754b4036150537f80ac539e15c2f1028841` (v1.2.0). `actions/*` use major tags (spec: only third-party
    actions are SHA-pinned).
12. **Package checks** are one .NET 10 file-based app, `eng/check-packages.cs`, run with `dotnet run eng/check-packages.cs -- <dir>`.
13. **Docs history.** `docs/superpowers/**` (specs, research, this plan) is history and keeps the old names, apart from the
    one-line note the spec asks for at the top of the two Phase 1 specs.
14. **`phase-1` tag**: the controller does not create tags. The tag on `6e0ca89` (last Phase 1 commit on `master`) is an owner action.

## Environment notes for subagents

- The .NET SDK is at `$HOME/.dotnet`. Prefix every shell with
  `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`. Do not edit shell profiles.
- Full check: `dotnet build <sln> -c Release` must end with `0 Warning(s)` and `0 Error(s)`; then
  `dotnet test --solution <sln> -c Release --no-build`. Baseline before this plan: 858 tests, all green.
- Docker is not available; Dockerfiles are checked by reading them.
- Commit on branch `claude/admiring-turing-74whwp` with small logical commits. Do not push, tag or publish.

---

## Task 1: Rename to `Ambinder.A2A.*` (no split, no behavior change)

Spec §3 steps 1–3, 6, 7 and §2. This commit set must build and pass all 858 tests with only name changes.

Files: every `src/`, `tests/` project and folder; `A2A.Reference.slnx` → `Ambinder.A2A.slnx`; `Directory.Packages.props`
(comments only, if any); `.github/workflows/ci.yml`; `.github/dependabot.yml`; `.dockerignore`; `docker/Dockerfile.*`;
`docker-compose.yml`; `run-all.sh`; `run-all.ps1`; `samples/AppHost`; `README.md`; per-project READMEs; `docs/*.md`
(not `docs/superpowers/**`); `.gitignore`; the two Phase 1 specs.

Steps:

1. `git mv` each project folder and `.csproj`:
   `src/A2A.Protocol` → `src/Ambinder.A2A.Protocol`, `src/A2A.Protocol.Grpc` → `src/Ambinder.A2A.Protocol.Grpc` (temporary; removed
   in Task 3), `src/A2A.Server` → `src/Ambinder.A2A.Server`, `src/A2A.Client` → `src/Ambinder.A2A.Client`,
   `tests/A2A.X.Tests` → `tests/Ambinder.A2A.X.Tests` (Protocol, Server, Integration, MockEntra), `tests/A2A.Tck.Sut` →
   `tests/Ambinder.A2A.Tck.Sut`. `git mv A2A.Reference.slnx Ambinder.A2A.slnx` and fix every path in it.
2. Replace `namespace A2A.` → `namespace Ambinder.A2A.` and `using A2A.` → `using Ambinder.A2A.` (also `using static`, aliases,
   `global using`, `<Using Include="A2A...">`). Fix every fully qualified `A2A.X` name the compiler reports, `cref`s and
   `nameof`. Do not touch wire names (`A2A-Version`, `A2A-Extensions`, `a2a.*` span/metric/attribute names, `lf.a2a.v1`), the
   `A2A:` configuration prefix or environment variables (spec §2).
3. Samples: folders and project files keep their names. Add `samples/Directory.Build.props` (imports the root one with
   `GetPathOfFileAbove`) with `<AssemblyName>Ambinder.A2A.Samples.$(MSBuildProjectName)</AssemblyName>` and
   `<IsPackable>false</IsPackable>`. Keep sample namespaces and `RootNamespace` as they are (set `RootNamespace` to
   `$(MSBuildProjectName)` so it does not follow the new assembly name). Check `samples/AppHost` still finds the projects
   (`Projects.AgentServer` etc. come from project file names). Update `ENTRYPOINT` dll names in the four Dockerfiles.
4. Update `InternalsVisibleTo` names, project references, `.dockerignore`, Dockerfile `COPY` lines (new folders, new slnx),
   `docker-compose.yml`, `run-all.sh`, `run-all.ps1`, `ci.yml` (`Ambinder.A2A.slnx`), the `DocsTests` solution lookup
   (`tests/.../Repo/DocsTests.cs`, also its `tests/A2A.Integration.Tests/Flows` path), README tables and per-project READMEs,
   `docs/*.md` (top-level docs only).
5. `.gitignore`: add `TestResults/`, `*.nupkg`, `*.snupkg`, `artifacts/`, `BenchmarkDotNet.Artifacts/`.
6. Add as the first line under the title of `docs/superpowers/specs/2026-11-02-a2a-dotnet-reference-design.md` and
   `docs/superpowers/specs/2026-11-03-observability-design.md`: `> Written before the rename to Ambinder.A2A.*`.
7. Build and test. Commit: `refactor: rename projects and namespaces to Ambinder.A2A.*`.
8. Telemetry (spec §2): `A2ATelemetryNames` source and meter names `A2A.Server` → `Ambinder.A2A.Server`, `A2A.Client` →
   `Ambinder.A2A.Client` (and any other `A2A.<Assembly>` source/meter name, for example a protocol meter, follows its assembly).
   Span, attribute and metric names do not change. Update `docs/production.md` §6, sample telemetry setup (`AddSource`/`AddMeter`
   with literal names, if any), README. Build and test. Commit: `refactor: rename telemetry source and meter names`.

Verify:

- `git grep -nE 'namespace A2A\.|<(Root|Assembly)Name>A2A\.'` → nothing.
- `git grep -nE '\bA2A\.(Protocol|Server|Client|Integration|MockEntra|Tck|Reference)\b' -- ':!docs/superpowers'` → nothing
  (wire/config names such as `A2A:PublicBaseUrl` do not match this pattern).
- `find . -path ./node_modules -prune -o -name 'A2A.*.dll' -print | grep -v '/Ambinder\.'` after a build → nothing.
- Build 0 warnings; 858 tests green. Golden JSON and proto tests unchanged (`git diff --stat` shows no change under their data files).

## Task 2: Split the server: `Server` (core), `AspNetCore`, `AspNetCore.Grpc`

Spec §1 rules 2–6, 10; builder decisions 1, 2, 4–7. `Ambinder.A2A.Protocol.Grpc` still exists in this task (the client still uses it).

Steps:

1. Create `src/Grpc.Shared/` and `git mv` the `ProtoMapper*.cs` files there from `src/Ambinder.A2A.Protocol.Grpc/`. Make
   `ProtoMapper` `internal`. `Ambinder.A2A.Protocol.Grpc` compiles them as linked files for now (it stays public until Task 3 — if
   `ProtoMapper` being internal breaks the client, keep it public in this task only and make it internal in Task 3).
2. Create `src/Ambinder.A2A.AspNetCore` (FrameworkReference `Microsoft.AspNetCore.App`, ProjectReference Server). `git mv` into it
   (namespaces become `Ambinder.A2A.AspNetCore[.Sub]`, keeping the sub-namespace): `Transports/` except `Transports/Grpc`,
   `Hosting/A2AEndpointRouteBuilderExtensions.cs`, `Hosting/AgentCardEndpoints.cs`, `Hosting/A2AStartupValidation.cs`,
   `ConfigureA2AKestrel` (split out of `GrpcHostingExtensions.cs`), `Auth/AgentAuthorizer.cs`. Add `AddA2AServer` (same
   signature and XML doc as today minus gRPC) that calls `AddA2AServerCore`, then `AddAuthorization()`,
   `TryAddSingleton<IAgentAuthorizer, AgentAuthorizer>()`, and the `IStartupFilter`.
3. Create `src/Ambinder.A2A.AspNetCore.Grpc` (ProjectReference AspNetCore; `Grpc.AspNetCore`, `Google.Protobuf`,
   `Grpc.StatusProto`, `Google.Api.CommonProtos`, `Grpc.Tools` PrivateAssets; `IncludeGoogleApiCommonProtos=true`;
   `<Protobuf Include="../../proto/a2a.proto" ProtoRoot="../../proto" GrpcServices="Server" Access="Internal" />`; linked
   `../Grpc.Shared/*.cs`). `git mv` `Transports/Grpc/*` and `MapA2AGrpc` into it. Add `AddA2AGrpc(this A2AServerBuilder)`
   (calls `Services.AddGrpc()`, returns the builder). Namespace `Ambinder.A2A.AspNetCore.Grpc`.
4. `src/Ambinder.A2A.Server`: remove the FrameworkReference and gRPC references. Add PackageReferences (with
   `VersionOverride="10.0.0"` where decision 9 applies; add central `PackageVersion` entries at the newest 10.0.x patch for
   any new package) for what the remaining code uses: `Microsoft.Extensions.DependencyInjection.Abstractions`,
   `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`,
   `Microsoft.Extensions.Http`, `Microsoft.IdentityModel.JsonWebTokens` (push signing; no floor change, keep central version).
   Rename `AddA2AServer` in the core to `AddA2AServerCore`; it keeps every registration that needs no ASP.NET Core or gRPC.
   Fix cross-assembly internals per decision 6 (`src/Shared/*.cs` linked files, `internal`).
5. Wire the new projects into the slnx, tests, samples (AgentServer, Tck.Sut, MinimalServer fixture etc. now call
   `.AddA2AGrpc()` where they call `MapA2AGrpc()`), Dockerfile `COPY` lines, README tables. Test projects: Server.Tests keeps
   IVT from Server; add IVT to test assemblies that need the moved internals (prefer the matching one).
6. Build, test, commit: `refactor: split ASP.NET Core and gRPC hosting out of Ambinder.A2A.Server`.

Verify: `Ambinder.A2A.Server.csproj` has no FrameworkReference, no `Grpc.*`/`Google.*`/`Azure.*` reference; build 0 warnings;
all tests green; test changes are limited to `using` lines, project references, and the added `AddA2AGrpc()` calls (any other
change is reported to the controller).

## Task 3: Split the client: `Client`, `Client.Grpc`, `Client.Entra`; remove `Protocol.Grpc`

Builder decisions 3–6.

Steps:

1. Add `IA2AClientTransport` and `A2AClientOptions.Transports` (decision 3) to `Ambinder.A2A.Client`, with XML docs. Change
   `A2AClientFactory` to use built-in JSON-RPC/REST plus `options.Transports`. Keep every existing selection rule and message;
   add the "add the `Ambinder.A2A.Client.Grpc` package" hint when a card lists a binding that is known (`GRPC`) but not registered.
2. Create `src/Ambinder.A2A.Client.Grpc` (ProjectReference Client; `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.StatusProto`,
   `Google.Api.CommonProtos`, `Grpc.Tools`; proto `GrpcServices="Client" Access="Internal"`; linked `../Grpc.Shared/*.cs`).
   `git mv` `GrpcA2AClient`, `GrpcErrorTranslator` into it; move `GrpcAddress` into the transport. Add `GrpcClientTransport`
   (public sealed, `Instance`). Namespace `Ambinder.A2A.Client.Grpc`. `ClientSupport` and other shared client internals: linked
   from `src/Shared/` per decision 6.
3. Create `src/Ambinder.A2A.Client.Entra` (ProjectReference Client; `Azure.Identity`). `git mv` `AzureIdentityCredentialProvider`.
   Namespace `Ambinder.A2A.Client.Entra`.
4. `Ambinder.A2A.Client`: no `Grpc.*`, `Google.*`, `Azure.*` references.
5. Delete `src/Ambinder.A2A.Protocol.Grpc`. `Protocol.Tests` compiles `src/Grpc.Shared/*.cs` and the proto (`GrpcServices="Both"`,
   `Access="Internal"`) itself. Integration tests: gRPC client creation passes `Transports = [GrpcClientTransport.Instance]` (a
   fixture helper is fine); the fake gRPC server in `ClientHardeningTests` gets proto types from IVT of `AspNetCore.Grpc` or its
   own compiled copy, per decision 5; tests of `Client.Grpc` internals move to `tests/Ambinder.A2A.Client.Grpc.Tests`.
   Samples (`AgentClient`) reference `Client.Grpc` and `Client.Entra` where they use them.
6. Update slnx, Dockerfiles, `run-all.*`, READMEs. Build, test, commit: `refactor: split gRPC and Azure.Identity out of Ambinder.A2A.Client`.

Verify: no project references `Ambinder.A2A.Protocol.Grpc`; `Lf.A2a.V1` types are internal in both gRPC assemblies (check with a
small reflection run or `ilspy`-free check: `typeof(...).Assembly.GetExportedTypes()` in a throwaway script, or grep the
generated `.cs` for `internal`); no `InternalsVisibleTo` between `src/` assemblies; all tests green; build 0 warnings.

## Task 4: Build props, NuGet metadata, SourceLink, MinVer, AOT flags, license files

Spec §4, §5 (LICENSE, NOTICE), Goals 4–6, 11, 13 (flags only).

Steps:

1. Root `Directory.Build.props`: keep the current five properties; add `Deterministic=true`, `ContinuousIntegrationBuild=true` when
   `$(GITHUB_ACTIONS)=='true'`, `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, `IsPackable=false` (default),
   `WarningsNotAsErrors` for `NU1901;NU1902` unless `$(A2AStrictAudit)=='true'` (then all advisories are errors) — this gives the
   C1 levels: PR builds fail on NU1903/NU1904, warn on NU1901/NU1902; nightly/release set `-p:A2AStrictAudit=true`.
   `Directory.Packages.props`: `CentralPackageTransitivePinningEnabled=true`; add `MinVer` 8.0.0.
2. `src/Directory.Build.props` (imports root): `IsPackable=true`; metadata table of spec §4 (`Authors`/`Copyright` "Roman Ambinder",
   `PackageLicenseExpression` Apache-2.0, one `A2ARepositoryUrl` property feeding `PackageProjectUrl` and `RepositoryUrl`
   `https://github.com/roman-ambinder/a2a-starter`, base `PackageTags`, `PackageReadmeFile` README.md, `PackageIcon` icon.png,
   `PackageReleaseNotes` link to `.../releases/tag/v$(Version)`, `NeutralLanguage` en, `PackageRequireLicenseAcceptance` false);
   `GenerateDocumentationFile=true` + `NoWarn CS1591` (decision 8); `IncludeSymbols`, `SymbolPackageFormat=snupkg`,
   `DebugType=portable`, `PublishRepositoryUrl`, `EmbedUntrackedSources`, `EnablePackageValidation=true`; MinVer reference
   (`PrivateAssets=all`), `MinVerTagPrefix=v`, `MinVerMinimumMajorMinor=1.0`, `MinVerDefaultPreReleaseIdentifiers=preview.0`,
   `MinVerSkip` when no `.git` (decision 10); pack items: each project's `README.md`, `$(RepoRoot)eng/icon.png`, `LICENSE`,
   `NOTICE` at the package root. `src/Grpc.Shared` and `src/Shared` are not projects.
3. `tests/Directory.Build.props`, `samples/Directory.Build.props` (from Task 1), `benchmarks/Directory.Build.props` (+ a two-line
   `benchmarks/README.md` naming package 14) and `eng/Directory.Build.props`: import root, `IsPackable=false`.
4. `IsAotCompatible=true` in Protocol, Server, AspNetCore, Client csproj (map "yes"); not in the gRPC and Entra packages. Fix the
   trim/AOT analyzer warnings this turns on without behavior change (today: `JsonSerializer` calls with `JsonSerializerOptions`
   → use `JsonTypeInfo<T>` from the source-generated context; `MakeReadOnly(populateMissingResolver: true)`; `ActivatorUtilities`
   with a `Type` → `[DynamicallyAccessedMembers(PublicConstructors)]` on the handler type parameter/property). If a fix would
   change public API or behavior, stop and report.
5. Per-project `Description` and package-specific `PackageTags` in each shipped csproj. Each shipped project has a `README.md`
   (~40 lines: what it is, install line, a short example, link to the repo README; rewrite the existing per-project READMEs
   where they exist, keep facts that are still true).
6. `eng/icon.png`: 128x128 PNG, simple generated artwork (for example two connected nodes, solid colors), committed as binary.
7. `LICENSE`: the Apache-2.0 text from `https://www.apache.org/licenses/LICENSE-2.0.txt` with the appendix copyright line
   filled: `Copyright (c) Roman Ambinder`. `NOTICE`: project name `Ambinder.A2A`, `Copyright (c) Roman Ambinder`, attribution for
   `proto/a2a.proto` (A2A project, Apache-2.0, URL and tag from `proto/README.md`).
8. Verify: build 0 warnings; tests green; `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages` gives exactly one
   `.nupkg` and one `.snupkg` per shipped project (7) and nothing else; unzip one and check README, icon, LICENSE, NOTICE, XML
   docs, `<license type="expression">Apache-2.0</license>`, `<repository type="git" url=... commit=...>`; version looks like
   `1.0.0-preview.0.N` (MinVer, untagged); `dotnet tool install` the `sourcelink` tool (3.1.1) into `eng/.tools` or the scratch
   dir and run `sourcelink test` on each `.pdb` from the `.snupkg` files (needs the commit to be on GitHub; if it is not pushed
   yet, run `sourcelink print-urls` and check the URLs point at `raw.githubusercontent.com/roman-ambinder/a2a-starter/<sha>/`).
   Two clean builds into two different clones give byte-identical shipped DLLs (with `-p:ContinuousIntegrationBuild=true`).
9. Commits: `build: shared props, NuGet metadata, SourceLink and MinVer`, `build: enable trim and AOT analyzers for core packages`,
   `docs: add LICENSE, NOTICE, package READMEs and icon`.

## Task 5: Community files

Spec §5. Files: `CHANGELOG.md` (Keep a Changelog; `## [Unreleased]` with `### Breaking changes` listing the rename, the split, the
telemetry names, `AddA2AGrpc`, `AddA2AServerCore`, client `Transports`; plus a link footer), `SECURITY.md`, `CONTRIBUTING.md`
(build/test, API-file workflow placeholder for package 02, perf rules placeholder for 14, commit/PR rules, one change per PR, DCO
`Signed-off-by`, no CLA, the release runbook of spec §7), `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1 text from
`https://raw.githubusercontent.com/EthicalSource/contributor_covenant/release/content/version/2/1/code_of_conduct.md`, contact =
GitHub private reporting / the owner's GitHub profile, no e-mail address invented), `.github/CODEOWNERS` (`@roman-ambinder` for
`**/PublicAPI.*.txt`, `.github/workflows/`, `eng/`, `proto/`), `.github/ISSUE_TEMPLATE/{bug_report,feature_request,question}.yml`
+ `config.yml`, `.github/pull_request_template.md` (checklist: tests, docs, CHANGELOG, API files reviewed, perf evidence).
Commit: `docs: add community files and GitHub templates`.

## Task 6: Package checks, coexistence, core-only smoke

Spec §6 (allow-list, coexistence), Goal 3, acceptance 4–5.

1. `eng/package-dependencies.json`: `{ "packages": { "<id>": { "dependencies": ["<id>", ...] } } }` for the 7 packages, with the
   exact dependency IDs of each nuspec (our packages by ID). Protocol has `[]`.
2. `eng/check-packages.cs` (file-based app; `#:property` lines as needed). Input: a folder with `.nupkg`/`.snupkg` and the JSON
   path. Fails (exit 1, one line per problem) when: the set of `.nupkg` IDs is not equal to the JSON keys; a `.snupkg` is missing;
   a nuspec dependency set differs from its JSON entry (added or removed); a core package (Protocol, Server, Client) has a
   dependency matching `Grpc.*`, `Google.*`, `Azure.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore*`, `Npgsql*`,
   `Microsoft.Data.*`, `Microsoft.Azure.*`, or a FrameworkReference (`<frameworkReferences>`); a package lacks README, icon,
   LICENSE, NOTICE, `lib/net10.0/<id>.dll`, `lib/net10.0/<id>.xml`, license expression `Apache-2.0`, or repository commit. It writes
   a Markdown table (ID, size KB, dependency count) to `$GITHUB_STEP_SUMMARY` when set, else stdout.
   Prove the failure path once by hand (edit the JSON, run, see the error, revert) and say so in the report.
3. `tests/Ambinder.A2A.Coexistence/`: console project, `IsPackable=false`, not run. References the official `A2A`
   `1.0.0-preview4` (central `PackageVersion`) and ProjectReferences Protocol, Client, Server. One `Program.cs` that uses both
   `AgentCard` types (`using OfficialA2A = A2A;`, full names) and compiles. Add to the slnx. If the official package's dependency
   graph causes a restore or audit error, report it (do not suppress).
4. `tests/Ambinder.A2A.CoreOnly.Smoke/`: console app referencing only Server and Client (ProjectReference), calls
   `AddA2AServerCore` and `A2AClientFactory` types so both assemblies are used. `eng/check-core-only.sh`: publishes it to a temp
   folder and fails if any `Microsoft.AspNetCore.*`, `Grpc.*`, `Google.Protobuf*`, `Azure.*` DLL is in the output or the
   `runtimeconfig.json` names `Microsoft.AspNetCore.App`.
5. Commits: `build: package allow-list and content checks`, `test: coexistence with the official A2A package`,
   `test: core packages publish without ASP.NET Core, gRPC or Azure`.

## Task 7: Getting-started samples and samples on packages

Spec Goals 8–9, §6 "Samples on packages", §7 step 8.

1. `samples/GettingStarted.Agent` (`Microsoft.NET.Sdk.Web`, ProjectReference AspNetCore): the hello agent, at most ~20 lines of
   `Program.cs` (one handler that replies with text, `AddA2AServer`, `MapA2A`). Listens on `A2A:PublicBaseUrl` or a default
   `http://localhost:5100`.
2. `samples/GettingStarted.Client` (console, ProjectReference Client): resolves the card from a URL argument (default
   `http://localhost:5100`), sends "hello", prints the reply text — the 5-line call.
3. A test in `Ambinder.A2A.Integration.Tests` (no fixed ports, no sleeps, per B2) that runs the agent in-process (`TestServer` or
   port 0) and checks the client logic gets the expected text. Keep it small.
4. `samples/Directory.Build.targets`: when `$(A2AUsePackages)=='true'`, remove `ProjectReference` items that point into `src/`
   and add `PackageReference`s for the same IDs at `$(A2APackageVersion)` (plus matching `PackageVersion` items for CPM).
   `eng/samples-feed/NuGet.config`: `artifacts/packages` as a local source plus nuget.org, `<clear/>` first.
   `eng/build-samples-on-packages.sh`: restores and builds GettingStarted.Agent, GettingStarted.Client, AgentServer and
   AgentClient with `-p:A2AUsePackages=true -p:A2APackageVersion=<version> --configfile eng/samples-feed/NuGet.config` into a
   separate `--packages` folder so the local feed is really used. Run it once locally after `dotnet pack`.
5. README "Getting started" section points at the two samples. Commit: `samples: getting-started agent and client; build samples on packages`.

## Task 8: CI and release workflows

Spec §6, §7, Goals 7, 12, acceptance 8–13.

1. `.github/workflows/ci.yml`: triggers `push` (master), `pull_request`, `schedule` (nightly), `workflow_dispatch`,
   `workflow_call` (input `strict-audit`, boolean). Top-level `permissions: contents: read`. Jobs (all `actions/checkout@v7` with
   `fetch-depth: 0` where MinVer runs, `actions/setup-dotnet@v5` with `global-json-file`):
   - `build-and-test` (ubuntu): restore, build, test, with `-p:A2AStrictAudit=true` on schedule/workflow_call strict.
   - `windows-unit` (windows-latest): only on `schedule`, `workflow_call` and `workflow_dispatch`; tests Protocol, Server, Client
     test projects.
   - `pack`: pack to `artifacts/packages`, `dotnet run eng/check-packages.cs`, upload the packages artifact.
   - `samples-on-packages` (needs pack): download artifact, run `eng/build-samples-on-packages.sh`.
   - `core-only`: `eng/check-core-only.sh`.
   - `reproducibility`: two checkouts in two paths, `dotnet build -c Release -p:ContinuousIntegrationBuild=true` each, compare
     SHA-256 of the shipped DLLs (`src/*/bin/Release/net10.0/Ambinder.A2A.*.dll` of shipped projects only).
   - `proto-drift`: keep as is.
   - Hook jobs `tck`, `interop`, `benchmarks`: each calls a reusable workflow file (`tck.yml`, `interop.yml`, `benchmarks.yml`,
     `on: workflow_call`) that today has one job that prints "not yet implemented (package 03/16/14)" and succeeds.
   The coexistence project builds as part of the solution build.
2. `.github/workflows/release.yml`: `on: push: tags: ['v*']` and `workflow_dispatch` (inputs `tag`, `dry_run` default true).
   `permissions: contents: read` at top; per job least privilege. `concurrency` per tag. Jobs in order, each `needs:` the previous:
   `validate` (SemVer regex; `git merge-base --is-ancestor <sha> origin/master`; `CHANGELOG.md` has `## [<version>]`; outputs
   version and `prerelease`), `verify` (`uses: ./.github/workflows/ci.yml` with `strict-audit: true`; the hooks run inside it),
   `pack` (pack with `-p:ContinuousIntegrationBuild=true`, check that MinVer version equals the tag version, run
   `check-packages.cs`, `sourcelink test` on every `.pdb` inside the `.snupkg`s, upload), `sign` (if `vars.SIGN_PACKAGES == 'true'`:
   `sign` CLI with Azure Trusted Signing over OIDC (`id-token: write`), then `dotnet nuget verify --all`; else if the version has
   no pre-release part: fail with "stable releases must be signed"; else log "unsigned preview"), `attest` (CycloneDX SBOM via the
   pinned dotnet tool, `actions/attest-build-provenance@v4` over the `.nupkg` files; `id-token: write`, `attestations: write`),
   `publish` (environment `nuget-release`; skipped when `dry_run`; `NuGet/login@<sha>` trusted publishing with `user` from
   `vars.NUGET_USER`, then `dotnet nuget push *.nupkg` and `*.snupkg` with `--skip-duplicate`; when `vars.NUGET_USE_API_KEY == 'true'`
   use `secrets.NUGET_API_KEY` instead), `github-release` (skipped when `dry_run`; `contents: write`; notes cut from the CHANGELOG
   section; `--prerelease` when needed; attach packages, SBOM; use `gh release create`), `smoke` (skipped when `dry_run`; poll
   nuget.org flat container for the version up to 20 minutes; fresh `dotnet new web` + `Ambinder.A2A.AspNetCore` at the exact
   version + `samples/GettingStarted.Agent/Program.cs`; run it; second project with `Ambinder.A2A.Client` + the client sample's
   `Program.cs`; pass when the reply text matches; on failure open an issue with `gh issue create` (`issues: write`)), `docs`
   (hook for package 13: prints "not yet implemented").
   In `dry_run` every step runs except publish, GitHub Release and smoke (they need the published packages); the smoke job instead
   runs against the local feed.
3. Check both workflows with `actionlint` if it can be installed (it is a Go binary; try `go install` or the release download; if
   neither works, say so) and by reading them.
4. `CONTRIBUTING.md` runbook references the workflow inputs. Commit: `ci: package, samples, reproducibility and Windows jobs`,
   `ci: tag-driven release workflow`.

## Task 9: README and docs pass, final verification

1. `README.md`: package table (the 7 packages: what, install line), the "one package for a server, one for a client" line,
   coexistence note (`using OfficialA2A = A2A;`), project layout, renamed commands. `docs/production.md` telemetry names.
2. Full verification (controller runs it too): clean clone build 0 warnings; full tests green; pack + check-packages; core-only
   check; samples on packages; the grep checks of Task 1; `git status` clean.
3. Commit: `docs: README for the Ambinder.A2A packages`.

## Acceptance mapping (M1)

| Spec 01 acceptance item | Where |
|---|---|
| No `namespace A2A.` / `A2A.*.dll` | Task 1 verify |
| Phase 1 tests pass with only name changes; golden tests unchanged | Task 1 (rename alone); Tasks 2–3 limited, reported test edits |
| Coexistence project compiles in CI | Task 6, built in `build-and-test` |
| Map rows that exist have project, README, description; pack matches map | Tasks 2–4, 6 (`check-packages.cs`) |
| Allow-list test; Protocol no deps; core-only console app has no ASP.NET/gRPC/Azure DLL | Task 6 |
| Generated protobuf types and `ProtoMapper` internal | Tasks 2–3 (02's public surface test checks it later) |
| `sourcelink test` for every package | Task 4 (local), Task 8 (`release.yml` pack job). IDE step-through: manual, owner |
| Reproducibility job | Task 4 (local), Task 8 |
| Community files; LICENSE and NOTICE in every nupkg | Tasks 4–5, 6 |
| Pipeline end to end on a test fork; `dry_run` | Task 8 builds it; the end-to-end run needs GitHub (owner) |
| Stable tag cannot publish unsigned; tag not on master / no CHANGELOG refused | Task 8 (`validate`, `sign`) |
| NuGet audit levels (C1) | Task 4 props, Task 8 strict flag; checked locally once with a known-vulnerable test package |
| Windows leg; `.gitignore` | Tasks 8, 1 |
| Owner actions 1–7 | Owner; listed in the PR |
| Authors/Copyright/LICENSE/NOTICE name Roman Ambinder; RepositoryUrl | Task 4 |
| AOT smoke job | M2, not in this plan (flags are on from Task 4) |
