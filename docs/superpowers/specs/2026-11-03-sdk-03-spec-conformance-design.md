# Package 03: Spec conformance and TCK

Date: 2026-11-03
Status: draft for approval
Milestone: M1
Size: L

## Intent

The SDK claims to follow A2A v1.0.1. Today nothing proves it: the TCK does not run in CI (`README.md:192-200`), and several spec rules are missing or partial (R9, R10, R12). This package makes conformance a fact that CI checks on every PR, on all three transports. It also fixes the known spec gaps in the core, so packages built later stand on correct protocol behavior.

Most of the work is in the existing core assemblies (after package 01 they are `Ambinder.A2A.Protocol`, `Ambinder.A2A.Server`, `Ambinder.A2A.AspNetCore`, `Ambinder.A2A.Client`, and the two gRPC packages `Ambinder.A2A.AspNetCore.Grpc` and `Ambinder.A2A.Client.Grpc`, which share the proto code by source; names from package 01's map). This package adds no new NuGet package.

## Findings addressed

- R9: version handling. Only "1.0" is accepted, the client ignores `AgentInterface.ProtocolVersion`, no clear 0.3 answer.
- R10: strict enums. Unknown enum strings fail the whole payload.
- R12: missing `A2A-Extensions` response echo; deprecated OAuth flows dropped on round-trip; proto pinned to a `main` commit with no drift check.
- R37: TCK not automated.
- R26 (card part only): cards served with no Cache-Control or ETag.
- Pain point 2, "JSON serialization drift between SDKs": tolerant inbound, strict outbound.
- Pain point 3, "Version negotiation and mixed-version peers".
- Pain point 6, "Error formats and error mapping".
- Pain point 7, "Binding differences ... URL and tenant handling" (client tenant, request tenant match).
- Pain point 8, "Task and context ID handling" (server-side rules only).
- Pain point 24, "Spec/sample quality that misleads integrators": a written spec-decisions doc.

## Goals

1. The official TCK (`a2aproject/a2a-tck`) runs in CI on every PR against a .NET system under test (SUT), once per transport: `jsonrpc`, `http_json`, `grpc`.
2. The CI job fails on any MUST failure and on any SHOULD failure (TCK reports SHOULD as xfail; we treat it as a failure), except entries in a reviewed known-issues file.
3. The server validates `A2A-Version` on every operation on every transport. An empty value means 0.3. Unsupported versions get `VersionNotSupportedError` with the supported versions in `ErrorInfo.metadata`.
4. The client sends `A2A-Version` on every request and picks an interface by binding and protocol version.
5. Unknown enum values are read without failing the payload. Outbound JSON always uses ProtoJSON enum names.
6. Every A2A error on every transport carries `google.rpc.ErrorInfo` (reason, domain `a2a-protocol.org`, metadata). The client maps errors back to the same typed `A2AException` on all transports.
7. The server echoes activated extensions in the `A2A-Extensions` response header (HTTP) and `a2a-extensions` response metadata (gRPC). A required extension that the client did not declare gives `ExtensionSupportRequiredError` on every operation and transport.
8. `ListTasks` follows spec 3.1.4 on all transports: `nextPageToken` always present, status timestamp descending order, artifacts omitted unless `includeArtifacts` is true, scoping applied before any lookup.
9. The agent card endpoint sends `Cache-Control`, a strong `ETag` and `Vary: A2A-Version`, and answers `If-None-Match` with 304.
10. The client sets `tenant` on every request to exactly the tenant of the selected interface. The server rejects a request whose `tenant` does not match the interface it arrived on.
11. `contextId` / `taskId` rules from spec 3.4 hold and have a test each.
12. Deprecated `implicit` and `password` OAuth flows survive JSON and proto round-trips.
13. The vendored proto is the `v1.0.1` release tag. A CI check fails when the vendored file differs from the tag.
14. A `docs/spec-decisions.md` page lists every place where the spec is open and the choice this SDK made.

## Non-goals

- Serving or calling 0.3 peers. Package 15 does that; this package only gives the clear interim error and the seam package 15 plugs into.
- Card signing and verification. Package 04.
- Client-side card caching that honors Cache-Control/ETag (RFC 9111). Package 10.
- Moving `ListTasks` validation out of the store and the store conformance suite. Package 05. This package defines the rules and tests them end to end.
- Tenant resolution from tokens or routing, and tenant isolation in stores. Packages 07 and 05.
- Cross-SDK interop runs and golden JSON fixtures. Package 16.
- An extension framework (handlers, typed params). Package 11. This package only does header negotiation, echo and the required check.
- Skill-level `securityRequirements` enforcement (R12 first sub-item). Package 07.
- JSON-RPC batch support. Allowed by the spec to reject; stays rejected.
- SSE resume / `Last-Event-ID`. Not defined by the spec (open issue #1635).

## Anti-goals

- A green TCK job that hides failures: broad skip lists, `continue-on-error`, or a TCK commit pinned to a version that lacks the failing tests.
- Conformance code only in the SUT or samples. Every fix lands in the library.
- Tolerant reading that silently changes meaning, for example turning a 0.3 `"completed"` into a usable state.
- One transport behaving differently from the others for the same input.
- New public types added only to satisfy the TCK.

## Constraints

- Spec: A2A v1.0.1 (2026-05-28). Proto pinned to tag `v1.0.1` (work plan, "Spec version"). v1.1 items are out of scope.
- Missing `A2A-Version` is 0.3; until package 15 ships, the core answers `VersionNotSupported` (work plan, "A2A 0.3").
- Spec 3.6: client MUST send `A2A-Version: Major.Minor` (header, or query parameter `A2A-Version`); empty header MUST be treated as 0.3; patch is not used in negotiation.
- Spec 3.3, 5.4: errors carry code, message, details; each detail has `@type`; A2A errors MUST include `google.rpc.ErrorInfo`. Code table: TaskNotFound -32001/NOT_FOUND/404 ... VersionNotSupported -32009/FAILED_PRECONDITION/400 (full table in `research-spec.md` section 3). The current catalog already matches it (`src/A2A.Protocol/Errors/A2AError.cs:16-32`).
- Spec 4.6, 3.2.6: `A2A-Extensions` header / `a2a-extensions` gRPC metadata, comma-separated URIs.
- Spec 3.1.4 (ListTasks), 3.4 (task and context ids), 8 (card, caching, tenant), 5 (serialization: camelCase, ProtoJSON enum names, UTC `Z` timestamps, unknown fields ignored).
- TCK: `a2aproject/a2a-tck`, version 1.0.0 in pyproject, no GitHub releases, Python 3.11+ with `uv`. Run with `./run_tck.py --sut-host <url> --transport <grpc|jsonrpc|http_json> --level <must|should|may>`. It reads `{host}/.well-known/agent-card.json` and tests every transport in `supportedInterfaces` (`research-spec.md` section 4).
- Principle 5: no Azure or database dependency in the core. Principle 4: source-generated JSON, no per-call reflection.

## Design

### D1. TCK in CI

- New project `tests/Ambinder.A2A.Tck.Sut` (not packable). It is a small ASP.NET Core app that uses only public SDK APIs. Its root card advertises all three interfaces, with streaming, push notifications and the extended card turned on, so the TCK tests every MUST that depends on a capability. Its agent supports the behaviors the TCK scenarios need (complete at once, ask for input, long-running and cancelable, emit artifacts). The builder reads the TCK `scenarios/` folder to choose the exact behaviors.
- Auth: the SUT turns anonymous access on explicitly (it is off by default per the work plan) for the public operations. The extended card needs auth by spec, so the SUT also accepts one static test bearer token, if the TCK can send one. *(writer's call)*
- gRPC: the SUT must work with today's port-per-agent routing (R21, `src/A2A.Server/Transports/Grpc/A2AGrpcService.cs:130-132`). One agent is enough. Package 09 changes routing later; the SUT must keep passing.
- New workflow job `tck` in `.github/workflows/ci.yml`:
  1. Build and start the SUT on fixed local ports.
  2. Check out `a2aproject/a2a-tck` at a commit SHA stored in `tck/TCK_VERSION`. *(writer's call: SHA, since there are no releases.)*
  3. For each transport: `./run_tck.py --sut-host http://localhost:<port> --transport <t> --level should`.
  4. A small script (`tck/check-report.py`, run with `python -I`) reads `reports/compatibility.json`. It fails on any MUST failure, and on any SHOULD xfail that is not listed in `tck/known-issues.json`.
  5. Upload the HTML and JUnit reports as artifacts.
- `tck/known-issues.json`: each entry has the TCK test id, transport, upstream issue URL, a one-line reason, and an `expires` date. The script fails when an entry has expired or no longer fails (so the list shrinks). *(writer's call)*
- Only SHOULD failures caused by a TCK bug may go on the list. Each entry is reviewed in its PR and needs an upstream issue link and an expiry date. A MUST failure may never go on the list. *(Claude's call, C3)*
- The `tck` job is advisory for the first two weeks after it lands, then it is a required check *(Claude's call, C13; package 12 owns the gate list)*.
- A weekly scheduled job runs the same steps against TCK `main` HEAD and opens an issue on new failures. It never blocks PRs. *(writer's call)*

### D2. Version negotiation

Server (all three transports, in one shared place, today `src/A2A.Server/Transports/ServiceParameters.cs:32-39`):

- Read the version from the header, else the query parameter (current behavior, `ServiceParameters.cs:17-23`); gRPC from metadata `a2a-version`.
- Parse `Major.Minor`. A value with a patch (`1.0.1`) is accepted as `1.0` *(writer's call; spec says patch is not used)*. Anything that does not parse is unsupported.
- Empty value means `0.3`.
- If the version is in the supported set, process it. The core's supported set is `{1.0}`.
- Else, ask the registered `IA2AVersionDispatcher` instances (seam below). If none claims it, throw `VersionNotSupported`.
- Error message for an empty header: "No A2A-Version header was sent, so the request is treated as A2A 0.3. This agent supports 1.0. Send the header `A2A-Version: 1.0`." For other versions: "A2A version 'X' is not supported. Supported: 1.0."
- `ErrorInfo.metadata`: `supportedVersions` = `"1.0"` (comma-separated string, since ErrorInfo metadata values are strings). Current code puts a JSON array in `data` (`ServiceParameters.cs:10-11`); D4 replaces it.
- The negotiated version goes on `A2ACallContext` as `ProtocolVersion` (already passed in, `JsonRpcEndpoint.cs:124`), so handlers and telemetry can read it. A request rejected here with `VersionNotSupported` never reaches package 09's pipeline, so it gets no `a2a.server` span. It appears only as the ASP.NET Core request span with its HTTP status, enriched with `error.type` when telemetry is on *(Claude's call)*. Authentication failures are treated the same way.

Seam for package 15 (in `Ambinder.A2A.AspNetCore`, because it takes `HttpContext`; package 01's rule 2 keeps ASP.NET Core out of `Ambinder.A2A.Server`):

```csharp
public interface IA2AVersionDispatcher
{
    /// <summary>True if this dispatcher serves requests that ask for <paramref name="version"/> ("0.3" for an empty header).</summary>
    bool CanHandle(string version);
    /// <summary>Handles the whole HTTP request (JSON-RPC or REST) for the agent.</summary>
    Task HandleAsync(HttpContext http, A2AAgentRoute agent, string version, CancellationToken ct);
}
```

`A2AAgentRoute` is the resolved agent (name, registration, card). Exact type name is set by package 02. The core registers no dispatcher. gRPC needs no dispatcher: a 0.3 gRPC service has its own service name and is mapped by package 15. After a dispatcher converts a request, it calls `IA2ARequestHandler`, so the request runs through package 09's middleware pipeline with `ProtocolVersion = "0.3"`.

API rule (package 02): `IA2AVersionDispatcher` and `IAgentCardContributor` are implementer-facing. After 1.0, a new member gets a default implementation.

Client:

- `A2AClientFactory.Create` (`src/A2A.Client/A2AClientFactory.cs:18-46`; package 10 renames it `A2AClient.Create` and uses the same selection for `IA2AClientFactory` and `ConnectAsync`) picks the first interface in card order whose binding is supported and whose `protocolVersion` Major.Minor is supported. With a preferred binding, it first looks for that binding with a supported version. Patch is ignored (Python does the same, `research-sdks.md` Python section).
- Supported versions come from registered transport factories (package 10 owns the factory seam). The core client supports `1.0`. Package 15 adds `0.3`.
- No match: `NotSupportedException` that lists each interface as `binding/version` and says "add Ambinder.A2A.Compat.V0_3" when a 0.3 interface was skipped.
- Every request sends `A2A-Version` with the selected interface's Major.Minor, not a fixed option (today `src/A2A.Client/A2AClientOptions.cs:20`). The option is removed.

### D3. Tolerant enums

- Replace `StrictEnumConverter<T>` (`src/A2A.Protocol/Json/StrictEnumConverter.cs:6-8`) with an internal source-generator-friendly `ProtoEnumConverter<T>`. One hand-written converter per enum is fine; no reflection per call.
- Read: a known name maps to its value. A known integer maps to its value (ProtoJSON parsers accept integers) *(writer's call)*. An unknown name or integer maps to `Unspecified` (value 0).
- Write: always the ProtoJSON name. Never an integer.
- Server: a required enum that reads as `Unspecified` is a validation error with a clear message, for example "message.role is missing or has an unknown value; use ROLE_USER" (`InvalidParams`). This is where 0.3 values like `"user"` end up when no compat package is installed.
- Client: an unknown `TaskState` from an agent reads as `Unspecified`. `IsTerminal` and `IsInterrupted` return false for it, so a waiting loop keeps polling instead of crashing. *(writer's call)*
- Telemetry: the converter cannot log. It increments a counter `a2a.protocol.unknown_enum_values` (tag: enum type) through a static `Meter` in `Ambinder.A2A.Protocol`. *(writer's call)*
- The existing test `Unknown_enum_string_throws_JsonException` (`tests/A2A.Protocol.Tests/Json/ModelJsonTests.cs`) is replaced by tests for the new rules.

### D4. Error model on all transports

- One internal builder turns an `A2AException` into an ErrorInfo detail: `@type` `type.googleapis.com/google.rpc.ErrorInfo`, `reason` from the catalog, `domain` `a2a-protocol.org`, `metadata` from `A2AException.Data` flattened to strings. REST (`RestErrorWriter.cs:28-60`) and gRPC (`GrpcErrorMapper.cs:18-30`) already do this.
- JSON-RPC: today `data` is the raw exception data (`src/A2A.Server/Transports/JsonRpc/JsonRpcModels.cs:78-90`). Change it so `error.data` is an array of detail objects containing the ErrorInfo, as v1 requires (`research-painpoints.md` pain point 6; spec 5.4). The builder checks the exact JSON-RPC shape against the spec's JSON-RPC error section and the TCK; if they differ, the TCK wins and the decision goes into `docs/spec-decisions.md`.
- Client, all transports: map by `ErrorInfo.reason` first (`A2AError.FromReason`, `A2AError.cs:45-46`), then by code. Keep `metadata` on the exception as `A2AException.Details` (read-only string dictionary). *(writer's call on the name; package 02 may rename.)*
- Unknown exceptions on the server become `InternalError` with a generic message and a correlation id (the trace id) in `metadata.traceId`. Today they are logged and become a generic message (`GrpcErrorMapper.cs:48-54`); add the trace id. *(writer's call)*
- Every error message says what to do (principle 3). The builder reviews all `A2AException` messages in `src/` for this.

### D5. Extensions: echo and required

- After the agent is resolved and before the operation runs, the server computes the active list (`RequestValidator.ResolveExtensions`, `src/A2A.Server/Tasks/RequestValidator.cs:41-49`). Today this happens only on the send path. Move it to one shared place so every operation on the agent applies it. *(writer's call: every operation except the public card fetch.)* The shared place is a built-in middleware in package 09's `IA2AMiddleware` pipeline (09 §7.11, step 4), so JSON-RPC, REST, gRPC and in-process calls run the same code. In M1 it works from the extensions declared on the card. In M2 package 11 extends the same middleware with `A2AExtension` request hooks; it does not add a second check. *(Reconciled: 11 had the required check on send operations only; this spec owns the protocol rule, so it applies to every operation except card fetch.)*
- Response: set `A2A-Extensions: <uri1>,<uri2>` (HTTP, including SSE before the first byte) and `a2a-extensions` response header metadata (gRPC) when the active list is not empty. The middleware writes it through `A2AOperationContext.ResponseMetadata` (09).
- Client: expose the activated list on the response. Package 10's shape: `A2AResponseInfo.ActivatedExtensions`, given through `A2ARequestOptions.OnResponse` for unary calls and on the stream wrapper for streams. This package needs the value to be readable in tests.

### D6. ListTasks rules

Tested end to end on all transports with the in-memory store. Current behavior is mostly right (`src/A2A.Server/Tasks/InMemoryTaskStore.cs:23-61`); these rules become explicit tests:

- `nextPageToken` is present on every page and `""` on the last page, in JSON on all transports (proto3 omits empty strings in binary, which is fine for gRPC).
- Order: `status.timestamp` descending, then task id descending as a stable tie-break. Tasks without a timestamp sort last.
- `includeArtifacts` unset or false: the `artifacts` field is absent from every task, not an empty array.
- `pageSize` 1..100, default 50 *(existing values, kept)*. Out of range: `InvalidParams`.
- Scoping (owner, and tenant once package 05 adds it) applies before any filter, including when `contextId` is not set.
- `historyLength` 0 omits history; unset means no limit.

### D7. Agent card HTTP caching

- On `GET /.well-known/agent-card.json` and the per-agent card route (`src/A2A.Server/Hosting/AgentCardEndpoints.cs:30-41`):
  - Serialize the card once per card version and keep the bytes.
  - `ETag`: strong, `"` + base64url of the first 16 bytes of SHA-256 over the unsigned canonical card payload (JCS, after contributors run, without `signatures`) plus the sorted signing key ids (`kid`) + `"`. So the ETag is the same on every pod and does not change when the card is re-signed with the same keys. *(Claude's call, B13; packages 04, 09 and 10 use this rule)*
  - `Cache-Control: public, max-age=300` by default. Option `A2AServerOptions.CardCacheMaxAge` (TimeSpan, default 5 minutes; `TimeSpan.Zero` sends `no-cache`). *(writer's call)*
  - `Vary: A2A-Version`, always, so a CDN never serves a 0.3-shaped card to a 1.0 client (pain point 3).
  - `If-None-Match` that matches: 304 with no body.
- The extended card (authenticated) sends `Cache-Control: private, no-store`. *(writer's call)*
- Seam for package 15 and others: `IAgentCardContributor` (in `Ambinder.A2A.Server`). Before the card bytes are cached, each registered contributor may add members to the card JSON. Contributors run in registration order. The core registers none. *(writer's call)* What a contributor may do:
  - add new top-level members (for example 0.3 legacy fields, package 15);
  - append entries to `supportedInterfaces` and to `capabilities.extensions` (packages 15, 08, 11);
  - add new keys to `securitySchemes` (package 07 and other-scheme contributors);
  - set `securityRequirements` when the card has none at that level (package 07).
  - It must not change or remove a member that is already set, and must not replace an existing `securitySchemes` key or a non-empty `securityRequirements` (a debug assert checks this). *(Reconciled at package 07's request: adding is allowed, changing is not.)*

```csharp
public interface IAgentCardContributor
{
    void Contribute(AgentCardContext card, JsonObject cardJson);   // AgentCardContext: agent name, card model, public or extended (09 adds a nullable Request)
}
```

### D8. Tenant

- Client: every request gets `tenant` set to the selected interface's tenant. Today a request tenant wins when set (`src/A2A.Client/Transports/ClientSupport.cs:45-62`). New rule: if the interface has a tenant and the request sets a different one, throw `ArgumentException` ("This client talks to tenant 'X' (from the agent card); create a client for the other interface instead"). If the interface has no tenant, the request value is sent as given.
- Server: the interface a request arrived on is known (route, REST tenant prefix `RestEndpoints.cs:178`, gRPC port). If that interface declares a tenant and the request `tenant` is set and different, return `InvalidParams` ("tenant 'Y' does not match this interface"). An empty request tenant is filled from the interface. *(writer's call on the error code; the spec text only says "must match".)*
- Which tenant the server trusts for isolation is package 07 and 05 work.

### D9. contextId and taskId rules (spec 3.4)

Each rule gets a test on all transports:

- The server generates every `taskId`. A client `taskId` that does not exist gives `TaskNotFound` (also when it exists for another owner: same error, no leak).
- Only `taskId` given: `contextId` is taken from the task (today `TaskManager.cs:267`).
- Both given and different: rejected with `InvalidParams` (today `TaskManager.cs:264-265`).
- A client `contextId` on a new task is kept, never replaced (today `TaskManager.cs:241`). It is rejected with `InvalidParams` if it is longer than 256 characters or contains control characters. *(writer's call; the spec lets the server decide what is "unacceptable".)*
- A new `contextId` is always returned in the response.
- Follow-up on a terminal task: `UnsupportedOperation` (today `TaskManager.cs:258-259`).
- Context ids are scoped per agent, tenant and owner, so one owner cannot join another owner's context by guessing its id. The store contract change is package 05.

### D10. Deprecated OAuth flows

- Add `ImplicitOAuthFlow` (`AuthorizationUrl`, `RefreshUrl`, `Scopes`) and `PasswordOAuthFlow` (`TokenUrl`, `RefreshUrl`, `Scopes`) to `OAuthFlows` (`src/A2A.Protocol/Models/Security.cs:57-62`), both `[Obsolete("Removed from OAuth 2.1; kept so cards round-trip.")]`. Fields per `proto/a2a.proto:568-574,609-627`.
- Map them in `ProtoMapper.Security.cs`. Add them to the source-generated JSON context.
- The client never uses them to get tokens. The server never generates them.
- Round-trip tests: JSON in, JSON out; proto in, proto out; JSON to proto to JSON.

### D11. Proto pin and drift check

- Replace `proto/a2a.proto` with the file at tag `v1.0.1`. The only difference from the current file is the `AgentInterface.url` comment (`research-spec.md` section 2). Update `proto/README.md` with the tag, its commit SHA and the SHA-256 of the file.
- New CI step `proto-drift`: download `https://raw.githubusercontent.com/a2aproject/A2A/v1.0.1/specification/a2a.proto` and fail if it differs from the vendored file, and fail if the SHA-256 in the README does not match. *(The download runs on every PR; it needs no secrets.)*
- The weekly job from D1 also checks the A2A releases API for a tag newer than `v1.0.1` and opens an issue. *(writer's call)*

### D12. Spec decisions doc

`docs/spec-decisions.md`: one row per open point, with spec section, the choice, and the reason. It starts with every *(writer's call)* above that touches wire behavior: patch in version header, integer enums, unknown enums to `Unspecified`, card caching values, the card ETag rule (hash of the unsigned canonical card payload plus the signing key ids, B13), tenant mismatch error code, contextId limits, JSON-RPC error data shape. Each later package adds its own rows. Package 13 links it from the docs site.

### D13. Task state transitions

This package owns `TaskStateRules` (internal, shared as linked source, B12) and this table. It follows A2A v1.0.1. `research-spec.md` does not list a transition table and contradicts none of the rows below; the rows come from the spec's task lifecycle and from how it defines interrupted and terminal states.

| From | Allowed to | Who may trigger it |
|---|---|---|
| (new task) | `SUBMITTED` | the server, when it creates the task |
| `SUBMITTED` | `WORKING`, `INPUT_REQUIRED`, `AUTH_REQUIRED`, `COMPLETED`, `FAILED`, `REJECTED`, `CANCELED` | the agent; `CANCELED` also by a cancel request; `FAILED` also by recovery |
| `WORKING` | `WORKING` (progress update), `INPUT_REQUIRED`, `AUTH_REQUIRED`, `COMPLETED`, `FAILED`, `REJECTED`, `CANCELED` | the agent; `CANCELED` also by a cancel request; `FAILED` also by recovery |
| `INPUT_REQUIRED` | `SUBMITTED`, `WORKING`, `CANCELED` | the server, when a continuation message arrives (it sets `SUBMITTED`, then the new run moves to `WORKING`); `CANCELED` by a cancel request |
| `AUTH_REQUIRED` | `SUBMITTED`, `WORKING`, `CANCELED` | the same as `INPUT_REQUIRED`, and only for a caller 07's `ITaskContinuationPolicy` allows |
| `COMPLETED`, `FAILED`, `REJECTED`, `CANCELED` | nothing | terminal states are final; a follow-up message gives `UnsupportedOperation` (D9), a cancel gives `TaskNotCancelable` (a cancel of an already `CANCELED` task returns the task) |

Rules:

- The agent can never set `UNSPECIFIED` or `SUBMITTED`. The server sets `SUBMITTED`.
- A move that is not in the table throws `A2AException(InvalidAgentResponse)` and the task ends `FAILED` (package 11 describes the agent side).
- Recovery (package 09) is the only path that moves a `WORKING` or `SUBMITTED` task to `FAILED` without an agent event, unless `Restart(1)` is chosen.
- An unknown `TaskState` from a remote agent reads as `Unspecified` (D3) and is never checked against this table.
- Tests: one case for every row, including every illegal pair, on the in-memory and the EF Core stores.

## How it meets the six principles

1. Protocol: the TCK runs on every PR on all three transports and fails on MUST and SHOULD. Check: the `tck` CI job is required for merge (after a two-week advisory period, C13).
2. .NET enabler: no new APIs beyond the dispatcher seam and error details; options use `IOptions<A2AServerOptions>`. Check: PublicAPI files (package 02) show only the listed additions.
3. Easy: version, tenant and extension errors say what to send instead. Check: a test asserts the message text of each new error.
4. Performance: enum converters are source-generated or hand-written, no reflection; card bytes and ETag are computed once. Check: package 14 benchmark for card GET and for task JSON read stays within budget.
5. Modular: 0.3 support plugs in through `IA2AVersionDispatcher` and the client transport factory, not code in the core. Check: the core has no reference to any 0.3 type or name.
6. Integration pains: covers version negotiation, error model, JSON drift (tolerant inbound), tenant and binding differences. Check: one test per pain point listed under "Findings addressed".

## Dependencies

Needs:
- 01: final assembly names and the rename, before this work starts.
- 02: names of `A2AAgentRoute` and `A2AException.Details`; PublicAPI tracking.
- 05: context and tenant scoping in the store; validation moved out of the store (keeps D6 tests green).
- 07: trusted tenant resolution (D8 server side).
- 10: the client transport factory seam with a supported-version list (D2 client).
- 09: the `IA2AMiddleware` pipeline, where the D5 extension check runs.

Provides:
- `IA2AVersionDispatcher` (`Ambinder.A2A.AspNetCore`) and `IAgentCardContributor` (`Ambinder.A2A.Server`) to package 15; `IAgentCardContributor` also to 07, 08, 09 and 11.
- The extension negotiation middleware (D5) to 09's pipeline, extended by 11.
- Version-aware interface selection in the client (`A2AClient.Create` and `IA2AClientFactory`, package 10) to packages 10 and 15.
- `tests/Ambinder.A2A.Tck.Sut` and the `tck` CI job, reused by package 16 as a .NET server under test.
- `docs/spec-decisions.md` to package 13 and all packages.
- `A2AException.Details` (ErrorInfo metadata) to package 10.

## Acceptance criteria

- [ ] `tck` CI job runs on every PR, all three transports, `--level should`; required for merge two weeks after it lands (C13); zero MUST failures and no MUST entry in `tck/known-issues.json`; every SHOULD failure listed there is a TCK bug with an upstream link and expiry (C3).
- [ ] `proto-drift` CI step passes; `proto/a2a.proto` equals tag `v1.0.1`; `proto/README.md` names the tag, SHA and file hash.
- [ ] Tests, each on JSON-RPC, REST and gRPC (use `tests/A2A.Integration.Tests/Fixtures/TransportMatrix.cs`):
  - [ ] missing version gives `VersionNotSupported` with the 0.3 message and `supportedVersions` metadata
  - [ ] `2.0` and `abc` give `VersionNotSupported`; `1.0.1` is accepted
  - [ ] a registered fake `IA2AVersionDispatcher` receives empty-version requests
  - [ ] unknown `TaskState` and `Role` strings read as `Unspecified`; integers read; output always names
  - [ ] every A2A error carries ErrorInfo; the client maps each catalog error back to the same `A2AErrorCode`
  - [ ] JSON-RPC `error.data` contains the ErrorInfo detail
  - [ ] activated extensions echoed; missing required extension gives `ExtensionSupportRequired` on GetTask, ListTasks and SendMessage
  - [ ] ListTasks: empty `nextPageToken` on last page, order, artifacts absent, scoping
  - [ ] each D9 rule
  - [ ] client tenant always equals the interface tenant; server rejects a mismatched tenant
- [ ] Card endpoint tests: `ETag` (same value from two server instances and after re-signing with the same keys, B13), `Cache-Control`, `Vary: A2A-Version`, 304 on `If-None-Match`, extended card `no-store`.
- [ ] OAuth implicit and password flows round-trip in JSON and proto.
- [ ] Client interface selection tests: version match, patch ignored, 0.3-only card gives the "add Compat" message.
- [ ] `docs/spec-decisions.md` exists with every wire-level writer's call from this spec.
- [ ] `README.md` "Optional conformance" section replaced by a short "Conformance" section that links the CI job.

## Left to the builder

- Exact SUT agent behaviors and ports, after reading the TCK scenarios.
- The report-check script's language, as long as it runs isolated (`python -I`) and needs no extra packages.
- Whether enum converters are generated or hand-written.
- Where the shared version, extension and tenant checks live inside `Ambinder.A2A.Server`, as long as all transports call the same code.
- Counter names, following package 14 / observability naming.
- Test file layout.

## Open questions

None. The SHOULD known-issues rule (Q1) is Claude's call C3, in D1.
