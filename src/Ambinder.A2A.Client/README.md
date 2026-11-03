# Ambinder.A2A.Client

Calls A2A v1.0 agents. One interface, `IA2AClient`, with the same operations over JSON-RPC and REST. `A2AClientFactory` reads the agent card and picks the transport. It has no gRPC or Azure dependency: add [Ambinder.A2A.Client.Grpc](https://www.nuget.org/packages/Ambinder.A2A.Client.Grpc) for gRPC and [Ambinder.A2A.Client.Entra](https://www.nuget.org/packages/Ambinder.A2A.Client.Entra) for Azure.Identity credentials.

```
dotnet add package Ambinder.A2A.Client
```

## Extension points

| Type | Purpose |
|---|---|
| `IA2AClient` | Send, stream, get, list, cancel, subscribe, push config create/get/list/delete, extended card. Mock it in your tests. |
| `A2AClientFactory.Create(card, options)` | Builds the client for the first supported card interface, or `A2AClientOptions.PreferredBinding`. A card whose only interface is `GRPC` throws `NotSupportedException` unless a gRPC transport is in `A2AClientOptions.Transports`. |
| `IA2AClientTransport` | One more protocol binding for the factory. |
| `IA2ACredentialProvider` | Returns the auth headers for a request. Built in: `ClientCredentialsProvider`, `ApiKeyCredentialProvider`, `BearerTokenCredentialProvider`. |
| `A2ACardResolver` | Fetches and caches (5 minutes) the agent card. |

`ClientCredentialsProvider` sends its secret to the token URL, so the card cannot choose it: use `tokenUrlOverride`, or `allowedTokenHosts` (https only, http for loopback). Server errors arrive as `A2AException`; HTTP 401 and 403 as `A2AAuthException`. Telemetry uses only `System.Diagnostics` (activity source `Ambinder.A2A.Client`).

## Example

```csharp
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;

using var http = new HttpClient();
var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri("http://localhost:5200"), ct);

await using var client = A2AClientFactory.Create(card, new A2AClientOptions
{
    HttpClient = http,
    PreferredBinding = A2AConstants.BindingRest,
});

var request = new SendMessageRequest
{
    Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("world")] },
};
var response = await client.SendMessageAsync(request, ct);
Console.WriteLine(response.Message?.Parts[0].Text ?? response.Task?.Status.State.ToString());
```

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
