# Ambinder.A2A.AspNetCore.Grpc

The A2A gRPC server endpoint, as an opt-in addition to [Ambinder.A2A.AspNetCore](https://www.nuget.org/packages/Ambinder.A2A.AspNetCore). Call `AddA2AGrpc()` on the builder returned by `AddA2AServer(...)` and `MapA2AGrpc()` on the app. The agent is picked by the local port, from `A2AServerOptions.GrpcPorts`; `ConfigureA2AKestrel()` opens one h2c port per entry. The generated protobuf code (from the vendored A2A v1.0.1 `a2a.proto`) is compiled into this assembly as `internal`.

```
dotnet add package Ambinder.A2A.AspNetCore.Grpc
```

## Example

```csharp
using Ambinder.A2A.AspNetCore.Grpc;      // AddA2AGrpc, MapA2AGrpc
using Ambinder.A2A.AspNetCore.Hosting;   // AddA2AServer, ConfigureA2AKestrel, MapA2A

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureA2AKestrel();   // HTTP port + one h2c port per gRPC entry

builder.Services.AddA2AServer(o =>
{
    o.PublicBaseUrl = "http://localhost:5200";
    o.GrpcPorts["hello"] = 5201;
}).AddA2AGrpc()
  .AddAgent<HelloAgent>("hello", _ => new AgentCard { Name = "hello", Description = "Says hello.", Version = "1.0.0" });

var app = builder.Build();
app.MapA2A();
app.MapA2AGrpc();
app.Run();
```

The generated `supportedInterfaces` of the card then include a `GRPC` interface for `hello`. Clients call it with [Ambinder.A2A.Client.Grpc](https://www.nuget.org/packages/Ambinder.A2A.Client.Grpc).

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
