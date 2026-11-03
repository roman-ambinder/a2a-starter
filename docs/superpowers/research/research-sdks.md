# Official/major A2A SDKs - state as of 2026-11-03

Method: GitHub repo pages, GitHub releases API, NuGet page, repo docs. Treated as data. Items marked (not verified) were not visible in the fetched pages.

## Version table
| SDK | Latest | Date | Stable? | Spec | Transports |
|---|---|---|---|---|---|
| a2a-dotnet (NuGet A2A, A2A.AspNetCore, A2A.Grpc, A2A.Grpc.AspNetCore, A2A.V0_3) | 1.0.0-preview4 | 2026-11-02 | PRERELEASE (preview1 2026-03-16, preview2 04-09, preview3 10-14) | 1.0 (+0.3 compat pkg) | JSON-RPC, REST(+SSE), gRPC |
| a2a-python (a2a-sdk) | 1.3.0 | 2026-10-27 | Stable (non-prerelease 1.x; 1.2.3 10-19, 1.2.2 10-05) | 1.0 (+0.3 compat) | JSON-RPC, REST, gRPC (client+server) |
| a2a-java | 1.5.0.Final (also 0.3.4.Final line) | 2026-11-03 | Stable | 1.0 and 0.3 side by side | JSON-RPC, gRPC, REST |
| a2a-js (@a2a-js/sdk) | 1.3.0 | 2026-09-29 | Stable ("v1.0 stable release") | 1.0.0 (+opt-in 0.3 legacyCompat) | JSON-RPC, REST, gRPC (Node only) |
| a2a-go (module /v2) | v2.7.0 | 2026-10-21 | Stable (non-prerelease, v2.x) | 1.0 | gRPC, REST, JSON-RPC |

Sources:
- https://github.com/a2aproject/a2a-dotnet ; https://api.github.com/repos/a2aproject/a2a-dotnet/releases ; https://www.nuget.org/packages/A2A (1.0.0-preview4, prerelease, net8.0+net10.0, ~1.2M total downloads)
- https://github.com/a2aproject/a2a-python ; https://api.github.com/repos/a2aproject/a2a-python/releases
- https://github.com/a2aproject/a2a-java ; https://api.github.com/repos/a2aproject/a2a-java/releases ; https://a2aproject.github.io/a2a-java/
- https://github.com/a2aproject/a2a-js ; https://api.github.com/repos/a2aproject/a2a-js/releases
- https://github.com/a2aproject/a2a-go ; https://api.github.com/repos/a2aproject/a2a-go/releases

## .NET (official) - key section
Source: https://raw.githubusercontent.com/a2aproject/a2a-dotnet/main/README.md, docs at https://github.com/a2aproject/a2a-dotnet/tree/main/docs (durable-push-notifications, extensions, migration-guide-v1, push-notifications, security, service-discovery, taskmanager-migration-guide).

Status: v1.0 spec, but all four 1.0 builds are PRERELEASE. Fast churn: preview3 had ~60 PRs, preview4 ~25 (19 days after preview3). Breaking changes between previews likely (e.g. preview2 renamed Blocking -> ReturnImmediately; preview4 "consolidated request handler contract"). Targets net8.0 and net10.0. Deps are light (Microsoft.Extensions.AI.Abstractions, Logging.Abstractions). Apache-2.0.

Public API shape:
- Client: IA2AClient, A2AClient (JSON-RPC), A2AHttpJsonClient (REST), A2AGrpcClient, A2AClientFactory (create from AgentCard, PreferredBindings), A2ACardResolver. Errors as A2AException with A2AErrorCode.
- Server: IA2ARequestHandler implemented by A2AServer; IAgentHandler (ExecuteAsync(RequestContext, AgentEventQueue, ct), CancelAsync default impl); helpers TaskUpdater (SubmitAsync, StartWorkAsync, AddArtifactAsync, CompleteAsync, FailAsync, RequireInputAsync) and MessageResponder (stateless reply). context.IsContinuation, context.StreamingResponse, context.UserText.
- Storage: ITaskStore (Get/Save/Delete/List) + InMemoryTaskStore only (README says dev/test only). IPushNotificationStore separate. Task mutation applied internally (TaskProjection.Apply). ChannelEventNotifier registered by AddA2AAgent.
- Hosting: AddA2AAgent<THandler>(agentCard), MapA2A(path) (JSON-RPC), MapHttpA2A() (REST), MapWellKnownAgentCard(card), AddA2AGrpc()/MapGrpcA2A() (separate package).
- Models: AgentTask, AgentCard (SupportedInterfaces with ProtocolBinding/ProtocolVersion), Message, Part (non-abstract, ContentCase).
- Writing an agent: implement IAgentHandler, register, map. Minimal echo agent is ~20 lines.
- Also: extension negotiation (preview4, docs/extensions.md, sample), .NET service-discovery logical URLs, Semantic Kernel sample, A2ACli sample, PushNotificationReceiver sample, V0_3 compat package.
- Push notifications (https://raw.githubusercontent.com/a2aproject/a2a-dotnet/main/docs/durable-push-notifications.md): IPushNotificationSender, default HttpPushNotificationSender (application/a2a+json; retries network/408/429/5xx; destination validation, TLS, redirect/cookie protection, host allowlist; auto-delete configs at terminal state; per-task ordering only in-process; IHostedService lifecycle). SDK does NOT give durable outbox/dead-letter/cross-process ordering - app must build. Signing/JWT/JWKS for push: not covered in that page (not verified elsewhere).

Limitations / gaps (from open issues https://api.github.com/repos/a2aproject/a2a-dotnet/issues and docs):
- Preview, not stable; API still moving.
- Only in-memory ITaskStore shipped; #502 asks for durable conditional persistence + ambiguous-write recovery; #463 "Task live coordination is process-local across server instances" (no multi-instance queue/event backplane); #450 tenant/owner isolation in ITaskStore still a PR.
- No built-in auth (Entra/API key/JWT) described; only security note on HttpClient cookie isolation. #399 (configureRequest callback on clients) is a PR. No built-in client interceptor pipeline found (use HttpClient handlers / DI).
- No OpenTelemetry/ActivitySource documented in README/issues (not verified; check source).
- Agent card per-request customization is an open feature (#353). Card signing: no evidence in .NET (not verified). Extended card: not verified.
- No database/Redis stores in the repo.
- Client retry: none documented.
- v0.3 compat quirk (#386).
- Upside: three transports, spec 1.0, extension negotiation, MS-owned-ish ecosystem (M.E.AI abstractions), samples, active weekly releases, 1.2M downloads.

## Python (a2a-sdk) v1.3.0
Sources: https://github.com/a2aproject/a2a-python ; https://api.github.com/repos/a2aproject/a2a-python/contents/src/a2a/server
- Install extras: http-server, fastapi, starlette, grpc, telemetry (OpenTelemetry), encryption, postgresql/mysql/sqlite/sql (database stores), all. Python 3.10+.
- Server dirs: agent_execution (AgentExecutor, RequestContext), events (EventQueue), tasks (TaskStore, TaskUpdater, SQL stores), request_handlers, routes (FastAPI/Starlette), cluster, owner_resolver.py (tenant/owner), id_generator.py; src/a2a/auth, extensions, migrations + a2a_db_cli.py + alembic.ini (DB migrations CLI), compat (0.3).
- Release notes: configurable SSE shutdown grace period (1.2.2), release parked interrupted tasks after timeout (1.3.0), client creates separate httpx client per client, 1.0 interface selection ignores patch version.
- Docs: https://a2a-protocol.org/latest/sdk/python/api/ ; migration guide 0.3->1.0 in repo; samples in a2a-samples; a2a-inspector.
- TCK: a runnable Python SUT exists for the TCK (https://github.com/a2aproject/a2a-tck).
- Detailed client interceptors / push sender / card signing: not verified from fetched pages.

## Java (a2a-java) 1.5.0.Final
Sources: https://github.com/a2aproject/a2a-java ; https://a2aproject.github.io/a2a-java/ ; https://raw.githubusercontent.com/a2aproject/a2a-java/main/extras/README.md
- Java 17+. Modules: client, server-common, spec, spec-grpc, jsonrpc-common, http-client, transport, reference, compat-0.3, boms, tck, itk, acts, extras, integrations/microprofile-config.
- Server: implement AgentExecutor; SDK handles protocol/routing/streaming; serves 1.0 and 0.3 clients side by side; reference JSON-RPC server (Quarkus-based reference artifacts; Jakarta/WildFly SUT exists for TCK).
- Extras (multi-instance ready): task-store-database-jpa (TaskStore, shared across instances), push-notification-config-store-database-jpa, queue-manager-replicated (QueueManager; MicroProfile Reactive Messaging over Kafka/Pulsar/AMQP, or custom ReplicationStrategy), http-client-vertx.
- 1.5.0: first phase of OpenTelemetry support; unified HTTP client config (breaking); authorized task listings; JPA schema migration; validation SPI change. 0.3.4.Final adds push notification auth support.
- Docs mention guides for Authorization, Security, Configuration, Extras, Multi-Tenancy.
- TCK module in repo; TCK SUT generators for a2a-java.
- Client: streaming, push notifications, task lifecycle.

## JS/TS (@a2a-js/sdk) v1.3.0
Sources: https://github.com/a2aproject/a2a-js ; releases API
- Server: AgentExecutor + DefaultRequestHandler + transport adapters; Express adapters jsonRpcHandler, restHandler, grpcService (Node only); one handler shared by several transports; task cancel; optional JWS agent card signing (src/signature.ts); extensions.ts; opt-in legacyCompat for 0.3.
- Stores: InMemory default; DatabaseTaskStore + DatabasePushNotificationStore (Kysely: Postgres, MySQL, SQLite, Cloudflare D1) added in 1.3.0; `a2a-db` migration CLI. Push credentials stored in plain text (documented caveat).
- Push: capability flag; InMemoryPushNotificationStore + DefaultPushNotificationSender, replaceable.
- Client: ClientFactory (URL or card), per-call signal/serviceParameters/context, sendMessageStream async generator, CallInterceptor before/after hooks, auth helpers retrying on 401/403.
- 1.2.x fixes: SettleByTaskId, extended card caching, tenant request context, input-mode validation, JSON-RPC errors as HTTP 200.
- Docs: migration guide, compat guide, persistent stores guide, samples.

## Go (a2a-go) v2.7.0
Source: https://github.com/a2aproject/a2a-go ; releases API
- Spec 1.0; Go 1.26+. a2asrv.NewHandler(AgentExecutor, opts); NewJSONRPCHandler, NewRESTHandler, a2agrpc.NewHandler; client a2aclient.NewFromCard, agentcard.DefaultResolver (file:// support). Pluggable auth middleware, DB backends.
- 2.5-2.7: AgentCard JWS signing/verification (2.6.0), input-mode validation, A2A-Version header handling, a2a+json Accept negotiation, extended card caching, CLI (separate a2a-cli repo).

## Testing / conformance
- A2A TCK: https://github.com/a2aproject/a2a-tck (pytest suite; all three transports; MUST=fail, SHOULD=xfail, MAY=skip; JSON/HTML/JUnit reports). SUT generators for a2a-java, a2a-python, a2a-jakarta. Not seen for .NET (not verified).
- Newer cross-SDK tooling: ITK and ACTS (names appear in .NET and Java repos/releases, e.g. "[ACTS] Push CRUD failures" issue #592; Go "shared ITK scenarios"). Purpose not detailed in fetched pages; treat as interop/conformance harnesses.
- a2a-inspector for manual validation. Java has test-utils-docker module.

## Features product teams will expect (merged checklist)
Spec/transport
- [ ] A2A 1.0 spec; JSON-RPC + REST + gRPC server and client; SSE streaming; resubscribe/subscribe
- [ ] v0.3 backward compat (client and server), A2A-Version header handling, a2a+json media type
- [ ] Multi-tenancy (tenant in path/request), owner/tenant isolation in stores
Server model
- [ ] Single agent-handler abstraction (AgentExecutor / IAgentHandler) + RequestContext + event queue + TaskUpdater helper + stateless message responder
- [ ] ITaskStore + shipped durable stores (SQL/EF/JPA/SQLAlchemy/Kysely), schema migrations
- [ ] Push-config store (durable) and sender with retries, SSRF/destination validation, auth schemes, auto-cleanup; outbox guidance
- [ ] Multi-instance: pluggable queue/event backplane (Java Kafka/Pulsar/AMQP replicated queue; Python cluster module); cross-process task coordination and cancel
- [ ] Concurrency safety: terminal-state transitions, cancel check-then-act, conditional/optimistic writes
- [ ] Agent card serving (well-known), per-request/customizable card, extended (authenticated) card with caching, JWS card signing + verification
- [ ] Extensions: declare, negotiate, activate (header), custom ops
- [ ] Input-mode/media-type validation, request size limit, SSE keep-alive and graceful shutdown
- [ ] Auth integration points (ASP.NET auth, middleware, user/principal in RequestContext), interceptors/middleware hooks on server
- [ ] OpenTelemetry tracing/metrics built in (Python telemetry extra, Java 1.5 OTel); ILogger logging
- [ ] Framework hosting: ASP.NET Core minimal API mapping, DI registration; (FastAPI/Starlette, Express, Spring/Quarkus/Jakarta analogues)
Client
- [ ] Client factory with transport negotiation from card (preferred bindings, fallback) + card resolver with caching
- [ ] Interceptors/per-call options (headers, cancellation, context); credential providers (bearer, API key, OAuth2/OIDC, 401 refresh retry)
- [ ] Retries/timeouts, typed errors (A2AException + codes), DI/HttpClientFactory integration, service discovery
Quality/packaging
- [ ] Stable (non-preview) semver releases, changelog, migration guides, API docs site, samples, CLI/inspector
- [ ] TCK/ITK/ACTS conformance in CI; test utilities (in-memory fakes, test host)
- [ ] Security guidance (cookie isolation, threat model), dependency patching

## Build-vs-adopt note (official .NET)
Official .NET SDK is spec-current (1.0, three transports, extension negotiation) but still prerelease (preview4, 2026-11-02), with only an in-memory task store, process-local coordination, and no documented built-in auth, OTel, or card signing. Internal SDK likely ahead on auth/JWT push signing/Entra and possibly behind on spec 1.0, v0.3 compat, extensions and TCK; re-evaluate when the official SDK reaches 1.0 GA, or consider adopting its models/abstractions and layering internal auth/stores on top.
