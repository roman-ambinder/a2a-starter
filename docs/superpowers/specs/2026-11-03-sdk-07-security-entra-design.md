# SDK package 07: Security model and Entra

- Date: 2026-11-03
- Status: approved by owner 2026-11-03
- Milestone: M1 (`1.0.0-preview`)
- Size: L

## Intent

Today the library leaves auth to the samples, and its default task ownership leaks data. Users of one front-end app share tasks. Every anonymous caller shares one owner key. Any app in the tenant can continue an `AUTH_REQUIRED` task. The tenant field is stored but never checked.

This package makes the library safe by default and gives teams a working, tested Entra ID setup in a few lines. It does four things:
- It fixes the ownership and tenant rules in the core server.
- It enforces the security requirements the agent card declares, down to skill level.
- It ships Entra building blocks for the server (token validation, app roles, card schemes) and the client (Azure.Identity, workload identity, token caching).
- It flows the caller's identity into the agent's code.

Other auth schemes plug in through small interfaces, but none ship.

## Findings addressed

- R18: the tenant field is stored but not isolated.
- R19: default task ownership is too coarse for delegated tokens and anonymous callers. The step-up rule is too wide. The API key type is tied to the sample.
- R20: auth lives in the samples only. There are no Entra building blocks, card schemes are hand-written, and nothing has been tested against a real tenant.
- R12 (one bullet): skill-level `securityRequirements` are never enforced.
- R31 (Entra part): no OBO provider. Workload identity is only a thin wrapper. Card-derived credentials are unsafe.
- R7 (auth part): `Azure.Identity` is a dependency of every client consumer.
- Pain point 14, "Authentication and per-request credentials on the client": identity propagation into the executor (java #893). This package covers the server half; package 10 covers per-request headers.
- Pain point 15, "Identity / trust / authorization of agents": hooks for per-skill authorization, without inventing a trust model.
- Pain point 7, "Binding differences ... tenant handling": the tenant comes from trusted routing state, not from the client field alone (dotnet #539).

## Goals

1. With the default `IOwnerResolver`, an app-only Entra token gets owner `tid:{tid}/app:{appid}`. A delegated token (with an `scp` claim) gets `tid:{tid}/user:{oid}`. Two users of the same client app cannot see each other's tasks. Tested.
2. Anonymous callers are refused (401) on every operation except card fetch, unless the agent opts in with `AllowAnonymous()`. Tested on all three transports.
3. An `AUTH_REQUIRED` task can be continued only by its owner, or by an owner the agent named when it set `AUTH_REQUIRED`. Any other caller gets `TaskNotFound`. Tested.
4. Every task read and write is scoped by (agent, A2A tenant, owner). A task created under tenant `a` cannot be read, listed, cancelled or subscribed to under tenant `b`, even by the same owner. Tested against the store conformance suite (package 05).
5. `services.AddA2AServer(...).AddEntraAuthentication(config.GetSection("A2A:Entra"))` validates Entra v2 access tokens for single-tenant and multi-tenant apps. It registers app-role and scope policies, and it adds the Entra `securitySchemes` and `securityRequirements` to every protected agent card. No sample auth code is needed. Tested with the fake Entra authority.
6. The card is the single source of truth for authorization. The agent-level `securityRequirements` and the skill-level ones are enforced from the card. A card cannot advertise a requirement the server does not check: startup fails if a requirement names a scheme that has no evaluator.
7. The agent's code gets an `A2ACaller` (owner, kind, Entra tenant, app id, user object id, claims) for every run, including runs that outlive the HTTP request. Tested.
8. `services.AddA2AClient("x", ...).AddEntraCredentials(...)` gets tokens through Azure.Identity. In AKS it uses workload identity with no secrets. Tokens are cached per scope and refreshed before expiry. A forced refresh and a claims challenge work for package 10's auth retry. Tested.
9. An agent that calls another agent on behalf of its user can use `EntraOnBehalfOfCredentialProvider`, which takes the user assertion from a delegate; `Ambinder.A2A.AspNetCore.Entra` provides the adapter that reads it from the current `A2ACaller` (B10). Tested with the fake authority. Checked by hand against a real tenant (see Design, "Testing against a real tenant").
10. A CI job runs the Entra tests against a real test tenant with no stored secrets (GitHub OIDC federated credentials). The job is written now but runs only when its tenant settings exist, and is skipped visibly otherwise (owner decision O3: no cloud resources for now). The app-registration runbook in the docs is the one that job uses.

## Non-goals

- Auth schemes other than Entra as shipped packages (API key, mTLS, generic OIDC, OAuth for other identity providers). They plug in through the extension points below. The API key handler stays in `samples/` as the worked example.
- Agent card signing and verification. That is package 04.
- Client auth retry mechanics, per-request headers, interceptors and the `AUTH_REQUIRED` step-up hook on the client. Those are package 10. This package provides the Entra credential provider that package 10 calls.
- The authoring API that exposes `A2ACaller` and the "name who may continue" option on the event sink. That is package 11. This package defines the data types and the rules, and ships a minimal sink method so M1 can test the rule.
- Store schema and persistence of owner, tenant and continuation grants. That is package 05. This package states which fields it needs.
- Routing by tenant or metadata for gRPC, and public versus bind URLs. That is package 09.
- Rate limiting per caller. That belongs to package 09. It partitions by the same token claims `OwnerKey` uses, read before the body (see "Rate-limit key" below), not by `A2ACaller`.
- A trust model for agent identity, actor chains or delegation tokens beyond OBO. The spec has not settled these (pain point 15).

## Anti-goals

- A default that lets two different callers see each other's tasks. This includes anonymous callers, users of one app, and apps of one tenant.
- Copying security-sensitive code into every product team: if a team still needs `samples/AgentServer/Auth/AuthSetup.cs` to use Entra, this package failed.
- A card that says one thing while the server checks another (the drift in R20).
- A client that fetches a token for a scope the agent card chose and sends it to that agent. A hostile card could then collect tokens for other resources.
- Tokens, client assertions, `Authorization` values or user assertions in logs, traces, exception messages or stores.
- A hard dependency on `Azure.Identity`, `Microsoft.Identity.Web` or any Entra package in `Ambinder.A2A.Server` or `Ambinder.A2A.Client`.
- Error messages that say only "401". Each failure must say what is missing and where to configure it.

## Constraints

- Owner rule (work plan, "Task owner"): app-only token → tenant + app; delegated token (`scp` claim) → tenant + user; anonymous off by default; only the same owner, or an identity the agent names, may continue an `AUTH_REQUIRED` task; the owner resolver is replaceable.
- Auth (work plan, "Auth"): Entra ID only, out of the box. Server: token validation and app roles. Client: Azure.Identity, including AKS workload identity. Other schemes plug in but do not ship.
- Names: packages and namespaces `Ambinder.A2A.*`. .NET 10 only.
- Principle 5: no Azure dependency in the core packages. The Entra code lives in `Ambinder.A2A.AspNetCore.Entra` and `Ambinder.A2A.Client.Entra`.
- Spec v1.0.1 rules (from `research-spec.md`):
  - Section 7 and the security section: authenticate every request (MUST), authorize every operation (MUST), TLS in production (MUST), and logs MUST NOT contain secrets.
  - Do not reveal that an unauthorized resource exists (MUST NOT), and do not tell not-found apart from not-authorized (SHOULD NOT).
  - ListTasks (3.1.4) MUST return only tasks visible to the caller. Scoping MUST apply even without a `contextId` filter, and before any lookup that leaks existence.
  - In-task auth (section 7, clarified in A2A #2081): the `AUTH_REQUIRED` state alone MUST NOT be treated as authorization. A credential does not authorize later messages unless that is defined.
  - Extended card (3.1.11): it MUST require authentication with the schemes the public card declares, and it MUST check permissions.
  - Tenant: when an `AgentInterface` sets `tenant`, every request's `tenant` must match it (proto comment, v1.0.1, `research-spec.md` §2).
- No cloud resources for now (owner decision O3). Live Entra tests are written but run only when their secrets exist. Until then the fake authority plus the manual pre-release checklist meet the exit criteria.
- Current dependency versions (`Directory.Packages.props`): `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14, `Microsoft.IdentityModel.JsonWebTokens` 8.23.0, `Azure.Identity` 1.21.0. Package 01 may update them.

## Design

### Package layout

| Package | Contents | New dependencies |
|---|---|---|
| `Ambinder.A2A.Server` (core, package 01; no ASP.NET Core) | `A2ACaller`, `IOwnerResolver`, `DefaultOwnerResolver`, `ITenantResolver`, `ITaskContinuationPolicy`, `TaskContinuationGrant`, `ISecurityRequirementEvaluator`, `ISkillAuthorizer`, `IA2ACallerAccessor`, `A2ASecurityOptions` | none |
| `Ambinder.A2A.AspNetCore` (package 01) | transport-side authentication (builds the principal and the `A2ACaller`), access modes (`AllowAnonymous`). It never authorizes: requirement checks run in the `IA2AMiddleware` pipeline. | none beyond 01 |
| `Ambinder.A2A.AspNetCore.Entra` | `AddEntraAuthentication`, `A2AEntraServerOptions`, `EntraSecurityRequirementEvaluator`, `EntraCardSecurityContributor`, Entra policies, metadata-outage handling, `AddCallerAssertionForOnBehalfOf()` (the adapter from `IA2ACallerAccessor` to the OBO user-assertion delegate, B10) | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.Validators`, `Ambinder.A2A.Client.Entra` |
| `Ambinder.A2A.Client.Entra` | `AddEntraCredentials`, `EntraCredentialProvider`, `EntraOnBehalfOfCredentialProvider`, `A2AEntraClientOptions`. No dependency on `Ambinder.A2A.Server` (B10). | `Azure.Identity` |

*(writer's call)* The server Entra package uses `JwtBearer` plus `Microsoft.IdentityModel.Validators` (for `AadIssuerValidator` and the signing-key issuer check). It does not use `Microsoft.Identity.Web`. Reasons: `Microsoft.Identity.Web` pulls in MSAL and token acquisition, which the server half does not need, and it brings its own config shape. The validators package handles the multi-tenant issuer rules, the one hard part. The builder must confirm that the `AadIssuerValidator` API in the pinned version covers v2 issuers and the `{tenantid}` template. If it does not, the builder writes an equivalent validator (about 30 lines) and tests it the same way.

`AzureIdentityCredentialProvider` moves from `src/A2A.Client/Auth/AzureIdentityCredentialProvider.cs` into `Ambinder.A2A.Client.Entra` as `EntraCredentialProvider`. The client core drops `Azure.Identity` (`src/A2A.Client/A2A.Client.csproj:3`).

### API rules (package 02)

- `AddA2A...`, `Add...Entra...` and other registration extension methods live in `namespace Microsoft.Extensions.DependencyInjection`. `MapA2A...` methods live in `Microsoft.AspNetCore.Builder`.
- Public interfaces in this package stay small. After 1.0, any new member on them gets a default implementation, so existing implementations keep compiling.
- `DefaultOwnerResolver` stays public (package 02 makes default implementations internal unless users need them): the other-schemes pattern decorates it.

### Caller identity: `A2ACaller`

```csharp
namespace Ambinder.A2A.Server.Security;

public enum CallerKind { Anonymous, Application, User, Other }

public sealed record A2ACaller(
    string Owner,             // opaque owner key, max 256 chars
    CallerKind Kind,
    string? EntraTenantId,    // tid
    string? AppId,            // azp (v2) or appid (v1); for User, the client app the user signed in through
    string? UserObjectId,     // oid, only for User
    ClaimsPrincipal Principal)// a snapshot copy, safe to use after the request ends
{
    public string? AccessToken { get; init; } // only when A2ASecurityOptions.KeepCallerToken is true; never stored or logged
}
```

- Rate-limit key *(Claude's call)*: ASP.NET Core computes it before the body is read, from token claims only: tenant id plus app id (app-only tokens) or user object id (delegated tokens). These are the claims `OwnerKey` uses. Anonymous requests partition by client IP. It does not wait for `A2ACaller` or `ITenantResolver`. Package 09 §7.6 owns the limiter.
- The transport authenticates and resolves the caller once per request, after authentication and before the handler. `A2ACallContext.Owner` (`src/A2A.Server/A2ACallContext.cs:17`) is replaced by `A2ACallContext.Caller`.
- `Principal` is a deep copy of `HttpContext.User`, made at request time. The agent run outlives the request (R36), so it must not hold `HttpContext`.
- Reduced caller across pods and recovery *(Claude's call, B8)*: the task stores a documented reduced caller (05 `StoredCaller`): owner key, Entra tenant id, app id, user object id, roles and scopes. No access token. A run on another pod (06, forwarded continuation) or a recovered run (09) gets an `A2ACaller` rebuilt from it; its `Principal` holds only those claims and `AccessToken` is null. On-behalf-of therefore works only on the pod that received the request, during that request's run. The docs say so.
- `IA2ACallerAccessor.Current` returns the caller of the agent run in progress (an `AsyncLocal`, set by the task manager around each run and cancel). Deep services in the agent use it, and so does the OBO provider. *(writer's call)*
- Package 11 exposes the caller as `AgentRequestContext.Caller`. In M1, before package 11 lands, this package adds `Caller` to the current `AgentRequestContext` (`src/A2A.Server/Agents/AgentRequestContext.cs:15-23`) next to `User`.
- `AccessToken` keeps the raw bearer token only when `A2ASecurityOptions.KeepCallerToken = true` (default `false`). It lives in memory for the run and is never copied into `StoredTask`. It exists for OBO. *(writer's call)*

### Owner resolution

```csharp
public interface IOwnerResolver
{
    /// Returns the caller, or throws A2AAuthException (401) with a message that says what is missing.
    ValueTask<A2ACaller> ResolveAsync(ClaimsPrincipal user, AgentRegistration agent, CancellationToken ct);
}
```

`DefaultOwnerResolver` rules, in order (claims are read with `MapInboundClaims = false` names, and the mapped long names are accepted as a fallback, as today in `src/A2A.Server/Auth/DefaultOwnerResolver.cs:26-27`):

1. Not authenticated → `Kind = Anonymous`, `Owner = "anonymous"`. Whether this is allowed is decided by the agent's access mode, not here (see "Anonymous access").
2. Authenticated, with `tid` and an `scp` claim (delegated) → `Kind = User`, `Owner = "tid:{tid}/user:{oid}"`. A missing `oid` → 401 "Delegated token has no oid claim." *(writer's call: `oid`, not `sub`. In Entra, `sub` is pairwise per app, while `oid` is stable for the user across apps in the tenant. So a user keeps the same tasks when the front end changes app id. It still differs per tenant.)*
3. Authenticated, with `tid` and no `scp` (app-only) → `Kind = Application`, `Owner = "tid:{tid}/app:{appid}"`, where `appid` = `azp`, else `appid`. If neither is present → 401 "App-only token has no azp or appid claim."
4. Authenticated, with no `tid` → 401: "The caller was authenticated by scheme '{AuthenticationType}', which the default owner resolver does not understand. Register an IOwnerResolver for this scheme (see docs: Security > Other schemes)." Today's fallbacks (`apikey:{name}`, `user:{sub}`, `src/A2A.Server/Auth/DefaultOwnerResolver.cs:44-55`) are removed. *(writer's call: refusing is safer than guessing, and it is the extension point for other schemes.)*

Owner keys have a typed public builder *(Claude's call, B7)*:

```csharp
public readonly record struct OwnerKey
{
    public static OwnerKey ForApp(string tenantId, string appId);          // "tid:{tid}/app:{appid}"
    public static OwnerKey ForUser(string tenantId, string userObjectId);  // "tid:{tid}/user:{oid}"
    public static OwnerKey Parse(string value);
    public string Value { get; }
}
```

The string format is part of the public contract and frozen at 1.0, because every stored task holds it (05). Owner key strings are opaque to the store. Grants are built from `OwnerKey` (below). Other schemes' resolvers build their own strings (for example `apikey:{name}`) through `OwnerKey.Parse`. `CallerIdentity` (`src/A2A.Server/Auth/CallerIdentity.cs`) and `DefaultApiKeyAuthenticationType` are deleted (R19, last bullet). The sample API key handler gets its own small `IOwnerResolver` that wraps the default one and maps its identity to `apikey:{name}`. That sample is the documented pattern for other schemes.

Ownership checks stay where they are today: `LoadOwnedAsync` and `LoadForContinuationAsync` (`src/A2A.Server/Tasks/TaskManager.cs:407-427`). They compare `Caller.Owner` and tenant (below), and they answer `TaskNotFound` on a mismatch.

### Anonymous access

- Every agent is `AgentAccess.Authenticated` by default. On every operation except the card endpoints and the push JWKS, an unauthenticated caller gets 401 with a challenge.
- `AddAgent<T>(...).AllowAnonymous()` (or `AgentAccess.Anonymous`) opts one agent in. For anonymous callers on such an agent: *(writer's call)*
  - Owner is `anonymous`.
  - `ListTasks` returns an empty page with `nextPageToken = ""`. Anonymous callers never list.
  - Get, Cancel, Subscribe and push-config operations work by task id. The task id is a server-made 128-bit random value, so it acts as a capability. The docs say this plainly.
  - An authenticated caller is never treated as anonymous on such an agent. It gets its own owner as usual.
- Local development *(Claude's call, C8)*: when the host environment is `Development` and no authentication scheme is registered at all, agents with no explicit access mode run as `Anonymous`, and startup logs one warning per agent. This keeps "nothing to configure for local development" (principle 3) without changing the production default. In any other environment, an agent that is `Authenticated` while no authentication scheme is registered fails startup: "Agent 'x' requires authentication but no scheme is registered. Call AddEntraAuthentication(...) or mark the agent AllowAnonymous()."
- `AgentRegistration.AuthorizationPolicy` (`src/A2A.Server/Agents/AgentRegistration.cs:16`) stays as an optional extra ASP.NET Core policy on top of the card requirements.

### Continuing an `AUTH_REQUIRED` task

```csharp
public sealed record TaskContinuationGrant(
    IReadOnlyList<OwnerKey> AllowedOwners, // exact match (B7)
    DateTimeOffset ExpiresAt);

public interface ITaskContinuationPolicy
{
    bool CanContinue(StoredTask task, A2ACaller caller);
}
```

- Default rule: allowed when `caller.Owner == task.Owner` (for any non-terminal state, as today). It is also allowed when the task state is `AUTH_REQUIRED`, the task has a grant, `caller.Owner` is in `AllowedOwners`, `now < ExpiresAt`, and the caller's A2A tenant equals the task's tenant.
- Today's rule ("any app in the same tenant", `src/A2A.Server/Auth/DefaultOwnerResolver.cs:61-67`) is removed.
- The agent sets the grant when it moves the task to `AUTH_REQUIRED`. M1 adds one overload to `IAgentEventSink`: `RequireAuthAsync(Message statusMessage, TaskContinuationGrant? grant, CancellationToken ct)`. Package 11 may wrap it in nicer helpers. The grant's `ExpiresAt` has no default; the agent must choose it. *(writer's call: an open-ended grant is a standing permission.)*
- The grant is cleared as soon as the task leaves `AUTH_REQUIRED`, so it is single-use (A2A #2081). The task keeps its original owner.
- What a grant holder gets *(Claude's call, B6)*: it receives the response of its own continuation call (the blocking result, or the stream up to a terminal or interrupted state), because the protocol returns it. It cannot `GetTask`, `ListTasks`, `SubscribeToTask` or `CancelTask` the task afterwards unless it is the owner (`TaskNotFound`). Package 10's client and package 11 follow this.
- Package 05 stores the grant with the task (`StoredTask.ContinuationGrant`, nullable).
- A grant may name `CallerKind.Application` owners and `User` owners. The docs show the common case: a user-facing app hands step-up to a named approval app.

### Tenant isolation

```csharp
public interface ITenantResolver
{
    /// The trusted A2A tenant for this request, or null for "no tenant".
    /// Throws A2AException(InvalidParams) when the request's tenant is not served here.
    ValueTask<string?> ResolveAsync(TenantResolutionContext context, CancellationToken ct);
}

public sealed record TenantResolutionContext(
    AgentRegistration Agent, string? RouteTenant, string? RequestTenant, A2ACaller Caller, string Transport);
```

- The "A2A tenant" is the routing tenant of the spec (`AgentInterface.tenant`, request `tenant`). It is not the Entra `tid`.
- `DefaultTenantResolver`:
  - If the transport found a tenant in the route (REST `/{tenant}/...`, `src/A2A.Server/Transports/Rest/RestEndpoints.cs:164-178`), that is the trusted value. A different non-empty body tenant is `InvalidParams`, as today.
  - Otherwise the request tenant is used, but only if it is one of the agent's configured tenants (`A2ASecurityOptions.Tenants` per agent, or the tenants on the agent's card interfaces). An unknown tenant → `InvalidParams` "Tenant 'x' is not served by agent 'y'." An agent with no configured tenants accepts only an empty tenant.
  - Optional binding: `A2ASecurityOptions.TenantBindings[a2aTenant] = [entraTid, ...]`. When it is set, a caller whose `EntraTenantId` is not in the list gets 403. *(writer's call: this lets one deployment serve several product tenants safely.)*
- The resolved tenant goes into `A2ACallContext.Tenant`. Every store call passes it: package 05's `TaskKey(Tenant, Agent, TaskId)` and `OwnerScope(Tenant, Agent, Owner)` carry it, and push-config calls use `TaskRef`, which wraps a `TaskKey`. An empty tenant is its own partition. The comment "Phase 1 does not isolate by it" (`src/A2A.Server/A2ACallContext.cs:11`) goes away.
- gRPC has no route tenant. It uses the request field with the same allow-list. Package 09 owns any future routing by tenant.

### Card-driven authorization

The card's `securityRequirements` (agent level, `src/A2A.Protocol/Models/AgentCard.cs:62`) and each skill's `securityRequirements` (`:42`) become the enforced rules.

```csharp
public interface ISecurityRequirementEvaluator
{
    /// The card scheme names this evaluator handles.
    bool CanEvaluate(string schemeName, SecurityScheme scheme);
    /// True when the caller satisfies the scheme with these scopes.
    ValueTask<bool> IsSatisfiedAsync(A2ACaller caller, string schemeName, SecurityScheme scheme,
        IReadOnlyList<string> scopes, CancellationToken ct);
}

public interface ISkillAuthorizer
{
    /// Throws A2AAuthException (403) when the caller does not meet the skill's requirements.
    ValueTask AuthorizeAsync(A2ACaller caller, AgentRegistration agent, string skillId, CancellationToken ct);
}
```

- Semantics follow OpenAPI, as the spec models them: the list of requirements is OR. Within one requirement, all schemes are AND. An empty list means no requirement.
- At startup, every scheme name used in any requirement (agent or skill, public or extended card) must exist in the card's `securitySchemes`. It must also have a registered evaluator. Otherwise startup fails with the agent, skill and scheme named.
- Agent level: checked on every operation except card fetch, in the `IA2AMiddleware` pipeline (package 09 §7.11, step 3), after the access-mode check and before the handler. Transports only authenticate and build the caller; they never authorize *(Claude's call)*. So the check is the same on JSON-RPC, REST, gRPC and in-process calls, and the skill-level checks use the same path. Failure → 403. This replaces the "one ASP.NET policy per agent" model (`src/A2A.Server/Agents/AgentRegistration.cs:10-16`) as the main path.
- Skill level: a message does not name a skill, so the SDK cannot know the skill up front.
  - If the agent's card has exactly one skill, that skill's requirements are checked with the agent-level ones. *(writer's call)*
  - Otherwise the agent (or package 11's skill router) calls `context.AuthorizeSkillAsync("skill-id")`, backed by `ISkillAuthorizer`.
  - If the check fails before the first task event, the call fails with 403. If it fails after, the task ends `REJECTED` with a status message: "Not authorized for skill 'x'." The message never lists the caller's claims. *(writer's call)*
- Extended card: `GetExtendedAgentCard` requires authentication (3.1.11). With `A2ASecurityOptions.FilterExtendedCardSkills = true` (default), it returns only the skills whose requirements the caller meets. *(writer's call)*
- Built-in evaluator in the core: `openIdConnect` schemes are satisfied by any authenticated caller.

### Server Entra building blocks (`Ambinder.A2A.AspNetCore.Entra`)

```csharp
public static A2AServerBuilder AddEntraAuthentication(this A2AServerBuilder builder, IConfiguration section);
public static A2AServerBuilder AddEntraAuthentication(this A2AServerBuilder builder, Action<A2AEntraServerOptions> configure);
```

`A2AEntraServerOptions` (bound from `A2A:Entra` by convention):

| Option | Default | Meaning |
|---|---|---|
| `Instance` | `https://login.microsoftonline.com/` | Authority host. Sovereign clouds set their own. |
| `TenantId` | required | A tenant GUID (single-tenant), or `organizations` (multi-tenant). `common` is rejected: personal accounts are not supported. *(writer's call)* |
| `ClientId` | required | The API app's client id. Accepted as `aud` (v2 tokens use it). |
| `AppIdUri` | `api://{ClientId}` | Also accepted as `aud`. Used to build `{AppIdUri}/.default`. |
| `AllowedTenants` | `[]` | Required when `TenantId` is `organizations`. Empty → startup error, unless `AllowAnyTenant = true`. |
| `AllowAnyTenant` | `false` | Explicit opt-in to any Entra tenant. |
| `AcceptV1Tokens` | `false` | v1 issuers (`sts.windows.net`) are rejected unless this is set. The runbook sets `requestedAccessTokenVersion = 2`. |
| `InvokeRole` | `A2A.Invoke` | The app role for app-only callers. |
| `InvokeScope` | `A2A.Invoke` | The delegated scope for user callers. Null → delegated tokens are rejected (403). |
| `SchemeName` | `entra` | Card scheme name for client credentials. |
| `UserSchemeName` | `entraUser` | Card scheme name for the authorization-code flow (only when `InvokeScope` is set). |
| `OidcSchemeName` | `entraOidc` | Card scheme name for OpenID Connect metadata. |
| `AuthenticationScheme` | `A2AEntra` | The ASP.NET Core scheme name. |
| `RequireHttpsMetadata` | `true` | `false` only allowed in `Development` (startup check, as in `samples/AgentServer/Auth/AuthSetup.cs:32-35`). |
| `ClockSkew` | 2 minutes | Token lifetime skew. |

Behavior:
- It registers JwtBearer with `MapInboundClaims = false`, `RoleClaimType = "roles"`, `NameClaimType = "azp"`, valid audiences `[ClientId, AppIdUri]`, signature algorithms RS256 only, and issuer validation:
  - Single tenant: exact v2 issuer `{Instance}{TenantId}/v2.0`.
  - Multi-tenant: issuer template validation, then `tid` must be in `AllowedTenants`, plus the signing-key issuer check.
- If no default authentication scheme is set yet, it becomes the default authenticate and challenge scheme. If the app already set a default, the package does not override it. *(writer's call)*
- The OpenID-metadata outage handling moves here from `samples/AgentServer/Auth/AuthSetup.cs:66-105`: metadata unavailable → 401 plus a warning log naming the authority, never 500.
- It registers `EntraSecurityRequirementEvaluator`. For a scheme it generated (matched by name and token URL host), the requirement scopes are satisfied this way:
  - App-only callers: every listed scope, after stripping the `{AppIdUri}/` prefix and `.default`, must be in `roles`. The bare `.default` scope maps to `InvokeRole`.
  - User callers: every listed scope must be in `scp`.
  - Hand-written Entra schemes with other names need their own evaluator registration (`AddEntraEvaluatorFor("name")`).
- It registers `EntraCardSecurityContributor`, which adds to each card of an `Authenticated` agent:
  - `securitySchemes[SchemeName]`: oauth2, flow `clientCredentials`, `tokenUrl = {Instance}{TenantId}/oauth2/v2.0/token`, scopes `{ "{AppIdUri}/.default": "Call this agent as an application" }`.
  - `securitySchemes[UserSchemeName]` (only if `InvokeScope` is set): oauth2, flow `authorizationCode`, `authorizationUrl` and `tokenUrl` under the same authority, `pkceRequired = true`, scopes `{ "{AppIdUri}/{InvokeScope}": "Call this agent as a signed-in user" }`. There are two schemes because `OAuthFlows` allows exactly one flow (`src/A2A.Protocol/Models/Security.cs:56`, proto oneof).
  - `securitySchemes[OidcSchemeName]`: openIdConnect, `{Instance}{TenantId}/v2.0/.well-known/openid-configuration`.
  - `securityRequirements`: `[{SchemeName: ["{AppIdUri}/.default"]}, {UserSchemeName: ["{AppIdUri}/{InvokeScope}"]}]`, the second only if delegated is on.
  - For multi-tenant, `TenantId` in the URLs is `organizations`.
- `EntraCardSecurityContributor` implements package 03's `IAgentCardContributor`. It runs in package 09's card build step (today `AgentCardInterfaces.Apply`, `src/A2A.Server/Hosting/A2AServerBuilder.cs:47-48`). It only adds: new keys in `securitySchemes`, and `securityRequirements` when the card has none. It never changes a member the agent set, which fits 03's "must not change existing members" rule. A generated scheme name that clashes with a hand-written one fails startup. Hand-written requirements on the card are kept; generated ones are added only if the card has none at agent level. *(writer's call)*
- Policy helpers for teams that still want ASP.NET policies: `AuthorizationPolicyBuilder.RequireA2AEntraAccess(roles: [...], scopes: [...])`. It passes for app-only callers that have all the roles, and for user callers that have all the scopes.

Minimal use (the sample's `AuthSetup.cs` shrinks to this):

```csharp
builder.Services.AddA2AServer(o => o.PublicBaseUrl = "https://agents.contoso.com")
    .AddEntraAuthentication(builder.Configuration.GetSection("A2A:Entra"))
    .AddAgent<ReportAgent>("report", ReportCard.Build);
```

### Client Entra building blocks (`Ambinder.A2A.Client.Entra`)

Both providers implement package 10's `IA2ACredentialProvider` (`ApplyAsync(A2ACredentialContext, ct)`, with `ForceRefresh` and the parsed `Challenge`; `ForceRefresh` is true exactly on the auth retry).

```csharp
public static IA2AClientBuilder AddEntraCredentials(this IA2AClientBuilder builder, Action<A2AEntraClientOptions> configure);
public static IA2AClientBuilder AddEntraOnBehalfOf(this IA2AClientBuilder builder, Action<A2AEntraOboOptions> configure);
```

`A2AEntraClientOptions`:

| Option | Default | Meaning |
|---|---|---|
| `Scope` | required | For example `api://billing-agent/.default`. |
| `Credential` | null | A `TokenCredential` to use. If it is null, `CredentialType` picks one. |
| `CredentialType` | `Auto` | `Auto`, `WorkloadIdentity`, `ManagedIdentity`, `ClientCertificate`, `ClientSecret`, `AzureCli`, `Default`. |
| `TenantId`, `ClientId` | from env (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`) | Used by the typed credentials. |
| `CertificatePath` / `CertificateThumbprint` | null | For `ClientCertificate`. |
| `RefreshBefore` | 5 minutes | Refresh window before `ExpiresOn`. If Azure.Core sets `AccessToken.RefreshOn`, that is used. |
| `AllowCardScopes` | `[]` | App ID URIs whose scopes may be taken from the card when `Scope` is not set. Empty → never. |

- `Auto` *(writer's call)*:
  - In `Development`: `DefaultAzureCredential` (developer tools).
  - In any other environment: `ChainedTokenCredential(WorkloadIdentityCredential, ManagedIdentityCredential)`. `DefaultAzureCredential` probes many sources, which makes it slow and unpredictable in production.
  - `ClientSecret` is supported but logs a warning outside `Development`.
- Caching: one cache per provider instance, keyed by (scope, claims, tenant). A single-flight refresh per key (one `SemaphoreSlim` per key). The cache is bounded at 64 keys. `ForceRefresh` (package 10 sets it after a 401) bypasses the cache once. A `WWW-Authenticate` claims challenge (`error="insufficient_claims"`, `claims="..."`) is passed as `TokenRequestContext.Claims`. *(writer's call: not every `TokenCredential` caches, and we need forced refresh semantics either way.)*
- The scope never comes from the card unless the card's scope's resource is in `AllowCardScopes`. This blocks the token-collection attack in the anti-goals. It mirrors `ClientCredentialsProvider.AllowedTokenHosts` today (`src/A2A.Client/Auth/ClientCredentialsProvider.cs:61-62`).
- OBO: `EntraOnBehalfOfCredentialProvider` uses Azure.Identity `OnBehalfOfCredential`.
  - The user assertion comes from `A2ARequestOptions.UserAssertion` (package 10) or, if that is not set, from a user-assertion delegate (`A2AEntraOboOptions.UserAssertionProvider`, `Func<CancellationToken, ValueTask<string?>>`) *(Claude's call, B10)*. The client package does not reference the server's `IA2ACallerAccessor`.
  - `Ambinder.A2A.AspNetCore.Entra` provides the adapter `AddCallerAssertionForOnBehalfOf()`, which sets the delegate to read `IA2ACallerAccessor.Current.AccessToken`. That requires `KeepCallerToken = true` on the server, so identity flows from the incoming call to the outgoing one. In a run on another pod or a recovered run there is no token (B8).
  - If there is no assertion, the provider throws with a message naming both sources.
  - Client authentication for OBO: a certificate, a secret, or a client assertion callback. On AKS the callback reads the workload identity token file. The builder confirms the `OnBehalfOfCredential` overload that takes an assertion callback in the pinned Azure.Identity version.
  - Credential instances are cached per SHA-256 of the user assertion, LRU of 1,000, and evicted at token expiry.
- Remote agent as an LLM tool (package 11's `A2AAgentTool`) *(Claude's call, B9)*: it calls with the agent's own app identity (`EntraCredentialProvider`) by default. On-behalf-of is opt-in. The tool binds every remote task and context id it creates to the parent caller's owner key, and refuses ids it did not create for that owner. This stops a prompt from steering the tool to another user's remote task.
- Extended cards on the client *(Claude's call, B11)*: package 10 caches them per credential identity, never across identities, because the server filters the extended card by caller (above).
- `ClientCredentialsProvider` and `ApiKeyCredentialProvider` move to `samples/AgentClient` as examples of other schemes (decision: other schemes do not ship). `BearerTokenCredentialProvider` stays in the client core as a delegate-based `DelegateCredentialProvider`, for tests and custom schemes. *(writer's call)*

### Extension points for other schemes

A team that uses another scheme (API key, mTLS, another OIDC provider) does four things, and the docs show each one with the API key sample:
1. Register any ASP.NET Core authentication handler.
2. Register an `IOwnerResolver` (it can decorate `DefaultOwnerResolver` and handle only its own identity type).
3. Register an `ISecurityRequirementEvaluator` for its card scheme name, and a package 03 `IAgentCardContributor` that adds its scheme (or put the scheme on the card by hand).
4. On the client, implement `IA2ACredentialProvider`.

Card changes go only through package 03's `IAgentCardContributor`, as package 09 requires. This package adds no card seam of its own.

### Error behavior

| Situation | Result |
|---|---|
| No credentials on an `Authenticated` agent | 401 plus `WWW-Authenticate` from the scheme. The JSON-RPC and REST bodies stay empty, as today (`src/A2A.Server/Transports/TransportRequest.cs:31-45`). gRPC: `UNAUTHENTICATED`. |
| Bad token (signature, audience, issuer, expiry, wrong tenant) | 401. Logged at Information with the reason code, never the token. |
| Tenant not in `AllowedTenants` | 401, log "tenant {tid} not allowed". |
| Owner cannot be resolved | 401 with the resolver's message. |
| Agent-level requirement not met | 403. gRPC: `PERMISSION_DENIED`. |
| A2A tenant not served | `InvalidParams` (-32602 / 400 / `INVALID_ARGUMENT`). |
| Entra tenant not bound to the A2A tenant | 403. |
| Another owner's task, or a continuation without a grant | `TaskNotFound`. |
| Skill requirement not met | 403 before the task starts; `REJECTED` task after. |
| Startup misconfiguration | `InvalidOperationException` before the server listens, naming the option and the fix. |

### Testing against a real tenant

There are two tiers. *(writer's call)*

1. On every PR: `FakeEntraAuthority`.
   - An in-process test authority built from `samples/MockEntra/TokenIssuer.cs`. It serves OIDC metadata and JWKS, and it issues v1 and v2, app-only and delegated tokens. It supports several tenants and issuer templates, so multi-tenant tests run without a tenant.
   - It lives in the repo test fixtures. Package 12 keeps it repo-only; teams get `TestIdentity` from `Ambinder.A2A.Testing` instead *(Claude's call, C13)*.
   - Every row of the error table and every owner rule has a test here, on all three transports.
2. Nightly, on release tags, and on demand: workflow `entra-live.yml`. It runs only when the test-tenant settings exist (repository variables for the tenant and app ids); otherwise every job step is skipped with a visible "skipped: no test tenant" notice (owner decision O3). Until a tenant exists, the manual pre-release checklist covers these cases.
   - It uses a dedicated test tenant (tenant A) and a second tenant (tenant B).
   - App registrations: the agent API (roles `A2A.Invoke`, `Reports.Approve`; scope `A2A.Invoke`), client app 1 (has `A2A.Invoke`), client app 2 (no roles), and a multi-tenant client registered in tenant B.
   - The workflow signs in with GitHub OIDC federated credentials on each client app, through Azure.Identity `ClientAssertionCredential`. No secrets are stored. This is the same mechanism as AKS workload identity.
   - The agent server runs in the job with real `AddEntraAuthentication`.
   - The tests cover:
     - app-only owner keys
     - a missing role (403)
     - wrong audience (401)
     - tenant B rejected in single-tenant mode, and accepted in multi-tenant mode with an allow-list
     - v1 tokens rejected
     - two client apps not seeing each other's tasks
     - a grant-based continuation
     - the generated card schemes matching the tenant's real metadata URLs
   - Package 09's AKS end-to-end job covers `WorkloadIdentityCredential` itself.
   - Delegated tokens and OBO cannot be obtained without a user sign-in in CI. They are covered by the fake authority, plus a scripted manual check (device code) in the pre-release checklist for every release *(Claude's call, C8)*. No test user with a non-interactive grant is set up.
   - Live-tenant failures do not block PRs. They block a release tag when the job runs.

### Migration from current code

- `IOwnerResolver.GetOwner` and `CanContinueInterrupted` (`src/A2A.Server/Auth/IOwnerResolver.cs:10-19`) are replaced by `ResolveAsync` and `ITaskContinuationPolicy`. Pre-1.0, there is no compatibility shim.
- `IAgentAuthorizer` and `AgentAuthorizer` become internal and run the access mode, the card requirements and the optional extra policy.
- `TransportRequest.AuthorizeAsync` (`src/A2A.Server/Transports/TransportRequest.cs:17-25`) returns an `A2ACaller` and the resolved tenant.
- The sample `AuthSetup.cs` shrinks to `AddEntraAuthentication` plus the API key example. The sample `AgentCards.cs` drops the hand-written Entra schemes (`samples/AgentServer/Cards/AgentCards.cs:109-161`).
- MockEntra stays as a Docker Compose sample.
- `docs/production.md` §1 is replaced by the runbook (package 13).

## How it meets the six principles

1. **Protocol.** It follows the authentication, authorization, ListTasks scoping, extended-card and in-task-auth rules (spec §3.1.4, §3.1.11, §7). Check: TCK MUST and SHOULD auth scenarios pass. A dedicated test shows that `AUTH_REQUIRED` alone grants nothing.
2. **.NET enabler.** It uses ASP.NET Core authentication and authorization, the Options pattern, `IConfiguration` binding, and Azure.Identity `TokenCredential`. Check: public API review (package 02). Every public member has XML docs.
3. **Easy.** Entra server setup is one call plus a config section, and the client is one call. Check: the getting-started agent with Entra is at most 25 lines. Each startup misconfiguration has a test that asserts its message names the fix.
4. **Performance.** Owner, tenant and requirement checks add no network calls and allocate little. Check: package 14 benchmark `AuthorizeRequest` (resolve caller, tenant, evaluate requirements) is under 5 µs and under 1 KB allocated per call. The client token cache hit path is under 1 µs.
5. **Modular.** Entra lives in separate packages, and every rule is an interface with a default. Check: `Ambinder.A2A.Server` and `Ambinder.A2A.Client` have no `Azure.*` or `Microsoft.Identity*` package references (a CI check on the package graph). The API key sample works using only the extension points.
6. **Integration pains.** It ends sample copy-paste, provides OBO identity propagation and per-skill hooks, and tests against a real tenant. Check: the `entra-live` job is green before a release when it can run; otherwise the manual pre-release checklist is signed off (O3). The sample server has no auth code beyond config.

## Dependencies

Needs:
- 01: package names and the server and client split.
- 02: public API tracking for the new types.
- 05: store contract with a tenant on every task and push-config lookup, plus `StoredTask.ContinuationGrant`. The conformance suite must include the tenant-isolation and owner-isolation cases from Goal 4.
- 10: the `IA2ACredentialProvider` shape (`ApplyAsync(A2ACredentialContext, ct)`), `A2ARequestOptions.UserAssertion`, and `IA2AClientBuilder`. This is an interface-only need: 10's spec already fixes these shapes, so 07 builds against them first and the work-plan order "07 → 10" (10's auth retry uses 07's providers) still holds.
- 03: `IAgentCardContributor` must allow adding new keys to `securitySchemes` and adding `securityRequirements` when absent (it forbids changing existing members, not adding).
- 09: the card build step where contributors run, and the `IA2AMiddleware` pipeline. The agent-level requirement check, the skill auto-check and the continuation policy run as an `IA2AMiddleware` registered by this package (09 §7.11 step 3), so they behave the same on every transport and for in-process calls. Package 09's AKS job runs workload identity end to end.

Provides:
- To 10: `EntraCredentialProvider` and `EntraOnBehalfOfCredentialProvider` (with the user-assertion delegate, B10), the semantics of `ForceRefresh` and claims challenges, the grant-holder rule (B6), and the identity rule for extended-card caching (B11).
- To 11: `A2ACaller`, `OwnerKey` (B7), the grant-holder rule (B6), `IA2ACallerAccessor`, `ISkillAuthorizer`, `TaskContinuationGrant`, and `IAgentEventSink.RequireAuthAsync(..., grant, ...)`. Package 11 uses `ITaskContinuationPolicy` (not the old `IOwnerResolver.CanContinueInterrupted`) and has an optional `TaskContinuationGrant` on `TaskUpdater.RequireAuthAsync`. 11's `Caller`, `Owner`, `Tenant` and `User` on the context map to `A2ACaller`, `A2ACaller.Owner`, the resolved tenant and `A2ACaller.Principal`.
- To 06: the reduced caller (B8) and the tenant, which are forwarded between pods as data (no re-resolution on the owner pod; the receiving pod already ran the checks, B5).
- To 05: `OwnerKey` (B7) and the reduced-caller fields (B8).
- To 09: the reduced caller for recovered runs (B8).
- To 09: the claim rules behind `OwnerKey` (tenant id plus app id, or user object id) so the rate limiter can build its partition key from the token alone, before the body is read *(Claude's call)*. No dependency on `A2ACaller` resolution.
- To 12: `FakeEntraAuthority` (repo test fixture only, C13), and the claims shape `TestIdentity` must produce.
- To 13: the security model section, the Entra runbook, and the "other schemes" guide.

## Acceptance criteria

- [ ] Unit tests for `DefaultOwnerResolver`: app-only (`azp` and `appid`), delegated (`scp` + `oid`), a missing `oid`, a missing app id, no `tid`, and mapped claim names.
- [ ] Integration tests on JSON-RPC, REST and gRPC:
  - [ ] anonymous refused by default
  - [ ] `AllowAnonymous` behavior, including the empty `ListTasks`
  - [ ] two users of one app isolated
  - [ ] two apps of one tenant isolated
- [ ] `OwnerKey.ForApp` and `OwnerKey.ForUser` produce the documented strings; a golden test freezes the format (B7).
- [ ] A grant holder gets the response of its continuation call (blocking and streaming) and then `TaskNotFound` on Get, List, Subscribe and Cancel (B6).
- [ ] A recovered or forwarded run sees the reduced caller and no access token; OBO in such a run fails with the documented message (B8).
- [ ] Development with no scheme: anonymous with one startup warning per agent; any other environment with no scheme and no `AllowAnonymous()` fails startup (C8).
- [ ] Continuation tests: same owner; a granted owner within expiry; a granted owner after expiry (`TaskNotFound`); a granted owner after the task left `AUTH_REQUIRED` (`TaskNotFound`); another app in the same Entra tenant (`TaskNotFound`).
- [ ] Tenant tests: route tenant wins; unknown tenant is `InvalidParams`; cross-tenant get, list, cancel and subscribe give `TaskNotFound`; an Entra tenant binding is enforced.
- [ ] Requirement tests: OR and AND semantics; a missing evaluator fails startup; the single-skill auto check; `AuthorizeSkillAsync` before and after the first event (403 versus `REJECTED`); the extended card filtered by skill.
- [ ] Entra server tests with `FakeEntraAuthority`: single-tenant, multi-tenant with an allow-list, `AllowAnyTenant`, v1 rejected, wrong audience, `RequireHttpsMetadata` outside Development fails startup, metadata outage gives 401 not 500, and the generated card schemes match a golden JSON file.
- [ ] Entra client tests: cache hit, refresh before expiry, `ForceRefresh`, claims challenge passed through, a card scope refused unless allowed, the `Auto` credential choice per environment, and OBO taking the assertion from the delegate, with `AddCallerAssertionForOnBehalfOf()` reading it from `IA2ACallerAccessor` (B10).
- [ ] No token, assertion or `Authorization` value appears in captured logs or spans in any test (a log and trace scrubber assertion in the shared fixture).
- [ ] The `entra-live.yml` workflow exists, uses federated credentials only, runs nightly when the test-tenant settings exist, and is skipped visibly when they do not (O3). The manual pre-release checklist has the device-code check for delegated tokens and OBO (C8).
- [ ] A package-graph CI check proves the core packages have no Azure or Microsoft.Identity references.
- [ ] TCK auth-related MUST scenarios pass on all three transports (package 03 job).
- [ ] Docs (package 13 sections written by this package): security model (owner, tenant, continuation, card-driven authorization, threat notes), the Entra app-registration runbook used by `entra-live`, and "Other auth schemes" with the API key sample.
- [ ] Benchmarks `AuthorizeRequest` and `EntraTokenCacheHit` are in the package 14 suite with a baseline.

## Left to the builder

- Internal class layout, and whether `A2ASecurityOptions` sits inside `A2AServerOptions` or beside it.
- How the principal is deep-copied (clone the identities and claims, or rebuild from claims).
- Exact log event ids and messages, as long as they name the fix and contain no secrets.
- Where `FakeEntraAuthority` keeps its keys, and its fluent API.
- Whether `AddEntraEvaluatorFor` is a separate method or an option.
- The exact `entra-live` test list beyond the cases named above, and the Bicep or Terraform for the test tenant's app registrations.
- Whether the client token cache uses `ConcurrentDictionary` plus semaphores or a small LRU type.

## Open questions

None. Live delegated and OBO coverage (Q1) and the Development anonymous default (Q2) are Claude's call C8 ("Testing against a real tenant", "Anonymous access").
