# A2A integration pain points (evidence from the field)

Date: 2026-11-03. Method: GitHub issue lists sorted by comments, targeted issue reads, spec "what's new" doc, Python migration guide, a few web posts. Page content was treated as data. Issue states/dates are as shown on fetch.
Limits: I did not read every issue. Comment counts are from the first page of each tracker. Marked "inferred" = my own conclusion, not found in a source. Vendor blogs are weak evidence; I say so.

Evidence scale: A = many independent reports / many SDKs; B = several reports or one very detailed one with maintainer confirmation; C = single report or inferred.

---

## Theme 1. Wire format, versions, and cross-SDK interop

### 1. 0.3 -> 1.0 breaking changes and silent failures (A)
- What goes wrong: nearly every JSON shape changed. Enums became `TASK_STATE_*` / `ROLE_*`. `kind` was removed; Parts merged into one `Part` (`text/raw/url/data`, `mediaType`). Stream `final` flag removed (clients waiting for `final:true` hang). Errors moved from RFC 9457 to `google.rpc.Status`/`ErrorInfo`. Pagination became `pageToken/pageSize`. Old string compares like `"completed"` fail silently. Implicit/password OAuth flows removed. `/v1` REST prefix dropped. Timestamps must be ISO 8601 UTC with ms.
- Evidence: https://github.com/a2aproject/A2A/blob/main/docs/whats-new-v1.md ; https://github.com/a2aproject/a2a-python/tree/main/docs/migrations/v1_0 ; https://llm4agents.com/blog/a2a-v1-audit-breaking-changes ; migration guide itself had a wrong example (pagination names) fixed in https://github.com/a2aproject/A2A/pull/2158 ; a2a-js user needed v0.3 push bodies converted: https://github.com/a2aproject/a2a-js/issues/646 ; third-party projects doing v1 compat work: https://github.com/topcheer/ggcode/pull/2515 , https://github.com/5queezer/hrafn/issues/66
- Existing SDKs: Python has `enable_v0_3_compat=True` plus a v0.3 `AgentInterface` (known limits tracked in a2a-python #742); JS has `@a2a-js/sdk/compat/v0_3` and added a single `legacyPushNotificationToV1StreamResponse` helper (PR #726) after pushback on exposing all converters (bundle size tripled, converters would become a public contract).
- SDK opportunity: first-class dual-version server and client (serve 0.3 and 1.0 interfaces from one handler), strongly typed enums so string compares cannot compile, a stream reader that handles "closure = done", a public boundary converter for legacy webhook bodies. Keep compat in a separate package to protect bundle/AOT size.

### 2. JSON serialization drift between SDKs (A)
- What goes wrong: SDKs disagree on discriminators, casing, nesting. .NET dropped `kind` on sealed `Message` because System.Text.Json only writes discriminators via the polymorphic base (https://github.com/a2aproject/a2a-dotnet/issues/138, 14 comments). Java server (Jackson) emitted OAuth2 security scheme without wrapper, Go client rejected it (https://github.com/a2aproject/a2a-go/issues/347). JS 0.3 REST needed `data.data` and `content` because REST followed ProtoJSON while JSON-RPC followed JSON Schema; maintainer says 0.3 was inconsistent (https://github.com/a2aproject/a2a-js/issues/330). Java 0.2.5 client vs Python 0.3.0 server gave parse errors (https://github.com/a2aproject/a2a-java/issues/204). a2a-tck lists `history_length` sent instead of `historyLength` (tck #242) and path joins like `//message:send` and trailing `/` (tck #262, #263): https://github.com/a2aproject/a2a-tck/issues . Python proto types: whole numbers in `Part.data` come back as floats (`4326` -> `4326.0`) per the Python migration guide.
- Existing SDKs: Python moved to proto-generated types (strict, no Pydantic) -> new pain: no validation of required fields, no schema generation for FastAPI (https://github.com/a2aproject/a2a-python/issues/884, 33 comments, open; community `a2a-pydantic` created). Go added a lenient fallback parser (PR #358).
- SDK opportunity: one canonical ProtoJSON-conformant serializer (source generated, AOT-safe in .NET), strict outbound / tolerant inbound ("be liberal in what you accept" mode with logging), golden-file tests shared with the TCK, required-field validation.

### 3. Version negotiation and mixed-version peers (B)
- What goes wrong: empty `A2A-Version` header means 0.3 (silent downgrade). CDN can serve a cached 0.3 card to a 1.0 client unless `Vary: A2A-Version` is set. Clients assume `supportedInterfaces[0]` is the best one. Python 1.0 handler did not validate the header (a2a-python #856, cited in #884). Peer SDKs were on different spec versions in practice (java #204, go #157 maintainer: "library implements 0.3.0, not draft 1.0").
- Evidence: https://llm4agents.com/blog/a2a-v1-audit-breaking-changes ; https://github.com/a2aproject/A2A/blob/main/docs/whats-new-v1.md ; https://github.com/a2aproject/A2A/discussions/1416 (backward compat; some fields like `security` cannot be bridged)
- SDK opportunity: client picks interface by (binding, version) match, always sends `A2A-Version`, server validates it and returns the proper version error, card endpoint sets `Vary`. Expose negotiated version on the request context.

### 4. Agent Card fetching, discovery, and per-request cards (B)
- What goes wrong: card route is not required at `/.well-known/` (dotnet #105, closed with `MapWellKnownAgentCard`). v1 servers may serve only `agent-card.json`, older ones `agent.json`; clients probe both (ggcode PR above). Hosting many agents behind one server needs per-request/per-subdomain cards; .NET v1 lost the v0.3 request-time card handler and the issue is still open: https://github.com/a2aproject/a2a-dotnet/issues/353 . Card fetch through Kubernetes ingress failed with 502, suspected h2c upgrade (https://github.com/a2aproject/a2a-java/issues/502, 26 comments, closed without confirmed root cause). Live-looking cards for dead endpoints: https://github.com/a2aproject/A2A/issues/1755 (small, shaky data: reachability 12-73% depending on registry; tasks/send probes outdated).
- SDK opportunity: card resolver that tries v1 and legacy paths, caches with ETag/Cache-Control, forces HTTP/1.1 or h2 explicitly (no h2c upgrade surprises), async `HttpContext`-aware card provider for ASP.NET Core, health/liveness check helper.

### 5. Signed Agent Cards: canonicalization mismatch (B)
- What goes wrong: spec 8.4.1 under-determined. Python 1.1.2 escapes non-ASCII, sorts by code point, emits `1e-06`; JS matches RFC 8785. Both SDKs drop REQUIRED fields at default values, contradicting the spec example. Signatures from one SDK fail in another. TCK has unimplemented CARD-SIGN tests and a case where a2a-js accepts what it should reject.
- Evidence: https://github.com/a2aproject/A2A/issues/2122 (48 comments, open) ; https://github.com/a2aproject/a2a-python/issues/1278 ; https://github.com/a2aproject/A2A/issues/2318 (example invalid, fixed in PR #2319) ; tck #261, #187, #227, #236.
- SDK opportunity: ship one RFC 8785-correct JCS implementation with field-presence rules, verify against cards signed by Python/JS/Go in CI, make verify opt-in with clear failure reasons (key mismatch vs format drift vs tamper - suggested in A2A #1755 thread). Be explicit about the "served-scope" reading until the TSC rules.

### 6. Error formats and error mapping (B)
- What goes wrong: v1 changed HTTP errors to `google.rpc.Status` with `ErrorInfo` (`reason`, `domain: a2a-protocol.org`); JSON-RPC `data` is an array. Parsers keyed to problem+json misread. SDKs threw language exceptions instead of protocol errors: java #959 "Throw A2AError instead of java.lang exceptions" (12 comments); go #175 custom errors/details; python #737 "Improve error handling"; a2a-cli #78 protocol error lost in fallback. Python 1.0 turned silent bad executor sequences into hard `InvalidAgentResponseError`.
- Evidence links: https://github.com/a2aproject/a2a-java/issues?q=959 ; https://github.com/a2aproject/a2a-go/issues/175 ; https://github.com/a2aproject/a2a-python/issues/737 ; https://github.com/a2aproject/a2a-cli/issues/78
- SDK opportunity: one typed exception hierarchy mapped to all three bindings (JSON-RPC code, HTTP status, gRPC status) with ErrorInfo; client surfaces the same typed errors; unknown exceptions become INTERNAL with correlation id.

### 7. Binding differences: JSON-RPC vs REST vs gRPC, URL and tenant handling (B)
- What goes wrong: same operation, different field shapes (go #157: `name` for gRPC vs `id` for JSON-RPC). Adding `metadata` to a gRPC request broke HTTP serialization (A2A #1002). Clients ignore the tenant declared in `AgentInterface` (tck #253). `tenant` is client-supplied; dotnet #539 says hosts should pick agents from trusted routing state, not the client `Tenant` field (security point, cited in https://github.com/a2aproject/a2a-dotnet/issues/353). SUBSCRIBE verb mismatch (tck #201).
- SDK opportunity: transport abstraction with identical behavior tests across bindings; tenant resolved server-side by a pluggable resolver; client applies interface tenant by default.

---

## Theme 2. Task lifecycle, streaming, long-running work

### 8. Task and context ID handling in multi-turn conversations (A)
- What goes wrong: clients invent or reuse the wrong `taskId`. Server rejects unknown client-generated taskId ("Task was specified but does not exist"), or a completed task's id ("terminal state"). Hosts must clear taskId on terminal state, but keep contextId; reset both when switching agents. ADK RemoteA2aAgent did not send taskId on input-required follow-ups, started new tasks; forwarding session id as contextId can collide across users. JS executors that make their own ids break cancel. Go "missing TaskID" confusion.
- Evidence: https://github.com/a2aproject/a2a-samples/issues/304 ; https://github.com/google/adk-python/issues/4934 , /3393 , /3765 , /7305 ; https://github.com/google/adk-java/issues/1718 ; https://github.com/a2aproject/a2a-js/issues/132 ; https://github.com/a2aproject/a2a-python/issues/218 and PR #264 ; LangGraph endpoint uses contextId directly as thread_id so it must be a UUID (https://docs.langchain.com/langsmith/server-a2a).
- SDK opportunity: a client-side `Conversation`/session object that tracks contextId + current non-terminal taskId and applies the rules (clear on terminal, resume on input-required/auth-required). Server `RequestContext` always supplies ids; executors cannot mint their own by accident. Namespace contextId with tenant/user.

### 9. Waiting for completion / long-running tasks / non-blocking send (A)
- What goes wrong: every integrator writes a poll-until-terminal loop, resubscribe-on-drop logic, timeout and backoff. Non-blocking send still waits for first event (python #951: 5-7 s under load; closed "not planned", workaround = subclass handler). Cancel in non-blocking mode did not reach the executor (js #132). Heartbeat/cron agents have no way to say "offline until X" (A2A #1667, 41 comments, no maintainer answer in thread).
- Evidence: https://github.com/a2aproject/a2a-python/issues/951 ; https://github.com/a2aproject/a2a-js/issues/132 ; https://github.com/a2aproject/A2A/issues/1667 ; MAF uses continuation tokens + `AllowBackgroundResponses` to poll or resubscribe: https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/a2a ; general guidance (blogs, weak): https://dev.to/rosgluk/a2a-streaming-and-async-tasks-for-long-running-agent-workflows-1193
- SDK opportunity: `SendAndWaitAsync(message, WaitOptions{timeout, pollBackoff, preferStream, onUpdate})` that streams, falls back to polling, resubscribes after drop, handles input-required/auth-required as returned states, and respects cancellation tokens (cancel -> CancelTask). Server: documented non-blocking path that returns the Task immediately.

### 10. Streaming (SSE) robustness: buffering, reconnect, slow consumers, lost events (B)
- What goes wrong: events delivered only at end (js #316, likely proxy/env; unresolved). Python EventConsumer timeout lost events from a Redis-backed queue (python #768; fixed only by rewrite in 1.0 alpha). Go: one slow subscriber wedges the whole event broker, deadlocks creation for other tasks, leaks quota (go #395, open). Context canceled with delayed streaming messages (go #124). Starlette 503/ReadTimeout on multiple TextParts (python #112). Proxies/mobile limits for SSE (blogs, weak). No `final` in v1 so "stream closed" is the only completion signal.
- Evidence: https://github.com/a2aproject/a2a-go/issues/395 ; https://github.com/a2aproject/a2a-python/issues/768 ; https://github.com/a2aproject/a2a-js/issues/316 ; https://github.com/a2aproject/a2a-go/issues/124 ; https://github.com/a2aproject/a2a-python/issues/112 ; https://www.glukhov.org/ai-systems/architecture/a2a-streaming-async-task-lifecycle
- SDK opportunity: bounded per-subscriber channels with a drop-subscriber policy (not drop-events), heartbeat comments to defeat idle proxies, `Last-Event-ID`/resubscribe helper, correct `X-Accel-Buffering: no`/flush headers (inferred), clear semantics for end-of-stream.

### 11. Artifact chunking and ordering (C)
- What goes wrong: v1 adds `index` on `artifactUpdate` for ordering; clients must append/lastChunk correctly. Large artifacts pressure LLM context (blog, weak: https://dev.to/chunxiaoxx/a2a-vs-mcp-in-2026-the-layered-architecture-production-ai-teams-actually-need-45jm). I found no dedicated high-traffic issue on chunk assembly. Treat as inferred need.
- SDK opportunity (inferred): `ArtifactAssembler` that merges chunks from stream or task snapshot; builder for emitting chunked artifacts.

### 12. Persistent task store, multi-instance servers, push-notification delivery (A)
- What goes wrong: only in-memory store at first; everyone writes their own Postgres/Redis store. JS #114 (22 comments) closed by official DB store in 1.3.0 (PR #756). Store lacks user identity for scoped `tasks/list` (maintainer comment). Queues across instances (Redis) are fragile (python #768). Push: Python ignored `PushNotificationConfig.authentication` (no `Authorization` header) until fix in 1.2.0 (https://github.com/a2aproject/a2a-python/issues/585); push only sent from the streaming path at first (python #218); push config model flattened in v1 and payload is now StreamResponse.
- Evidence: https://github.com/a2aproject/a2a-js/issues/114 ; https://github.com/a2aproject/a2a-python/issues/585 ; https://github.com/a2aproject/a2a-python/issues/218
- SDK opportunity: `ITaskStore` + EF Core/Redis/Cosmos reference implementations, user-scoped queries, distributed event queue interface, push sender with auth schemes, retry/backoff, SSRF guard on webhook URLs (inferred), signature/token verification helper for receivers, test fakes.

### 13. Cancellation semantics (B)
- What goes wrong: cancel races, cancel not invoking executor in non-blocking mode (js #132, python/TCK #190 "verify a cancel race is fixed"), who may cancel (client, gateway, upstream agent - blog guidance). Executors that ignore the cancel token keep running (inferred).
- SDK opportunity: cancellation token flowed into executor; framework guarantees final state `CANCELED` and idempotent cancel.

---

## Theme 3. Security, identity, extensions

### 14. Authentication and per-request credentials on the client (A)
- What goes wrong: per-user headers for a multi-user client agent: .NET #107 (open; options debated; CA1068 and overload explosion), JS #101, Go #163 TCK. OAuth: v1 removed implicit/password flows, adds device code and PKCE flag. Card fetch needs a handler pipeline but the card is needed to build the pipeline (dotnet #107 comment). Java OIDC propagation into executor broke (java #893, 31 comments; fixed by #902/#904). Gateway-level auth guidance is generic REST (https://zuplo.com/learning-center/agent-to-agent-a2a-protocol-guide).
- Evidence: https://github.com/a2aproject/a2a-dotnet/issues/107 ; https://github.com/a2aproject/a2a-js/issues/101 ; https://github.com/a2aproject/a2a-java/issues/893
- SDK opportunity: `IHttpClientFactory`-friendly client, `DelegatingHandler`/auth provider interface that can read the card's security schemes, per-call `RequestOptions` (headers, tenant, extensions, version), built-in OAuth2 client-credentials/auth-code+PKCE/device-code token providers, flow user identity into executor context (ClaimsPrincipal) and into task store scoping.

### 15. Identity / trust / authorization of agents (A in volume, but mostly proposals)
- What goes wrong: the most commented issues in the spec repo are all about agent identity, trust signals, delegation (OBO), skill-level authorization: A2A #1672 (658 comments), #1786 (249), #1829 (143), #1628 (125), #1716 (50), #2028 (36). No settled core solution; many are vendor proposals. Signing verification is only a SHOULD.
- Evidence: https://github.com/a2aproject/A2A/issues?q=is%3Aissue+sort%3Acomments-desc
- SDK opportunity: do not invent a trust model; provide hooks (authorization interceptors per skill, card-verification policy, actor-chain/OBO extension support when it settles). Mark inferred: product teams will ask for per-skill authz.

### 16. Extension negotiation (B)
- What goes wrong: opt-in is the `A2A-Extensions` header (comma-separated URIs); users used `x-a2a-extensions` or put URIs in `Message.metadata` and thought it was ignored (go #345, 11 comments). `Message.extensions` is only a marker. Required-extensions activation (tck #193). Agents must not silently fall back to older extension versions; must return `ExtensionSupportRequiredError`. Existing extension docs (x402) still use 0.3 shapes (llm4agents audit).
- Evidence: https://github.com/a2aproject/a2a-go/issues/345 ; https://github.com/a2aproject/a2a-tck/issues ; https://llm4agents.com/blog/a2a-v1-audit-breaking-changes
- SDK opportunity: typed extension registry on client and server; automatic header handling; required-extension enforcement and echo of activated extensions in response header.

### 17. Server custom methods / extensibility and custom cards (C)
- .NET #169 "Refactor to allow extension and custom methods" (open, 6), Go #129 "Method Extensions" (open). Opportunity (inferred): middleware pipeline around the request handler.

---

## Theme 4. Hosting model, framework integration, ops

### 18. Wrapping an LLM/agent framework as an A2A server (A, mostly inferred demand)
- Evidence of work being redone per framework: LangGraph needs `messages` state key, UUID thread ids, metadata set after deploy (https://docs.langchain.com/langsmith/server-a2a); open-source LangGraph lacks A2A models (https://github.com/langchain-ai/langgraph/issues/7398); community `a2a-langgraph` wrapper (https://pypi.org/project/a2a-langgraph/); samples break (LangGraph currency agent, https://github.com/a2aproject/a2a-samples/issues/722); sample demos have the most comment counts in a2a-samples (#30 task list empty, #48 502, #188 can't run). Microsoft Agent Framework fixed A2A message validation and sample server capabilities (https://github.com/microsoft/agent-framework/pull/9465).
- SDK opportunity: `IAgentExecutor` adapters for Microsoft.Extensions.AI `IChatClient`, Semantic Kernel / Agent Framework agents, with automatic mapping of messages <-> A2A Parts, task state transitions (working, input-required, completed), and streaming deltas -> status/artifact updates.

### 19. Using a remote A2A agent as a tool / sub-agent; manager-supervisor patterns (B)
- What goes wrong: a supervisor wants one child task per worker agent under one context. Java emitter is bound to one task; child tasks fail with taskId mismatch; the maintainer recommends remote delegation via the Client API (https://github.com/a2aproject/a2a-java/issues/1068). Client and server in same project causes DI/runtime problems (java #282, #893). Initiating client does not receive sub-agent artifacts unless forwarded (same #1068).
- Existing: ADK `RemoteA2aAgent`; MAF `A2AAgent` (https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/a2a); a2a-langgraph.
- SDK opportunity: `AsAIFunction()` / tool wrapper that turns a card's skills into tool definitions with wait-for-completion and input-required bubbling (inferred; no single issue states this).

### 20. Observability and trace propagation (B)
- What goes wrong: OpenTelemetry context lost across the thread hop between HTTP handler and executor (java #698, 14 comments, fixed in PR #707). Users cannot switch off or filter SDK spans (python #295; workaround: collector filter on scope `a2a-python-sdk`; telemetry moved to optional extra). W3C `traceparent` over A2A across hops (blog guidance, weak).
- Evidence: https://github.com/a2aproject/a2a-java/issues/698 ; https://github.com/a2aproject/a2a-python/issues/295
- SDK opportunity (.NET): `ActivitySource` with stable names, `Activity` flows into executor, `traceparent` injection on client, metrics (task duration, state counts), simple opt-out via standard OTel config.

### 21. Testing and mocking agents (B)
- Go proposal for a public `a2atest` package (go #310, open). TCK exists but has its own defects and gaps (a2a-tck issues: flaky tests, helper requires WORKING state which breaks sync agents - tck #146; 51 open issues). Official CLI wanted (A2A #1929, 65 comments).
- Evidence: https://github.com/a2aproject/a2a-go/issues/310 ; https://github.com/a2aproject/a2a-tck/issues ; https://github.com/a2aproject/A2A/issues/1929
- SDK opportunity: in-memory transport + fake agent/executor harness, `WebApplicationFactory` helpers, TCK runner in CI, recorded-card fixtures.

### 22. DI/host integration and lifetime problems (B)
- Java Quarkus build-time injection failure for unused beans (java #213), request-scope not active (java #893), HTTP/1.1 support request (java #270, 21 comments), Android client (java #793), JS hard dependency on Express (js #55) and missing export (js #94). .NET: method naming (Async suffix, #88), `HttpClient` lifetime/socket exhaustion advice (#107).
- SDK opportunity: no hard dependency on one web host; `AddA2AClient(IHttpClientFactory)`, `MapA2A()`; Native AOT support; no static singletons.

### 23. MCP interop and architecture confusion (C, weak sources)
- Blog consensus: MCP for tools, A2A for delegation (https://mirlohi.com/articles/mcp-vs-a2a-2026 , https://dev.to/chunxiaoxx/a2a-vs-mcp-in-2026-the-layered-architecture-production-ai-teams-actually-need-45jm); adoption claims (97M MCP downloads vs ~150 production A2A deployments) are unsourced self-reports (https://www.glukhov.org/ai-systems/comparisons/a2a-protocol-2026-adoption/). I found no specific bug threads on MCP bridging. Inferred: bridging helper (A2A skill <-> MCP tool) is a likely boilerplate item.

### 24. Spec/sample quality that misleads integrators (B)
- Spec examples wrong: `final` in status event (A2A #1433), missing `contextId` in SSE example (#1683), invalid canonicalization example (#2318), wrong migration field names (PR #2158). Samples broken by retired models/deps (a2a-samples #722, #154 protobuf version error, #217 import error). TCK vendored spec pinned to 1.0.0 while spec is 1.0.1 (tck #231, #240).
- SDK opportunity: ship conformance tests with the SDK, link each behavior to spec section, treat spec ambiguities in a "known deviations" doc.

### 25. Registry / discovery ecosystem health (C)
- A2A #1755 (shaky data): many registry-listed agents unreachable, mixed protocol variants. Opportunity (inferred): client-side health checks, version probing, strict failure messages.

---

## Ranking summary (by evidence)
1 Breaking changes/silent failures 0.3->1.0 (A)
2 Serialization drift across SDKs (A)
3 Task/context id handling in multi-turn (A)
4 Wait-for-completion / long-running / non-blocking (A)
5 Persistent store + push notification delivery/auth (A)
6 Client auth and per-request headers (A)
7 Streaming robustness and backpressure (B)
8 Agent card fetch/per-request cards/ingress (B)
9 Signed card canonicalization (B)
10 Version negotiation (B)
11 Extension negotiation (B)
12 Observability propagation (B)
13 Error model mapping (B)
14 Binding/tenant/URL differences (B)
15 Framework wrapping and remote-agent-as-tool (B/inferred)
16 Testing/mocking (B)
17 Spec/sample inaccuracies (B)
18 Identity/trust/authz (A volume, unresolved in spec)
19 Cancellation (B)
20 Artifact chunking, MCP bridging, registry health (C/inferred)

## Notes on gaps
- I did not find a first-hand production postmortem from AWS, Azure AI Foundry, or CrewAI. Vendor/consultancy blogs found are generic. Treat them as weak.
- a2a-dotnet-specific signals worth a look: #107 (per-request headers), #353 (per-request cards), #169 (custom methods), #138 (kind serialization), #88 (naming), #105 (card route).
