# Production notes (Phase 2)

Everything below is **Phase 2 — not implemented**. Phase 1 runs on one machine with mocks. This page lists what you must change to run the AgentServer for real. Back to the [README](../README.md).

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
| OpenTelemetry / OTLP | Phase 2 — not implemented |
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
6. Do not run MockEntra. It refuses to start outside Development. Check the `aud`, `iss`, `tid` and `roles` claims of a real token before you rely on them: the server reads `roles` and `tid` (task owner is `tenant:{tid}`) and `azp`.
7. The Docker images set `ASPNETCORE_ENVIRONMENT=Development`. Override it for real use, or Development-only settings (API key, plain-HTTP metadata, private push targets) turn on.

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

- `ITaskStore` (`src/A2A.Server/Tasks/ITaskStore.cs`): implement `GetAsync`, `SaveAsync`, `ListAsync` on a database. `ListAsync` must keep the owner filter, the sort (newest status first) and the page tokens.
- `IPushConfigStore` (`src/A2A.Server/Push/IPushConfigStore.cs`): implement the six methods. Webhook credentials are secrets: encrypt them at rest.
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

**Proxy note.** The push `HttpClient` checks the address in its `ConnectCallback`. That check assumes the connection goes straight to the webhook host. If the process uses an HTTP proxy, the callback sees the proxy address, not the webhook address. Either set `UseProxy=false` on the handler, or send push traffic through an egress proxy that blocks private ranges itself.

**Other push settings.** `A2A:Push:MaxRetries` (3), `InitialBackoff` (1 s), `Timeout` (15 s). Webhook receivers should verify the JWT as shown in `samples/WebhookReceiver`.

## 6. Observability (OTLP)

Phase 2 — not implemented. There is no OpenTelemetry code. Add the OpenTelemetry packages for traces, metrics and logs and an OTLP exporter configured by the standard variable `OTEL_EXPORTER_OTLP_ENDPOINT`. Leave it off locally.

## 7. Limits and hardening

Phase 2 — not implemented: rate limiting on public endpoints. `A2A:MaxRequestBodyBytes` (4 MB) is the only request size limit. The health endpoints are `/health/live` and `/health/ready`.

## 8. Multi-instance

Phase 2 — not implemented. Run exactly one instance. Reasons:

- `TaskEventBroker` (streaming, resubscribe) and the push queue are in process memory. A second instance cannot see events of the first.
- Agent execution and its cancellation tokens live in the process that accepted the request. A cancel that hits another instance does nothing.
- Per-task semaphores are in memory.
- The push signing key differs per instance.

To scale out you need a shared store plus a shared event bus (for example a message broker), sticky or task-aware routing, and one shared signing key.
