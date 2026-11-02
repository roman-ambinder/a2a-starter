# A2A.Protocol.Grpc

The gRPC side of A2A v1.0: C# code generated from `proto/a2a.proto` (namespace `Lf.A2a.V1`) and `ProtoMapper`, which converts between the generated types and the records in [A2A.Protocol](../A2A.Protocol/README.md).

The proto is pinned to A2A commit `4735f4876449c6f381825d8a841b31b16c6ad0c4`. See [proto/README.md](../../proto/README.md).

## What to copy

Reference or copy the whole project, together with the `proto/` folder. Both [A2A.Server](../A2A.Server/README.md) and [A2A.Client](../A2A.Client/README.md) need it.
The project sets `IncludeGoogleApiCommonProtos=true` so the `google/api` imports resolve.

## Extension points

None to implement. `ProtoMapper` has `ToProto` / `FromProto` overloads for each model, plus `ToProtoObject(object)` and `FromProtoObject(IMessage)` for dispatch by type.

## Minimal use

```csharp
using A2A.Protocol;
using A2A.Protocol.Grpc;
using Proto = Lf.A2a.V1;   // do not alias it as "Pb": that collides with a global namespace

var message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hello")] };
Proto.Message wire = ProtoMapper.ToProto(message);
Message again = ProtoMapper.FromProto(wire);
```
