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
| [docs/flows.md](docs/flows.md) | One sequence diagram per flow. |
| [docs/production.md](docs/production.md) | What to change for Phase 2. |
| [docs/architecture.html](docs/architecture.html) | Architecture overview (open in a browser). |

`src/` never references `samples/`.

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

`samples/AgentServer/AgentServerApp.cs` is the full wiring.

## Copy the client side

1. Copy or reference `src/A2A.Protocol`, `src/A2A.Protocol.Grpc` and `src/A2A.Client` ([details](src/A2A.Client/README.md)), plus `proto/` (`A2A.Protocol.Grpc` compiles `proto/a2a.proto`).
2. Fetch the card with `A2ACardResolver`.
3. Create a client with `A2AClientFactory.Create(card, new A2AClientOptions { ... })`. It picks JSON-RPC, REST or gRPC from the card.
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

## Optional conformance

The official A2A TCK (technology compatibility kit) can check this server. It is not part of `dotnet test`; run it in CI as a separate job:

1. Start the stack (`./run-all.sh` or `docker compose up -d --wait`).
2. Check out the TCK from the A2A project and point its system-under-test URL at `http://localhost:5200` (the root card is the `report` agent). The `report` agent needs credentials, so give the TCK the header `X-API-Key: dev-api-key` or a MockEntra token. The open `echo` agent is at `http://localhost:5200/agents/echo`.
3. Run the TCK as described in its own README, and fail the CI job on any failure.

The exact TCK command and setting names depend on the TCK version, so they are not copied here.
