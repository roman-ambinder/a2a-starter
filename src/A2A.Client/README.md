# A2A.Client

Calls A2A v1.0 agents. One interface, `IA2AClient`, with the same 11 operations over JSON-RPC, REST and gRPC. `A2AClientFactory` reads the agent card and picks the transport.

## What to copy

Reference or copy the project (plus [A2A.Protocol](../A2A.Protocol/README.md) and [A2A.Protocol.Grpc](../A2A.Protocol.Grpc/README.md)). `samples/AgentClient/FlowRunner*.cs` shows every operation.

## Extension points

| Type | Purpose |
|---|---|
| `IA2AClient` | The operations: send, stream, get, list, cancel, subscribe, push config create/get/list/delete, extended card. Mock it in your tests. |
| `A2AClientFactory.Create(card, options)` | Builds the client for the first supported card interface, or the one in `A2AClientOptions.PreferredBinding` (`JSONRPC`, `HTTP+JSON`, `GRPC`). |
| `IA2ACredentialProvider` | Returns the auth headers for a request. Built in: `ClientCredentialsProvider` (OAuth2 client credentials, reads the token URL from the card or an override), `AzureIdentityCredentialProvider` (Azure.Identity `TokenCredential`), `ApiKeyCredentialProvider`, `BearerTokenCredentialProvider`. |
| `A2ACardResolver` | Fetches and caches (5 minutes) the agent card. |

Writing your own provider:

```csharp
public sealed class FixedHeaderProvider(string token) : IA2ACredentialProvider
{
    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
        => new(new Dictionary<string, string> { ["Authorization"] = "Bearer " + token });
}
```

## Minimal use

```csharp
using A2A.Client;
using A2A.Protocol;

using var http = new HttpClient();
var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri("http://localhost:5200"), ct);

await using var client = A2AClientFactory.Create(card, new A2AClientOptions
{
    HttpClient = http,
    PreferredBinding = A2AConstants.BindingRest,
    // Credentials = new ApiKeyCredentialProvider("dev-api-key"),   // only for cards that declare an API key scheme
});

var request = new SendMessageRequest
{
    Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("world")] },
};
var response = await client.SendMessageAsync(request, ct);     // blocking: waits for a terminal or interrupted state
Console.WriteLine(response.Message?.Parts[0].Text ?? response.Task?.Status.State.ToString());

await foreach (var e in client.SendStreamingMessageAsync(request, ct))   // needs capabilities.streaming on the card
    Console.WriteLine(e.StatusUpdate?.Status.State);
```

Notes:

- The snippet above talks to the open `hello` agent from the [A2A.Server README](../A2A.Server/README.md). The root card of `samples/AgentServer` is the `report` agent and needs credentials.
- `ApiKeyCredentialProvider` throws if the card does not declare an API key scheme. Use `BearerTokenCredentialProvider` or `ClientCredentialsProvider` for bearer-only cards.
- Server errors arrive as `A2AException` (with `Code`); HTTP 401 and 403 arrive as `A2AAuthException`.
- Set `A2AClientOptions.Extensions` to send `A2A-Extensions`.
