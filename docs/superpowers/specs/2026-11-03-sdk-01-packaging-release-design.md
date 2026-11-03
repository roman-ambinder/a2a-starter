# SDK 01: Naming, packaging and release

Date: 2026-11-03
Status: draft for approval
Milestone: M1 (the rename and the package split go first; signing and the full pipeline are finished by `1.0.0`; previews ship unsigned, owner decision O4)
Size: M

## Intent

Today the code is a reference repo: assemblies are called `A2A.*`, there is no NuGet metadata, and CI only builds and tests. This package turns it into something a team can `dotnet add package`, trust, debug and upgrade.

It does five things. It renames every project, namespace and assembly to `Ambinder.A2A.*`. It fixes the package split, so a team takes only what it uses. It adds NuGet metadata, the license files and the community files. It makes builds reproducible and debuggable (SourceLink, symbols). It adds a tag-driven release pipeline that tests, signs, attests and publishes. It comes first because every other package is created under the new names.

This spec also owns the **package map** (see Design). Other specs use these IDs.

## Findings addressed

- R1: package ID and namespace clash with the official `A2A` package. Fixed by the rename and a coexistence test.
- R2: no NuGet metadata, SourceLink, symbols, license or community files.
- R3: no release pipeline, versioning, signing, SBOM, or Windows leg.
- R7: every consumer gets gRPC, protobuf and Azure.Identity. Fixed by the split.
- R8: single target and no trim or AOT annotations. Target stays `net10.0` (decided); this spec adds the AOT and trim flags and a smoke test.
- R41: repo hygiene (`TestResults/`, the `A2A.Reference.slnx` name).
- Part of R40: NuGet vulnerability audit as a build gate. (CodeQL and coverage belong to package 12.)
- Pain points: "Spec/sample quality that misleads integrators" (#24: samples must build against the shipped packages), "DI/host integration and lifetime problems" (#22: no hard dependency on one host, Native AOT, no static singletons).

## Goals

1. Every shipped project has `PackageId = AssemblyName = root namespace = Ambinder.A2A.<Name>`. A scan finds no `namespace A2A.` and no `A2A.*.dll` in the build output.
2. The official `A2A` 1.0.0-preview4 package and any `Ambinder.A2A.*` package can be referenced from one project and compile (types used by full name or alias).
3. The package map below is built. A dependency allow-list test fails when a package gains a dependency that is not on the list. Concretely: `Ambinder.A2A.Protocol` has no package dependencies. `Ambinder.A2A.Server` and `Ambinder.A2A.Client` pull no `Grpc.*`, `Google.Protobuf`, `Azure.*`, `Microsoft.AspNetCore.*` (Client and Server core) or database package.
4. `dotnet pack` produces, for each shipped project, a `.nupkg` and a `.snupkg` with: license expression `Apache-2.0`, README, icon, repository URL and commit, XML docs, and the NOTICE file.
5. A debugger can step into any package source from nuget.org via SourceLink. `sourcelink test` passes for every `.pdb` (embedded or snupkg).
6. Builds are deterministic: two clean builds of one commit give byte-identical assemblies.
7. Pushing a tag `vX.Y.Z[-prerelease]` runs: full CI (including TCK and interop jobs once they exist) then pack, validate, sign, attest, publish (after manual approval), GitHub Release, post-publish smoke test. Nothing is published from a branch build.
8. The post-publish smoke test installs the published packages from nuget.org into a fresh project and runs the hello agent plus a client call. This proves "a working agent in about 20 lines" against the real packages.
9. Every sample in `samples/` that a guide uses builds against the packed `.nupkg` files (local feed), not only against project references, in CI.
10. The repo has LICENSE (Apache-2.0), NOTICE, CHANGELOG.md, SECURITY.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, CODEOWNERS, and issue and PR templates.
11. Version numbers come from git tags (MinVer). One version for all packages.
12. NuGet vulnerability audit checks every dependency (direct or transitive) of a shipped package. A High or Critical advisory fails a PR build. A Low or Moderate advisory is a warning on a PR and an error in nightly and release builds. *(Claude's call, C1)*
13. `IsAotCompatible` is on for Protocol, Client, Server and AspNetCore (and every other package the map marks "yes"), and a Native AOT publish of a sample agent and a sample client passes in CI by `1.0.0`.

## Non-goals

- Targets other than `net10.0` (decided in the work plan).
- API tracking, XML-doc enforcement, analyzers, semver rules and hiding internals: package 02. This spec only provides the packaging that carries the XML files.
- What goes inside each package: the owning package spec (03 to 12, 15). This spec fixes the IDs, the dependency rules and the layout.
- CodeQL, coverage upload, mutation testing, soak tests: package 12.
- Benchmarks and budgets: package 14. This spec only gives it a place in the repo and the workflow hook.
- The docs site and its deploy job: package 13. The release workflow here has a hook job that 13 fills.
- A CI feed for every merge to `master`. Preview tags are cheap enough. (Writer's call, see below.)
- Project templates (`dotnet new`). Not in v1.
- Redis or other backend packages (after 1.0).

## Anti-goals

- A release that cannot be traced to a commit, a green CI run and an approver.
- A package that only works from the repo (a missing file, a project reference that did not become a dependency).
- Renaming that leaves two names alive (for example `A2A.Server` in docs, Dockerfiles, compose, telemetry names or test assemblies).
- A "core" package that quietly needs ASP.NET Core, gRPC, Azure or a database.
- A secret (NuGet key, signing key) stored in the repo or readable by pull requests from forks.
- Strict version pins between our packages that block a team from taking a patch of one package.
- Publishing a package ID we have not reserved under the `Ambinder.` prefix.

## Constraints

From the work plan:

- Package IDs and namespaces `Ambinder.A2A.*`. The NuGet ID `A2A` belongs to the official SDK.
- .NET 10 only. SDK pinned by `global.json` (today 10.0.401, `rollForward: latestFeature`).
- Public on nuget.org, Apache-2.0, reserve the `Ambinder` ID prefix.
- Strategy is "keep and grow this SDK", so the Phase 1 history stays in git. Old specs (`2026-11-02-a2a-dotnet-reference-design.md` and the observability spec) are history and keep the old names. Add one line at their top: "Written before the rename to Ambinder.A2A.*".
- Spec version A2A v1.0.1; proto pinned to the `v1.0.1` tag (package 03 does the pin). Packaging only has to compile `proto/a2a.proto` into the gRPC packages.
- Principle 5: no hidden dependency on Azure or a database in the core.

Current facts this spec changes:

- `Directory.Build.props:1-8` has only TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors, LangVersion.
- Project files: `src/A2A.Protocol`, `src/A2A.Protocol.Grpc`, `src/A2A.Server`, `src/A2A.Client`. Test assemblies use `InternalsVisibleTo` (`A2A.Client.csproj:12`, `A2A.Server.csproj:15`).
- `.github/workflows/ci.yml:16-33`: one Ubuntu job, restore, build, test.
- Config section names start with `A2A:` (for example `A2A:PublicBaseUrl`, `A2A:Push:*`, `A2A:MaxRequestBodyBytes`). Activity source and meter names are `A2A.Server` and `A2A.Client` (`docs/production.md` section 6).
- Git remote is `roman-ambinder/a2a-starter`; the solution is `A2A.Reference.slnx`.
- The repo stays at `roman-ambinder/a2a-starter` for now, and package URLs use it (owner decision O1).
- One builder, no fixed dates. Review gates are automated checks (public API diff, required CI, TCK, interop) plus the owner's admin approval for releases. No second human reviewer is required (owner decision O2).

## Design

### 1. Package map (authoritative)

All packages target `net10.0`. "Owner" is the spec that designs the contents. A spec may rename its package before the first preview tag, but must update this table in the same PR. After the first preview is on nuget.org, an ID never changes.

| Package ID | What it holds | Key dependencies | Owner | AOT flag | M |
|---|---|---|---|---|---|
| `Ambinder.A2A.Protocol` | Models, enums, errors, constants, JSON (source-generated), telemetry names; JCS canonicalizer, card canonicalization and JWS primitives (namespace `Ambinder.A2A.Protocol.Signing`, package 04) | none | 03, 04 | yes | M1 |
| `Ambinder.A2A.Server` | Server core without a web host: agent model and authoring helpers, task manager, request handler, middleware pipeline (`IA2AMiddleware`), execution host, shutdown and recovery, store contracts and in-memory stores, the four cluster seams (`ITaskEventBus`, `ITaskRouter`, `ITaskForwarder`, `ClusterCallContext`) with single-pod defaults, push contracts and sender, security contracts (`A2ACaller`, owner, tenant, continuation policy), card signing on the server side, `AddA2AServer` | Protocol; `Microsoft.Extensions.{DependencyInjection,Options,Logging,Hosting,Http}.Abstractions` or the matching base packages | 05, 06, 07, 08, 09, 11 | yes | M1 |
| `Ambinder.A2A.AspNetCore` | JSON-RPC and REST endpoints, SSE, card endpoints, `IA2AVersionDispatcher` (03), push JWKS endpoint and Data Protection credential protector (08), `MapA2A`, hosting helpers (health, forwarded headers, graceful shutdown wiring) | Server; `FrameworkReference Microsoft.AspNetCore.App` | 03, 08, 09 | yes | M1 |
| `Ambinder.A2A.AspNetCore.Grpc` | gRPC server service, `MapA2AGrpc` | AspNetCore; `Grpc.AspNetCore`, `Google.Protobuf` | 03, 09 | no (until proven) | M1 |
| `Ambinder.A2A.Client` | `IA2AClient`, JSON-RPC and REST transports, card resolver and card verification (04), interceptors, `AddA2AClient`, resilience wiring, push-wait abstractions `IA2APushWaiter` and `PushSubscription` (B10) | Protocol; `Microsoft.Extensions.{Http,Options,DependencyInjection,Logging}`, `Microsoft.Extensions.Http.Resilience` | 10 | yes | M1 |
| `Ambinder.A2A.Client.Grpc` | gRPC client transport, registered into the client's transport list | Client; `Grpc.Net.Client`, `Google.Protobuf` | 10 | no (until proven) | M1 |
| `Ambinder.A2A.AspNetCore.Entra` | Entra token validation, app roles, requirement evaluator, card security schemes, the adapter that feeds the server caller's token to the OBO provider as a user-assertion delegate (B10) | AspNetCore, Client.Entra; `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.Validators` (not Microsoft.Identity.Web, see 07) | 07 | no | M1 |
| `Ambinder.A2A.Client.Entra` | Azure.Identity credential providers, workload identity, OBO (takes a user-assertion delegate; no dependency on `Ambinder.A2A.Server`, B10) | Client; `Azure.Identity` | 07 | no | M1 |
| `Ambinder.A2A.Azure.KeyVault` | Key Vault signing key source for card signing (04) and push signing (08) | Server; `Azure.Security.KeyVault.Keys`, `Azure.Identity` | 04 (push part: 08) | no | M1 |
| `Ambinder.A2A.Stores.EntityFrameworkCore` | Provider-neutral EF Core task, push-config, outbox and instance stores | Server; `Microsoft.EntityFrameworkCore.Relational` | 05 | no | M1 |
| `Ambinder.A2A.Stores.EntityFrameworkCore.PostgreSql` | `UseA2APostgreSql()`, migrations | Stores.EntityFrameworkCore; `Npgsql.EntityFrameworkCore.PostgreSQL` | 05 | no | M1 |
| `Ambinder.A2A.Stores.EntityFrameworkCore.Sqlite` | `UseA2ASqlite()`, migrations | Stores.EntityFrameworkCore; `Microsoft.EntityFrameworkCore.Sqlite` | 05 | no | M1 |
| `Ambinder.A2A.Stores.EntityFrameworkCore.SqlServer` | `UseA2ASqlServer()`, migrations | Stores.EntityFrameworkCore; `Microsoft.EntityFrameworkCore.SqlServer` | 05 | no | M1 |
| `Ambinder.A2A.Stores.Cosmos` | Cosmos DB stores | Server; `Microsoft.Azure.Cosmos` | 05 | no | M1 |
| `Ambinder.A2A.Push.Receiver` | Webhook receiver for clients: credential, signature, task-id checks, dedupe; implements the Client's `IA2APushWaiter` (B10) | Client; `FrameworkReference Microsoft.AspNetCore.App`; `Microsoft.IdentityModel.JsonWebTokens` (no `Ambinder.A2A.Server` or `Ambinder.A2A.AspNetCore` dependency) | 08 | no | M1 |
| `Ambinder.A2A.Cluster` | Owner-pod HTTP forwarding, internal endpoints, the pod-auth seam, streaming proxy (the seam interfaces live in `Ambinder.A2A.Server`, see above). Supported on the EF Core store only in 1.0 (B3) | Server, AspNetCore | 06 | no | M2 |
| `Ambinder.A2A.Cluster.Entra` | Pod-to-pod authentication with Entra workload identity, the only shipped pod-auth mode (C6) | Cluster, AspNetCore.Entra, Client.Entra; `Azure.Identity` | 06 | no | M2 |
| `Ambinder.A2A.Extensions.AI` | Microsoft.Extensions.AI bridges (chat-client agent, agent as `AIFunction`) | Server, Client; `Microsoft.Extensions.AI.Abstractions` | 11 | yes (11 Goal 14) | M2 |
| `Ambinder.A2A.Extensions.AI.AgentFramework` | Microsoft Agent Framework bridge. Ships in 1.0; if `Microsoft.Agents.AI` is not GA by M2, it ships as a preview package (C12) | Extensions.AI; `Microsoft.Agents.AI` | 11 | no | M2 (preview if not GA) |
| `Ambinder.A2A.Compat.V0_3` | A2A 0.3 models and client adapters (C14) | Client (gRPC 0.3 client adds `Grpc.Net.Client`) | 15 | yes for JSON-RPC and REST (15 acceptance); gRPC 0.3 not AOT-verified | M2 |
| `Ambinder.A2A.Compat.V0_3.AspNetCore` | A2A 0.3 server adapters (C14) | Compat.V0_3, AspNetCore (gRPC 0.3 server adds `Grpc.AspNetCore`) | 15 | yes for JSON-RPC and REST; gRPC 0.3 not AOT-verified | M2 |
| `Ambinder.A2A.Testing` | In-memory test host, fakes, recording client, assertions, telemetry collectors (an internal slice is built in M1 as repo test code, B2) | Server, Client, AspNetCore; `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.TimeProvider.Testing`; test framework neutral | 12 | no | M2 |
| `Ambinder.A2A.Testing.AI` | `ScriptedChatClient`, `FakeChatClient` | Testing, Extensions.AI; `Microsoft.Extensions.AI.Abstractions` | 12 | no | M2 |
| `Ambinder.A2A.Testing.Conformance` | Store conformance suite (cases written by 05 in M1 under `tests/`, published here in M2) | Testing; `xunit.v3.extensibility.core` | 12 (cases: 05) | no | M2 |

Not in the map on purpose: `Ambinder.A2A.Cluster.Redis` (after 1.0, 06) and `Ambinder.A2A.Grpc.Core` (a fallback only, rule 4). Test, sample and benchmark projects (for example `Ambinder.A2A.Benchmarks`, `Ambinder.A2A.Tck.Sut`, `tests/Ambinder.A2A.Perf.Testing`, the M1 harness slice `tests/Ambinder.A2A.TestHarness` from 12) are not shipped.

Rules:

1. **Shipped projects** are the rows above. Everything else (samples, tests, benchmarks, tools) has `IsPackable=false`.
2. **Core means no host.** `Protocol`, `Server` and `Client` must not reference ASP.NET Core, gRPC, Azure, EF or a database. Enforced by the dependency allow-list test (Acceptance).
3. **The Phase 1 projects map like this:**

   | Phase 1 project | Becomes |
   |---|---|
   | `A2A.Protocol` | `Ambinder.A2A.Protocol` |
   | `A2A.Protocol.Grpc` | its proto and mapper code is compiled into `Ambinder.A2A.AspNetCore.Grpc` and `Ambinder.A2A.Client.Grpc` (see next point); the project itself is removed |
   | `A2A.Server` | split into `Ambinder.A2A.Server` (core), `Ambinder.A2A.AspNetCore` (transports, hosting), `Ambinder.A2A.AspNetCore.Grpc` |
   | `A2A.Client` | `Ambinder.A2A.Client` (+ `.Client.Grpc`, `.Client.Entra` for the gRPC transport and Azure.Identity providers) |

4. **gRPC code is shared by source, not by a public package** *(writer's call)*. The generated protobuf types and `ProtoMapper` become `internal` (package 02, R6). Both gRPC packages compile the same files from `src/Grpc.Shared/` plus `proto/a2a.proto` with `<Protobuf Access="Internal" ...>`. Two internal copies cost a few hundred KB of IL and give a clean public surface with no `InternalsVisibleTo` between shipped packages. If the builder finds the copies too large or the descriptors clash at run time, the fallback is a third package `Ambinder.A2A.Grpc.Core` with `InternalsVisibleTo` to the two gRPC packages and an exact-version dependency. Any change to this must keep the same public IDs for the two gRPC packages.
5. **gRPC is opt-in.** `Ambinder.A2A.AspNetCore` serves JSON-RPC and REST. A team adds `Ambinder.A2A.AspNetCore.Grpc` to serve gRPC, and `Ambinder.A2A.Client.Grpc` to call it. A card that lists a gRPC interface the client cannot speak gives an error that names the package to add (error text: package 10). *(writer's call; the TCK still runs all three transports because the CI test host references all of them)*
6. **No `InternalsVisibleTo` between shipped assemblies.** It is allowed only to the matching test assemblies and to the benchmarks assembly (package 02 and 14 use this).
7. **Dependency floors.** A library references each Microsoft package at the lowest .NET 10 version it needs (for example `10.0.0`), not at the newest patch. Tests and samples use the newest. Central versions stay in `Directory.Packages.props`; shipped projects use `VersionOverride` where needed. `CentralPackageTransitivePinningEnabled=true` so a vulnerable transitive package is lifted to a safe version in our build, and the vulnerability audit (Goal 12) sees what consumers get. *(writer's call)*
8. **No separate card-signing package.** Package 04 puts JCS and JWS primitives in `Ambinder.A2A.Protocol` (BCL only), signing in `Server` and verification in `Client`; only the Key Vault key source is its own package (`Ambinder.A2A.Azure.KeyVault`). The earlier `Ambinder.A2A.CardSigning` row is dropped (owner spec 04 decides contents).
9. **Dependencies between our packages are normal ranges** (`>= X.Y.Z`, what `ProjectReference` packs by default), not exact pins. Because there is no `InternalsVisibleTo` between shipped assemblies (rule 6), mixed versions within one major are safe. The one exception is a package that uses the fallback in rule 4.
10. **Shared code without `InternalsVisibleTo`** *(Claude's call, B12)*. Small helpers that several packages need (page tokens, task state rules, request validation) live as source files under `src/Shared/` and are linked into each package that uses them, compiled as `internal`. This is the same pattern as `Grpc.Shared` (rule 4). `A2AJson` in `Ambinder.A2A.Protocol` exposes a public typed `JsonTypeInfo<T>` accessor for every protocol type, so hot paths in other packages use typed metadata without internals (package 02 and 14).

### 2. Naming rules

- Folder, project file, `AssemblyName`, `RootNamespace` and `PackageId` are identical: `src/Ambinder.A2A.Server/Ambinder.A2A.Server.csproj`.
- Namespaces follow the package: `Ambinder.A2A.Protocol`, `Ambinder.A2A.Server`, and so on. Sub-namespaces per concern, not per folder (package 02 does the namespace review; its rule: the 20-line agent needs at most two `using` lines of our namespaces).
- `AddA2A...` extension methods on `IServiceCollection` live in `Microsoft.Extensions.DependencyInjection`, and `MapA2A...` on `IEndpointRouteBuilder` in `Microsoft.AspNetCore.Builder` (package 02 explains why). That is a namespace choice, not an assembly choice.
- Type names do not change in the rename (`AgentCard`, `AgentTask`, `IA2AClient`, `A2AException`). They are in the `Ambinder.A2A.*` namespaces, so they do not collide with the official `A2A.*` types. A consumer who uses both writes `using OfficialA2A = A2A;`. The migration doc shows this (package 13).
- Test projects: `Ambinder.A2A.<Package>.Tests`. Samples: assembly names `Ambinder.A2A.Samples.<Name>`, folders keep their short names (`samples/AgentServer`) so docs and compose files stay readable. *(writer's call)*
- Telemetry: the activity source and meter names follow the assembly names, which is the OpenTelemetry habit. `A2A.Server` becomes `Ambinder.A2A.Server`, `A2A.Client` becomes `Ambinder.A2A.Client`. Span, attribute and metric names (`a2a.server <Method>`, `a2a.agent`, `a2a.server.tasks.completed`) do not change. Constants stay in `A2ATelemetryNames`. This is a breaking change for anyone with a dashboard on the old names, so the Phase 1 migration page (package 13) lists it. *(writer's call)*
- Configuration: the `A2A:` section prefix stays (`A2A:PublicBaseUrl`, `A2A:Push:...`). It is a protocol-named config key, not an assembly name, and it is shorter to type. Environment variables stay too. *(writer's call)*
- Wire and protocol names do not change: `A2A-Version`, `A2A-Extensions`, `a2a.*` tags, the proto package `lf.a2a.v1`.
- Extension URIs *(Claude's call)*: package 01 owns the names. The push signature extension URI is `https://github.com/roman-ambinder/a2a-starter/extensions/push-signature/v1`. It follows owner decision O1 on the repo location, so it changes if the repo moves before 1.0. Package 08 uses it and package 13 hosts the page.

### 3. The rename (one mechanical PR, done first)

Order: tag the last Phase 1 commit `phase-1` (package 13 needs it for the migration map), then one PR, no behavior change:

1. `git mv` every project folder and `.csproj`; rename the solution to `Ambinder.A2A.slnx` (the `DocsTests` helper looks for `A2A.Reference.slnx` at `tests/A2A.Integration.Tests/Repo/DocsTests.cs:11`; update it).
2. Replace `namespace A2A.` with `namespace Ambinder.A2A.` and every `using A2A.` with `using Ambinder.A2A.`. Fix fully qualified `A2A.X` names the compiler reports.
3. Update `InternalsVisibleTo`, project references, `Directory.Packages.props` groups, `.github/dependabot.yml` groups, `.dockerignore`, the four Dockerfiles, `docker-compose.yml`, `run-all.sh`, `run-all.ps1`, `samples/AppHost`, the README tables and the per-project READMEs.
4. Split the old `A2A.Server` and `A2A.Client` into the new packages as in the map. This can be a second PR right after the rename PR; the rename PR must build and pass all tests alone.
5. Delete `src/A2A.Protocol.Grpc`; add `src/Grpc.Shared` with the moved files.
6. Rename the telemetry source and meter names (above).
7. Add `TestResults/`, `*.nupkg`, `*.snupkg`, `artifacts/`, `BenchmarkDotNet.Artifacts/` to `.gitignore` (R41).

Check: `git grep -nE '\bA2A\.(Protocol|Server|Client)\b'` finds only the history notes, the migration doc, and the coexistence test. All existing tests pass unchanged apart from names. The golden JSON and proto round-trip tests are byte-for-byte the same.

### 4. Build and package properties

Layout (replace the single `Directory.Build.props`):

- `Directory.Build.props` (all projects): TargetFramework, Nullable, ImplicitUsings, LangVersion, TreatWarningsAsErrors, `Deterministic=true`, `ContinuousIntegrationBuild=true` when `GITHUB_ACTIONS=true`, `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, and advisory warnings as errors per the CI rule in section 6 (C1).
- `src/Directory.Build.props` (shipped projects): `IsPackable=true`, the metadata below, `GenerateDocumentationFile=true` (package 02 owns the doc rules), `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `DebugType=portable`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`, `EnablePackageValidation=true` (baseline set by package 02 at 1.0.0), `IsAotCompatible` per the package map, the MinVer reference.
- `samples/`, `tests/`, `benchmarks/` have their own `Directory.Build.props` with `IsPackable=false`.
- SourceLink is built into the .NET SDK for GitHub-hosted repos (since .NET 8), so no extra package is needed. The builder confirms with `sourcelink test`.
- No strong naming *(writer's call)*: it is no longer advised for new libraries and does not add security.

Metadata (set once, per-package `Description` and tags only in the csproj):

| Property | Value |
|---|---|
| `Authors`, `Copyright` | `Roman Ambinder`, `Copyright (c) Roman Ambinder` (owner decision O5) |
| `PackageLicenseExpression` | `Apache-2.0` |
| `PackageProjectUrl`, `RepositoryUrl` | `https://github.com/roman-ambinder/a2a-starter` (owner decision O1), set in one property so a later move changes one line |
| `PackageTags` | `a2a;agent2agent;agents;ai;json-rpc;grpc;aspnetcore` plus package-specific tags |
| `PackageReadmeFile` | `README.md` (each package has its own, ~40 lines: what, install, a 10-line example, link to the docs; content rules in package 13) |
| `PackageIcon` | `icon.png` (128x128, one file in `eng/`) |
| `PackageReleaseNotes` | link to the GitHub release for the tag |
| `NeutralLanguage`, `PackageRequireLicenseAcceptance` | `en`, `false` |
| Extra pack items | `LICENSE` and `NOTICE` at the package root |

Versioning *(writer's call)*: **MinVer** with `MinVerTagPrefix=v`, `MinVerMinimumMajorMinor=1.0`, default pre-release `preview.0`. Tag `v1.0.0-preview.1` gives `1.0.0-preview.1`. Untagged commits give `1.0.0-preview.1.N`; such builds are never pushed. One version for all packages: every tag releases every package, even an unchanged one, so users can line them up ("use 1.2.x of everything"). Pre-release identifiers: `preview.N` for M1, `rc.N` for the API freeze (package 02), then `1.0.0`.

Reproducibility: with the settings above and `global.json` pinned, the same commit gives the same DLLs. CI proves it (Goal 6) by building twice in separate folders and comparing SHA-256 of every shipped DLL. (The `.nupkg` itself has timestamps and is not compared.)

### 5. License and community files

- `LICENSE`: Apache-2.0 full text, copyright holder "Roman Ambinder" (owner decision O5).
- `NOTICE`: project name and the copyright line "Copyright (c) Roman Ambinder" (owner decision O5), plus attribution for the vendored A2A proto (`proto/a2a.proto` is from the A2A project, Apache-2.0, source URL and tag in `proto/README.md`). Packages carry both files. Apache-2.0 requires keeping the NOTICE with redistributions.
- `CHANGELOG.md`: Keep a Changelog format, one section per tag, a "Breaking changes" heading in every preview. The GitHub Release body is cut from the matching section by the pipeline.
- `SECURITY.md`: report privately through GitHub private vulnerability reporting (owner turns it on); response targets; supported versions (rule in package 02); no public issues for vulnerabilities.
- `CONTRIBUTING.md`: how to build and test, the API-file workflow (package 02), the perf rules (package 14), commit and PR rules, "one change per PR". Contributions use DCO sign-off (`Signed-off-by`), no CLA *(writer's call)*.
- `CODE_OF_CONDUCT.md`: Contributor Covenant.
- `.github/CODEOWNERS`: owners for `PublicAPI.*.txt` (the API owner), `.github/workflows/`, `eng/`, `proto/`. The owner is the only code owner; the public API diff check, not a second reviewer, is the gate (owner decision O2).
- `.github/ISSUE_TEMPLATE/` (bug, feature, question) and `pull_request_template.md` with a checklist: tests, docs, `CHANGELOG`, API files reviewed, perf evidence when a hot path changed.

### 6. CI (`.github/workflows/ci.yml`) changes

Keep the current build and test job, then add:

- **Windows leg** *(Claude's call, C1)*: unit tests of Protocol, Server and Client on `windows-latest`, nightly and on release. Linux unit tests run on every PR. Integration, TCK and interop stay on Ubuntu.
- **Advisory levels** *(Claude's call, C1)*: in PR builds only NU1903 and NU1904 (High, Critical) are errors; NU1901 and NU1902 (Low, Moderate) are warnings. Nightly and release builds make all four errors. Package 12 follows this.
- **Pack job** on every PR: `dotnet pack` all shipped projects, then the checks in Release step 3. No publish.
- **Dependency allow-list test**: reads each `.nupkg` (nuspec) and compares with `eng/package-dependencies.json`. A new dependency must be added to that file in the same PR, which makes it visible in review.
- **Samples on packages**: restore the getting-started samples from the local `artifacts/packages` feed (a `NuGet.config` in `eng/samples-feed/`) and build them. Catches a package that is missing a file or a dependency.
- **Coexistence test** (R1): a tiny project in `tests/Ambinder.A2A.Coexistence/` references the official `A2A` package (version pinned to 1.0.0-preview4 or the newest preview at the time) and our Protocol, Client and Server packages, and uses both `AgentCard` types by alias. It must restore and compile. It does not run.
- **Reproducibility job**: build twice, compare DLL hashes.
- **AOT smoke job** (M2): `dotnet publish -p:PublishAot=true` for the AOT sample agent and client, run them against each other on JSON-RPC and REST, and check the publish log has no trim or AOT warnings. gRPC and Entra packages are excluded and stay marked "not AOT-verified".
- Hook jobs for TCK (package 03), interop (package 16) and benchmarks (package 14). Each is a reusable workflow (`workflow_call`) so `release.yml` can require them.
- Third-party actions (not owned by `actions/*`) are pinned by commit SHA. Dependabot updates them (it already watches `github-actions`). Each job sets `permissions:` to the least it needs.

### 7. Release pipeline (`.github/workflows/release.yml`)

Trigger: `push` of a tag matching `v*`. Jobs, in order, each `needs:` the previous:

1. **validate**: the tag is valid SemVer; the tagged commit is reachable from `master` (`git merge-base --is-ancestor`); `CHANGELOG.md` has a section for this version.
2. **verify**: call the full CI workflow on this commit (build, unit and integration tests, Windows leg, all advisory levels as errors, TCK, interop, reproducibility, AOT smoke, benchmark smoke). Any failure stops the release.
3. **pack**: `dotnet pack -c Release -p:ContinuousIntegrationBuild=true`. Then checks:
   - the set of `.nupkg` files equals the package map (no missing, no extra);
   - every package has README, icon, LICENSE, NOTICE, XML docs, and the license expression;
   - the dependency allow-list test;
   - `sourcelink test` on every `.pdb`;
   - package validation (ApiCompat) against the baseline once one exists (package 02);
   - `dotnet nuget verify` on signed output after step 4.
4. **sign**: sign every `.nupkg` with Microsoft's `sign` CLI using Azure Trusted Signing, reached by GitHub OIDC (no stored key). Owner decision O4: previews ship unsigned; setting up Azure Trusted Signing is an owner action required before `1.0.0`. Signing is *required* for a stable tag (no `-`) and off for previews until the owner turns it on. When the repo variable `SIGN_PACKAGES` is not `true`, the step logs "unsigned preview" and continues, but only for versions with a pre-release part.
5. **attest**: generate an SBOM (SPDX or CycloneDX, for example `Microsoft.Sbom.Targets` or `anchore/sbom-action`) and create a GitHub build-provenance attestation for the packages (`actions/attest-build-provenance`; job needs `id-token: write` and `attestations: write`). Both are attached to the GitHub Release.
6. **publish** (GitHub Environment `nuget-release`, required reviewer = the owner, as admin approval; no second reviewer, owner decision O2): push `.nupkg` and `.snupkg` to nuget.org with `--skip-duplicate`. Auth is NuGet trusted publishing (GitHub OIDC, `NuGet/login`) with a package-ID glob `Ambinder.*` policy; if the builder finds that unavailable, fall back to a scoped API key (glob `Ambinder.*`, push only, expiring) stored as an environment secret. The approval click is the last human gate, because a pushed version can be unlisted but never replaced.
7. **github-release**: create the release for the tag with notes from the `CHANGELOG` section, mark it pre-release when the version has a pre-release part, attach packages, SBOM and attestation.
8. **smoke**: wait for nuget.org indexing (poll up to 20 minutes), then in a fresh folder: `dotnet new web`, add `Ambinder.A2A.AspNetCore` at exactly this version, paste the hello agent from the getting-started guide (the file is taken from the repo's `samples/GettingStarted.Agent`), run it, then a second project adds `Ambinder.A2A.Client` and does the 5-line call. Pass means the call returns the expected text. On failure, the job opens a GitHub issue with the log and the runbook below says what to do.
9. **docs** (hook, package 13): build and deploy the versioned docs site for this tag.

Runbook (in `CONTRIBUTING.md`): to release, merge to `master`, update `CHANGELOG`, tag `vX.Y.Z[-pre]`, push the tag, approve in the `nuget-release` environment. If smoke fails after publish: unlist the bad version (`dotnet nuget delete` unlists on nuget.org), fix, tag the next patch or preview number. Never re-tag.

### 8. nuget.org and GitHub: actions only the owner can do

These cannot be done by code. They are acceptance items.

| # | Owner action | When |
|---|---|---|
| 1 | Check that the IDs in the package map are free on nuget.org. Do this before the rename PR; if one is taken, change the table first. | Before the rename |
| 2 | Choose the nuget.org account (an organization account is better than a personal one). A second nuget.org owner is optional (O2: one builder). | Before first push |
| 3 | Apply for the reserved ID prefix `Ambinder.` (nuget.org "reserved ID prefix" request). Keep all packages under the prefix. | Before first push |
| 4 | Done (owner decision O1): the repo stays at `roman-ambinder/a2a-starter` for now. If it moves later, rename or transfer once and change the one URL property. | Before first preview tag |
| 5 | Create GitHub Environment `nuget-release` with a required reviewer; add the trusted-publishing policy or the API key secret. | Before first preview tag |
| 6 | Set up Azure Trusted Signing and register the certificate on nuget.org (owner decision O4). Previews ship unsigned until then. | Before `1.0.0` |
| 7 | Turn on private vulnerability reporting, secret scanning and push protection, and branch protection on `master` (required checks: CI, pack, public API diff, TCK and interop once they are required; no force push; no second human review, owner decision O2). | Before first preview tag |

## How it meets the six principles

1. **Protocol.** Packaging does not change the wire. The rename PR must leave the golden JSON and proto round-trip tests byte-identical. The release pipeline will not start without green TCK and interop jobs on the tagged commit.
2. **.NET enabler.** Standard NuGet practice: SourceLink, snupkg, deterministic builds, `Apache-2.0` expression, signed packages, trim and AOT flags, `AddA2A...` in the namespace the web SDK already imports. Check: a fresh `dotnet new web` project needs no extra `using` for the registration calls.
3. **Easy.** Install is one package for a server (`Ambinder.A2A.AspNetCore`) and one for a client (`Ambinder.A2A.Client`). The post-publish smoke test (Goal 8) runs the real getting-started steps against nuget.org on every release.
4. **Performance.** The split keeps gRPC, protobuf and Azure.Identity out of apps that do not use them (smaller publish size). Check: the pack job records each package's size and its dependency count in the job summary; package 14 sets the budget on them.
5. **Modular and extendable.** Small packages (the map above), one rule for the core ("no host"), tested by the allow-list. Check: `Ambinder.A2A.Server` and `Ambinder.A2A.Client` restore into a console app with no ASP.NET Core and no gRPC assemblies in the output folder (a test publishes such an app and lists the DLLs).
6. **Integration pains.** Removes the namespace clash with the official SDK (R1, coexistence test), the sample drift pain (samples build on the packed packages), and the host coupling pain (core has no web host, AOT smoke test). Supply-chain pain (unknown provenance) is covered by signing, SBOM and attestation.

## Dependencies

- **Needs:** nothing (it goes first). Package 03 for the proto pin (the vendored proto is compiled as it is until then).
- **Provides to all packages:** the package map and IDs, the folder layout, `Directory.Build.props` files, `eng/package-dependencies.json` (each package edits its own entry), reusable CI workflows, the release pipeline, the README template, the local-feed sample check.
- **Provides to 02:** the `EnablePackageValidation` switch, the pack output with XML files, `CODEOWNERS` entries for `PublicAPI.*.txt`.
- **Provides to 03:** the TCK hook job in CI and the release gate. **To 16:** the interop hook job.
- **Provides to 13:** the `phase-1` tag, the docs hook job, the post-publish smoke test, the sample feed.
- **Provides to 14:** the `benchmarks/` folder convention, the `perf` hook job, a package-size report.
- **Provides to 12:** the `Ambinder.A2A.Testing`, `Ambinder.A2A.Testing.AI` and `Ambinder.A2A.Testing.Conformance` IDs and layout.
- **Depends on 03 to 12 and 15** to keep their package rows in the map correct. Every spec uses exactly the IDs in this map.

## Acceptance criteria

- [ ] `git grep -nE 'namespace A2A\.|<(Root|Assembly)Name>A2A\.'` finds nothing; no `A2A.*.dll` (without the `Ambinder.` prefix) in any build output.
- [ ] All Phase 1 tests pass after the rename with only name changes; the golden JSON and proto tests are unchanged.
- [ ] Coexistence project (official `A2A` + ours) restores and compiles in CI.
- [ ] Every row of the package map that exists at the milestone has a project, a README, and a `description`; `dotnet pack` output matches the map exactly.
- [ ] `eng/package-dependencies.json` allow-list test exists and fails when a dependency is added without a list change. `Ambinder.A2A.Protocol` has no dependencies. A console app that references only `Ambinder.A2A.Server` and `Ambinder.A2A.Client` publishes without any `Microsoft.AspNetCore.*`, `Grpc.*`, `Google.Protobuf` or `Azure.*` DLL.
- [ ] Generated protobuf types and `ProtoMapper` are `internal` in both gRPC assemblies (checked by the public surface test in package 02).
- [ ] `sourcelink test` passes for every package. Stepping into package code from a consumer project in an IDE shows the GitHub source at the right commit (manual check, once per milestone).
- [ ] Reproducibility job passes (identical DLL hashes from two clean builds).
- [ ] LICENSE, NOTICE, CHANGELOG, SECURITY, CONTRIBUTING, CODE_OF_CONDUCT, CODEOWNERS and the templates exist. Each `.nupkg` contains LICENSE and NOTICE.
- [ ] Tag `v1.0.0-preview.1` on a test fork (or a test feed) runs the whole pipeline end to end, including the manual approval and the smoke job. A dry run flag (`workflow_dispatch` with `dry_run=true`) goes through every step except the nuget.org push.
- [ ] A stable tag cannot publish when signing is off.
- [ ] A tag that is not on `master`, or has no CHANGELOG section, is refused.
- [ ] NuGet audit fails a test PR that adds a package with a High or Critical advisory, and only warns for a Low or Moderate one; the nightly build fails on both (C1).
- [ ] Windows leg is green (nightly and on release, C1). `.gitignore` covers `TestResults/` and pack output.
- [ ] Owner actions 1 to 7 are done and ticked in the release issue (6 may be after the first preview).
- [ ] `Authors`, `Copyright`, `LICENSE` and `NOTICE` name "Roman Ambinder" (O5); `RepositoryUrl` is `roman-ambinder/a2a-starter` (O1).
- [ ] AOT smoke job (M2): Protocol, Client, Server and AspNetCore publish with `PublishAot` and zero warnings; the two samples talk to each other.

## Left to the builder

- Icon artwork, package descriptions, tag lists beyond the base set.
- Exact action versions and job names; whether `verify` is one reusable workflow or several.
- Which SBOM tool; which signing tool, if `sign` does not fit.
- Whether `Grpc.Shared` is a shared project, linked files, or a source-only project.
- Polling intervals and timeouts in the smoke job.
- Whether the package-size report is a table in the job summary or an artifact.
- Exact `NuGetAuditLevel` suppressions (each must have a comment and an expiry date).

## Open questions

None. Q1 (signing) is owner decision O4, Q2 (author) is O5, Q3 (repo) is O1, and Q4 (PR gates) is Claude's call C1. All are in the Design above.
