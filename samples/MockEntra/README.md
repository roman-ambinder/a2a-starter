# MockEntra

Development-only copy of the Microsoft Entra ID v2.0 endpoints the agent server needs. No real tenant required.

Run (it refuses any environment except Development, so `ASPNETCORE_ENVIRONMENT=Development` is required; Docker Compose sets it):

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project samples/MockEntra
```

| Endpoint | Purpose |
|---|---|
| `GET /{tenant}/v2.0/.well-known/openid-configuration` | OIDC discovery |
| `GET /{tenant}/discovery/v2.0/keys` | JWKS (RSA 2048, generated at startup) |
| `POST /{tenant}/oauth2/v2.0/token` | client-credentials grant (form or Basic auth) |
| `GET /{tenant}/oauth2/v2.0/authorize` | returns 501 |
| `GET /health/live`, `/health/ready` | health |

URLs in the discovery document use `MockEntra:IssuerBaseUrl` (default `http://localhost:5100`), never the request Host.

Test identities live in `appsettings.Development.json`: tenant `a2a00000-0000-0000-0000-000000000001`, scope `api://a2a-demo/.default`, clients `a2a-client`, `a2a-approver` (has `Reports.Approve`), `a2a-noroles`.

```bash
curl -s -d grant_type=client_credentials -d client_id=a2a-approver -d client_secret=dev-approver-secret \
  -d scope=api://a2a-demo/.default \
  http://localhost:5100/a2a00000-0000-0000-0000-000000000001/oauth2/v2.0/token
```

Never deploy this. Tokens are signed with a throwaway key.
