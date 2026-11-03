# SDK package 10: Production client

- Date: 2026-11-03
- Status: approved by owner 2026-11-03
- Milestone: M1 (`1.0.0-preview`)
- Size: L

## Intent

Today the client is a correct, thin wrapper over the 11 operations. Everything a production caller needs on top is left to each team:
- DI registration and connection pooling
- retries that are safe for A2A
- timeouts that do not kill long blocking calls
- per-user headers
- auth refresh
- waiting until a task is done
- stream resume
- multi-turn task and context ids
- safe card fetching
- limits on what a hostile agent can send back

The sample client shows how much code that is (`samples/AgentClient/FlowRunner*.cs`).

This package turns the client into the piece a product team registers once and calls in a few lines. The hard, repeated work moves into the SDK: wait for completion, resume, conversation ids, auth retry and step-up. It also keeps the two promises that make a client safe: bounded reads of untrusted responses, and no credential going where a card says it should go.

## Findings addressed

- R27: no DI, no `IHttpClientFactory`, no resilience, no timeout option, no 401 refresh. `SocketsHttpHandler` per client.
- R28: no stream reconnect, no "send and wait" helper, no input-required or auth-required handling.
- R29: fixed 5-minute card cache, Cache-Control and ETag ignored, no size limit, no DI.
- R30: SSE, unary and gRPC responses read without bounds.
- R31 (client part): `ClientCertificate` ignored with a supplied `HttpClient`, and no provider chosen from card schemes. Entra providers are package 07.
- R32: public transport classes skip tracing, `TracingA2AClient.Inner` is public, no convenience overloads, `A2AAuthException` carries an HTTP status for gRPC, transient and permanent errors are not told apart.
- R7 (client part): gRPC and Azure.Identity on every client consumer.
- R9 (client part): interface choice ignores `protocolVersion` (selection rules are package 03; this package owns the transport factory seam).
- Package 14, debt PD3: the SSE reader allocates a string per line and a `StringBuilder` per event, then converts strings back to bytes (`src/A2A.Client/Transports/Sse/SseReader.cs:17-38`, `JsonRpcA2AClient.cs:113`, `RestA2AClient.cs:195`). The `PipeReader` rewrite here closes it.
- Pain point 9, "Waiting for completion / long-running tasks / non-blocking send".
- Pain point 8, "Task and context ID handling in multi-turn conversations" (client side).
- Pain point 14, "Authentication and per-request credentials on the client" (per-user headers, dotnet #107; the card is needed to build the pipeline).
- Pain point 4, "Agent Card fetching, discovery, and per-request cards" (probe both paths, caching, h2c surprises).
- Pain point 10, "Streaming (SSE) robustness" (reconnect, end-of-stream semantics, idle proxies).
- Pain point 11, "Artifact chunking and ordering" (assembly helper).
- Pain point 22, "DI/host integration and lifetime problems" (`HttpClient` lifetime, no static singletons).

## Goals

1. `services.AddA2AClient("billing", o => o.AgentUrl = new("https://billing.contoso.com"))` registers a named client on `IHttpClientFactory`. Consumers get it as `[FromKeyedServices("billing")] IA2AClient`, or from `IA2AClientFactory.CreateClient("billing")`. No socket churn when it is resolved per request (a test counts connections).
2. Without DI, a client call is at most 5 lines: `await using var client = await A2AClient.ConnectAsync(url); var r = await client.SendTextAsync("hi");`.
3. Retries never resend a `SendMessage` the server may have processed, unless the caller opts in. Idempotent operations and connection failures before the request was sent are retried with backoff and a circuit breaker. Tested with a fault-injecting handler.
4. A blocking `SendMessage` is not cut by the 100 s `HttpClient` default. Every operation has its own timeout, and every timeout says which option to raise. Tested.
5. Per-request headers, extensions and credentials can be set on each call without new overloads per operation. Interceptors see and change every call on every transport. Tested.
6. A 401 triggers one credential refresh and one retry. A `WWW-Authenticate` claims challenge reaches the credential provider. An `AUTH_REQUIRED` task calls a step-up hook that may continue the task with other credentials. Tested.
7. `SendAndWaitAsync` returns when the task is terminal or interrupted, or when the timeout is hit. It picks stream, poll or push (or blocking send) on its own, resumes dropped streams, and falls back to polling. Tested on all three transports, including a mid-stream drop.
8. `A2AConversation` keeps the context id and the open task id across turns by the spec rules, so callers never handle ids. Tested against the booking flow (input-required → continue → completed).
9. The card resolver honors Cache-Control and ETag, bounds size and cache entries, and probes `agent-card.json` and then legacy `agent.json` only on 404. It runs package 04's verifier on the raw bytes, and it gives a clear message for 0.3 cards. Tested.
10. No response from an agent can make the client buffer more than the configured limits: unary body, SSE event, gRPC message, card. Tested with oversized responses on each path.
11. `ArtifactAssembler` rebuilds artifacts from `append` and `lastChunk` updates, and from task snapshots after a resume. Tested.
12. gRPC is opt-in (package 01): it lives in `Ambinder.A2A.Client.Grpc`, with one channel per agent address, reused, bounded and closed with the client. A card that offers only gRPC, without that package, fails with a message naming the package.
13. The client core references no `Grpc.*`, `Google.Protobuf` or `Azure.*` package (package 01 allow-list test).

## Non-goals

- Interface and version selection rules, `A2A-Version` per interface, ErrorInfo mapping, and the tenant rule. Those are package 03. This package implements its hooks and keeps the behavior 03 defines.
- The 0.3 transport and the 0.3 card converter. That is package 15. It plugs into the seams defined here.
- Card signature canonicalization and JWS verification. That is package 04. This package calls `IAgentCardVerifier`.
- Entra token acquisition, workload identity and OBO. That is package 07 (`Ambinder.A2A.Client.Entra`).
- Webhook receiving and verification. That is package 08 (`Ambinder.A2A.Push.Receiver`). This package defines `IA2APushWaiter` and `PushSubscription`; the receiver implements the waiter *(Claude's call, B10)*.
- Using a remote agent as an LLM tool (`AIFunction`). That is package 11, which builds on the wait helper. Its id binding and app-identity default (B9) live there.
- Fake clients for tests. That is package 12 (`FakeRemoteAgent`). This package names the seam it replaces.
- Service discovery and registries beyond a URL.

## Anti-goals

- A retry policy that turns one user request into two tasks on the agent.
- A client that is safe only if the team remembers to set five options. Defaults must be safe for calling a third-party agent.
- A wait helper that hangs forever, or swallows cancellation, or silently cancels the remote task when the caller's token fires.
- Hidden static state that breaks with several hosts or tests in one process (shared handlers are fine; shared mutable caches without bounds are not).
- A way to call an agent that skips tracing (today you can construct a transport class directly, R32).
- Sending a credential, or a token whose scope or URL came from the card, without an allow-list.
- Message content, tokens or headers with secrets in logs or spans.

## Constraints

- Spec v1.0.1 (from `research-spec.md`):
  - Client MUST send `A2A-Version: Major.Minor` (3.6).
  - Client MUST pick the first supported interface, use its URL, and set `tenant` exactly to its tenant (section 8; package 03 D8).
  - Blocking `SendMessage` waits until terminal or interrupted. `returnImmediately: true` returns at once.
  - A stream is either one `Message` then close, or `Task` first, then events, closing at terminal or interrupted. Close is the only end signal; there is no `final` flag.
  - `SubscribeToTask` sends the current task first. Subscribing to a terminal task is `UnsupportedOperation`. No replay of missed events is defined (open issue #1635).
  - Cancel is idempotent. `SendMessage` MAY dedupe by `messageId`, so a resend is not safe by default.
  - Card: well-known path `/.well-known/agent-card.json`. Clients SHOULD honor RFC 9111 and send `If-None-Match`. They SHOULD replace the public card with the extended card for the session. They SHOULD verify signatures when present.
  - Messages are not reliable delivery: outputs are artifacts.
- Proto: `TaskArtifactUpdateEvent` has `artifact`, `append`, `last_chunk`, `metadata`. There is no index field (`proto/a2a.proto:308-322`). Ordering is stream order.
- Package 01: `Ambinder.A2A.Client` depends on Protocol, `Microsoft.Extensions.{Http,Options,DependencyInjection,Logging}` and `Microsoft.Extensions.Http.Resilience`. gRPC is in `Ambinder.A2A.Client.Grpc`, Azure.Identity in `Ambinder.A2A.Client.Entra`. No `InternalsVisibleTo` between shipped assemblies.
- Package 03: no per-request tenant override. The client tenant is the selected interface's tenant. A request with a different tenant throws `ArgumentException`. The `ProtocolVersion` option is removed; the version comes from the interface.
- Package 07: credential providers implement this package's `IA2ACredentialProvider`. Scopes and token URLs are never taken from a card without an allow-list.
- Principle 3: a client call in about 5 lines, and nothing to configure for local development.

## Design

### Package layout

| Package | Contents |
|---|---|
| `Ambinder.A2A.Client` | `IA2AClient`, `A2AClient` (static connect), `IA2AClientFactory`, `AddA2AClient`, JSON-RPC and REST transports (internal), `IA2ATransportFactory`, `IAgentCardReader`, `A2ACardResolver`, interceptors, credential provider contract, the A2A resilience handler, `SendAndWaitAsync`, `A2AConversation`, `ArtifactAssembler`, bounded readers, the push-wait abstractions `IA2APushWaiter` and `PushSubscription` (B10) |
| `Ambinder.A2A.Client.Grpc` | gRPC transport factory, channel cache. `AddGrpcTransport()` on the client builder |
| `Ambinder.A2A.Client.Entra` | package 07 |

`Ambinder.A2A.Client` depends on `Microsoft.Extensions.Http.Resilience`; package 01's map and allow-list include it. It is a Microsoft.Extensions package (principle 2).

### API rules (package 02)

- `AddA2A...`, `Add...Entra...` and other registration extension methods live in `namespace Microsoft.Extensions.DependencyInjection`. `MapA2A...` methods live in `Microsoft.AspNetCore.Builder`.
- Public interfaces in this package stay small. After 1.0, any new member on them gets a default implementation, so existing implementations keep compiling.
- `IA2AClient` is mocked by users, so convenience calls are extension methods, never interface members (package 02).

### Public surface

```csharp
public interface IA2AClient : IAsyncDisposable
{
    AgentCard Card { get; }                                   // the card in use (extended card once fetched)
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, A2ARequestOptions? options = null, CancellationToken ct = default);
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, A2ARequestOptions? options = null, CancellationToken ct = default);
    Task<AgentTask> GetTaskAsync(GetTaskRequest request, A2ARequestOptions? options = null, CancellationToken ct = default);
    // ... the other 8 operations, same pattern (one optional options parameter, ct last)
}
```

- *(writer's call)* One optional `A2ARequestOptions` parameter on every operation. This settles dotnet #107 without overload explosion, and it keeps `CancellationToken` last (CA1068).
- `A2AClient` is the only implementation the SDK hands out. Tracing, interceptors, resilience, limits and auth always apply. Transport classes (`JsonRpcA2AClient`, `RestA2AClient`, `GrpcA2AClient`) become internal. `TracingA2AClient` and its `Inner` go away (R32).
- Convenience members are extension methods on `IA2AClient`, so mocks stay small: `SendTextAsync(string text, ...)`, `GetTaskAsync(string taskId, ...)`, `CancelTaskAsync(string taskId, ...)`, `SubscribeAsync(string taskId, ...)`, `SendAndWaitAsync(...)`, `StartConversation(...)`.
- Response metadata: `A2AClient.LastResponse` is not used (it is not thread-safe). Instead, `A2ARequestOptions.OnResponse` (an `Action<A2AResponseInfo>`) and the stream wrapper expose `A2AResponseInfo`: `ActivatedExtensions` (from the `A2A-Extensions` response header or gRPC metadata, package 03 D5), `Transport`, `ProtocolVersion`, `HttpStatus` or `GrpcStatus`, `Attempts`. *(writer's call)*

```csharp
public sealed class A2ARequestOptions
{
    public IDictionary<string, string> Headers { get; }          // extra headers / gRPC metadata for this call
    public IReadOnlyList<string>? Extensions { get; init; }      // replaces the client default list for this call
    public TimeSpan? Timeout { get; init; }                      // overrides the per-operation default
    public IA2ACredentialProvider? Credentials { get; init; }    // per-user credentials for this call
    public string? UserAssertion { get; init; }                  // for OBO providers (package 07)
    public string? CredentialCacheKey { get; init; }             // identity key for per-call Credentials; enables the extended card (B11)
    public bool? AllowSendRetry { get; init; }                   // see Resilience
    public Action<A2AResponseInfo>? OnResponse { get; init; }
    public IDictionary<string, object?> Items { get; }           // passed to interceptors
}
```

There is no `Tenant` member (package 03 D8). Reserved headers (`A2A-Version`, `A2A-Extensions`, `Authorization` when a credential provider is set, `Content-Type`) cannot be set through `Headers`: `ArgumentException` naming the header and the right option.

### Registration and DI

```csharp
public static IA2AClientBuilder AddA2AClient(this IServiceCollection services, string name, Action<A2AClientOptions> configure);
public static IA2AClientBuilder AddA2AClient(this IServiceCollection services, string name, IConfiguration section);

public interface IA2AClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
    IHttpClientBuilder HttpClient { get; }                       // the named HttpClient behind this A2A client
    IA2AClientBuilder AddInterceptor<T>() where T : class, IA2AInterceptor;
    IA2AClientBuilder AddCredentials(Func<IServiceProvider, IA2ACredentialProvider> factory);
    IA2AClientBuilder AddTransport<T>() where T : class, IA2ATransportFactory;   // Client.Grpc and Compat use this
}

public interface IA2AClientFactory
{
    IA2AClient CreateClient(string name);                        // cheap; the card is fetched lazily on first call
    ValueTask<IA2AClient> CreateClientAsync(string name, CancellationToken ct = default); // fetches the card now
}
```

`A2AClientOptions` (bound from `A2A:Clients:{name}` by convention):

| Option | Default | Meaning |
|---|---|---|
| `AgentUrl` | required (or `Card`) | The agent base URL or card URL. |
| `Card` | null | A fixed card. No fetch when set. |
| `PreferredBindings` | `[]` | Order of preference (`JSONRPC`, `HTTP+JSON`, `GRPC`). Package 03 selection rules apply. |
| `Extensions` | `[]` | Default `A2A-Extensions` list. |
| `ClientCertificate` | null | mTLS. Applied to the named client's primary handler, so it also works under DI (fixes R31's "ignored" case). |
| `Timeouts` | see Timeouts | |
| `Resilience` | see Resilience | |
| `Limits` | see Bounded reads | |
| `Card*` | see Card resolver | Resolver options for this client. |
| `CardReaders` | `[]` | Extra `IAgentCardReader`s, for non-DI callers and per-client additions. *(Added in the plan review, 2026-11-03; Claude's call.)* |
| `UseExtendedCard` | `Auto` | `Auto`: after the first authenticated call, if `capabilities.extendedAgentCard` is true, fetch the extended card and use it (spec SHOULD). It is cached per credential identity (B11). `Never` or `Always`. |

Behavior:
- Each named client gets a named `HttpClient` (`"A2A:{name}"`). The primary handler is `SocketsHttpHandler` with `PooledConnectionLifetime = 2 min`, `UseCookies = false`, `AllowAutoRedirect = false`, and `AutomaticDecompression = All`. `HttpClient.Timeout = Infinite`, because the SDK applies its own per-operation timeouts.
- `AddA2AClient` also registers the keyed `IA2AClient` (keyed singleton by name). The client object is cheap and thread-safe. The card is fetched on the first call, through the same named `HttpClient`. This solves "the card is needed to build the pipeline" (pain point 14): nothing in the pipeline needs the card until a call runs.
- Package 12's seam: `A2AClientOverride(string Name, Func<IServiceProvider, IA2AClient> Create)` registrations are checked first by `IA2AClientFactory` and by the keyed registration. `AddFakeA2AClient` adds one. *(writer's call)*
- No DI: `A2AClient.ConnectAsync(Uri url, Action<A2AClientOptions>? configure = null, CancellationToken ct = default)` builds the same pipeline around one process-wide `SocketsHttpHandler` (static, never disposed, `PooledConnectionLifetime = 2 min`). It disposes only its own per-client state. `A2AClient.Create(AgentCard card, ...)` does the same without a fetch. The old `A2AClientFactory.Create(card, options)` (`src/A2A.Client/A2AClientFactory.cs:18-46`) becomes `A2AClient.Create`.

### Transport factory and card reader seams

```csharp
public interface IA2ATransportFactory
{
    IReadOnlyList<string> Bindings { get; }                 // e.g. ["JSONRPC", "HTTP+JSON"]
    IReadOnlyList<string> ProtocolVersions { get; }         // Major.Minor, e.g. ["1.0"]; package 15 adds ["0.3"]
    IA2ATransport Create(AgentInterface selected, AgentCard card, A2ATransportContext context);
}

public interface IA2ATransport : IAsyncDisposable          // internal contract surfaced for 15 and Client.Grpc
{
    ValueTask<A2ATransportResponse> SendAsync(A2ATransportRequest request, CancellationToken ct);     // unary
    IAsyncEnumerable<StreamResponse> StreamAsync(A2ATransportRequest request, CancellationToken ct);  // streaming
}

public interface IAgentCardReader
{
    /// Tries to read raw card JSON into a 1.0 AgentCard. Returns false when this reader does not apply.
    bool TryRead(ReadOnlySpan<byte> json, Uri source, [NotNullWhen(true)] out AgentCard? card);
}
```

- The `A2AClient` wrapper does everything around the transport: interceptors, credentials, auth retry, resilience marking, timeouts, tracing, limits and tenant. A transport only maps requests to the wire and back. That is why package 15's 0.3 transport gets all of it unchanged.
- `A2ATransportContext` gives the transport the named `HttpClient` (or handler factory), the limits, and the logger.
- Interface selection (package 03 D2): the supported (binding, version) pairs are the union over registered factories. Selection failure messages name the missing package:
  - "The agent offers only GRPC; add Ambinder.A2A.Client.Grpc and call .AddGrpcTransport()."
  - "The agent offers only protocol 0.3; add Ambinder.A2A.Compat.V0_3 and call .AddV0_3Support()."
- Card readers run in order. The core reader handles 1.0 cards. If no reader accepts and the JSON has `url` but no `supportedInterfaces`, the core throws package 15's message: "This agent card looks like A2A 0.3 (it has `url` but no `supportedInterfaces`). Add the Ambinder.A2A.Compat.V0_3 package and call AddV0_3Support()."

### Interceptors

```csharp
public interface IA2AInterceptor
{
    ValueTask OnSendingAsync(A2AClientCallContext call, CancellationToken ct);                 // before each attempt
    ValueTask OnCompletedAsync(A2AClientCallContext call, A2ACallOutcome outcome, CancellationToken ct); // after the last attempt
}

public sealed class A2AClientCallContext            // client side; distinct from the server's A2ACallContext
{
    public string Method { get; }                 // "SendMessage", ...
    public object Request { get; }                // the request record (read-only use)
    public A2ARequestHeaders Headers { get; }     // mutable; same names on HTTP and gRPC
    public AgentCard Card { get; }
    public AgentInterface Interface { get; }
    public A2ARequestOptions? Options { get; }
    public int Attempt { get; }
    public IDictionary<string, object?> Items { get; }
}
```

- Before and after hooks, as in the JS SDK (`research-sdks.md` JS section). *(writer's call: one shape for unary and streaming. For streams, `OnCompletedAsync` runs when the stream ends or fails.)*
- The type is `A2AClientCallContext`, not `A2ACallContext`: the server already has `A2ACallContext` (03, 07, 09), and package 11's AI bridge references both packages. *(Reconciled: one name, one type.)*
- Interceptors run in registration order on sending and in reverse order on completion. Credentials run after all `OnSendingAsync` hooks, so an interceptor cannot read the `Authorization` value (`Headers` hides it).
- Standard `DelegatingHandler`s on `IA2AClientBuilder.HttpClient` still work for JSON-RPC and REST. The docs say interceptors are the transport-neutral choice.

### Credentials and auth retry

```csharp
public interface IA2ACredentialProvider
{
    ValueTask ApplyAsync(A2ACredentialContext context, CancellationToken ct);
}

public sealed class A2ACredentialContext
{
    public AgentCard Card { get; }
    public AgentInterface Interface { get; }
    public string Method { get; }
    public A2ARequestHeaders Headers { get; }     // the provider sets Authorization or other auth headers here
    public A2ARequestOptions? Options { get; }    // per-user data, UserAssertion
    public bool ForceRefresh { get; }             // true on the retry after a 401
    public AuthChallenge? Challenge { get; }      // parsed WWW-Authenticate of the 401 (scheme, error, claims, scope, resource_metadata)
}
```

- This replaces `GetHeadersAsync(AgentCard, ct)` (`src/A2A.Client/Auth/IA2ACredentialProvider.cs:6-9`).
- Provider order: `A2ARequestOptions.Credentials`, then the client's provider. At most one applies per call.
- `DelegateCredentialProvider` (core) wraps a `Func<A2ACredentialContext, CancellationToken, ValueTask>`. The API key and generic OAuth providers move to samples (package 07).
- `ApplyAsync` runs once per attempt. Tokens are therefore fresh on retries and on stream resumes.
- On HTTP 401 or gRPC `UNAUTHENTICATED`, when a provider exists and the attempt was not already an auth retry: parse `WWW-Authenticate`, call `ApplyAsync` again with `ForceRefresh = true` and the `Challenge`, and retry once.
  - This is safe for every operation, `SendMessage` included, because the server rejects before the handler (package 07 error table).
  - For streams, it happens only before the first event.
  - A second 401 throws `A2AAuthException` with `Kind = Unauthenticated`, the challenge's `error` and `error_description`, and the hint "check the scope and audience configured for client '{name}'".
- 403 or `PERMISSION_DENIED` is never retried. It throws `A2AAuthException` with `Kind = Forbidden`.
- `A2AAuthException` gets `Kind` (`Unauthenticated`, `Forbidden`) and nullable `HttpStatus` and `GrpcStatus`, instead of an HTTP-only status (R32). Package 02 owns the final shape.

Card-scheme selection *(writer's call)*: a provider may also implement `IA2ACredentialSelector.CanSatisfy(string schemeName, SecurityScheme scheme)`. When a client has several providers (`AddCredentials` called more than once), the client walks the card's `securityRequirements` in order and uses the first provider that can satisfy a whole requirement. If none can: `A2AAuthException` "Agent 'x' requires one of [entra, apiKey]; registered providers satisfy none."

### `AUTH_REQUIRED` step-up hook

```csharp
public interface IA2AAuthRequiredHandler
{
    /// Called when a waited task enters AUTH_REQUIRED. Return a continuation to resume the task, or null to stop.
    ValueTask<AuthContinuation?> HandleAsync(AuthRequiredContext context, CancellationToken ct);
}
// No positional constructors (spec 02 §4; owner rule 2026-11-03: the six package 05 key structs are the only positional public types).
public sealed record AuthRequiredContext
{
    public required IA2AClient Client { get; init; }
    public required AgentTask Task { get; init; }
    public Message? StatusMessage { get; init; }
}
public sealed record AuthContinuation
{
    public required Message Message { get; init; }
    public A2ARequestOptions? Options { get; init; } // may carry other credentials
}
```

- It is used by `SendAndWaitAsync` and `A2AConversation`. It is registered per client (`builder.OnAuthRequired<T>()`) or passed in `A2AWaitOptions`.
- The SDK sends the continuation message on the same task and context ids, with the given options. That is how a named identity continues the task (package 07's grant rule). After `MaxAuthSteps` (default 3), the wait returns the task in `AUTH_REQUIRED`.
- After a step-up continuation *(Claude's call, B6)*: the grant holder gets the response of its own continuation call, but cannot Get, List, Subscribe or Cancel the task afterwards (07). So the wait helper stays on the continuation's own stream (or blocking result). If it must resubscribe or poll after that, it uses the original caller's credentials (the client's provider or the original call's options), never the continuation's `Options`.
- Default (no handler): the wait returns with `State = AuthRequired` and the status message. Credentials stay out of band, as the spec says.
- `IA2AInputRequiredHandler` has the same shape for `INPUT_REQUIRED` (its context and continuation records also use `required` / `init` properties). Default: return to the caller.

### Resilience

The problem: JSON-RPC sends every operation as POST, so a generic HTTP retry handler would resend `SendMessage`.

The design:
- The client marks each request with an `HttpRequestOptions` key `A2ARetrySafety`:
  - `Idempotent`: GetTask, ListTasks, CancelTask (idempotent per spec), all push-config operations (Delete is idempotent per spec; Create with a client-set id is an upsert), GetExtendedAgentCard, card fetch, SubscribeToTask before the first event.
  - `NotSafe`: SendMessage and SendStreamingMessage, unless `AllowSendRetry` is set on the client or the call. The rule follows package 05's dedupe (05 §7.10): our server dedupes new-task sends by `(tenant, agent, owner, messageId)` by default and returns the existing task; it never dedupes a continuation; other SDKs' servers may not dedupe at all (spec MAY). So:
    - `AllowSendRetry` defaults to `false`, because the client cannot know whether the server dedupes. Our server dedupes by `messageId` by default (05), so `AllowSendRetry` is safe against `Ambinder.A2A` servers; it stays opt-in for every server, and there is no detection *(Claude's call, C11)*.
    - With `AllowSendRetry`, only a send without `taskId` (a new task) is retried, and the retry resends the same request with the same `messageId`. A continuation is never retried after bytes were sent, even with the opt-in.
    - The docs say: set `AllowSendRetry` for agents built on `Ambinder.A2A` with default settings, or other agents known to dedupe.
  - Any request is retry-safe if it failed before any byte was sent (connect refused, DNS failure, TLS failure).
- `builder.AddA2AResilience(o => ...)` (on by default in `AddA2AClient` and `ConnectAsync`) adds a `Microsoft.Extensions.Http.Resilience` pipeline whose `ShouldHandle` honors that key. *(writer's call)*

| Setting | Default |
|---|---|
| Retry attempts | 3 (retry-safe requests only), exponential with jitter from 500 ms, honors `Retry-After` up to 30 s |
| Retry on | connection errors, 408, 429, 500, 502, 503, 504 |
| Circuit breaker | per named client: 50 % failures over 30 s, at least 20 calls, break 15 s. An open circuit throws `A2ATransportException` with `IsTransient = true` and "circuit open for agent 'x'". |
| Attempt timeout | the operation timeout (below) |

- Package 09's overload errors are retry-safe for every operation, `SendMessage` included, because the server rejects them before the handler: ErrorInfo reason `SERVER_BUSY` (HTTP 503) and `RATE_LIMITED` (HTTP 429 or gRPC `RESOURCE_EXHAUSTED`) are retried after `Retry-After`. `SERVER_SHUTTING_DOWN` on a stream triggers a resume (`SubscribeToTask`), not a resend. Package 06's stream reasons `TASK_STREAM_INTERRUPTED` and `SUBSCRIBER_TOO_SLOW` do the same in `SendAndWaitAsync`, `A2AConversation` and `StreamWithResumeAsync`.
- Teams who call `AddStandardResilienceHandler()` on `builder.HttpClient` themselves get a startup warning that it may resend `SendMessage`. *(writer's call)*
- gRPC uses a gRPC service-config retry policy with the same rules: idempotent methods only, codes `UNAVAILABLE` and `RESOURCE_EXHAUSTED`. The circuit breaker is applied in the `A2AClient` wrapper for gRPC.
- Errors (R32): a non-A2A HTTP failure maps to `A2ATransportException : A2AException` with `IsTransient`, `HttpStatus` or `GrpcStatus`. 5xx, 408, 429 and network errors are transient. Other 4xx without an A2A error body are permanent. A2A protocol errors stay `A2AException` with `Code` and `Details` (package 03).

### Timeouts

| Operation | Default (`A2AClientOptions.Timeouts`) |
|---|---|
| Unary operations other than blocking send | 30 s |
| `SendMessage` with `returnImmediately: true` | 30 s |
| `SendMessage` blocking | 10 min |
| Streams (send or subscribe) | no total limit; `StreamIdleTimeout` 5 min with no bytes (heartbeats count) |
| Card fetch | 10 s |
| `SendAndWaitAsync` total | 30 min (`A2AWaitOptions.Timeout`) |

- A timeout throws `A2ATimeoutException : A2AException`. The message names the operation, the elapsed time and the option to raise, for example "SendMessage (blocking) timed out after 10 min; raise A2AClientOptions.Timeouts.BlockingSend or use SendAndWaitAsync."
- Caller cancellation throws `OperationCanceledException`, never `A2ATimeoutException`.
- Idle streams: package 09's server heartbeats are comments. The reader counts any bytes as activity.

### Wait for completion

```csharp
Task<A2AResult> SendAndWaitAsync(this IA2AClient client, SendMessageRequest request,
    A2AWaitOptions? wait = null, A2ARequestOptions? options = null, CancellationToken ct = default);
Task<A2AResult> SendAndWaitAsync(this IA2AClient client, string text, ...);   // convenience
Task<A2AResult> WaitAsync(this IA2AClient client, string taskId, A2AWaitOptions? wait = null, ...); // for an existing task
```

`A2AWaitOptions`:

| Option | Default | Meaning |
|---|---|---|
| `Mode` | `Auto` | `Auto`, `Stream`, `Poll`, `Push`, `Blocking`. |
| `Timeout` | 30 min | Total wait. |
| `PollInitialInterval` | 1 s | |
| `PollMaxInterval` | 30 s | Backoff factor 1.5 with jitter. |
| `MaxResumes` | 5 | Stream resumes before falling back to polling. |
| `ReturnOnInterrupted` | true | Return on `INPUT_REQUIRED` or `AUTH_REQUIRED` after the handlers decline. |
| `CancelRemoteOnTimeout` | false | Send `CancelTask` when `Timeout` fires. |
| `CancelRemoteOnCallerCancel` | false | Send `CancelTask` (best effort, 5 s) when `ct` fires. False by default, because a caller's cancel is often a shutdown, and the remote work should go on *(Claude's call, C11)*. |
| `OnUpdate` | null | `Func<StreamResponse, ValueTask>`, called for each event or poll change. |
| `PushWaiter` | null | `IA2APushWaiter` (defined here, implemented by package 08's receiver, B10), for `Push` mode. |
| `PushWebhookUrl` | null | The receiver URL, for `Push` mode. |
| `AuthRequired` and `InputRequired` | null | The handlers above. |

`Auto` picks:
1. If the card says `streaming: true`: `Stream`.
2. Else: `Poll`.

`Push` is never picked automatically, because it needs a receiver URL.

The modes:
- **Stream.**
  - Call `SendStreamingMessage`. A `Message` first means a direct reply: return it.
  - If the stream ends without a terminal or interrupted state: wait with backoff, then `SubscribeToTask(taskId)`. The first event there is the task snapshot, which replaces local state, and artifacts are re-synced from it.
  - Repeat up to `MaxResumes`, then switch to `Poll`.
  - `UnsupportedOperation` on subscribe means the task is terminal: do one `GetTask` and return it.
  - A drop before the first event (no task id yet) throws `A2AStreamInterruptedException`: "the stream ended before the agent returned a task; the message may or may not have been accepted." The send is not retried unless `AllowSendRetry` is set.
- **Poll.** `SendMessage` with `returnImmediately: true`, then `GetTask` with backoff until the task is terminal or interrupted. History is not requested (`historyLength: 0`) except on the last read. *(writer's call)*
- **Push.**
  - Create the push config inside the send request (`configuration.taskPushNotificationConfig`), using `PushSubscription.CreateNew` (defined here, B10; it records the agent base URL so the receiver can verify a push signature, 08), and register the subscription with the waiter.
  - Wait for a terminal or interrupted notification.
  - As a safety net, poll every `PollMaxInterval`, because push is at-least-once and the receiver may be down.
- **Blocking.** One blocking `SendMessage` with the blocking timeout.

`A2AResult`:
- `Task` (`AgentTask?`) and `Message` (`Message?`): exactly one is set.
- `State`, `IsTerminal`, `IsInterrupted`, `StatusMessage`.
- `Artifacts`, assembled.
- `Text`: the text parts of the reply message, or of the artifacts.
- `Events`: the events seen, only when `KeepEvents` is set.

Unknown task states from newer agents read as `Unspecified` (package 03) and keep the wait going.

### Stream resume for callers who stream themselves

`client.StreamWithResumeAsync(request, A2AStreamOptions? options, A2ARequestOptions? requestOptions, ct)` yields the same events as `SendStreamingMessageAsync`, plus `A2AStreamEvent.Resumed` markers. It resumes through `SubscribeToTask` as above. The raw `SendStreamingMessageAsync` keeps today's behavior: it throws when the stream ends before a final event (`src/A2A.Client/Transports/ClientSupport.cs:142-143`).

### Multi-turn conversations

```csharp
public sealed class A2AConversation
{
    public string? ContextId { get; }
    public string? TaskId { get; }                 // the open (non-terminal) task, if any
    public A2AConversationState State { get; }     // serializable record (ContextId, TaskId)
    public Task<A2AResult> SendAsync(string text, A2AWaitOptions? wait = null, A2ARequestOptions? options = null, CancellationToken ct = default);
    public Task<A2AResult> SendAsync(Message message, ...);
    public void Reset();                           // forget both ids
}
// client.StartConversation(A2AConversationState? resume = null)
```

Rules (pain point 8; spec 3.4):
- The first send has no ids. The server mints the context id and the task id. The client never invents ids.
- After each result: keep `ContextId`. Keep `TaskId` only if the task is non-terminal (`INPUT_REQUIRED`, `AUTH_REQUIRED`, `WORKING`, `SUBMITTED`). Clear it on a terminal state.
- The next send uses `ContextId` and, if set, `TaskId`. A message whose `taskId` or `contextId` is set by the caller → `ArgumentException` (the conversation owns ids).
- `TaskNotFound` on a continuation (for example the task was purged) clears `TaskId` and rethrows with the hint "start a new turn with conversation.SendAsync again".
- One send at a time: a concurrent call throws `InvalidOperationException`.
- A conversation is bound to one client. Switching agents means a new conversation.
- `State` can be saved (for example in a web session) and passed to `StartConversation(resume)`.

### Card resolver

`A2ACardResolver` becomes a DI service (`IA2ACardResolver`), with one shared cache per service provider.

| Option (`A2ACardResolverOptions`) | Default |
|---|---|
| `HonorHttpCaching` | true: `max-age`, `no-store`, `no-cache`, and `ETag` with `If-None-Match` revalidation |
| `DefaultTtl` | 5 min (no caching headers) |
| `MaxTtl` | 1 h |
| `MaxCardBytes` | 1 MiB |
| `MaxCacheEntries` | 1,000 (LRU) |
| `Timeout` | 10 s |
| `ProbeLegacyPath` | true: on 404 from `/.well-known/agent-card.json`, try `/.well-known/agent.json` once |
| `RequireHttps` | true, except loopback hosts *(writer's call)* |
| `HttpVersion` | 1.1, `RequestVersionOrLower` (no h2c upgrade, pain point 4) |

- A URL ending in `.json` is used as is (today's rule, `src/A2A.Client/A2ACardResolver.cs:54-59`). Otherwise the well-known path is appended.
- The resolver reads at most `MaxCardBytes` (bounded stream), then runs package 04's `IAgentCardVerifier` on the raw bytes, then the card readers.
- The result type is `ResolvedAgentCard`, a property record (spec 02 §4; plan review, 2026-11-03) with `Card`, `Verification`, `Source`, `FetchedAt` and `ETag`. `GetAsync` also has an overload taking `A2ACardFetchContext` (the client's `HttpClient`, options and readers), which `A2AClient` uses *(plan review; Claude's call)*. `IA2AClient.Card` is the card, and `A2AClient.CardVerification` exposes the verification result (package 04 asked for "the result kept with the card").
- Errors: 401 or 403 → `A2AAuthException`. Other non-2xx → `A2ATransportException` with the URL and status. Bad JSON → `InvalidAgentResponse` with the reader's message. Too large → `InvalidAgentResponse` "card larger than 1 MiB; raise A2ACardResolverOptions.MaxCardBytes".
- The extended card (`UseExtendedCard`) is cached per client and per credential identity, never in the shared cache, because the server filters it by caller *(Claude's call, B11)*. The identity is the client's own provider for calls without per-call credentials, or `A2ARequestOptions.CredentialCacheKey` for calls with them. When a call uses `A2ARequestOptions.Credentials` without a `CredentialCacheKey`, `Auto` does not fetch or use the extended card for that call (it uses the public card) and logs a debug line once.
- The card ETag the server sends is the same on every pod and after re-signing with the same keys (03 D7, B13), so `If-None-Match` revalidation works behind a load balancer.

### Bounded reads

`A2AClientOptions.Limits`:

| Limit | Default | Where |
|---|---|---|
| `MaxResponseBytes` | 8 MiB | Unary JSON-RPC and REST bodies (read through a counting stream, not `ReadAsByteArrayAsync`, today `JsonRpcA2AClient.cs:89`) |
| `MaxEventBytes` | 4 MiB | One SSE event (data lines joined) |
| `MaxGrpcMessageBytes` | 8 MiB | `GrpcChannelOptions.MaxReceiveMessageSize` and `MaxSendMessageSize` |
| `MaxErrorBodyBytes` | 64 KiB | Error bodies, read only for the A2A error envelope |

- Going over a limit stops the read, disposes the response, and throws `A2AException(InvalidAgentResponse)`: "{what} from agent '{name}' is larger than {limit}; raise A2AClientOptions.Limits.{Option} if this agent is trusted."
- The SSE reader is rewritten on `PipeReader` with a line-length and event-size cap. It parses `data:` bytes straight into the UTF-8 JSON reader, with no string per line and no string-to-bytes copy (PD3). It still handles CRLF, LF and CR, and a last event without a blank line, like `src/A2A.Client/Transports/Sse/SseReader.cs:9-13`. The `id` and `retry` fields are parsed but not used: the spec defines no replay. *(writer's call: keep `Last-Event-ID` out until the spec settles #1635.)*
- JSON depth stays at the serializer default (64).

### Artifact assembly

```csharp
public sealed class ArtifactAssembler
{
    public void Apply(StreamResponse evt);                  // artifactUpdate: append or replace; task: sync from snapshot
    public IReadOnlyList<Artifact> Artifacts { get; }       // in first-seen order
    public bool IsComplete(string artifactId);              // lastChunk seen
}
```

- `append: false` (or the first chunk for an id) replaces the artifact.
- `append: true` appends its parts to the artifact with the same `artifactId`. Adjacent text parts with the same media type are joined into one part. *(writer's call)*
- `append: true` for an unknown id starts a new artifact and logs a debug line.
- `lastChunk` marks the artifact complete.
- A task snapshot (after a resume) replaces all artifacts present in the snapshot. Complete flags are kept for ids already completed.
- `SendAndWaitAsync` uses it. It is public for callers that stream themselves.

### gRPC channel management (`Ambinder.A2A.Client.Grpc`)

- One `GrpcChannel` per (named client, interface address), created on first use, and disposed when the client factory or host is disposed (or `A2AClient.DisposeAsync` for `ConnectAsync` clients).
- Its handler is the named client's handler chain from `IHttpMessageHandlerFactory`, with `DisposeHttpClient = false`. The primary `SocketsHttpHandler` sets `EnableMultipleHttp2Connections = true`, `KeepAlivePingDelay = 60 s`, `KeepAlivePingTimeout = 30 s` and `PooledConnectionLifetime = 2 min` (DNS changes are picked up). *(writer's call)*
- `http://` addresses use h2c, as today (`src/A2A.Client/Transports/GrpcA2AClient.cs:23`). `https://` uses TLS with ALPN.
- Message size limits come from `Limits`. Deadlines come from the operation timeout. Metadata names are lowercased, as today.

### Telemetry

- `A2AClient` records the `a2a.client <Method>` span as today, with attributes `a2a.agent`, `a2a.transport`, task and context ids, plus `a2a.client.attempts` and `a2a.client.resumed` (a count).
- Wait helpers add an `a2a.client wait` span around the whole wait, with mode and outcome.
- New metrics on the `Ambinder.A2A.Client` meter: `a2a.client.retries` (counter: agent, method, reason), `a2a.client.circuit_open` (counter), `a2a.client.wait.duration` (histogram: mode, outcome).
- Never recorded: message content, headers, tokens, push credentials.

### Migration from current code

- `A2AClientFactory.Create` → `A2AClient.Create`.
- `A2AClientOptions.HttpClient` is removed. Use `AddA2AClient` or `ConnectAsync`, or pass an `HttpMessageHandler` through `A2AClientOptions.PrimaryHandler` for tests.
- `ProtocolVersion` and `PreferredBinding` → per-interface version (03) and `PreferredBindings`.
- `IA2ACredentialProvider.GetHeadersAsync` → `ApplyAsync`. `AzureIdentityCredentialProvider` → package 07.
- `A2ACardResolver(HttpClient)` → `IA2ACardResolver`, with a static `A2ACardResolver.Default` for non-DI use.
- The sample client's `WaitForStateAsync` (`samples/AgentClient/FlowRunner.cs:228`) and resubscribe flow are replaced by `SendAndWaitAsync` and `StreamWithResumeAsync`. The sample shrinks.
- Client tests move to a new `tests/Ambinder.A2A.Client.Tests` project that does not reference the samples (R39).

## How it meets the six principles

1. **Protocol.** It follows the version and tenant rules (via 03), the stream end semantics, subscribe-first-is-snapshot, idempotent-only retries, card caching (RFC 9111) and extended-card replacement. Check: interop suite direction A (package 16) passes against the Python, JS and Java servers on all transports. The TCK client-facing scenarios used by 16 pass.
2. **.NET enabler.** `IServiceCollection.AddA2AClient`, keyed services, `IHttpClientFactory`, `Microsoft.Extensions.Http.Resilience`, the Options pattern, `ILogger`, OpenTelemetry. Check: public API review (02). XML docs on every public member.
3. **Easy.** Five lines without DI. One registration call with DI. Wait and conversation helpers remove the loops. Check:
   - the getting-started client sample is at most 5 lines of calls
   - `samples/AgentClient` loses its wait and resubscribe code
   - each timeout, limit and selection error has a test that asserts the message names the option or package
4. **Performance.** No per-call `HttpClient` or handler. Pooled `PipeReader` SSE parsing. Source-generated JSON. No full-body buffering on streams. Check: package 14 benchmarks `ClientSendMessage` (allocations per call against an in-memory handler), `SseParse` (MB/s and allocations per event), `ArtifactAssemble`. Budgets are set in M2.
5. **Modular.** Transports, card readers, interceptors, credential providers, card verifier, auth and input handlers and the push waiter are all seams. gRPC and Entra are separate packages. Check: the package 01 allow-list test. Package 15 plugs in a 0.3 transport using only public seams (its tests).
6. **Integration pains.** Covers wait for completion, multi-turn ids, per-request credentials, card fetching and streaming robustness. Check: one test per pain point listed under "Findings addressed". The booking and report flows in the sample run through `A2AConversation` and `SendAndWaitAsync`.

## Dependencies

Needs:
- 01: package split (`Client`, `Client.Grpc`, `Client.Entra`), and `Microsoft.Extensions.Http.Resilience` on the client allow-list.
- 02: public API shape for `A2AAuthException.Kind`, `A2ATransportException`, `A2ATimeoutException`, and API tracking.
- 03: interface and version selection, `A2A-Version` per interface, the tenant rule (D8), ErrorInfo mapping and `A2AException.Details`, activated-extension echo (D5), and `Unspecified` for unknown states.
- 04: `IAgentCardVerifier` (called on raw bytes) and `CardVerificationResult`.
- 07: `EntraCredentialProvider` and `EntraOnBehalfOfCredentialProvider` implementing `IA2ACredentialProvider`, the server error table that makes 401 retries safe, and the grant-holder rule (B6).
- 08: the receiver's implementation of this package's `IA2APushWaiter` for push wait mode (B10).
- 09: SSE heartbeats on the server, which keep idle detection quiet, and the error reasons `SERVER_BUSY`, `RATE_LIMITED` and `SERVER_SHUTTING_DOWN` with `Retry-After`.
- 05: the new-task `messageId` dedupe rule that `AllowSendRetry` relies on.
- 06: the stream error reasons `TASK_STREAM_INTERRUPTED` and `SUBSCRIBER_TOO_SLOW`.

Provides:
- To 07: `IA2ACredentialProvider`, `A2ACredentialContext` (`ForceRefresh`, `Challenge`), `A2ARequestOptions.UserAssertion`, `IA2AClientBuilder`.
- To 08: `IA2APushWaiter` and `PushSubscription` (B10).
- To 11: `AddA2AClient(name)`, `SendAndWaitAsync` with `A2AWaitOptions`, `A2AResult`, per-call `Extensions`, and `IA2AInputRequiredHandler` (for bubbling input-required to an LLM tool).
- To 12: `A2AClientOverride` (for `AddFakeA2AClient`), `IA2AClientFactory`, and the `PrimaryHandler` option for in-memory test servers.
- To 15: `IA2ATransportFactory`, `IA2ATransport`, `IAgentCardReader`, and the "looks like 0.3" error.
- To 16: the client that the interop suite uses (direction A).
- To 13: the client guide (registration, waiting, conversations, auth, limits, resilience).
- To 14: the client benchmarks.

## Acceptance criteria

- [ ] New `tests/Ambinder.A2A.Client.Tests` project with no sample references.
- [ ] DI tests: named and keyed resolution; the lazy card fetch happens once; the `A2AClientOverride` replaces a client; mTLS certificate applied under DI; no new connections per resolve (connection-count test against a local server).
- [ ] `ConnectAsync` five-line test, and a sample in the docs.
- [ ] Resilience tests with a fault-injecting handler: idempotent ops retried; `SendMessage` not retried after bytes were sent; `SendMessage` retried on connect failure; `AllowSendRetry` opt-in; `Retry-After` honored; circuit opens and closes; gRPC retry policy limited to idempotent methods.
- [ ] Timeout tests: blocking send survives 2 minutes (fake clock or slow server); each timeout message names its option; caller cancel gives `OperationCanceledException`; stream idle timeout triggers a resume.
- [ ] Interceptor tests on all three transports: header added, order, `OnCompletedAsync` for streams, `Authorization` hidden.
- [ ] Auth tests: 401 → `ForceRefresh` → success; second 401 → `A2AAuthException(Unauthenticated)`; 403 not retried; claims challenge passed to the provider; card-requirement provider selection; reserved headers refused in `Headers`.
- [ ] Step-up test: the agent sets `AUTH_REQUIRED` with a grant for app B (package 07). The handler returns a continuation with app B's credentials. The task completes. Without a handler, the wait returns `AuthRequired`. After the continuation, the wait stays on the continuation's stream; a forced resume uses the original caller's credentials (B6).
- [ ] Extended card tests: cached per credential identity; per-call credentials without `CredentialCacheKey` use the public card; with a key, the extended card is fetched and cached for that key (B11).
- [ ] Wait tests on JSON-RPC, REST and gRPC: stream happy path; direct message reply; drop mid-stream → resume → complete; drop past `MaxResumes` → poll; drop before the first event → `A2AStreamInterruptedException`; poll mode; push mode with package 08's receiver; total timeout with and without `CancelRemoteOnTimeout`.
- [ ] Conversation tests: the booking flow end to end; ids cleared on terminal; caller-set ids refused; concurrent send refused; state round-trip.
- [ ] Card resolver tests: `max-age`, `no-store`, ETag 304 revalidation, LRU bound, legacy `agent.json` probe only on 404, 0.3 card message, size limit, https rule, verifier called on raw bytes and result exposed.
- [ ] Bounded-read tests: oversized unary, SSE event, gRPC message, card and error body. Each fails fast with the named option, and memory stays bounded (allocation assertion).
- [ ] Artifact assembler tests: append and replace, `lastChunk`, unknown-id append, snapshot after resume.
- [ ] Transport seam test: a test-only fake transport factory for a made-up version `9.9` is selected from a card and gets interceptors, auth and tracing.
- [ ] Missing-package messages for gRPC-only and 0.3-only cards.
- [ ] Interop direction A (package 16) green on Python, JS and Java servers.
- [ ] Package 01 allow-list test green for `Ambinder.A2A.Client`.
- [ ] PD3 closed: package 14's `Stream` micro scenario (`SseReader` parse of 10,000 frames) shows before and after numbers in the PR, with zero per-line string allocations.
- [ ] Package 14's `Client` scenarios (cached card lookup allocates nothing, 304 refresh, cached-token credential, empty interceptor chain) plus `ClientSendMessage` and `ArtifactAssemble` have a baseline.
- [ ] Docs: the client guide sections listed under Provides (13).

## Left to the builder

- Internal class layout of the call pipeline, and whether interceptors and credentials run as one ordered list.
- The exact jitter formula, and how `Retry-After` dates are parsed.
- Whether `A2ARequestHeaders` wraps `HttpRequestHeaders` or is its own small collection mapped to gRPC `Metadata`.
- `PipeReader` details of the SSE reader, and the line buffer size.
- LRU implementation for the card cache and the gRPC channel cache.
- The names of the convenience extension methods beyond those listed, as long as package 02's naming review passes.
- Whether `A2AConversation` exposes the last `A2AResult`.

## Open questions

None. `AllowSendRetry` (Q1) and `CancelRemoteOnCallerCancel` (Q2) are Claude's call C11 ("Resilience", "Wait for completion").
