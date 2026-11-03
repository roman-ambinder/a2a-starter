# SDK 02: Public API and versioning

Date: 2026-11-03
Status: approved by owner 2026-11-03
Milestone: M1 (internals hidden, API tracking on); frozen at M2 (`1.0.0`, semver from there)
Size: M-L

## Intent

Every public type is a promise to product teams. Today the promise is accidental: about 100 public types, many of them plumbing (`TaskManager`, `SseWriter`, `ProtoMapper`, the generated protobuf classes), and nothing tells a reviewer that an API changed.

This package makes the public surface small, deliberate, documented and tracked. It sets the rules for what may be public, how the API may change, how a breaking change is recorded, and when the API is frozen. Every other package adds its public types under these rules.

## Findings addressed

- R4: no public API tracking, no package validation, no semver or breaking-change policy.
- R5: XML docs are not generated or enforced; many public members have none.
- R6: large accidental public surface (server internals, transport plumbing, generated protobuf types in the public contract).
- R32 (part): transport clients are public with public constructors. They become internal; package 10 designs the client entry points.
- R40 (part): analyzer level and a banned-API list. (CodeQL and coverage: package 12.)
- R36 (part): lifetime rules must be in XML docs of the public seams.
- Pain points: "DI/host integration and lifetime problems" (#22), "Error formats and error mapping" (#6: the exception types are public API), "JSON serialization drift between SDKs" (#2: the JSON contract is public API).

## Goals

1. Every shipped project tracks its public API in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. The build fails (errors, not warnings) when a public member is added, changed or removed without a matching line (RS0016, RS0017 and related).
2. After the hide-internals pass, the surface of each package matches the disposition table below. No type in the "internal" column is public.
3. No public signature exposes a type from `Google.Protobuf`, `Grpc.*`, `Microsoft.IdentityModel.*` or `Azure.*` except where the package map says the package is the bridge to it (the allow-list below). A reflection test enforces this.
4. Every public type and member has an XML doc comment that is not empty. Build error otherwise. A lint test checks `<param>`, `<returns>`, `<typeparam>` and that no `<summary>` is empty.
5. The `.xml` doc file ships inside each `.nupkg`.
6. A written versioning policy defines what is a breaking change, how deprecation works, and what each version number promises. It is a docs page and is linked from the README and `CONTRIBUTING.md`.
7. During `1.0.0-preview.N` an API change is allowed, but the PR must list it under "Breaking changes" in `CHANGELOG.md`. At `1.0.0-rc.1` the API is frozen; from `1.0.0` package validation compares each package with the last stable one and fails on a break.
8. Interfaces that users implement or mock follow the "evolution rules" below, so adding a feature never breaks an existing implementation.
9. The 20-line hello agent and the 5-line client call need at most two `using` lines of `Ambinder.A2A.*` namespaces each (checked by the getting-started samples compiling with `ImplicitUsings`).
10. Analyzer level `latest` / mode `Recommended` is on for `src/`, with a short, commented list of exceptions.

## Non-goals

- The design of the new public types (agent model, client, stores, push, security): packages 05, 07, 08, 10, 11. This spec says what they must obey and records them in the API files.
- Packaging, versioning tool and release: package 01. (This spec decides *when* a version number changes; 01 does the tagging.)
- Docs site and API reference rendering: package 13 (DocFX). This spec makes the XML complete.
- The JSON/protocol correctness of the models: package 03.
- Performance rules and which performance APIs are banned: package 14. This spec owns the mechanism (banned-API analyzer) and the general bans.
- CodeQL, coverage, mutation tests: package 12.

## Anti-goals

- A tracking file that nobody reads because it is regenerated blindly on every build.
- An API frozen with plumbing types in it "because they were already public".
- `[EditorBrowsable(Never)]` or an `.Internal` namespace used as a substitute for `internal`.
- A semver policy so strict that every bug fix needs a new major, or so loose that a minor release breaks a user's implementation of `ITaskStore`.
- Docs generated from names only ("Gets or sets the Name").
- A public API that forces consumers to reference Google.Protobuf, Grpc or IdentityModel types.

## Constraints

- The first stable version is `1.0.0`; the first preview is `1.0.0-preview.1` (package 01). SemVer 2.0. One version for all packages (package 01).
- No `InternalsVisibleTo` between shipped assemblies. Allowed only to the matching `*.Tests` assembly and to `Ambinder.A2A.Benchmarks` (package 01, rule 6). Also allowed to the repo's `Ambinder.A2A.Integration.Tests` assembly, which is never shipped *(owner decision, 2026-11-03)*.
- One builder, no fixed dates (owner decision O2). The review gate for API changes is the automated public API diff check in required CI, plus the owner's admin approval for releases. No second human reviewer is required.
- `TreatWarningsAsErrors` is already on (`Directory.Build.props:6`). Nullable is on. The proto-generated code and `A2AJsonContext` output must not trip the doc analyzer (generated code is `internal` after this work, so CS1591 does not apply to it).
- Models are the A2A contract (spec v1.0.1, package 03). Their shape is dictated by the spec; this spec only fixes *how* they evolve.
- Current model style: `public sealed record` with `init` properties and `field ?? default` getters (`src/A2A.Protocol/Models/AgentTask.cs:4-12`). Non-positional records everywhere except `StoredTask` (`src/A2A.Server/Tasks/ITaskStore.cs:6`, positional) and `A2ACallContext`.
- Telemetry names, configuration keys, HTTP header names, JSON field names, error codes and `ErrorInfo` reasons are part of the contract (see "What counts as API").

## Design

### 1. What counts as API

Public API is anything a consumer can compile against or depend on at run time:

1. Public and protected types and members in the shipped assemblies (tracked by the API files).
2. **Wire contract:** JSON names and shapes, enum strings, header names, `ErrorInfo` reasons, REST paths, JSON-RPC method names. Changed only by a spec version change (package 03).
3. **Configuration:** option class names and property names, config section names (`A2A:...`) and their defaults. Changing a default that affects security or data is breaking and is called out in the changelog even in a minor release if it is a security fix.
4. **Telemetry:** `ActivitySource` and `Meter` names, span names, metric names and tag names. Dashboards depend on them.
5. **DI behavior:** which services `Add...` registers, their lifetimes, and which are registered with `TryAdd` (replaceable). Documented in the XML docs of the `Add...` method.
6. **Exceptions and codes:** the exception types a method throws and the A2A error codes. Message text is *not* API.
7. **Log event IDs:** the `EventId` of each log message (packages use `[LoggerMessage]` with fixed IDs, package 14). Message text is not API.

Not API: anything `internal`, the namespace of an internal type, private members, the exact text of messages, the order of log lines, the timing of events beyond what the XML docs promise.

### 2. Tracking public API

- Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PrivateAssets=all`) to every shipped project through `src/Directory.Build.props`. In `src/Directory.Build.targets`, add `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` as `AdditionalFiles`, and create them if they are missing. Each file starts with `#nullable enable`.
- Diagnostics set to **error**: RS0016 (not declared), RS0017 (declared but missing), RS0022, RS0025, RS0026 (overload and optional-parameter rules), RS0036, RS0037 (nullability annotations).
- RS0026 ("do not add multiple public overloads with optional parameters") stays on. Together with the rule "no new optional parameters on shipped methods" it removes a classic binary break.
- **Preview rule** *(writer's call)*: during `1.0.0-preview.N` nothing is moved to `Shipped`. All lines stay in `Unshipped`, so removing or changing an API during previews is a plain edit of one file and is visible in the PR diff. The "is it breaking" record for previews is the `CHANGELOG` "Breaking changes" section, reviewed by hand.
- **Freeze rule:** at `1.0.0-rc.1` the whole `Unshipped` file is merged into `Shipped`. From then on, a removal needs a `*REMOVED*` line in `Unshipped` and the policy below.
- **The review surface is the file diff.** `CODEOWNERS` lists the API owner for `PublicAPI.*.txt` in every package (package 01 creates the entries). A required CI check posts the public API diff on every PR that changes these files; the builder reads it before merging. The PR template has a checkbox "API files reviewed". There is no second human reviewer (owner decision O2).
- **Package validation:** `EnablePackageValidation` is on from package 01. At `1.0.0` set `PackageValidationBaselineVersion` to `1.0.0`; after that each release compares with the previous stable release. A break fails the pack job. A deliberate break (major release) sets the property to the new version in the same PR.
- **Workflow for contributors:** write the code, build (it fails), run the analyzer's "add to API file" fix for the whole solution (documented in `CONTRIBUTING.md`; the builder confirms whether `dotnet format analyzers --diagnostics RS0016` does it from the command line), read the diff, commit. Never regenerate a file wholesale.

### 3. Hiding internals (R6)

Rule: **a type is public only if a product team writes code against it, implements it, or configures it.** Everything else is `internal`, with `InternalsVisibleTo` only to tests and benchmarks.

Disposition of the current public types:

| Current public type | Disposition |
|---|---|
| `TaskManager` (public ctor with 10 parameters, `TaskManager.cs:40`) | internal. Reached through `IA2ARequestHandler` or the future handler seam (11). |
| `TaskEventBroker` | internal. Replaced by an event-bus interface (package 06); the interface is public, the in-process default is internal. |
| `PushNotificationSender` | internal. The public seam is the sender interface and the push store, designed in package 08. |
| `PushSigningKey` | internal. The public seam is a key-provider interface (package 08, R16). |
| `TracingRequestHandler` | internal. Tracing is always on when the DI registration is used; users do not wrap handlers by hand. |
| `PageToken`, `TaskStateRules` (its transition table is package 03 D13), `TaskWriter.Releaser` | internal. `PageToken`, `TaskStateRules` and `RequestValidation` are shared with other packages as linked source files compiled as `internal` into each one (package 01 rule 10, B12). |
| `RequestValidation`, `ServiceParameters`, `RestErrorWriter`, `GrpcErrorMapper`, `SseWriter`, `AgentCardInterfaces`, `WebhookUrlValidator`, `A2AGrpcService` | internal. (A public webhook URL validator may come back as part of package 08 if the receiver needs it; it is then listed there.) |
| `SseReader` | internal. |
| `RestA2AClient`, `GrpcA2AClient`, `JsonRpcA2AClient`, `TracingA2AClient` (and its `Inner`) | internal (R32). Created through DI or the factory; package 10 designs both. |
| `ProtoMapper` (all partials) and generated `Lf.A2a.V1.*` | internal. The `<Protobuf>` item gets `Access="Internal"`. No Google.Protobuf type is visible in any public signature. |
| `StrictEnumConverter<T>`, `UtcTimestampConverter`, `A2AJsonContext` | internal. |
| `A2AJson` | public, reduced to what store and extension authors need: `Options` (read-only `JsonSerializerOptions`), `TypeInfoResolver` (an `IJsonTypeInfoResolver` for `JsonTypeInfoResolver.Combine`, so an app with its own source-generated context, or an AOT app, can add A2A types), and a public typed `JsonTypeInfo<T>` accessor for every protocol type (for example `A2AJson.AgentTask`, `A2AJson.Message`; exact names left to the builder). Our other packages use these accessors on hot paths, so they need no internals. *(writer's call; typed accessors are Claude's call, B12)* |
| Protocol models (`AgentCard`, `AgentTask`, `Message`, `Part`, requests, responses, events, security types), `A2AException`, `A2AAuthException`, `A2AErrorCode`, `A2AError`, `A2AErrorInfo`, `A2AConstants` | public. Reviewed once in the freeze review. |
| `A2ATelemetryNames` | public (telemetry names are API). |
| `IAgentHandler`, `IAgentEventSink`, `AgentRequestContext`, `AgentRegistration`, `AgentCatalog`, `A2AServerBuilder`, `A2AServerOptions`, `PushOptions`, `ITaskStore`, `IPushConfigStore`, `StoredTask`, `InMemoryTaskStore`, `InMemoryPushConfigStore`, `IOwnerResolver`, `DefaultOwnerResolver`, `CallerIdentity`, `IAgentAuthorizer`, `AgentAuthorizer`, `ITaskEventObserver`, `IA2ARequestHandler`, `A2ACallContext` | public today. Each is reviewed by its owning package (05, 07, 08, 11). The owning spec states if it stays, changes or goes internal. The default implementations (`InMemory...`, `DefaultOwnerResolver`, `AgentAuthorizer`) become `internal` unless a user needs to construct them; users get them through `Add...` options. *(writer's call as a default)* |
| `IA2AClient`, `A2AClientOptions`, `A2ACardResolver`, `IA2ACredentialProvider` and the four providers | public today. Reviewed by package 10 (client) and 07 (credentials). |
| `GrpcHostingExtensions`, `A2AEndpointRouteBuilderExtensions`, `A2AServiceCollectionExtensions` | public as extension entry points, moved into the namespaces in section 5. |

An API file entry for a type exists only after its owning spec has accepted it. The hide-internals pass runs *before* the API files are first generated, so the first `Unshipped.txt` is small.

**Guard test (`PublicSurfaceTests`).** For each shipped assembly, load it with `MetadataLoadContext`, walk all public and protected members, and assert:

- no signature mentions an assembly outside the package's allow-list: Protocol allows only the BCL; Server and Client allow BCL and `Microsoft.Extensions.*` abstractions; AspNetCore adds `Microsoft.AspNetCore.*`; the gRPC packages allow `Grpc.Net.Client` / `Grpc.AspNetCore` types only on their own `Add...` options, never `Google.Protobuf`; `Entra` packages (including `Cluster.Entra`) and `Azure.KeyVault` allow `Azure.Core` (`TokenCredential`) and the authentication types of ASP.NET Core; `Extensions.AI` allows `Microsoft.Extensions.AI.Abstractions`; the store packages allow their database SDK types only on their own registration and model methods (`DbContext`, `ModelBuilder`, `CosmosClient`); `Push.Receiver`, `Cluster`, `Compat.V0_3.AspNetCore` and `Testing` allow ASP.NET Core types; `Testing.Conformance` allows xunit v3 types (the bridges are the rows of package 01's map);
- no public type lives in a namespace containing `Internal`;
- no public type is named in a "must be internal" list (the table above), so a regression is caught by name too.

### 4. Evolution rules (what keeps `1.x` compatible)

**Interfaces.** There are two kinds, and each has a rule.

- *Implementer-facing* interfaces: users write classes that implement them (`IAgentHandler`, `ITaskStore`, `IPushConfigStore`, `IOwnerResolver`, `ITaskContinuationPolicy`, `IA2ACredentialProvider`, client interceptors from 10, server middleware `IA2AMiddleware` from 09, extensions from 11, the cluster and event-bus seams from 06, the card seams from 03 and 04). Adding a member to one breaks every existing implementation. Rule: after `1.0.0`, add new behavior as a **default interface method** (as `IAgentHandler.CancelAsync` already does, `IAgentHandler.cs:35-36`) or as a **new interface** that a feature checks for (`if (store is ITaskStoreQuery q)`). Capability is discovered at run time and the default does the safe thing.
- *Consumer-facing* interfaces that users also mock (`IA2AClient`): same rule, because test doubles implement them. Convenience calls (send a text message, wait for completion) are **extension methods** in a static class, never new interface members. Package 10 follows this.

**Classes.** `sealed` unless inheritance is a designed extension point. A non-sealed public class needs a line in the XML docs saying how to inherit and which members may be overridden. `A2AException` is the only intended base class today; keep it. Default implementations are `sealed` and `internal` where possible.

**Records and models.**
- Public models are `sealed record` with `init` properties and **no positional constructor**. Positional parameters are API: adding one is a break, and so is reordering. `StoredTask` and `A2ACallContext` are changed to property records when their owner specs touch them.
- Adding a property to a model is a minor change. A new property must have a default that keeps old behavior and must not be `required` for existing callers. Whether inbound JSON fields are required is decided by package 03.
- A record that can hold a secret (`AuthenticationInfo`, `TaskPushNotificationConfig` and any new credential-holding type) overrides `PrintMembers` so `ToString()` and logs never show the secret. A test per such type checks it.

**Enums.** Adding a member is allowed in a minor release. Protocol enums already tolerate unknown wire values (package 03, R10). The XML docs of every public enum say "more values can be added; handle unknown values". Do not rely on numeric values.

**Methods.**
- Every async method ends in `Async`, takes `CancellationToken` as the last parameter (default allowed), and returns `ValueTask` or `Task` (package 14 sets when to use `ValueTask`).
- After `1.0.0`, no new optional parameters on an existing method. Add an overload instead. (Optional parameters are not binary compatible.)
- Return `IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IAsyncEnumerable<T>`, not `List<T>`. Take `IEnumerable<T>` / `ReadOnlySpan<T>` where it fits.
- Public methods validate arguments with `ArgumentNullException.ThrowIfNull` and friends, and throw `A2AException` with an `A2AErrorCode` for protocol failures. Error messages say what the caller must do (principle 3).

**Options.** Options classes are `sealed`, have a public parameterless constructor and settable properties, are bound from `A2A:...` config, and are validated at startup with `IValidateOptions<T>`. A failing validation names the property, the bad value, and the fix. New options get safe defaults. Removing or changing the meaning of an option is breaking.

**Dependencies in the API.** A third-party type in a public signature makes that library part of our contract. Only the allow-list in section 3 may do that. A major-version bump of a dependency whose types are in our API is a breaking change for us.

**Experimental API.** New or unsettled API after `1.0.0` carries `[Experimental("A2AEXP001")]` (BCL attribute) plus a doc line. Experimental API may change in any release. Each experimental feature has its own id (`A2AEXP002`, ...), documented in a table in the policy page. An experimental API leaves that state in a minor release with a changelog note. At `1.0.0`, nothing the work plan puts in M2 is experimental; this is for later work (for example A2A v1.1 features).

**Deprecation.** `[Obsolete("Use X instead.", DiagnosticId = "A2AOBS001")]`, with a unique id per obsolete API and a link in the message. The API stays at least one full minor release with a warning, and is removed only in the next major. A security-related change may shorten this, with a changelog note. *(writer's call)*

### 5. Naming and namespaces

- Registration methods: `AddA2A...` on `IServiceCollection`, in `namespace Microsoft.Extensions.DependencyInjection`. Mapping methods: `MapA2A...` on `IEndpointRouteBuilder`, in `namespace Microsoft.AspNetCore.Builder`. The web SDK imports both namespaces implicitly, so the hello agent needs no extra `using` for them. Analyzer rule IDE0130 (namespace does not match folder) is switched off for those files. *(writer's call; it is the usual .NET convention)*
- Each concern has one namespace, not one per folder. The Phase 1 namespaces (`A2A.Server.Agents`, `.Tasks`, `.Push`, `.Auth`, `.Hosting`, `.Telemetry`, `.Transports.*`) are collapsed in the review: the types a developer meets on day one (`IAgentHandler`, `AgentRequestContext`, the sink or its replacement) live in `Ambinder.A2A.Server`. Extension-point types live in `Ambinder.A2A.Server.Stores`, `.Push`, `.Security`. Each owning package uses this rule when it lists its types.
- Types that need a distinguishing prefix get `A2A` (`A2AException`, `IA2AClient`); do not prefix the others.
- Public interface names start with `I`; options end in `Options`; builders end in `Builder`; extension classes end in `Extensions`.

### 6. XML documentation (R5)

- `GenerateDocumentationFile=true` in `src/Directory.Build.props`. CS1591 is an error (via `TreatWarningsAsErrors`). The `.xml` file packs next to the DLL.
- **Required content:**
  - `<summary>`: what it is or does, in one or two plain sentences. Never just the name again.
  - `<param>`, `<returns>`, `<typeparam>` for every one. Say the valid range or allowed values where there is a limit.
  - `<exception>` for every exception a caller should expect: `A2AException` with the `A2AErrorCode`, and argument exceptions.
  - `<remarks>` for threading, lifetime, ordering and "what an implementer must guarantee" on every implementer-facing interface. `IAgentHandler` states that a new instance is created per execution and per cancel and that `HttpContext` is not available (R36). `ITaskStore` states the concurrency and ordering contract. Package 05 writes the store contract text; this rule makes it mandatory.
  - `<example>` on the entry points: `AddA2AServer`, `AddAgent`, `MapA2A`, `AddA2AClient`, `IAgentHandler`, `IA2AClient` main calls. The example is short and compiles (the docs build checks it, package 13).
  - Options properties state the default and the unit (`<value>` or in the summary).
  - Implementations use `<inheritdoc/>`.
- **Lint test (`XmlDocQualityTests`):** parses each shipped `.xml` and fails on an empty `<summary>`, a summary identical to the member name split into words, a missing `<param>` or `<returns>`, an `<inheritdoc/>` that points at nothing, and a `<see cref>` that does not resolve. The compiler warnings CS1570 to CS1574 and CS1584/CS1587 (malformed docs) are errors through `TreatWarningsAsErrors`.
- Text style: the repo style. Short, plain sentences, no jargon.

### 7. Analyzers and style

- `AnalysisLevel=latest`, `AnalysisMode=Recommended` for `src/`. `EnforceCodeStyleInBuild=true` with a short list of IDE rules set to error: unused usings (IDE0005), file-scoped namespaces, `readonly` fields where possible, no `var` rules (leave style to the editor). Exceptions are listed in `.editorconfig` with a comment each. *(writer's call)*
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `eng/BannedSymbols.txt`. General bans (this package): `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow` (use `TimeProvider`), `Thread.Sleep`, `Task.Wait`, `Task.Result`, `Task.GetAwaiter().GetResult()` on library paths, `new HttpClient()` in `src/` (use `IHttpClientFactory`; the client package owns a single documented exception), `Console.*` in `src/`. Performance bans (reflection-based `JsonSerializer` overloads, `Regex` without source generation) are listed by package 14 in the same file.
- `ConfigureAwait(false)` on every await in `src/` (CA2007 as error). The code already does this (for example `SseWriter.cs:29-32`). *(writer's call)*
- Logging: CA1848 and CA2254 as errors, so every log call uses `[LoggerMessage]` (also a performance rule in package 14).
- Trim and AOT analyzers (`IsAotCompatible`) are enabled by package 01.
- `.editorconfig` replaces the whitespace-only file (`.editorconfig:1-15`) and keeps those rules.

### 8. Versioning policy (the page this package writes)

Text for `docs/policies/versioning.md` (package 13 publishes it):

- **Numbers.** `MAJOR.MINOR.PATCH`, SemVer 2.0. All packages share one version.
- **Patch:** bug fixes, security fixes, doc fixes, performance fixes. No API change. Behavior changes only to match the spec or the docs.
- **Minor:** new API, new packages, new options with safe defaults, new experimental API, new spec features that are additive (for example A2A 1.1 features), deprecations.
- **Major:** removal of obsolete API, any break to a stable API, a raise of the minimum .NET version, a new A2A major spec version as the default.
- **Previews (`-preview.N`):** anything may change. Breaks are listed in the changelog. Preview packages are for early adopters, not for production dependencies of libraries.
- **Release candidates (`-rc.N`):** API frozen. Only fixes.
- **What is breaking:** removing or renaming a public member; changing a signature, return type, nullability annotation to a stricter one, or a default that changes behavior; adding a member to an implementer-facing or mocked interface without a default; adding an abstract member to a public class; changing a JSON name, header name, telemetry name, config key or error code; changing a service lifetime; raising a dependency's major version when its types are in our API.
- **What is not breaking:** a new class or method; a new property on a model; a new enum member; a new option with a default; a new optional wire field; anything internal.
- **Spec relation.** SDK 1.x implements A2A 1.x. Additive spec changes arrive in a minor SDK release. A2A 2.0 would be SDK 2.0. Choices where the spec is open are written in the "known deviations and choices" page (package 03) and changing one counts as a minor change with a changelog note, a break only if a stable behavior is removed.
- **Support.** The latest minor of the latest major gets fixes. The previous minor gets security fixes for three months after the next minor ships. The target is `net10.0` only (work plan). *(Claude's call, C2)*
- **Deprecation:** see section 4.

### 9. The M1 to M2 timeline

| Point | State |
|---|---|
| Now to first preview | Rename (01), hide internals (this package), generate the first API files, turn on XML docs and analyzers. |
| `1.0.0-preview.N` | Each package adds its API to its `Unshipped` file as it lands. Breaks go to the changelog. |
| API review (before `rc.1`) | One review pass by the builder over every package, driven by the API diff (no second reviewer, O2): names, sealing, nullability, async shape, options, namespaces, XML docs, the forbidden-type test. Output is a short list of fixes. All fixes land before `rc.1`. |
| `1.0.0-rc.1` | `Unshipped` merged into `Shipped`. Only fixes after this. |
| `1.0.0` | `PackageValidationBaselineVersion=1.0.0`. Policy page published. |

*(writer's call: the `rc` step is added; the work plan only names `1.0.0`. It costs one tag and gives a real soak window before the freeze is permanent.)*

### 10. Migration from current code

1. Package 01's rename and split land first.
2. Make the types in the disposition table `internal`; fix compile errors by adding `InternalsVisibleTo` for the test and benchmark assemblies only.
3. Add `Access="Internal"` to the `<Protobuf>` item (`src/A2A.Protocol.Grpc/A2A.Protocol.Grpc.csproj:15`, moved by 01) and check that no `Lf.A2a.V1` type remains public.
4. Add `A2AJson.TypeInfoResolver` and the typed `JsonTypeInfo<T>` accessors (B12); make `A2AJsonContext` internal.
5. Turn on the analyzers; generate the first API files; review the whole list once.
6. Turn on `GenerateDocumentationFile`; write the missing docs. Roughly the examples named in R5 (`IA2AClient`, `IA2ARequestHandler`, `AgentCard`, `AgentSkill`, `IPushConfigStore.GetAsync`, `AgentCatalog`) plus every model property.
7. Move `AddA2A...` / `MapA2A...` to the conventional namespaces.

Each removed or moved Phase 1 public member gets a row in the Phase 1 migration map (package 13).

## How it meets the six principles

1. **Protocol.** The wire contract is part of the tracked API (section 1). Protocol types are never edited without a spec reference (package 03). Check: the golden JSON tests run on every PR; renaming a JSON name is a "Breaking" changelog line.
2. **.NET enabler.** Nullable annotations are in the API files (`#nullable enable`, RS0036/RS0037). XML docs are mandatory. `AddA2A...` and `MapA2A...` live where .NET developers expect them. Check: the XML lint test and the getting-started samples with no extra `using`.
3. **Easy.** A small surface means less to learn: Phase 1 had 100+ public types (R4, counting generated ones); the surface test and review shrink it. Check: the hello agent and the client call stay within the line and `using` limits (Goal 9). Error messages in options validation state the fix (test: each `IValidateOptions` failure message contains a "set" or "add" instruction).
4. **Performance.** Hiding the transport helpers lets package 14 rework them freely. The banned-API list keeps slow patterns out (reflection JSON, `new HttpClient`, `DateTime.UtcNow` in hot paths). Check: the build fails on a banned call.
5. **Modular and extendable.** Seams are public interfaces with defaults; implementations are internal. The evolution rules make seams safe to extend. Check: the forbidden-type test proves a package does not leak its dependencies; the allow-list shows which packages are bridges.
6. **Integration pains.** Fixes "typed exceptions mapped across bindings" by making the exception hierarchy tracked API; "serialization drift" by publishing one `A2AJson` contract; "DI/host" by the registration-namespace rule and a review rule against new static singletons.

## Dependencies

- **Needs:** 01 (rename, split, props files, CODEOWNERS, pack, `EnablePackageValidation`).
- **Provides to all packages:** the API-file workflow, the evolution rules, the allow-list test, the XML-doc rules and lint, the banned-symbols file, the analyzer settings, the namespace rule, the versioning policy text.
- **Provides to 05, 06, 07, 08, 10, 11, 15:** the "implementer-facing interface" rules, so their seams can grow after `1.0.0`.
- **Provides to 13:** the versioning policy page and complete XML docs for DocFX; the API files as an index for the Phase 1 migration map.
- **Provides to 14:** the `BannedSymbols.txt` mechanism and the `InternalsVisibleTo` rule for the benchmarks assembly.
- **Needs from 03:** model shape decisions (required fields, unknown values); from 10: the client entry points that replace the public transport clients; from 08: the sender and key-provider interfaces.
- **Type names this package creates:** `A2AJson.Options`, `A2AJson.TypeInfoResolver`, the typed `JsonTypeInfo<T>` accessors on `A2AJson`; test classes `PublicSurfaceTests`, `XmlDocQualityTests`.

## Acceptance criteria

- [ ] Each shipped project has both API files; removing a line from `Unshipped` fails the build (RS0016) and adding an undeclared public member fails the build.
- [ ] No type in the "internal" rows of the table is public. `Lf.A2a.V1` types are not public in any assembly.
- [ ] `PublicSurfaceTests` passes: no forbidden dependency in a public signature, no `Internal` namespace, none of the named types public.
- [ ] `GenerateDocumentationFile` is on; the build has zero CS1591; `XmlDocQualityTests` passes; each `.nupkg` contains its `.xml` file.
- [ ] `XmlDocQualityTests` also checks that the entry points listed in section 6 have an `<example>`.
- [ ] Analyzer set (`AnalysisMode=Recommended`, banned symbols, CA2007, CA1848, CA2254) is on for `src/` and the solution builds clean with it.
- [ ] A secret-holding record test exists for `AuthenticationInfo` and `TaskPushNotificationConfig`: `ToString()` does not contain the credential.
- [ ] `AddA2A...` and `MapA2A...` are in the conventional namespaces; the hello agent sample compiles with no extra `using` for them.
- [ ] `docs/policies/versioning.md` exists, matches section 8, and is linked from README and CONTRIBUTING.
- [ ] Before `rc.1`: the API review list is closed; `Unshipped` is merged into `Shipped`.
- [ ] At `1.0.0`: `PackageValidationBaselineVersion=1.0.0`; a PR that removes a public member fails the pack job (proven by a test PR).
- [ ] `CODEOWNERS` routes `PublicAPI.*.txt` changes to the API owner, and a required CI check shows the public API diff on every PR that changes them (O2).
- [ ] `A2AJson` has a typed `JsonTypeInfo<T>` accessor for every public protocol type; a test checks that none is missing (B12).

## Left to the builder

- The exact `.editorconfig` severities beyond the ones named, and the commented exception list.
- The wording of XML docs (the rules fix the content, not the sentences).
- The exact content of `BannedSymbols.txt` beyond the entries named here and in package 14.
- How the forbidden-type test loads assemblies (`MetadataLoadContext` or a Roslyn/ILSpy walk).
- The command that applies the "add to API file" fix in bulk.
- The numbering of `A2AEXP` and `A2AOBS` diagnostic ids.

## Open questions

None. The support window (Q1) is Claude's call C2, in section 8.
