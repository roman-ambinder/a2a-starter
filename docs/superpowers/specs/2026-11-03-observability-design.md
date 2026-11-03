# Observability (Phase 2, part 1): design

> Written before the rename to Ambinder.A2A.*

Date: 2026-11-03
Status: draft for approval. Items marked *(Design call.)* were decided by Claude after the builder review, not by the owner.

## Intent

Today you cannot see what happens inside a flow. When a send, a stream, a cancel or a push goes wrong, the only clues are console logs spread over three or four processes.

The goal: run everything on a Windows laptop **without Docker**, open one local web page, and see each flow as one timeline. That means traces, metrics and logs, from the client, through the server and the agent, to the push webhook.

This is a reference repo. The instrumentation should also show *how* to do observability well in an A2A server and client, and it must carry over unchanged to AKS later.

## Goals

1. **One trace per client flow, end to end.** AgentClient → transport (JSON-RPC, REST or gRPC) → server → `TaskManager` → agent handler → LLM call → push delivery → WebhookReceiver.
2. **A2A meaning on the spans.** Spans carry the method, agent, task, context and transport. Task state changes show on the timeline.
3. **Useful metrics.** Request duration per transport, tasks by final state, open streams, and push deliveries by outcome.
4. **Logs linked to traces.** Existing `ILogger` output goes to the same viewer, with trace and span IDs, so you can jump from a span to its logs.
5. **Local viewer with one command.** `dotnet run --project samples/AppHost` starts the apps and the Aspire Dashboard.
6. **Ready for AKS.** Apps send standard OTLP to whatever `OTEL_EXPORTER_OTLP_ENDPOINT` points at. No code change is needed to send to a collector in a cluster.

## Non-goals

- Signing key, rate limiting and test-wait fixes. These are the other Phase 2 items and come later, in separate work.
- An OpenTelemetry Collector, Azure Monitor or Grafana setup. Production.md only describes how to point at one.
- Kubernetes manifests, or Aspire deployment or publish features.
- Running the dashboard in docker-compose.
- Dashboards or alert rules.

## Anti-goals (failure even if it "works")

- **Secrets or content in telemetry.** No tokens, API keys, client secrets, webhook credentials, push signatures, `Authorization` values or URL query strings. No message, part or artifact content and no LLM prompts or answers.
- **On by default.** With no `OTEL_EXPORTER_OTLP_ENDPOINT`, nothing is exported, and tests, `run-all`, docker-compose and CI behave exactly as today.
- **Libraries tied to a vendor.** `src/` projects must not reference OpenTelemetry or Aspire packages.
- **AppHost leaking into images or production.** Dockerfiles and the sample apps must not depend on the AppHost.
- **Needs Docker, admin rights, a cloud account or a manual tool install.** Anything beyond the .NET 10 SDK is a failure.
- **Data leaving the machine.** The dashboard listens on `localhost` only. Microsoft usage telemetry is turned off.
- **Behavior changes.** No change to wire formats, status codes, errors or timing that tests can see.

## Constraints

- .NET 10, central package management (`Directory.Packages.props`), `TreatWarningsAsErrors`, nullable on.
- **Stable NuGet packages only** (an existing repo rule). Checked 2026-11-03:
  - `Aspire.AppHost.Sdk` / `Aspire.Hosting.AppHost` 13.6.4
  - `OpenTelemetry.Extensions.Hosting` 1.20.1
  - `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1
  - `OpenTelemetry.Instrumentation.AspNetCore` 1.20.1
  - `OpenTelemetry.Instrumentation.Http` 1.20.1
  - `Microsoft.Extensions.AI` 10.11.0, for `UseOpenTelemetry` on the chat client.
  - `OpenTelemetry.Instrumentation.GrpcNetClient` has **no stable release**, so do not use it. gRPC client calls already show up through the HTTP client instrumentation, and gRPC server calls through ASP.NET Core instrumentation.
- Must build and pass in the existing GitHub Actions CI on `ubuntu-latest`, with the AppHost included in the solution.
- Ports and URLs must stay as they are today: MockEntra 5100, AgentServer 5200 with gRPC on 5201–5204, WebhookReceiver 5300. Agent cards, config and docs depend on them.
- Docs follow the repo's plain-English style.

## Decided

### 1. Libraries emit; hosts export

- `A2A.Server` gets one `ActivitySource` and one `Meter`, both named `A2A.Server`. `A2A.Client` gets one `ActivitySource` named `A2A.Client`. Both use `System.Diagnostics` only, with no new package references.
- The names are public constants, so hosts can subscribe to them, for example `A2ATelemetry.SourceName`.
- With no listener attached, the cost is near zero, which is the normal .NET behavior.

### 2. Server spans

- **One span per A2A operation, the same for every transport.** Name: `a2a.server <Method>` (for example `a2a.server SendMessage`). It is a child of the ASP.NET Core request span.
- **Attributes:**
  - `a2a.method`
  - `a2a.agent`
  - `a2a.transport` (`jsonrpc`, `rest` or `grpc`)
  - `a2a.task.id` and `a2a.context.id`, when known
  - `a2a.tenant`, when set
  - On errors: span status Error plus `error.type` set to the `A2AErrorCode` name.
- **Agent run span** (`a2a.agent.run`) covers the handler's execution. The run can outlive the request (non-blocking send, streaming), so it must still belong to the request's trace. Its parent is the operation span.
- **Task state changes** are span events named `a2a.task.state`, with attribute `a2a.task.state`. They are recorded on the current span when the change is committed (in `TaskWriter`'s commit path). *(Design call.)*
  - During an agent run, that is the agent run span.
  - A cancel from `CancelTaskAsync` lands on the `a2a.server CancelTask` operation span.
- **Push delivery span** (`a2a.push.deliver`) has attributes for task ID, attempt number and outcome. Delivery runs on a background queue, so capture the trace context when an item is queued and use it as the parent. The outgoing webhook request then carries `traceparent`, and WebhookReceiver's spans join the same trace.
  - Do not record the webhook URL's query string or any credential.
  - Record the host only, matching what the current logs already print.

### 3. Client spans

- **One span per `IA2AClient` call**, named `a2a.client <Method>`, with the same attribute names as the server side.
- The HTTP client instrumentation adds the outgoing HTTP span and the `traceparent` header for all three transports.
- **AgentClient** starts one root span per flow (for example `flow push`), so each flow is one trace in the viewer.
- AgentClient is a console app: it must flush telemetry before it exits.

### 4. Metrics

| Name | Type | Tags |
|------|------|------|
| `a2a.server.tasks.completed` | Counter | `a2a.agent`, `a2a.task.state` (final state) |
| `a2a.server.streams.active` | UpDownCounter | `a2a.agent` |
| `a2a.server.push.deliveries` | Counter | `outcome` (`delivered`, `retried`, `failed`, `dropped`) |

- `a2a.server.tasks.completed` counts **terminal states only**: `completed`, `failed`, `canceled` and `rejected`. *(Design call.)*
  - It counts once per task, when the terminal state is committed in `TaskWriter`. That includes cancels, which are written outside any agent run.
  - Interrupted states (`input-required`, `auth-required`) are not counted, so multi-turn tasks are not double-counted. They still appear as `a2a.task.state` span events.

Hosts also collect the built-in ASP.NET Core, HttpClient and .NET runtime meters.

### 5. Logs

- Hosts add the OpenTelemetry logging provider: formatted message on, scopes on.
- Existing log lines are not changed, except where a line would export a secret. The builder checks every existing log call for that.

### 6. LLM

- The demo `IChatClient` is wrapped with `UseOpenTelemetry()` from `Microsoft.Extensions.AI`.
- `EnableSensitiveData` stays **false**, so no prompts or answers are recorded.

### 7. Shared host setup

- A small shared project in `samples/`, `samples/SampleTelemetry`, holds one extension method that the four sample apps call from their `XxxApp.Build` methods.
- **When `OTEL_EXPORTER_OTLP_ENDPOINT` is not set, the method does nothing.** No OpenTelemetry SDK, no instrumentation, no listeners. *(Design call.)* This is what keeps "off by default" exact:
  - .NET only creates request activities, and only adds `traceparent` to outgoing calls, when something is listening.
  - Without a listener, wire traffic stays byte-for-byte as today, including client calls, MockEntra token calls and push webhooks.
  - The integration tests build the samples in-process through `Build`. They never set the variable, so they never start the SDK. That avoids four SDK instances in one process seeing each other's spans.
- When the variable is set, the method adds tracing, metrics and logging, the ASP.NET Core and HTTP instrumentation, the A2A sources and meter, `Microsoft.Extensions.AI`, and the OTLP exporter.
- Service name comes from `OTEL_SERVICE_NAME`, with the app's own name as the default.
- Keep URL query redaction on (the default in these instrumentation versions).
- Health check requests (`/health/*`) are left out of traces, so the timeline isn't noisy.

### 8. AppHost (local only)

- New project `samples/AppHost` using `Aspire.AppHost.Sdk`, added to `A2A.Reference.slnx`. It is not referenced by any other project and not used in any Dockerfile.
- **Resources:** MockEntra, then WebhookReceiver, then AgentServer, each waiting for the previous one to be healthy (the same order as `run-all`). Each uses the same ports as today, with **no Aspire proxy** in front, and environment `Development`.
- **AgentClient** is a resource with **explicit start**, running `all`. You click Start in the dashboard to run the demo flows. Its output shows in the dashboard console view.
- **Dashboard:**
  - Plain HTTP on `localhost` only, which needs `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`.
  - Fixed ports, so the link is always the same.
  - The default browser login token stays on. The link with the token is printed in the console.
- **Usage telemetry off**, set in the AppHost's launch settings:
  - `ASPIRE_DASHBOARD_TELEMETRY_OPTOUT=true`
  - `DOTNET_CLI_TELEMETRY_OPTOUT=1`
  - Note: Microsoft says dashboard usage telemetry is only sent when the dashboard is started from Visual Studio or VS Code. Its docs don't say whether the opt-out also stops unhandled-exception reports, so do not claim it does.
- The only prerequisite is the .NET 10 SDK, with no workload and no Aspire CLI.

### 9. Docs

- **README:** a short "See traces, metrics and logs" section: the one command, the dashboard link, clicking Start on AgentClient, and what you'll see.
- **`docs/production.md` §6:** rewrite from "not implemented" to:
  - what is emitted
  - the source and meter names
  - which attributes are guaranteed never recorded
  - how to point at an OpenTelemetry Collector on AKS by setting `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME`
- **`samples/AppHost/README.md`:** what the AppHost is for, and what it is not for (local only).
- `run-all.ps1` and `run-all.sh` stay unchanged. They pass through `OTEL_*` variables if you set them.

### 10. Tests

- **All telemetry tests use their own `ActivityListener` and `MeterListener`**, never the OpenTelemetry SDK or the sample setup. *(Design call.)*
  - Listeners are process-wide and test classes run in parallel. So each test filters what it sees by its own trace ID (start a root activity in the test) or by its own task ID.
  - Alternatively, put telemetry tests in their own non-parallel xUnit collection.
- **Library tests** check:
  - span names and attributes for each method on each transport
  - that the agent run and push spans are in the request's trace
  - the three metrics
  - an error sets status and `error.type`
- **A "no secrets" test** runs flows with API key and bearer auth plus push with credentials. It asserts that no span attribute, event or tag contains any of the known secret values, and that no message content appears.
- **An integration test** checks that the webhook request received by WebhookReceiver carries a `traceparent` from the same trace as the client call.
- **An "off by default" check.** With no endpoint set, a sample host registers no OpenTelemetry services at all (no `TracerProvider`, `MeterProvider` or OTLP exporter). With an endpoint set, it registers them.
- The AppHost gets no automated run test (that needs Aspire's process launcher in CI). It must build in CI, and it is checked by hand on Windows.

## Success check (manual, on Windows without Docker)

1. `dotnet run --project samples/AppHost` starts MockEntra, WebhookReceiver and AgentServer, and prints the dashboard link.
2. In the dashboard, start AgentClient. Each flow appears as one trace. The `push` flow's trace includes AgentServer's push delivery and WebhookReceiver's handling.
3. Metrics show the three A2A metrics. Logs show up and link to their traces.
4. No span or log contains a token, key, secret or message text.
5. Stopping the AppHost stops all apps.

## Left to the builder

- Where exactly to start the server operation span: a decorator around `IA2ARequestHandler`, or inside `TaskManager`. It must cover all transports in one place.
- Fixed dashboard and OTLP port numbers, as long as they don't clash with 5100–5300.
- How AgentClient sets up and flushes the SDK (for example `Sdk.CreateTracerProviderBuilder` or a generic host).
- Whether AgentClient runs from a separate terminal can also send to the dashboard. This is fine to skip if it needs weakening the dashboard's OTLP auth. If skipped, say so in the README.
- Exact `AddService` resource attributes beyond the service name.
- The name of the shared class and method in `samples/SampleTelemetry`.
- Whether runtime metrics come from the built-in `System.Runtime` meter or a package. Prefer built-in.
