# SDK package 11: Agent authoring and AI bridges — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task by task, with every subagent on model `sonnet` (see "Execution notes" at the end). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give agent authors a small, clear model on top of `IAgentHandler`/`IAgentEventSink`: a richer `AgentRequestContext`, `TaskUpdater`/`ArtifactWriter`, delegate agents, written and tested rules for multi-turn, cancellation, errors and DI scope, a real `A2AExtension` framework, and a new package `Ambinder.A2A.Extensions.AI` that serves an `IChatClient` as an agent and turns a remote agent into an `AIFunction`. Plus the `Ambinder.A2A.Extensions.AI.AgentFramework` bridge (C12).

**Spec (binding):** `docs/superpowers/specs/2026-11-03-sdk-11-authoring-ai-design.md` ("the spec"; "§N" below means its Design section N). Work plan: `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md` (B6, B7, B8, B9, C12). Package map: `docs/superpowers/specs/2026-11-03-sdk-01-packaging-release-design.md` §1.

**Architecture:**
- Authoring helpers live in `Ambinder.A2A.Server`, root namespace `Ambinder.A2A.Server` (package 02's day-one rule). Extensions live in `Ambinder.A2A.Server.Extensions`.
- `IAgentHandler` stays the only contract the framework calls. Delegate agents (level 1) and `TaskUpdater` (level 2) are helpers over it; the raw sink (level 3) stays.
- Extension activation, the required check and the echo stay in the **one** extension-negotiation middleware that 03/09 built (09 §7.11 step 4). This package adds the `A2AExtension` registry and its request hook to that step, and the outgoing hook to the sink (under the task lock).
- `Ambinder.A2A.Extensions.AI` depends on `Ambinder.A2A.Server`, `Ambinder.A2A.Client` and `Microsoft.Extensions.AI.Abstractions` only. `Ambinder.A2A.Extensions.AI.AgentFramework` adds `Microsoft.Agents.AI`.

**Tech Stack:** .NET 10, `Microsoft.Extensions.AI.Abstractions` 10.11.0 (pinned in `Directory.Packages.props`; use what is there), `System.Text.Json` source generation and `JsonNode` (AOT-safe), xUnit v3, ASP.NET Core `TestServer`, `FakeTimeProvider`.

---

## 0. Assumptions and prerequisites

### 0.1 Package 01 has landed (assumed)

This plan assumes package 01's rename and split are merged into `master`. It uses 01's names:

| Thing | Name after 01 |
|---|---|
| Solution | `Ambinder.A2A.slnx` |
| Server core (agents, task manager, sink) | `src/Ambinder.A2A.Server/` |
| Transports and hosting | `src/Ambinder.A2A.AspNetCore/` |
| Client | `src/Ambinder.A2A.Client/` |
| New in this plan | `src/Ambinder.A2A.Extensions.AI/`, `src/Ambinder.A2A.Extensions.AI.AgentFramework/` (01 may have created empty projects; reuse them) |
| Tests | `tests/Ambinder.A2A.Server.Tests`, `tests/Ambinder.A2A.Integration.Tests`, new `tests/Ambinder.A2A.Extensions.AI.Tests`, new `tests/Ambinder.A2A.Extensions.AI.AgentFramework.Tests` |
| Dependency allow-list | `eng/package-dependencies.json` and its test (01 §6) |

Current code is named in this plan by **file name** (for example `TaskManager.cs`, `AgentEventSink.cs`, `AgentRequestContext.cs`). After 01 and the M1 packages, find each with `git ls-files | grep <FileName>`. The spec's line numbers refer to the Phase 1 tree; use them only to find the code they describe.

**Steps that depend on package 01** (they use its names, layout or allow-list):
- Task 0 (checks 01's layout).
- Every task: project paths, `Ambinder.A2A.*` namespaces, test project names, `Ambinder.A2A.slnx` in commands.
- Task 14 and Task 19: creating `Ambinder.A2A.Extensions.AI` and `Ambinder.A2A.Extensions.AI.AgentFramework` per the package map, with README, description, AOT flag (yes for Extensions.AI, no for AgentFramework) and `eng/package-dependencies.json` entries.
- Task 23: the dependency-graph test extends 01's allow-list test.

If Task 0 finds that 01 has **not** landed: stop and report. Do not do the rename here.

### 0.2 Other packages this plan needs

Package 11 is M2, so most M1 packages should be merged when this runs. Task 0 records which seams exist. **Gate rule (binding):** a task whose seam is missing is not started. Do not build a stand-in for another package's contract (no local pipeline, no copied `TaskQuery`, no local wait helper). Report blocked tasks and go on with unblocked ones. The one exception is test-only code, listed in the table.

| Seam | Owner | Needed by | If missing |
|---|---|---|---|
| `IA2AMiddleware`, `A2AOperationContext.ResponseMetadata`, the extension-negotiation middleware in step 4 (slot 400) with its internal `IExtensionHook` list and `A2AExtensionsKeys.Active` (09 plan Task 6) | 09 (+03 D5) | Tasks 11, 12 | Stop before Task 11. |
| `AgentOptions` and the `AddAgent(…, Action<AgentOptions>)` overload (09 plan Task 4) | 09 | Tasks 4, 6, 11 | Stop before Task 4. |
| Zero-argument `AddA2AServer()` (09 plan, §7.2 task) | 09 | Tasks 4, 22 | Tasks 4 and 22 use `AddA2AServer(o => {})` and record the gap in the PR body. |
| `AgentExecutionHost` (run scope, shutdown token and grace) | 09 | Tasks 6, 8 | Implement in `TaskManager` where the run is started today. |
| `AgentRequestContext.Recovery` (09) | 09 | Task 1 | Keep whatever exists; nothing to add. |
| `ITaskStore.ListAsync(TaskQuery)` with `ContextId` inside `OwnerScope`, `AgentTask.Metadata` kept by every store, new-task `messageId` dedupe | 05 | Tasks 2 (merge patch round-trip), 9, 10 | Stop before Task 9. Task 2's metadata test runs on the in-memory store only. |
| `A2ACaller`, `OwnerKey`, `TaskContinuationGrant`, `IAgentEventSink.RequireAuthAsync(Message, TaskContinuationGrant?, CancellationToken)`, `AgentRequestContext.Caller`, `AuthorizeSkillAsync`, reduced caller | 07 | Tasks 1, 3, 10 | Stop before Task 1 (the context shape depends on it). |
| Cancel of a `CANCELED` task returns the task; D13 transition table (`TaskStateRules`) | 03 (plan Task 17) | Task 6 | Implement the idempotent cancel in Task 6 as the spec says (§6 cancellation rule 5) and note it in the PR body. |
| `IAgentCardContributor` (may append to `capabilities.extensions`) | 03 (plan Task 13) | Task 11 | Stop before Task 11. |
| `AddA2AClient`, `IA2AClientFactory`, `IA2AClient` with `A2ARequestOptions` (per-call `Extensions`, `Credentials`, `UserAssertion`), `SendAndWaitAsync`, `A2AWaitOptions`, `A2AResult`, `CancelTaskAsync(string)` | 10 | Tasks 18, 19 | Stop before Task 18. |
| `EntraOnBehalfOfCredentialProvider` and `AddCallerAssertionForOnBehalfOf()` | 07 | Task 18 (OBO opt-in test) | Test OBO opt-in with a `DelegateCredentialProvider` that records `A2ARequestOptions.UserAssertion`; note it. |
| `ScriptedChatClient` (`Ambinder.A2A.Testing.AI`) | 12 | Tasks 16–18 | *(test-only exception)* `Ambinder.A2A.Testing.AI` depends on `Ambinder.A2A.Extensions.AI`, so 12 cannot ship it first. Task 15 writes a test double `tests/Ambinder.A2A.Extensions.AI.Tests/Fakes/ScriptedChatClient.cs`. Package 12 moves it into `Ambinder.A2A.Testing.AI` later. |
| Test harness (`A2ATestServer`, `AgentHarness` or the M1 slice `tests/Ambinder.A2A.TestHarness`) | 12 (B2) | integration tests | Use `WebApplication.CreateSlimBuilder()` + `UseTestServer()`. No fixed ports, no sleeps; use `FakeTimeProvider` or gates. |
| `PublicAPI.Unshipped.txt` | 02 | every task with public API | Add each new public member there in the same task. If absent, list the API in the PR body. |
| `benchmarks/Ambinder.A2A.Benchmarks` | 14 | Task 24 | Skip Task 24; leave its acceptance item open. |
| TCK job and interop suite | 03, 16 | Task 26 | Report "not available" in the PR body. |

### 0.3 Decisions this plan makes (plan-writer's calls)

1. **Where the hooks run.** The request hook (`OnRequestAsync`) runs in 09's extension-negotiation middleware through its internal `IExtensionHook` list, after activation and the required check (spec §7 behaviors 2, 3, 5). The outgoing hook (`OnOutgoingAsync`) runs in `AgentEventSink`, under the task lock, just before each event is stored (spec §7 behavior 6: "before it is stored … under the task lock"). There is no second negotiation path.
2. **Plain text part** (for the stored-artifact coalescing, spec §4): a `Part` with `Text` set and no `Metadata`, `Filename` or `MediaType` other than null or `text/plain`. Only adjacent parts of that kind are merged in storage. Wire events keep the chunk as sent.
3. **JSON merge patch** (RFC 7386, spec §4 `TaskMetadata`): an internal `JsonMergePatch.Apply(JsonElement? target, JsonElement patch) → JsonElement?` on `System.Text.Json.Nodes` (AOT-safe, no reflection), with the RFC 7386 appendix test vectors.
4. **Error reference id** (spec §6 errors rule 3): `Activity.Current?.TraceId.ToHexString()` when there is an activity, else `Guid.NewGuid().ToString("N")`. The log uses the property name `ErrorRef`.
5. **Derived remote `contextId`** (spec §8.3): first 16 bytes of `SHA-256(UTF-8(parentContextId + "\n" + remoteAgentUrl))`, where the URL is the selected interface URL, lower-cased scheme and host, no trailing `/`. Set the version nibble to 8 and the variant bits to `10`. Format `D`.
6. **Tool id binding storage** (B9): in the parent task's metadata under the key `ambinder.a2a.tools` (written through `AgentEventOptions.TaskMetadata`, so it survives pods and recovery): `{ "<remote agent url>": { "owner": "<parent owner key>", "contextId": "...", "taskIds": ["..."] } }`, keeping the last 50 task ids per remote agent. Outside an agent run (no ambient scope) the bindings are kept in the tool instance.
7. **`ScriptedChatClient` test double** (see §0.2): test code only, never shipped.
8. **Docs pages** use package 13's tree (see Task 25 for the mapping from the spec's section names to files).

---

## Global Constraints

- **Branch:** the session's branch, from the latest `master`. One commit per task at least (Conventional Commits). Push after each task.
- **Build rules:** `TreatWarningsAsErrors`, nullable on, central package management, XML docs on every public member (`CS1591`), match the surrounding style.
- **AOT and trim (Goal 14):** no reflection on user types; every `JsonSerializer` call passes a `JsonTypeInfo` (from `A2AJson` typed accessors or a source-generated context); the tool JSON schema is a hand-written constant. `Ambinder.A2A.Extensions.AI` has `IsAotCompatible=true` and builds with zero trim/AOT analyzer warnings.
- **Dependencies (principle 5):** `Ambinder.A2A.Server` gets no `Microsoft.Extensions.AI*` reference. `Ambinder.A2A.Extensions.AI` references only `Microsoft.Extensions.AI.Abstractions` (never `Microsoft.Extensions.AI`) and no LLM provider.
- **Agents never mint ids:** no helper may let agent code set `TaskId`, `ContextId`, `Role` or `MessageId` (Goal 2).
- **Untrusted text:** provider error text, stack traces and remote-agent replies never reach a task message (unless `ExposeErrors`), and remote replies are tool output, never instructions (anti-goals).
- **No per-token events:** chat streaming always goes through `ArtifactWriter` with coalescing.
- **Exact strings:** use the spec's texts verbatim: `Agent error (ref <id>)`, `The model call failed.`, `{"status":"failed","error":"unknown taskId"}`, the missing-`IChatClient` message (Task 16).
- **Tests:** TDD per task (failing test → see it fail → implement → see it pass → commit). No fixed ports or sleeps. A test that needs time uses `FakeTimeProvider` or a gate; the two spec tests that use real time ("agent sleeps 5 s", "2,000 tokens at 1 ms spacing") use a fake clock or a scripted stream with explicit flush points instead, and assert the same bound.

Commands from the repo root:

```bash
dotnet build Ambinder.A2A.slnx
```

```bash
dotnet test Ambinder.A2A.slnx
```

## Review Focus

1. **Cancel can never be undone by the agent.** An agent that ignores the token, throws, or hangs in `CancelAsync` still ends `CANCELED`, and its later sink calls throw `OperationCanceledException` (Task 6).
2. **`returnImmediately` returns in time** even when the agent never emits an event; a late reply becomes `COMPLETED` only for `returnImmediately` calls (Task 7).
3. **Errors before the first event store nothing**, for blocking and streaming calls (Task 5).
4. **`GetConversationAsync` never returns another owner's or tenant's tasks** with the same `contextId` (Task 9).
5. **Extension URIs match exactly**; a `…/v2` request does not activate a `…/v1` extension (Task 12).
6. **Tool id binding (B9):** a model-supplied `taskId` the tool did not create for this owner is refused without a network call (Task 18).
7. **No provider text leaks** into task messages by default (Task 16).
8. **Streaming cost is linear** (text-part coalescing in storage, Task 2; benchmark, Task 24).

---

## File map

Created (`src/Ambinder.A2A.Server/`): `Agents/AgentRequestKind.cs`, `Agents/ConversationOptions.cs`, `Agents/ConversationReader.cs` (internal), `Agents/AgentEventOptions.cs`, `Agents/TaskUpdater.cs`, `Agents/ArtifactWriter.cs`, `Agents/AgentResponder.cs`, `Agents/AgentEventSinkExtensions.cs`, `Agents/DelegateAgentHandler.cs` (internal), `Agents/AgentCardBuilder.cs`, `Tasks/JsonMergePatch.cs` (internal), `Tasks/TextPartCoalescer.cs` (internal), `Extensions/A2AExtension.cs`, `Extensions/ExtensionRequestContext.cs`, `Extensions/ExtensionOutgoingContext.cs`, `Extensions/ExtensionOutgoingKind.cs`, `Extensions/ExtensionRegistry.cs` (internal), `Extensions/ExtensionCardContributor.cs` (internal), `Extensions/A2AExtensionHook.cs` (internal, implements 09's `IExtensionHook`).
Modified: `AgentRequestContext.cs`, `IAgentEventSink.cs`, `IAgentHandler.cs` (XML docs), `AgentEventSink.cs`, `TaskManager.cs` (and `AgentExecutionHost.cs` if 09 moved runs there), `TaskWriter.cs`, `A2AServerBuilder.cs`, `A2AServerOptions.cs` (`ReturnImmediatelyGrace`), `AgentOptions.cs` (`CancelTimeout`, `UseExtension<T>()`), `A2AStartupValidation.cs`, `ExtensionNegotiationMiddleware.cs` (09).
Created: `src/Ambinder.A2A.Extensions.AI/` (`A2AChatMapper.cs`, `ChatAgentOptions.cs`, `ChatAgentReplyMode.cs`, `ChatClientAgentHandler.cs`, `ChatAgentServerBuilderExtensions.cs`, `StreamingArtifactPump.cs` (internal), `AgentExecutionScope.cs`, `A2AAgentTool.cs`, `A2AToolOptions.cs`, `IA2AToolProvider.cs`, `RemoteAgentToolProvider.cs` (internal), `ToolSchema.cs` (internal), `ToolJsonContext.cs` (internal), `README.md`); `src/Ambinder.A2A.Extensions.AI.AgentFramework/` (`AgentFrameworkServerBuilderExtensions.cs`, `AgentFrameworkAgentHandler.cs`, `README.md`).
Samples: `samples/AgentServer/Agents/{EchoAgent,ReportAgent,BookingAgent,SecureAgent}.cs`, `samples/AgentServer/Extensions/{TimestampExtension,AuditExtension}.cs`, `samples/AgentServer/Cards/AgentCards.cs`; new target-program samples under `samples/Authoring/` (Task 23).
Docs: `docs/agents/{write-an-agent,task-lifecycle,input-and-auth-required,streaming-and-artifacts,extensions,llm-agents,call-other-agents}.md`.

---

## Phase A — Authoring core (`Ambinder.A2A.Server`)

### Task 0: Branch and precondition check

**Files:** none. Results go into the PR body.

- [ ] **Step 1:** `git fetch origin && git checkout -B <session branch> origin/master`.
- [ ] **Step 2: Confirm 01.**

```bash
test -f Ambinder.A2A.slnx && ls src | grep -E '^Ambinder\.A2A\.(Server|AspNetCore|Client)$'
```

```bash
git grep -nE 'namespace A2A\.' -- src | head
```

Expected: the slnx and the three folders exist; the grep prints nothing. Otherwise stop: "package 01 has not landed".
- [ ] **Step 3: Record the seams of §0.2** (a hit = present):

```bash
git grep -nE 'interface IA2AMiddleware|class ExtensionNegotiationMiddleware|interface IExtensionHook|A2AExtensionsKeys' -- src
```

```bash
git grep -nE 'class AgentOptions|Action<AgentOptions>|AddA2AServer\(this IServiceCollection services\)' -- src
```

```bash
git grep -nE 'record TaskQuery|ContextId \{ get; init; \}|OriginMessageId' -- src/Ambinder.A2A.Server
```

```bash
git grep -nE 'record A2ACaller|struct OwnerKey|record TaskContinuationGrant|AuthorizeSkillAsync|RequireAuthAsync\(Message' -- src
```

```bash
git grep -nE 'interface IAgentCardContributor|SendAndWaitAsync|class A2AWaitOptions|interface IA2AClientFactory' -- src
```

```bash
git grep -nE 'class ScriptedChatClient' -- src tests; ls tests benchmarks 2>/dev/null; git grep -l 'PublicAPI.Unshipped' -- src | head -3
```

- [ ] **Step 4:** Write the gate table (seam → present/missing → tasks blocked) into the PR body draft. Run `dotnet build Ambinder.A2A.slnx` and `dotnet test Ambinder.A2A.slnx` for a green baseline. No commit.

### Task 1: `AgentRequestContext` becomes a sealed class with the new members (§2)

**Files:**
- Modify: `AgentRequestContext.cs`; every place that constructs it (`git grep -n "new AgentRequestContext("`), including `TaskManager.cs`, recovery (09), tests and samples.
- Create: `Agents/AgentRequestKind.cs`
- Test: `tests/Ambinder.A2A.Server.Tests/Agents/AgentRequestContextTests.cs`

- [ ] **Step 1: Write failing tests** (acceptance: "`AgentRequestContext` has the members in section 2. Unit tests cover `Kind`, `IsStreaming`, `Metadata`, `UserText`, `GetMetadata<T>`"):
  - Through the in-process handler: a new message → `Kind == NewTask`, `IsContinuation == false`, `IsCancel == false`; a continuation of an `INPUT_REQUIRED` task → `Continuation`; the cancel call → `Cancel`.
  - `IsStreaming` is true for `SendStreamingMessage`, false for `SendMessage`.
  - `Metadata` equals `SendMessageRequest.Metadata`; null for cancel.
  - `UserText` joins text parts with `\n`; empty string with no text parts.
  - `GetMetadata<T>(key, info)` reads from request metadata first, then message metadata *(the spec says "request or message metadata"; request wins — plan-writer's call, stated in the XML doc)*; returns `default` when missing; uses only the given `JsonTypeInfo<T>`.
  - `Tenant`, `Owner` (= `Caller.Owner`), `User` (= `Caller.Principal`), `ProtocolVersion`, `Transport` copied from the call.
  - `Services` is the execution scope's provider (resolve a scoped marker service and compare with the one the handler got).
  - `AgentRequestContext.Create(...)` (public factory for package 12) builds a context with explicit values.
- [ ] **Step 2:** `dotnet test tests/Ambinder.A2A.Server.Tests --filter "FullyQualifiedName~AgentRequestContextTests"`. Expected: FAIL (compile).
- [ ] **Step 3: Implement.** Sealed class, get-only properties, same names as today (`AgentName`, `TaskId`, `ContextId`, `Message`, `CurrentTask`, `User`, `ActiveExtensions`, `Configuration`), plus what 07 and 09 added (`Caller`, `AuthorizeSkillAsync`, `Recovery`), plus the spec's new members. `ActivatedExtensions` and `Extension<T>()` are added in Task 12 (leave them out here). `GetConversationAsync` is added in Task 9. The internal constructor is used by the server; `Create` takes named optional parameters so it can grow.
- [ ] **Step 4:** Replace constructor calls. Run the full suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server)!: AgentRequestContext as a class with kind, metadata, caller and services`.

### Task 2: Sink changes — event options, task metadata merge, text coalescing, reply context id, cancel exception (§4)

**Files:**
- Create: `Agents/AgentEventOptions.cs`, `Tasks/JsonMergePatch.cs` (internal), `Tasks/TextPartCoalescer.cs` (internal)
- Modify: `IAgentEventSink.cs`, `AgentEventSink.cs`, `TaskWriter.cs`, test doubles implementing `IAgentEventSink`
- Test: `tests/Ambinder.A2A.Server.Tests/Tasks/AgentEventSinkTests.cs` (extend), `JsonMergePatchTests.cs`, `TextPartCoalescerTests.cs`

- [ ] **Step 1: Write failing tests** (acceptance "Sink tests"):
  - `UpdateStatusAsync(state, msg, new AgentEventOptions { EventMetadata = e }, ct)` → the wire `TaskStatusUpdateEvent.Metadata == e` (streaming subscriber sees it). Same for `AddArtifactAsync` → `TaskArtifactUpdateEvent.Metadata`.
  - `TaskMetadata` is merged as RFC 7386 into `AgentTask.Metadata`: add a key, replace a key, delete with `null`, nested objects; a later turn reads it in `CurrentTask.Metadata`.
  - `JsonMergePatchTests`: every example in RFC 7386 Appendix A.
  - Coalescing: three `AddArtifactAsync` appends of plain text `"a"`, `"b"`, `"c"` → the stored artifact has **one** part `"abc"`; the three wire events each carry one chunk. A text part with metadata, or a file part between texts, is not merged.
  - Old two-argument overloads still work and pass `null` options (default interface methods).
  - `ReplyAsync` with a message whose `ContextId` is `"other"` → the reply's `contextId` is the request context's id.
  - After CancelTask, `UpdateStatusAsync` and `AddArtifactAsync` on the run's sink throw `OperationCanceledException` whose `CancellationToken` is the execution token. After the task reached a terminal state by another path, the same.
  - Illegal use (reply after a task event; a bad transition) still throws `A2AException(InvalidAgentResponse)`.
- [ ] **Step 2:** Run the filters. Expected: FAIL.
- [ ] **Step 3: Implement.** `public sealed record AgentEventOptions { public JsonElement? EventMetadata { get; init; } public JsonElement? TaskMetadata { get; init; } }`. New sink members exactly as spec §4; the old overloads become default methods on the interface. Merge and coalescing happen under the task lock in the writer path. Do not touch 07's `RequireAuthAsync(Message, TaskContinuationGrant?, CancellationToken)`; add no options overload to it unless 02 asks.
- [ ] **Step 4:** Run the full suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): event and task metadata, text-part coalescing, cancel-safe sink`.

### Task 3: `TaskUpdater`, `ArtifactWriter`, sink extension (§3)

**Files:**
- Create: `Agents/TaskUpdater.cs`, `Agents/ArtifactWriter.cs`, `Agents/AgentEventSinkExtensions.cs`
- Test: `tests/Ambinder.A2A.Server.Tests/Agents/TaskUpdaterTests.cs`, `ArtifactWriterTests.cs`

- [ ] **Step 1: Write failing tests** (acceptance "`TaskUpdater` tests"), with a recording sink:
  - Each method sends the right state: `StartWorkAsync` → `WORKING`, `CompleteAsync` → `COMPLETED`, `FailAsync` → `FAILED`, `RejectAsync` → `REJECTED`, `RequireInputAsync` → `INPUT_REQUIRED`, `RequireAuthAsync` → `AUTH_REQUIRED`, `CancelAsync` → `CANCELED`.
  - Methods with text build a message with a new `messageId` (unique per call), `Role.Agent`, the context's `TaskId` and `ContextId`. With `text == null` no message is sent (where the parameter is optional).
  - `RequireAuthAsync(prompt, grant)` always sends a status message and passes `grant` to 07's sink overload. Integration: with `OwnerKey.ForApp(tid, "approver")` the approver can continue; another app gets `TaskNotFound`.
  - `AddArtifactAsync` with no id uses a new GUID in `N` format; `AddTextArtifactAsync` sets the media type.
  - `ArtifactWriter`: first write `append: false`; later writes `append: true`; `CompleteAsync` sends `lastChunk: true` once; `DisposeAsync` without error sends `lastChunk: true`; `DisposeAsync` after an exception sends nothing; the id is fixed at `OpenArtifact`.
  - `NewMessage(parts)` builds an agent message with the right ids.
  - `ReplyTextAsync(context, text, ct)` on `IAgentEventSink` replies with a text message.
  - `TaskUpdater` holds no lock and no state beyond the context and sink (two updaters on one context interleave correctly).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement exactly the API in spec §3 (namespace `Ambinder.A2A.Server`).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): TaskUpdater and ArtifactWriter`.

### Task 4: Delegate agents, `AgentResponder`, `AgentCardBuilder`, `AddAgent` overloads (§1, §3, §9)

**Files:**
- Create: `Agents/AgentResponder.cs`, `Agents/DelegateAgentHandler.cs` (internal), `Agents/AgentCardBuilder.cs`
- Modify: `A2AServerBuilder.cs`
- Test: `tests/Ambinder.A2A.Server.Tests/Agents/DelegateAgentTests.cs`, `AgentCardBuilderTests.cs`

- [ ] **Step 1: Write failing tests.**
  - `AddAgent("echo", AgentCardBuilder.Create("Echo", "Repeats text."), (ctx, reply, ct) => reply.TextAsync($"echo: {ctx.UserText}", ct))` answers `echo: hi` on SendMessage (direct message, no task stored).
  - A delegate that uses `reply.Task` (a `TaskUpdater`) runs a task to `COMPLETED`.
  - The delegate reads a scoped service through `ctx.Services`; it is a fresh instance per execution.
  - `AgentCardBuilder`: defaults `text/plain` in/out, version `1.0.0`, no interfaces; `.WithSkill(...)`, `.Streaming()`, `.Push()`, `.WithInputModes(...)`, `.WithOutputModes(...)`, `.Build()`; the implicit conversion to `AgentCard` equals `.Build()`.
  - `AddAgent<T>(name, AgentCard card)` (a value, not only a factory) works, also combined with 09's `Action<AgentOptions>` overload.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.** `AgentResponder` with `TextAsync(string, ct)`, `Task` (`TaskUpdater`), `Sink` (struct or class is the builder's choice). `DelegateAgentHandler : IAgentHandler` resolves the delegate from a singleton holder keyed by agent name. XML docs say closures must not capture scoped services.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): delegate agents and AgentCardBuilder`.

### Task 5: Error contract (§6 errors)

**Files:** Modify `TaskManager.cs` (or `AgentExecutionHost.cs`), `AgentEventSink.cs`. Test: `tests/Ambinder.A2A.Integration.Tests/Authoring/AgentErrorTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "Error tests"), theory over JSON-RPC, REST, gRPC and in-process:
  - Handler throws `A2AException(ContentTypeNotSupported, "x")` before any event, blocking call → that exact error code and message; no task in the store (`ListTasks` empty, `GetTask` of the would-be id → `TaskNotFound`).
  - Same with a streaming call → the error arrives before the first byte through the normal error mapping (HTTP status / JSON-RPC error / gRPC status), not as an SSE error frame.
  - After `StartWorkAsync`, `A2AException("bad input")` → task `FAILED` with status message text `bad input`.
  - After the task exists, `InvalidOperationException("secret detail")` → task `FAILED` with text matching `^Agent error \(ref [0-9a-f]{32}\)$`, and the captured error log has the same ref in `ErrorRef`; `secret detail` is not in the task.
  - With an `Activity` running, the ref equals its trace id.
  - Reaching the end without a final state still fails the task (unchanged behavior, keep the existing test).
- [ ] **Step 2:** Run. Expected: FAIL (at least the ref text and the streaming-before-first-byte case).
- [ ] **Step 3:** Implement per §0.3 item 4.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): agent error contract with reference ids`.

### Task 6: Cancellation contract (§6 cancellation)

**Files:** Modify `AgentOptions.cs` (add `CancelTimeout`, default 30 s), `TaskManager.cs`/`AgentExecutionHost.cs`. Test: `tests/Ambinder.A2A.Integration.Tests/Authoring/CancellationTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "Cancellation tests"), with `FakeTimeProvider` and gates:
  - The `ExecuteAsync` token is cancelled by CancelTask; a client disconnect of a streaming call does **not** cancel it.
  - Agent ignores the token (waits on a gate) → task `CANCELED`; its later `AddArtifactAsync` throws `OperationCanceledException` and does not change the task.
  - Agent throws after the token fires → still `CANCELED`, no error log for the expected `OperationCanceledException`.
  - `CancelAsync` override hangs → after `CancelTimeout` (advance the fake clock) the task is `CANCELED` and one warning is logged.
  - `CancelAsync` runs on a **new** handler instance in a **new** scope (instance and scoped-service identity differ from the run's).
  - Second cancel of a `CANCELED` task returns the task; cancel of a `COMPLETED` task → `TaskNotCancelable` (if 03 Task 17 landed, these already pass; keep them as contract tests).
  - A run that outlives its cancel by more than `CancelTimeout` logs once at Debug.
  - Host stop: the token fires after 09's shutdown grace (use 09's test seam if present; otherwise skip this case and list it).
- [ ] **Step 2:** Run. Expected: FAIL (timeout and Debug-log cases at least).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): bounded CancelAsync and a written cancel contract`.

### Task 7: `returnImmediately` grace (§5 rule 7)

**Files:** Modify `A2AServerOptions.cs` (`ReturnImmediatelyGrace`, default 100 ms), `TaskManager.cs`, `AgentEventSink.cs`. Test: `tests/Ambinder.A2A.Integration.Tests/Authoring/ReturnImmediatelyTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "`returnImmediately` tests"), using `FakeTimeProvider` (the spec's "sleeps 5 s, returns within grace + 200 ms" becomes: the agent waits on a gate, the test advances the fake clock by the grace, and the call returns before the gate is released):
  - Agent emits nothing → SendMessage with `returnImmediately: true` returns a task in `SUBMITTED`, stored, visible to `GetTask`.
  - Agent emits `WORKING` inside the grace → that task is returned.
  - Agent later calls `ReplyAsync` → the task becomes `COMPLETED` with the reply as status message.
  - Without `returnImmediately`, a reply after a task event is still `InvalidAgentResponse`.
  - Grace validated at startup (0 to 10 s; outside → `InvalidOperationException` naming `A2AServerOptions.ReturnImmediatelyGrace`) *(range is plan-writer's call)*.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): returnImmediately returns within a grace window`.

### Task 8: DI scope and startup validation (§6 DI)

**Files:** Modify `A2AServerBuilder.cs` (`TryAddScoped<THandler>()` in `AddAgent<THandler>`), `TaskManager.cs`/`AgentExecutionHost.cs` (resolve from the scope; keep the `ActivatorUtilities` fallback for hand-built catalogs), `A2AStartupValidation.cs`, `IAgentHandler.cs` (XML docs). Test: `tests/Ambinder.A2A.Integration.Tests/Authoring/AgentScopeTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "DI tests"):
  - A handler with a constructor dependency that is not registered fails **at startup** with a message naming the handler and the missing type (not at the first call).
  - A scoped dependency is a fresh instance per execution and per cancel, and is disposed when `ExecuteAsync` returns (even after the HTTP response finished: `returnImmediately` case).
  - `IHttpContextAccessor.HttpContext` is null inside `ExecuteAsync`.
  - A user registration of the handler type wins over `TryAddScoped` (register it as singleton first; the same instance is seen twice).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement.** Startup validation resolves each registered handler type once in a throwaway scope (or relies on `ValidateOnBuild` when the host enables it; do the explicit check so it works in Production too) and reports all failures together.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): scoped agent handlers validated at startup`.

### Task 9: `GetConversationAsync` (§2) — needs 05

**Files:** Create `Agents/ConversationOptions.cs`, `Agents/ConversationReader.cs` (internal). Modify `AgentRequestContext.cs`. Test: `tests/Ambinder.A2A.Server.Tests/Agents/ConversationTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "`GetConversationAsync` tests"):
  - Alice and Bob both use `contextId` `c1` → Alice's conversation has only her tasks. A task of tenant `b` with the same owner and context is excluded.
  - Order: tasks oldest first; inside a task, history in order, then (with `IncludeArtifactText`) one agent message with the artifact text parts.
  - Continuation: the current task's history comes from `CurrentTask.History`; for a new task the current `Message` is last.
  - De-duplication by `messageId`.
  - `MaxMessages` keeps the newest; `MaxTasks` caps tasks; `IncludeNonText = false` turns a file part into `[file report.csv, text/csv]`.
  - The result is cached for the life of the context (the store's `ListAsync` is called once for two calls; use a counting store decorator).
  - A reply (`ReplyAsync`) turn is not visible in the next turn (rule 5).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement** with one `ITaskStore.ListAsync(new TaskQuery { Scope = new OwnerScope(Tenant, Agent, Owner), ContextId = ContextId, PageSize = MaxTasks, IncludeArtifacts = IncludeArtifactText, HistoryLength = null }, ct)`; reverse to oldest first.
- [ ] **Step 4:** Run. Expected: PASS. If 05's EF Core store is present, run the same test class against it through 05's `StoreSet`.
- [ ] **Step 5:** Commit `feat(server): GetConversationAsync over the owner's context`.

### Task 10: Multi-turn contract tests (§5) and caller rules (B6, B8)

**Files:** Test only: `tests/Ambinder.A2A.Integration.Tests/Authoring/MultiTurnContractTests.cs`. Fix causes in `TaskManager.cs` if a test fails.

- [ ] **Step 1: Write the tests**, one per rule, named after it (spec 3.1.1, 3.1.2, 3.4):
  1. No `taskId` → new task id from the server, client `contextId` kept, `Kind = NewTask`; same owner + same `messageId` again → the existing task, handler ran once (05 dedupe). A continuation with a reused `messageId` is not deduped.
  2. `taskId` of an `INPUT_REQUIRED` task → `Continuation`; of a `WORKING` task → `UnsupportedOperation`; of a terminal task → rejected.
  3. Grant holder (07) gets its own continuation response, then `TaskNotFound` on Get/List/Subscribe/Cancel (B6).
  4. A continuation gets a new handler instance (field state from turn 1 is gone); `TaskMetadata` written in turn 1 is readable in `CurrentTask.Metadata` in turn 2.
  5. Reply creates no task.
  6. (docs only)
  7. covered by Task 7.
  8. Mismatching `contextId`/`taskId` rejected before the agent runs.
  - B8: in a recovered run (09 `Restart` policy, if present), `context.Caller` is the reduced caller and `AccessToken` is null. If 09's recovery is not merged, list this test as blocked.
- [ ] **Step 2:** Run. Expected: PASS, or fix the cause.
- [ ] **Step 3:** Commit `test(server): multi-turn contract`.

---

## Phase B — Extension framework (`Ambinder.A2A.Server.Extensions`) — needs 09 + 03

### Task 11: `A2AExtension`, registration, card entries (§7 behaviors 1, 7)

**Files:** Create `Extensions/A2AExtension.cs`, `Extensions/ExtensionRegistry.cs` (internal), `Extensions/ExtensionCardContributor.cs` (internal, an `IAgentCardContributor`). Modify `A2AServerBuilder.cs` (`AddExtension<T>()`), `AgentOptions.cs` (`UseExtension<T>()`). Test: `tests/Ambinder.A2A.Server.Tests/Extensions/ExtensionCardTests.cs`.

- [ ] **Step 1: Write failing tests.**
  - `AddExtension<TimestampTestExtension>()` → every agent's public card has one `capabilities.extensions` entry with the extension's `uri`, `description`, `required`, `params`.
  - `AddAgent(…, o => o.UseExtension<T>())` → only that agent's card has it.
  - A hand-written card entry with the same URI is kept and not duplicated.
  - The extension is a singleton (same instance across calls).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement `A2AExtension` exactly as spec §7 (abstract `Uri`; virtual `Required`, `Description`, `CardParams`, `OnRequestAsync`, `OnOutgoingAsync`).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): A2AExtension and card entries`.

### Task 12: Activation, required check, echo, request and outgoing hooks (§7 behaviors 2–7)

**Files:** Create `Extensions/ExtensionRequestContext.cs`, `Extensions/ExtensionOutgoingContext.cs`, `Extensions/ExtensionOutgoingKind.cs`, `Extensions/A2AExtensionHook.cs` (internal). Modify 09's `ExtensionNegotiationMiddleware.cs` (register the hook; feed the registry into activation and the required check), `AgentEventSink.cs` (outgoing hook under the lock), `AgentRequestContext.cs` (`ActivatedExtensions`, `Extension<T>()`). Remove any leftover `RequestValidator.ResolveExtensions` call in `TaskManager`. Test: `tests/Ambinder.A2A.Integration.Tests/Extensions/ExtensionFrameworkTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "Extension tests"), theory over JSON-RPC, REST, gRPC:
  - Required extension not requested → `ExtensionSupportRequired` listing the URI, on every operation except the public card fetch.
  - Unknown requested URI ignored; `…/v2` requested while `…/v1` registered → not activated, not echoed.
  - Activated URIs echoed: `A2A-Extensions` header (HTTP), `a2a-extensions` response metadata (gRPC), for unary and streaming.
  - Request hook: reads `GetMetadata<T>(info)` from `metadata[Uri]`; `Replace(message)` changes what the agent sees; `Reject(InvalidParams, "x")` fails the call before any task exists (no task stored).
  - Hooks of an inactive extension do not run.
  - Outgoing hook: `SetMetadata<T>` on a status message, an artifact and a reply merges into `metadata[Uri]` and adds `Uri` to that message's or artifact's `extensions`; hooks run in registration order.
  - `context.Extension<T>()` returns the instance when active, null otherwise; `ActivatedExtensions` lists instances; `ActiveExtensions` (URIs) unchanged.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement per §0.3 item 1. Typed metadata access uses only `JsonTypeInfo<T>`.
- [ ] **Step 4:** Run the full suite (03's extension tests must stay green). Expected: PASS.
- [ ] **Step 5:** Commit `feat(server): extension hooks on the negotiation middleware`.

### Task 13: Sample extensions on `A2AExtension`

**Files:** Create `samples/AgentServer/Extensions/TimestampExtension.cs`, `AuditExtension.cs`. Modify `samples/AgentServer/Cards/AgentCards.cs` (drop the hand-written `TimestampExtension` card entry), `samples/AgentServer/Agents/EchoAgent.cs` (drop its hand-written extension code), `samples/AgentServer/Program.cs`. Tests: existing sample/integration tests that assert the timestamp and audit behavior.

- [ ] **Step 1:** Make the existing extension tests assert through the new classes (they fail to compile or fail).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Port the behavior into the two `A2AExtension` subclasses; same URIs and metadata shapes as today.
- [ ] **Step 4:** Run. Expected: PASS (acceptance: "the sample timestamp and audit extensions run on `A2AExtension`").
- [ ] **Step 5:** Commit `refactor(samples): timestamp and audit extensions on A2AExtension`.

---

## Phase C — `Ambinder.A2A.Extensions.AI`

### Task 14: Project and `A2AChatMapper` (§8.1)

**Files:** Create `src/Ambinder.A2A.Extensions.AI/Ambinder.A2A.Extensions.AI.csproj` (refs `Ambinder.A2A.Server`, `Ambinder.A2A.Client`, package `Microsoft.Extensions.AI.Abstractions`; `IsAotCompatible=true`), `README.md`, `A2AChatMapper.cs`; `tests/Ambinder.A2A.Extensions.AI.Tests/`. Modify `Ambinder.A2A.slnx`, `eng/package-dependencies.json`.

- [ ] **Step 1: Write failing tests** for the mapping table (acceptance "mapping round trip for text/raw/url/data"):
  - `text` ↔ `TextContent`; `raw` + `mediaType` ↔ `DataContent` (bytes copied once: assert the `DataContent.Data` memory is not the source array when the source is a pooled buffer, or simply that the round trip equals); `url` + `mediaType` ↔ `UriContent`; `data` (JSON) → `DataContent` with `application/json`, and back to a data part.
  - `metadata`, `filename` ↔ `AdditionalProperties["a2a.metadata"]`, `["a2a.filename"]`.
  - Roles: `Role.User` ↔ `ChatRole.User`; `Role.Agent` ↔ `ChatRole.Assistant`; `messageId` ↔ `ChatMessage.MessageId`.
  - `TextReasoningContent` dropped unless `includeReasoning: true`; `FunctionCallContent`/`FunctionResultContent` not mapped by the mapper.
  - `ToChatMessages`/`ToParts`/`ToMessage` over lists.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Confirm the `Microsoft.Extensions.AI.Abstractions` 10.11.0 type names (`TextContent`, `DataContent`, `UriContent`, `TextReasoningContent`, `ChatMessage.MessageId`, `AdditionalPropertiesDictionary`) against the pinned package; if one differs, use the real name and note it in the commit.
- [ ] **Step 4:** Run. Expected: PASS. `dotnet build` shows no trim/AOT warnings for the project.
- [ ] **Step 5:** Commit `feat(ai): Ambinder.A2A.Extensions.AI with A2AChatMapper`.

### Task 15: `ScriptedChatClient` test double (test-only, §0.2)

**Files:** Create `tests/Ambinder.A2A.Extensions.AI.Tests/Fakes/ScriptedChatClient.cs` (skip if `Ambinder.A2A.Testing.AI` exists; use it instead).

- [ ] **Step 1:** Write a test: a scripted client returns a fixed response, a fixed stream of updates with explicit "flush points" (a callback the test controls), throws a scripted exception, and records the messages it received and the cancellation token.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement `IChatClient` with `GetResponseAsync` and `GetStreamingResponseAsync`. XML comment: "Test double; package 12 moves this into Ambinder.A2A.Testing.AI."
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `test(ai): scripted chat client test double`.

### Task 16: `AddChatAgent` and `ChatClientAgentHandler` (§8.2)

**Files:** Create `ChatAgentOptions.cs`, `ChatAgentReplyMode.cs`, `ChatClientAgentHandler.cs`, `ChatAgentServerBuilderExtensions.cs` (namespace `Microsoft.Extensions.DependencyInjection`), `StreamingArtifactPump.cs` (internal), `AgentExecutionScope.cs`. Test: `tests/Ambinder.A2A.Extensions.AI.Tests/ChatAgentTests.cs`.

- [ ] **Step 1: Write failing tests** (acceptance "AI tests"):
  - `AddChatAgent("assistant", card, o => o.Instructions = "…")` with `IChatClient` from DI: the chat client receives the system message first, then the mapped conversation.
  - Second turn (same `contextId`, new task) sees the first turn's answer (from the artifact, via `GetConversationAsync`).
  - Streaming: 2,000 one-word updates → at most 40 artifact events; flush at `FlushChars` (512) or `FlushInterval` (100 ms, fake clock) whichever first, and at the end; the final event has `lastChunk: true`; the task ends `COMPLETED`.
  - Non-streaming call → one artifact named `response` (`ArtifactName`).
  - Cancel mid-stream → task `CANCELED`; the chat client's token was cancelled.
  - Provider exception with message `provider secret` → task `FAILED` with exactly `The model call failed.`; with `ExposeErrors = true` the text includes the exception message; the error is logged.
  - `ErrorContent` in the response fails the task.
  - `Reply = Message` → one `GetResponseAsync` over the current message only (no conversation), a direct reply, no task stored.
  - Two chat agents in one app with different named options (`IOptionsMonitor<ChatAgentOptions>.Get(agentName)`).
  - `ChatClientKey` resolves a keyed `IChatClient`; the factory overload works.
  - No `IChatClient` registered → startup fails with exactly "Register an IChatClient (for example services.AddChatClient(...)) or set ChatAgentOptions.ChatClientKey."
  - `ReportToolCalls = true` → a `WORKING` status message `Calling <name>` per function call; `IncludeReasoning` maps reasoning.
  - The handler sets `AgentExecutionScope.Current` (parent context id, owner, a way to write task metadata) for the run and clears it after.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement per §8.2. `ChatClientAgentHandler` is scoped. The pump uses `TaskUpdater.OpenArtifact` and coalesces deltas.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ai): AddChatAgent serves an IChatClient as an A2A agent`.

### Task 17: Tool schema and result shaping (§8.3, pure parts)

**Files:** Create `ToolSchema.cs` (internal, constant JSON), `ToolJsonContext.cs` (internal source-generated context), `A2AToolOptions.cs`. Test: `tests/Ambinder.A2A.Extensions.AI.Tests/ToolShapeTests.cs`.

- [ ] **Step 1: Write failing tests.**
  - Name: card name sanitized to `[a-zA-Z0-9_-]{1,64}` (spaces → `_`, others removed, cut at 64, empty → `agent`).
  - Description: card description + skill names and descriptions, cut at 1,000 characters.
  - Schema: `message` (string, required), `taskId` (string, optional); the schema string is valid JSON and constant.
  - Result JSON: `{"status":"completed","text":"..."}`, `{"status":"input_required","taskId":"...","question":"..."}`, `{"status":"failed","error":"..."}`; non-text parts listed as `[name, mediaType, size]` or their URL; cut at `MaxResultChars` (default 20,000).
  - Derived `contextId` (§0.3 item 5): stable for the same parent and remote URL, different per remote URL and per parent; version nibble 8.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ai): tool schema, results and derived context ids`.

### Task 18: `A2AAgentTool` (§8.3, B9) — needs 10

**Files:** Create `A2AAgentTool.cs`, `IA2AToolProvider.cs`, `RemoteAgentToolProvider.cs` (internal); extend `ChatAgentOptions.RemoteAgents` handling. Test: `tests/Ambinder.A2A.Extensions.AI.Tests/AgentToolTests.cs` (hosted fake remote agent on `TestServer`).

- [ ] **Step 1: Write failing tests** (acceptance "Tool tests"):
  - Completed remote task → `{"status":"completed","text":…}`.
  - Remote `INPUT_REQUIRED` → `input_required` with the `taskId`; a second call with that `taskId` completes.
  - A `taskId` the tool did not create for this owner → `{"status":"failed","error":"unknown taskId"}` and **no** request reaches the fake agent (B9). A binding made for owner A is not usable when the ambient owner is B.
  - Bindings survive a new tool instance inside a later turn of the same parent task (stored in parent task metadata, §0.3 item 6).
  - Default identity: the call carries the client's own credentials and no `UserAssertion`; with `UseOnBehalfOf = true` the call carries `A2ARequestOptions.UserAssertion` from the caller (B9; OBO only on the receiving pod, B8).
  - Remote `FAILED`/`REJECTED` and A2A errors are returned as results; `A2AAuthException` and cancellation are thrown.
  - Timeout (`A2AToolOptions.Timeout`, fake clock) → a failed result *(plan-writer's call: the spec lists timeout among tool tests without saying throw or result; a result lets the model recover, matching "A2A failures … are returned as a result")*.
  - Cancelling the tool call's token sends `CancelTask` to the remote agent (best effort).
  - Inside an `AddChatAgent` run the remote `contextId` is the derived one, stable across turns; outside a run the tool keeps its own; `ContextIdProvider` overrides.
  - `ForwardArtifacts = true` re-emits remote artifacts on the parent task as `<agent>/<artifactId>`.
  - `o.RemoteAgents.Add("travel")` with `AddA2AClient("travel", …)` exposes the tool to the chat client; tools are resolved lazily once; `IA2AToolProvider.GetToolsAsync` returns them for custom pipelines.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement on 10's `SendAndWaitAsync` / `A2AWaitOptions` (no own wait loop).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ai): A2AAgentTool with id binding and app identity by default`.

### Task 19: Agent Framework bridge (§8.4, C12)

**Files:** Create `src/Ambinder.A2A.Extensions.AI.AgentFramework/` (csproj refs `Ambinder.A2A.Extensions.AI`, package `Microsoft.Agents.AI`), `AgentFrameworkServerBuilderExtensions.cs` (`AddAgentFrameworkAgent(name, card, Func<IServiceProvider, AIAgent>)`), `AgentFrameworkAgentHandler.cs`, `README.md`; `tests/Ambinder.A2A.Extensions.AI.AgentFramework.Tests/`. Modify `Directory.Packages.props`, `eng/package-dependencies.json`.

- [ ] **Step 0: Check the library.** Find the newest `Microsoft.Agents.AI` on nuget.org. Record its version and whether it is GA. Confirm the names `AIAgent`, its streaming run method and the thread type in that version (the spec says the builder verifies them). If not GA: mark the package as preview (pre-release version suffix in the csproj per 01's MinVer setup, "experimental" note in the README) — C12.
- [ ] **Step 1: Write failing tests** with a fake `AIAgent` (or one built over the scripted chat client): streaming run output becomes coalesced artifact chunks via the same pump as Task 16; each turn rebuilds the thread from `GetConversationAsync` (no state held between turns); cancel ends `CANCELED`; an exception gives `The model call failed.`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement, reusing `A2AChatMapper` and the streaming pump. Shipped assemblies may not use `InternalsVisibleTo` with each other (01 rule 6), so move `StreamingArtifactPump` to a linked shared source file under `src/Shared/AI/` (01 rule 10), compiled as internal into both packages.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ai): Agent Framework bridge`.

---

## Phase D — Samples, checks, docs

### Task 20: Rewrite the sample agents

**Files:** `samples/AgentServer/Agents/EchoAgent.cs`, `ReportAgent.cs`, `BookingAgent.cs`, `SecureAgent.cs`; their flow tests (`BookingFlowTests`, `ReportFlowTests`, `EchoFlowTests`, `SecureFlowTests`).

- [ ] **Step 1:** Run the flow tests to record the green baseline.
- [ ] **Step 2:** Rewrite with `TaskUpdater` (spec §10 table): `BookingAgent` uses `GetConversationAsync`/task metadata instead of regex over history; `ReportAgent` keeps `IChatClient` and streams through `ArtifactWriter`; `SecureAgent` uses `RequireAuthAsync` (with a grant where the flow needs one).
- [ ] **Step 3:** Run the flow tests. Expected: PASS with unchanged wire behavior (these agents are package 16's interop SUT agents).
- [ ] **Step 4:** Commit `refactor(samples): sample agents on TaskUpdater`.

### Task 21: Remaining API and docs-comment pass

**Files:** `IAgentHandler.cs`, `IAgentEventSink.cs`, `TaskUpdater.cs`, `AgentRequestContext.cs`.

- [ ] **Step 1:** Write the DI-scope, cancellation and error rules (spec §6) into the XML docs of `IAgentHandler` (spec §6 DI rule 6). Replace the old "With `returnImmediately`, … emit WORKING promptly" note with the grace rule.
- [ ] **Step 2:** If 02 is present, make sure every new public member is in `PublicAPI.Unshipped.txt` and the build is clean.
- [ ] **Step 3:** Commit `docs(server): authoring contract in XML docs`.

### Task 22: Line-count and compile checks for the target programs (§9, principle 3)

**Files:** Create `samples/Authoring/Echo/Program.cs`, `samples/Authoring/TaskAgent/ReportAgent.cs` (+ a minimal `Program.cs`), `samples/Authoring/ChatAgent/Program.cs`; test `tests/Ambinder.A2A.Integration.Tests/Samples/TargetProgramTests.cs`.

- [ ] **Step 1: Write failing tests:** the three projects build (they are in the solution); counting non-blank, non-comment lines: echo `Program.cs` ≤ 8, the task agent class ≤ 20, chat `Program.cs` ≤ 10; each sample runs on `TestServer` and answers (echo text; task completes with an artifact after an input-required turn; chat agent with the scripted client).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Write the samples from spec §9 (adjust names only where 02 changed them). The docs pages (Task 25) include these files through 13's snippet mechanism if it exists, so the docs code is the compiled code.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `test(samples): target programs compile and stay short`.

### Task 23: Dependency-graph and AOT checks (principle 5, Goal 14)

**Files:** extend 01's allow-list test or add `tests/Ambinder.A2A.Integration.Tests/Repo/AuthoringDependencyTests.cs`; CI step for the AOT analyzers.

- [ ] **Step 1:** Add tests that fail when `Ambinder.A2A.Server` references any `Microsoft.Extensions.AI*`, when `Ambinder.A2A.Extensions.AI` references `Microsoft.Extensions.AI` (full) or any LLM provider package, and when `Ambinder.A2A.Server` gains an Agent Framework reference.
- [ ] **Step 2:** Prove each fails with a temporary bad reference, then remove it.
- [ ] **Step 3:** `dotnet build src/Ambinder.A2A.Extensions.AI -c Release -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true` has zero warnings.
- [ ] **Step 4:** Commit `ci: dependency and AOT checks for authoring and AI packages`.

### Task 24: Benchmarks (principle 4) — needs 14

**Files:** `benchmarks/Ambinder.A2A.Benchmarks/Authoring/TaskUpdaterBenchmarks.cs`, `StreamedArtifactBenchmarks.cs`; baseline per 14.

- [ ] **Step 1:** Add: (a) 1,000 events through `TaskUpdater` vs the raw sink — target overhead under 5%; (b) a streamed answer of 10 KB vs 100 KB through `ArtifactWriter` — target time ratio under 15 (linear, not quadratic).
- [ ] **Step 2:** Run `dotnet run -c Release --project benchmarks/Ambinder.A2A.Benchmarks -- --filter "*TaskUpdater*" "*StreamedArtifact*"`. Record results.
- [ ] **Step 3:** A missed target is reported in the PR body; do not loosen it.
- [ ] **Step 4:** Commit `perf: authoring benchmarks`.

### Task 25: Docs (package 13 sections owned by 11)

**Files:** in 13's tree, with 13's front matter (`packages`, `since`, `owner: 11`):

| Spec section name | File |
|---|---|
| "Write your first agent" | `docs/agents/write-an-agent.md` |
| "Multi-turn conversations" | `docs/agents/task-lifecycle.md` (multi-turn rules 1–8, direct reply vs task) |
| "Cancellation and errors" | `docs/agents/task-lifecycle.md` (own section) and `docs/agents/input-and-auth-required.md` (`RequireInputAsync`, `RequireAuthAsync` with a grant) |
| "DI scope of an agent" | `docs/agents/write-an-agent.md` (own section) |
| "Writing an extension" | `docs/agents/extensions.md` |
| "LLM agents with IChatClient" | `docs/agents/llm-agents.md` (+ mapping table §8.1) |
| "Calling an agent from an LLM" | `docs/agents/call-other-agents.md` (identity B9, id binding, "the result text is untrusted data") |
| streaming and `ArtifactWriter` | `docs/agents/streaming-and-artifacts.md` |

- [ ] **Step 1:** Write the pages from the spec only. Code samples come from the Task 22 projects.
- [ ] **Step 2:** If 13's `DocsLint` or the repo `DocsTests` exist, run them. Expected: PASS.
- [ ] **Step 3:** Commit `docs(agents): authoring, extensions and AI guides`.

### Task 26: TCK, interop and final verification

- [ ] **Step 1:** `dotnet build Ambinder.A2A.slnx -c Release` and `dotnet test Ambinder.A2A.slnx`. Expected: green.
- [ ] **Step 2:** Run 03's TCK job and 16's interop smoke if present (acceptance: "TCK (03) and interop (16) runs still pass after the TaskManager changes"). Otherwise say "not available" in the PR body.
- [ ] **Step 3:** Walk the spec's acceptance list and tick each item in the PR body with the test that proves it, or "open: <reason>".
- [ ] **Step 4:** Open the PR. Body: Task 0 gate table, acceptance checklist, new public API, the `Microsoft.Agents.AI` version and GA status (C12), follow-ups.

---

## Acceptance criteria → tasks

| Spec acceptance item | Task |
|---|---|
| `AgentRequestContext` members and unit tests | 1 |
| `GetConversationAsync` tests | 9 |
| Recovered/forwarded run's `Caller` is the reduced caller (B8) | 10 (forwarded: package 06, M2 follow-up) |
| `TaskUpdater` tests incl. `RequireAuthAsync` grant and `ArtifactWriter` | 3 |
| Sink tests | 2 |
| Error tests | 5 |
| Cancellation tests | 6 |
| `returnImmediately` tests | 7 |
| DI tests | 8 |
| Extension tests | 11, 12, 13 |
| AI tests with `ScriptedChatClient` | 14, 15, 16 |
| Tool tests | 17, 18 |
| Target-program compile tests and line counts | 22 |
| Dependency-graph test; no trim/AOT warnings | 23 |
| TCK and interop still pass | 26 |
| Docs sections | 25 |
| Benchmarks meet the numbers | 24 (and the 2,000-token test in 16) |

---

## Execution notes

- **Method:** execute this plan with **superpowers:subagent-driven-development**: one fresh implementer subagent per task, then that skill's spec-compliance review and code-quality review before the next task starts.
- **Model:** every subagent (implementers and both reviewers) runs on model **`sonnet`**. Pass `model: "sonnet"` on every subagent dispatch. This follows the owner's rule: planning on Opus, execution on Sonnet. If a task is too large on `sonnet`, split it; do not switch models.
- **Order:** numbered order. Gates from §0.2: stop before Task 1 without 07; before Task 4 without 09's `AgentOptions`; before Task 9 without 05's query; before Task 11 without 09's negotiation middleware or 03's contributor; before Task 18 without 10's wait helper. Report what landed and what waits.
- **Each implementer's brief:** its task text; §0 and Global Constraints; Review Focus; the Task 0 gate table; the spec path. Tell it the spec is binding, to use the spec's exact names and strings, to add no public API beyond the spec, and to stop and report when another package's landed code differs from what this plan assumes.
- **Each reviewer's brief:** the task text, the spec sections the task cites, and the diff. Reviewers check against the spec, not only this plan, and reject tests written after the code.
- **Push after every task**, so a reclaimed container loses nothing.
- **This plan file** is changed during execution only to tick checkboxes.
