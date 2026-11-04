# SDK package 07: Security model and Entra — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Every subagent runs on model `sonnet` (see "Execution notes" at the end). Steps use checkbox (`- [ ]`) syntax for tracking.

**Milestone:** M1.

**Goal:** Make the server safe by default (owner keys per app or per user, no anonymous access, continuation only by the owner or a named grant holder, every task operation scoped by A2A tenant), drive authorization from the agent card, flow the caller identity into agent code, and ship Entra building blocks for the server (`Ambinder.A2A.AspNetCore.Entra`) and the client (`Ambinder.A2A.Client.Entra`).

**Spec (binding):** `docs/superpowers/specs/2026-11-03-sdk-07-security-entra-design.md` (called "the spec" below). Work plan: `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md` (Claude's calls B6–B11, C8, C13; owner decision O3). Package map: `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md` §1.

**Architecture:**
- Core contracts in `Ambinder.A2A.Server` (namespace `Ambinder.A2A.Server.Security`, no ASP.NET Core): `A2ACaller`, `CallerKind`, `OwnerKey`, `IOwnerResolver`, `DefaultOwnerResolver`, `ITenantResolver`, `TenantResolutionContext`, `ITaskContinuationPolicy`, `TaskContinuationGrant`, `ISecurityRequirementEvaluator`, `ISkillAuthorizer`, `IA2ACallerAccessor`, `A2ASecurityOptions`.
- Transport side in `Ambinder.A2A.AspNetCore`: authenticate, build the `A2ACaller`, resolve the tenant, apply the access mode (`AllowAnonymous`). It never authorizes.
- Authorization (agent-level card requirements, single-skill auto check, continuation policy pre-check) runs once as an `IA2AMiddleware` at step 3 of package 09's pipeline (09 §7.11), so JSON-RPC, REST, gRPC and in-process calls behave the same.
- `TaskManager` keeps the ownership checks in `LoadOwnedAsync` / `LoadForContinuationAsync`, now comparing `Caller.Owner` and the resolved tenant, and calling `ITaskContinuationPolicy` under the task lock.
- `Ambinder.A2A.AspNetCore.Entra`: JwtBearer + `Microsoft.IdentityModel.Validators`, Entra evaluator, `EntraCardSecurityContributor` (a package 03 `IAgentCardContributor`), policy helper, metadata-outage handling, `AddCallerAssertionForOnBehalfOf()`.
- `Ambinder.A2A.Client.Entra`: `EntraCredentialProvider` (moved from `AzureIdentityCredentialProvider`) with its own per-scope cache, `EntraOnBehalfOfCredentialProvider` with a user-assertion delegate (B10). Both implement package 10's `IA2ACredentialProvider`.

**Tech Stack:** .NET 10, ASP.NET Core authentication/authorization, `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14, `Microsoft.IdentityModel.JsonWebTokens` 8.23.0, `Microsoft.IdentityModel.Validators` (new; pin the same 8.x line as JsonWebTokens), `Azure.Identity` 1.21.0, xUnit v3, ASP.NET Core `TestServer`. Versions come from `Directory.Packages.props`; package 01 may have updated them — use what is there.

---

## 0. Assumptions and prerequisites

### 0.1 Package 01 has landed (assumed)

This plan assumes package 01 (rename and split) is merged into `master` before execution starts. It uses 01's names:

| Thing | Name after 01 |
|---|---|
| Solution | `Ambinder.A2A.slnx` |
| Server core | `src/Ambinder.A2A.Server/` (namespace `Ambinder.A2A.Server`, no ASP.NET Core) |
| Transports and hosting | `src/Ambinder.A2A.AspNetCore/` |
| gRPC server | `src/Ambinder.A2A.AspNetCore.Grpc/` |
| Client core | `src/Ambinder.A2A.Client/` |
| New in this plan | `src/Ambinder.A2A.AspNetCore.Entra/`, `src/Ambinder.A2A.Client.Entra/` (01 may already have created empty projects; reuse them) |
| Tests | `tests/Ambinder.A2A.<Package>.Tests` (01 naming rule); existing `Ambinder.A2A.Server.Tests`, `Ambinder.A2A.Integration.Tests`, `Ambinder.A2A.MockEntra.Tests` |
| Dependency allow-list | `eng/package-dependencies.json` and its test (01 §6) |
| Shared linked sources | `src/Shared/` (01 rule 10, B12) |

Paths to current code in this plan (for example `TaskManager.cs`, `TransportRequest.cs`) are given by **file name**. After 01's split, find them with `git ls-files | grep <FileName>`; the spec's line numbers refer to the pre-rename tree.

**Steps that depend on package 01** (they fail or need adapting if 01 is not merged):
- Task 0 (precondition check reads 01's layout).
- Every task: project paths, namespaces `Ambinder.A2A.*`, test project names, `Ambinder.A2A.slnx` in build commands.
- Task 3 and Task 6: the split of `TransportRequest`/transports into `Ambinder.A2A.AspNetCore` (the core must not reference ASP.NET Core).
- Task 13 and Task 18: creating `Ambinder.A2A.AspNetCore.Entra` and `Ambinder.A2A.Client.Entra` per the package map, their `eng/package-dependencies.json` entries, README, description.
- Task 18: 01's plan (Task 3) already creates `Ambinder.A2A.Client.Entra`, moves `AzureIdentityCredentialProvider` there (namespace `Ambinder.A2A.Client.Entra`) and removes `Azure.Identity` from `Ambinder.A2A.Client`. Task 18 starts from that state and only does what is left (see its Step 0).
- Task 12: after 01's split, `AgentAuthorizer` lives in `Ambinder.A2A.AspNetCore` (it uses ASP.NET Core's `IAuthorizationService`), while `IAgentAuthorizer` stays in the core. Task 12 keeps that split (§0.3 item 8).
- Task 24: the package-graph check builds on 01's allow-list test.

If Task 0 finds that 01 has **not** landed: stop and report. Do not do the rename here.

### 0.2 Other packages this plan touches

The spec needs seams from other packages (spec "Dependencies"). They are all M1 and may or may not be merged when this plan runs. Task 0 records which exist. Rules per seam:

| Seam | Owner | Needed by | If it is missing |
|---|---|---|---|
| `TaskKey`, `OwnerScope`, `TaskRef`, `StoredTask.Owner`, `StoredTask.ContinuationGrant`, `StoredTask.Caller`, `StoredCaller`, store conformance suite | 05 | Tasks 7, 8, 9, 10 | **Stop before Task 7** and report. Tasks 1–6 can land first as their own PR. Do not invent store shapes. |
| `IA2AMiddleware`, `A2AOperationContext`, built-in step 3 slot, per-agent options on `AddAgent` | 09 §7.11 | Tasks 11, 12 | **Stop before Task 11** and report. Do not build a pipeline (09 owns it). |
| Task recovery (`TaskRecoveryService`, `StoredTask.Caller` read on recovery) | 09 §7.7 | Task 10 (recovered-run test) | Write the unit test of the rebuild; mark the recovery integration test as a follow-up in the PR body. |
| `IAgentCardContributor`, `AgentCardContext` | 03 | Task 15 | **Stop before Task 15** and report. |
| `IA2ACredentialProvider.ApplyAsync(A2ACredentialContext, ct)`, `A2ACredentialContext` (`ForceRefresh`, `Challenge`), `AuthChallenge`, `A2ARequestOptions.UserAssertion`, `IA2AClientBuilder`, `DelegateCredentialProvider`, `IA2ACredentialSelector` | 10 | Tasks 18–20 | *(plan-writer's call)* Add exactly the shapes in 10's spec ("Credentials and auth retry", `IA2AClientBuilder`) to `Ambinder.A2A.Client`, with no retry logic. The spec allows this: "07 builds against them first". Copy member names and XML docs from spec 10 verbatim. |
| Repo-internal test harness (`tests/Ambinder.A2A.TestHarness`: `A2ATestServer`, `FakeTimeProvider` wiring) | 12 slice (B2) | All integration tests | Use `WebApplication.CreateSlimBuilder()` + `builder.WebHost.UseTestServer()` directly. **No fixed ports, no `Task.Delay`/sleeps** (B2). Use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) for expiry. |
| `PublicAPI.Unshipped.txt` tracking | 02 | Every task adding public API | If present: add each new public member to the package's `PublicAPI.Unshipped.txt` in the same task (build fails otherwise). If absent: skip; list the public API in the PR body. |
| `benchmarks/Ambinder.A2A.Benchmarks` | 14 | Task 25 | Skip Task 25, leave the acceptance item open, say so in the PR body. |
| Docs site tree (`docs/security/…`, `docs/agents/…`) | 13 | Task 26 | Create the pages at 13's paths with 13's front matter anyway. |
| TCK job | 03 | Task 27 | Skip the TCK run, say so in the PR body. |
| `Microsoft.IdentityModel.JsonWebTokens` removed from `Ambinder.A2A.Server` (01 keeps it in the core for push signing "until 08"; 08 plan Task 13 drops it) | 08 | Task 24 | Run Task 24 with the one documented exception in §0.3 item 9, and list "08 open" in the PR body. |

### 0.3 Decisions this plan makes (plan-writer's calls)

The spec leaves these open or splits them across sections. They are recorded here so the reviewer sees them:

**Owner review, 2026-11-03:** all nine decisions below are accepted as written. Item 7 is out of date in one detail: package 13's plan now lists `docs/security/other-auth-schemes.md` in `docs/security/toc.yml`, with 07 as owner.

1. **Continuation policy runs twice.** The middleware (Task 11) loads the task and runs `ITaskContinuationPolicy` before the handler, as specs 09 §7.11 step 3 and 11 §5 rule 3 require. `TaskManager.LoadForContinuationAsync` runs the same policy again under the task lock (spec "Ownership checks stay where they are today"). The second check closes the race where the grant is cleared (task left `AUTH_REQUIRED`) between the middleware check and the start of the run.
2. **Claim rules are shared source, not new public API.** The claim reading behind `OwnerKey` (tenant + app id, or tenant + user object id, short and mapped claim names) lives in `src/Shared/Security/OwnerClaims.cs`, linked as `internal` into `Ambinder.A2A.Server` (owner resolver) and available for 09's rate limiter in `Ambinder.A2A.AspNetCore` (B12 pattern). The spec's `OwnerKey` API is not extended.
3. **Reduced-caller mapping is internal.** `ReducedCaller.From(A2ACaller) → StoredCaller` and `ReducedCaller.ToCaller(StoredCaller) → A2ACaller` are internal to `Ambinder.A2A.Server`. Package 06 (M2) makes them public if it needs to.
4. **`FakeEntraAuthority`** lives in a non-packable project `tests/Ambinder.A2A.FakeEntra/`, served to JwtBearer through `JwtBearerOptions.BackchannelHttpHandler` (in-process, no ports). It is built from the token code in `samples/MockEntra/TokenIssuer.cs` (copied and generalized to many tenants, v1 and v2, app-only and delegated). MockEntra itself stays unchanged.
5. **Manual pre-release checklist** goes in `docs/release/pre-release-checklist.md` (create it if no other package has; append if one exists).
6. **`entra-live.yml` repository variables** (non-secret): `A2A_ENTRA_TENANT_A`, `A2A_ENTRA_TENANT_B`, `A2A_ENTRA_API_CLIENT_ID`, `A2A_ENTRA_CLIENT1_ID`, `A2A_ENTRA_CLIENT2_ID`, `A2A_ENTRA_MT_CLIENT_ID`.
7. **Other-schemes guide** goes in `docs/security/other-auth-schemes.md` (13's tree has no page for it).
8. **The extra-policy authorizer stays split across the two assemblies.** The spec says `IAgentAuthorizer` and `AgentAuthorizer` "become internal" and run inside the security path. After 01's split that cannot work as written: the security middleware is in `Ambinder.A2A.Server` (no ASP.NET Core), `AgentAuthorizer` needs `IAuthorizationService` and lives in `Ambinder.A2A.AspNetCore`, and an internal type cannot cross that boundary (01 rule 6: no `InternalsVisibleTo` between shipped assemblies). So:
   - `IAgentAuthorizer` stays a **public** interface in `Ambinder.A2A.Server` (package 02's plan also keeps it public). Its contract shrinks to the optional extra policy: `ValueTask AuthorizeAsync(A2ACaller caller, AgentRegistration agent, CancellationToken ct)`, which throws `A2AAuthException(403)`. Access modes and card requirements are no longer its job.
   - The core registers a no-op default with `TryAddSingleton`, so a host without ASP.NET Core still runs.
   - `AgentAuthorizer` (the `IAuthorizationService` implementation) stays in `Ambinder.A2A.AspNetCore`, becomes `internal sealed`, and `AddA2AServer` registers it, replacing the no-op. It evaluates `AgentRegistration.AuthorizationPolicy` against `caller.Principal`.
   - `SecurityMiddleware` calls `IAgentAuthorizer` after the card-requirement check and before the continuation pre-check.
9. **Package-graph exception until 08.** `Microsoft.IdentityModel.JsonWebTokens` matches the `Microsoft.Identity*` ban, and 01 leaves it in `Ambinder.A2A.Server` until 08's Task 13 removes it. Task 24's check allows exactly that one package in exactly that one assembly while `src/Ambinder.A2A.Server` still has push-signing code that uses it. The test checks this with `git grep` and fails once the code is gone and the reference is still there. Every other `Microsoft.Identity*` or `Azure.*` reference fails at once.

---

## Global Constraints

- **Branch:** work on the branch the session names; base it on `master` after Task 0 passes. One commit per task (Conventional Commits, for example `feat(security): add OwnerKey`).
- **Build rules:** `TreatWarningsAsErrors`, nullable on, central package management, `CS1591` (XML docs on every public member). Match surrounding style and comment density.
- **Core stays clean (principle 5):** `Ambinder.A2A.Server` and `Ambinder.A2A.Client` get no `Azure.*`, `Microsoft.Identity*`, `Microsoft.AspNetCore.*` package references. The one temporary exception is `Microsoft.IdentityModel.JsonWebTokens` in `Ambinder.A2A.Server`, which 01 left there and 08 removes (§0.3 item 9). This plan adds no such reference. `Ambinder.A2A.Client.Entra` does not reference `Ambinder.A2A.Server` (B10).
- **Package 02 rules** (`docs/superpowers/plans/2026-11-03-sdk-02-public-api.md`; 02 may land before or after this plan):
  - **No positional public types.** The only positional public types in the SDK are the six package 05 key structs (owner rule, 2026-11-03). `A2ACaller` and `TaskContinuationGrant` use `required` / `init` properties; spec 07 now shows them that way. Build them with object initializers (`new A2ACaller { Owner = ..., Kind = ..., Principal = ... }`).
  - **`A2ACallContext` becomes a property record in Task 3.** Spec 02 §4 says `A2ACallContext` is changed to a property record when its owner spec touches it, and Task 3 replaces its `User` and `Owner` members. Keep every other member and its name.
  - **`AgentRegistration` gets no new positional parameter.** Task 5 adds `Access` as an `init` property. Converting its existing positional parameters is package 11's job (the 02 API review checklist lists it).
  - **Visibility:** `DefaultOwnerResolver` stays public, because spec 07 ("API rules") keeps it public so other-scheme resolvers can decorate it; plan 02 no longer lists it as must-be-internal. `AgentAuthorizer` becomes `internal sealed` (decision 8). `ReducedCaller` stays internal (decision 3).
  - **No `Microsoft.IdentityModel.*` type in a public signature** of `Ambinder.A2A.AspNetCore.Entra` or `Ambinder.A2A.Client.Entra` (spec 02 Goal 3). ASP.NET Core authentication types (`JwtBearerOptions`, `AuthenticationBuilder`) and `Azure.Core` (`TokenCredential`) are allowed; 02's `PublicSurfaceTests` checks this.
  - **XML docs:** full docs on every public member, `<remarks>` with lifetime and concurrency on every implementer-facing interface (`IOwnerResolver`, `ITenantResolver`, `ITaskContinuationPolicy`, `ISecurityRequirementEvaluator`, `ISkillAuthorizer`, `IAgentAuthorizer`, `IA2ACallerAccessor`), and the "More values can be added in later versions; handle unknown values." remark on every public enum (`CallerKind`, `AgentAccess`).
  - **Logging:** `[LoggerMessage]` with fixed `EventId`s. `Ambinder.A2A.Server` and `Ambinder.A2A.AspNetCore` use their plan 02 blocks (1000–1999 and 2000–2999). `Ambinder.A2A.AspNetCore.Entra` uses 7000–7999 and `Ambinder.A2A.Client.Entra` 6000–6999, their fixed blocks in the 02 plan's Task 7.3 table (*cross-plan review*).
  - **API files:** each task that adds or removes public API updates `PublicAPI.Unshipped.txt` with the analyzer fix (already in each task). Removing `CallerIdentity` and the old `IOwnerResolver` shape, and the `A2ACallContext` change, are "Breaking changes" lines in `CHANGELOG.md` and rows in `docs/migration/phase1-map.csv`.
- **Never logged, traced, stored or put in exception messages:** access tokens, `Authorization` values, client assertions, user assertions, client secrets. `A2ACaller.AccessToken` is never copied into `StoredTask`/`StoredCaller`. Every test fixture that captures logs or spans runs the scrubber assertion (Task 23).
- **Errors say what to do:** every 401/403/startup message names what is missing and where to configure it (spec "Anti-goals"; exact texts are given in the tasks — use them verbatim).
- **Not-found hides existence:** another owner's task, another tenant's task, and a continuation without a valid grant all give `TaskNotFound` with the same message as a missing task.
- **Registration methods** live in `namespace Microsoft.Extensions.DependencyInjection`; `Map…` in `Microsoft.AspNetCore.Builder` (spec "API rules").
- **Pre-1.0:** no compatibility shims for removed types (spec "Migration").
- **Tests:** TDD — write the failing test, run it and see it fail, implement, run it and see it pass, commit. No fixed ports, no sleeps (B2). Integration tests that the spec says run "on all three transports" use a `[Theory]` over JSON-RPC, REST and gRPC.
- **No cloud (O3):** no test may need a real tenant except the `entra-live` tests, which skip visibly without their variables.

Run commands from the repo root:

```bash
dotnet build Ambinder.A2A.slnx
```

```bash
dotnet test Ambinder.A2A.slnx
```

## Review Focus

1. **Two users of one front-end app** (delegated tokens with the same `azp`, different `oid`) cannot see each other's tasks (Task 2, Task 6).
2. **`AUTH_REQUIRED` alone grants nothing.** Another app in the same Entra tenant gets `TaskNotFound` on continuation (Task 8). A grant is single-use and expires (Task 8).
3. **Grant holder after its call** gets `TaskNotFound` on Get, List, Subscribe and Cancel (B6, Task 8).
4. **Cross-tenant reads** (same owner, A2A tenant `a` vs `b`) give `TaskNotFound` on every operation, including push-config (Task 9).
5. **A card cannot advertise an unchecked requirement:** startup fails for an unknown scheme or a scheme with no evaluator (Task 12).
6. **Multi-tenant Entra:** issuer template + `AllowedTenants` + signing-key issuer check; `common` rejected; v1 rejected by default (Task 14).
7. **Hostile card scope:** the client never fetches a token for a scope taken from the card unless its resource is in `AllowCardScopes` (Task 19).
8. **No secrets in logs or spans** across all tests (Task 23).

---

## File map

Created (core, `src/Ambinder.A2A.Server/Security/`): `A2ACaller.cs`, `CallerKind.cs`, `OwnerKey.cs`, `IOwnerResolver.cs` (replaces the old one), `DefaultOwnerResolver.cs` (rewritten), `ITenantResolver.cs`, `TenantResolutionContext.cs`, `DefaultTenantResolver.cs`, `TaskContinuationGrant.cs`, `ITaskContinuationPolicy.cs`, `DefaultTaskContinuationPolicy.cs`, `ISecurityRequirementEvaluator.cs`, `OpenIdConnectRequirementEvaluator.cs`, `ISkillAuthorizer.cs`, `CardSkillAuthorizer.cs`, `IA2ACallerAccessor.cs`, `A2ACallerAccessor.cs`, `A2ASecurityOptions.cs`, `AgentAccess.cs`, `ReducedCaller.cs` (internal), `SecurityMiddleware.cs` (internal), `RequirementSet.cs` (internal), `SecurityStartupValidation.cs` (internal).
Created (shared): `src/Shared/Security/OwnerClaims.cs`.
Created: `src/Ambinder.A2A.AspNetCore.Entra/` (`EntraServerBuilderExtensions.cs`, `A2AEntraServerOptions.cs`, `EntraIssuerValidation.cs`, `EntraSecurityRequirementEvaluator.cs`, `EntraCardSecurityContributor.cs`, `EntraAuthorizationPolicyExtensions.cs`, `EntraMetadataOutage.cs`, `CallerAssertionExtensions.cs`, `README.md`).
Created: `src/Ambinder.A2A.Client.Entra/` (`EntraClientBuilderExtensions.cs`, `A2AEntraClientOptions.cs`, `A2AEntraOboOptions.cs`, `EntraCredentialType.cs`, `EntraCredentialProvider.cs`, `EntraTokenCache.cs` (internal), `EntraOnBehalfOfCredentialProvider.cs`, `README.md`).
Created (tests): `tests/Ambinder.A2A.FakeEntra/`, `tests/Ambinder.A2A.AspNetCore.Entra.Tests/`, `tests/Ambinder.A2A.Client.Entra.Tests/`, `tests/Ambinder.A2A.Entra.LiveTests/`, `tests/Shared/SecretScrubber.cs`, security test files in `Ambinder.A2A.Server.Tests/Security/` and `Ambinder.A2A.Integration.Tests/Security/`.
Created (CI/docs): `.github/workflows/entra-live.yml`, `docs/security/{threat-model,identity-and-ownership,tenants,entra-setup,other-auth-schemes}.md`, `docs/agents/multi-tenant.md`, `docs/release/pre-release-checklist.md`.
Modified: `A2ACallContext.cs`, `AgentRegistration.cs`, `AgentRequestContext.cs`, `IAgentEventSink.cs`, `AgentEventSink.cs`, `TaskManager.cs`, `TaskWriter.cs`, `A2AServerBuilder.cs`, `A2AServerOptions.cs`, `A2AStartupValidation.cs`, `TransportRequest.cs`, `JsonRpcEndpoint.cs`, `RestEndpoints.cs`, `A2AGrpcService.cs`, `AgentCardEndpoints.cs`, client `A2AClientFactory.cs`/`A2AClientOptions.cs`, `src/Ambinder.A2A.Client/*.csproj`, `Directory.Packages.props`, `eng/package-dependencies.json`, samples `AgentServer/Auth/*`, `AgentServer/Cards/AgentCards.cs`, `AgentServer/Program.cs`, `AgentClient/*`, `docs/production.md`.
Changed: `IAgentAuthorizer.cs` (core, stays public, narrowed to the extra policy; no-op default) and `AgentAuthorizer.cs` (in `Ambinder.A2A.AspNetCore` after 01, becomes internal), see §0.3 item 8.
Deleted: `Auth/CallerIdentity.cs`; client `AzureIdentityCredentialProvider.cs` (01 moved it to `Ambinder.A2A.Client.Entra`; Task 19 replaces it with `EntraCredentialProvider`), client `Auth/ClientCredentialsProvider.cs` and `Auth/ApiKeyCredentialProvider.cs` (moved to `samples/AgentClient`).

---

## Phase A — Core identity (needs 01 only)

### Task 0: Branch and precondition check

**Files:** none. The results go into the PR body, not into a file.

- [ ] **Step 1:** `git fetch origin && git checkout -B <session branch> origin/master`.
- [ ] **Step 2:** Confirm package 01:

```bash
test -f Ambinder.A2A.slnx && ls src | grep -E '^Ambinder\.A2A\.(Server|AspNetCore|Client)$'
```

```bash
git grep -nE 'namespace A2A\.' -- src | head
```

Expected: the slnx and three folders exist; the grep prints nothing. Otherwise stop and report "package 01 has not landed".
- [ ] **Step 3:** Record which seams from §0.2 exist (each grep prints a hit when present):

```bash
git grep -nE 'record struct TaskKey|record struct OwnerScope|ContinuationGrant|class StoredCaller|record StoredCaller' -- src
```

```bash
git grep -nE 'interface IA2AMiddleware|class A2AOperationContext' -- src
```

```bash
git grep -nE 'interface IAgentCardContributor' -- src
```

```bash
git grep -nE 'ApplyAsync\(A2ACredentialContext|interface IA2AClientBuilder|UserAssertion' -- src
```

```bash
ls tests | grep -E 'TestHarness'; ls benchmarks 2>/dev/null; git grep -l 'PublicAPI.Unshipped' -- src | head -3
```

- [ ] **Step 3a:** Check for stand-ins other packages added with 07's shape:

```bash
git grep -n "Owned by package 07" -- src
```

  If 05 added `OwnerKey.cs` or `TaskContinuationGrant.cs` (05 plan fallback table), do not recreate them or add duplicates. Tasks 1 and 8 finish those files: keep the names and members, add validation, XML docs and tests, and remove the header comment.
- [ ] **Step 4:** Write the results as a table in the PR body draft (seam → present/missing → what this run does, from §0.2). Run `dotnet build Ambinder.A2A.slnx` and `dotnet test Ambinder.A2A.slnx` to get a green baseline. No commit.

### Task 1: `OwnerKey` with a golden format test (B7)

**Files:**
- Create, or finish 05's minimal version if Task 0 Step 3a found it: `src/Ambinder.A2A.Server/Security/OwnerKey.cs`
- Test: `tests/Ambinder.A2A.Server.Tests/Security/OwnerKeyTests.cs`

- [ ] **Step 1: Write failing tests.**
  - `ForApp("t1","a1").Value == "tid:t1/app:a1"`; `ForUser("t1","u1").Value == "tid:t1/user:u1"` (golden: these two string literals are the frozen 1.0 format; the test's XML comment says "changing this breaks every stored task, see spec 07 B7").
  - `Parse("apikey:ops").Value == "apikey:ops"`; `Parse` round-trips both formats.
  - Null/empty arguments throw `ArgumentException`. A tenant or id containing `/` throws (would make the key ambiguous).
  - A value longer than 256 chars throws (spec: "opaque owner key, max 256 chars").
  - Equality is ordinal on `Value`.
- [ ] **Step 2:** Run `dotnet test tests/Ambinder.A2A.Server.Tests --filter "FullyQualifiedName~OwnerKeyTests"`. Expected: compile failure.
- [ ] **Step 3: Implement** exactly the spec shape:

```csharp
namespace Ambinder.A2A.Server.Security;

public readonly record struct OwnerKey
{
    public static OwnerKey ForApp(string tenantId, string appId);          // "tid:{tid}/app:{appid}"
    public static OwnerKey ForUser(string tenantId, string userObjectId);  // "tid:{tid}/user:{oid}"
    public static OwnerKey Parse(string value);
    public string Value { get; }
    public override string ToString() => Value;
}
```

XML docs state the format is part of the public contract and frozen at 1.0.
- [ ] **Step 4:** Run the tests. Expected: PASS. Add API lines to `PublicAPI.Unshipped.txt` if 02 is present.
- [ ] **Step 5:** Commit `feat(security): add OwnerKey with frozen string format`.

### Task 2: `A2ACaller`, `CallerKind`, claim rules, new `IOwnerResolver` and `DefaultOwnerResolver`

**Files:**
- Create: `src/Ambinder.A2A.Server/Security/{A2ACaller.cs,CallerKind.cs,IOwnerResolver.cs,DefaultOwnerResolver.cs}`, `src/Shared/Security/OwnerClaims.cs` (link into `Ambinder.A2A.Server.csproj` with `<Compile Include="../Shared/Security/OwnerClaims.cs" Link="Shared/Security/OwnerClaims.cs" />`, and into `Ambinder.A2A.AspNetCore.csproj` the same way for 09's limiter).
- Delete: old `Auth/IOwnerResolver.cs`, `Auth/DefaultOwnerResolver.cs`, `Auth/CallerIdentity.cs` (including `DefaultApiKeyAuthenticationType`).
- Test: `tests/Ambinder.A2A.Server.Tests/Security/DefaultOwnerResolverTests.cs` (replaces `Auth/OwnerResolverTests.cs`).

- [ ] **Step 1: Write failing tests** (spec acceptance "Unit tests for `DefaultOwnerResolver`"). Build principals with `new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"))`:
  1. Unauthenticated → `Kind = Anonymous`, `Owner = "anonymous"`.
  2. App-only with `tid` + `azp` → `Application`, `"tid:{tid}/app:{azp}"`, `AppId = azp`, `EntraTenantId = tid`, `UserObjectId = null`.
  3. App-only with `tid` + `appid` only (v1) → same with `appid`.
  4. App-only with neither → `A2AAuthException` 401, message exactly "App-only token has no azp or appid claim."
  5. Delegated (`scp` present) with `tid` + `oid` + `azp` → `User`, `"tid:{tid}/user:{oid}"`, `AppId = azp`, `UserObjectId = oid`.
  6. Two delegated principals, same `azp`, different `oid` → different owners (Review Focus 1).
  7. Delegated without `oid` → 401 "Delegated token has no oid claim."
  8. Authenticated, no `tid`, `AuthenticationType = "ApiKey"` → 401 "The caller was authenticated by scheme 'ApiKey', which the default owner resolver does not understand. Register an IOwnerResolver for this scheme (see docs: Security > Other schemes)."
  9. Mapped long claim names (`http://schemas.microsoft.com/identity/claims/tenantid`, `…/objectidentifier`, `http://schemas.microsoft.com/identity/claims/scope`) work as fallbacks.
  10. `Principal` on the result is a copy: mutating the original identity after resolve (add a claim) does not change `caller.Principal`.
  11. `AccessToken` is null (the resolver never sets it; Task 3 does when `KeepCallerToken` is on).
  12. `ToString()` hides the token *(cross-plan review: spec 02 "A record that can hold a secret ... overrides `PrintMembers` so `ToString()` and logs never show the secret. A test per such type checks it.")*. An `A2ACaller` with `AccessToken = "s3cr3t-token"`: `ToString()` does not contain `s3cr3t-token` but does contain `AccessToken = ***`. With `AccessToken = null` it contains `AccessToken = null`.
- [ ] **Step 2:** Run the filter `~DefaultOwnerResolverTests`. Expected: FAIL (compile).
- [ ] **Step 3: Implement.**
  - `CallerKind { Anonymous, Application, User, Other }` and `A2ACaller` exactly as in spec "Caller identity: `A2ACaller`", in `namespace Ambinder.A2A.Server.Security`. `A2ACaller` also gets a `private bool PrintMembers(StringBuilder builder)` (the form for a `sealed record`, as in the package 02 plan, Task 6). It writes every member except `AccessToken`, which it writes as `***` when set and `null` when not. Test 12 checks it. Add `public static A2ACaller Anonymous { get; }` only if the API review in 02 allows; otherwise construct inline.
  - `IOwnerResolver.ResolveAsync(ClaimsPrincipal user, AgentRegistration agent, CancellationToken ct)` per spec.
  - `OwnerClaims` (internal static): `TenantId(ClaimsPrincipal)`, `AppId(...)` (`azp`, else `appid`), `ObjectId(...)`, `IsDelegated(...)` (`scp` present), each with the mapped-name fallback. Also `TryGetPartitionKey(ClaimsPrincipal, out string)` returning `tid:{tid}/app:{appid}` or `tid:{tid}/user:{oid}` for 09's limiter (no `A2ACaller`, spec "Rate-limit key").
  - `DefaultOwnerResolver` (public, sealed, spec "API rules": stays public so other-scheme resolvers decorate it) applies rules 1–4 in order and builds keys through `OwnerKey`. Principal snapshot: `user.Clone()` (builder's choice per spec "Left to the builder"; test 10 proves the copy).
- [ ] **Step 4:** Fix compile errors in callers by switching them to the new interface (the transports change fully in Task 3; for now call `ResolveAsync` and use `.Owner`). Delete `OwnerResolverTests.cs`. Run `dotnet build Ambinder.A2A.slnx` then the filter. Expected: PASS.
- [ ] **Step 5:** Commit `feat(security)!: owner keys per app or per user; refuse unknown schemes`.

### Task 3: `A2ACallContext.Caller`, transport-side caller resolution, `KeepCallerToken`

**Files:**
- Create: `src/Ambinder.A2A.Server/Security/A2ASecurityOptions.cs`
- Modify: `A2ACallContext.cs`, `TransportRequest.cs`, `JsonRpcEndpoint.cs`, `RestEndpoints.cs`, `A2AGrpcService.cs`, `TaskManager.cs`, `A2AServerBuilder.cs`/`A2AServiceCollectionExtensions.cs`; 03's seam: `src/Ambinder.A2A.AspNetCore/Hosting/{IA2AVersionDispatcher.cs,IA2ACallerResolver.cs,DefaultA2ACallerResolver.cs}`, `src/Shared/TransportRequest.cs` (`DispatchCaller`), `src/Ambinder.A2A.AspNetCore/Transports/TransportRequest.Dispatch.cs`, `src/Ambinder.A2A.AspNetCore.Grpc/Transports/TransportRequest.Caller.cs`
- Test: `tests/Ambinder.A2A.Server.Tests/Security/CallContextTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Security/CallerResolutionTests.cs`

- [ ] **Step 1: Write failing tests.**
  - Integration (theory over the three transports, TestServer, test auth scheme that issues claims from a header): an agent that records `context.Caller` sees `Owner`, `Kind`, `EntraTenantId`, `AppId`, `UserObjectId` from the token claims.
  - With `A2ASecurityOptions.KeepCallerToken = true`, `Caller.AccessToken` equals the bearer value; with the default (`false`) it is null.
  - `A2ACallContext` has no `Owner` or `User` property any more (compile-level: the test uses `call.Caller.Owner` and `call.Caller.Principal`).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - `A2ASecurityOptions` (bound from `A2A:Security`; *placement next to `A2AServerOptions` is the builder's choice per spec*): `KeepCallerToken` (false), `Tenants` (per agent: `Dictionary<string, string[]>`), `TenantBindings` (`Dictionary<string, string[]>`), `FilterExtendedCardSkills` (true). XML docs from the spec bullets.
  - `A2ACallContext`: make it a property record (`required` / `init` properties, no positional constructor; spec 02 §4). Replace `User` and `Owner` with `A2ACaller Caller`; make `Tenant` a non-null `string` (`""` = no tenant, 05 §"`TaskManager` builds keys from `A2ACallContext.Tenant` (now required, `""` when none")). Keep `ProtocolVersion`, `RequestedExtensions`, `Transport`, and whatever 09 added (`IsForwarded`). Remove the "Phase 1 does not isolate by it" comment.
  - **Package 15 decision D1 (approved by the owner 2026-11-03).** This logic becomes a public seam, `IA2ACallerResolver.ResolveAsync(HttpContext http, string agentName, string? requestTenant, CancellationToken ct) → (A2ACaller Caller, string Tenant)` in `Ambinder.A2A.AspNetCore`. Then 0.3 compat (its gRPC service and its version dispatcher) uses the same caller and tenant rules as the 1.0 transports, and that seam has to merge before the `1.0.0-rc.1` freeze.
    - **This task makes the seam public** *(plan review 2026-11-04)*. 03's Task 8 has landed (merged with package 01, `02781b9`) with an owner decision recorded in the 03 plan: keep the three types internal with a stand-in record `DispatchCaller(ClaimsPrincipal User, string Owner)`, and "Package 07 Phase A swaps the record for `A2ACaller` and makes the three types public". So here:
      - Put the caller rules in the existing `DefaultA2ACallerResolver` (`src/Ambinder.A2A.AspNetCore/Hosting/`). Do not add a second class.
      - Replace `DispatchCaller` (`src/Shared/TransportRequest.cs`) with `A2ACaller` in `IA2ACallerResolver.ResolveAsync`, `A2AVersionDispatchContext.Caller` and the transports, then delete the record.
      - Make `IA2AVersionDispatcher`, `A2AVersionDispatchContext` and `IA2ACallerResolver` `public`. Add their lines to `PublicAPI.Unshipped.txt` if package 02's API files exist.
      - gRPC: `Ambinder.A2A.AspNetCore.Grpc` has its own copy of the rules (`Transports/TransportRequest.Caller.cs`), because the interface was internal to another assembly. Once it is public, resolve `IA2ACallerResolver` from DI in the gRPC service and delete the copy, so there is one implementation.
      - Check: `git grep -nE "public (sealed )?(interface IA2AVersionDispatcher|class A2AVersionDispatchContext|interface IA2ACallerResolver)" -- src` lists all three, and `git grep -n DispatchCaller -- src` prints nothing.
    - Task 9 adds tenant resolution to this class, so the seam returns the resolved tenant.
  - `TransportRequest.AuthorizeAsync` → `DefaultA2ACallerResolver.ResolveAsync(HttpContext http, string agentName, string? requestTenant, CancellationToken ct) → (A2ACaller Caller, string Tenant)` (the signature in the note above; it looks up the `AgentRegistration` by `agentName` itself); for this task the tenant is still the raw request tenant or `""` (Task 9 adds the resolver). It must not call `IAgentAuthorizer` any more for card requirements (authorization moves to Task 11), but keeps the existing `AuthorizationPolicy` call until Task 12 replaces it. When `KeepCallerToken` is on, read the bearer from `Authorization` (`Bearer ` prefix) and set `AccessToken` with `with { AccessToken = … }`.
  - gRPC: same through `ServerCallContext.GetHttpContext()`.
  - `TaskManager`: every `call.Owner` → `call.Caller.Owner`.
- [ ] **Step 4:** Update existing code and tests that construct `A2ACallContext` (grep `new A2ACallContext(`) to object initializers. Run the full suite. Expected: PASS except tests asserting old owner strings (`tenant:` / `apikey:` / `user:`); update those to the new format.
- [ ] **Step 5:** Commit `feat(security)!: A2ACallContext carries A2ACaller`.

### Task 4: `IA2ACallerAccessor` and `AgentRequestContext.Caller`

**Files:**
- Create: `Security/IA2ACallerAccessor.cs`, `Security/A2ACallerAccessor.cs` (internal `AsyncLocal<A2ACaller?>` implementation)
- Modify: `AgentRequestContext.cs`, `TaskManager.cs` (set around each run and cancel), service registration
- Test: `tests/Ambinder.A2A.Server.Tests/Security/CallerAccessorTests.cs`

- [ ] **Step 1: Write failing tests.**
  - Inside `IAgentHandler.ExecuteAsync`, a service resolved from the run scope that injects `IA2ACallerAccessor` sees `Current == context.Caller`.
  - It is still set after the HTTP request has ended: the agent awaits a gate the test releases after `SendMessage` with `returnImmediately = true` returned; `Current.Owner` is still the caller's (spec Goal 7, "runs that outlive the HTTP request").
  - Same for the cancel path.
  - Outside a run, `Current` is null. Two concurrent runs of different callers each see their own caller.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.** `public interface IA2ACallerAccessor { A2ACaller? Current { get; } }` (XML doc: "the caller of the agent run in progress"). Register the singleton. In `TaskManager`, wrap the agent invocation and the cancel invocation so the `AsyncLocal` is set on the background run's execution context, and restored after. Add `A2ACaller Caller` to `AgentRequestContext` next to `User` (spec "Package 11 exposes…; in M1 this package adds `Caller`"). If package 11 already reshaped `AgentRequestContext` into a class, add the property there instead.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(security): flow A2ACaller into agent runs`.

### Task 5: Access modes, anonymous refusal, Development default (C8)

**Files:**
- Create: `Security/AgentAccess.cs`
- Modify: `AgentRegistration.cs` (add `AgentAccess Access`), `A2AServerBuilder.cs` (per-agent `AllowAnonymous()`), `TransportRequest.cs`, `A2AStartupValidation.cs`, `TaskManager.cs` (anonymous `ListTasks`)
- Test: `tests/Ambinder.A2A.Integration.Tests/Security/AnonymousAccessTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Hosting/SecurityStartupTests.cs`

- [ ] **Step 1: Write failing tests.**
  - Theory over three transports, agent with default access and a registered auth scheme: unauthenticated `SendMessage`, `GetTask`, `ListTasks`, `CancelTask`, `SubscribeToTask`, the four push-config operations and `GetExtendedAgentCard` → 401 with `WWW-Authenticate` (HTTP, empty body) / `UNAUTHENTICATED` (gRPC). The public card endpoint and the push JWKS endpoint (if 08 has landed) → 200.
  - Agent with `AllowAnonymous()`: unauthenticated `SendMessage` works, owner `anonymous`; `ListTasks` returns an empty page with `nextPageToken = ""`; `GetTask` by id works; an authenticated caller on the same agent gets its own owner (not `anonymous`) and does not see the anonymous task.
  - Agent with `AllowAnonymous()`, on all three transports: unauthenticated `GetExtendedAgentCard` → 401 with `WWW-Authenticate` (HTTP) / `UNAUTHENTICATED` (gRPC), and an authenticated caller gets the card. `AllowAnonymous()` covers ordinary operations only; the extended card always needs an authenticated caller (spec 03; the ACTS run of package 16 expects exactly this, 16 plan Task 24; owner decision 2026-11-03).
  - Startup: environment `Production`, no authentication scheme registered, agent without access mode → `InvalidOperationException` with exactly "Agent 'x' requires authentication but no scheme is registered. Call AddEntraAuthentication(...) or mark the agent AllowAnonymous()."
  - Startup: environment `Development`, no scheme → starts; the agent behaves as `Anonymous`; one warning log per agent (count = number of agents).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - `public enum AgentAccess { Authenticated, Anonymous }`; default `Authenticated`. `AgentRegistration.Access` as an `init` property with a default (not a new positional parameter; package 02 rules in the global constraints); keep `AuthorizationPolicy`.
  - `AllowAnonymous()`: put it where per-agent configuration lives after 09 (`AddAgent<T>(…, o => …)` options or a per-agent builder). If `AddAgent` still returns `A2AServerBuilder` and there is no per-agent options type, add an optional `AgentAccess? access = null` parameter on `AddAgent` and stop to ask the owner whether to add a per-agent builder (do not invent one).
  - "Explicit access mode" is tracked (null = not set) so the Development rule only applies to agents with no explicit mode.
  - Access check in `TransportRequest.ResolveCallerAsync`: `Kind == Anonymous && effective access == Authenticated` → `A2AAuthException(401, "Authentication is required. Send a bearer token for agent 'x', or mark the agent AllowAnonymous().")`. Existing `WriteAuthFailureAsync` keeps the challenge behavior.
  - The extended card is the exception to the access mode: `GetExtendedAgentCard` requires `Kind != Anonymous` even when the effective access is `Anonymous` (same 401 and message shape). Keep this in the same access-check code, not in each transport (owner decision 2026-11-03).
  - Startup check in `A2AStartupValidation` using `IAuthenticationSchemeProvider.GetAllSchemesAsync()` and `IHostEnvironment.IsDevelopment()`.
  - `TaskManager.ListTasksAsync`: anonymous caller → empty `ListTasksResponse` with `NextPageToken = ""`, no store call.
- [ ] **Step 4:** Run. Expected: PASS. Run the full suite; existing flows that relied on open agents either register a test scheme or opt in with `AllowAnonymous()`.
- [ ] **Step 4a: Conformance servers opt in.** The TCK cannot send credentials, and neither can the ITK and ACTS runs. Without an opt-in, the `tck` and `acts` CI jobs fail after this task, and the normal `dotnet test` run does not show it.
  - Check: `git grep -nE "AddAgent<" -- tests/Ambinder.A2A.Tck.Sut itk 2>/dev/null`.
  - For each agent found in `tests/Ambinder.A2A.Tck.Sut` (package 03) and `itk/` (package 16), add `AllowAnonymous()` in the same commit. Each project uses only public APIs, so use the public `AllowAnonymous()` shape from Step 3.
  - If the TCK server exists, run it and fetch the card plus one unauthenticated `SendMessage` (JSON-RPC) to see it answer. If the `tck` workflow job exists, confirm it is green on this PR. If neither project exists yet, write in the PR body: "Packages 03 and 16: the TCK server and the ITK agent must call `AllowAnonymous()` (07 Task 5)."
- [ ] **Step 5:** Commit `feat(security)!: agents require authentication by default`.

### Task 6: Owner isolation across transports

**Files:** Test only: `tests/Ambinder.A2A.Integration.Tests/Security/OwnerIsolationTests.cs`

- [ ] **Step 1: Write the tests** (spec acceptance "Integration tests on JSON-RPC, REST and gRPC"), theory over the three transports:
  - Two users of one app (same `tid`, same `azp`, `scp` set, different `oid`): user B gets `TaskNotFound` on Get, Cancel, Subscribe and push-config of A's task, and A's task is not in B's `ListTasks`.
  - Two apps of one tenant (same `tid`, different `azp`, no `scp`): same assertions.
- [ ] **Step 2:** Run. Expected: PASS (Tasks 2–5 implemented the rules). If any fails, fix the cause in `TaskManager`/`PushConfigService`, not the test.
- [ ] **Step 3:** Commit `test(security): owner isolation on all transports`.

> **Checkpoint A.** Tasks 0–6 can be reviewed and merged as their own PR if 05 or 09 is not ready.

---

## Phase B — Store-scoped rules (needs 05)

### Task 7: Reduced caller written at create (B8)

**Files:**
- Create: `src/Ambinder.A2A.Server/Security/ReducedCaller.cs` (internal)
- Modify: `TaskManager.cs`/`TaskWriter.cs` (set `StoredTask.Caller` at create)
- Test: `tests/Ambinder.A2A.Server.Tests/Security/ReducedCallerTests.cs`

- [ ] **Step 1: Write failing tests.**
  - `From(caller)` copies owner key, Entra tenant id, app id, user object id, roles (`roles` claims) and scopes (`scp` split on spaces). It never contains the access token, even when `caller.AccessToken` is set (assert on every string property and on the JSON of the stored task).
  - `ToCaller(stored)` returns an `A2ACaller` whose `Principal` holds only those claims (`tid`, `azp`, `oid`, `roles`, `scp`), `AccessToken` null, `Kind` derived (user object id → `User`; app id → `Application`; owner `anonymous` → `Anonymous`; else `Other`).
  - A new task in the in-memory store has `Caller` set at create.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement against 05's `StoredCaller` exactly as 05 defines it (do not add fields).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(security): store the reduced caller with each task`.

### Task 8: Continuation grants and `ITaskContinuationPolicy` (B6)

**Files:**
- Create: `Security/ITaskContinuationPolicy.cs`, `Security/DefaultTaskContinuationPolicy.cs`
- Create, or finish 05's minimal version if Task 0 Step 3a found it: `Security/TaskContinuationGrant.cs`
- Modify: `IAgentEventSink.cs` (new overload), `AgentEventSink.cs`, `TaskWriter.cs` (set and clear the grant), `TaskManager.cs` (`LoadForContinuationAsync` uses the policy under the lock), test doubles implementing `IAgentEventSink`
- Test: `tests/Ambinder.A2A.Server.Tests/Security/ContinuationPolicyTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Security/ContinuationGrantTests.cs`

- [ ] **Step 1: Write failing tests** (spec acceptance "Continuation tests", "A grant holder…", "AUTH_REQUIRED alone grants nothing"). Use `FakeTimeProvider` for expiry.
  - Unit (`DefaultTaskContinuationPolicy.CanContinue`): same owner, any non-terminal state → true; other owner, `AUTH_REQUIRED`, in grant, before expiry, same A2A tenant → true; after expiry → false; other owner not in grant → false; grant present but state `WORKING` or `INPUT_REQUIRED` → false; different A2A tenant → false.
  - Integration (three transports): agent calls `RequireAuthAsync(msg, new TaskContinuationGrant { AllowedOwners = [OwnerKey.ForApp(tid, "approver")], ExpiresAt = now + 10 min }, ct)`.
    - The approver app continues → the task proceeds; the approver receives the blocking result; in a streaming variant it receives the stream up to the terminal state.
    - Afterwards the approver gets `TaskNotFound` on `GetTask`, `CancelTask`, `SubscribeToTask`; the task is absent from its `ListTasks` (B6).
    - The task's owner is still the original owner.
    - A second continuation by the approver after the task left `AUTH_REQUIRED` → `TaskNotFound` (grant cleared, single use).
    - Approver after `ExpiresAt` (advance the fake clock) → `TaskNotFound`.
    - Another app in the same Entra tenant, no grant → `TaskNotFound` (today's "same tenant" rule is gone).
    - `RequireAuthAsync` without a grant → only the owner can continue.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Types exactly as in spec "Continuing an `AUTH_REQUIRED` task". `ExpiresAt` has no default (a `required` `init` property; spec 02 §4).
  - `IAgentEventSink.RequireAuthAsync(Message statusMessage, TaskContinuationGrant? grant, CancellationToken ct)`: sets state `AUTH_REQUIRED` with the status message and stores `StoredTask.ContinuationGrant = grant` in the same write. Reject an `ExpiresAt` not in the future with `A2AException(InvalidAgentResponse, "TaskContinuationGrant.ExpiresAt must be in the future.")`.
  - `TaskWriter`: on any state change away from `AUTH_REQUIRED`, write `ContinuationGrant = null` in the same store write.
  - `DefaultTaskContinuationPolicy` (registered with `TryAddSingleton`, takes `TimeProvider`). The tenant check compares the call's resolved tenant with `StoredTask.Key.Tenant` — pass it via the caller-side data the interface allows: since `CanContinue(StoredTask task, A2ACaller caller)` has no tenant parameter, `TaskManager` only calls the policy for tasks it loaded by `TaskKey(call.Tenant, …)`, so a task of another tenant is never loaded (Task 9). Document this in the XML doc of `ITaskContinuationPolicy`.
  - `LoadForContinuationAsync`: `stored is null || !_policy.CanContinue(stored, call.Caller)` → `TaskNotFound` (same message as missing). Remove `IOwnerResolver.CanContinueInterrupted` usage and the `owners` constructor parameter of `TaskManager`; inject `ITaskContinuationPolicy` instead.
- [ ] **Step 4:** Run. Expected: PASS. Run the full suite.
- [ ] **Step 5:** Commit `feat(security)!: continuation only by owner or a named, expiring grant`.

### Task 9: Tenant resolution and isolation

**Files:**
- Create: `Security/ITenantResolver.cs`, `Security/TenantResolutionContext.cs`, `Security/DefaultTenantResolver.cs`
- Modify: `TransportRequest.cs` (call the resolver), `RestEndpoints.cs` (pass the route tenant), `A2AGrpcService.cs` (request field), `TaskManager.cs` and `PushConfigService.cs` (keys use `call.Tenant`)
- Test: `tests/Ambinder.A2A.Server.Tests/Security/DefaultTenantResolverTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Security/TenantIsolationTests.cs`, and conformance cases in 05's suite

- [ ] **Step 1: Write failing tests** (spec acceptance "Tenant tests", Goal 4).
  - Unit: route tenant `a` and body tenant empty → `a`; route `a`, body `b` → `InvalidParams`; no route, body `x` not configured → `InvalidParams` with "Tenant 'x' is not served by agent 'y'."; no route, body `a` configured in `A2ASecurityOptions.Tenants["y"]` → `a`; configured from the card's `supportedInterfaces[].tenant` → accepted, except that a gRPC interface's `tenant` in `Tenant` routing mode is a routing value and never counts (09 plan Task 8), so an agent-name routing value sent over REST → `InvalidParams`; agent with no tenants and empty body → `""`; agent with no tenants and body `a` → `InvalidParams`.
  - Unit: `TenantBindings["a"] = ["tidA"]`, caller with `EntraTenantId = "tidB"` → `A2AAuthException` 403; caller `tidA` → OK.
  - Integration (three transports): a task created under tenant `a` → under tenant `b`, same owner: `GetTask`, `ListTasks`, `CancelTask`, `SubscribeToTask` and push-config get/list/delete → `TaskNotFound` (list: not present). gRPC uses the request field.
  - Store conformance: if 05's suite does not already contain "same task id, different tenant, not visible" and "list scoped by tenant even without contextId" cases, add them to 05's case catalog (spec "Dependencies: The conformance suite must include the tenant-isolation and owner-isolation cases from Goal 4").
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 2a: One tenant setting.** The package 03 plan (Task 14, owner decision 4: "package 07 may replace it") adds `A2AServerOptions.InterfaceTenants` (agent name → one tenant). It writes that tenant on every card interface of the agent and rejects a mismatched request tenant with `InvalidParams`. Check: `git grep -n "InterfaceTenants" -- src tests`.
  - **Found:** `A2ASecurityOptions.Tenants` becomes the only setting. Remove `InterfaceTenants`; move its callers and tests to `Tenants`. When an agent has exactly one configured tenant, `AgentCardInterfaces` writes it on each interface, as 03 did, so 03's "card lists the tenant on all three interfaces" test still passes. With several tenants, leave the interface `tenant` empty and say so in `docs/spec-decisions.md`. Keep this plan's message ("Tenant 'x' is not served by agent 'y'.") for an unknown tenant. Change 03's tenant-mismatch test, and its `docs/spec-decisions.md` row, to the message `DefaultTenantResolver` gives for route `a` with body `b`. There is one resolver and one message on every transport. The 03 tests in `Conformance/TenantTests.cs` must pass after the change, with only the setting name and message text changed.
  - **Not found** (03 has not run yet): nothing to remove. Write in the PR body: "Package 03 Task 14: use `A2ASecurityOptions.Tenants` and `DefaultTenantResolver` instead of adding `A2AServerOptions.InterfaceTenants`."
- [ ] **Step 3: Implement** per spec "Tenant isolation". Register `DefaultTenantResolver` with `TryAddSingleton`. `TransportRequest.ResolveCallerAsync` now returns the resolved tenant; transports pass it to `A2ACallContext.Tenant`. `TaskManager` builds every `TaskKey`/`OwnerScope`/`TaskRef` from `call.Tenant`.
- [ ] **Step 4:** Run. Expected: PASS. Run the full suite.
- [ ] **Step 5:** Commit `feat(security): trusted tenant resolution and tenant isolation`.

### Task 10: Recovered and forwarded runs see the reduced caller

**Files:**
- Modify: wherever 09's recovery (and later 06's forwarding) builds `AgentRequestContext` for a run without a live request.
- Test: `tests/Ambinder.A2A.Server.Tests/Security/RecoveredCallerTests.cs`

- [ ] **Step 1: Write failing tests** (spec acceptance "A recovered or forwarded run sees the reduced caller…").
  - If 09's `TaskRecoveryService` exists: a task with a stored caller is recovered (`Restart` policy); the agent sees `Caller.Owner` equal to the stored owner, `Caller.AccessToken == null`, and `IA2ACallerAccessor.Current` equal to it.
  - OBO in such a run: covered in Task 21 (the adapter returns no assertion; the provider throws the documented message).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Use `ReducedCaller.ToCaller(stored.Caller)` in the recovery path and set the caller accessor around the recovered run.
- [ ] **Step 4:** Run. Expected: PASS. If 09's recovery is not merged: keep only the `ReducedCaller` unit tests from Task 7 and list "recovered-run caller test" as a follow-up in the PR body. Forwarded runs are package 06 (M2); list it as 06's follow-up.
- [ ] **Step 5:** Commit `feat(security): rebuild the reduced caller for recovered runs`.

---

## Phase C — Card-driven authorization (needs 09 §7.11)

### Task 11: Requirement evaluation and the security middleware

**Files:**
- Create: `Security/ISecurityRequirementEvaluator.cs`, `Security/OpenIdConnectRequirementEvaluator.cs`, `Security/EffectiveCardSecurity.cs` (internal), `Security/RequirementSet.cs` (internal), `Security/SecurityMiddleware.cs` (internal)
- Modify: server registration (register the middleware in 09's built-in step 3 through `A2AServerBuilder.UseAuthorizationMiddleware<SecurityMiddleware>()`, the slot-300 hook the 09 plan adds for this package, Task 3, "for package 07; one only". If 09 landed with a different name for the hook, use 09's name and do not add a second way in) *(cross-plan review)*
- Test: `tests/Ambinder.A2A.Server.Tests/Security/RequirementSetTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Security/CardRequirementTests.cs`

- [ ] **Step 1: Write failing tests** (spec acceptance "Requirement tests: OR and AND semantics").
  - Unit (`RequirementSet.IsSatisfiedAsync` with fake evaluators): `[]` → satisfied; `[{A}]` with A true → satisfied; `[{A},{B}]` with A false, B true → satisfied (OR); `[{A,B}]` with A true, B false → not satisfied (AND); scopes are passed through to the evaluator per scheme.
  - Built-in `openIdConnect` evaluator: any authenticated caller → true; anonymous → false.
  - Integration (three transports + an in-process call through `IA2ARequestHandler`): agent card requires scheme `custom` with a test evaluator that checks a role; caller without the role → 403 / `PERMISSION_DENIED` on every operation except the card fetch; caller with the role → OK. A test middleware registered by the app (09 step 5) observes that it runs after the security check (it never sees the denied call).
  - Continuation pre-check: a `SendMessage` with a `taskId` the caller may not continue → `TaskNotFound` before the handler runs (the handler's invocation counter stays 0).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - `ISecurityRequirementEvaluator` exactly as in spec "Card-driven authorization".
  - `EffectiveCardSecurity` (internal, `Ambinder.A2A.Server`) is the one source of what a card requires *(Claude's call: the Entra contributor lives in another assembly, and 01 rule 6 rules out `InternalsVisibleTo`, so no new public type is needed)*. At startup, for each agent and each card (public, extended), it serializes the card model to a `JsonObject`, runs every registered `IAgentCardContributor` with `AgentCardContext.Request = null` (and `IsExtended = true` for the extended card; this JSON is only read here, never served), and reads `securitySchemes` and `securityRequirements` from the result. `RequirementSet` and `SecurityStartupValidation` (Task 12) read it, and never the raw `AgentCard` model. Otherwise Entra's generated requirements would be served and not enforced: an empty list means no requirement.
  - `RequirementSet` (internal): built once per agent and per card (public, extended) from `EffectiveCardSecurity`, with the evaluator chosen per scheme at build time (first registered evaluator whose `CanEvaluate` is true; no per-call lookup — performance budget, spec principle 4).
  - `SecurityMiddleware : IA2AMiddleware`, in 09's step 3: (1) skip card operations; (2) agent-level `RequirementSet` → `A2AAuthException(403, "The caller does not meet the security requirements of agent 'x'. Required (any of): [entra: api://…/.default] | [entraUser: …].")` (lists scheme names and scopes from the card, never the caller's claims); (3) single-skill auto check (Task 12); (4) continuation pre-check for `SendMessage`/`SendStreamingMessage` with a `taskId` (load by `TaskKey(call.Tenant, agent, taskId)`, then `ITaskContinuationPolicy`; `TaskNotFound` on deny). The authoritative check stays in `LoadForContinuationAsync` (§0.3 item 1).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(security): enforce card security requirements in the middleware pipeline`.

### Task 12: Startup checks, skill authorization, extended card filter, the extra-policy authorizer

**Files:**
- Create: `Security/ISkillAuthorizer.cs`, `Security/CardSkillAuthorizer.cs`, `Security/SecurityStartupValidation.cs` (internal)
- Modify: `AgentRequestContext.cs` (`AuthorizeSkillAsync`), `TaskManager.cs` (403 before first event vs `REJECTED` after), `AgentCardEndpoints.cs` / extended card handler, `IAgentAuthorizer.cs` (core: narrowed contract, no-op default), `AgentAuthorizer.cs` (in `Ambinder.A2A.AspNetCore`: internal, registered by `AddA2AServer`), `SecurityMiddleware.cs` (calls `IAgentAuthorizer`), per §0.3 item 8
- Test: `tests/Ambinder.A2A.Integration.Tests/Security/SkillAuthorizationTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Hosting/SecurityStartupTests.cs` (extend)

- [ ] **Step 1: Write failing tests.**
  - Startup: a requirement (agent level, skill level, public or extended card) naming a scheme absent from `securitySchemes` → `InvalidOperationException` naming agent, skill (if any) and scheme. A scheme present but with no evaluator → `InvalidOperationException` "Agent 'x' skill 'y' requires scheme 'z', but no ISecurityRequirementEvaluator handles it. Register one (AddEntraAuthentication registers the Entra schemes) or remove the requirement."
  - Single-skill card whose skill requires role `Reports.Approve` → a caller without it gets 403 on `SendMessage` (checked with the agent level).
  - Multi-skill card: the agent calls `context.AuthorizeSkillAsync("approve")` as its first action → 403 for an unauthorized caller (blocking and streaming, before any event). The agent emits `WORKING` first, then calls it → the task ends `REJECTED` with status message text exactly "Not authorized for skill 'approve'." (no claims in it).
  - Extended card: unauthenticated → 401; with `FilterExtendedCardSkills = true` (default) the returned skills are only those whose requirements the caller meets; with `false` all skills.
  - Optional `AuthorizationPolicy` on an agent still applies on top, on all three transports and for in-process calls through the pipeline (existing tests keep passing): a caller who meets the card but not the policy gets 403 / `PERMISSION_DENIED`.
  - The core alone (`AddA2AServerCore` or whatever 01 named the host-neutral registration; an `AgentHarness`-style host with no ASP.NET Core) resolves the no-op `IAgentAuthorizer` and runs; an agent with an `AuthorizationPolicy` on such a host fails startup with "Agent 'x' sets AuthorizationPolicy, which needs ASP.NET Core authorization. Use AddA2AServer from Ambinder.A2A.AspNetCore or remove the policy." *(plan-writer's call: a policy that is silently skipped would be a hole)*.
  - `AgentAuthorizer` is not a public type (reflection over `Ambinder.A2A.AspNetCore`'s exported types).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement** per spec. `ISkillAuthorizer` exactly as in the spec; `CardSkillAuthorizer` uses the per-skill `RequirementSet`. `AgentRequestContext.AuthorizeSkillAsync(string skillId, CancellationToken ct = default)` resolves `ISkillAuthorizer` from the run scope. In `TaskManager`, an `A2AAuthException` thrown by the agent before its first event completes `FirstResult` with that exception (the transport answers 403); after the first event it is turned into `REJECTED` with the message above. Delete the "one ASP.NET policy per agent" main-path code in `TransportRequest`. Implement §0.3 item 8: narrow `IAgentAuthorizer`, register the no-op in the core and `AgentAuthorizer` in `AddA2AServer`, and call it from `SecurityMiddleware`. Do not add `InternalsVisibleTo` between `src/` assemblies.
- [ ] **Step 4:** Run. Expected: PASS. Run the full suite.
- [ ] **Step 5:** Commit `feat(security): skill-level requirements, startup checks, filtered extended card`.

---

## Phase D — Server Entra (`Ambinder.A2A.AspNetCore.Entra`)

### Task 13: `FakeEntraAuthority` and the Entra package skeleton

**Files:**
- Create: `tests/Ambinder.A2A.FakeEntra/Ambinder.A2A.FakeEntra.csproj` (`IsPackable=false`), `FakeEntraAuthority.cs`, `FakeTokenRequest.cs`
- Create: `src/Ambinder.A2A.AspNetCore.Entra/Ambinder.A2A.AspNetCore.Entra.csproj` (refs: `Ambinder.A2A.AspNetCore`, `Ambinder.A2A.Client.Entra` — add that reference in Task 21 once the project exists; packages `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.Validators`), `README.md`
- Create: `tests/Ambinder.A2A.AspNetCore.Entra.Tests/`
- Modify: `Directory.Packages.props` (add `Microsoft.IdentityModel.Validators`), `eng/package-dependencies.json`, `Ambinder.A2A.slnx`
- Test: `tests/Ambinder.A2A.AspNetCore.Entra.Tests/FakeEntraAuthorityTests.cs`

- [ ] **Step 1: Write failing tests** for the fixture itself: it serves `/{tenant}/v2.0/.well-known/openid-configuration` and the JWKS through an `HttpMessageHandler`; it issues tokens (v2 issuer `{instance}{tid}/v2.0`, v1 issuer `https://sts.windows.net/{tid}/`), app-only (`azp`, `roles`, no `scp`) and delegated (`scp`, `oid`, `azp`); several tenants; tokens validate with `JsonWebTokenHandler` against its keys; it can be switched to "metadata unavailable" (handler throws `HttpRequestException`).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement from `samples/MockEntra/TokenIssuer.cs` (key generation, JWKS shape, `JsonWebTokenHandler.CreateToken`), generalized. Fluent API is the builder's choice (spec "Left to the builder"), for example `authority.AppToken(tenant, appId, roles: [...], audience: ...)`, `authority.UserToken(tenant, oid, appId, scopes: [...])`. Create the empty `Ambinder.A2A.AspNetCore.Entra` project with README (what, install, 10-line example, link; 01 §4) and its allow-list entry.
- [ ] **Step 4:** `dotnet build Ambinder.A2A.slnx` and run the fixture tests. Expected: PASS.
- [ ] **Step 5:** Commit `test(entra): add FakeEntraAuthority; scaffold AspNetCore.Entra`.

### Task 14: `AddEntraAuthentication` — token validation, tenants, startup checks, metadata outage

**Files:**
- Create: `A2AEntraServerOptions.cs`, `EntraServerBuilderExtensions.cs`, `EntraIssuerValidation.cs`, `EntraMetadataOutage.cs`
- Test: `tests/Ambinder.A2A.AspNetCore.Entra.Tests/EntraAuthenticationTests.cs`, `EntraStartupTests.cs`

- [ ] **Step 0: Confirm the validator API.** In the pinned `Microsoft.IdentityModel.Validators`, check that `AadIssuerValidator` validates v2 issuers with the `{tenantid}` template and that the signing-key issuer check is available (`TokenValidationParameters.EnableAadSigningKeyIssuerValidation()` or the equivalent). Write the finding in the commit message. If it does not cover them, write an equivalent internal validator (about 30 lines, spec "writer's call") and test it with the same cases.
- [ ] **Step 1: Write failing tests** (spec acceptance "Entra server tests with `FakeEntraAuthority`" and the error table), host on TestServer, post-configure `JwtBearerOptions.BackchannelHttpHandler` to the fake authority's handler:
  - Single tenant: valid v2 app token with `A2A.Invoke` → 200; wrong audience → 401; token for tenant B → 401; expired (beyond `ClockSkew`) → 401; v1 token → 401; v1 with `AcceptV1Tokens = true` → 200.
  - Both `ClientId` and `AppIdUri` (`api://{ClientId}` default) are accepted as `aud`.
  - Multi-tenant (`TenantId = "organizations"`): tenant in `AllowedTenants` → 200; tenant not in it → 401 and an Information log "tenant {tid} not allowed"; `AllowAnyTenant = true` → 200 for any tenant.
  - Only RS256 accepted (an HS256 token → 401).
  - Metadata outage → 401 (not 500) and one Warning log naming the authority.
  - Bad-token logs contain the reason code and never the token (scrubber from Task 23 — until then assert the token string is absent from captured logs).
  - Startup failures (`InvalidOperationException` naming option and fix): `TenantId` missing; `TenantId = "common"` ("personal accounts are not supported; use a tenant id or 'organizations'"); `organizations` with empty `AllowedTenants` and `AllowAnyTenant = false`; `ClientId` missing; `RequireHttpsMetadata = false` outside `Development`.
  - When the app already set a default authenticate scheme, `AddEntraAuthentication` does not override it; otherwise `A2AEntra` becomes default authenticate and challenge scheme.
  - Binding from `IConfiguration` (`A2A:Entra` section) gives the same result as the `Action` overload.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement** per spec "Server Entra building blocks": both overloads on `A2AServerBuilder` (namespace `Microsoft.Extensions.DependencyInjection`); options table with the spec defaults; JwtBearer, registered under the scheme name `A2AEntra`, with `MapInboundClaims = false`, `RoleClaimType = "roles"`, `NameClaimType = "azp"`, valid audiences, `ValidAlgorithms = [RS256]`, issuer validation per mode, `ClockSkew`. Move the outage logic (`IsMetadataUnavailable`, `OnAuthenticationFailed`) from `samples/AgentServer/Auth/AuthSetup.cs` into `EntraMetadataOutage` (internal) using `LoggerMessage` source generation.
  - **The scheme name `A2AEntra` is a contract** *(cross-plan review)*. Package 06's `Ambinder.A2A.Cluster.Entra` validates pod tokens with `context.AuthenticateAsync("A2AEntra")` (06 plan Task 15), and package 12's test-scheme rule checks whether a default scheme exists.
    - Say the name in the XML docs of both `AddEntraAuthentication` overloads.
    - Add a test `Scheme_name_is_A2AEntra` that reads `IAuthenticationSchemeProvider` after `AddEntraAuthentication` and finds `A2AEntra`, so a rename fails here and not in 06's cluster tests.
    - Add no new public constant for it: spec 07 lists none.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(entra): AddEntraAuthentication with single and multi-tenant validation`.

### Task 15: Entra evaluator, card contributor, policy helper

**Files:**
- Create: `EntraSecurityRequirementEvaluator.cs`, `EntraCardSecurityContributor.cs`, `EntraAuthorizationPolicyExtensions.cs`
- Test: `EntraEvaluatorTests.cs`, `EntraCardTests.cs`, golden file `tests/Ambinder.A2A.AspNetCore.Entra.Tests/Golden/entra-card-security.json`, `EntraPolicyTests.cs`

- [ ] **Step 1: Write failing tests.**
  - Evaluator, for generated schemes (matched by name and token URL host): app-only caller with `roles: [A2A.Invoke]` satisfies `{entra: ["api://x/.default"]}` (bare `.default` maps to `InvokeRole`); app-only requirement `api://x/Reports.Approve` needs role `Reports.Approve`; user caller satisfies `{entraUser: ["api://x/A2A.Invoke"]}` only with `A2A.Invoke` in `scp`; user caller with `InvokeScope = null` → 403; a hand-written scheme with another name is not handled unless `AddEntraEvaluatorFor("name")` is called (that method or option is the builder's choice).
  - Card (golden JSON): single-tenant with `InvokeScope` set produces exactly the `securitySchemes` `entra` (clientCredentials, `tokenUrl = {Instance}{TenantId}/oauth2/v2.0/token`, scope `{AppIdUri}/.default` → "Call this agent as an application"), `entraUser` (authorizationCode, `pkceRequired = true`, scope `{AppIdUri}/{InvokeScope}` → "Call this agent as a signed-in user"), `entraOidc` (`{Instance}{TenantId}/v2.0/.well-known/openid-configuration`) and the two `securityRequirements`. Multi-tenant uses `organizations` in the URLs. A second golden case without `InvokeScope` has no `entraUser` and one requirement.
  - The contributor only adds: hand-written agent-level requirements are kept and no generated ones are added; a hand-written scheme with a generated name → startup failure; `AllowAnonymous()` agents get nothing.
  - End to end: an agent registered with only `AddEntraAuthentication` + `AddAgent` passes Task 12's startup checks (every scheme has an evaluator) and enforces the card: a caller without the role gets 403, with no hand-written card security.
  - Extended card: `GetExtendedAgentCard` on all three transports returns the generated schemes and requirements, and `FilterExtendedCardSkills` still filters by them.
  - `RequireA2AEntraAccess(roles: [...], scopes: [...])` passes app-only callers with all roles and user callers with all scopes, fails otherwise.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement** per spec. The contributor implements 03's `IAgentCardContributor` and runs in 09's card build step; it writes into the card JSON only new `securitySchemes` keys and `securityRequirements` when absent at agent level. Its security output must not depend on `AgentCardContext.Request`, because `EffectiveCardSecurity` (Task 11) runs it at startup with no request.
  - **Extended card.** Contributors do not run on the extended card (03 plan owner note 5; 09's `Extended` path skips step 5), but the spec adds the schemes "to each card of an `Authenticated` agent". In the `GetExtendedAgentCard` path, merge the extended card's `EffectiveCardSecurity` onto the model: `card with { SecuritySchemes = ..., SecurityRequirements = ... }`. Do this in `Ambinder.A2A.Server`, next to where 11 merges extensions (11 plan), so it covers all three transports.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(entra): card schemes, requirement evaluator and policy helper`.

### Task 16: Entra end to end on all transports

**Files:** Test only: `tests/Ambinder.A2A.AspNetCore.Entra.Tests/EntraEndToEndTests.cs`

- [ ] **Step 1: Write the tests** (theory over three transports, fake authority): app-only owner key is `tid:{tid}/app:{azp}`; delegated owner key is `tid:{tid}/user:{oid}`; missing role → 403 / `PERMISSION_DENIED`; no token → 401 with `WWW-Authenticate: Bearer` / `UNAUTHENTICATED`; two client apps do not see each other's tasks; a grant-based continuation by a second app works. Also every row of the spec's error table (tier 1 says "on all three transports"), which earlier tasks test on one path only:
  - a bad signature, an issuer outside `AllowedTenants` and a v1 token when v1 is off (Task 14) → 401 / `UNAUTHENTICATED`;
  - Entra tenant binding mismatch (Task 9) → 403 / `PERMISSION_DENIED`;
  - skill authorization (Task 12): 403 / `PERMISSION_DENIED` before the first event, `REJECTED` state after it.
- [ ] **Step 2:** Run. Expected: PASS. Fix causes, not tests.
- [ ] **Step 3:** Commit `test(entra): end-to-end owner, role and grant tests`.

### Task 17: Sample server uses `AddEntraAuthentication`; API key as the other-schemes example

**Files:**
- Modify: `samples/AgentServer/Auth/AuthSetup.cs` (shrinks to `AddEntraAuthentication` + the API key scheme), `samples/AgentServer/Auth/AuthOptions.cs`, `samples/AgentServer/Cards/AgentCards.cs` (drop hand-written Entra schemes; keep the `apiKey` scheme), `samples/AgentServer/Program.cs`, `samples/AgentServer/appsettings*.json` (`A2A:Entra` section for MockEntra: `Instance = http://localhost:5100/`, MockEntra's tenant id, `AppIdUri = api://a2a-demo`, `RequireHttpsMetadata = false` in Development only), docker-compose / AppHost env vars if they set `Auth:*`
- Create: `samples/AgentServer/Auth/ApiKeyOwnerResolver.cs` (decorates `DefaultOwnerResolver`; maps the API key identity to `OwnerKey.Parse($"apikey:{name}")`), `samples/AgentServer/Auth/ApiKeyRequirementEvaluator.cs`
- Test: existing `AuthSetupTests.cs`, `SecureFlowTests.cs`, `DiscoveryAndAuthTests.cs`, `ComposeFileTests.cs` (update)

- [ ] **Step 1:** Update the sample tests first to the new owner strings and card shape (they fail).
- [ ] **Step 2:** Run `dotnet test tests/Ambinder.A2A.Integration.Tests`. Expected: FAIL.
- [ ] **Step 3:** Change the sample. Check MockEntra tokens: issuer `{IssuerBaseUrl}/{TenantId}/v2.0` must equal `{Instance}{TenantId}/v2.0`, and `aud` must be `ClientId` or `AppIdUri`. If MockEntra lacks something the real validation needs (for example `kid` matching), fix MockEntra, not the validation.
- [ ] **Step 4:** Run the integration tests and `run-all.sh` smoke if CI runs it. Expected: PASS. The sample server has no auth code beyond config and the API key example (spec principle 6 check).
- [ ] **Step 5:** Commit `refactor(samples): AgentServer uses AddEntraAuthentication`.

---

## Phase E — Client Entra (`Ambinder.A2A.Client.Entra`)

### Task 18: Client contract and project split

**Files:**
- Already done by 01 (check in Step 0, do not redo): `src/Ambinder.A2A.Client.Entra/` exists with `AzureIdentityCredentialProvider` (namespace `Ambinder.A2A.Client.Entra`); `Ambinder.A2A.Client` has no `Azure.Identity` reference; `eng/package-dependencies.json` has a `Client.Entra` entry.
- Create only if Step 0 finds them missing: the `Ambinder.A2A.Client.Entra` project (refs `Ambinder.A2A.Client`, package `Azure.Identity`), its `README.md` and its allow-list entry.
- Rename: `src/Ambinder.A2A.Client.Entra/AzureIdentityCredentialProvider.cs` → `EntraCredentialProvider.cs` (`git mv`; rewritten in Task 19). If 10 already ported it to `ApplyAsync` (10 plan, "A4"), keep that port as the starting point.
- Move: `ClientCredentialsProvider.cs`, `ApiKeyCredentialProvider.cs` → `samples/AgentClient/Auth/` (skip if 10 already moved them).
- Modify: `BearerTokenCredentialProvider.cs` → `DelegateCredentialProvider` (if 10 has not done it); `Ambinder.A2A.Client.Entra` README (it now describes the Entra providers of this package).
- If 10's contract is missing: add `IA2ACredentialProvider.ApplyAsync`, `A2ACredentialContext`, `AuthChallenge`, `A2ARequestOptions` (with `Credentials`, `UserAssertion`, `CredentialCacheKey`), `IA2AClientBuilder` exactly as 10's spec defines (§0.2).
- Test: `tests/Ambinder.A2A.Client.Entra.Tests/` (new project), update `CredentialProviderTests.cs`

- [ ] **Step 0: Check 01's state.**

```bash
ls src/Ambinder.A2A.Client.Entra && git grep -n "class AzureIdentityCredentialProvider\|class EntraCredentialProvider" -- src
```

```bash
git grep -n "Azure.Identity" -- src/Ambinder.A2A.Client/*.csproj src/Ambinder.A2A.Client.Entra/*.csproj
```

Expected: the project exists, the provider is in it, and only `Client.Entra` references `Azure.Identity`. Record the result in the PR body. If 01 did not do this, do it here (create the project, move the provider, remove the reference) before Step 1.
- [ ] **Step 1: Write failing tests** for what is still missing:
  - The type `Ambinder.A2A.Client.Entra.EntraCredentialProvider` exists and `AzureIdentityCredentialProvider` does not (reflection over the `Client.Entra` assembly).
  - `ClientCredentialsProvider` and `ApiKeyCredentialProvider` are not types of `Ambinder.A2A.Client`; the `AgentClient` sample still builds with them.
  - `DelegateCredentialProvider` exists in `Ambinder.A2A.Client` and `BearerTokenCredentialProvider` does not.
  - `Ambinder.A2A.Client` still has no `Azure.*` or `Microsoft.Identity*` dependency, and `Ambinder.A2A.Client.Entra` has no `Ambinder.A2A.Server` dependency (B10). These two pass from the start (01 did the split). They guard the split, so they are kept even though they are not red.
- [ ] **Step 2:** Run. Expected: the type tests FAIL; the two guard tests PASS.
- [ ] **Step 3:** Do the rename and the moves. Samples that used the moved providers reference them from `samples/AgentClient`.
- [ ] **Step 4:** `dotnet build Ambinder.A2A.slnx` and run the client tests. Expected: PASS.
- [ ] **Step 5:** Commit `refactor(client)!: Entra provider naming; move non-Entra providers to samples`.

### Task 19: `EntraCredentialProvider` with cache, refresh, claims challenge and card-scope rule

**Files:**
- Create/rewrite: `EntraCredentialProvider.cs` (also implements 10's `IA2ACredentialSelector`), `EntraTokenCache.cs` (internal), `A2AEntraClientOptions.cs`, `EntraCredentialType.cs`, `EntraClientBuilderExtensions.cs` (`AddEntraCredentials`)
- Test: `tests/Ambinder.A2A.Client.Entra.Tests/EntraCredentialProviderTests.cs`

**Selector.** 10 picks only providers that implement `IA2ACredentialSelector` when a client has more than one (10 spec, card-scheme selection). `CanSatisfy(schemeName, scheme)` returns true for an OAuth2 or OpenID Connect scheme whose token or discovery URL is on the configured `Instance` host, or whose name is one of the generated names (`entra`, `entraUser`, `entraOidc`). Add a test `TwoProviders_EntraAndApiKey_PickByCardScheme`.

- [ ] **Step 1: Write failing tests** with a counting fake `TokenCredential` and `FakeTimeProvider` (spec acceptance "Entra client tests"):
  - Cache hit: two calls, one `GetTokenAsync`.
  - Refresh before expiry: token expires in 10 min, `RefreshBefore = 5 min`; at +6 min a new token is fetched. If `AccessToken.RefreshOn` is set, it wins.
  - `ForceRefresh = true` bypasses the cache once; the next normal call uses the new token.
  - Claims challenge: `Challenge` with `error="insufficient_claims"` and `claims="..."` → `TokenRequestContext.Claims` equals the decoded value; separate cache key.
  - Single flight: 20 concurrent calls for one key → one `GetTokenAsync`.
  - Bounded: 65 distinct keys → at most 64 entries.
  - Sets `Authorization: Bearer {token}` on `context.Headers`.
  - Scope rule: `Scope` unset and card scope `api://evil/.default` with `AllowCardScopes = []` → throws with a message naming `A2AEntraClientOptions.Scope` and `AllowCardScopes`; with `AllowCardScopes = ["api://evil"]` → that scope is used.
  - `Auto` in `Development` → `DefaultAzureCredential`; in `Production` → `ChainedTokenCredential(WorkloadIdentityCredential, ManagedIdentityCredential)`; `ClientSecret` outside Development logs one warning (inspect the chosen type through an internal factory seam, not by getting real tokens).
  - The token never appears in captured logs.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement** per spec "Client Entra building blocks": `AddEntraCredentials(this IA2AClientBuilder, Action<A2AEntraClientOptions>)` calls `builder.AddCredentials(sp => …)`; options table with the spec defaults; `IHostEnvironment` read optionally from DI (absent → treat as not Development); cache keyed by (scope, claims, tenant), one `SemaphoreSlim` per key, 64-key bound (data structure is the builder's choice).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(entra-client): EntraCredentialProvider with cache and forced refresh`.

### Task 20: `EntraOnBehalfOfCredentialProvider` and `AddEntraOnBehalfOf`

**Files:**
- Create: `EntraOnBehalfOfCredentialProvider.cs` (also implements `IA2ACredentialSelector`: true only for authorization-code schemes on the `Instance` host and for `entraUser`), `A2AEntraOboOptions.cs`; extend `EntraClientBuilderExtensions.cs`
- Test: `tests/Ambinder.A2A.Client.Entra.Tests/OnBehalfOfProviderTests.cs`

- [ ] **Step 0: Confirm the Azure.Identity API.** In the pinned version, confirm the `OnBehalfOfCredential` constructor that takes a client-assertion callback. Record it in the commit message. If absent, support certificate and secret only and list the gap in the PR body.
- [ ] **Step 1: Write failing tests** (behind an internal credential-factory seam so no network is used):
  - Assertion from `A2ARequestOptions.UserAssertion` wins over the delegate.
  - Without it, `A2AEntraOboOptions.UserAssertionProvider` (`Func<CancellationToken, ValueTask<string?>>`) is used.
  - Neither → throws with a message naming both sources ("Set A2ARequestOptions.UserAssertion or A2AEntraOboOptions.UserAssertionProvider (on a server: AddCallerAssertionForOnBehalfOf with KeepCallerToken = true).").
  - Credential instances cached per SHA-256 of the assertion (LRU 1,000), evicted at token expiry; the raw assertion is not a dictionary key and never logged.
  - Client authentication: certificate, secret, or assertion callback (workload identity token file).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. `A2AEntraOboOptions` holds `Scope`, `TenantId`, `ClientId`, `ClientCertificate`/`CertificatePath`, `ClientSecret`, `ClientAssertionProvider`, `UserAssertionProvider` *(field list is plan-writer's call from the spec bullets; confirm names in the 02 API review)*.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(entra-client): on-behalf-of provider with user-assertion delegate`.

### Task 21: `AddCallerAssertionForOnBehalfOf()` adapter (B10)

**Files:**
- Create: `src/Ambinder.A2A.AspNetCore.Entra/CallerAssertionExtensions.cs`; add the `Ambinder.A2A.Client.Entra` project reference to `Ambinder.A2A.AspNetCore.Entra.csproj`
- Test: `tests/Ambinder.A2A.AspNetCore.Entra.Tests/CallerAssertionTests.cs`

- [ ] **Step 1: Write failing tests.**
  - Server with `KeepCallerToken = true` and an agent that calls a second (fake) agent through a client registered with `AddEntraOnBehalfOf(...).AddCallerAssertionForOnBehalfOf()`: the OBO factory receives the incoming user token as the assertion.
  - With `KeepCallerToken = false` → the provider throws the Task 20 message; the agent's task fails without the token in the message or logs.
  - Recovered run (reduced caller, `AccessToken == null`) → same failure (B8).
  - Startup: `AddCallerAssertionForOnBehalfOf()` with `KeepCallerToken = false` logs one warning naming the option.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement: the extension on `IA2AClientBuilder` sets `UserAssertionProvider = ct => ValueTask.FromResult(accessor.Current?.AccessToken)` using `IA2ACallerAccessor` from DI.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(entra): feed the caller token to the OBO provider`.

### Task 22: Sample client uses `AddEntraCredentials`

**Files:** `samples/AgentClient/*` (use `AddEntraCredentials` with `CredentialType = ClientSecret` against MockEntra in Development; keep API key flows on the moved sample provider, which implements `IA2ACredentialSelector` for its `apiKey` scheme, so the sample's two providers can be picked by the card), related tests in `ConsoleFlowRunnerTests.cs`.

- [ ] **Step 1:** Update the client sample tests to the new registration (they fail).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Change the sample.
- [ ] **Step 4:** Run the integration tests. Expected: PASS.
- [ ] **Step 5:** Commit `refactor(samples): AgentClient uses AddEntraCredentials`.

---

## Phase F — Cross-cutting checks, live tests, docs

### Task 23: Secret scrubber assertion in the shared fixtures

**Files:** Create `tests/Shared/SecretScrubber.cs`; wire it into every fixture that captures logs or activities (`ActivityCollector`, test logger providers, `A2ATestServer` if present).

- [ ] **Step 1:** Write a failing test that logs a fake token through a test logger and expects the scrubber assertion to fail.
- [ ] **Step 2:** Run. Expected: FAIL (no scrubber).
- [ ] **Step 3:** Implement: the fixture records every token, assertion and `Authorization` value the test issued (fake authority, fake credentials) and, on dispose, asserts none of them (and no `Bearer ey…` pattern) appears in any captured log message, exception text, span name, tag or event.
- [ ] **Step 4:** Run the whole suite. Expected: PASS; fix any real leak found.
- [ ] **Step 5:** Commit `test: assert no secrets in logs or spans`.

### Task 24: Package-graph check

**Files:** `eng/package-dependencies.json`, 01's allow-list test (extend), or a new test in `Ambinder.A2A.Integration.Tests/Repo/`.

- [ ] **Step 1:** Add a test that fails when `Ambinder.A2A.Server` or `Ambinder.A2A.Client` has any `Azure.*` or `Microsoft.Identity*` dependency (direct, from the packed nuspec) and when `Ambinder.A2A.Client.Entra` depends on `Ambinder.A2A.Server`. The only allowed exception is §0.3 item 9: `Microsoft.IdentityModel.JsonWebTokens` in `Ambinder.A2A.Server` while `git grep -n "Microsoft.IdentityModel" -- 'src/Ambinder.A2A.Server/**/*.cs'` still finds a use. When that grep is empty and the reference is still there, the test fails with "Remove Microsoft.IdentityModel.JsonWebTokens from Ambinder.A2A.Server: nothing in the core uses it any more (package 08 moved push signing off it)."
- [ ] **Step 2:** Run it with a temporary bad reference to see it fail, remove the bad reference.
- [ ] **Step 3:** Run. Expected: PASS.
- [ ] **Step 4:** Commit `ci: package-graph check for security dependencies`.

### Task 25: Benchmarks (needs 14)

**Files:** `benchmarks/Ambinder.A2A.Benchmarks/Security/AuthorizeRequestBenchmark.cs`, `EntraTokenCacheHitBenchmark.cs`, baseline file per 14.

- [ ] **Step 1:** Add `AuthorizeRequest` (resolve caller, tenant, evaluate a two-requirement card) and `EntraTokenCacheHit` with `MemoryDiagnoser`.
- [ ] **Step 2:** Run `dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*AuthorizeRequest*" "*EntraTokenCacheHit*"`. Targets (spec principle 4): `AuthorizeRequest` < 5 µs and < 1 KB allocated; cache hit < 1 µs. Record the baseline per 14.
- [ ] **Step 3:** If a target is missed, report the numbers in the PR body; do not loosen the target.
- [ ] **Step 4:** Commit `perf: security benchmarks and baseline`.

### Task 26: Docs, runbook, checklist (package 13 sections owned by 07)

**Files:**
- Create: `docs/security/threat-model.md`, `docs/security/identity-and-ownership.md`, `docs/security/tenants.md`, `docs/security/entra-setup.md`, `docs/security/other-auth-schemes.md`, `docs/agents/multi-tenant.md`, `docs/release/pre-release-checklist.md` (or append to an existing one)
- Modify: `docs/production.md` §1 (replace with a link to `docs/security/entra-setup.md`), `docs/clients/authentication.md` (Entra part, if 10 created it), `docs/migration/from-phase-1.md` (owner key change row, if 13 created it)

- [ ] **Step 1:** Write the pages with 13's front matter (`packages`, `since`, `verified: { version, date, env: unit-tests }`, `owner: 07`) and the note "not yet verified on a real tenant" (O3). Content, from the spec only:
  - identity-and-ownership: the four owner rules; `OwnerKey` format frozen at 1.0; anonymous access and "task id acts as a capability"; Development default (C8); continuation grants and what a grant holder gets (B6); reduced caller and "OBO only on the receiving pod" (B8); `IA2ACallerAccessor`.
  - tenants: A2A tenant vs Entra `tid`; route vs body; allow-list; `TenantBindings`.
  - threat-model: the anti-goals as threats with mitigations (hostile card scope, shared owners, standing grants, secrets in logs, A2A tenant spoofing).
  - entra-setup: app registrations (agent API with roles `A2A.Invoke`, `Reports.Approve` and scope `A2A.Invoke`; `requestedAccessTokenVersion = 2`; client app 1 with `A2A.Invoke`; client app 2 without roles; multi-tenant client in tenant B), admin consent, federated credentials for GitHub OIDC and for AKS workload identity, the `A2A:Entra` config. This is the runbook `entra-live` uses.
  - other-auth-schemes: the four steps with the API key sample (`ApiKeyOwnerResolver`, `ApiKeyRequirementEvaluator`, card scheme, client provider).
  - pre-release checklist: device-code check for delegated tokens and OBO (C8); the `entra-live` cases when the job cannot run (O3).
- [ ] **Step 2:** If 13's `DocsLint` or the repo `DocsTests` exist, run them. Expected: PASS.
- [ ] **Step 3:** Commit `docs(security): security model, Entra runbook, other schemes`.

### Task 27: `entra-live.yml` and live tests (skipped visibly without a tenant, O3)

**Files:**
- Create: `.github/workflows/entra-live.yml`, `tests/Ambinder.A2A.Entra.LiveTests/` (`IsPackable=false`; not part of the default `dotnet test` filter — mark with a trait `Category=EntraLive` and exclude it in `ci.yml`)

- [ ] **Step 1: Write the live tests**, one per spec case: app-only owner keys; missing role (403); wrong audience (401); tenant B rejected single-tenant and accepted multi-tenant with an allow-list; v1 rejected; two client apps isolated; grant-based continuation; generated card schemes match the tenant's real metadata URLs. Tokens come from `ClientAssertionCredential` with the GitHub OIDC token (`ACTIONS_ID_TOKEN_REQUEST_URL`/`_TOKEN`, audience `api://AzureADTokenExchange`). Each test starts with: when any `A2A_ENTRA_*` variable is missing → `Assert.Skip("skipped: no test tenant")` (confirm the dynamic-skip API in the pinned xunit v3).
- [ ] **Step 2:** Run locally: `dotnet test tests/Ambinder.A2A.Entra.LiveTests`. Expected: every test reported as **skipped** with that reason (not passed, not failed).
- [ ] **Step 3: Write the workflow.** Triggers: `schedule` (nightly), `push` tags `v*`, `workflow_dispatch`. `permissions: { contents: read, id-token: write }`. Job steps: checkout (third-party actions pinned by SHA, 01 §6); a `check` step that sets `enabled=true` only when all `vars.A2A_ENTRA_*` are non-empty, else prints `::notice::skipped: no test tenant` and writes it to `$GITHUB_STEP_SUMMARY`; every later step has `if: steps.check.outputs.enabled == 'true'`. No `secrets.*` anywhere. Live failures do not gate PRs (not in `pull_request` triggers); the release workflow (01) lists this job as required for tags only when it runs.
- [ ] **Step 4:** Validate the YAML (`actionlint` if available; else a YAML parse). Push the branch and confirm in the Actions run of `workflow_dispatch` (if the session can trigger it) that it ends green with the "skipped: no test tenant" notice.
- [ ] **Step 5:** Commit `ci: entra-live workflow, skipped without a test tenant`.

### Task 28: Final verification and PR

- [ ] **Step 1:** `dotnet build Ambinder.A2A.slnx -c Release` and `dotnet test Ambinder.A2A.slnx`. Expected: all green.
- [ ] **Step 2:** If 03's TCK job exists, run it; the auth-related MUST scenarios pass on all three transports. Otherwise state "TCK not available yet" in the PR body.
- [ ] **Step 3:** `git grep -nE 'CallerIdentity|CanContinueInterrupted|DefaultApiKeyAuthenticationType|AzureIdentityCredentialProvider|tenant:\{' -- src samples tests` finds nothing (old API and old owner format gone).
- [ ] **Step 4:** Walk the spec's "Acceptance criteria" list and tick each item in the PR body with the test name that proves it, or "open: <reason>" (for example 05/09/14 not landed).
- [ ] **Step 5:** Open the PR. Body: Task 0 seam table, acceptance checklist, public API added, follow-ups (forwarded-run caller test for 06; anything skipped).

---

## Acceptance criteria → tasks

| Spec acceptance item | Task |
|---|---|
| `DefaultOwnerResolver` unit tests | 2 |
| Anonymous refused / `AllowAnonymous` / two users / two apps on three transports | 5, 6 |
| `OwnerKey` golden format (B7) | 1 |
| Grant holder gets its response then `TaskNotFound` (B6) | 8 |
| Recovered/forwarded run sees reduced caller; OBO fails (B8) | 7, 10, 21 |
| Development with no scheme / other env fails (C8) | 5 |
| Continuation tests | 8 |
| Tenant tests | 9 |
| Requirement tests (OR/AND, missing evaluator, single skill, `AuthorizeSkillAsync`, extended card) | 11, 12 |
| Entra server tests with fake authority + golden card | 14, 15, 16 |
| Entra client tests incl. OBO delegate and adapter | 19, 20, 21 |
| No secrets in logs or spans | 23 |
| `entra-live.yml` + manual checklist (O3, C8) | 26, 27 |
| Package-graph CI check | 24 |
| TCK auth MUST scenarios | 28 |
| Docs (security model, runbook, other schemes) | 26 |
| Benchmarks `AuthorizeRequest`, `EntraTokenCacheHit` | 25 |

---

## Execution notes

- **Method:** execute this plan with **superpowers:subagent-driven-development**: one fresh implementer subagent per task, then a spec-compliance review and a code-quality review of that task before the next task starts.
- **Model:** every subagent (implementer and both reviewers) runs on model **`sonnet`**. Pass `model: "sonnet"` on every Agent call. The controller does not raise the model for any task; if a task is too hard on `sonnet`, split it rather than switching models.
- **Order:** tasks run in the numbered order. Phases stop at their gates (§0.2): if 05 is missing, stop after Task 6; if 09's pipeline is missing, stop after Task 10; if 03's contributor is missing, stop before Task 15. Report what landed and what waits. Task 24 can run before 08's Task 13 only with the §0.3 item 9 exception; re-run it after 08 lands, and the exception must then be unused.
- **Each subagent's brief:** the task text from this plan, §0 and Global Constraints, the spec path, and the Task 0 seam table. Tell it to quote the spec's exact error messages and names, not to add public API beyond the spec, and to stop and ask when a seam differs from what this plan assumes.
- **Reviews check in particular:** Review Focus items 1–8; no token in any message, log or store field; every new public member has XML docs and (if 02 is present) an `PublicAPI.Unshipped.txt` line.
- **No cloud:** no subagent creates Azure resources or app registrations (O3). Live tests must skip.

---

## Known minor review notes

Minor findings from the plan review of 2026-11-03 (reviewed commit `a079936`). They are not blockers. Apply each one when you touch that code. Line numbers are from that commit, so find the text by symbol.

- **Already fixed on master (PR #66).** Task 11 says "register the middleware in 09's built-in step 3" without naming 09's hook `A2AServerBuilder.UseAuthorizationMiddleware<T>()` (09 plan:433). Plan: docs/superpowers/plans/2026-11-03-sdk-07-security-entra.md:401. Spec: Dependencies (09 §7.11 step 3). Fix: name `UseAuthorizationMiddleware<SecurityMiddleware>()` and add it to the Task 0 search.
- §0.3 item 2 offers `OwnerClaims` to 09's rate limiter as linked source, but 09 reuses it only "If 07 has landed with a public claim-name class", otherwise writes its own `A2ARateLimitPartitionKey` with a `sub:` fallback (09 plan:1005-1018): two copies of a frozen rule. Plan: docs/superpowers/plans/2026-11-03-sdk-07-security-entra.md:75, :237. Spec: §Caller identity "Rate-limit key"; Provides "To 09". Fix: say in 07 and 09 that 09's partition key calls the linked `OwnerClaims.TryGetPartitionKey` in `Ambinder.A2A.AspNetCore`.
- No task checks principle 3: "the getting-started agent with Entra is at most 25 lines". Plan: docs/superpowers/plans/2026-11-03-sdk-07-security-entra.md:503-522. Spec: §How it meets the six principles, item 3. Fix: line-count assertion or docs snippet test in Task 17 or 26.
