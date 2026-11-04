# Ambinder.A2A.Client.Grpc

The gRPC transport for [Ambinder.A2A.Client](https://www.nuget.org/packages/Ambinder.A2A.Client). The client core has no gRPC dependency; add this package and pass `GrpcClientTransport.Instance` in `A2AClientOptions.Transports`. A card interface with binding `GRPC` then yields a `GrpcA2AClient`. The generated protobuf code (from the vendored A2A v1.0.1 `a2a.proto`) is compiled into this assembly as `internal`.

```
dotnet add package Ambinder.A2A.Client.Grpc
```

| Type | Purpose |
|---|---|
| `GrpcClientTransport.Instance` | The `IA2AClientTransport` for `GRPC`. A card URL as `host:port` uses TLS unless the host is loopback; `A2AClientOptions.GrpcUseTls` overrides that. `http://` and `https://` URLs are used as is. |
| `GrpcA2AClient` | The client itself; protocol version, extensions and credentials travel as call metadata. |

## Example

```csharp
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Grpc;

await using var client = A2AClientFactory.Create(card, new A2AClientOptions
{
    PreferredBinding = "GRPC",
    Transports = [GrpcClientTransport.Instance],
});
```

The server side is [Ambinder.A2A.AspNetCore.Grpc](https://www.nuget.org/packages/Ambinder.A2A.AspNetCore.Grpc).

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
