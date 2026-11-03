# Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One trace per client flow, from AgentClient through the server, the agent, the LLM and push, to WebhookReceiver. Traces, metrics and logs are viewed locally in the Aspire Dashboard, started by one command on Windows without Docker.

**Architecture:**
- The libraries (`src/`) emit spans and metrics through `System.Diagnostics` only:
  - Server operation spans come from a decorator around `IA2ARequestHandler`.
  - The agent-run span, task-state events and the completed-task counter live in `TaskManager` and `TaskWriter`.
  - Push spans carry a trace context captured when the item is queued.
  - Client spans come from a decorator that `A2AClientFactory` returns.
- The sample hosts turn on the OpenTelemetry SDK and OTLP exporter **only** when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- A separate `samples/AppHost` (Aspire) launches everything with the dashboard.

**Tech Stack:**
- .NET 10 and ASP.NET Core
- `System.Diagnostics` `ActivitySource` and `Meter`
- OpenTelemetry .NET 1.20.1 (Extensions.Hosting, Exporter.OpenTelemetryProtocol, Instrumentation.AspNetCore, Instrumentation.Http)
- Microsoft.Extensions.AI 10.11.0
- Aspire.AppHost.Sdk and Aspire.Hosting.AppHost 13.6.4
- xUnit v3

**Spec:** `docs/superpowers/specs/2026-11-03-observability-design.md`

## Global Constraints

- **Packages:**
  - Stable NuGet versions only, pinned in `Directory.Packages.props`. Do **not** use `OpenTelemetry.Instrumentation.GrpcNetClient` (no stable release).
  - `src/` projects must not reference any OpenTelemetry or Aspire package.
- **Build rules:** `TreatWarningsAsErrors`, nullable on, central package management. Match the surrounding code style and comment density.
- **Off means off:** with `OTEL_EXPORTER_OTLP_ENDPOINT` unset or empty, the sample hosts register no OpenTelemetry services and wrap nothing. Wire traffic, tests, `run-all`, docker-compose and CI behave exactly as today.
- **Never recorded** in any span name, attribute, event or metric tag:
  - tokens, API keys, client secrets, webhook credentials, push tokens, push signatures, `Authorization` values
  - URL query strings
  - message, part or artifact content, LLM prompts and answers
- **Ports stay as they are:** MockEntra 5100, AgentServer 5200 with gRPC on 5201–5204, WebhookReceiver 5300.
- **Names are fixed by the spec:**
  - Sources `A2A.Server` and `A2A.Client`, meter `A2A.Server`.
  - Span names `a2a.server <Method>`, `a2a.client <Method>`, `a2a.agent.run` and `a2a.push.deliver`.
  - Event `a2a.task.state`.
  - Metrics `a2a.server.tasks.completed`, `a2a.server.streams.active` and `a2a.server.push.deliveries`.
- **Work on a branch:** `feat/observability`, created from `master` before Task 1. Commit the spec and this plan as its first commit.
- **Docs:** plain, short English sentences (the repo style).

## Review Focus

1. **A developer machine with `OTEL_EXPORTER_OTLP_ENDPOINT` already set in its environment.** The integration tests must still run with telemetry off. `A2ATestHost` forces the key to empty (Task 5).
2. **A push webhook URL with secrets in its query string** (`?sig=...`). The push span records the host only (Task 3, no-secrets test in Task 7).
3. **A streaming call the caller cancels mid-stream.** `a2a.server.streams.active` must return to 0, and the span must not be marked as an error (Task 1).
4. **A task continued after `input-required`, then completed.** It is counted once in `a2a.server.tasks.completed`, and both state events appear (Task 2).
5. **The client Docker image (`runtime:10.0`, no ASP.NET).** AgentClient must not pull in `Microsoft.AspNetCore.App` (Task 6 keeps its telemetry out of `SampleTelemetry`).

---

### Task 1: Telemetry names and server operation spans

**Files:**
- Create: `src/A2A.Protocol/A2ATelemetryNames.cs`
- Create: `src/A2A.Server/Telemetry/ServerInstruments.cs`
- Create: `src/A2A.Server/Telemetry/TracingRequestHandler.cs`
- Modify: `src/A2A.Server/A2ACallContext.cs`, to add the `Transport` property
- Modify: `src/A2A.Server/Transports/TransportRequest.cs:47`, adding a `transport` parameter to `CreateCallContext`, plus its callers in the JSON-RPC, REST and gRPC transports
- Modify: `src/A2A.Server/Hosting/A2AServiceCollectionExtensions.cs:53`
- Create: `tests/Shared/ActivityCollector.cs` and `tests/Shared/MetricCollector.cs`, linked into both test projects with `<Compile Include="../Shared/*.cs" />`
- Create: `tests/A2A.Server.Tests/Telemetry/TelemetryCollection.cs`, which defines `[CollectionDefinition("Telemetry", DisableParallelization = true)]`. Every test that uses `MetricCollector` goes in this collection, because metric tags carry no trace ID.
- Test: `tests/A2A.Server.Tests/Telemetry/OperationSpanTests.cs`, `tests/A2A.Integration.Tests/Telemetry/TransportSpanTests.cs`

**Interfaces:**
- **Produces `A2A.Protocol.A2ATelemetryNames`** (public static class of `const string`):
  - Sources and meter: `ServerSource = "A2A.Server"`, `ServerMeter = "A2A.Server"`, `ClientSource = "A2A.Client"`.
  - Attributes: `Method = "a2a.method"`, `Agent = "a2a.agent"`, `Transport = "a2a.transport"`, `TaskId = "a2a.task.id"`, `ContextId = "a2a.context.id"`, `Tenant = "a2a.tenant"`, `TaskState = "a2a.task.state"`, `ErrorType = "error.type"`, `PushAttempt = "a2a.push.attempt"`, `Outcome = "outcome"`, `ServerAddress = "server.address"`.
  - Transport values: `TransportJsonRpc = "jsonrpc"`, `TransportRest = "rest"`, `TransportGrpc = "grpc"`.
  - Metric names: `TasksCompleted = "a2a.server.tasks.completed"`, `StreamsActive = "a2a.server.streams.active"`, `PushDeliveries = "a2a.server.push.deliveries"`.
  - Event name: `TaskStateEvent = "a2a.task.state"`.
  - Helper: `public static string StateValue(TaskState state)` returns `submitted`, `working`, `completed`, `failed`, `canceled`, `input-required`, `rejected`, `auth-required` or `unspecified`.
- **Produces `internal static class ServerInstruments`** with `ActivitySource Source`, `Meter Meter`, `Counter<long> TasksCompleted`, `UpDownCounter<long> StreamsActive` and `Counter<long> PushDeliveries`.
- **Produces `A2ACallContext.Transport`:** `public string? Transport { get; init; }`, a non-positional init property. Existing callers are unchanged.
- **Produces `public sealed class TracingRequestHandler(IA2ARequestHandler inner) : IA2ARequestHandler`.**
- **Produces the test helpers.** `ActivityCollector(params string[] sources) : IDisposable` exposes:
  - `Activity Root`, a test root activity with a fresh trace ID
  - `IReadOnlyList<Activity> Stopped`
  - `Activity Single(string name)`
  - `Task<Activity> WaitForAsync(string name, TimeSpan timeout)`

  Its sampler returns `AllDataAndRecorded` only when `options.Parent.TraceId == Root.TraceId`, and `None` otherwise. That way, parallel tests never see each other's activities, and no other test gets extra activities or `traceparent` headers. `MetricCollector(string meter) : IDisposable` exposes `IReadOnlyList<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> Measurements`.

- [ ] **Step 1: Write the failing tests** in `OperationSpanTests`. Wrap `new TestHost().Manager` in `TracingRequestHandler` and start each call under `collector.Root`:
  - `SendMessage_creates_operation_span_with_a2a_attributes`
    - `Single("a2a.server SendMessage")` has `a2a.method = "SendMessage"` and `a2a.agent = "test"`.
    - `a2a.task.id` equals the returned task's ID, and `a2a.context.id` equals its context ID.
    - `Kind == ActivityKind.Internal`, and its parent is `Root`.
  - `Unknown_task_sets_error_status_and_error_type`
    - `GetTask` for a missing ID throws `A2AException`.
    - The span has `Status == ActivityStatusCode.Error` and `error.type == nameof(A2AErrorCode.TaskNotFound)`.
  - `Every_handler_method_gets_a_span`: one call per `IA2ARequestHandler` method gives one span each, named `a2a.server <Method>`. The method names are SendMessage, SendStreamingMessage, GetTask, ListTasks, CancelTask, SubscribeToTask, CreateTaskPushNotificationConfig, GetTaskPushNotificationConfig, ListTaskPushNotificationConfigs, DeleteTaskPushNotificationConfig and GetExtendedAgentCard. Errors are allowed.
  - `Stream_cancelled_mid_way_returns_active_streams_to_zero_without_error`
    - Put this in the `[Collection("Telemetry")]` collection, defined with `DisableParallelization = true`.
    - Use a `MetricCollector`. The sum of `a2a.server.streams.active` measurements must be 0 after the caller cancels the stream.
    - The stream span's status is not Error.
  - `Tenant_attribute_only_when_set`: `a2a.tenant` is absent when `Call().Tenant` is null.
- [ ] **Step 2: Write the failing test** `TransportSpanTests.Server_span_records_transport_for_each_binding` (in `A2ATestHost`'s collection). For JSON-RPC, REST and gRPC clients (echo agent, `PreferredBinding`), send one message under `collector.Root` (sources `A2A.Server`). Each `a2a.server SendMessage` span has `a2a.transport` set to `jsonrpc`, `rest` and `grpc` respectively.
- [ ] **Step 3: Run the tests and confirm they fail to compile.**
  Run: `dotnet test tests/A2A.Server.Tests` and `dotnet test tests/A2A.Integration.Tests --filter-class "*TransportSpanTests"`
  Expected: build errors: `TracingRequestHandler` and `ActivityCollector` not found.
- [ ] **Step 4: Implement the code.**
  - `A2ATelemetryNames` and `ServerInstruments` (static fields, created once).
  - `CreateCallContext(HttpContext http, AgentRegistration agent, string owner, string? version, IReadOnlyList<string> extensions, object request, string transport)` sets `Transport`. The JSON-RPC endpoint passes `TransportJsonRpc`, REST passes `TransportRest`, and `A2AGrpcService.PrepareAsync` passes `TransportGrpc`.
  - `TracingRequestHandler`: each method starts `ServerInstruments.Source.StartActivity("a2a.server " + method, ActivityKind.Internal)`. It sets method, agent and transport, plus tenant, task ID and context ID when non-empty, taking them from the request and then from the response.
    - On `A2AException`: status Error, `error.type = ex.Code.ToString()`, then rethrow.
    - On other exceptions except `OperationCanceledException`: `error.type = ex.GetType().FullName`.
    - Streaming methods are async iterators that keep the span open for the whole enumeration. They add 1 to `StreamsActive` (tag `a2a.agent`) when enumeration starts and subtract 1 in `finally`.
  - DI: `services.TryAddSingleton<IA2ARequestHandler>(sp => new TracingRequestHandler(sp.GetRequiredService<TaskManager>()));`
- [ ] **Step 5: Run the tests and confirm they pass.**
  Run: `dotnet test A2A.Reference.slnx`
  Expected: all tests pass, 763 plus the new ones.
- [ ] **Step 6: Commit** with `feat(server): A2A operation spans and active-stream metric`.

### Task 2: Agent-run span, task-state events, completed-task counter

**Files:**
- Modify: `src/A2A.Server/Tasks/TaskManager.cs`, in `RunAsync` (around lines 300–336)
- Modify: `src/A2A.Server/Tasks/TaskWriter.cs`, in `CommitAsync`
- Test: `tests/A2A.Server.Tests/Telemetry/TaskTelemetryTests.cs`, in the `Telemetry` collection

**Interfaces:**
- Consumes: `ServerInstruments`, `A2ATelemetryNames`, `TracingRequestHandler`, `ActivityCollector` and `MetricCollector` (Task 1).

- [ ] **Step 1: Write the failing tests.**
  - `Agent_run_span_is_child_of_send_span_and_outlives_it`
    - Non-blocking send (`ReturnImmediately = true`) with a script that waits on a gate.
    - `a2a.agent.run` has parent `a2a.server SendMessage`, the same trace ID, and `a2a.task.id` set.
    - It stops after the send span stops.
  - `State_changes_are_events_on_agent_run_span`: a script that goes working → completed. The run span's events named `a2a.task.state` carry the values `working` and `completed`, in order.
  - `Cancel_state_event_lands_on_cancel_span`: `CancelTask` on a working task. The `a2a.server CancelTask` span has an `a2a.task.state` event with value `canceled`.
  - `Completed_counter_counts_terminal_states_once`
    - Four tasks end as completed, failed, canceled and rejected.
    - The `a2a.server.tasks.completed` measurements are exactly 4, each value 1, with `a2a.task.state` tags `completed`, `failed`, `canceled` and `rejected`, and `a2a.agent = "test"`.
  - `Input_required_then_completed_counts_once`
    - A task goes to `input-required`, then is continued and completed.
    - One `tasks.completed` measurement with state `completed`.
    - The span events include `input-required`.
  - `Agent_exception_marks_run_span_error`: a script that throws. The run span status is Error and `error.type` is the exception type's full name.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/A2A.Server.Tests --filter-class "*TaskTelemetryTests"`. Expected: FAIL (no `a2a.agent.run` span).
- [ ] **Step 3: Implement the code.**
  - In `RunAsync`, wrap the body in `using var span = ServerInstruments.Source.StartActivity("a2a.agent.run")` with agent, task ID and context ID tags. Its parent comes from `Activity.Current`, which flows through `Task.Run`. Set Error status and `error.type` in the existing exception branch (not the cancel branch).
  - In `TaskWriter.CommitAsync`, after `SaveAsync`, read the state from `evt.StatusUpdate?.Status.State ?? evt.Task?.Status.State`. If it is not null:
    - Add an `ActivityEvent(TaskStateEvent)` with tag `a2a.task.state = StateValue(state)` to `Activity.Current`.
    - If `TaskStateRules.IsTerminal(state)`, add 1 to `TasksCompleted` with tags agent and state.
- [ ] **Step 4: Run all tests and confirm they pass.** Run `dotnet test A2A.Reference.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(server): agent-run span, task-state events, completed-task counter`.

### Task 3: Push delivery span and metric

**Files:**
- Modify: `src/A2A.Server/Push/PushWorkItem.cs`, to add `ActivityContext Parent`
- Modify: `src/A2A.Server/Push/PushNotificationSender.cs`, in `OnTaskEventAsync`, `DeliverToAsync` and the queue-full branch
- Test: `tests/A2A.Server.Tests/Push/PushTelemetryTests.cs`, in the `Telemetry` collection. Reuse the stub-handler harness from `PushTests.cs`.

**Interfaces:**
- Consumes: Task 1 names and helpers.
- Produces: `internal sealed record PushWorkItem(string AgentName, string TaskId, StreamResponse Event, bool IsFinal, ActivityContext Parent)`.

- [ ] **Step 1: Write the failing tests.**
  - `Push_span_joins_task_trace_and_sends_traceparent`
    - The event is committed under `Root`.
    - `a2a.push.deliver` has `Root`'s trace ID, `a2a.task.id`, `server.address` set to the webhook host, `a2a.push.attempt = 1` and `outcome = "delivered"`.
    - The stub handler saw a `traceparent` header containing that trace ID.
  - `Push_span_never_records_query_string_or_credentials`
    - URL `https://hook.example/cb?sig=SECRET1`, credentials `SECRET2`, token `SECRET3`.
    - No tag value on any collected activity contains `SECRET1`, `SECRET2` or `SECRET3`.
  - `Push_metric_counts_retries_and_failure`: the webhook returns 500 for every attempt, with `MaxRetries = 2`. `a2a.server.push.deliveries` has `outcome=retried` twice and `outcome=failed` once. The span's outcome is `failed` and its attempt is 3.
  - `Rejected_4xx_counts_as_failed_without_retry`: on 400, one `failed` and no `retried`.
- [ ] **Step 2: Run them and confirm they fail.** Expected: FAIL (no `a2a.push.deliver`).
- [ ] **Step 3: Implement the code.**
  - `OnTaskEventAsync` captures `Activity.Current?.Context ?? default`. When the queue is full, it adds 1 to `PushDeliveries{outcome=dropped}`.
  - `DeliverToAsync` starts `Source.StartActivity("a2a.push.deliver", ActivityKind.Internal, item.Parent)` around the attempt loop, so the `HttpClient` call runs inside it and gets `traceparent`.
    - It sets `server.address = target.Host`, and nothing else from the URL.
    - It counts `retried` once per retry, `delivered` on success, and `failed` on give-up or 4xx rejection.
    - It sets the final `a2a.push.attempt` and `outcome` on the span, with status Error on `failed`.
- [ ] **Step 4: Run all tests and confirm they pass.** Run `dotnet test A2A.Reference.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(server): push delivery span, trace propagation and metric`.

### Task 4: Client spans

**Files:**
- Create: `src/A2A.Client/TracingA2AClient.cs`
- Modify: `src/A2A.Client/A2AClientFactory.cs:40-42`
- Modify: `tests/A2A.Integration.Tests/Client/TransportParityTests.cs:266-274`, so the asserts unwrap `.Inner`
- Test: `tests/A2A.Integration.Tests/Telemetry/ClientSpanTests.cs`

**Interfaces:**
- Consumes: `A2ATelemetryNames` (Task 1).
- Produces:
  - `public sealed class TracingA2AClient : IA2AClient` with constructor `TracingA2AClient(IA2AClient inner, string transport, string? tenant)` and property `public IA2AClient Inner { get; }`.
  - `A2AClientFactory.Create` returns `TracingA2AClient` wrapping the chosen client. The transport value is `TransportRest`, `TransportGrpc` or `TransportJsonRpc`.

- [ ] **Step 1: Write the failing tests.**
  - `Client_span_is_parent_of_server_span_on_every_transport`
    - For each binding, a send under `Root`, with sources `A2A.Client`, `A2A.Server` and `Microsoft.AspNetCore`.
    - `a2a.client SendMessage` has `a2a.transport` equal to the binding.
    - The `a2a.server SendMessage` span has the same trace ID. The client span is an ancestor of it, through the HTTP span.
  - `Client_error_sets_status_and_error_type`: `GetTask` on an unknown ID gives status Error and `error.type = "TaskNotFound"`.
  - Update `TransportParityTests` to assert `IsType<RestA2AClient>(((TracingA2AClient)client).Inner)`, and the same for the others.
- [ ] **Step 2: Run them and confirm they fail.** Expected: FAIL (no `TracingA2AClient`).
- [ ] **Step 3: Implement the code.** Same pattern as `TracingRequestHandler`: span `a2a.client <Method>`, `ActivityKind.Internal`, the same tags and error rules. Streams keep the span open for the whole enumeration. `DisposeAsync` forwards to `Inner`.
- [ ] **Step 4: Run all tests and confirm they pass.** Run `dotnet test A2A.Reference.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(client): A2A client spans`.

### Task 5: Sample host telemetry (off unless the endpoint is set)

**Files:**
- Create: `samples/SampleTelemetry/SampleTelemetry.csproj` (class library, `FrameworkReference Microsoft.AspNetCore.App`) and `samples/SampleTelemetry/SampleTelemetryExtensions.cs`
- Modify: `Directory.Packages.props`, adding `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore` and `OpenTelemetry.Instrumentation.Http` at 1.20.1, and `Microsoft.Extensions.AI` at 10.11.0
- Modify: `A2A.Reference.slnx`, to add SampleTelemetry
- Modify: `samples/AgentServer/AgentServerApp.cs`, `samples/MockEntra/MockEntraApp.cs`, `samples/WebhookReceiver/WebhookReceiverApp.cs` and their `.csproj` files (add the project reference)
- Modify: `samples/AgentServer/Llm/ChatClientRegistration.cs`, plus the `Microsoft.Extensions.AI` reference in `AgentServer.csproj`
- Modify: `docker/Dockerfile.agentserver`, `docker/Dockerfile.mockentra` and `docker/Dockerfile.webhook`. Copy `samples/SampleTelemetry/SampleTelemetry.csproj` before `dotnet restore`.
- Modify: `tests/A2A.Integration.Tests/Fixtures/A2ATestHost.cs`, to force the endpoint key to `""` for every host it builds
- Test: `tests/A2A.Integration.Tests/Samples/SampleTelemetryTests.cs`

**Interfaces:**
- Consumes: `A2ATelemetryNames` (Task 1).
- Produces:
  - `public static class SampleTelemetryExtensions`, with constants `EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT"` and `LlmSource = "AgentServer.Llm"`.
  - `public static bool IsEnabled(IConfiguration configuration)`: true when the endpoint key is non-empty.
  - `public static WebApplicationBuilder AddSampleTelemetry(this WebApplicationBuilder builder, string serviceName, params string[] extraSources)`. It does nothing when not enabled. When enabled, it adds:
    - service name from `OTEL_SERVICE_NAME`, with `serviceName` as the default
    - tracing: ASP.NET Core instrumentation (filter out paths starting with `/health`), HTTP client instrumentation, `A2A.Server`, `A2A.Client` and `extraSources`
    - metrics: ASP.NET Core, HttpClient, `System.Runtime` and `A2A.Server`
    - logging through OpenTelemetry, with `IncludeFormattedMessage` and `IncludeScopes`
    - `UseOtlpExporter()`

- [ ] **Step 1: Write the failing tests.**
  - `Telemetry_is_off_when_endpoint_unset`: `AgentServerApp.Build` with the endpoint key `""`. `app.Services.GetService<TracerProvider>()` and `GetService<MeterProvider>()` are null, and `IChatClient` is `FakeChatClient`.
  - `Telemetry_is_on_when_endpoint_set`: the endpoint key is `"http://127.0.0.1:4317"`. Both providers are non-null, and `IChatClient` is not `FakeChatClient` (it is wrapped).
  - The existing `FakeChatClientTests` assertion `IsType<FakeChatClient>` must still pass unchanged.
- [ ] **Step 2: Run them and confirm they fail.** Expected: FAIL (the providers are null when on).
- [ ] **Step 3: Implement the code.**
  - `AddSampleTelemetry` as specified above.
  - Each sample's `Build` calls it right after `configure?.Invoke(builder)`, with service names `agentserver`, `mockentra` and `webhookreceiver`. AgentServer passes `LlmSource`.
  - `AddDemoChatClient`: when `SampleTelemetryExtensions.IsEnabled(configuration)`, register `FakeChatClient` through `services.AddChatClient(sp => ActivatorUtilities.CreateInstance<FakeChatClient>(sp)).UseOpenTelemetry(sourceName: LlmSource)`. Leave `EnableSensitiveData` at its default of false. Otherwise keep today's registration.
  - `A2ATestHost` sets `builder.Configuration[SampleTelemetryExtensions.EndpointKey] = ""` in each host's `configure`.
- [ ] **Step 4: Run all tests and confirm they pass.** Run `dotnet test A2A.Reference.slnx`. Expected: PASS. Also run `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:1 dotnet test tests/A2A.Integration.Tests`. Expected: PASS (the forced-off fixture).
- [ ] **Step 5: Audit existing log calls (spec §5).** These now get exported, so check every `Log*` call in `src/` and `samples/` (`grep -rn "Log\(Trace\|Debug\|Information\|Warning\|Error\|Critical\)"`).
  - No placeholder may receive a secret, a full URL with a query string, or message content.
  - Pay particular attention to `samples/AgentServer/Auth/AuthSetup.cs:78`, which logs a JwtBearer exception. Make sure the token isn't in the message; `IdentityModelEventSource.ShowPII` must stay false.
  - Fix any line that leaks. Note in the commit message which lines you checked.
- [ ] **Step 6: Check that the Docker builds still restore.** Run `docker compose build` if Docker is available. Otherwise check by reading that each Dockerfile copies `SampleTelemetry.csproj` before restore.
- [ ] **Step 7: Commit** with `feat(samples): opt-in OpenTelemetry for sample hosts`.

### Task 6: AgentClient telemetry and per-flow root span

**Files:**
- Create: `samples/AgentClient/ClientTelemetry.cs`
- Modify: `samples/AgentClient/Program.cs`, `samples/AgentClient/FlowRunner.cs` (`RunFlowAsync`, around line 102), and `AgentClient.csproj`. Add `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` and `OpenTelemetry.Instrumentation.Http` (add `OpenTelemetry` 1.20.1 to the props), but **not** SampleTelemetry or any AspNetCore package.
- Test: `tests/A2A.Integration.Tests/Flows/FlowTelemetryTests.cs`

**Interfaces:**
- Produces:
  - `public static class ClientTelemetry`, with `public const string SourceName = "AgentClient"`.
  - `public static IDisposable? Start(IConfiguration configuration)`: null when the endpoint is unset. Otherwise it returns a tracer and meter provider holder. Disposing it flushes.
  - `RunFlowAsync` starts `ActivitySource(SourceName).StartActivity("flow " + name)`. On failure, the status is Error with the flow's failure description.

- [ ] **Step 1: Write the failing test** `Each_flow_is_one_trace`. Run the `send` flow through `FlowRunner` against `A2ATestHost`, with the collector root as parent and sources `AgentClient`, `A2A.Client` and `A2A.Server`.
  - `flow send` exists.
  - The `a2a.client SendMessage` and `a2a.server SendMessage` spans share its trace ID.
- [ ] **Step 2: Run it and confirm it fails.** Expected: FAIL (no `flow send` span).
- [ ] **Step 3: Implement the code.** `Program.cs` builds configuration from environment variables, then `using var telemetry = ClientTelemetry.Start(config);` wraps the command invocation, so the data is flushed before exit.
- [ ] **Step 4: Run all tests and confirm they pass.** Run `dotnet test A2A.Reference.slnx`. Expected: PASS. Then run `dotnet publish samples/AgentClient -c Release -o /tmp/ac` and check that `/tmp/ac/AgentClient.runtimeconfig.json` lists only `Microsoft.NETCore.App`.
- [ ] **Step 5: Commit** with `feat(agentclient): per-flow traces and opt-in OTLP export`.

### Task 7: End-to-end trace and no-secrets tests

**Files:**
- Test: `tests/A2A.Integration.Tests/Telemetry/EndToEndTraceTests.cs` (`A2ATestHost` collection)

**Interfaces:**
- Consumes: everything above. These are tests only; no product code unless a test finds a bug.

- [ ] **Step 1: Write the tests.**
  - `Push_reaches_webhook_receiver_in_same_trace`
    - Collector sources: `A2A.Client`, `A2A.Server` and `Microsoft.AspNetCore`.
    - Under `Root`, subscribe and send to the report agent with a push config to `ReceiverBaseUrl + "/webhook"`. Reuse the `PushFlowTests` helper pattern, then wait until completed.
    - There is an `a2a.push.deliver` span with `Root`'s trace ID.
    - There is a `Microsoft.AspNetCore.Hosting.HttpRequestIn` activity with `Root`'s trace ID whose `url.path` (or `http.target`) tag is `/webhook`, and whose parent chain reaches `a2a.push.deliver`.
  - `No_secret_or_content_in_any_span`. Run these under `Root`, collecting every source:
    - a send with the API key `A2ATestHost.ApiKey`
    - a send with a bearer token from MockEntra
    - a push send with credentials `"cred-" + Guid` and token `"tok-" + Guid`
    - message text `"content-" + Guid`

    No activity's display name, tag value or event tag value contains any of these:
    - the API key
    - the bearer token
    - the credentials or push token
    - the message text
    - the `X-A2A-Push-Signature` value the receiver logged, if available
    - the substring `?`, in any `url.full` value
- [ ] **Step 2: Run them.** Run `dotnet test tests/A2A.Integration.Tests --filter-class "*EndToEndTraceTests"`. Expected: PASS. If a test fails, fix the product code in the task that owns it, not the test.
- [ ] **Step 3: Commit** with `test: end-to-end trace and no-secrets checks`.

### Task 8: Aspire AppHost

**Files:**
- Create: `samples/AppHost/AppHost.csproj` (`Sdk="Aspire.AppHost.Sdk/13.6.4"`), `samples/AppHost/AppHost.cs` and `samples/AppHost/Properties/launchSettings.json`
- Modify: `Directory.Packages.props` (`Aspire.Hosting.AppHost` 13.6.4) and `A2A.Reference.slnx`

**Interfaces:**
- Consumes: the four sample projects (`Projects.MockEntra`, `Projects.WebhookReceiver`, `Projects.AgentServer` and `Projects.AgentClient`).
- Produces: nothing used by other code. No project references the AppHost.

- [ ] **Step 1: Write `AppHost.cs`.**
  - Resources `mockentra`, `webhookreceiver` and `agentserver`, each from its launch profile, with the `http` endpoint set to `IsProxied = false`, so the ports stay 5100, 5300 and 5200.
  - Each has `.WithHttpHealthCheck("/health/ready")`.
  - Start order: `webhookreceiver.WaitFor(mockentra)`, then `agentserver.WaitFor(webhookreceiver)`.
  - `agentclient` uses `.WithArgs("all").WithExplicitStart().WaitFor(agentserver)`.
- [ ] **Step 2: Write `launchSettings.json`.**
  - One `http` profile. Copy the shape of the `http` profile from the official 13.6.4 `aspire-apphost` template.
  - `applicationUrl` is `http://localhost:18888`. The OTLP endpoint and resource service endpoint are on `http://localhost:18889` and `http://localhost:18890`.
  - Environment: `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, `ASPIRE_DASHBOARD_TELEMETRY_OPTOUT=true` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`.
  - Every URL uses `localhost`. Leave the browser token auth at its default.
- [ ] **Step 3: Build.** Run `dotnet build A2A.Reference.slnx -c Release`. Expected: 0 warnings, build succeeded.
- [ ] **Step 4: Run it by hand on this machine.**
  - Run `dotnet run --project samples/AppHost`.
  - Expected: the console prints a `http://localhost:18888/login?t=...` link, and the three apps report healthy in the dashboard.
  - Start `agentclient`. Its traces appear, and the `push` flow trace includes `webhookreceiver`.
  - Stop with Ctrl+C. Expected: all app processes exit. Check with `lsof -i :5200`.
- [ ] **Step 5: Commit** with `feat(samples): Aspire AppHost for local observability`.

### Task 9: Docs

**Files:**
- Modify: `README.md`, adding a "See traces, metrics and logs" section
- Modify: `docs/production.md` §6
- Create: `samples/AppHost/README.md`
- Test: the existing `tests/A2A.Integration.Tests/Repo/DocsTests.cs` (relative links)

- [ ] **Step 1: Write the docs.**
  - **README:**
    - the command `dotnet run --project samples/AppHost`
    - open the printed link
    - click Start on `agentclient`
    - what you see: one trace per flow, metrics, and logs linked to traces
    - the only prerequisite is the .NET 10 SDK; no Docker
    - data stays local
    - an AgentClient run from a separate terminal does not send to the dashboard (unless Task 8 found a way without weakening auth)
  - **production.md §6:** what is emitted, the source and meter names, the never-recorded list, and how to point at a collector on AKS (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`). Off by default.
  - **AppHost README:** what it is for (local only), what it is not for (not in images, not for production), and the opt-out variables. Note that Microsoft's docs don't say whether the opt-out also stops dashboard crash reports.
- [ ] **Step 2: Run the docs tests and confirm they pass.** Run `dotnet test tests/A2A.Integration.Tests --filter-class "*DocsTests"`. Expected: PASS.
- [ ] **Step 3: Run the full CI equivalent.** Run `dotnet build A2A.Reference.slnx -c Release && dotnet test A2A.Reference.slnx --no-build -c Release`. Expected: 0 warnings, all tests pass.
- [ ] **Step 4: Commit** with `docs: observability guide and production notes`.
