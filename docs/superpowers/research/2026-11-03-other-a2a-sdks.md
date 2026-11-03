# Other A2A SDKs — functionality and approaches

Date: 2026-11-03. Sources: two deep-research web passes (raw JSON next to this file) plus a direct
read of shallow clones of `a2aproject/a2a-dotnet` (e93f834, 2026-11-02), `a2a-python` (11f1116,
2026-11-02) and `a2a-tck` (d4d6fae, 2026-10-22).

## 1. Landscape

- Six official SDKs under `a2aproject`: Python, Go, Java, JS, .NET, Rust.
- All main SDKs target spec v1.0. v0.3 lives in a separate opt-in package
  (a2a-js opt-in, a2a-go `a2acompat/a2av0`, a2a-python `a2a.compat.v0_3`, a2a-dotnet `A2A.V0_3` + `A2A.V0_3Compat`).
- All three bindings (JSON-RPC, HTTP+JSON, gRPC) are treated as peers.
- Packaging: one transport-neutral core handler, thin per-binding adapters, optional packages per binding/store.
  Java uses a BOM; .NET keeps gRPC in `A2A.Grpc` / `A2A.Grpc.AspNetCore` so core users never pull `Grpc.*`.

## 2. a2a-dotnet (official) — read from source

Version 1.0.0-preview4 (2026-11-02). No stable 1.0. net8.0, works on net10.0.

| Area | What it has |
|---|---|
| Hosting | `AddA2AAgent<THandler>()`, `MapA2A()` (JSON-RPC), `MapHttpA2A()` (REST), `MapWellKnownAgentCard()`, `AddA2AGrpc()`/`MapGrpcA2A()`. One `IA2ARequestHandler` (`A2AServer`) behind all bindings. Agent card cache via `AgentCardCacheOptions.MaxAge` (1h). |
| Agent API | `IAgentHandler`, `TaskUpdater`, `MessageResponder`, task continuation (`TaskContinuationContext`). Custom operations registry tied to extensions (`A2ACustomOperationRegistry`). |
| Task store | `ITaskStore`: Get/Save/Delete/List. Only `InMemoryTaskStore` shipped. **No owner/tenant parameter** in the contract. |
| Push store | `IPushNotificationStore`: Save/Get/GetAll/List/Delete. Delete takes an optional `version` (optimistic concurrency). List uses HMAC-signed, revision-bound page cursors. Only in-memory shipped. No encryption at rest. |
| Push delivery | `HttpPushNotificationSender` + `PushNotificationHostedService`. Bounded `Channel` (256), queue-full policy Wait/Reject, 3 attempts, exponential backoff 1s×2 up to 30s, 15s per-attempt timeout, 5s shutdown drain, auto-delete configs on terminal state, legacy `X-A2A-Notification-Token` header. Process-local only (no durable outbox). |
| SSRF guard | `IPushNotificationUrlValidator.ValidateAsync` returns the **resolved IPs**, so the connector can pin them (closes DNS-rebinding). Default: HTTPS only, public addresses only, loopback opt-in. |
| Subscribe | `SubscribeToTaskAsync` over `ChannelEventNotifier` — per-task in-process channels plus per-task lock. Single node only; no cross-instance replication. |
| Extended card | `GetExtendedAgentCardAsync` is `virtual`; base throws `ExtendedAgentCardNotConfigured` / `UnsupportedOperation`. You must subclass `A2AServer` to serve one. |
| Card signing | Model has `AgentCardSignature` and maps it over gRPC. **No signer, no verifier, no JCS canonicalization.** Extension code avoids mutating signed cards. |
| Telemetry | `ActivitySource("A2A")` and `Meter("A2A")`. Metrics: `a2a.server.request.count/duration`, `a2a.server.error.count`, `a2a.server.task.created`, `a2a.server.stream.event.count`, `a2a.server.push.count`, and client mirrors. Span names are mixed (`A2AServer.SendMessage`, `SendMessage`, `A2AClient/{method}`, `A2A.Push.{action}`). Tags mix `a2a.*` with unprefixed `task.id`; push uses hashed ids (`a2a.push.config.id_hash`). No explicit trace-context propagation into push requests beyond what `HttpClient` does by default. |
| Conformance | CI runs the shared **a2a-itk** image pinned to a commit: ITK interop (star vs peers on PR, full matrix nightly) and **ACTS** conformance over jsonrpc,rest,grpc. `acts/sut-behaviors.yaml` lists the `tck-*` behaviours the test agent claims; claimed-but-missing behaviours fail instead of skipping. Known gaps go in `a2a-itk/known_failures.yaml`. |

## 3. a2a-python — patterns not in a2a-dotnet

- **Owner scoping** in stores via `owner_resolver(ServerCallContext)`. Push store has `get_info` (caller-scoped)
  and `get_info_for_dispatch` (cross-owner). Trap: default dispatch fallback returns nothing.
- **SQL stores** (SQLAlchemy) for tasks and push configs; push configs can be Fernet-encrypted at rest.
- **Card signing**: `utils/signing.py` — `create_agent_card_signer` (JWS, protected header with `kid`, `alg`, `jku`)
  and `create_signature_verifier(key_provider(kid, jku), algorithms=[...])`; `_jcs.py` does RFC 8785 canonicalization.
  Signer default `alg` is **HS256** — a footgun for a public card.
- **Card route**: `card_modifier` per request; weak ETag computed over the card **without** `signatures`, so
  per-request ES256 signing still revalidates with 304.
- **Extended card**: `DefaultRequestHandler(extended_agent_card=..., extended_card_modifier=...)` — data, not subclassing.
- **Telemetry**: generic `@trace_function` / `trace_class` decorators with `attribute_extractor`; no-op if OTel missing.
- **Client**: `ClientFactory` transport registry, `create_from_url` + `signature_verifier` hook.

## 4. a2a-java

- Each binding is its own artifact; versions through `a2a-java-sdk-bom`.
- Extras: JPA task and push stores (CDI `@Alternative`), `ReplicatedQueueManager` with pluggable
  `ReplicationStrategy` (Kafka reference) so streaming/resubscribe works on any node. OTel and multitenancy extras.
- Security is delegated to Quarkus (`@Authenticated` on endpoints + `quarkus-oidc`).

## 5. Framework integrations

- **Microsoft Agent Framework** (.NET and Python) builds on the official SDKs. .NET: `AddA2AServer(name)` keyed per agent,
  `MapA2AHttpJson` / `MapA2AJsonRpc`, keyed DI overrides for handler, session store and task store.
  No gRPC, no background tasks yet, one card per host.
- **LangSmith Agent Server**: `/a2a/{assistant_id}`, JSON-RPC v1.0 only, no SubscribeToTask, no push.
- **Google ADK, BeeAI**: no verified data.

## 6. TCK

`a2a-tck` (Python, pytest). Runs against `--sut-host`, picks bindings from the card's `supportedInterfaces`.
Requirement groups (in `tck/requirements/`): agent card (CARD-DISC/STRUCT/CACHE/EXT/SIGN/PROTO), auth, core operations
(SEND, GET, LIST, CANCEL, HIST, MULTI, STREAM, CAP, ERR, EXECUTION-MODE), data model, per-binding (gRPC, HTTP+JSON,
JSON-RPC, BIND-EQUIV), push notifications, streaming, versioning, interop. RFC 2119 levels: MUST = fail,
SHOULD = xfail, MAY = skip if not declared. Reports: compatibility JSON/HTML + JUnit XML. SUT code is generated from
Gherkin scenarios for a2a-python, a2a-java, a2a-jakarta (no .NET target).

## 7. Takeaways for this SDK

Copy:
1. Validator that returns resolved IPs, and pin the connection to them (a2a-dotnet).
2. Bounded push queue with explicit queue-full policy, backoff, drain on shutdown, auto-delete on terminal (a2a-dotnet).
3. HMAC-signed, revision-bound page tokens; versioned delete for push configs (a2a-dotnet).
4. Owner scoping in the store contract plus an explicit dispatch read — make the dispatch read abstract (a2a-python).
5. JWS signer + verifier with JCS and an algorithm allow-list; ETag over the unsigned card (a2a-python). Default to ES256, not HS256.
6. Extended card as configuration (card + modifier), not subclassing (a2a-python).
7. Pluggable queue replication for multi-node subscribe (a2a-java).
8. Wire ITK/ACTS from `a2a-itk` into CI with a pinned revision and a claimed-behaviours file that fails when unimplemented (a2a-dotnet).

Gaps nobody fills well (room to lead):
- Durable push outbox (all are process-local or best-effort).
- Push config encryption at rest outside Python.
- Consistent OTel naming (`a2a.*` everywhere) and trace propagation into push webhooks.
- Durable stores for .NET (EF Core / Redis) — a2a-dotnet ships in-memory only.
- Multi-node subscribe outside Java.

## 8. Mapping to the SDK specs (`docs/superpowers/specs/2026-11-03-sdk-*.md`)

| # | Takeaway | Spec | Status |
|---|---|---|---|
| 1 | Pin push connection to validated IPs | 08 "SSRF guard and proxy" | Covered. Connect-time re-resolve in `ConnectCallback` does the same job; proxy mode adds a pre-attempt check. |
| 2 | Bounded queue, backoff, drain, auto-delete on terminal | 08 "Delivery pipeline", 05 §7.6 | Covered and goes further: durable outbox, per-config order, dead letters. In-memory full → dead-letter `outbox_full` instead of Wait/Reject. |
| 3 | HMAC page tokens, versioned push-config delete | 05 §7.4, §7.7 | Different choice. Tokens are plain `v1:{sequence}` keyset cursors, scoped by owner, so tampering only moves within the caller's own data. Delete is idempotent, no version. Fine as is; record it as a decision. |
| 4 | Owner scoping + explicit dispatch read | 05 §7.2 (`OwnerScope`, `TaskRef`), 07 | Covered. Delivery reads from the outbox payload, so Python's cross-owner dispatch trap cannot happen. |
| 5 | JWS + JCS + alg allow-list, ES256 default, ETag over unsigned card | 04, B13 | Covered. `AllowedAlgorithms` has no HS*; dev key is ES256; ETag = unsigned payload hash + key ids. |
| 6 | Extended card as configuration | 07 (auth, skill filter), 09 §7.4 (per-request cards) | Covered. |
| 7 | Multi-node subscribe | 06 | Covered, different approach: owner-pod forwarding, not Java-style queue replication. |
| 8 | ITK/ACTS from `a2a-itk` in CI | 03 (TCK), 16 (interop) | **Gap.** 03 runs `a2a-tck` directly; 16 builds its own harness against Python/JS/Java and puts Go out of scope. Upstream `a2a-itk` already ships a shared Docker image, a peer matrix (Python, Go, .NET), PR star + nightly full matrix, ACTS conformance over all three transports, and a `known_failures.yaml`. a2a-dotnet uses it, pinned to a commit. |
| 9 | Push credentials encrypted at rest | 08 "Credentials at rest", 05 §7.7 | Covered (Data Protection via `IPushCredentialProtector`; conformance test checks stored bytes). |
| 10 | Durable push outbox | 05 §7.6, 08 | Covered. |
| 11 | OTel names and propagation | 01 (names follow assembly), 02 (names are API), 08 (`traceparent`), observability design | Covered. `Ambinder.A2A.*` source names also avoid a clash with the official SDK's `"A2A"` source. |
| 12 | Durable .NET stores | 05 §7.11–7.12 (EF Core, Cosmos) | Covered. |
| 13 | Coexist with official `A2A` package | 01 (rename + coexistence test vs 1.0.0-preview4) | Covered. preview4 shipped 2026-11-02, so the pin is current. |

### Spec changes (applied 2026-11-03 to 03, 05, 16)

- **16 (interop):** evaluate adopting `a2a-itk` as the harness (or as a second job) instead of a hand-built one. It brings Go and the official .NET SDK as peers for free. Keep the golden JSON fixtures, which `a2a-itk` does not replace.
- **03 (TCK):** borrow the ACTS "claimed behaviours" file: the SUT lists the `tck-*` behaviours it implements, and a listed-but-missing behaviour fails instead of skipping. Consider running ACTS next to `a2a-tck`.
- **05:** add one line to "Left to the builder" or the decisions doc that page tokens are unsigned keyset cursors on purpose (owner-scoped).
