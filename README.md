# A2A v1.0 reference for .NET 10

A2A (Agent2Agent) is an open protocol that lets one AI agent call another over HTTP or gRPC.
This repo is a working reference of A2A v1.0: a server library, a client library, four demo agents and a console client. Copy the parts you need.

Phase 1 (this repo) runs fully on your machine with mock services. Phase 2 (real Entra, real LLMs, durable storage) is not built; see [docs/production.md](docs/production.md).

## Quick start

Prerequisites: .NET SDK 10.0.401 or newer. Docker is optional. Tests and the first run need no API keys and no network services beyond localhost.

```bash
./run-all.sh                                        # or: pwsh ./run-all.ps1
dotnet run --project samples/AgentClient -- all     # in a second terminal
dotnet test --solution A2A.Reference.slnx           # unit and integration tests (Microsoft.Testing.Platform syntax)
```

`run-all` builds once, then starts MockEntra (5100), WebhookReceiver (5300) and AgentServer (5200 to 5204). Press Ctrl+C to stop all three.
The client prints PASS or FAIL for each flow and exits with 0 when all pass.

With Docker instead:

```bash
docker compose up -d --wait             # starts mock-entra, webhook-receiver, agent-server and waits for health
docker compose run --rm client all      # runs every flow inside the compose network
docker compose down
```

## See traces, metrics and logs

You can watch every flow in a local dashboard. The only prerequisite is the .NET 10 SDK. No Docker, no Aspire CLI, no workload.

```bash
dotnet run --project samples/AppHost
```

1. Open the `http://localhost:18888/login?t=...` link that the command prints.
2. On the Resources page, open the "..." menu of `agentclient` (it shows Not started) and click Start. It runs all 13 flows.
3. Look at the Traces, Metrics and Structured logs pages.

You see one trace per flow, named `agentclient: flow <name>`. Under Metrics, pick `agentserver` to see `a2a.server.tasks.completed`, `a2a.server.streams.active` and `a2a.server.push.deliveries`. Log lines link to their trace. The `push` trace includes spans from `agentserver` and `webhookreceiver`.

The AppHost runs the apps as normal processes on their usual ports. Your data stays on the machine: the dashboard listens on localhost only and keeps data in memory. Usage telemetry is switched off; see [samples/AppHost](samples/AppHost/README.md) for what that does and does not cover.

The dashboard shows a red banner, "No trusted development certificate". It is harmless here, because the AppHost uses plain HTTP on localhost. `dotnet dev-certs https --trust` removes it if you want (optional).

An AgentClient that you start from a separate terminal does not send to the dashboard. The dashboard needs an API key that only apps started by the AppHost get, and it answers 401 otherwise. Start the client from the dashboard instead.

Telemetry is off by default. Nothing is exported unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Tests, `run-all` and docker-compose are unchanged. For a real collector, see [docs/production.md](docs/production.md#6-observability-otlp).

## Repo layout

| Path | What it is |
|---|---|
| [src/A2A.Protocol](src/A2A.Protocol/README.md) | Wire models, JSON rules and error codes. No ASP.NET Core. |
| [src/A2A.Protocol.Grpc](src/A2A.Protocol.Grpc/README.md) | Generated gRPC code and the model-to-proto mapper. |
| [src/A2A.Server](src/A2A.Server/README.md) | Hosting library: JSON-RPC, REST and gRPC endpoints, task manager, push delivery. |
| [src/A2A.Client](src/A2A.Client/README.md) | Client library: one `IA2AClient` over all three transports. |
| [samples/AgentServer](samples/AgentServer/README.md) | Four demo agents. |
| [samples/MockEntra](samples/MockEntra/README.md) | Fake Microsoft Entra ID token service (Development only). |
| [samples/WebhookReceiver](samples/WebhookReceiver/README.md) | Verifies and stores push notifications. |
| [samples/AgentClient](samples/AgentClient/README.md) | Console client that runs every flow. |
| [samples/AppHost](samples/AppHost/README.md) | Local-only launcher with a trace, metrics and logs dashboard. |
| `samples/SampleTelemetry` | OpenTelemetry setup shared by the three server hosts. Off unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set. |
| `tests/` | Four test projects: `A2A.Protocol.Tests`, `A2A.Server.Tests`, `A2A.MockEntra.Tests`, `A2A.Integration.Tests`. |
| `docker/`, `docker-compose.yml` | Images and the compose stack. |
| `.github/` | GitHub Actions CI and Dependabot (see [Continuous integration](#continuous-integration)). |
| [docs/flows.md](docs/flows.md) | One sequence diagram per flow. |
| [docs/production.md](docs/production.md) | What to change for Phase 2. |
| [docs/architecture.html](docs/architecture.html) | Architecture overview (open in a browser). |

`src/` never references `samples/`.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on every push to `master`. It restores, builds and tests `A2A.Reference.slnx` in Release on Ubuntu, with the SDK from `global.json`. A new push to the same ref cancels the run before it.
It does not run the TCK or start Docker or the AppHost.

`.github/dependabot.yml` opens weekly update pull requests for NuGet packages, GitHub Actions and the Docker base images in `docker/`.

## Ports and transports

| Service | Port | Transport |
|---|---|---|
| MockEntra | 5100 | HTTP |
| AgentServer | 5200 | HTTP: agent cards, JSON-RPC, REST, push JWKS, health |
| AgentServer gRPC, `echo` | 5201 | gRPC over plain HTTP/2 (h2c) |
| AgentServer gRPC, `report` | 5202 | gRPC (h2c) |
| AgentServer gRPC, `booking` | 5203 | gRPC (h2c) |
| AgentServer gRPC, `secure` | 5204 | gRPC (h2c) |
| WebhookReceiver | 5300 | HTTP |

Integration tests use the same layout shifted to base 15000 (env `A2A_TEST_PORT_BASE` overrides).

URLs on port 5200:

| What | URL |
|---|---|
| Root card (the `report` agent) | `GET /.well-known/agent-card.json` |
| Card of agent `{name}` | `GET /agents/{name}/.well-known/agent-card.json` |
| JSON-RPC | `POST /agents/{name}/jsonrpc` |
| REST base | `/agents/{name}/rest` (for example `POST /agents/report/rest/message:send`) |
| gRPC | `http://localhost:{agent port}` |

Every request needs the header `A2A-Version: 1.0` (gRPC metadata `a2a-version`). Cards are exempt.

## Development-only test values

WARNING: these values are public demo values. They are in this repo on purpose. Never use them outside local development, and never deploy MockEntra.

| Item | Value |
|---|---|
| Tenant | `a2a00000-0000-0000-0000-000000000001` |
| Audience | `api://a2a-demo` |
| Scope | `api://a2a-demo/.default` |
| Client `a2a-client` | secret `dev-client-secret`, roles `A2A.Invoke` |
| Client `a2a-approver` | secret `dev-approver-secret`, roles `A2A.Invoke`, `Reports.Approve` |
| Client `a2a-noroles` | secret `dev-noroles-secret`, no roles |
| API key | header `X-API-Key`, value `dev-api-key` |
| Token endpoint | `POST http://localhost:5100/a2a00000-0000-0000-0000-000000000001/oauth2/v2.0/token` |

They live in `samples/MockEntra/appsettings.Development.json` and `samples/AgentServer/appsettings.Development.json`.

## Agents

| Agent | Policy | Auth on the card | Capabilities | What it demonstrates |
|---|---|---|---|---|
| `echo` | open | none | streaming; extension `https://example.com/a2a/ext/timestamp/v1` (optional) | Direct message reply (no task), text, JSON and file parts, extensions |
| `report` | `Invoke` | Entra OR API key | streaming, push, extended card | Long task: WORKING, three artifacts, cancel, resubscribe, push, list/get |
| `booking` | `Invoke` | Entra OR API key | streaming, push | Multi-turn: `INPUT_REQUIRED` until you give a date |
| `secure` | `InvokeBearer` | Entra only | streaming, push, extended card; extension `https://example.com/a2a/ext/audit/v1` (required) | `AUTH_REQUIRED` step-up, 401 and 403, required extension |

`Invoke` means authenticated plus role `A2A.Invoke`. `InvokeBearer` means `Invoke` and a bearer token (an API key gets 403).
Agent code is in `samples/AgentServer/Agents/`; cards are in `samples/AgentServer/Cards/AgentCards.cs`.

## Flow map

Run one flow with `dotnet run --project samples/AgentClient -- <command>`. Options (`--transport jsonrpc|rest|grpc|auto`, `--auth`, `--verbose`, ...) are in the [AgentClient README](samples/AgentClient/README.md). Diagrams: [docs/flows.md](docs/flows.md).

| Flow | Agent | Console command | Test class |
|---|---|---|---|
| Discovery and cards | all four | `discover` | `DiscoveryAndAuthTests` |
| Blocking send | report | `send` | `ReportFlowTests` |
| Streaming send | report | `stream` | `ReportFlowTests` |
| Get task and history | report | `get` | `ReportFlowTests` |
| List, filter, page | report | `list` | `ReportFlowTests` |
| Cancel | report | `cancel` | `ReportCancelFlowTests` |
| Resubscribe | report | `resubscribe` | `ResubscribeTests` |
| Multi-turn | booking | `multi-turn` | `BookingFlowTests` |
| Auth-required step-up | secure | `auth` | `SecureFlowTests` |
| Push notifications | report | `push` | `PushFlowTests`, `ReceiverVerificationTests` |
| Extended card | report | `extended-card` | `DiscoveryAndAuthTests` |
| Extensions | echo, secure | `extensions` | `EchoFlowTests`, `SecureFlowTests` |
| Errors | report, secure, echo | `errors` | `ReportFlowTests`, `SecureFlowTests`, `EchoFlowTests` |
| Everything, through the console runner | all | `all` | `ConsoleFlowRunnerTests` |

Every public test class under `tests/A2A.Integration.Tests/Flows/` is named in this table. `DocsTests` fails if a console command or a flow test class is missing from this table.

## Copy the server side

1. Copy or reference `src/A2A.Protocol`, `src/A2A.Protocol.Grpc` and `src/A2A.Server` ([details](src/A2A.Server/README.md)), plus `proto/` (`A2A.Protocol.Grpc` compiles `proto/a2a.proto`).
2. Write an `IAgentHandler` (see `samples/AgentServer/Agents/EchoAgent.cs` for the smallest one).
3. Register it with `AddA2AServer(...).AddAgent<THandler>(name, publicCard, extendedCard, policy)`, then call `MapA2A()` and `MapA2AGrpc()`.
4. Bring your own authentication and authorization policies. `samples/AgentServer/Auth/AuthSetup.cs` shows Entra JWT plus API key.
5. Swap `ITaskStore` and `IPushConfigStore` for durable ones before production.

`samples/AgentServer/AgentServerApp.cs` is the full wiring. The default request handler records spans and metrics with `System.Diagnostics`; see [docs/production.md](docs/production.md#6-observability-otlp).

## Copy the client side

1. Copy or reference `src/A2A.Protocol`, `src/A2A.Protocol.Grpc` and `src/A2A.Client` ([details](src/A2A.Client/README.md)), plus `proto/` (`A2A.Protocol.Grpc` compiles `proto/a2a.proto`).
2. Fetch the card with `A2ACardResolver`.
3. Create a client with `A2AClientFactory.Create(card, new A2AClientOptions { ... })`. It picks JSON-RPC, REST or gRPC from the card. The result is a `TracingA2AClient` that records one span per call; its `Inner` property is the transport client.
4. Pick credentials: `ClientCredentialsProvider` (any OAuth2 token URL, including Entra), `AzureIdentityCredentialProvider`, `ApiKeyCredentialProvider`, `BearerTokenCredentialProvider`, or your own `IA2ACredentialProvider`.
   `ClientCredentialsProvider` sends your client secret to the token URL, so it does not trust the card blindly. Either pass the token URL yourself (`tokenUrlOverride`), or list the hosts you trust in `allowedTokenHosts` (for example `login.microsoftonline.com`). A card token URL is used only when its host is in that list and it is `https` (`http` only for localhost, 127.0.0.1 or ::1). Anything else throws `A2AAuthException` (401) and no request is sent.

`samples/AgentClient/FlowRunner*.cs` shows every operation.

## Phase 1 limits

- Single instance only. The event broker, push queue and running agent work live in process memory.
- All state is in memory: tasks, push configs and the push signing key (a new key at every start).
- Auth is mock: MockEntra signs tokens with a throwaway key. No real Entra tenant is used or tested.
- Agents use a fake LLM (`FakeChatClient` behind `IChatClient`). `Llm:Provider` values other than `Fake` fail at startup.
- HTTP and gRPC are plain text (no TLS).
- `run-all.ps1` exists but was not tested on this machine. `run-all.sh` is the script that was used.

Everything to change for Phase 2 is in [docs/production.md](docs/production.md).

## Conformance

The SDK follows A2A v1.0.1. Where the spec leaves a choice open, the choice is written down in [docs/spec-decisions.md](docs/spec-decisions.md). That page also shows how to run the official A2A TCK against `tests/A2A.Tck.Sut`, a small server that serves all three transports. CI checks that `proto/a2a.proto` equals the `v1.0.1` release file; it does not run the TCK yet.
