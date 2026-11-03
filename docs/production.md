# Production notes (Phase 2)

Everything below is **Phase 2 — not implemented**, except section 6 (observability), which is built. Phase 1 runs on one machine with mocks. This page lists what you must change to run the AgentServer for real. Back to the [README](../README.md).

Config keys can be set in `appsettings.json` or as environment variables (double underscore for a colon, for example `Auth__Authority`).

## Checklist

| Area | Status |
|---|---|
| Real Entra ID | Phase 2 — not implemented (wiring exists, never verified against a real tenant) |
| Real LLM provider | Phase 2 — not implemented |
| Durable `ITaskStore` and `IPushConfigStore` | Phase 2 — not implemented |
| TLS on HTTP and gRPC | Phase 2 — not implemented |
| Push signing key from a key store | Phase 2 — not implemented |
| `PushOptions.AllowPrivateNetworks=false` hardening | Phase 2 — not implemented (the switch exists, the default is already false) |
| OpenTelemetry / OTLP | Built, off by default (see section 6). Pointing it at a real collector on AKS is not verified. |
| Rate limits, HTTPS redirection, HSTS | Phase 2 — not implemented |
| Multi-instance support | Phase 2 — not implemented |

## 1. Real Entra ID

Phase 2 — not implemented.

1. Register an API app in Entra. Set its Application ID URI (for example `api://<your-app>`). This is the audience.
2. Add two app roles to that app, allowed for applications: `A2A.Invoke` and `Reports.Approve`. The server policies check these role names in the `roles` claim.
3. Register one client app per caller. Give each a secret or certificate and assign the app roles it needs (`A2A.Invoke` for normal callers, plus `Reports.Approve` for approvers). Grant admin consent.
4. Set the server config:

| Key | Value |
|---|---|
| `Auth:Authority` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `Auth:Audience` | The Application ID URI, for example `api://<your-app>` |
| `Auth:TokenUrl` | `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token` (advertised in the cards) |
| `Auth:OpenIdConfigurationUrl` | `https://login.microsoftonline.com/<tenant-id>/v2.0/.well-known/openid-configuration` |
| `Auth:RequireHttpsMetadata` | `true` (the default) |
| `Auth:ApiKeys` | Your own keys from a secret store, or none. The `dev-api-key` exists only in Development. |

`Auth:RequireHttpsMetadata=true` now fails fast: the server refuses to start when `Auth:Authority` is not an `https://` URL.
The card scope is `{Audience}/.default`, so keep the audience in the `api://...` form.

5. Clients ask for the scope `api://<your-app>/.default` with `ClientCredentialsProvider` or `AzureIdentityCredentialProvider`.
6. Do not run MockEntra. It refuses to start outside Development. Check the `aud`, `iss`, `tid` and `roles` claims of a real token before you rely on them: the server reads `roles`, `tid` and `azp`. The task owner is one client app in one tenant: `tenant:{tid}/app:{azp}` (falls back to `appid`, then `sub`/`oid`). Another app in the same tenant cannot see, list, cancel, subscribe to or configure push for that task. The one exception is step-up: a task in `AUTH_REQUIRED` may be continued by another app of the same tenant (`IOwnerResolver.CanContinueInterrupted`). Replace `IOwnerResolver` if you need a different rule.
7. The Docker images do not set `ASPNETCORE_ENVIRONMENT`, so they run as Production by default. Only `docker-compose.yml` sets `Development` for each service. Do not set `Development` in a real deployment: it turns on Development-only settings (API key, plain-HTTP metadata, private push targets).
8. Clients send their client secret only to a token URL they trust. `ClientCredentialsProvider` uses the card's `tokenUrl` only when its host is in `allowedTokenHosts` and it is `https` (plain `http` only for loopback). Otherwise pass the token URL yourself.

## 2. LLM provider

Phase 2 — not implemented.

`Llm:Provider` accepts only `Fake` in Phase 1. These values fail at startup with a "planned for Phase 2" message:

| Value | What is missing |
|---|---|
| `AzureOpenAI` | An `IChatClient` registration in `ChatClientRegistration.AddDemoChatClient`, endpoint, deployment name and key or managed identity |
| `OpenAI` | The same, with an API key |
| `Anthropic` | The same, with an API key |
| `Ollama` | The same, with a base URL. `docker compose --profile ollama` only starts a container; no agent uses it. |

Keys must come from environment variables or a secret store, never from the repo. The seam is `Microsoft.Extensions.AI.IChatClient`: agents (see `ReportAgent`) already take it by constructor injection.

## 3. Durable storage

Phase 2 — not implemented. Both stores are in memory and lose everything on restart.

- `ITaskStore` (`src/Ambinder.A2A.Server/Tasks/ITaskStore.cs`): implement `GetAsync`, `SaveAsync`, `ListAsync` on a database. `ListAsync` must keep the owner filter, the sort (newest status first) and the page tokens.
- `IPushConfigStore` (`src/Ambinder.A2A.Server/Push/IPushConfigStore.cs`): implement the six methods. Webhook credentials are secrets: encrypt them at rest.
- Register your types before `AddA2AServer` (it uses `TryAdd`).

## 4. TLS

Phase 2 — not implemented. Phase 1 uses plain HTTP on 5200 and plain HTTP/2 (h2c) on 5201 to 5204.

- Terminate TLS in Kestrel (certificate in config or a secret store) or at a reverse proxy in front.
- gRPC clients need an `https://` address; the card URLs come from `A2A:PublicBaseUrl`, so set it to the public `https://` URL.
- `A2AClientOptions.ClientCertificate` is available for mutual TLS.
- Add HTTPS redirection and HSTS in the host (`app.UseHttpsRedirection()`, `app.UseHsts()`).

## 5. Push notifications

Phase 2 — not implemented.

**Signing key.** `PushSigningKey` creates a new RSA 2048 key at every start and serves it at `/.well-known/a2a-push-jwks.json`. A restart changes the key and a second instance has a different key. Load a stable key from a key store (for example Azure Key Vault) and publish all active public keys in the JWKS.

**Private networks.** `PushOptions.AllowPrivateNetworks` is `false` by default; only `samples/AgentServer/appsettings.Development.json` turns it on. With `false`, webhook URLs must be `https` and must not point to loopback or private addresses. Keep it `false` in production (`A2A:Push:AllowPrivateNetworks`). The check runs again when the connection opens, so DNS changes after config creation do not bypass it.

**Proxy note.** Push delivery never uses a proxy: the `"a2a-push"` handler sets `UseProxy=false`, so system proxy settings (`HTTP_PROXY`, `HTTPS_PROXY`) are ignored. The address check in `ConnectCallback` therefore always sees the webhook host. If your network needs an egress proxy, replace the handler and make the proxy block private ranges itself.

**Other push settings.** `A2A:Push:MaxRetries` (3), `InitialBackoff` (1 s), `Timeout` (15 s). Webhook receivers should verify the JWT as shown in `samples/WebhookReceiver`.

## 6. Observability (OTLP)

Built, and off by default. Nothing is exported unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set to a non-empty value. The libraries in `src/` use only `System.Diagnostics` and reference no OpenTelemetry package. The sample hosts add the OpenTelemetry SDK and an OTLP exporter. For a local view, see [samples/AppHost](../samples/AppHost/README.md).

**Source and meter names.**

| Name | Kind | Emitted by |
|---|---|---|
| `A2A.Server` | activity source and meter | `A2A.Server` |
| `A2A.Client` | activity source | `A2A.Client` |
| `AgentClient` | activity source | the per-flow spans of the AgentClient sample |
| `Grpc.Net.Client` | activity source | gRPC client calls; AgentClient exports it |
| `AgentServer.Llm` | activity source | the demo LLM client |

Names are constants in `A2ATelemetryNames` (`src/Ambinder.A2A.Protocol`). If you copy the libraries, add these names to your own exporter.

**Spans.**

- `a2a.server <Method>`: one per A2A operation, the same for JSON-RPC, REST and gRPC. Child of the ASP.NET Core request span. Streaming spans stay open for the whole stream. Attributes: `a2a.method`, `a2a.agent`, `a2a.transport` (`jsonrpc`, `rest`, `grpc`), `a2a.task.id`, `a2a.context.id`, `a2a.tenant` (the last three when known). On errors the status is Error. `error.type` is the A2A error code name, or the exception's full type name for any other exception. A call that the caller cancels is not an error.
  - Requests that a transport rejects before the handler get no `a2a.server` span. That covers validation errors, an unsupported version or extension, an unknown agent or method, and missing authentication. They show only as the ASP.NET Core request span, with its HTTP status.
- `a2a.agent.run`: the agent handler's run. It stays in the request's trace even when the run outlives the request. Attributes: `a2a.agent`, `a2a.task.id`, `a2a.context.id`. When the agent throws (and the task was not canceled), the status is Error and `error.type` is the exception's full type name.
- Event `a2a.task.state` (attribute `a2a.task.state`): one per committed task state change, on the current span.
- `a2a.push.deliver`: one per webhook delivery, in the trace of the call that queued it. The webhook request carries `traceparent`. Attributes: `a2a.agent`, `a2a.task.id`, `a2a.push.attempt`, `server.address` (host only) and `outcome`. `outcome` is `delivered`, `failed` or `canceled`. `canceled` means shutdown, or the config was deleted during a retry. It is set on the span only; the metric does not count it.
- `a2a.client <Method>`: one per `IA2AClient` call. Attributes: `a2a.method`, `a2a.agent` (the agent card's name), `a2a.transport`, and `a2a.task.id`, `a2a.context.id` and `a2a.tenant` when known. `error.type` follows the server rule. The HTTP client instrumentation adds the outgoing request span.
- `flow <name>` (AgentClient sample): one root span per demo flow, so each flow is one trace. The Aspire dashboard shows it as `agentclient: flow <name>`. On failure the status description is only the exception type and the A2A code or HTTP status. The full failure text stays in the console.
- Demo LLM client spans (`AgentServer.Llm`) come from `UseOpenTelemetry()` in `Microsoft.Extensions.AI`.

**Metrics** (meter `A2A.Server`).

| Name | Type | Tags |
|---|---|---|
| `a2a.server.tasks.completed` | counter | `a2a.agent`, `a2a.task.state`. Counts final states only: `completed`, `failed`, `canceled`, `rejected`. Once per task. |
| `a2a.server.streams.active` | up-down counter | `a2a.agent` |
| `a2a.server.push.deliveries` | counter | `a2a.agent`, `outcome` (`delivered`, `retried`, `failed`, `dropped`). `dropped` counts once per queued event (the queue was full). The others count per webhook config: `delivered` and `failed` once per delivery, `retried` once per retry. |

The sample hosts also collect the built-in ASP.NET Core and HttpClient meters and the .NET runtime meter (`System.Runtime`). Health check requests (`/health/*`) are left out of traces.

**Logs.** The sample hosts add the OpenTelemetry logging provider with the formatted message and scopes on. Log lines carry the trace ID, so a viewer can link them to a trace. Existing log lines are unchanged.

**Never recorded.** No span name, attribute, event or metric tag holds any of these:

- tokens, API keys, client secrets, credentials and push tokens
- push signatures and `Authorization` values
- URL query strings
- webhook URL paths on the sending side. The push span records the host only. The sample hosts also cut the HttpClient span's `url.full` to `scheme://host[:port]` for webhook requests (requests under an `a2a.push.deliver` span). A receiver that exports its own request spans records its own path; the sample WebhookReceiver does.
- message, part and artifact content
- LLM prompts and answers. `EnableSensitiveData` is pinned to `false` in code, even if `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` is set.

Tests check this with known secret values.

**Logs show webhook paths.** The `Push to {Url}` log lines print the webhook URL without its query, but with its path. HttpClient's own request logs do the same. With telemetry on, these lines are exported too. Do not put secrets in webhook paths, or treat these logs as secret.

**Logged exceptions are not scrubbed.** When telemetry is on, a logged exception is exported with its message and stack trace. Agent authors must not put secrets or user content in exception messages.

**API notes.**

- `A2AClientFactory.Create` returns a `TracingA2AClient`. Use its `Inner` property to get the transport client. A transport client you construct yourself records no spans.
- The default `IA2ARequestHandler` from `AddA2AServer` is `TracingRequestHandler`, which wraps `TaskManager`.

**Point it at an OpenTelemetry Collector on AKS.** Set these on each pod (for example in the Deployment env, or from a ConfigMap):

```
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector.observability:4317
OTEL_SERVICE_NAME=agent-server
```

`OTEL_SERVICE_NAME` is the name shown in the viewer. The defaults are the app names (`agentclient` for the client). Other standard `OTEL_EXPORTER_OTLP_*` variables (protocol, headers) work too. Use a collector address that is reachable only inside the cluster, or add authentication headers. The Aspire AppHost is for local use only; you do not need it on AKS.

## 7. Limits and hardening

Phase 2 — not implemented: rate limiting on public endpoints. `A2A:MaxRequestBodyBytes` (4 MB) is the only request size limit. The health endpoints are `/health/live` and `/health/ready`.

## 8. Multi-instance

Phase 2 — not implemented. Run exactly one instance. Reasons:

- `TaskEventBroker` (streaming, resubscribe) and the push queue are in process memory. A second instance cannot see events of the first.
- Agent execution and its cancellation tokens live in the process that accepted the request. A cancel that hits another instance does nothing.
- Per-task semaphores are in memory.
- The push signing key differs per instance.

To scale out you need a shared store plus a shared event bus (for example a message broker), sticky or task-aware routing, and one shared signing key.
