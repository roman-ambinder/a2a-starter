# Package 15: A2A 0.3 compatibility

Date: 2026-11-03
Status: approved by owner 2026-11-03
Milestone: M2 (ships in 1.0 GA)
Size: L

## Intent

Many agents that product teams will call or be called by still speak A2A 0.3 (Python a2a-sdk users, ADK, LangGraph, older .NET; R9). The 0.3 to 1.0 change touched almost every JSON shape, and the failures are often silent: a client that waits for `final: true` hangs, a string compare on `"completed"` never matches (pain point 1).

This package lets one .NET agent serve 0.3 and 1.0 clients from the same handler, and lets the .NET client call 0.3 agents with the same API it uses for 1.0. All 0.3 code lives in two separate packages, `Ambinder.A2A.Compat.V0_3` (models and client) and `Ambinder.A2A.Compat.V0_3.AspNetCore` (server), so the core stays clean, small and AOT-friendly. Teams that do not need 0.3 do not pay for it.

## Findings addressed

- R9: no 0.3 interop and no version negotiation (the negotiation part is package 03; the 0.3 part is here).
- Pain point 1, "0.3 -> 1.0 breaking changes and silent failures".
- Pain point 3, "Version negotiation and mixed-version peers" (mixed-version cards and clients).
- Pain point 2, "JSON serialization drift between SDKs" (0.3's own JSON-RPC vs REST inconsistency).

## Goals

1. A server with the compat package installed answers 0.3 JSON-RPC and 0.3 REST requests at the same agent URLs it uses for 1.0, using the same agent handler.
2. A 0.3 request is converted to 1.0 models, run by the core, and the result is converted back to 0.3 shapes: method names, `kind` discriminators, enum strings, `final` on stream events, file and data parts, errors.
3. The agent card served to 0.3 clients carries the 0.3 fields (`url`, `preferredTransport`, `additionalInterfaces`, `protocolVersion`) next to the 1.0 fields, and lists 0.3 interfaces in `supportedInterfaces` with `protocolVersion` `0.3`.
4. The .NET client calls a 0.3 agent with the same `IA2AClient` API. It reads 0.3-only cards, sends 0.3 requests, and turns 0.3 responses and streams into 1.0 models. A 0.3 stream ends when `final: true` arrives or the stream closes.
5. Push notifications to webhooks registered through a 0.3 API use the 0.3 body. A public helper converts a 0.3 webhook body into a 1.0 `StreamResponse` for receivers.
6. gRPC 0.3 is supported on server and client as an opt-in.
7. The core has no reference to any 0.3 type, method name or field name. A build check enforces it.
8. Interop passes against the Python, JS and Java SDKs in their 0.3 modes (package 16 harness).
9. Installing the compat package takes one line on the server and one on the client.

## Non-goals

- Versions before 0.3 (0.2.x). Not supported.
- Version negotiation and the interim `VersionNotSupported` error for a missing header. Package 03.
- Bridging 0.3 fields that have no 1.0 meaning, beyond carrying them. Some fields, like `security`, cannot be fully bridged (A2A discussion #1416); we map what maps and document the rest.
- Making 0.3 TCK runs a merge gate, if no maintained 0.3 TCK exists (see Design, testing).
- A public set of 0.3 model types or converters. Only the webhook helper is public (lesson from a2a-js PR #726: public converters tripled bundle size and became a contract).
- Card signing rules for 0.3. Package 04 signs whatever card JSON is served.

## Anti-goals

- Any `if (version == "0.3")` branch in the core.
- A 0.3 client that hangs because a server never sends `final: true`.
- Lossy conversion that silently drops data a 1.0 handler needs (for example file names or metadata).
- A dual card that breaks strict 1.0 parsers (for example by changing the type of a 1.0 field).
- Separate agent handlers or separate task stores for 0.3 callers. One task is one task, whatever version created it.

## Constraints

- Package IDs and namespaces: `Ambinder.A2A.Compat.V0_3` (work plan, "A2A 0.3") and `Ambinder.A2A.Compat.V0_3.AspNetCore` (C14). Both ship in 1.0 GA, milestone M2.
- Until this package is installed, a missing `A2A-Version` is treated as 0.3 and answered with `VersionNotSupported` (work plan; package 03, D2).
- Spec 3.6: an empty `A2A-Version` header MUST be treated as 0.3. A server that advertises a 0.3 interface must serve 0.3 semantics there. An agent may expose several interfaces with different versions (`research-spec.md` section 5).
- 0.3 to 1.0 renames (`research-spec.md` section 5, from `whats-new-v1.md` and spec Appendix A):
  - Methods: `message/send` → `SendMessage`, `message/stream` → `SendStreamingMessage`, `tasks/get` → `GetTask`, `tasks/resubscribe` → `SubscribeToTask`, `agent/getAuthenticatedExtendedCard` → `GetExtendedAgentCard`, `tasks/pushNotificationConfig/set` → `CreateTaskPushNotificationConfig`.
  - Enums: lowercase / kebab (`input-required`) → `TASK_STATE_*`; `user`/`agent` → `ROLE_*`.
  - Parts: 0.3 parts have a `kind` discriminator; 1.0 `Part` is one message with a oneof `text/raw/url/data`. Stream events use the `StreamResponse` wrapper, not `kind`.
  - Card: `url`/`preferredTransport`/`additionalInterfaces` → `supportedInterfaces[]` with `protocolVersion` per interface.
  - IDs are plain in 1.0 (no `tasks/{id}` compound names). REST drops the `/v1` prefix. Errors moved to `google.rpc.Status`/`ErrorInfo` (0.3 REST used RFC 9457 problem details, pain point 1). Pagination changed.
  - Stream `final` flag was removed in 1.0 (pain point 1).
- Principle 4: source-generated JSON, AOT-safe; principle 5: small packages, no hidden dependencies.
- 0.3 spec source of truth: the A2A repo at tag `v0.3.0` (2025-07-30): its spec text, JSON schema and proto. The builder vendors those files into the package's source tree, like `proto/` today, with a README naming the tag and SHA.

## Design

### How it plugs into the core

The core offers three seams. This package uses them and nothing else:

| Seam | Owner | What compat does with it |
|---|---|---|
| `IA2AVersionDispatcher` (server, `Ambinder.A2A.AspNetCore`) | 03 | Claims versions `0.3` (empty header) and `0.3` sent explicitly. Handles the whole JSON-RPC or REST request. |
| `IAgentCardContributor` (server, `Ambinder.A2A.Server`) | 03 | Adds 0.3 top-level fields and appends 0.3 interfaces to `supportedInterfaces` in the served card JSON. |
| `IA2ATransportFactory` (registered with `IA2AClientBuilder.AddTransport<T>()`) and `IAgentCardReader` (client) | 10 | The factory reports `ProtocolVersions = ["0.3"]` and creates a 0.3 `IA2ATransport`; the reader's `TryRead` converts 0.3-only cards to 1.0 `AgentCard` models. |

Push uses two more seams that are not core request seams: package 08's `IPushPayloadFormatter` (sender) and `IPushBodyConverter` (receiver), and package 05's `StoredPushConfig.ProtocolVersion` (see "Server: push notifications").

gRPC 0.3 needs no core seam: the 0.3 proto has its own service name, so compat maps its own gRPC service next to the core one. The builder confirms the 0.3 proto package name does not collide with `lf.a2a.v1`; if it does, compat generates its C# types in a different namespace and maps the 0.3 service name explicitly.

The dispatcher runs after the core has resolved the agent and run ASP.NET authentication and authorization, so 0.3 callers get exactly the same auth and owner rules as 1.0 callers (packages 07, 05). The core passes the resolved agent; compat calls the same `IA2ARequestHandler` (today `src/A2A.Server/IA2ARequestHandler.cs`; after package 09 it is the middleware pipeline's `A2APipelineRequestHandler`) with 1.0 request models and an `A2ACallContext` whose `ProtocolVersion` is `0.3`. So 0.3 calls run through every `IA2AMiddleware` too.

### Package contents

Two packages *(Claude's call, C14)*, so client-only apps do not pull in ASP.NET Core:

| Package | Contents | Depends on |
|---|---|---|
| `Ambinder.A2A.Compat.V0_3` | 0.3 models, `V0_3JsonContext`, converters, the 0.3 client transport and card reader, `AddV0_3Support`, `LegacyPushNotification` | `Ambinder.A2A.Client`; the opt-in gRPC 0.3 client adds `Grpc.Net.Client` |
| `Ambinder.A2A.Compat.V0_3.AspNetCore` | the 0.3 `IA2AVersionDispatcher`, the card contributor, the 0.3 SSE writer, the 0.3 push payload formatter, `AddA2AV0_3Compat`, `MapA2AGrpcV0_3`, `V0_3ServerOptions` | `Ambinder.A2A.Compat.V0_3`, `Ambinder.A2A.AspNetCore` (which brings `Ambinder.A2A.Server`; the dispatcher seam lives there); the opt-in gRPC 0.3 server adds `Grpc.AspNetCore` |

Package 01's map records both rows.

Public API (small on purpose):

```csharp
// Ambinder.A2A.Compat.V0_3.AspNetCore
public static class V0_3ServerExtensions
{
    public static A2AServerBuilder AddA2AV0_3Compat(this A2AServerBuilder builder, Action<V0_3ServerOptions>? configure = null);
    public static IEndpointRouteBuilder MapA2AGrpcV0_3(this IEndpointRouteBuilder endpoints);   // opt-in
}

// Ambinder.A2A.Compat.V0_3
public static class V0_3ClientExtensions
{
    public static IA2AClientBuilder AddV0_3Support(this IA2AClientBuilder builder);
}

// Ambinder.A2A.Compat.V0_3.AspNetCore
public sealed class V0_3ServerOptions
{
    public bool JsonRpc { get; set; } = true;
    public bool Rest { get; set; } = true;
    public bool LegacyCardFields { get; set; } = true;     // add url / preferredTransport / ...
    public string AdvertisedVersion { get; set; } = "0.3.0";
}

// Ambinder.A2A.Compat.V0_3
public static class LegacyPushNotification
{
    /// <summary>Converts a 0.3 webhook body (a Task JSON object) to a 1.0 StreamResponse.</summary>
    public static StreamResponse ToStreamResponse(ReadOnlySpan<byte> body);
    public static bool IsLegacyBody(ReadOnlySpan<byte> body);
}
```

Builder type names: `A2AServerBuilder` (the server builder class, packages 02 and 07) and `IA2AClientBuilder` (package 10). *(Reconciled: this spec said `IA2AServerBuilder`, which no other spec defines.)* Everything else is internal: 0.3 models, a source-generated `V0_3JsonContext`, converters, the 0.3 SSE writer and reader.

### Server: 0.3 JSON-RPC

- Same URL as the 1.0 JSON-RPC interface. The core's version gate sees an empty or `0.3` version and hands the request to the dispatcher. 1.0 requests never reach compat.
- Method table:

| 0.3 method | Core call |
|---|---|
| `message/send` | SendMessage |
| `message/stream` | SendStreamingMessage |
| `tasks/get` | GetTask |
| `tasks/cancel` | CancelTask |
| `tasks/resubscribe` | SubscribeToTask |
| `tasks/pushNotificationConfig/set` | CreateTaskPushNotificationConfig |
| `tasks/pushNotificationConfig/get` | GetTaskPushNotificationConfig |
| `tasks/pushNotificationConfig/list` | ListTaskPushNotificationConfigs |
| `tasks/pushNotificationConfig/delete` | DeleteTaskPushNotificationConfig |
| `agent/getAuthenticatedExtendedCard` | GetExtendedAgentCard |

  The builder checks the full method list against the v0.3.0 spec. A 0.3 method with no core counterpart gets `MethodNotFound`. A 1.0 method name sent without a version header gets `VersionNotSupported` from compat with a message that says "send `A2A-Version: 1.0`" *(writer's call: this catches 1.0 clients that forgot the header, a common bug.)*
- Request conversion (0.3 → 1.0):
  - `MessageSendParams` → `SendMessageRequest`. `configuration.blocking` → `returnImmediately = !blocking` *(0.3 default blocking behavior is checked against the v0.3.0 spec)*. `historyLength`, `acceptedOutputModes`, `pushNotificationConfig` map directly.
  - Message: `kind` dropped, `role` `user`/`agent` → `Role`, `messageId`, `contextId`, `taskId`, `referenceTaskIds`, `extensions`, `metadata` copied.
  - Parts: `{"kind":"text","text"}` → `Part.Text`; `{"kind":"file","file":{"bytes"|"uri","mimeType","name"}}` → `Part.Raw` or `Part.Url` with `MediaType` and `Filename`; `{"kind":"data","data":{...}}` → `Part.Data`. Part `metadata` copied.
  - Tolerant read for the known 0.3 inconsistency (JSON-RPC followed the JSON schema, REST followed ProtoJSON; a2a-js #330): accept both forms for parts and file content. *(writer's call)*
- Response conversion (1.0 → 0.3):
  - `Task` → 0.3 Task with `kind: "task"`, `status.state` as 0.3 string (`TASK_STATE_INPUT_REQUIRED` → `input-required`, and so on; `Unspecified` → `unknown`), `history` and `artifacts` with 0.3 parts.
  - `Message` → `kind: "message"`.
  - Timestamps written as ISO 8601 UTC, as in 1.0 (valid 0.3 too).
- Errors: JSON-RPC error with the same numeric code as 1.0 (the 0.3 code table uses the same numbers for the shared errors; the builder confirms). `data` is the message details object, not the 1.0 ErrorInfo array *(builder confirms against 0.3 schema)*. 1.0-only errors (`ExtendedAgentCardNotConfigured`, `ExtensionSupportRequired`, `VersionNotSupported`) map to the nearest 0.3 code; the table goes in `docs/spec-decisions.md`.

### Server: 0.3 streaming

- SSE with JSON-RPC envelopes per event, as in 0.3. Events: Task (`kind: "task"`), Message, `TaskStatusUpdateEvent` (`kind: "status-update"`, `final`), `TaskArtifactUpdateEvent` (`kind: "artifact-update"`, `append`, `lastChunk`).
- `final: true` is set on the status update that moves the task into a terminal or interrupted state. If the core stream closes and the last event written was not such an update, compat writes one more status update with the current status and `final: true`, then closes. *(writer's call; this is the "no hang" rule.)*
- Keep-alive comments and other SSE behavior come from the core SSE writer (packages 09, 10).

### Server: 0.3 REST

- 0.3 REST paths use the `/v1` prefix (`research-spec.md` section 5), so they do not collide with 1.0 paths. Compat maps them under each agent's REST base: for example `POST {base}/v1/message:send`, `GET {base}/v1/tasks/{id}`, `POST {base}/v1/tasks/{id}:cancel`. The full list comes from the v0.3.0 spec.
- Requests without a version header on 1.0 REST paths still go through the dispatcher (they are 0.3 by rule), which answers with a clear error pointing to the `/v1` path or to `A2A-Version: 1.0`.
- Resource names (`tasks/{id}`, `tasks/{id}/pushNotificationConfigs/{configId}`) are parsed to plain ids.
- Errors: RFC 9457 problem details (`application/problem+json`) with `type`, `title`, `status`, `detail`, and the A2A code in an extension member. *(builder checks the exact 0.3 REST error form.)*

### Server: 0.3 gRPC (opt-in)

- `MapA2AGrpcV0_3()` maps the 0.3 gRPC service from the vendored v0.3.0 proto on the same ports as the core gRPC service. Conversion goes through the same internal converters (proto ↔ 0.3 model ↔ 1.0 model). *(writer's call: opt-in because it adds a second generated proto set; most 0.3 traffic is JSON-RPC.)*

### Server: the agent card

Through `IAgentCardContributor`:
- For each 1.0 interface the agent has on JSON-RPC and REST (and gRPC, when mapped), add a matching interface with `protocolVersion: "0.3"` and the same URL (REST: the base URL, since 0.3 clients add `/v1`). 1.0 interfaces stay first, so 1.0 clients pick them (spec: clients pick the first supported).
- When `LegacyCardFields` is true, also write the 0.3 top-level fields: `url` (the 0.3 JSON-RPC URL, or the first 0.3 interface), `preferredTransport` (`JSONRPC`), `additionalInterfaces` (the other 0.3 interfaces as `{url, transport}`), `protocolVersion` (`AdvertisedVersion`), and `security` from the 1.0 `securityRequirements` where they map. 1.0 parsers ignore unknown fields, so one dual card serves both. *(writer's call: one card, not content negotiation by header, because many 0.3 clients send no header and many 1.0 clients forget it on card fetch.)*
- Fields that cannot be bridged are listed in the docs (A2A discussion #1416).
- Card signing (package 04) signs the final served JSON, including legacy fields, so both 1.0 and 0.3 verifiers see the same bytes.
- `Vary: A2A-Version` stays on (package 03) even though the card is the same, so later versions can vary safely.

### Server: push notifications

- A push config created through a 0.3 method is stored with `StoredPushConfig.ProtocolVersion = "0.3"` (package 05's opaque field). Compat does nothing special for this: the core copies `A2ACallContext.ProtocolVersion` into the field when it creates the config (05 §7.7, 08). *(writer's call)*
- The push sender (package 08) picks an `IPushPayloadFormatter` by the config's `ProtocolVersion`. Compat registers one whose `CanFormat("0.3")` is true and whose `Format(...)` writes the 0.3 Task JSON. The 1.0 default writes `StreamResponse`.
- Receivers: `LegacyPushNotification` is the converter behind package 08's receiver hook `IPushBodyConverter`. The docs show the few-line adapter, so compat takes no dependency on `Ambinder.A2A.Push.Receiver` *(writer's call)*.

### Client

- `AddV0_3Support()` registers:
  - an `IAgentCardReader` (package 10): when the card JSON has no `supportedInterfaces` but has `url` (and optionally `protocolVersion` 0.3.x), it builds a 1.0 `AgentCard` with interfaces from `url`/`preferredTransport`/`additionalInterfaces`, each `protocolVersion: "0.3"`. Other fields map by name; `security` maps to `securityRequirements` where possible. Without compat, the core gives an error: "This agent card looks like A2A 0.3 (it has `url` but no `supportedInterfaces`). Add the Ambinder.A2A.Compat.V0_3 package and call AddV0_3Support()." (package 10 owns that check).
  - an `IA2ATransportFactory` (package 10) with `ProtocolVersions = ["0.3"]` on JSON-RPC, REST and gRPC, registered through `IA2AClientBuilder.AddTransport<T>()`. Its `IA2ATransport` is the same contract as the 1.0 transports, so tracing, auth, retries and interceptors from package 10 apply unchanged.
- Interface choice: 1.0 interfaces win when both exist (package 03, D2, first supported in card order; a dual card lists 1.0 first). *(writer's call: if a card lists 0.3 first and the client supports 1.0, the client still follows card order, as the spec says.)*
- Requests: 1.0 models → 0.3 JSON. No `A2A-Version` header is sent to 0.3 agents *(writer's call: some 0.3 servers may reject unknown headers; the spec says empty means 0.3)*. Option `SendVersionHeaderToV0_3 = false`.
- Responses and stream events: 0.3 → 1.0 models. A stream ends at `final: true` or when the server closes it, whichever is first. 0.3 `unknown` state → `TaskState.Unspecified`.
- 0.3 errors (JSON-RPC codes, REST problem details, gRPC status) → the same `A2AException` codes as 1.0.
- Features 0.3 lacks (for example `ListTasks`, if not in v0.3.0): the client throws `A2AException(UnsupportedOperation)` with "This agent speaks A2A 0.3, which has no ListTasks."

### Telemetry and logs

- Spans and metrics carry `a2a.protocol.version` = `0.3` or `1.0` (names per package 14 / observability spec).
- One info log per agent and process the first time a 0.3 request is served or sent, so teams can see who still uses 0.3.

### Testing

- Converter unit tests: one per 0.3 type and enum value, both directions, from fixtures in `tests/vectors/v0_3/` (taken from the v0.3.0 spec examples and from real Python/JS/Java 0.3 output captured by package 16).
- Server tests on JSON-RPC, REST and gRPC: every 0.3 method; stream ends with `final: true` in each terminal and interrupted state, including the synthetic final event; dual card shape; 1.0 requests at the same URLs are untouched.
- Client tests against an in-process 0.3 server built from the compat server side, plus interop.
- Interop (package 16 harness, M2): our server with compat against the Python SDK client with `enable_v0_3_compat=True`, the JS client with `legacyCompat`, the Java 0.3.4.Final client; our client against the same SDKs' 0.3 servers. JSON-RPC and REST required; gRPC where the other SDK supports 0.3 gRPC.
- 0.3 TCK: if `a2aproject/a2a-tck` has a revision that targets 0.3, run it in the nightly job against the compat server; not a merge gate. *(writer's call)*
- Core purity check: a test scans the core assemblies (Protocol, Server, AspNetCore, Client, AspNetCore.Grpc, Client.Grpc) for 0.3-only names (`message/send`, `tasks/resubscribe`, `status-update`, `additionalInterfaces`, `preferredTransport`) and fails if found, plus a project-reference check that no core project references compat. *(writer's call)*

## How it meets the six principles

1. Protocol: serves 0.3 semantics on 0.3 interfaces and 1.0 on 1.0 interfaces, per spec 3.6. Check: interop matrix in 0.3 mode passes; core TCK still passes with compat installed.
2. .NET enabler: one `Add…()` per side, options pattern, no new abstractions for users. Check: PublicAPI file lists only the types in "Public API" above.
3. Easy: one line on the server, one on the client; clear errors when compat is missing. Check: the getting-started sample for 0.3 is under 5 extra lines; tests assert the "add compat" messages.
4. Performance: source-generated JSON for 0.3 models; no cost for 1.0 requests (the dispatcher is reached only for non-1.0 versions). Check: package 14 benchmark shows no change on 1.0 paths with compat installed; a 0.3 SendMessage benchmark within 15% of 1.0. *(writer's call on the 15%)*
5. Modular: all 0.3 code in two packages (client side and server side) behind three core seams. Check: core purity test.
6. Integration pains: closes "silent failures" (typed enums, `final` handling, dual card). Check: a test per pain-point symptom (hang on missing `final`, `"completed"` compare, `kind` parts, `/v1` paths).

## Dependencies

Needs:
- 02: builder type names; PublicAPI tracking.
- 03: `IA2AVersionDispatcher`, `IAgentCardContributor`, version-aware interface selection, `docs/spec-decisions.md`.
- 04: signing of the final served card JSON (no special work, but tests must cover the dual card).
- 05 and 08: `StoredPushConfig.ProtocolVersion` (opaque, filled by the core from `A2ACallContext.ProtocolVersion`); `IPushPayloadFormatter` in the push sender; `IPushBodyConverter` in the receiver.
- 09: the middleware pipeline that 0.3 calls run through.
- 10: `IA2ATransportFactory`, `IA2ATransport`, `IAgentCardReader`, `IA2AClientBuilder.AddTransport<T>()`; the "looks like 0.3" error.
- 16: harness and SDK containers in 0.3 mode.

Provides:
- `Ambinder.A2A.Compat.V0_3` with `AddV0_3Support` and `LegacyPushNotification`.
- `Ambinder.A2A.Compat.V0_3.AspNetCore` with `AddA2AV0_3Compat`, `MapA2AGrpcV0_3` and `V0_3ServerOptions`.
- 0.3 fixtures in `tests/vectors/v0_3/` (to 16 and 12).
- A docs section "Talking to A2A 0.3 agents" and "Serving A2A 0.3 clients" (to 13).

## Acceptance criteria

- [ ] Packages `Ambinder.A2A.Compat.V0_3` and `Ambinder.A2A.Compat.V0_3.AspNetCore` build, pack, are AOT-compatible on JSON-RPC and REST (no trim or AOT warnings), and have PublicAPI files with only the listed types.
- [ ] A client-only app that references `Ambinder.A2A.Compat.V0_3` has no `Microsoft.AspNetCore.*` assembly in its publish output (C14).
- [ ] Vendored v0.3.0 spec files with a README naming tag and SHA.
- [ ] Server: every 0.3 JSON-RPC method and REST path works, with tests; gRPC 0.3 works when mapped.
- [ ] Every 0.3 stream ends with a `final: true` status update; test per terminal and interrupted state.
- [ ] Dual card: 1.0 interfaces first, 0.3 interfaces added, legacy fields present; a strict 1.0 parse (our serializer) and a 0.3 parse (fixture-based) both succeed.
- [ ] 0.3 push configs get 0.3 bodies; `LegacyPushNotification.ToStreamResponse` converts captured Python/JS 0.3 bodies.
- [ ] Client: calls a 0.3 agent on JSON-RPC and REST (and gRPC) with the normal `IA2AClient` API; reads a 0.3-only card.
- [ ] Interop matrix in 0.3 mode passes for Python, JS and Java, or each gap is listed with the upstream issue.
- [ ] Core purity test passes.
- [ ] Core TCK job (package 03) still passes with compat installed in the SUT.
- [ ] Docs: install, what maps and what does not (including `security`), the error code table, how to find 0.3 callers in telemetry.

## Left to the builder

- Internal structure of converters and the 0.3 model types.
- Exact REST path list and problem-details field names, from the v0.3.0 spec.
- Whether gRPC 0.3 types are generated in this package or a private sub-assembly.
- Log wording and event ids.
- Which captured fixtures to keep.

## Open questions

None. The package split (Q1) is Claude's call C14 ("Package contents").
