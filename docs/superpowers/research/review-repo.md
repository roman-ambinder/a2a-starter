# A2A .NET repo: gaps between what is here and an official production SDK

Repo: /Users/romanambinder/IdeaProjects/a2a-eample (branch fix/test-waits, commit 5292d56). Read-only review.
Severity: **B** = blocker for v1, **I** = important, **N** = nice to have. Size: S (< 1 day), M (days), L (1–3 weeks), XL (> 3 weeks).

---

## A. Strategy and packaging

### R1. Package name and namespace clash with the existing official A2A .NET SDK
- Area: packaging / strategy
- What: every assembly uses the root namespace `A2A.*` (for example `src/A2A.Protocol/A2AConstants.cs:3`, `src/A2A.Server/IA2ARequestHandler.cs:3`). On nuget.org the package ID `A2A` already exists. It is published from github.com/a2aproject/a2a-dotnet; the latest version is 1.0.0-preview4 (2026-11-02), and the versions run from 0.3.4-preview to 1.0.0-preview4. The type names overlap too (`AgentCard`, `AgentTask`, `A2AClient`-style names).
- Why: if teams use both packages, or a transitive dependency pulls in the upstream one, they get type and namespace conflicts. You also need a recorded decision: build on upstream, contribute to it, or fork it. Without that, there is no clear story for why this package should be the official one.
- Size: S (decision) to L (rename). Severity: **B**

### R2. No NuGet packaging metadata or pack pipeline at all
- Area: packaging
- What: `Directory.Build.props:1-8` sets only TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors and LangVersion. No src csproj has PackageId, Version/VersionPrefix, Authors, Description, PackageLicenseExpression, PackageReadmeFile, RepositoryUrl, PackageTags, IncludeSymbols/SymbolPackageFormat=snupkg, ContinuousIntegrationBuild, Deterministic or EmbedUntrackedSources (`src/A2A.Client/A2A.Client.csproj:1-14`, `src/A2A.Server/A2A.Server.csproj:1-17`, `src/A2A.Protocol/A2A.Protocol.csproj:1-5`, `src/A2A.Protocol.Grpc/A2A.Protocol.Grpc.csproj:1-18`). Samples and tests are not marked `IsPackable=false`. The repo has no LICENSE, CHANGELOG, SECURITY.md, CONTRIBUTING or CODEOWNERS file, and no git tags.
- Why: the packages cannot ship, cannot be debugged through SourceLink or symbols, and are not reproducible. Internal feeds usually require license and owner metadata.
- Size: M. Severity: **B**

### R3. CI has no release pipeline, versioning, signing or SBOM
- Area: packaging / CI
- What: `.github/workflows/ci.yml:16-33` has one job: restore, build and test on ubuntu. It has no `dotnet pack`, no publish to a feed, no version calculation (for example MinVer or Nerdbank.GitVersioning), no package signing, no SBOM, no release on tag, no Windows or macOS leg, and no coverage upload.
- Why: an official SDK needs releases that are repeatable and traceable, with provenance.
- Size: M. Severity: **B**

### R4. No public API tracking and no semver or breaking-change policy
- Area: packaging / API
- What: no `Microsoft.CodeAnalysis.PublicApiAnalyzers` and no `PublicAPI.Shipped.txt`/`Unshipped.txt`. No package-validation baseline (`EnablePackageValidation`, `PackageValidationBaselineVersion`). No documented semver policy. A public API change can merge without anyone noticing.
- Why: product teams pin to this SDK, so accidental breaks across 100+ public types (see R6) are very likely.
- Size: S–M. Severity: **B**

### R5. XML docs not generated or enforced; many public members undocumented
- Area: API / docs
- What: `GenerateDocumentationFile` is not set anywhere (`Directory.Build.props:1-8`), so no .xml ships and CS1591 is never raised. Examples with no docs:
  - every method of `IA2AClient` (`src/A2A.Client/IA2AClient.cs:13-23`)
  - most `IA2ARequestHandler` methods (`src/A2A.Server/IA2ARequestHandler.cs:11-33`)
  - all `AgentCard` and `AgentSkill` properties (`src/A2A.Protocol/Models/AgentCard.cs:3-67`)
  - `IPushConfigStore.GetAsync` (`src/A2A.Server/Push/IPushConfigStore.cs:11`)
  - `AgentCatalog.All/TryGet` (`src/A2A.Server/Agents/AgentCatalog.cs:22-25`)
- Why: IntelliSense is the main documentation product teams will read.
- Size: M. Severity: **I**

### R6. Large accidental public surface (implementation details are public)
- Area: public API
- What: these types are public but are internals or transport plumbing:
  - Server, framework internals:
    - `TaskManager` (`src/A2A.Server/Tasks/TaskManager.cs:20`, public ctor with 10 params at :40)
    - `TaskEventBroker` (`src/A2A.Server/Tasks/TaskEventBroker.cs:14`)
    - `PushNotificationSender` (`src/A2A.Server/Push/PushNotificationSender.cs:21`)
    - `TracingRequestHandler` (`src/A2A.Server/Telemetry/TracingRequestHandler.cs`)
    - `PageToken` (`src/A2A.Server/Tasks/PageToken.cs`)
    - `TaskStateRules` (`src/A2A.Server/Tasks/TaskStateRules.cs`)
    - `TaskWriter.Releaser` (`src/A2A.Server/Tasks/TaskWriter.cs:108`)
  - Server, transport plumbing:
    - `SseWriter` (`src/A2A.Server/Transports/Sse/SseWriter.cs`)
    - `RestErrorWriter`, `GrpcErrorMapper`, `ServiceParameters`, `RequestValidation`
    - `WebhookUrlValidator`, `AgentCardInterfaces`, `A2AGrpcService`
  - Client: `SseReader` (`src/A2A.Client/Transports/Sse/SseReader.cs`).
  - Protocol: `StrictEnumConverter<T>`, `UtcTimestampConverter`, `A2AJsonContext`.
  - Protocol.Grpc: the whole `ProtoMapper` plus the generated `Lf.A2a.V1` types (`proto/a2a.proto:12`). This exposes Google.Protobuf types in the public contract.
- Why: every public type becomes a compatibility promise. Concrete sealed classes (TaskManager, TaskEventBroker, PushSigningKey) also block replacement (see R13, R16).
- Size: M. Severity: **B**

### R7. Package split forces heavy dependencies on every consumer
- Area: packaging
- What:
  - `A2A.Client` always references Azure.Identity, Grpc.Net.Client and Grpc.StatusProto, plus A2A.Protocol.Grpc (`src/A2A.Client/A2A.Client.csproj:3-10`).
  - `A2A.Server` always brings Grpc.AspNetCore and calls `services.AddGrpc()` (`src/A2A.Server/A2A.Server.csproj:6-9`, `src/A2A.Server/Hosting/A2AServiceCollectionExtensions.cs:56`).
- Why: a team that only needs JSON-RPC still gets gRPC, protobuf and Azure.Identity. That adds size, vulnerability exposure and version conflicts. The usual split is Core / AspNetCore / Grpc / Azure (Entra) / Testing.
- Size: M. Severity: **I**

### R8. Single target (net10.0); no AOT or trim annotations
- Area: packaging
- What: `Directory.Build.props:3` sets only `net10.0`. No `IsAotCompatible` or `IsTrimmable`, even though Protocol uses source-generated JSON (`src/A2A.Protocol/Json/A2AJson.cs:10-30`).
- Why: probably fine for AKS teams on .NET 10 LTS. But the Protocol and Client packages are often used from older apps (net8, netstandard2.0, Functions). AOT-friendly SDKs are expected for containers.
- Size: M. Severity: **N**

---

## B. Spec coverage and interoperability

### R9. Only protocol version "1.0" is accepted; no 0.3.x interop and no version negotiation
- Area: spec
- What:
  - The server rejects any `A2A-Version` other than 1.0 (`src/A2A.Server/Transports/ServiceParameters.cs:11,34-37`).
  - The client sends a fixed version (`src/A2A.Client/A2AClientOptions.cs:20`) and ignores `AgentInterface.ProtocolVersion` when it picks an interface (`src/A2A.Client/A2AClientFactory.cs:23-27`).
  - Enums accept only the `TASK_STATE_*` / `ROLE_*` strings (`src/A2A.Protocol/Models/Enums.cs:6-27`), so 0.3-style payloads (`"completed"`, `"kind"`) fail.
- Why: a large share of agents teams will call (Python a2a-sdk, ADK, LangGraph, older .NET) still speak 0.3.x. An official client must at least detect the version, and ideally talk to 0.3.
- Size: L. Severity: **I**

### R10. Strict enums break forward compatibility
- Area: spec / protocol
- What: `StrictEnumConverter` (`src/A2A.Protocol/Json/StrictEnumConverter.cs:6-8`) throws on any unknown string. A newer agent that adds a `TaskState` value or a new `Role` makes the whole response fail to deserialize.
- Why: proto3 semantics say unknown enum values must be kept or mapped to UNSPECIFIED, not rejected. With strict parsing, client and server must be upgraded in lockstep.
- Size: S. Severity: **I**

### R11. Agent card signatures are modeled but never produced or verified
- Area: spec / security
- What: `AgentCard.Signatures` and `AgentCardSignature` exist (`src/A2A.Protocol/Models/AgentCard.cs:45-50,66`) and are mapped to proto (`src/A2A.Protocol.Grpc/ProtoMapper.AgentCard.cs:93,107`). No code signs a card (JWS) on the server. `A2ACardResolver` does not verify one (`src/A2A.Client/A2ACardResolver.cs:21-52`).
- Why: in a production mesh, clients need to trust card contents (endpoint URLs, token URLs). Card spoofing leads to credential leaks (see the token-URL logic in `ClientCredentialsProvider`).
- Size: M. Severity: **I**

### R12. Other spec behaviors missing or partial
- Area: spec
- What:
  - Activated extensions are not echoed back in an `A2A-Extensions` response header. The server only parses the request header (`src/A2A.Server/Transports/ServiceParameters.cs:15-22`); grep finds no response header write.
  - Skill-level `securityRequirements` (`src/A2A.Protocol/Models/AgentCard.cs:42`) are never enforced. The only check is one ASP.NET policy per agent (`src/A2A.Server/Agents/AgentRegistration.cs:10-16`).
  - Deprecated `implicit` and `password` OAuth flows (`proto/a2a.proto:568-574,609-627`) are dropped from `OAuthFlows` (`src/A2A.Protocol/Models/Security.cs:57-62`). A card that uses them loses that data on round-trip.
  - JSON-RPC batches are rejected (`src/A2A.Server/Transports/JsonRpc/JsonRpcModels.cs:30`). This is allowed, but it is a gap for JSON-RPC 2.0 clients.
  - No `SubscribeToTask` resume point: events published while a subscriber was disconnected are lost (in-memory broker, `src/A2A.Server/Tasks/TaskEventBroker.cs:29-51`), and there is no SSE `id`/`Last-Event-ID` support (`src/A2A.Client/Transports/Sse/SseReader.cs:11`).
- Proto vs. models: apart from the dropped flows, every proto message and field has a model (checked AgentCard, Task, Message, Part, Artifact, events, push config, requests and responses against `proto/a2a.proto:143-807`). The proto is pinned to a2aproject/A2A@4735f48 from `main` (`proto/README.md:3-6`), not a release tag. Pin to a released spec tag and add a CI check that diffs it against upstream.
- Size: M. Severity: **I**

---

## C. Production server

### R13. Single-instance only: in-memory broker, run registry, locks, push queue
- Area: server / scale-out
- What:
  - `TaskEventBroker` is a sealed concrete class with no interface (`src/A2A.Server/Tasks/TaskEventBroker.cs:14`), registered directly (`A2AServiceCollectionExtensions.cs:41`).
  - The running-execution and cancel maps are process-local (`src/A2A.Server/Tasks/TaskManager.cs:28,32`), so a cancel that lands on another pod does nothing (`TaskManager.cs:116-125`).
  - Per-task locks are in-process `SemaphoreSlim` (`src/A2A.Server/Tasks/TaskWriter.cs:20-32`).
  - The push queue is an in-memory channel (`src/A2A.Server/Push/PushNotificationSender.cs:24,33-36`).
  - Acknowledged in `docs/production.md` §8.
- Why: teams on AKS run at least 2 replicas for availability. Today, streaming, resubscribe, cancel and push all break silently behind a load balancer. You need seams (`ITaskEventBus`, `IDistributedTaskLock` or optimistic concurrency, `ICancellationSignal`), plus at least one reference implementation (Redis or Azure Service Bus or Cosmos change feed).
- Size: XL. Severity: **B**

### R14. No durable stores, and the store contract is not ready for real databases
- Area: server / storage
- What:
  - Only `InMemoryTaskStore` and `InMemoryPushConfigStore` exist (`A2A.Server/Tasks/InMemoryTaskStore.cs:7`, `A2A.Server/Push/InMemoryPushConfigStore.cs`).
  - `ITaskStore.SaveAsync` is a blind replace with no version or ETag (`src/A2A.Server/Tasks/ITaskStore.cs:14-15`), so it is unsafe once locks are not shared.
  - There is no Delete, TTL or retention method.
  - `ListAsync` takes no tenant (`ITaskStore.cs:21`).
  - Protocol validation (page size 1..100, history length, page-token parsing) lives inside the store implementation (`InMemoryTaskStore.cs:25-37`; `InMemoryPushConfigStore.cs:45-46`), so every provider must re-implement it the same way.
  - There is no store conformance test suite for implementers to run.
- Why: durable state is the first thing every production team needs. The contract has to be fixed before v1, because changing it later is a breaking change.
- Size: L (contract, conformance tests, plus one provider such as Cosmos DB or PostgreSQL). Severity: **B**

### R15. Unbounded memory growth: tasks, locks, history, push configs
- Area: server / reliability
- What:
  - Tasks are never evicted (`InMemoryTaskStore.cs:12`).
  - Per-task semaphores are "kept for the process lifetime" (`TaskWriter.cs:20-21`), even with a durable store.
  - History and artifacts grow without limit per task, and the number of push configs per task is unlimited (`InMemoryPushConfigStore.cs`; no max).
  - `ListAsync` sorts all tasks on every call (`InMemoryTaskStore.cs:39-43`).
- Why: a long-running pod hits OOM. The semaphore leak stays even after a durable store is plugged in.
- Size: S–M. Severity: **I**

### R16. Push signing key is per process and not pluggable
- Area: server / push
- What: `PushSigningKey` is a sealed class that creates a fresh RSA 2048 key in its constructor (`src/A2A.Server/Push/PushSigningKey.cs:12-26`) and publishes one key (`:31-46`). There is no interface for a key provider (Key Vault / HSM), no rotation and no multi-key JWKS. It is registered via TryAdd of the concrete type (`PushServiceCollectionExtensions.cs:15`).
- Why: receivers cannot pin keys. Each restart or replica changes the `kid`. Key Vault signing (key never leaves the HSM) cannot be plugged in.
- Size: M. Severity: **B**

### R17. Push delivery is lossy and non-standard
- Area: server / push
- What:
  - Events are dropped when the 10,000-item queue is full (`PushNotificationSender.cs:24,58-66`) and lost on restart (in memory).
  - After the retries run out, there is no dead-letter or outbox (`PushNotificationSender.cs:189-193`).
  - The signature is a custom `X-A2A-Push-Signature` JWT plus a custom JWKS path (`src/A2A.Protocol/A2AConstants.cs:193`, `src/A2A.Server/Hosting/A2AEndpointRouteBuilderExtensions.cs:22`). This is not part of the A2A spec, so other A2A SDK receivers will ignore it.
  - Delivery always uses `UseProxy=false` (`PushServiceCollectionExtensions.cs:28`), which conflicts with corporate egress proxies on AKS.
- Why: webhooks are the main mechanism for long tasks across products. They need at-least-once delivery, and the custom security has to be documented as an extension.
- Size: L. Severity: **I**

### R18. Tenant field is stored but not isolated
- Area: server / security
- What: `A2ACallContext.cs:11` says "Phase 1 does not isolate by it". The tenant is copied onto `StoredTask` (`ITaskStore.cs:6`), but Get, List and Cancel filter only by agent and owner (`InMemoryTaskStore.cs:40`; `TaskManager.cs:401-407`). REST parses a tenant path prefix (`src/A2A.Server/Transports/Rest/RestEndpoints.cs:178`), and gRPC routes by port only.
- Why: a multi-tenant agent (one deployment serving several product tenants) would leak across tenants. Teams need a tenant resolver seam (from token `tid`, route or header) plus enforcement in the store contract.
- Size: M. Severity: **B**

### R19. Default task ownership is too coarse for delegated tokens and anonymous callers
- Area: server / security
- What:
  - The owner key prefers `azp` over `sub`/`oid` (`src/A2A.Server/Auth/DefaultOwnerResolver.cs:48-52`). With delegated (user) tokens, `azp` is the client app, so all users of one front-end app share tasks: any of them can Get, List, Cancel or Subscribe to another user's task.
  - Every unauthenticated caller of an open agent maps to the one key `anonymous` (`src/A2A.Server/Auth/CallerIdentity.cs:17`; `DefaultOwnerResolver.cs:43`). They can all list and cancel each other's tasks.
  - `CanContinueInterrupted` lets any app in the same Entra tenant continue an AUTH_REQUIRED task (`DefaultOwnerResolver.cs:61-66`). In a single corporate tenant, that means any internal app.
  - `DefaultApiKeyAuthenticationType` is tied to the sample's scheme name (`DefaultOwnerResolver.cs:19-20`).
- Why: these are data-exposure risks in the default setup of an official SDK. Defaults must be safe.
- Size: S–M. Severity: **B**

### R20. Auth is left to samples; no Entra, API key or mTLS building blocks in the library
- Area: server / auth
- What: JwtBearer plus API key wiring and the `ApiKeyAuthenticationHandler` live only in `samples/AgentServer/Auth/AuthSetup.cs:38-47` and `samples/AgentServer/Auth/ApiKeyAuthenticationHandler.cs`. The library has no `AddA2AEntraAuthentication()`, no Microsoft.Identity.Web integration, no app-role policy helpers, and no client-certificate (mTLS) owner mapping. Cards' `securitySchemes` are hand-written and not derived from the configured auth (`samples/AgentServer/Cards/AgentCards.cs`). `docs/production.md` §1 says Entra was "never verified against a real tenant".
- Why: every team would copy the sample code, which is security-sensitive and then drifts. Entra on AKS (workload identity) is the stated target.
- Size: L. Severity: **B**

### R21. gRPC routes agents by listening port, and Kestrel binding comes from the public URL
- Area: server / hosting on AKS
- What:
  - The agent is chosen by `http.Connection.LocalPort` (`src/A2A.Server/Transports/Grpc/A2AGrpcService.cs:130-132`), with one port per agent (`A2AServerOptions.cs:17-18`).
  - `ConfigureA2AKestrel` binds to the port and host taken from `PublicBaseUrl` (`src/A2A.Server/Hosting/GrpcHostingExtensions.cs:26-34`). On AKS the public URL is `https://x.contoso.com` (port 443) while the pod listens on 8080.
  - gRPC listeners are h2c-only (`GrpcHostingExtensions.cs:34`).
  - The card's gRPC URL is `{publicHost}:{internalPort}` (`src/A2A.Server/Hosting/AgentCardInterfaces.cs:21-27`), which is wrong behind ingress.
- Why: this does not work behind ingress, Application Gateway or a service mesh without workarounds. N agents means N Service ports. You need routing by metadata or tenant, a separate public and bind address, and per-interface public URL overrides.
- Size: M. Severity: **B**

### R22. No graceful shutdown or recovery of running tasks
- Area: server / reliability
- What: agents run fire-and-forget via `Task.Run(..., CancellationToken.None)` (`TaskManager.cs:296`). Nothing tracks them for host shutdown: TaskManager is not an `IHostedService` and does not observe `IHostApplicationLifetime` (grep finds no usage in src). On SIGTERM (AKS rollout), in-flight tasks die without being marked; with a durable store they stay WORKING forever. There is no sweep at startup.
- Why: rolling deployments happen daily. Orphaned tasks and clients that hang are the result.
- Size: M. Severity: **B**

### R23. No backpressure, concurrency limits or rate limiting
- Area: server / limits
- What:
  - The number of concurrent agent executions is unbounded (`TaskManager.cs:296`).
  - There is no per-caller quota and no `AddRateLimiter` integration (grep finds none in src; `docs/production.md` §7).
  - Blocking `SendMessage` waits for the whole run with no server-side cap (`TaskManager.cs:68`).
  - `MaxRequestBodyBytes` applies only to JSON-RPC and REST (`JsonRpcEndpoint.cs:68`, `RestEndpoints.cs:110`). gRPC uses defaults (`A2AServiceCollectionExtensions.cs:56`).
  - There are no limits on parts per message, history length or artifact size.
- Why: LLM-backed agents are expensive. One noisy caller can exhaust the pod or the LLM quota.
- Size: M. Severity: **I**

### R24. SSE streams send no heartbeats
- Area: server / streaming
- What: `SseWriter` writes only events (`src/A2A.Server/Transports/Sse/SseWriter.cs:22-32`), with no keep-alive comments. Yet the client already skips heartbeats (`src/A2A.Client/Transports/JsonRpcA2AClient.cs:106`).
- Why: AKS ingress controllers (nginx: 60 s by default) and Azure load balancers cut idle streams during long LLM steps.
- Size: S. Severity: **I**

### R25. Health checks, TLS, HSTS and forwarded headers are left to samples
- Area: server / hosting
- What: health checks are mapped only in the sample (`samples/AgentServer/AgentServerApp.cs:59-64`). The library has no readiness check for the stores or the push queue. TLS, HSTS and HTTPS redirection are not implemented (`docs/production.md` checklist). There is no `ForwardedHeaders` guidance, and card URLs come only from static config (`AgentCardInterfaces.cs:15`).
- Why: every team re-does this. The SDK should ship `AddA2AHealthChecks()` and a hardened hosting template.
- Size: S–M. Severity: **I**

### R26. Cards are static; route layout is fixed
- Area: server / API
- What: card factories run once when the catalog is first resolved (`A2AServerBuilder.cs:18-21,41-53`). There is no `IAgentCardProvider` for dynamic or per-caller extended cards. Routes are hardcoded to `/agents/{agent}/...` (`A2AEndpointRouteBuilderExtensions.cs:20-24`), with no prefix option and no way to map a single agent at root. Cards are served with no Cache-Control or ETag (`AgentCardEndpoints.cs`; grep finds none).
- Size: M. Severity: **N**

---

## D. Client

### R27. No DI registration, HttpClientFactory or resilience integration
- Area: client
- What:
  - The client has no `AddA2AClient()` / typed or named client (grep in `src/A2A.Client` finds no `IServiceCollection` use).
  - `A2AClientFactory.Create` is static and takes a card (`src/A2A.Client/A2AClientFactory.cs:18`).
  - When no `HttpClient` is passed, each client builds its own `SocketsHttpHandler` (`ClientSupport.cs:36-41`; `RestA2AClient.cs:42`; `JsonRpcA2AClient.cs:36`), which causes socket churn if created per call.
  - There are no retries, circuit breaker or hedging, and no Polly / Microsoft.Extensions.Http.Resilience (grep finds none).
  - There is no timeout option; the HttpClient default of 100 s will kill a blocking `SendMessage` on a long task.
  - The client never retries after a 401 by refreshing the token.
- Why: production callers need pooled, resilient, DI-friendly clients with sensible defaults. They should know which operations are safe to retry: SendMessage is safe only with a messageId-based dedupe on the server, and that dedupe is also missing.
- Size: M–L. Severity: **B**

### R28. No streaming reconnect or task-completion helpers
- Area: client
- What: if a stream drops before a final event, the client throws (`JsonRpcA2AClient.cs:111`, `ClientSupport.StreamEndedEarly`). There is no automatic `SubscribeToTask` resume, no "send and wait until terminal" helper (subscribe or poll with backoff), and no handling of input-required or auth-required loops. A user must write all of this themselves (see `samples/AgentClient/FlowRunner.*.cs`).
- Size: M. Severity: **I**

### R29. Card resolver: fixed 5-minute cache, no size limits, no DI
- Area: client
- What:
  - The cache is a fixed 5 minutes and ignores Cache-Control and ETag (`src/A2A.Client/A2ACardResolver.cs:11,26,50`). The cache map is never trimmed (`:15`).
  - There is no response size limit (`:37`, `ReadAsByteArrayAsync`).
  - There is no card signature check (R11) and no authenticated extended-card refresh flow.
- Size: S–M. Severity: **I**

### R30. Untrusted responses are read without bounds
- Area: client / security
- What:
  - SSE events grow in a `StringBuilder` without a cap (`src/A2A.Client/Transports/Sse/SseReader.cs:17-38`).
  - Unary responses are read fully into memory (`JsonRpcA2AClient.cs:89,103`; `RestA2AClient.cs:233`).
  - There is no `MaxReceiveMessageSize` for gRPC (`GrpcA2AClient.cs:33-36`).
- Why: calling third-party agents is a trust boundary. A hostile or broken agent can cause the caller to run out of memory (OOM).
- Size: S. Severity: **I**

### R31. Credential providers are limited
- Area: client / auth
- What:
  - `ClientCredentialsProvider` supports only a client secret (`src/A2A.Client/Auth/ClientCredentialsProvider.cs:46`): no certificate or federated assertion.
  - `ApiKeyCredentialProvider` and `BearerTokenCredentialProvider` hold static values (`Auth/ApiKeyCredentialProvider.cs:9`, `Auth/BearerTokenCredentialProvider.cs:6`).
  - There is no on-behalf-of (OBO) provider to forward the user's identity to a downstream agent, and no provider chosen from the card's `securitySchemes`.
  - `A2AClientOptions.ClientCertificate` is ignored when an `HttpClient` is supplied (`A2AClientOptions.cs:25-26`).
- Why: on AKS the expected path is workload identity, which `AzureIdentityCredentialProvider` covers. Agent-to-agent calls that carry a user context need OBO.
- Size: M. Severity: **I**

### R32. Client API ergonomics
- Area: client API
- What:
  - The transport clients are public sealed classes with public constructors (`RestA2AClient`, `GrpcA2AClient`, `JsonRpcA2AClient`). Calling them directly skips tracing (`docs/production.md` §6, API notes).
  - `TracingA2AClient.Inner` is part of the public API (`A2AClientFactory.cs:16`).
  - There are no convenience overloads (send a text message, get a task by id).
  - `A2AAuthException` carries an HTTP status for gRPC too (`src/A2A.Protocol/Errors/A2AAuthException.cs`).
  - Transient and permanent errors are not distinguished: any non-A2A HTTP failure maps to InternalError (`RestA2AClient.cs:247`).
- Size: M. Severity: **I**

---

## E. Agent authoring model

### R33. Thin authoring model: no metadata, helpers, or context history
- Area: authoring
- What:
  - `AgentRequestContext` (`src/A2A.Server/Agents/AgentRequestContext.cs:15-23`) has no request `Metadata` (`SendMessageRequest.Metadata`, `Requests.cs:115`), no tenant, no owner, and no access to earlier tasks in the same `contextId`.
  - `IAgentEventSink` (`src/A2A.Server/Agents/IAgentEventSink.cs:15-27`) cannot set task or event metadata (proto `TaskStatusUpdateEvent.metadata`, `TaskArtifactUpdateEvent.metadata`).
  - There are no helpers: every agent writes `MessageId = Guid...`, `Role`, `ContextId` by hand (`samples/AgentServer/Agents/EchoAgent.cs:22-28`; `ReportAgent.cs`). There is no `TaskUpdater` or `AgentMessage.Text(...)` builder.
  - An agent cannot fail with a specific A2A error. Every exception becomes "Agent error" (`TaskManager.cs:332-334`).
  - The `returnImmediately` contract relies on the author emitting an event early (`IAgentHandler.cs:15-17`); the framework does not enforce it.
- Size: M. Severity: **I**

### R34. No Microsoft.Extensions.AI integration in the SDK
- Area: authoring / LLM
- What: `IChatClient` use exists only in samples (`samples/AgentServer/Agents/ReportAgent.cs:15`; `samples/AgentServer/Llm/ChatClientRegistration.cs`). The library has no adapter between A2A Message/Part and `ChatMessage`/`AIContent`, no "IChatClient-backed agent" base, no streaming-to-artifact bridge, and no "A2A agent as an `AIFunction` tool" for calling remote agents from an LLM. Only `Fake` is a valid provider (`docs/production.md` §2).
- Why: almost every product team agent will be LLM-backed. This is the most valuable piece for adoption.
- Size: L. Severity: **I**

### R35. No extension framework
- Area: authoring
- What: extensions are only matched by URI (`src/A2A.Server/Tasks/RequestValidator.cs:44-55`). There is no pluggable extension handler or middleware pipeline (request and response hooks), and no typed access to extension params or metadata.
- Size: M. Severity: **N**

### R36. DI scope and lifetime semantics underspecified
- Area: authoring
- What: the handler is created in a new async scope per run and per cancel (`TaskManager.cs:364-388`), so the cancel instance is not the run instance. This is documented only in a one-line summary (`IAgentHandler.cs:5-7`). `HttpContext` is not available, because the run outlives the request, and nothing says so. `AgentRegistration.HandlerType` is a `Type` with only a runtime check (`AgentCatalog.cs:15-16`).
- Size: S. Severity: **N**

---

## F. Testing and quality

### R37. TCK conformance not automated
- Area: testing
- What: the README says it plainly: "The CI workflow in this repo does not run the TCK" (`README.md:192-200`). The CI workflow has no TCK job (`.github/workflows/ci.yml`).
- Why: an "official" SDK must prove conformance on every change, for all three bindings.
- Size: M. Severity: **B**

### R38. No testing package for product teams
- Area: testing
- What: helpers exist only inside the repo: `tests/Shared/ActivityCollector.cs`, `tests/Shared/MetricCollector.cs`, and fixtures in `tests/A2A.Integration.Tests/Fixtures`. There is no `A2A.Testing` package with an in-memory test server (WebApplicationFactory), a fake `IAgentEventSink`, a recording client, a store conformance suite (see R14), or a fake remote agent.
- Size: M. Severity: **I**

### R39. Test structure and coverage gaps
- Area: testing
- What:
  - There is no `A2A.Client.Tests` project. Client tests live in the integration project, which references the samples (`tests/A2A.Integration.Tests/*.csproj` → `samples/AgentServer`, `samples/AgentClient`), so the library is tested through sample code.
  - There is no coverage collection, threshold or report in CI (`ci.yml:31-33`).
  - There are no load or soak tests (the memory growth in R15), no fuzzing of the JSON/SSE parsers, and no multi-instance tests.
- Size: M. Severity: **I**

### R40. No analyzers or security scanning beyond defaults
- Area: quality
- What:
  - `.editorconfig` holds only whitespace rules (`.editorconfig:1-15`). No `AnalysisLevel`/`AnalysisMode`, and no `EnforceCodeStyleInBuild`.
  - There is no CodeQL, no secret scanning config, no `NuGetAudit` mode/level set, and no `dotnet list package --vulnerable` gate (`ci.yml`).
  - Dependabot exists (`.github/dependabot.yml`), and `TreatWarningsAsErrors` is on (`Directory.Build.props:6`).
- Size: S. Severity: **I**

### R41. Repo hygiene artifacts
- Area: quality
- What: `TestResults/` folders exist at the root and under tests (for example `tests/A2A.Integration.Tests/TestResults`). The `.gitignore` covers `graphify-out/` but not TestResults (`.gitignore:9-10`). The solution is named `A2A.Reference.slnx`, which is reference or demo naming, as is the README framing "Phase 1 ... mock services" (`README.md:6`).
- Size: S. Severity: **N**

---

## G. Docs a product team needs

### R42. Missing consumer-facing documentation
- Area: docs
- What: today's docs explain this repo (`README.md`, `docs/flows.md`, `docs/architecture.html`, `docs/production.md` as a "Phase 2" list). They are missing:
  - "Install the NuGet and build your first agent / call your first agent" quickstarts
  - an API reference site (DocFX, which needs R5)
  - a versioning and support policy plus CHANGELOG
  - guides for implementing a durable store and an event bus (R13, R14)
  - an AKS deployment guide (Helm/manifests, workload identity, ingress for SSE and gRPC, probes, HPA, PodDisruptionBudget)
  - a security / threat-model doc (ownership model R19, tenant model R18, push SSRF, card trust)
  - an Entra app-registration runbook that has been verified
  - a migration guide from upstream a2a-dotnet or 0.3 (R1, R9)
  - troubleshooting and FAQ
  - SECURITY.md (vulnerability reporting) and CONTRIBUTING.md (internal inner-source)
- Size: L. Severity: **I**
