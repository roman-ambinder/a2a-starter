# Ambinder.A2A.AspNetCore

ASP.NET Core hosting for the A2A server in [Ambinder.A2A.Server](../Ambinder.A2A.Server/README.md): JSON-RPC, REST and SSE endpoints, agent card endpoints, `MapA2A()`, `ConfigureA2AKestrel()`, and `AddA2AServer(...)`, which calls the core's `AddA2AServerCore(...)` and adds authorization, `AgentAuthorizer` and the startup check.

gRPC is opt-in: see [Ambinder.A2A.AspNetCore.Grpc](../Ambinder.A2A.AspNetCore.Grpc/README.md). The full host example is in the Server README.
