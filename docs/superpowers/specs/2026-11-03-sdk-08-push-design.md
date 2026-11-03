# SDK package 08: Push notifications and webhook receiver

- Date: 2026-11-03
- Status: approved by owner 2026-11-03
- Milestone: M1 (`1.0.0-preview`)
- Size: L

## Intent

Webhooks are how long tasks report back across products. Today push delivery loses events in four ways: on restart, when the queue is full, after the retries run out, and when replicas disagree on the signing key. Its main security feature, a signature JWT, is a custom header that no other SDK knows, and it cannot use a corporate egress proxy.

This package makes delivery durable and at-least-once. It does so through the store contract of package 05, with ordered retries and a dead-letter state. It sends the spec-standard push credentials first. It keeps the signature JWT as a documented, optional A2A extension, backed by a pluggable signing key (Key Vault or configured keys), with rotation and a JWKS of all active keys. It adds proxy support without weakening the SSRF guard.

It also ships `Ambinder.A2A.Push.Receiver`, so the teams that receive webhooks do not each write verification, task matching and dedupe by hand.

## Findings addressed

- R16: the push signing key is per process and not pluggable. There is no Key Vault, no rotation and no multi-key JWKS.
- R17: push delivery is lossy (queue drop, restart, no dead-letter). The signature is non-standard. `UseProxy=false` conflicts with egress proxies.
- R15 (push part): the number of push configs per task is unbounded.
- R6 (push part): `PushNotificationSender`, `PushSigningKey` and `WebhookUrlValidator` are public implementation details.
- Pain point 12, "Persistent task store, multi-instance servers, push-notification delivery": durable delivery, spec push auth (python #585 ignored `authentication`), SSRF guard, and a verification helper for receivers.
- Package 14, debt PD7: the in-memory queue and per-task lanes, and signing plus request building per delivery (`src/A2A.Server/Push/PushNotificationSender.cs:24,33-36`). The outbox redesign closes it; acceptance uses the `Push` benchmark scenarios.
- Pain point 9, "Waiting for completion": the push path that package 10's wait helper uses needs a receiver that it can listen to.

## Goals

1. A task event committed by the task writer reaches every push config of that task at least once. This holds through a process crash at any point after the commit, on the durable stores of package 05. Tested with a kill-and-restart test.
2. Deliveries to one config arrive in event order. A failing webhook never delays other configs or other tasks. Tested.
3. A delivery that keeps failing ends in a dead-letter state after the retry budget. It can be listed, requeued and purged through `IPushDeadLetters`. Nothing is dropped silently: the only drop path (an in-memory outbox at capacity) dead-letters the entry and counts it. Tested.
4. Every delivery sends `config.authentication` as `Authorization: {scheme} {credentials}` and `config.token` in the notification token header. A plain receiver from another A2A SDK can authenticate it (interop test with package 16).
5. With the signature extension on, all replicas sign with keys from one `IPushSigningKeySource` (built on package 04's `A2ASigningKey`). The JWKS lists every active public key. A key rotation causes no failed verification at a receiver that caches the JWKS for up to the configured overlap. Tested with a fake key source. A live Key Vault test is written and runs only when its secrets exist (owner decision O3).
6. Push delivery can go through an HTTP proxy. When it does, the SDK still refuses private and loopback targets, and it refuses to start in production unless the operator states that the proxy enforces egress rules. Tested.
7. `services.AddA2APushReceiver(...)` plus `app.MapA2APushReceiver("/a2a/webhook", handler)` gives a receiver that does five things:
   - checks the body size and content type
   - checks the credentials and token
   - optionally verifies the signature extension
   - checks that the task id is an expected one
   - deduplicates deliveries before it calls the handler

   A receiver is about 10 lines. Tested.
8. The push feature adds no Azure dependency to `Ambinder.A2A.Server`. Key Vault support is a separate package.

## Non-goals

- The store schema and providers for outbox rows and push configs. That is package 05. This package defines what it needs from them.
- Cross-pod forwarding of push-config operations. Push-config CRUD goes through the task store, so any pod can serve it. Delivery coordination uses outbox claims, not the cluster layer of package 06.
- Converting 0.3 push bodies for legacy receivers. That is package 15. It plugs a body converter into the receiver.
- The client-side "wait using push" mode. That is package 10. It owns the push-wait abstractions `IA2APushWaiter` and `PushSubscription` in `Ambinder.A2A.Client`; this package's receiver implements them *(Claude's call, B10)*.
- Agent-identity (Entra) tokens that the agent obtains for a webhook. The `IPushRequestAuthenticator` seam exists, but nothing ships for it in v1.
- An HTTP admin API for dead letters. Teams call `IPushDeadLetters` from their own admin surface.
- Card signing keys. That is package 04, which may reuse the Key Vault package (see Design).

## Anti-goals

- A delivery path that can lose a committed event without a metric and a log line.
- A signature feature that only works with this SDK's receiver and is presented as if it were the A2A standard.
- Replicas that publish different JWKS documents, so that verification depends on which pod a receiver reaches.
- An SSRF guard that a proxy setting, a redirect or a DNS change can bypass.
- Webhook credentials or tokens in logs, traces, outbox rows or exception messages.
- A receiver helper that accepts a body before it has authenticated it, or that runs the handler twice for one delivery in normal operation.
- Network I/O inside the task write. The outbox contributor and the payload formatter must stay "return quickly", like the current observer contract (`src/A2A.Server/Tasks/ITaskEventObserver.cs:5-10`).

## Constraints

- Spec v1.0.1, push (sections 4.3 and 7, from `research-spec.md`):
  - The agent MUST attempt delivery at least once. The payload is a `StreamResponse`.
  - The agent MUST send the configured authentication credentials. It SHOULD retry with exponential backoff and a 10–30 s timeout, and it SHOULD validate webhook URLs against SSRF.
  - The receiver MUST validate authenticity, return 2xx, and validate that the task id matches an expected task. It SHOULD be idempotent and use HTTPS.
  - Delete of a push config MUST be idempotent.
  - Capability gating: `pushNotifications: false` → `PushNotificationNotSupportedError` on all four push operations.
- Proto: `AuthenticationInfo.scheme` is an IANA HTTP auth scheme, case-insensitive (RFC 9110 §11.1). `credentials` depends on the scheme (`proto/a2a.proto:325-332`). `TaskPushNotificationConfig` has `tenant`, `id`, `task_id`, `url` (required), `token` and `authentication` (`proto/a2a.proto:470-485`).
- Work plan: "05 → 06, 08". The push outbox uses the store contract (outbox rows). There is no Redis or message bus in v1. The core has no Azure dependency.
- Package 07: push-config operations are scoped by (agent, tenant, owner) like task operations.
- No cloud resources for now (owner decision O3). Live Key Vault tests are written but run only when their secrets exist, and are skipped visibly otherwise. Until then fakes and the manual pre-release checklist meet the exit criteria.
- Content type of the delivery: `application/a2a+json` (spec v1.0.1 prefers it). This is what the sender uses today (`src/A2A.Server/Push/PushNotificationSender.cs:228`).

## Design

### Packages

| Package | Contents |
|---|---|
| `Ambinder.A2A.Server` (core) | push-config validation, `PushDeliveryService` (outbox dispatcher), `PushOutboxContributor`, `IPushPayloadFormatter`, `IPushSigningKeySource`, `EphemeralPushSigningKeySource`, local push keys, the signature extension, the SSRF guard, `IPushDeadLetters`, `IPushCredentialProtector`, `PushOptions` |
| `Ambinder.A2A.Azure.KeyVault` (defined by package 04) | adds `AddAzureKeyVaultPushSigningKey(...)` next to 04's card signing key |
| `Ambinder.A2A.AspNetCore` (package 01) | the JWKS endpoint, the default `DataProtectionPushCredentialProtector`, the named `"a2a-push"` HTTP client setup. The core `Ambinder.A2A.Server` must not reference ASP.NET Core (01 rule 2), so these live here. |
| `Ambinder.A2A.Push.Receiver` | the receiver endpoint, verification, task registry, dedupe, and the implementation of `Ambinder.A2A.Client`'s `IA2APushWaiter` (B10). It depends on `Ambinder.A2A.Client` and ASP.NET Core, not on `Ambinder.A2A.Server`. |

`PushNotificationSender`, `PushSigningKey`, `PushWorkItem` and `WebhookUrlValidator` become internal (R6). `PushNotificationSender` is replaced by `PushDeliveryService`.

### API rules (package 02)

- `AddA2A...`, `Add...Entra...` and other registration extension methods live in `namespace Microsoft.Extensions.DependencyInjection`. `MapA2A...` methods live in `Microsoft.AspNetCore.Builder`.
- Public interfaces in this package stay small. After 1.0, any new member on them gets a default implementation, so existing implementations keep compiling.

### Delivery pipeline

1. **Commit.** Push registers a `PushOutboxContributor : ITaskWriteContributor` (package 05, §7.6). For each committed event, the core calls it before the store write. It reads the task's push configs (`IPushConfigStore.GetAllAsync`) and adds one `OutboxEntry` per config through `TaskWriteContext.AddOutbox`. *(writer's call: one entry per config, so each webhook retries and orders on its own.)*
   - Package 05 writes the task and its outbox rows in one transaction or batch in every durable store (05 Goal 9). That gives true at-least-once from the commit on.
   - The body is built once per (event, payload format) at this point by an `IPushPayloadFormatter` (below), and stored in the entry. Every retry sends the same bytes, which the signature and the receiver dedupe depend on.
   - Configs are read once per event inside the write. The store keeps them in memory or in an indexed table, so no network call happens beyond the store itself.
   - This replaces the post-save `ITaskEventObserver` path for push (`src/A2A.Server/Tasks/ITaskEventObserver.cs`). Observers stay for in-process notifications.
2. **Wake.** The local `PushDeliveryService` is signalled for low latency. Other pods find the entry on their next poll.
3. **Claim.** `PushDeliveryService` (a hosted `BackgroundService`) claims due entries in batches with `IOutboxStore.ClaimDueAsync`. For each (task, config) lane it delivers only the lowest pending `Sequence`, which gives per-config order (see the lane requirement under Dependencies).
4. **Deliver.** For each claimed entry, the service:
   - loads the config (a deleted config → complete the entry and record outcome `canceled`)
   - unprotects the credentials
   - builds the request (headers below)
   - sends it with a per-attempt timeout
5. **Settle.** The outcome decides what happens next:
   - 2xx → complete.
   - Retryable failure (network error, timeout, 408, 429, 5xx) → reschedule with backoff and record the error class (never the response body).
   - Other 4xx → dead-letter at once, with the status as the reason.
   - Retry budget used up → dead-letter.
   - Dead-lettering an entry releases the next sequence for that config. *(writer's call: one bad event must not block the rest. The receiver sees a gap, and the dead letter records it.)*
6. **Final event.** When the entry for a terminal event completes or dead-letters, the service deletes that config (`DeleteConfigsAfterFinalDelivery = true` by default). This is today's behavior (`src/A2A.Server/Push/PushNotificationSender.cs:146-147`), now per config.

Store contract used (package 05 §7.6 `OutboxEntry` and `IOutboxStore`):
- `Kind = "push"`. `Task` and `Sequence` come from the write. `Id` is a GUID that stays the same across retries (it becomes the signature `jti`).
- `Payload` is opaque to the store. Push writes a small envelope into it: `{ v: 1, configId, isFinal, contentType, body }`, where `body` holds the formatter's output bytes. *(writer's call: config id and the final flag travel in the payload, so the store needs no push fields.)*
- `ClaimDueAsync`, `CompleteAsync` (deletes the row), `RescheduleAsync` (next attempt, short error class, never a body or secret) and `DeadLetterAsync` (reason) are used as 05 defines them. Claims (`ClaimedBy`, `ClaimExpiresAt`) stop a slow pod from completing a row another pod has re-claimed, and make multi-instance delivery safe without package 06.
- `ListDeadLettersAsync` backs `IPushDeadLetters.ListAsync`.
- Three more calls are needed from 05 (see Dependencies): requeue of a dead-lettered row, purge of old dead letters, and pending stats (count and oldest `CreatedAt`) for health and metrics.
- The in-memory store implements the same contract. It has a capacity (`PushOptions.InMemoryOutboxCapacity`, default 100,000). When it is full, a new entry is stored straight as dead-lettered with reason `outbox_full`, counted as `dropped`, and logged as an error. *(writer's call: blocking the task writer could deadlock, and silent drops are an anti-goal.)*

### Payload formatter

```csharp
public interface IPushPayloadFormatter
{
    /// Protocol versions this formatter writes bodies for, for example "1.0".
    bool CanFormat(string protocolVersion);
    /// Body bytes and content type for one event and one config. Runs inside the task write; must be fast and must not do I/O.
    PushPayload Format(StoredTask task, StreamResponse evt, TaskPushNotificationConfig config);
}
public readonly record struct PushPayload(ReadOnlyMemory<byte> Body, string ContentType);
```

- The default formatter writes the `StreamResponse` JSON (source-generated context) with `application/a2a+json`. It serializes once per event and shares the bytes across configs.
- Each stored push config carries an opaque `ProtocolVersion` string (`StoredPushConfig.ProtocolVersion`, package 05; null means `"1.0"`). The core fills it from the call's `A2ACallContext.ProtocolVersion` when the config is created, so configs created through 1.0 APIs get `"1.0"` and configs created through package 15's 0.3 methods get `"0.3"`. Package 15 registers a formatter with `CanFormat("0.3")` that writes the 0.3 body.
- The formatter is picked by the config's `ProtocolVersion`. No formatter for a version → the config is refused at create time with `InvalidParams`, so delivery never meets an unknown version.

### Options (`PushOptions`, inside `A2AServerOptions.Push`; config `A2A:Push`)

| Option | Default | Notes |
|---|---|---|
| `Timeout` | 15 s | Per attempt. Validated to 1–120 s. The docs recommend 10–30 s (spec). |
| `InitialBackoff` | 1 s | Exponential with full jitter. |
| `MaxBackoff` | 5 min | |
| `MaxAttempts` | 10 | Including the first attempt. *(writer's call)* |
| `MaxDeliveryAge` | 24 h | Dead-letter once an entry is older than this, even if attempts remain. |
| `HonorRetryAfter` | true | For 429 and 503, capped at `MaxBackoff`. |
| `PollInterval` | 1 s | Dispatcher poll when it has not been woken. |
| `ClaimDuration` | 60 s | The `claimFor` passed to `ClaimDueAsync`. Must exceed `Timeout` × 2 (startup check). |
| `BatchSize` | 64 | Entries per claim call. |
| `MaxConcurrentDeliveries` | 32 | Per instance. |
| `MaxConfigsPerTask` | 10 | More → `InvalidParams` (R15). The only definition of this limit; package 05's core enforces it with `CountAsync`. |
| `MaxUrlLength` | 2,048 | |
| `DeleteConfigsAfterFinalDelivery` | true | |
| `DeadLetterRetention` | 7 days | Purged by the dispatcher (`PurgeDeadLettersAsync`). Delivered rows are deleted at once by `CompleteAsync` (package 05). |
| `InMemoryOutboxCapacity` | 100,000 | In-memory store only. |
| `AllowPrivateNetworks` | false | Development only, as today (`src/A2A.Server/Push/PushOptions.cs:15-16`). |
| `AllowedNetworks` | `[]` | CIDR ranges that are allowed even though they are private, for example in-cluster receivers on AKS. *(writer's call)* |
| `AllowedHosts` | `[]` | If not empty, webhook hosts must match one entry (exact, or `*.suffix`). |
| `Proxy` | null | An absolute proxy URI. Null → no proxy (today's behavior). |
| `UseSystemProxy` | false | Use `HTTP(S)_PROXY` and the system settings. |
| `ProxyEnforcesEgressPolicy` | false | Must be `true` when a proxy is on outside Development (startup check). |
| `Signature.Enabled` | `null` (auto) | The signature extension (below). Auto: on when a signing key source is configured, or in Development (ephemeral key); off otherwise. `true` or `false` forces it. *(Claude's call, C9)* |
| `Signature.TokenLifetime` | 5 min | |
| `Signature.Algorithm` | from the key | RS256 (RSA ≥ 2048) or ES256 (P-256). |
| `ShutdownTimeout` | 10 s | Time in-flight deliveries get on host stop. Unfinished claims expire and are picked up later. |

`PushOptions.Issuer` (`src/A2A.Server/Push/PushOptions.cs:18-19`) stays internal and is set from the public base URL, as today.

### Request shape

Headers sent on every delivery:
- `Content-Type` from the payload formatter (`application/a2a+json` for 1.0 bodies).
- `Authorization: {scheme} {credentials}`, from `config.authentication`, as today (`src/A2A.Server/Push/PushNotificationSender.cs:229-230`).
  - When a config is created, `scheme` must be an RFC 9110 token (1–64 chars), and `credentials` must not contain CR, LF or other control characters. Otherwise `InvalidParams`. This blocks header injection.
  - If `credentials` is empty, the scheme alone is sent, as today.
- The notification token header with `config.token`, when it is set. Today this is `X-A2A-Notification-Token` (`src/A2A.Protocol/A2AConstants.cs:17`). The builder checks the v1.0.1 spec text and the Python and JS SDKs for the header name. If the spec names a header, use it. If not, keep this name and say so in the "spec choices" doc (package 03).
- `traceparent` from the delivery span, as today.
- With the signature extension on: `X-A2A-Push-Signature` (below).

The body is the stored `Payload` bytes, sent unchanged on every attempt. The signature and the receiver's dedupe depend on that.

Seam for computed credentials *(writer's call)*:

```csharp
public interface IPushRequestAuthenticator
{
    /// Called after the standard headers are set. May add or replace headers for this config. Default: no-op.
    ValueTask AuthenticateAsync(HttpRequestMessage request, TaskPushNotificationConfig config, CancellationToken ct);
}
```

### Credentials at rest

```csharp
public interface IPushCredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
```

- Default (`Ambinder.A2A.AspNetCore`): ASP.NET Core Data Protection, with purpose `Ambinder.A2A.Push.Credentials`. Without ASP.NET Core, the core default is a pass-through protector that logs a startup warning outside Development. Package 05's durable push-config stores call it for `authentication.credentials` and `token` before they write. *(writer's call)*
- The docs tell teams that Data Protection keys must be shared across pods. On AKS that means Blob storage plus Key Vault, the standard ASP.NET Core setup. Not needed in M1 (single replica). In M2, package 09's `overlays/cluster/` configures the shared key ring (Azure Blob Storage, protected with a Key Vault key), see 09 §7.10.
- Outbox rows never contain credentials. They are read from the config at delivery time.

### Signature extension (`X-A2A-Push-Signature`)

Decision *(writer's call)*: keep the signature, but present it as an A2A extension, not as the protocol.
- The spec-standard credentials are always sent. The signature is extra.
- It is declared on the card in `capabilities.extensions`:

  ```json
  { "uri": "https://github.com/roman-ambinder/a2a-starter/extensions/push-signature/v1", "required": false,
    "description": "Webhook deliveries carry a JWS over the body; verify with the JWKS below.",
    "params": { "jwksUrl": "https://agents.contoso.com/.well-known/a2a-push-jwks.json", "header": "X-A2A-Push-Signature" } }
  ```

  The URI is `https://github.com/roman-ambinder/a2a-starter/extensions/push-signature/v1`. Package 01 owns it (it follows owner decision O1 on the repo location). The extension spec page (package 13) is versioned `v1`.
- Receivers find the JWKS from the card's extension params. A receiver must not take the JWKS location from the token itself.
- JWT shape (compact JWS):
  - Header: `alg` (RS256 or ES256), `kid`, `typ: "JWT"`.
  - Claims: `iss` (server public base URL), `aud` (the exact webhook URL), `iat`, `exp` (iat + `TokenLifetime`), `jti` (the outbox entry id, the same on every retry of that delivery), `task_id`, `body_sha256` (base64url SHA-256 of the exact body bytes).
  - This adds `jti` to the current claims (`src/A2A.Server/Push/PushSigningKey.cs:48-65`).
- The JWKS endpoint stays at `/.well-known/a2a-push-jwks.json` (`src/A2A.Server/Hosting/A2AEndpointRouteBuilderExtensions.cs:22`), for compatibility with the current sample receiver. It sends `Cache-Control: public, max-age=300`.
- The core builds the JWS by hand: base64url header and payload, then `await signer.SignAsync(bytes)`. This lets an HSM or Key Vault sign asynchronously. Today's code goes through the synchronous IdentityModel signing path. *(writer's call: avoids sync-over-async, and removes the JsonWebTokens dependency from the sending path.)*
- When the extension is on *(Claude's call, C9)*:
  - A signing key source is configured (`AddA2APushSigningKeys(...)` or `AddAzureKeyVaultPushSigningKey(...)`): on, in every environment.
  - Development with no key source: on, with the ephemeral key, automatically.
  - Any other environment with no key source: off. Pushes use the spec-standard `authentication` and token only. Startup logs one line: "Push signatures are off: no signing key source is configured. Call AddAzureKeyVaultPushSigningKey(...) or AddA2APushSigningKeys(...) to turn them on." The card does not declare the extension.
- Startup rule: if `Signature.Enabled` is forced to `true` outside `Development` and the only provider is the ephemeral one, startup fails: "Push signatures need a key shared by all replicas. Call AddAzureKeyVaultPushSigningKey(...) or AddA2APushSigningKeys(...), or remove A2A:Push:Signature:Enabled." No silent per-pod keys.

### Signing key source

Push signing reuses package 04's key abstraction, so one key setup (local or Key Vault) serves card signing and push signing. There is no second key type. Push adds only the "which keys to publish" view:

```csharp
public interface IPushSigningKeySource
{
    /// The key to sign with now (package 04's A2ASigningKey). Must be among the published keys.
    ValueTask<A2ASigningKey> GetSigningKeyAsync(CancellationToken ct);
    /// Every public key a receiver may still need: the current key, a pre-published next key, and recently retired keys.
    ValueTask<IReadOnlyList<JsonWebKeyPublic>> GetPublishedKeysAsync(CancellationToken ct);
}
```

- `A2ASigningKey` (`KeyId`, `Algorithm`, `NotAfter`, `PublicJwk`, `SignAsync`) and `JsonWebKeyPublic` come from package 04. Allowed push algorithms: RS256 and ES256.
- `PushSigningKeySource.From(IAgentCardSigningKeySource source)` adapts a card key source. Teams may share one key, but the docs recommend a separate key (or Key Vault key) per purpose. *(writer's call)*

The SDK ships three sources:
1. `EphemeralPushSigningKeySource` (used automatically in Development when no key source is configured, C9). One RSA 2048 key per process, as today. For development and single-instance tests only.
2. Local keys (core, no Azure), built on package 04's local key types: PEM files, `X509Certificate2`, or configuration (`A2A:Push:Signature:Keys[]` with `Path`, `NotBefore`, `NotAfter`; `kid` follows 04's default, the RFC 7638 thumbprint).
   - The current key is the newest key with `NotBefore <= now - PublishLead`.
   - The JWKS holds every key whose `NotAfter + RetireGrace > now`.
   - Files are watched and reloaded on change, which suits Kubernetes secrets.
3. Key Vault (`Ambinder.A2A.Azure.KeyVault`, the package that 04 defines): `AddAzureKeyVaultPushSigningKey(Uri keyId, TokenCredential? credential = null)`, next to 04's `AddAzureKeyVaultCardSigningKey`.
   - It follows 04's Key Vault rules: `kid` = `name/version`; signs with `CryptographyClient.SignAsync` on the digest, so the private key never leaves the vault; disabled or expired versions are never used or published; the docs name the RBAC role.
   - It uses the options of 04's Key Vault source: `RefreshInterval` (default 1 h), `PublishLead` (default 1 h), `RetireGrace` (default 24 h). Package 04 owns them (added there at this package's request).
   - The signing version is the newest enabled version whose `CreatedOn` (or `NotBefore`) is at least `PublishLead` in the past. A freshly rotated key is therefore published before it is used. The JWKS holds enabled versions plus versions retired less than `RetireGrace` ago.
   - Rotation is the Key Vault rotation policy. Nothing in the SDK runs it.
   - The default credential is the same as 04's Key Vault source, which follows package 07's rule: `DefaultAzureCredential` only in `Development`; elsewhere `ChainedTokenCredential(WorkloadIdentityCredential, ManagedIdentityCredential)`.
   - If the first refresh fails, the source throws at first use and the health check reports unhealthy. If a later refresh fails, the last known keys are kept, a warning is logged, and a `a2a.server.push.signing_key.refresh_failures` metric is counted.

Registration: `AddA2APushSigningKeys(config)` (local) or `AddAzureKeyVaultPushSigningKey(...)`. The last registration wins (`Replace`).

`PublishLead` must be at least the JWKS `max-age` plus the receiver cache time. The receiver helper below refreshes the JWKS on an unknown `kid`, so a shorter lead only costs one extra fetch.

### SSRF guard and proxy

The guard runs at three points:
1. **Config create.** This is today's `ValidateAsync` (`src/A2A.Server/Push/WebhookUrlValidator.cs:22-36`), plus:
   - the URL length limit
   - the `AllowedHosts` check
   - `AllowedNetworks` exceptions to the private-address rule
   - refusal of fragments
   - refusal of non-default ports below 1024 other than 443 and 80. *(writer's call)*
2. **Connect time, no proxy.** This is today's `ConnectCallback` re-resolve-and-check (`src/A2A.Server/Push/WebhookUrlValidator.cs:115-139`). It stays the default path.
3. **Proxy mode.** The connection goes to the proxy, so a connect-time check would see the proxy. Instead:
   - Before each attempt, the sender resolves the webhook host and applies the same address rules.
   - Redirects stay off (`AllowAutoRedirect = false`, as today).
   - Cookies are off.
   - Outside Development, startup requires `ProxyEnforcesEgressPolicy = true`, which states that the proxy itself blocks private ranges, because DNS can change between the check and the proxy's own lookup. The docs explain the remaining gap.
   - Proxy credentials come from the proxy URI or `DefaultCredentials` and are never logged.

The `"a2a-push"` named HTTP client keeps `Timeout = Infinite` (the sender sets a per-attempt timeout). It never reads the response body beyond the headers, and it disposes the response at once.

### Dead letters and operations

```csharp
public interface IPushDeadLetters
{
    IAsyncEnumerable<PushDeadLetter> ListAsync(PushDeadLetterFilter filter, CancellationToken ct); // by agent, tenant, task, age
    ValueTask RequeueAsync(string entryId, CancellationToken ct);  // back to Pending, attempts reset
    ValueTask<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken ct);
}
public sealed record PushDeadLetter(string EntryId, string Agent, string? Tenant, string TaskId, string ConfigId,
    long Sequence, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset DeadLetteredAt, string Reason);
```

- Requeue works only while the config still exists. Otherwise it throws `InvalidOperationException` "config was deleted".
- Requeued entries are delivered out of order relative to newer ones. The docs say so.

Telemetry (extends today's span and metric, `docs/production.md` §6):
- The `a2a.push.deliver` span stays, with `a2a.push.attempt` and outcome.
- Outcomes of `a2a.server.push.deliveries` become `delivered`, `retried`, `dead_lettered` and `dropped`. `failed` is renamed to `dead_lettered`.
- New metrics: `a2a.server.push.outbox.pending` (a gauge) and `a2a.server.push.outbox.oldest_age_seconds` (a gauge, from `IOutboxStore.GetPendingStatsAsync`, package 05's `OutboxStats`).
- `IPushOutboxHealth` reports `Degraded` when the oldest pending entry is older than 5 minutes, and `Unhealthy` when the store cannot be read. Package 09's health checks use it.

### Webhook receiver (`Ambinder.A2A.Push.Receiver`)

```csharp
builder.Services.AddA2APushReceiver(o =>
{
    o.WebhookUrl = "https://receiver.contoso.com/a2a/webhook";   // must equal the signature aud
    o.Signature.TrustedAgents.Add(new Uri("https://agents.contoso.com")); // optional
});
app.MapA2APushReceiver("/a2a/webhook", async (A2APushNotification n, CancellationToken ct) =>
{
    Console.WriteLine($"{n.TaskId}: {n.Event.StatusUpdate?.Status.State}");
});
```

Types:
- `A2APushNotification`: `TaskId`, `Event` (`StreamResponse`), `DeliveryId` (the `jti`, or a body hash), `Subscription`, `Verified` (flags for which checks passed), and `RawBody` (`ReadOnlyMemory<byte>`).
- `IA2APushHandler`: the interface form of the handler delegate.
- `IPushSubscriptionRegistry`: the expected tasks and their credentials. The default is in-memory with expiry.
  - `RegisterAsync(PushSubscription)`: task id, or `*` until bound (today's wildcard behavior, `samples/WebhookReceiver/NotificationStore.cs:7-20`); the expected credentials; the token; and `ExpiresAt`.
  - `MatchAsync(credentials, token)`.
  - `BindAsync(subscription, taskId)`.
  - `RemoveAsync`.
- `PushSubscription.CreateNew(taskId?)` (type in `Ambinder.A2A.Client`, B10): makes random 256-bit credentials and a token, and returns the `TaskPushNotificationConfig` fields for the client to send. Teams no longer invent secrets.
- `IPushDeduplicator`: `TryMarkAsync(deliveryKey, ttl)`. The default is in-memory, an LRU of 100,000 keys, 24 h. A durable implementation plugs in.
- `IA2APushWaiter` and `PushSubscription` are defined in `Ambinder.A2A.Client` (package 10), not here *(Claude's call, B10)*. The receiver registers its implementation of `IA2APushWaiter` (`WaitAsync(taskId, Func<StreamResponse,bool> until, ct)`), so package 10's push wait mode uses it in the same process.

Request processing, in this order. Every failure is a 401 with an empty body and a warning log with the reason (never secrets), unless noted:
1. Method POST only. The body limit is `MaxBodyBytes` (default 1 MiB) → 413. The content type must be `application/a2a+json` or `application/json` → 415.
2. Credentials: the `Authorization` value and the token header must both match one registration (fixed-time compare, as in `samples/WebhookReceiver/NotificationStore.cs:34-44`). With `o.RequireCredentials = true` (default), a delivery without them is refused.
3. Signature. The receiver verifies the signature whenever one is present *(Claude's call, C9)*, and requires one when `Signature.Required = true`:
   - The `iss` must be a trusted agent: one in `Signature.TrustedAgents`, or the agent base URL recorded on the matched `PushSubscription` (the client records it when it registers the config). Only then is anything fetched. An untrusted `iss` → 401.
   - The JWKS URL comes from that agent's card extension params, or from `/.well-known/a2a-push-jwks.json` under the trusted base URL. It is cached, and refreshed on an unknown `kid` at most once every 30 s.
   - Then the receiver checks: `alg` is RS256 or ES256; `aud == WebhookUrl`; the lifetime with 30 s skew; the lifetime is at most 10 min; `iat` is not in the future; `body_sha256` matches the exact raw body; `task_id` matches.
   - This moves `samples/WebhookReceiver/PushSignatureValidator.cs` into the package, with ES256 and `jti` added.
   - A present signature that fails any check → 401. If a signature is present but there is no trusted agent at all (no `TrustedAgents` and no agent on the subscription), it cannot be checked: the receiver relies on the credentials and logs a warning once.
4. Parse: the body is deserialized as `StreamResponse` with the source-generated context. It must have exactly one payload. Invalid → 400.
5. Task match: the task id of the payload (`task.id`, or `taskId` of a status update, artifact update or message) must equal the subscription's task (or bind the wildcard). It must also equal the signed `task_id` when there is one. Otherwise 401.
6. Dedupe: the key is the `jti` when the signature was verified, else SHA-256 of (task id + body). A duplicate → 200 `{"status":"duplicate"}` without calling the handler.
7. Handler: success → 200. An exception → 500, so the agent retries, and the dedupe mark is removed so the retry runs the handler again. *(writer's call)*
8. A hook for package 15: `IPushBodyConverter` runs before step 4 when the content looks like a 0.3 body (package 15's `LegacyPushNotification` converter plugs in here). Nothing ships for it in this package.

The receiver adds no `Ambinder.A2A.Server` dependency. It depends on `Ambinder.A2A.Client` for the push-wait abstractions (B10), and uses `Microsoft.IdentityModel.JsonWebTokens` for verification only. The sample `WebhookReceiver` is rewritten to about 30 lines on top of it.

### Migration from current code

- `PushNotificationSender` (in-memory channel and lanes) → `PushDeliveryService` over the outbox. The in-memory store gives the same behavior for local development, plus dead letters.
- `PushSigningKey` → `EphemeralPushSigningKeySource`. `AgentCardEndpoints.PushJwksAsync` (`src/A2A.Server/Hosting/AgentCardEndpoints.cs:23-25`) serves `GetPublishedKeysAsync`.
- `PushNotificationSender` as `ITaskEventObserver` → `PushOutboxContributor` as `ITaskWriteContributor` (package 05).
- `PushServiceCollectionExtensions` (`src/A2A.Server/Push/PushServiceCollectionExtensions.cs:19-32`) keeps the named client and adds the proxy branch.
- The `failed` metric outcome → `dead_lettered`. The docs' telemetry table is updated.
- The sample receiver and its tests move to the package API.

## How it meets the six principles

1. **Protocol.** At-least-once delivery, the configured credentials sent, backoff and timeouts as the spec says, the SSRF guard, and receiver checks of authenticity, 2xx and task id. The signature is a declared extension, not a protocol change. Check: TCK push scenarios pass. Interop (package 16): Python and JS agents deliver to our receiver with standard credentials, and our agent delivers to a receiver built with another SDK.
2. **.NET enabler.** It uses `BackgroundService`, `IHttpClientFactory`, Options, Data Protection, `IHealthCheck`-friendly status, OpenTelemetry metrics, and a minimal-API endpoint helper. Check: XML docs on all public members. Options validation runs at startup.
3. **Easy.** Push works with no configuration in Development. Production works with no extra step (standard credentials); one key registration turns signatures on (C9). A receiver is about 10 lines. Check: a receiver sample at most 15 lines. Each startup error test asserts that the message names the fix.
4. **Performance.** No network I/O under the task lock. The body is serialized once per event and shared across configs. Delivery is batched and bounded. Check: the package 14 benchmark `PushEnqueue` adds under 10 µs to the task writer per event with 3 configs (in-memory store). Delivery throughput is at least 2,000 per second per instance against a local 200-OK sink.
5. **Modular.** The key provider, credential protector, request authenticator, dead letters, dedupe and subscription registry are all interfaces with defaults. Key Vault is a separate package. Check: the package-graph CI check (no Azure in the core).
6. **Integration pains.** Durable delivery, spec-standard auth, a receiver helper, and corporate proxies. Check: the kill-and-restart test, and a receiver interop test with another SDK.

## Dependencies

Needs:
- 05: `ITaskWriteContributor`, `OutboxEntry` and `IOutboxStore` as in 05 §7.6, plus these additions (all now in 05 §7.6, §7.7 and §7.13):
  - a lane key on `OutboxEntry` (string, push sets it to the config id), with `ClaimDueAsync` returning only the lowest-`Sequence` unfinished row per (task, lane). Today 05 orders per task only, which would let one failing webhook block every other config of the task.
  - `RequeueDeadLetterAsync(id)`, `PurgeDeadLettersAsync(olderThan)` and `GetPendingStatsAsync()` (count, oldest `CreatedAt`).
  - an opaque `ProtocolVersion` string on stored push configs (for package 15).
  - push-config stores that call `IPushCredentialProtector` for `authentication.credentials` and `token` (05 leaves encryption to this package).
  - conformance cases for lane ordering, claim takeover, and dead-letter requeue.
- 07: the tenant and owner scoping of push-config operations.
- 09: health check wiring for `IPushOutboxHealth`, graceful-shutdown ordering (stop claiming first), and the Data Protection key setup on AKS.
- 01: package names, including `Ambinder.A2A.Azure.KeyVault` and `Ambinder.A2A.Push.Receiver` (allow-list: Receiver depends on Client, B10), and the extension URI (`https://github.com/roman-ambinder/a2a-starter/extensions/push-signature/v1`).
- 10: `IA2APushWaiter` and `PushSubscription` in `Ambinder.A2A.Client` (B10).

Provides:
- To 10: the receiver's implementation of 10's `IA2APushWaiter`, for the push wait mode. `IA2APushWaiter` and `PushSubscription` themselves live in `Ambinder.A2A.Client` (B10).
- To 04: push becomes the second user of `A2ASigningKey` and the Key Vault package. 04's Key Vault source now has the `PublishLead` and `RetireGrace` behavior push needs.
- To 15: the `IPushPayloadFormatter` seam in the sender, the `ProtocolVersion` field on push configs, and the `IPushBodyConverter` hook in the receiver.
- To 12: an in-memory outbox and a recording webhook sink as test helpers.
- To 13: the push guide, the extension spec page `push-signature/v1`, and the receiver guide.
- To 14: the `PushEnqueue` and `PushDeliveryThroughput` benchmarks.

## Acceptance criteria

- [ ] Kill-and-restart test (SQLite store from package 05): commit events, kill the dispatcher mid-delivery, restart it. Every event is delivered at least once, and the receiver helper deduplicates them to exactly once.
- [ ] Ordering test: a slow config keeps its order, and another config of the same task is not delayed.
- [ ] Retry tests: 5xx, 408, 429 with `Retry-After`, timeouts and network errors are retried with backoff. Other 4xx dead-letter at once. The budget (`MaxAttempts`, `MaxDeliveryAge`) dead-letters. Dead-lettering releases the next entry.
- [ ] Multi-instance test: two dispatchers on one store deliver each entry once in normal operation, and a dispatcher that dies holding a claim is taken over after `ClaimDuration`.
- [ ] In-memory capacity test: when the outbox is full, the entry is dead-lettered with `outbox_full` and the `dropped` metric is counted.
- [ ] Header tests: `Authorization` built from `authentication`, the token header, header injection refused at config create, and `MaxConfigsPerTask` enforced.
- [ ] Signature tests: the JWS verifies with the receiver; `jti` is stable across retries; ES256 and RS256; a rotation with a fake provider (old-key signatures keep verifying during `RetireGrace`, and a new key is published before it is used); the ephemeral provider forced on outside Development fails startup; with no key source outside Development the extension is off, the card does not declare it, deliveries carry standard credentials only, and the startup log says so (C9).
- [ ] Key Vault live test (sign, verify, rotate a version, JWKS contents) exists and runs only when its secrets exist, skipped visibly otherwise; the manual pre-release checklist covers it until then (O3).
- [ ] SSRF tests: the existing cases, plus `AllowedNetworks`, `AllowedHosts`, proxy mode refusing private targets, the startup check for `ProxyEnforcesEgressPolicy`, and no redirect following.
- [ ] Receiver tests: each refusal step in order (413, 415, 401 reasons, 400), wildcard binding, dedupe by `jti` and by hash, a handler exception giving 500 and then a successful redelivery, the JWKS location taken only from trusted agents (or the subscription's agent), and a present but bad signature refused (C9).
- [ ] Interop (package 16): delivery to, and receipt from, at least the Python and JS SDKs using standard credentials only.
- [ ] TCK push scenarios pass on all three transports.
- [ ] Telemetry tests are updated for the new outcomes and gauges. No credentials appear in logs or spans (scrubber assertion).
- [ ] Docs: the push guide (delivery guarantees, ordering, dead letters, proxy and SSRF, key setup for Key Vault and for configured keys), the receiver guide, and the `push-signature/v1` extension page.
- [ ] PD7 closed: package 14's `Push` scenarios (enqueue cost on the event path, now including the outbox write; delivery throughput with 1 and 10 configs per task, with signing) show before and after numbers in the PR. Budgets: as in the Performance principle line above.

## Left to the builder

- Internal dispatcher structure: channels, a semaphore, or `Parallel.ForEachAsync`.
- The jitter formula, and how `Retry-After` dates are parsed.
- The exact error classes recorded in `OutboxEntry.LastError` (package 05).
- File-watch details for the local push key source.
- How the receiver discovers the JWKS URL from the card (fetch the card once per trusted agent and cache it).
- The fluent shape of `AddA2APushReceiver` options and the minimal-API handler overloads.
- Whether the in-memory outbox shares code with package 05's in-memory store or sits beside it.

## Open questions

None. The signature default (Q1) is Claude's call C9 ("Signature extension"). The Key Vault default credential (Q2) was settled in reconciliation: 04 and this spec use 07's rule.
