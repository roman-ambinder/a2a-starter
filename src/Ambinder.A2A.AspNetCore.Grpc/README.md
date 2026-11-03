# Ambinder.A2A.AspNetCore.Grpc

The A2A gRPC server endpoint. Call `AddA2AGrpc()` on the builder returned by `AddA2AServer(...)` and `MapA2AGrpc()` on the app. The agent is picked by the local port, from `A2AServerOptions.GrpcPorts`.

The generated protobuf code (from `proto/a2a.proto`) and `ProtoMapper` (from `src/Grpc.Shared`) are compiled into this assembly as `internal`. See [Ambinder.A2A.Server](../Ambinder.A2A.Server/README.md) for the host example.
