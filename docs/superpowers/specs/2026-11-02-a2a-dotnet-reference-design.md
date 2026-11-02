# A2A .NET 10 Reference Implementation — Design

Date: 2026-11-02
Status: Draft, awaiting approval

## 1. Intent

Build a reference repository that the team copies from when they build A2A
(Agent2Agent protocol, v1.0) agents and clients in .NET 10.

The team will build **both** sides: their own agents (servers) and clients
that call other agents. The repo must show every flow the A2A v1.0
specification defines, working end to end, on every transport.

It must run on a laptop with **no external dependencies** at first (no API
keys, no cloud, no Docker required, no identity provider), and have a clear,
config-only path to run in a **production environment** (real Entra ID, real
LLM providers, durable storage, TLS, telemetry).

## 1a. Phasing (user decision, 2026-11-02)

User: "for now focus on the mocked external services."

- **Phase 1 (this build):** everything runs local with mocks. Fake agents,
  mock Entra, in-memory store, all transports, all flows, tests, `dotnet run`
  and Docker Compose. Code keeps clean seams (`ITaskStore`, `ITokenProvider`,
  `IChatClient`, auth config) so Phase 2 plugs in without reshaping Phase 1.
- **Phase 2 (later, separate plan):** real Entra wiring and verification, the
  four real LLM providers, durable task store, TLS, OpenTelemetry export,
  production push-signing keys, multi-replica support. Section 9.3 and the
  LLM parts of 7.5 describe Phase 2 intent; Phase 1 builds only the seams.
- Phase 1 runs as a **single instance**. Event bus, push queue, and running
  agent work live in process memory. Documented as a known limit.

## 2. Goals

1. Implement every A2A v1.0 operation, on all three transports, server and client.
2. Demonstrate every task state, every part type, streaming, push
   notifications, multi-turn, cancel, resubscribe, list/paging, the extended
   agent card, and the protocol error cases.
3. Auth modeled on Microsoft Entra ID, mocked locally, real Entra via config.
4. Demo agents run in deterministic "fake" mode by default; LLM mode via config
   for Azure OpenAI, OpenAI, Anthropic, and Ollama.
5. Zero-dependency local run via `dotnet run`. Docker Compose as a second option.
6. Production mode: same code, different config.
7. Code is clean enough to copy: clear project boundaries, small files,
   comments where the protocol is non-obvious, README per project.
8. Automated tests prove every flow on every transport.

## 3. Non-goals

- Supporting A2A v0.3 clients/servers (no backward-compat layer).
- A UI. The client is a console app.
- Publishing our protocol library as a NuGet package.
- Real deployment scripts for a specific cloud (Kubernetes manifests, Bicep, etc.).
- Agent orchestration frameworks, planners, or multi-agent workflows beyond
  what is needed to show the protocol.

## 4. Anti-goals (failure even if it technically works)

- **Any prerelease NuGet package.** Team rule: stable releases only. This rules
  out the official `A2A` SDK (all versions are prerelease as of 2026-11-02,
  latest `1.0.0-preview3`). We implement the protocol ourselves.
- First run needs a key, an account, a network service, or Docker.
- Wire format drifts from the spec (field names, enum values, error codes,
  REST paths). The normative source is the spec and its `a2a.proto`.
- The mock identity provider can be enabled in Production.
- Demo logic tangled into protocol code, so the team can't copy one without the other.
- Flows "shown" only in prose, not runnable and not tested.

## 5. Constraints

- .NET 10 SDK (installed locally: 10.0.401). Target `net10.0`.
- Stable NuGet packages only. Known stable candidates:
  ASP.NET Core (in-box), System.Text.Json (in-box), Grpc.AspNetCore, Grpc.Net.Client,
  Google.Protobuf, Grpc.Tools, Microsoft.AspNetCore.Authentication.JwtBearer,
  Microsoft.IdentityModel.*, Azure.Identity, Microsoft.Extensions.AI,
  Azure.AI.OpenAI, OpenAI, Anthropic (official, 13.3.0), OllamaSharp,
  OpenTelemetry.*, xUnit, Microsoft.AspNetCore.Mvc.Testing.
  Builder verifies each is stable at build time and pins exact versions
  (central package management, `Directory.Packages.props`).
- "No external dependencies" means no runtime services or credentials.
  A one-time `dotnet restore` from nuget.org is acceptable.
- Protocol version: A2A **v1.0**. Clients send the `A2A-Version` header;
  server rejects unsupported versions with the spec's error.

## 6. Decisions made with the user

| Topic | Decision |
|---|---|
| Audience | Team reference, used for both agent and client work |
| SDK | No official SDK (prerelease). Hand-built protocol on stable packages |
| Transports | JSON-RPC 2.0, HTTP+JSON (REST) with SSE, gRPC — all three |
| Agents | Fake deterministic mode default; LLM mode via config |
| LLM providers | Azure OpenAI, OpenAI, Anthropic, Ollama — via `IChatClient` |
| Auth | Entra ID model. Local mock Entra; real Entra by config |
| Running | Plain `dotnet run`; Docker Compose as an option |
| Environments | Zero-dependency local first; production option via config |

## 7. Architecture

### 7.1 Solution layout (proposed; builder may adjust names)

```
A2A.Reference.sln
Directory.Build.props / Directory.Packages.props
proto/a2a.proto                       # vendored from the spec, pinned commit/version noted
src/
  A2A.Protocol/        # v1.0 models, JSON options, enums, error codes, constants. No ASP.NET dependency.
  A2A.Protocol.Grpc/   # Generated gRPC types from a2a.proto + mappers to/from A2A.Protocol models.
  A2A.Server/          # ASP.NET Core library: transports, task manager, task store, push sender, auth glue.
  A2A.Client/          # Client library: card resolver, transport clients, SSE reader, token providers.
samples/
  AgentServer/         # Host app: registers demo agents + all transports.
  AgentClient/         # Console app: one command per flow, --transport switch.
  WebhookReceiver/     # Small ASP.NET app receiving push notifications, verifying them.
  MockEntra/           # Local Entra-shaped token service (Development only).
tests/
  A2A.Protocol.Tests/  # JSON contract tests (shape, enums, round-trip vs spec examples).
  A2A.Integration.Tests/ # Every flow × every transport, in-process.
docs/
  flows.md             # Flow map: flow → agent → client command → test name.
docker-compose.yml, .env.example, run-all.sh, run-all.ps1
```

Rule: `src/` contains only protocol and plumbing. `samples/` contains demo
logic. The team must be able to copy `src/` alone.

### 7.2 Protocol library (`A2A.Protocol`)

- C# records for every v1.0 type: AgentCard (+ skills, capabilities, interfaces,
  security schemes, extensions), Message, Part (text / file / data), Task,
  TaskStatus, Artifact, status/artifact update events, push notification
  config, request/response types for each operation, ListTasks paging.
- Enums serialized exactly as the spec defines (e.g. `TASK_STATE_WORKING`,
  `ROLE_USER`). v1.0 removed the `kind` discriminator; follow v1.0 rules for
  part/result discrimination.
- Single shared `JsonSerializerOptions` (source-generated context preferred).
- Error type with spec error codes (JSON-RPC codes, A2A-specific codes, and
  their REST/gRPC status mappings), defined once, used by all transports.

### 7.3 Server library (`A2A.Server`)

- **Agent abstraction**: an interface the team implements (e.g. `IAgentHandler`)
  that receives a request context (message, task, cancellation, user identity)
  and writes events through an event sink (status update, artifact chunk,
  message, final state). Transports never see agent code.
- **Task manager**: owns task lifecycle and state transitions, enforces legal
  transitions, terminal states, cancel rules, history length, `returnImmediately`.
- **Event bus per task**: fan-out to streaming subscribers, resubscribe
  (SubscribeToTask) and push sender.
- **Task store**: `ITaskStore` interface. Implementations:
  - In-memory (default, local).
  - Durable store for production (see 9.3).
- **Transports** (each a thin adapter over the task manager):
  - JSON-RPC 2.0 endpoint (single POST route), incl. SSE for streaming methods.
  - HTTP+JSON REST routes per spec section 11, incl. SSE.
  - gRPC service from `a2a.proto`, incl. server streaming.
- **Agent card endpoints**: public card at `/.well-known/agent-card.json`;
  extended card via GetExtendedAgentCard (authenticated).
- **Push notification sender**: background queue, retries with backoff,
  signs each webhook call (see 8.4), URL validation against SSRF.
- Registration via `builder.Services.AddA2AServer(...)` + `app.MapA2A(...)`.
- `tenant` field: accepted and passed into the request context; not used for
  isolation by the demo store beyond keying (builder's call how far to go).

### 7.4 Client library (`A2A.Client`)

- Card resolver (fetch + cache public card; fetch extended card with auth).
- `IA2AClient` with one method per operation; three implementations
  (JSON-RPC, REST, gRPC); a factory picks one from the card's supported
  interfaces or an explicit preference.
- SSE reader that yields typed events; reconnect via SubscribeToTask.
- `ITokenProvider` with two implementations: mock Entra (client-credentials
  HTTP call) and real Entra (Azure.Identity `TokenCredential`). Also API key.
- `HttpClient` via `IHttpClientFactory`, auth as a delegating handler.

### 7.5 Demo agents (in `samples/AgentServer`)

Each agent has its own card and path (e.g. `/agents/{name}/...`). The root
`/.well-known/agent-card.json` serves a default agent (my call).

| Agent | Shows |
|---|---|
| **Echo** | SendMessage returning a direct Message (no task). Echoes text, file, and data parts back. |
| **Report** (long-running) | Task creation, WORKING status updates, streamed artifact chunks (append / last chunk), multiple artifacts (text, file, data), cancel, resubscribe, push notifications, `returnImmediately`, ListTasks. |
| **Booking** (multi-turn) | INPUT_REQUIRED → client answers on same task/context → COMPLETED. REJECTED for requests it refuses (e.g. unsupported city). FAILED on a trigger word. |
| **Secure** | Requires an app role; shows 401/403. AUTH_REQUIRED mid-task until the client sends a token with a higher scope, then continues. Extended card lists extra skills only visible to authenticated callers. |

Each agent has a fake implementation (deterministic, fixed delays configurable
so tests run fast) and an LLM implementation using `IChatClient`. In LLM mode
the protocol behavior (states, artifacts, multi-turn) stays the same; only the
generated text changes.

One small **extension** demo: card declares an extension URI; client opts in
via the spec's extension header; agent adds extension metadata to its reply.

### 7.6 Client console (`samples/AgentClient`)

- One command per flow, e.g. `discover`, `send`, `stream`, `get`, `list`,
  `cancel`, `resubscribe`, `multi-turn`, `push`, `auth`, `extended-card`,
  `errors`, `all`.
- `--transport jsonrpc|rest|grpc|auto`, `--agent <name>`, `--auth mock|entra|apikey|none`.
- Prints each wire request/response (toggle `--verbose`) so the team can read the protocol.

### 7.7 Webhook receiver (`samples/WebhookReceiver`)

Receives push notifications, verifies signature/token, logs events, exposes a
small endpoint the client (and tests) query to see what arrived.

## 8. Auth

### 8.1 Mock Entra (`samples/MockEntra`)

- Endpoints: `/{tenantId}/v2.0/.well-known/openid-configuration`,
  JWKS, `/{tenantId}/oauth2/v2.0/token` (client-credentials).
- Issues RS256 JWTs with Entra-shaped claims: `iss`
  (`https://<mock-host>/{tid}/v2.0`), `aud` (`api://a2a-demo`), `tid`,
  `azp`/`appid`, `roles`, `scp`, `exp`, `nbf`, `iat`.
- Known clients and roles configured in `appsettings.Development.json`.
- Starts only when `ASPNETCORE_ENVIRONMENT=Development`; refuses to start otherwise.

### 8.2 Server

- JwtBearer with `Authority`, `Audience`, valid issuers from config.
  Same code for mock and real Entra.
- Authorization policies per agent/skill using app roles and scopes.
- API key scheme as a simple alternative (key from config/secret).
- Agent card `securitySchemes` and `security` are built from config, so the
  card always tells the truth about the running auth setup.

### 8.3 Client

- Mock: client-credentials POST to mock token endpoint.
- Real: `ClientSecretCredential` / `ClientCertificateCredential` / `DefaultAzureCredential`
  from Azure.Identity, chosen by config.

### 8.4 Push notification auth

- Client registers the webhook with a token/credentials as the spec allows.
- Server signs outgoing webhook calls (JWT signed by the server's key,
  JWKS published by the server) so the receiver can verify origin.
- Receiver validates the token and the task ID.

## 9. Configuration and environments

### 9.1 Local (default, zero dependencies)

- Fake agents, mock Entra, in-memory task store, HTTP on fixed localhost ports,
  console logging. `dotnet run` per project, or `run-all.sh` / `run-all.ps1`.

### 9.2 Docker Compose (optional)

- Services: mock-entra, agent-server, webhook-receiver; client as
  `docker compose run client <command>`.
- Profile `ollama` adds a local Ollama container for LLM mode without keys.
- `.env.example` committed; `.env` holds provider keys (git-ignored).
- Health checks gate start order. Same ports as local; only hostnames differ.

### 9.3 Production option

Switched by `ASPNETCORE_ENVIRONMENT=Production` and config/env vars only:

- Real Entra: authority/tenant/audience from config; mock Entra disabled.
- Real LLM provider selected by config; keys from env vars or a secret store.
- Durable task store: my call is an EF Core–based `ITaskStore` with
  **SQLite** (still local, no service) and **PostgreSQL** providers, chosen by
  connection string. Builder may substitute if a stable provider is a problem.
- TLS required for HTTP and gRPC; HTTPS redirection and HSTS on.
- OpenTelemetry traces/metrics/logs, OTLP exporter configured by env var (off locally).
- Rate limiting on public endpoints; request size limits.
- Push sender: HTTPS-only webhook URLs, block private/loopback IP ranges in
  Production (allowed in Development).
- Health endpoints (`/health/live`, `/health/ready`).
- Dockerfiles produce non-root images usable outside compose.
- `docs/production.md` lists every setting needed.

## 10. Testing

- **Contract tests**: serialize each model and compare to spec JSON examples;
  enum strings; error code mapping table; gRPC ↔ model mapping round-trips.
- **Integration tests**: `WebApplicationFactory` hosts AgentServer + MockEntra
  in-process. Theory data runs each flow across JSON-RPC, REST, gRPC.
  Flows: discovery, extended card (with/without auth), send (message result
  and task result), stream, get, list + paging, cancel (cancelable and not),
  resubscribe after disconnect, multi-turn, push (receiver in-process), every
  task state, each part type, each error code, version header rejection,
  401/403, extension negotiation.
- LLM mode is not in the default test run. A separate opt-in test category
  runs against Ollama or a real provider when configured.
- `dotnet test` from repo root must pass with zero external dependencies.

## 11. Documentation

- Root README: what A2A is (short), quick start (3 commands), flow map table,
  how to switch to LLM mode, how to switch to real Entra, production notes.
- README per project: what it is, what to copy, extension points.
- `docs/flows.md`: each flow with a sequence diagram (Mermaid) and pointers to
  agent code, client command, and test.

## 12. Left to the builder

- Exact project, namespace, and type names.
- Port numbers (fixed and documented).
- JSON source-generation details, internal folder structure.
- Retry/backoff numbers for push sender; fake-agent delay defaults.
- How the gRPC mapping layer is organized.
- Whether streaming internals use `Channel<T>` or another primitive.
- CLI parsing approach for the console (System.CommandLine only if a stable
  version exists; otherwise a small hand-written parser).

## 12a. Builder calls (decided by Claude, not the user — review these)

1. **Step-up auth uses app roles, not scopes.** Real Entra client-credentials
   tokens carry `roles`, never `scp`. AUTH_REQUIRED demo: Secure agent pauses
   until the client presents a token for an app holding the `Reports.Approve`
   role (client switches to a second registered app). Works the same on real
   Entra. Delegated user flows (device code, on-behalf-of) deferred.
2. **gRPC multi-agent routing:** each agent gets its own gRPC port. Card's
   gRPC interface URL is that host:port. `tenant` keeps its spec meaning.
   JSON-RPC and REST use `/agents/{name}/...` on the shared HTTP port.
3. **Missing `A2A-Version` header:** rejected with the spec's version error
   on all operations. Public and extended card endpoints exempt from the check.
4. **Push auth:** client-supplied webhook credentials (spec primary mechanism)
   always sent. Server-signed JWT added on top; Phase 1 signing key generated
   at startup (dev only). Production key source is Phase 2.
5. **Client auth for third-party agents:** client reads the card's
   `securitySchemes` and supports API key, HTTP bearer (static token), and
   OAuth2 client-credentials against any token URL (Entra being one case).
   mTLS via configured client certificate on the `HttpClient`. OIDC/interactive
   flows deferred.
6. **Spec conformance:** `a2a.proto` wins on names and enum values; spec prose
   wins on behavior. Our own contract tests are the gate for `dotnet test`. An
   optional, CI-only conformance stage against the official A2A TCK is
   documented but not part of the zero-dependency run.
7. **LLM mode in Phase 1:** only the `IChatClient` seam and config shape. Real
   provider adapters (Azure OpenAI, OpenAI, Anthropic, Ollama) are Phase 2.

## 13. Risks

- Hand-built protocol may drift from spec. Mitigation: vendored `a2a.proto`
  as source of truth for names; contract tests against spec examples; note
  the spec version and commit in `proto/README.md`.
- REST path details and error mappings must be read from spec section 11 and
  the error section, not guessed.
- When a stable official SDK ships, the team may want to switch; keep
  `IAgentHandler` and `IA2AClient` narrow so swapping is contained.
