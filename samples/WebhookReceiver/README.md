# WebhookReceiver

A webhook receiver that verifies A2A push notifications before it keeps them. Demo only: everything is in memory.

```bash
dotnet run --project samples/WebhookReceiver        # http://localhost:5300
```

## Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/subscriptions` | Body `{"taskId","token","credentials"}`. Tells the receiver what to expect (`credentials` = the Bearer value you gave the A2A server). `taskId` `"*"` is for when you do not know the task id before the first send: the first accepted push binds the subscription to that push's task, and later pushes for another task with the same credentials and token get 401. Returns 201. |
| `POST /webhook` | Where the A2A server delivers. 200 `stored` or `duplicate`; 401 on any failed check (nothing stored); 413 over 1 MB. |
| `GET /api/notifications?taskId=` | JSON array of the received StreamResponse objects, in arrival order. |
| `GET /health/live`, `/health/ready` | Health. |

## Checks on `POST /webhook` (any failure = 401)

1. `Authorization: Bearer <credentials>` and `X-A2A-Notification-Token` match one subscription (fixed-time comparison).
2. `X-A2A-Push-Signature` JWT: `iss` is in `Receiver:TrustedServers`; key from `{iss}/.well-known/a2a-push-jwks.json` (cached, refreshed on an unknown `kid`, at most once per 30 s); RS256; `aud` = `{PublicUrl}/webhook`; not expired (30 s skew); `body_sha256` equals the SHA-256 (base64url) of the exact raw body.
3. The task id of the payload equals the signed `task_id` claim and the subscription's task (or, for `"*"`, the task it is bound to).
4. `iat` is not in the future (30 s skew) and `exp - iat` is at most 10 minutes.

Same body twice (same SHA-256) is stored once. Credentials, tokens and signatures are never logged.

`/api/subscriptions` and `/api/notifications` are unauthenticated demo endpoints, and the in-memory store is unbounded.

## Configuration

| Key | Default |
|---|---|
| `Receiver:TrustedServers` (list) | `http://localhost:5200` |
| `Receiver:PublicUrl` | `http://localhost:5300` |
| `urls` / `ASPNETCORE_URLS` | `http://localhost:5300` |

The server's push sender must be allowed to reach this host: in Development the AgentServer sets `A2A:Push:AllowPrivateNetworks=true`.

## Try it

Create a push config on the report agent with `authentication` `{"scheme":"Bearer","credentials":"<c>"}` and `token` `<t>`, URL `http://localhost:5300/webhook`, after registering `{"taskId":"*","token":"<t>","credentials":"<c>"}` here.
