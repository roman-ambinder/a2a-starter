# Spec decisions

This SDK follows A2A v1.0.1 (proto pinned to tag `v1.0.1`, see [proto/README.md](../proto/README.md)). Where the spec leaves a choice open, this page records the choice and the reason. Section numbers are from the v1.0.1 specification.

| Topic | Spec | Choice | Reason |
|---|---|---|---|
| Patch in `A2A-Version` | 3.6 | `1.0.1` is accepted as `1.0`. Anything that is not `Major.Minor` or `Major.Minor.Patch` is unsupported. | The spec says patch must not be used in negotiation. Rejecting it would break clients that send the full version. |
| Empty `A2A-Version` | 3.6.2 | Treated as 0.3. The server does not serve 0.3, so it answers `VersionNotSupported`, with a message that says to send `A2A-Version: 1.0`. | The spec makes empty mean 0.3. 0.3 support is planned as a separate package. |
| `supportedVersions` in errors | 3.3.2 | `VersionNotSupported` carries ErrorInfo metadata `supportedVersions` = `"1.0"` (comma-separated). | ErrorInfo metadata values are strings. |
| Client interface selection | 3.6, 8.3.2 | The client takes the first card interface (or the preferred binding) whose binding is supported and whose `protocolVersion` Major.Minor is `1.0`. An empty `protocolVersion` is taken as `1.0`. | The field is required, but some cards omit it; skipping them would leave no usable interface. |
| Unknown enum values | 5 | An unknown enum name or number reads as `*_UNSPECIFIED` instead of failing the payload. A known integer is accepted. Output is always the ProtoJSON name. | Same result as a protobuf parser that ignores unknown values; a newer peer must not break an older reader. The server rejects `ROLE_UNSPECIFIED` on a message ("use ROLE_USER"), so 0.3 values like `"user"` still fail with a clear error. An unknown `ListTasks` status filter means "no filter", as in proto3. |
| JSON-RPC `error.data` | 9.5 | An array of `@type`-tagged details. A2A errors carry one `google.rpc.ErrorInfo` (reason, domain `a2a-protocol.org`, metadata). Standard JSON-RPC errors (-32700 to -32603) carry no `data`. | 9.5 defines `data` as a detail array; 3.3.2 requires ErrorInfo for A2A errors. |
| Client error mapping | 3.3.2 | On every transport the ErrorInfo reason selects the error code; ErrorInfo metadata becomes `A2AException.Data` (a JSON object of strings). A JSON-RPC `data` that is not a detail array is kept as is. | One error model on all transports; older servers still work. |
| Extended card not offered | 3.3.4 | `capabilities.extendedAgentCard` not true: `UnsupportedOperation`. Declared but not configured: `ExtendedAgentCardNotConfigured`. | Exactly as 3.3.4 says. |
| Required extensions | 3.3.4, 4.6 | Checked on every operation of the agent (not only SendMessage), on all transports. The card endpoint is not an operation and is not checked. | 3.3.4 does not limit the rule to one operation. |
| Extension echo | 4.6 | Activated extensions (requested and declared on the card) are sent back in `A2A-Extensions` (HTTP) or `a2a-extensions` response metadata (gRPC). No header when none is activated. | Lets the client see what the agent applied. |
| `contextId` limits | 3.4.1 | A client `contextId` is kept as given. It is rejected (`InvalidParams`) when it is longer than 256 characters or has control characters. It is never replaced. | The spec lets the server reject ids it cannot accept, and forbids silently replacing them. |
| Tenant | 8.3.2 | The client sends the selected interface's `tenant` on every request. A request with a different non-empty tenant throws `ArgumentException` before anything is sent. Without an interface tenant the request value is sent as given. | The spec says the request tenant must equal the interface tenant. |
| gRPC interface URL | `AgentInterface.url` | The server writes `host:port` for gRPC interfaces. The client accepts `host:port` (TLS unless the host is loopback; `A2AClientOptions.GrpcUseTls` overrides) and full `http://` / `https://` URLs. | The spec gives `hostname:port` for gRPC, and gRPC clients in other SDKs and the TCK use the value as a channel target. |
| Card caching | 8.6.1 | Card responses send `Cache-Control: public, max-age=300` (`A2AServerOptions.CardCacheMaxAge`; zero sends `no-cache`), `Vary: A2A-Version`, and a strong `ETag` (base64url of the first 16 bytes of SHA-256 of the card JSON). `If-None-Match` gets 304. The REST extended card sends `Cache-Control: private, no-store`. | The hash is the same on every instance, so a load balancer does not break conditional requests. The extended card is per caller. |
| Deprecated OAuth flows | proto `OAuthFlows` | `implicit` and `password` flows are modeled and round-trip through JSON and proto. The client never uses them to get tokens. | Cards from other agents must not lose data. |
| JSON-RPC batch | 9 | Rejected with `InvalidRequest`. | JSON-RPC batches are not part of A2A. |
| SSE resume | 3.5.2 | Not supported (`Last-Event-ID` is ignored). | Not defined by the spec (open issue #1635). |
| When streams close | 3.1.2, 3.1.6, 11.7 | A `SendStreamingMessage` stream closes at a terminal or interrupted state (`INPUT_REQUIRED`, `AUTH_REQUIRED`). A `SubscribeToTask` stream stays open through interrupted states and closes only at a terminal state. | 11.7 closes streams at interrupted states, but 3.1.6 allows subscribing to any non-terminal task and ends that stream at a terminal state. Closing at once would make subscribing to a waiting task useless. |

## Checking conformance

`tests/A2A.Tck.Sut` is a small server for the official [A2A TCK](https://github.com/a2aproject/a2a-tck). It serves all three transports and the behaviors in the TCK's `scenarios/` files.

1. Start it: `dotnet run --project tests/A2A.Tck.Sut -- --port 9999 --grpc-port 10000`
2. In an `a2aproject/a2a-tck` checkout: `uv venv && uv pip install -e .`, then `./run_tck.py --sut-host http://localhost:9999 --level should`

The TCK reads the card at `{sut-host}/.well-known/agent-card.json` and tests every interface on it.
