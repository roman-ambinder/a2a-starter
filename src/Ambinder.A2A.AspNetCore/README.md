# Ambinder.A2A.AspNetCore

ASP.NET Core hosting for the A2A server in [Ambinder.A2A.Server](https://www.nuget.org/packages/Ambinder.A2A.Server): JSON-RPC, REST (HTTP+JSON) and SSE endpoints, the agent card endpoints, `MapA2A()`, `ConfigureA2AKestrel()` and `AddA2AServer(...)`, which calls the core's `AddA2AServerCore(...)` and adds authorization, `AgentAuthorizer` and the startup check. Your host app owns authentication; this library applies an ASP.NET Core policy name per agent.

```
dotnet add package Ambinder.A2A.AspNetCore
```

gRPC is opt-in: add [Ambinder.A2A.AspNetCore.Grpc](https://www.nuget.org/packages/Ambinder.A2A.AspNetCore.Grpc).

## Example

```csharp
using Ambinder.A2A.AspNetCore.Hosting;   // AddA2AServer, MapA2A
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddA2AServer(o =>
{
    o.PublicBaseUrl = "http://localhost:5200";
    o.DefaultAgent = "hello";            // served at /.well-known/agent-card.json
}).AddAgent<HelloAgent>("hello", _ => new AgentCard
{
    Name = "hello",
    Description = "Says hello.",
    Version = "1.0.0",
    Capabilities = new AgentCapabilities { Streaming = true },
    Skills = [new AgentSkill { Id = "hello", Name = "Hello", Description = "Greets." }],
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
});

var app = builder.Build();
app.MapA2A();      // cards, JSON-RPC, REST, push JWKS
app.Run();
```

`HelloAgent` is an `IAgentHandler`, see [Ambinder.A2A.Server](https://www.nuget.org/packages/Ambinder.A2A.Server). Every request needs the `A2A-Version: 1.0` header (cards are exempt). Pass an ASP.NET Core policy name to `AddAgent` to protect an agent, and call `app.UseAuthentication()`.

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
