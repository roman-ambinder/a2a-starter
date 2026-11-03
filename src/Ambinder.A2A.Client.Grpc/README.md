# Ambinder.A2A.Client.Grpc

The gRPC transport for [Ambinder.A2A.Client](../Ambinder.A2A.Client/README.md). The client core has no gRPC dependency; add this package and pass `GrpcClientTransport.Instance` in `A2AClientOptions.Transports`. A card interface with binding `GRPC` then yields a `GrpcA2AClient`.

## What to copy

Reference or copy the project together with `src/Grpc.Shared` (the `ProtoMapper` source, which [Ambinder.A2A.AspNetCore.Grpc](../Ambinder.A2A.AspNetCore.Grpc/README.md) also compiles) and the repo's `proto/` folder. The generated code and `ProtoMapper` are `internal`. The project sets `IncludeGoogleApiCommonProtos=true` so the `google/api` imports resolve. The proto is pinned to A2A commit `4735f4876449c6f381825d8a841b31b16c6ad0c4`; see [proto/README.md](../../proto/README.md).

## Extension points

| Type | Purpose |
|---|---|
| `GrpcClientTransport.Instance` | The `IA2AClientTransport` for `GRPC`. A card URL as `host:port` uses TLS unless the host is loopback; `A2AClientOptions.GrpcUseTls` overrides that. `http://` / `https://` URLs are used as is. |
| `GrpcA2AClient` | The client itself; protocol version, extensions and credentials travel as call metadata. |

## Minimal use

```csharp
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Grpc;

await using var client = A2AClientFactory.Create(card, new A2AClientOptions
{
    PreferredBinding = "GRPC",
    Transports = [GrpcClientTransport.Instance],
});
```
