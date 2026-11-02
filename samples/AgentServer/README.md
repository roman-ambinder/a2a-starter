# AgentServer

Demo A2A v1.0 server with four agents over JSON-RPC, REST (HTTP+JSON) and gRPC. Auth: Entra-shaped bearer tokens
(from [MockEntra](../MockEntra/README.md) in Development) or a development API key.

Run MockEntra first, then:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project samples/AgentServer
```

In Development the API key `dev-api-key` and plain-HTTP metadata (`RequireHttpsMetadata=false`) are enabled.
Other environments need an HTTPS authority and their own `Auth:ApiKeys`.

## Agents

| Agent | Policy | Card security | gRPC port | Notes |
|---|---|---|---|---|
| `echo` | open | none | 5201 | text, JSON, any file; extension `https://example.com/a2a/ext/timestamp/v1` (optional) |
| `report` | `Invoke` | `entra` OR `apiKey` | 5202 | push, extended card (+ skill `report-archive`); root card |
| `booking` | `Invoke` | `entra` OR `apiKey` | 5203 | push |
| `secure` | `InvokeBearer` | `entra` | 5204 | push, extended card (+ skill `audit-log`); extension `https://example.com/a2a/ext/audit/v1` (required) |

`Invoke` = authenticated + role `A2A.Invoke`. `InvokeBearer` = `Invoke` and the caller used a bearer token (API key → 403).
The agents are stubs until Tasks 15–16.

## URLs (HTTP port 5200)

- Root card (report): `GET /.well-known/agent-card.json`
- Agent card: `GET /agents/{name}/.well-known/agent-card.json`
- JSON-RPC: `POST /agents/{name}/jsonrpc`; REST base: `/agents/{name}/rest`; gRPC: `http://localhost:{port}`
- Push JWKS: `GET /.well-known/a2a-push-jwks.json`
- Health: `GET /health/live`, `GET /health/ready`

## Auth

Default scheme `A2A` forwards to `ApiKey` when the `X-API-Key` header is present, else to JwtBearer (`Bearer`).
API key callers get identity type `ApiKey`, claim `a2a:apikey` = key name and role `A2A.Invoke`.
Bearer tokens keep Entra claim names (`roles`, `tid`, `azp`); task owner = `tenant:{tid}` or `apikey:{name}`.
If the identity provider is down, bearer calls get 401 (with a warning in the log), never 500.

```bash
TOKEN=$(curl -s -d grant_type=client_credentials -d client_id=a2a-client -d client_secret=dev-client-secret \
  -d scope=api://a2a-demo/.default \
  http://localhost:5100/a2a00000-0000-0000-0000-000000000001/oauth2/v2.0/token | jq -r .access_token)
curl -s -H "Authorization: Bearer $TOKEN" -H "A2A-Version: 1.0" -H "Content-Type: application/a2a+json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"Q3 sales"}]}}}' \
  http://localhost:5200/agents/report/jsonrpc
curl -s -H "X-API-Key: dev-api-key" -H "A2A-Version: 1.0" http://localhost:5200/agents/report/rest/tasks
```

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `A2A:PublicBaseUrl` | `http://localhost:5200` | HTTP listener and card URLs |
| `A2A:DefaultAgent` | `report` | agent of the root card |
| `A2A:GrpcPorts:{agent}` | 5201–5204 | per-agent gRPC (h2c) port |
| `A2A:Push:*` | see `PushOptions` | push retries, backoff, `AllowPrivateNetworks` (true in Development) |
| `Auth:Authority` / `Auth:Audience` | MockEntra tenant / `api://a2a-demo` | JwtBearer validation |
| `Auth:TokenUrl` / `Auth:OpenIdConfigurationUrl` | MockEntra URLs | advertised in the cards (`entra`, `entraOidc`) |
| `Auth:RequireHttpsMetadata` | true (false in Development) | JwtBearer metadata over HTTPS only |
| `Auth:ApiKeys:{name}` | `dev` = `dev-api-key` in Development | API keys |
| `FakeAgents:StepDelay` | `00:00:00.5` | pause between agent steps |
| `Llm:Provider` | `Fake` | `Fake` only; `AzureOpenAI`, `OpenAI`, `Anthropic`, `Ollama` fail at startup (Phase 2) |

Settings files are copied to `bin/.../agentserver-config/` (the content root), so a project that references both
AgentServer and MockEntra does not get two `appsettings.json` files in one folder.
