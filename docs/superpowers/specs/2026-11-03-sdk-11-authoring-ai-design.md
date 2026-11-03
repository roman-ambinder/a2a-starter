# SDK 11: Agent authoring and AI bridges

Date: 2026-11-03
Status: draft for approval
Milestone: M2 (`1.0.0`)
Size: L (1–3 weeks)

## Intent

Writing an agent today means implementing `IAgentHandler` and talking to a raw `IAgentEventSink`. Every agent builds message ids, roles and context ids by hand, cannot read request metadata or earlier turns, cannot fail with a specific error, and has to guess how cancellation and DI scopes behave. There is no extension framework. There is no link to Microsoft.Extensions.AI, although most real agents are LLM-backed.

This package gives agent authors a small, clear model on top of the existing contract. It adds:

- a `TaskUpdater` helper and a richer request context (metadata, conversation history, caller);
- written rules for multi-turn work, cancellation, errors and DI scope;
- a real framework for A2A extensions;
- a package, `Ambinder.A2A.Extensions.AI`, that turns an `IChatClient` into an A2A agent and a remote A2A agent into an `AIFunction`.

The target: a working agent in about 20 lines, an LLM agent in about 10.

## Findings addressed

- R33: thin authoring model (no metadata, helpers, history; cannot fail with a specific error; `returnImmediately` not enforced).
- R34: no Microsoft.Extensions.AI integration in the SDK.
- R35: no extension framework.
- R36: DI scope and lifetime semantics underspecified.
- Pain point 8 (task and context id handling in multi-turn): the server side. The client side is package 10.
- Pain point 9 (non-blocking send waits for the first event): server side of `returnImmediately`.
- Pain point 13 (cancellation semantics): token flow, guaranteed CANCELED, idempotent cancel.
- Pain point 16 (extension negotiation): typed registry, required-extension enforcement, echo of activated extensions.
- Pain point 17 (custom methods and extensibility): partly. Extension hooks are covered. New RPC methods are not.
- Pain point 18 (wrap a framework as an A2A server): `IChatClient` and Agent Framework adapters.
- Pain point 19 (remote A2A agent as a tool): `AIFunction` wrapper.
- Pain point 11 (artifact chunking, inferred need): `ArtifactWriter`.
- Pain point 6 (error mapping): an agent can fail with a specific A2A error.

## Goals

1. An echo agent is registered in one statement and at most 8 lines of `Program.cs`. A multi-turn task agent with progress, an artifact and an input-required step is at most 20 lines of agent code. An `IChatClient`-backed agent is at most 10 lines of `Program.cs`.
2. `TaskUpdater` offers one method per task transition (`StartWorkAsync`, `CompleteAsync`, `FailAsync`, `RejectAsync`, `RequireInputAsync`, `RequireAuthAsync`, `CancelAsync`) and artifact helpers. No agent code sets `MessageId`, `Role`, `TaskId` or `ContextId`.
3. `AgentRequestContext` exposes request metadata, message metadata, tenant, owner, protocol version, transport, whether the call is streaming, whether it is a continuation or cancel, and a scoped `IServiceProvider`. No `HttpContext` is needed.
4. An agent can read the earlier conversation of its `contextId` (all earlier tasks of the same owner) in time order with one call, capped by options.
5. Event metadata: status events, artifact events and the task can carry metadata set by the agent.
6. An agent that throws `A2AException` before its first event fails the request with that exact error and stores no task. After the task exists, the task ends FAILED with that exception's message. Any other exception ends FAILED with a fixed text plus a reference id that is also in the log.
7. Cancellation is written down and tested. The token reaches the agent. The task always ends CANCELED. A second cancel of a CANCELED task returns the task. An agent that ignores the token cannot corrupt the task.
8. With `returnImmediately`, SendMessage returns within `ReturnImmediatelyGrace` (default 100 ms) whatever the agent does.
9. DI: the scope, lifetime and disposal of the handler are written down, tested, and validated at startup (a handler with a missing dependency fails at startup, not at the first call).
10. Extensions: one `A2AExtension` class declares the extension in the card, enforces `required`, runs request and outgoing hooks only when active, and the server echoes activated extensions on responses. Typed metadata access works without reflection.
11. `Ambinder.A2A.Extensions.AI`: `AddChatAgent` serves an `IChatClient` as an A2A agent with conversation memory, streaming tokens as coalesced artifact chunks, cancellation and safe failures.
12. `A2AAgentTool` turns a remote agent into an `AIFunction` that keeps the remote `contextId`, waits for completion, bubbles `INPUT_REQUIRED` to the model, and cancels the remote task when the call is cancelled.
13. The mapping between `Message`/`Part` and `ChatMessage`/`AIContent` is public, documented as a table, and round-trips for text, file bytes, file URLs and JSON data.
14. Everything above is trim- and AOT-safe: no reflection on user types, no `JsonSerializer` overloads without `JsonTypeInfo`.

## Non-goals

- The wire protocol, store contract and cluster layer (03, 05, 06). This package only uses them.
- Authentication and the owner model (07). The context exposes `Caller`, `User`, `Owner` and `Tenant`; 07 defines them.
- A general server middleware pipeline for every operation (rate limits, tenant checks). Package 09 owns it (`IA2AMiddleware`, 09 §7.11). Extension hooks (below) run inside it as built-in step 4 and are only for A2A extensions.
- New RPC methods added by extensions (pain point 17). Extensions here add metadata and checks, not methods.
- Client-side extension negotiation and the client wait helper (10). This package uses them.
- Agent card signing and serving (04, 09). `AgentCardBuilder` here only builds the card object.
- An MCP bridge (inferred need, weak evidence in the pain-point file, topic 23).
- One `AIFunction` per skill. The tool is one function per agent in v1.
- Semantic Kernel or LangChain-style adapters. `IChatClient` and Agent Framework cover the .NET path.
- Prompt and model management (which model, which key). That is the app's `IChatClient` registration.

## Anti-goals

- An authoring model that hides the A2A concepts. Task states, artifacts and messages stay visible. The helpers remove ceremony, not meaning.
- Agent code that is correct only if it remembers a rule (set the context id, send an early event, catch the cancel exception). The framework does it.
- Per-token events: one store write, SSE event and push per token would make LLM streaming unusable.
- Instance state that works on one pod and silently breaks on another. The rules say a handler holds no state between turns.
- Letting an LLM tool call leak provider error text, secrets or full stack traces into a task message.
- A hard dependency on Azure, Agent Framework, Semantic Kernel or a specific LLM provider in the core server package.
- A remote agent's reply treated as trusted instructions. It is tool output (data).

## Constraints

- Names: `Ambinder.A2A.*`, .NET 10, Apache 2.0 (work plan, decisions).
- Authoring helpers live in `Ambinder.A2A.Server` (no new package) *(writer's call)*. The AI bridge is a separate package, `Ambinder.A2A.Extensions.AI`, which depends on `Ambinder.A2A.Server`, `Ambinder.A2A.Client` and `Microsoft.Extensions.AI.Abstractions` only. It must not reference the full `Microsoft.Extensions.AI` package. The repo pins 10.11.0 (`Directory.Packages.props`).
- Spec rules the authoring model must keep:
  - The server generates task ids. A message with a `taskId` must reference an existing task, else `TaskNotFoundError`. A mismatching `contextId`/`taskId` is rejected. A client `contextId` is never silently replaced. (research-spec §3, spec 3.4)
  - Blocking send waits for a terminal or interrupted state. `returnImmediately: true` must return at once. (spec 3.1.1)
  - A stream is either one Message then close, or a Task first then updates, closing at a terminal or interrupted state. Events are ordered. (spec 3.1.2, 3.5)
  - Outputs belong in Artifacts, not Messages. (spec 3.1.6)
  - Cancel is idempotent. (spec 3.4)
  - `AUTH_REQUIRED` needs a Task, a state, and a status message; the state alone is not authorization. (spec 7)
  - `A2A-Extensions` is a comma-separated header (HTTP) or metadata key `a2a-extensions` (gRPC). A card extension with `required: true` that the client did not request gives `ExtensionSupportRequiredError`. An unsupported or unknown requested extension is ignored unless required. A breaking change needs a new URI, so URIs are matched exactly with no fallback to an older version. (spec 4.6, 3.2.6)
- Errors keep the codes of the error catalog (`A2AErrorCode`). Terminal-state rules stay in `TaskStateRules`.
- Public API: every new public member has XML docs and is added to the API tracking files (package 02).

## Design

### 1. Layers of the authoring model

Three levels. An author picks the lowest one that fits.

| Level | Type | Use |
|---|---|---|
| 1. Delegate agent | `AddAgent(name, card, Func<AgentRequestContext, AgentResponder, CancellationToken, ValueTask>)` | Stateless reply or a few lines of logic. |
| 2. Handler class | `IAgentHandler` + `TaskUpdater` | Tasks, progress, artifacts, multi-turn. |
| 3. Raw sink | `IAgentEventSink` | Full control. Everything above is built on it. |

`IAgentHandler` stays the one contract the framework calls. Levels 1 and 2 are helpers, not new contracts.

### 2. Request context

`AgentRequestContext` changes from a positional record to a sealed class with get-only properties (writer's call: a record with many positional members cannot grow without breaking callers). The server creates it; the Testing package (12) creates it through `AgentRequestContext.Create(...)`, an explicit public factory.

Existing members stay (`AgentName`, `TaskId`, `ContextId`, `Message`, `CurrentTask`, `User`, `ActiveExtensions`, `Configuration`; today `AgentRequestContext.cs`). New members:

| Member | Meaning |
|---|---|
| `Kind` (`AgentRequestKind`: `NewTask`, `Continuation`, `Cancel`) | `IsContinuation` and `IsCancel` are shortcuts. Today an agent infers this from `CurrentTask`. |
| `IsStreaming` | True when the call was SendStreamingMessage. Chat agents use it to choose streaming. |
| `Metadata` | `SendMessageRequest.Metadata` (`Requests.cs`, R33). Null for cancel. |
| `Caller` | Package 07's `A2ACaller` (owner, kind, Entra tenant, app id, user object id, claims). Safe to use after the HTTP request ends. In a run on another pod (06) or a recovered run (09) it is the reduced caller stored with the task: owner key, tenant id, app id, user object id, roles and scopes, with no access token. On-behalf-of works only on the pod that received the request, during that request's run *(Claude's call, B8)*. |
| `Tenant`, `Owner`, `ProtocolVersion`, `Transport` | Copied from `A2ACallContext` (`Owner` is `Caller.Owner`; `User` is `Caller.Principal`). 07 defines owner and tenant. |
| `UserText` | The text parts of `Message` joined by a newline. Empty if none. |
| `Services` | The `IServiceProvider` of the execution scope (see section 6). |
| `ActivatedExtensions` | `IReadOnlyList<A2AExtension>` for typed access (section 7). `ActiveExtensions` (URIs) stays. |
| `GetMetadata<T>(string key, JsonTypeInfo<T> info)` | Typed read from request or message metadata. Returns default if missing. |
| `GetConversationAsync(ConversationOptions?, ct)` | See below. |

`GetConversationAsync` returns the earlier conversation of `ContextId` as `IReadOnlyList<Message>`, oldest first:

1. It lists the owner's tasks of this agent with this `contextId` through package 05's `ITaskStore.ListAsync` with a `TaskQuery` whose `Scope` is the caller's `OwnerScope(Tenant, Agent, Owner)` and whose `ContextId` is set. Another owner's tasks with the same `contextId` are never returned (05 has a conformance case for it). A `contextId` is only a grouping label inside one owner.
2. Per task, in task order: the history messages (user and agent), then, if `IncludeArtifactText` (default true), one agent message with the text parts of the task's artifacts. Reason: agents put outputs in artifacts (spec 3.1.6), so the model must see them as its earlier answers.
3. The current task (continuation) is included from `CurrentTask.History`. For a new task, the current `Message` is appended last. Messages are de-duplicated by `messageId`.
4. `ConversationOptions`: `MaxMessages` (default 100, newest kept), `MaxTasks` (default 20), `IncludeArtifactText` (true), `IncludeNonText` (false: non-text parts become a short text note such as `[file report.csv, text/csv]`).
5. Cost: one indexed store query, on package 05's context-id index (EF: `(tenant, agent, owner, context_id, status_ts DESC, task_id DESC)`; Cosmos: the `contextId` composite index inside the owner's partition). The result is cached for the life of the context object.

Agents never mint ids. `AgentEventSink` already overwrites `TaskId` and `ContextId` on status messages (`AgentEventSink.cs` `Normalize`). `ReplyAsync` today keeps a `ContextId` that the agent set (`AgentEventSink.cs:69`). Change it to always use the context's id *(writer's call)*.

### 3. `TaskUpdater` and friends

All in namespace `Ambinder.A2A.Server` (package 02's rule: day-one authoring types live in the root server namespace).

```csharp
public sealed class TaskUpdater(AgentRequestContext context, IAgentEventSink sink)
{
    public string TaskId { get; }  public string ContextId { get; }
    public ValueTask StartWorkAsync(string? text = null, CancellationToken ct = default);
    public ValueTask CompleteAsync(string? text = null, CancellationToken ct = default);
    public ValueTask FailAsync(string text, CancellationToken ct = default);
    public ValueTask RejectAsync(string text, CancellationToken ct = default);
    public ValueTask RequireInputAsync(string prompt, CancellationToken ct = default);
    public ValueTask RequireAuthAsync(string prompt, TaskContinuationGrant? grant = null, CancellationToken ct = default);
    public ValueTask CancelAsync(string? text = null, CancellationToken ct = default);
    public ValueTask ReplyAsync(string text, CancellationToken ct = default);
    public ValueTask UpdateAsync(TaskState state, Message? message, AgentEventOptions? options, CancellationToken ct = default);
    public ValueTask AddArtifactAsync(string name, IEnumerable<Part> parts, string? artifactId = null, AgentEventOptions? options = null, CancellationToken ct = default);
    public ValueTask AddTextArtifactAsync(string name, string text, string? mediaType = null, string? artifactId = null, CancellationToken ct = default);
    public ArtifactWriter OpenArtifact(string name, string? mediaType = null, string? artifactId = null);
    public Message NewMessage(params Part[] parts);
}
```

Rules:

- Names match the other SDKs' helper (`TaskUpdater` with start-work, add-artifact, complete, fail, require-input; research-sdks, .NET and Python).
- Methods that take `text` build an agent `Message` with a new `messageId`, `Role.Agent`, and the context's `TaskId` and `ContextId`.
- `RequireAuthAsync` always sends a status message (spec 7 requires one). The optional `grant` is package 07's `TaskContinuationGrant` (owner keys allowed to continue, and an expiry the agent must choose). Grants are built from 07's typed `OwnerKey` (`OwnerKey.ForApp(tenantId, appId)`, `OwnerKey.ForUser(tenantId, userObjectId)`), never from hand-made strings *(Claude's call, B7)*. It is passed to 07's sink overload `IAgentEventSink.RequireAuthAsync(Message, TaskContinuationGrant?, CancellationToken)`. Without a grant, only the task's owner may continue.
- `ArtifactId` default is a new GUID in `N` format. `OpenArtifact` with no id gets one at once and keeps it.
- `ArtifactWriter` (`IAsyncDisposable`): `AppendAsync(string text)`, `AppendAsync(Part part)`, `CompleteAsync()`. The first write is `append: false`, later writes are `append: true`, and `CompleteAsync` (or `DisposeAsync` without error) sends `lastChunk: true`. If disposed after an exception it sends nothing: the framework fails the task.
- `TaskUpdater` adds no state of its own. It holds no lock and can be created anywhere in the handler.

`AgentResponder` (level 1) is a thin wrapper for delegate agents: `TextAsync(string)` (direct reply), `Task` (a `TaskUpdater`), `Sink`. A delegate agent that calls `TextAsync` is the whole echo agent.

Extension methods on `IAgentEventSink`: `ReplyTextAsync(context, text, ct)` for people who stay at level 3.

### 4. Sink changes

`IAgentEventSink` gets event metadata and a rule for cancel. The interface is not frozen yet (02 freezes it at M2), so it changes now.

```csharp
ValueTask UpdateStatusAsync(TaskState state, Message? message, AgentEventOptions? options, CancellationToken ct);
ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, AgentEventOptions? options, CancellationToken ct);
public sealed record AgentEventOptions { public JsonElement? EventMetadata { get; init; } public JsonElement? TaskMetadata { get; init; } }
```

- `EventMetadata` goes to `TaskStatusUpdateEvent.Metadata` or `TaskArtifactUpdateEvent.Metadata` (proto fields; `Events.cs`).
- `TaskMetadata` is merged into `AgentTask.Metadata` as a JSON merge patch (RFC 7386), under the task lock *(writer's call)*. Use: per-task agent state that survives a pod change and is readable in `CurrentTask.Metadata` (for example a remote job id for cancel).
- Old two-argument overloads stay as default interface methods that pass `null`.
- Package 07's M1 overload `RequireAuthAsync(Message statusMessage, TaskContinuationGrant? grant, CancellationToken ct)` stays; `TaskUpdater.RequireAuthAsync` calls it.
- Stored artifacts coalesce adjacent plain text parts on append *(writer's call)*. Without this, a streamed answer becomes one part per chunk, and `AgentEventSink.cs:111` copies the whole part list on every chunk (O(n²) over a long answer). Events on the wire keep the chunk as sent.
- After the task was cancelled or reached a terminal state by another path, sink methods throw `OperationCanceledException` (carrying the execution token) instead of `A2AException(InvalidAgentResponse)` (today `AgentEventSink.cs:154`) *(writer's call)*. A well-behaved agent unwinds through its normal cancel path. `RunAsync` already ignores an exception after cancel (`TaskManager.cs:318`).
- Illegal use by the agent (reply after a task event, bad transition) still throws `A2AException(InvalidAgentResponse)` and fails the task, as now.

### 5. Multi-turn rules (the contract, written for agent authors)

These are the existing behavior of `TaskManager.StartAsync` (`TaskManager.cs:218-298`), now documented and tested as the contract:

1. A message without `taskId` is a new request. The server creates the task id. The `contextId` is the client's if given, else new. `Kind` is `NewTask`. If the same owner already sent a new-task message with the same `messageId`, the server returns the existing task and does not run the handler again (package 05's dedupe, 05 §7.10). So a client retry never gives an agent a duplicate first turn. Continuations are not deduped.
2. A message with `taskId` continues that task, only if it is `INPUT_REQUIRED` or `AUTH_REQUIRED` (`Kind` is `Continuation`). A `WORKING`/`SUBMITTED` task rejects it with `UnsupportedOperation`. A terminal task rejects it. The client then starts a new task in the same `contextId`.
3. Who may continue an `AUTH_REQUIRED` task is decided by 07's `ITaskContinuationPolicy` (the owner, or an owner named in the task's `TaskContinuationGrant` before it expires). The policy runs as 07's middleware in 09's pipeline, before the handler. A grant holder receives the response of its own continuation call (blocking result, or the stream to a terminal or interrupted state), because the protocol returns it. It cannot Get, List, Subscribe or Cancel the task afterwards *(Claude's call, B6)*. Agents should not put data for the owner only into the continuation response.
4. An execution ends at the first terminal or interrupted state. A continuation starts a new execution with a new handler instance, maybe on another pod (06). So a handler holds no state in fields between turns. State goes in task metadata (`AgentEventOptions.TaskMetadata`), in artifacts, or in the agent's own store.
5. A reply (`ReplyAsync`) creates no task and stores nothing. The next turn cannot see it in `GetConversationAsync`. An agent that needs memory must use a task. This is the main reason `AddChatAgent` defaults to tasks.
6. Direct reply or task? Advice in the docs: reply when the answer is immediate and stateless; use a task when work can take long, streams, needs input, or the conversation needs memory.
7. `returnImmediately` (R33, pain point 9): SendMessage waits for the agent's first event for at most `ReturnImmediatelyGrace` (new option on `A2AServerOptions`, default 100 ms) *(writer's call)*. If none arrives, the server creates the task as `SUBMITTED` and returns it (today `TaskManager.cs:62-66` waits as long as the agent takes). If the agent then calls `ReplyAsync`, the server stores that message as the status message and moves the task to `COMPLETED`. This coercion applies only to calls with `returnImmediately`; otherwise a late reply stays an error.
8. The server checks `contextId`/`taskId` consistency before the agent runs (already so, `TaskManager.cs:264-265`).

### 6. Cancellation, errors, DI scope

**Cancellation** (the contract; most is current behavior in `TaskManager.CancelTaskAsync`, `TaskManager.cs:103-160`):

1. The `CancellationToken` passed to `ExecuteAsync` is cancelled by CancelTask and by the host stopping after the shutdown grace period (09 defines the grace). It is not the HTTP request token. A client disconnect does not cancel the agent.
2. On CancelTask the server: marks the run as cancel-requested, cancels the token, calls `CancelAsync` on a new handler instance in a new scope, then commits `CANCELED` if the task is not terminal. Whatever the agent does, the task ends `CANCELED` (`TaskManager.cs:141-149`).
3. `CancelAsync` has a time limit, `AgentOptions.CancelTimeout` (default 30 s) *(writer's call)*. After it, the server commits `CANCELED` and logs a warning. Today it has no limit.
4. `CancelAsync` default (set `CANCELED`) is usually enough. Override it only to stop external work. The instance differs from the run instance, so it reads what it needs from `CurrentTask.Metadata`.
5. Cancel of a task already `CANCELED` returns the task (idempotent, spec 3.4). Cancel of another terminal state throws `TaskNotCancelable`. Today the first case also throws (`TaskManager.cs:112-113`). Package 03 confirms with the TCK *(writer's call)*.
6. An `OperationCanceledException` from the agent after cancel is expected and ignored (`TaskManager.cs:318`). Agents need no try/catch.
7. An agent that ignores the token keeps running. Its later sink calls throw `OperationCanceledException` (section 4). It cannot change the task. The server logs once at Debug when a run outlives its cancel by more than `CancelTimeout`.
8. In a cluster (06) the cancel is forwarded to the pod that runs the task. The handler sees the same contract.

**Errors:**

1. `A2AException` thrown by a handler before its first event, with no task created: the call fails with that exact error code, nothing is stored. This is how an agent rejects bad input (`InvalidParams`, `ContentTypeNotSupported`). For a streaming call it surfaces when the stream starts, before the first byte, through the normal transport error mapping.
2. `A2AException` after the task exists: the task ends `FAILED` with a status message equal to the exception message. The exception message is treated as safe to show. Documented.
3. Any other exception: the task ends `FAILED` with the text `Agent error (ref <id>)`. `<id>` is the current trace id, else a new GUID. The same id is in the error log. Today the text is just `Agent error` (`TaskManager.cs:336`) *(writer's call)*.
4. Reaching the end without a final state still fails the task (`TaskManager.cs:312-316`). Unchanged.

**DI scope and lifetime** (R36):

1. Each execution (`ExecuteAsync`) and each cancel (`CancelAsync`) gets its own `IServiceScope` created from the root `IServiceScopeFactory`. The scope lives until the method returns, even after the HTTP response is gone. It is not the request scope. `IHttpContextAccessor.HttpContext` is null in agent code. Use `context.User`, `context.Metadata`, `context.Tenant`.
2. `AddAgent<THandler>` registers `THandler` as scoped with `TryAddScoped` *(writer's call)*. Today the fallback is `ActivatorUtilities.CreateInstance` (`TaskManager.cs:371-372`). With a registration the container validates the dependency graph (`ValidateOnBuild`) and disposes the instance. If the user already registered the type, that registration wins.
3. A scoped dependency (for example a `DbContext`) is fresh per execution. Do not capture it in a background task that outlives `ExecuteAsync`.
4. `AgentRegistration.HandlerType` stays a `Type` internally (R36). The only public way to build it is `AddAgent<THandler>`, whose generic constraint checks `IAgentHandler` at compile time. `AgentCatalog` keeps its runtime check for direct construction.
5. Delegate agents (level 1) run through one internal `DelegateAgentHandler` that resolves the delegate from a singleton holder. Closures must not capture scoped services; they use `context.Services`.
6. All of this goes into the XML docs of `IAgentHandler` and into the docs guide (13).

### 7. Extension framework (R35)

New namespace `Ambinder.A2A.Server.Extensions`.

```csharp
public abstract class A2AExtension
{
    public abstract string Uri { get; }                    // exact; version is part of it
    public virtual bool Required => false;
    public virtual string? Description => null;
    public virtual JsonElement? CardParams => null;        // goes into capabilities.extensions[].params
    // Hooks run only when the extension is active for the call.
    public virtual ValueTask OnRequestAsync(ExtensionRequestContext context, CancellationToken ct) => default;
    public virtual ValueTask OnOutgoingAsync(ExtensionOutgoingContext context, CancellationToken ct) => default;
}
```

Registration: `a2a.AddExtension<T>()` makes it available to every agent. `AddAgent(..., configure: a => a.UseExtension<T>())` limits it to one agent *(writer's call)*. A registered extension instance is a singleton; it must be thread-safe.

Behavior:

1. **Card.** The framework adds one `AgentExtension` entry (uri, description, required, params) per registered extension to `capabilities.extensions`. An extension class and its card entry can no longer drift apart. A hand-written entry with the same URI is kept and not duplicated.
2. **Activation.** Active = requested (header or gRPC metadata, already parsed into `A2ACallContext.RequestedExtensions`) ∩ registered for the agent, matched by exact string (no fallback to other versions). Unknown requested URIs are ignored. This is computed once, at the start of every operation, not inside `TaskManager`, so the response header can be written before the body. The place is package 03's extension-negotiation middleware, built-in step 4 of package 09's `IA2AMiddleware` pipeline (09 §7.11); this package extends that step with the `A2AExtension` registry and hooks. It does not add a second negotiation path.
3. **Required.** A registered or card-declared extension with `Required` that is not requested throws `ExtensionSupportRequired` listing the URIs (today `RequestValidator.ResolveExtensions`, `RequestValidator.cs:41-49`, also run from `TaskManager.cs:223`). The check runs on every operation except the public card fetch, as package 03 (D5, Goal 7) defines. *(Reconciled: this spec had it on message-sending operations only; 03 owns the protocol rule. TCK issue #193 in research-painpoints topic 16 stays a known risk for 03.)*
4. **Echo.** The response carries the activated URIs: header `A2A-Extensions` (HTTP bindings), initial metadata `a2a-extensions` (gRPC), written through 09's `A2AOperationContext.ResponseMetadata`. The research gives the request header rules (spec 4.6); the echo comes from the SDK opportunity in pain point 16, and 03 verifies that the TCK accepts it.
5. **Request hook.** `ExtensionRequestContext` has `Message`, `Metadata`, `Call` (the `A2ACallContext`), `GetMetadata<T>(JsonTypeInfo<T>)` (reads `metadata[Uri]`), `Replace(Message)`, and `Reject(A2AErrorCode, string)` which throws `A2AException` before any task is created. It runs after the standard validation and before the task is created.
6. **Outgoing hook.** `ExtensionOutgoingContext` exposes each outgoing message, status, artifact or reply before it is stored: `Kind`, `Message?`, `Artifact?`, `SetMetadata<T>(T value, JsonTypeInfo<T> info)` which merges into `metadata[Uri]` and adds `Uri` to `extensions` of that message or artifact (the spec marker). Convention: extension data lives under `metadata[<extension URI>]` (the sample already does this, `EchoAgent.cs`) *(writer's call; the research does not state a rule)*. Hooks run in registration order, under the task lock, so they must be fast and never call back into the manager.
7. **Typed access from agents.** `context.ActivatedExtensions` holds the instances. `context.Extension<T>()` returns the instance or null when not active.
8. **Sample.** `samples/AgentServer` replaces its hand-written timestamp and audit extension code (`AgentCards.TimestampExtension`, `EchoAgent`) with `A2AExtension` subclasses.

Hooks are not general middleware. General per-request middleware is package 09's `IA2AMiddleware`; apps that need more than extension hooks write one.

The card entries (behavior 1) are added through package 03's `IAgentCardContributor`, which allows appending to `capabilities.extensions`.

### 8. Package `Ambinder.A2A.Extensions.AI`

#### 8.1 Mapping

Public static class `A2AChatMapper` with extension methods (`ToChatMessage`, `ToChatMessages`, `ToPart`, `ToParts`, `ToMessage`).

| A2A `Part` | `AIContent` | Note |
|---|---|---|
| `text` | `TextContent` | |
| `raw` + `mediaType` | `DataContent` | bytes copied once |
| `url` + `mediaType` | `UriContent` | |
| `data` (JSON) | `DataContent` with `application/json` *(writer's call)* | no JSON content type exists in M.E.AI |
| `metadata`, `filename` | `AdditionalProperties` keys `a2a.metadata`, `a2a.filename` | |

Back: `TextContent`→text, `DataContent`→raw (or data part when `application/json`), `UriContent`→url. `TextReasoningContent` is dropped unless `IncludeReasoning` is set. `FunctionCallContent`/`FunctionResultContent` are internal to the tool loop and are not mapped unless `ReportToolCalls` is set (then each call becomes a `WORKING` status message `Calling <name>`). `ErrorContent` fails the task. Roles: `User`↔`Role.User`; `Role.Agent`↔`ChatRole.Assistant`. `ChatMessage.MessageId` carries `messageId`.

#### 8.2 `AddChatAgent`

```csharp
a2a.AddChatAgent("assistant", AgentCardBuilder.Create("Assistant", "Answers questions."),
    o => o.Instructions = "You are a helpful assistant.");   // IChatClient comes from DI
```

- Overloads take a keyed `IChatClient` (`o.ChatClientKey`) or a factory.
- `ChatClientAgentHandler : IAgentHandler` is a scoped service. Options are named options per agent (`IOptionsMonitor<ChatAgentOptions>.Get(agentName)`), so one app can host many chat agents.
- `ChatAgentOptions`: `Instructions` (system message), `ChatOptions` (template: temperature, tools), `Reply` (`Task` default, or `Message`), `ArtifactName` (default `response`), `Conversation` (`ConversationOptions`), `FlushInterval` (default 100 ms), `FlushChars` (default 512) *(writer's call)*, `IncludeReasoning`, `ReportToolCalls`, `ExposeErrors` (default false), `RemoteAgents` (names of registered A2A clients exposed as tools, see 8.3).

Flow for `Reply = Task`:

1. Messages = `Instructions` + `GetConversationAsync` mapped to `ChatMessage`s.
2. `StartWorkAsync()`.
3. If `context.IsStreaming`: `GetStreamingResponseAsync`; text deltas go to an `ArtifactWriter`. Deltas are coalesced and flushed when `FlushChars` accumulate or `FlushInterval` passes, whichever is first, and at the end. If not streaming: `GetResponseAsync`, one artifact.
4. `CompleteAsync()`.
5. The token is the chat client's token. Cancel ends the call; the framework sets `CANCELED`.
6. A chat exception: log it, `FailAsync("The model call failed.")`. With `ExposeErrors`, the exception message is included.

`Reply = Message` is stateless and cheap: one `GetResponseAsync` over the current message only (no conversation), and `ReplyAsync`. Streaming requests still work: the framework sends one message.

#### 8.3 Remote agent as a tool

```csharp
public sealed class A2AAgentTool : AIFunction
{
    public static ValueTask<A2AAgentTool> CreateAsync(IA2AClient client, AgentCard card, A2AToolOptions? options = null, CancellationToken ct = default);
}
```

- One function per agent. Name: the card name sanitized to `[a-zA-Z0-9_-]{1,64}`. Description: card description plus skill names and descriptions, cut at 1,000 characters *(writer's call)*.
- Hand-written JSON schema (a constant), no reflection. Parameters: `message` (string, required) and `taskId` (string, optional: answers a question the agent asked earlier).
- Identity *(Claude's call, B9)*: the tool calls the remote agent with the agent's own app identity by default (the registered client's provider, for example 07's `EntraCredentialProvider`). On-behalf-of is opt-in (`A2AToolOptions.UseOnBehalfOf = true`) and works only on the pod that received the request (B8).
- Id binding (prompt-injection guard, B9): the tool records every remote task id and context id it creates, bound to the parent caller's owner key (in the parent task's metadata, so it survives pods and recovery). A `taskId` from the model that the tool did not create for this owner is refused with a tool result `{"status":"failed","error":"unknown taskId"}`; the call is not sent. A derived `contextId` is always computed, never taken from the model.
- Result is a short JSON string for the model: `{"status":"completed","text":"..."}`, `{"status":"input_required","taskId":"...","question":"..."}`, `{"status":"failed","error":"..."}`. Text comes from the artifact text parts, or the reply message. Non-text parts are listed as `[name, mediaType, size]` or their URL. The result is cut at `MaxResultChars` (default 20,000) *(writer's call)*.
- A2A failures, `FAILED`/`REJECTED` and A2A errors are returned as a result so the model can recover. Authentication errors (`A2AAuthException`), and cancellation are thrown.
- Waiting uses the client wait helper of package 10 (stream, fall back to polling, resubscribe on drop). Tool timeout: `A2AToolOptions.Timeout`, default 2 minutes.
- The token cancelling the tool call sends CancelTask to the remote agent (best effort).
- `contextId` handling (pain point 8): inside an `AddChatAgent` run, the tool derives a stable remote `contextId` from the parent `contextId` and the remote agent URL (a version-8 style UUID from a SHA-256 hash) *(writer's call)*. So each parent conversation has one remote conversation per remote agent, switching agents never reuses an id, and two users never share one (their parent ids differ and the remote owner is the calling app identity anyway). The handler sets an ambient `AgentExecutionScope` (`AsyncLocal`) for the run; the tool reads it. Outside an agent run, the tool keeps its own `contextId`; create one tool per conversation or set `A2AToolOptions.ContextIdProvider`.
- Option `ForwardArtifacts` (default false): artifacts of the remote task are re-emitted on the parent task as `<agent>/<artifactId>` (pain point 19, forwarding). Needs the ambient scope.
- The result text is untrusted data. The docs say so and show how to keep it out of system instructions.
- Registration: `a2a.AddChatAgent(..., o => o.RemoteAgents.Add("travel"))`, with `"travel"` a client registered by package 10 (`AddA2AClient("travel", ...)`). The handler resolves tools lazily on first use and caches them for the card cache lifetime (10). It also resolves from `IA2AToolProvider.GetToolsAsync(ct)` for apps that build their own `IChatClient` pipeline.

#### 8.4 Microsoft Agent Framework bridge

Separate package `Ambinder.A2A.Extensions.AI.AgentFramework`: `AddAgentFrameworkAgent(name, card, Func<IServiceProvider, AIAgent>)`. It uses the same conversation mapping and the same streaming pump as 8.2, calling the framework agent's streaming run on a thread rebuilt from the A2A conversation each turn (stateless, pod-safe). It ships in 1.0. If `Microsoft.Agents.AI` has no GA release by the start of M2, it ships in 1.0 as a preview package (pre-release version suffix, marked experimental in its README) *(Claude's call, C12)*. The builder verifies the type and method names of that library at build time (not in the repo or the research). Microsoft's own A2A client agent for Agent Framework is out of scope (`learn.microsoft.com`, referenced in research-painpoints topic 9).

### 9. `AgentCardBuilder` and the minimum registration

To reach the 20-line target the card must be short. `AgentCardBuilder.Create(name, description)` returns a builder with `.WithSkill(id, name, description, tags?)`, `.Streaming()`, `.Push()`, `.WithInputModes(...)`, `.WithOutputModes(...)`, `.Build()` and an implicit conversion to `AgentCard`. Defaults: `text/plain` in and out, version `1.0.0`, no interfaces (hosting fills `supportedInterfaces`, as `A2AServerBuilder.AddAgent` documents). Signing and serving stay with 04 and 09.

`AddAgent` overloads that take an `AgentCard` value (not only `Func<IServiceProvider, AgentCard>`), and a delegate. `AddA2AServer()` with no argument is required from 09/02 for the shortest program (today it needs `Action<A2AServerOptions>`, `A2AServiceCollectionExtensions.cs:23`).

Target programs (they pin the surface; the builder may adjust exact names with 02):

```csharp
// 1. Echo: stateless reply
builder.Services.AddA2AServer()
    .AddAgent("echo", AgentCardBuilder.Create("Echo", "Repeats text."),
        (ctx, reply, ct) => reply.TextAsync($"echo: {ctx.UserText}", ct));
app.MapA2A();

// 2. Task agent with progress, artifact and a question (class, scoped DI)
public sealed class ReportAgent(IReportService reports) : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext ctx, IAgentEventSink sink, CancellationToken ct)
    {
        var task = new TaskUpdater(ctx, sink);
        if (ctx.UserText.Length < 3) { await task.RequireInputAsync("Which topic?", ct); return; }
        await task.StartWorkAsync("Collecting data", ct);
        await using var report = task.OpenArtifact("report.md", "text/markdown");
        await foreach (var chunk in reports.WriteAsync(ctx.UserText, ct)) await report.AppendAsync(chunk);
        await task.CompleteAsync(ct: ct);
    }
}

// 3. LLM agent (IChatClient is already in DI)
builder.Services.AddA2AServer().AddChatAgent("assistant", AgentCardBuilder.Create("Assistant", "Answers questions."),
    o => o.Instructions = "You are a helpful assistant.");
```

### 10. Migrations from current code

| Current | Change |
|---|---|
| `AgentRequestContext` positional record | Sealed class, same property names, plus new members. Tests and samples that call the constructor use `AgentRequestContext.Create`. |
| `IAgentEventSink.UpdateStatusAsync` / `AddArtifactAsync` | Add `AgentEventOptions` overloads. Old shape stays as default methods. |
| `AgentEventSink.ReplyAsync` keeps agent `ContextId` | Always the context's id. |
| Sink throws `InvalidAgentResponse` after cancel | Throws `OperationCanceledException`. |
| `TaskManager.SendMessageAsync` waits for first event | Grace window, then task `SUBMITTED`. |
| `TaskManager.InvokeHandlerAsync` `ActivatorUtilities` fallback | `AddAgent<T>` registers `T` scoped. Fallback stays for catalogs built by hand. |
| `RequestValidator.ResolveExtensions` in `TaskManager` | Moves to 03's extension middleware in 09's pipeline (M1); this package adds `A2AExtension` to it. |
| `EchoAgent`, `ReportAgent`, `BookingAgent`, `SecureAgent` in `samples/AgentServer` | Rewritten with `TaskUpdater`; `BookingAgent` uses `GetConversationAsync`/task metadata instead of regex over history; `ReportAgent` keeps `IChatClient` but streams through `ArtifactWriter`. |

## How it meets the six principles

1. **Protocol.** The multi-turn, cancel, extension and returnImmediately rules are the spec rules (listed in Constraints), each with a test. Check: conformance tests in this package map one-to-one to spec sections 3.1.1, 3.1.2, 3.4, 4.6, and 03's TCK run passes with the new TaskManager behavior.
2. **.NET enabler.** `AddAgent`, `AddChatAgent`, `AddExtension<T>`, named options, scoped DI, `IChatClient`/`AIFunction`, `JsonTypeInfo`, XML docs, nullable annotations. Check: a new-user sample builds with `dotnet new web` plus one package reference and no other setup.
3. **Easy.** Check: the three target programs compile as shown (a test compiles them from the docs snippets); a line-count check in the sample tests (echo ≤ 8 lines in `Program.cs`, task agent ≤ 20 lines of agent code, chat agent ≤ 10 lines). Every error says what to do: for example a missing `IChatClient` throws at startup with `Register an IChatClient (for example services.AddChatClient(...)) or set ChatAgentOptions.ChatClientKey.`
4. **Performance.** Coalesced chunks, no per-call reflection, hand-written tool schema, text-part coalescing. Check: a test streams 2,000 one-word tokens at 1 ms spacing and expects at most 40 artifact events; a benchmark (package 14) shows `TaskUpdater` adds under 5% over the raw sink for 1,000 events, and a 100 KB streamed answer costs linear, not quadratic, time (compare 10 KB and 100 KB: ratio under 15).
5. **Modular and extendable.** The AI bridge is a separate package that references only the abstractions. Agent Framework is a further package. Extensions are classes, not core edits. Check: a dependency-graph test fails if `Ambinder.A2A.Server` references `Microsoft.Extensions.AI`, or the AI package references `Microsoft.Extensions.AI` (full) or an LLM provider.
6. **Integration pains.** Multi-turn ids (contextId derivation, `GetConversationAsync`), cancellation, extension negotiation, framework wrapping, remote agent as tool. Check: each pain point listed under Findings has at least one named acceptance test below.

## Dependencies

On other packages:

- **01/02:** names, public API tracking, and the zero-argument `AddA2AServer()`.
- **03:** confirms with the TCK: cancel of a CANCELED task, extension echo, extension check scope, `returnImmediately`.
- **05:** `ITaskStore.ListAsync` with `TaskQuery.ContextId` inside an `OwnerScope`, on 05's context-id index. Durable stores keep `AgentTask.Metadata` (the `TaskMetadata` merge needs it). 05 decided: no batching, one store write per artifact event (05 §7.10); the chat agent's coalescing (about 40 chunk events per answer) and the core's text-part merge keep the cost bounded, and 14 measures it with the 2,000-chunk benchmark. 05's new-task `messageId` dedupe (rule 1 in section 5).
- **06:** handler statelessness across pods; cancel forwarding.
- **07:** `A2ACaller`, `Owner`, `Tenant`, `User` meaning; `OwnerKey` (B7); the reduced caller (B8); the grant-holder rule (B6); `ITaskContinuationPolicy`, `TaskContinuationGrant` and the sink's `RequireAuthAsync(..., grant, ...)` overload.
- **09:** shutdown token and grace; `MapA2A`; zero-argument `AddA2AServer()`; the `IA2AMiddleware` pipeline and `A2AOperationContext.ResponseMetadata` that the extension hooks run on.
- **03:** the extension-negotiation middleware (D5) that this package extends, and the `IAgentCardContributor` seam for card entries.
- **10:** `AddA2AClient(name, ...)`, per-call extension headers, and the wait helper (pain point 9, `SendAndWaitAsync` with a wait-options type; exact names set by 10). `A2AAgentTool` is built on them.

Provides to others (exact names):

- To **12:** `AgentRequestContext.Create`, `TaskUpdater`, `ArtifactWriter`, `AgentEventOptions`, `A2AExtension`, `ExtensionRequestContext`, `ExtensionOutgoingContext`, `ChatClientAgentHandler`, `A2AChatMapper`, `A2AAgentTool`.
- To **13:** the docs sections: "Write your first agent", "Multi-turn conversations", "Cancellation and errors", "DI scope of an agent", "Writing an extension", "LLM agents with IChatClient", "Calling an agent from an LLM".
- To **14:** benchmark targets above.
- To **16:** the sample agents rewritten here are the interop SUT agents.

## Acceptance criteria

- [ ] `AgentRequestContext` has the members in section 2. Unit tests cover `Kind`, `IsStreaming`, `Metadata`, `UserText`, `GetMetadata<T>`.
- [ ] `GetConversationAsync` tests: other owner's tasks excluded; artifacts included as agent messages; de-duplication; `MaxMessages`/`MaxTasks`; continuation vs new task ordering.
- [ ] A recovered or forwarded run's `Caller` is the reduced caller with no token (B8).
- [ ] `TaskUpdater` tests: each method sends the right state, message ids/roles/context ids, `RequireAuthAsync` always has a message and passes its grant to the sink (a granted owner can continue, others get `TaskNotFound`), `ArtifactWriter` sends append false then true then `lastChunk`.
- [ ] Sink tests: event metadata reaches the wire event; task metadata merge patch; stored artifact merges adjacent text parts while the event keeps the chunk; reply forces the context's `contextId`; sink throws `OperationCanceledException` after cancel.
- [ ] Error tests: `A2AException` before first event returns that code and stores no task (non-streaming and streaming); after the task exists it fails with the message; other exceptions give `Agent error (ref …)` and the same ref is in the log.
- [ ] Cancellation tests: token cancelled on CancelTask; task `CANCELED` even if the agent ignores the token, throws, or `CancelAsync` hangs past `CancelTimeout`; second cancel of a `CANCELED` task returns it; cancel of `COMPLETED` throws `TaskNotCancelable`.
- [ ] `returnImmediately` tests: agent that sleeps 5 s still returns within grace + 200 ms with a `SUBMITTED` task; late `ReplyAsync` becomes `COMPLETED`.
- [ ] DI tests: handler with a missing dependency fails at startup; scoped service is fresh per execution and per cancel; `HttpContext` is null; delegate agent uses `context.Services`.
- [ ] Extension tests: card entry generated; required missing → `ExtensionSupportRequired`; unknown URI ignored; version URI mismatch is not activated; request hook can reject before a task exists; outgoing hook sets metadata and the `extensions` marker; response header/metadata echo on JSON-RPC, REST and gRPC; the sample timestamp and audit extensions run on `A2AExtension`.
- [ ] AI tests with `ScriptedChatClient` (package 12): mapping round trip for text/raw/url/data; streaming produces coalesced chunks (≤ 40 events for 2,000 tokens); cancel mid-stream ends `CANCELED`; provider error gives `The model call failed.` with no provider text; `Reply = Message` stores no task; second turn sees the first turn's answer.
- [ ] Tool tests with a hosted fake agent: completed, input-required (second call with `taskId` completes), a `taskId` the tool did not create for this owner is refused without a call (B9), the default call uses the app identity and OBO only when opted in (B9), failed, timeout, cancel of the tool call cancels the remote task, derived `contextId` stable per parent and distinct per remote agent, result cut at `MaxResultChars`.
- [ ] Target-program compile tests and line-count checks pass.
- [ ] Dependency-graph test (Principle 5) passes. Trim/AOT analyzers report no warnings for `Ambinder.A2A.Extensions.AI`.
- [ ] TCK (03) and interop (16) runs still pass after the TaskManager changes.
- [ ] Docs sections listed under Dependencies exist (with 13).
- [ ] Benchmarks in Principle 4 exist and meet the numbers.

## Left to the builder

- Exact default for `ConversationOptions` caps if tests show better numbers.
- Internal class layout (`ExtensionPipeline`, `ConversationReader`, `DelegateAgentHandler`).
- Wording of status texts such as `Calling <name>`.
- The hash scheme for the derived `contextId`, if it stays a stable UUID.
- Whether `AgentResponder` is a struct or a class.
- Extra helper overloads (for example `AddTextArtifactAsync` with a `ReadOnlyMemory<char>`).
- The `IA2AToolProvider` cache details.

## Open questions

None. Server middleware (Q1) was settled by the controller: package 09 owns `IA2AMiddleware`. The Agent Framework bridge (Q2) is Claude's call C12 (section 8.4).
