# AgentClient

A console client that runs every A2A demo flow against the sample servers and prints PASS or FAIL for each.

```bash
dotnet run --project samples/AgentClient -- all --transport grpc
dotnet run --project samples/AgentClient -- send --transport rest --verbose
```

Start MockEntra (5100), AgentServer (5200) and WebhookReceiver (5300) first, all with `ASPNETCORE_ENVIRONMENT=Development`.

## Commands

| Command | Default agent | What it shows |
|---|---|---|
| `discover` | all four | Agent cards, bindings, skills, security schemes |
| `send` | report | Blocking send, three artifacts |
| `stream` | report | Streaming events |
| `get` | report | `returnImmediately`, then poll `GetTask`; `historyLength` |
| `list` | report | Filter by context and status, paging |
| `cancel` | report | `CancelTask` (a fast agent may finish first; reported, not an error) |
| `resubscribe` | report | Drop a stream, attach again with `SubscribeToTask` |
| `multi-turn` | booking | `INPUT_REQUIRED` turns on one task |
| `push` | report | Push config, delivery to the WebhookReceiver, config CRUD |
| `auth` | secure | `AUTH_REQUIRED` step-up to the approver, plus 401 and 403 |
| `extended-card` | report | Extended card needs credentials |
| `extensions` | echo, secure | Timestamp and audit extensions |
| `errors` | report, secure, echo | Task not found, not cancelable, version 0.3, content type, extension required, extended card not configured |
| `all` | | Every flow above in that order |

Exit code is 0 when every flow passes, 1 otherwise, 2 for a bad option value. A flow never crashes `all`: an error becomes a failed flow.

## Options (all commands)

| Option | Default | |
|---|---|---|
| `--server` | `http://localhost:5200` | AgentServer base URL |
| `--agent` | per command | Overrides the agent of `send`, `stream`, `get`, `list`, `cancel`, `resubscribe`, `multi-turn`, `push`, `extended-card` |
| `--transport` | `auto` | `jsonrpc`, `rest`, `grpc` or `auto` (first interface of the card) |
| `--auth` | `mock` | `none`, `apikey` (`dev-api-key`), `mock` (client credentials from MockEntra), `entra` (`DefaultAzureCredential`, needs `--scope`) |
| `--scope` | `api://a2a-demo/.default` | Scope for `--auth entra` |
| `--receiver` | `http://localhost:5300` | WebhookReceiver, used by `push` |
| `--verbose` | off | Print every request and response. `Authorization`, `X-API-Key`, `X-A2A-Notification-Token`, `X-A2A-Push-Signature`, `client_secret`, `access_token` and push `credentials`/`token` are masked. SSE and gRPC bodies are not logged. |

The `auth` flow always uses the mock identities (`a2a-client`, `a2a-approver`, `a2a-noroles`), because it needs three different callers.

`appsettings.json` holds the dev defaults (MockEntra URL, tenant, client ids and secrets, API key). These are demo values.
