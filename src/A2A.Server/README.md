# A2A.Server

Hosts A2A v1.0 agents in ASP.NET Core over JSON-RPC, REST (HTTP+JSON) and gRPC. It gives you the task manager, task event streaming, cancel, resubscribe, push notification delivery (signed JWT) and the agent card endpoints. You write the agents.

## What to copy

Reference or copy the project (plus [A2A.Protocol](../A2A.Protocol/README.md) and [A2A.Protocol.Grpc](../A2A.Protocol.Grpc/README.md)). Your host app owns authentication and authorization; this library only applies an ASP.NET Core policy name per agent.
`samples/AgentServer/AgentServerApp.cs` is the full wiring example.

## Extension points

| Interface | Purpose | Default |
|---|---|---|
| `IAgentHandler` | Your agent logic. One new instance per execution (and per cancel). | none, you write it |
| `ITaskStore` | Stores tasks (`GetAsync`, `SaveAsync`, `ListAsync`). | `InMemoryTaskStore` |
| `IPushConfigStore` | Stores push notification configs. | `InMemoryPushConfigStore` |
| `IAgentAuthorizer` | Checks the agent's policy before an operation. | `AgentAuthorizer` (ASP.NET Core policies) |
| `ITaskEventObserver` | Sees every task event (the push sender is one). | push sender |

Replace a store by registering your type before `AddA2AServer`; the library uses `TryAdd`.

## Minimal use

An agent:

```csharp
using A2A.Protocol;
using A2A.Server.Agents;

public sealed class HelloAgent : IAgentHandler
{
    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var text = context.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "";
        return sink.ReplyAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            ContextId = context.ContextId,
            Parts = [Part.FromText("hello, " + text)],
        }, ct);
    }
}
```

For a task instead of a direct reply, call `sink.UpdateStatusAsync(TaskState.Working, null, ct)`, then `sink.AddArtifactAsync(artifact, append: false, lastChunk: true, ct)`, then `sink.UpdateStatusAsync(TaskState.Completed, null, ct)`. A terminal or interrupted state (`INPUT_REQUIRED`, `AUTH_REQUIRED`) ends the execution.

The host:

```csharp
using A2A.Protocol;
using A2A.Server.Hosting;
using A2A.Server.Push;
using A2A.Server.Tasks;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureA2AKestrel();   // HTTP port + one h2c port per gRPC entry

// Optional: durable stores. Register BEFORE AddA2AServer.
// builder.Services.AddSingleton<ITaskStore, MyTaskStore>();
// builder.Services.AddSingleton<IPushConfigStore, MyPushStore>();

builder.Services.AddA2AServer(o =>
{
    o.PublicBaseUrl = "http://localhost:5200";
    o.DefaultAgent = "hello";            // served at /.well-known/agent-card.json
    o.GrpcPorts["hello"] = 5201;
}).AddAgent<HelloAgent>("hello", _ => new AgentCard
{
    Name = "hello",
    Description = "Says hello.",
    Version = "1.0.0",
    Capabilities = new AgentCapabilities { Streaming = true },   // clients are refused streaming otherwise
    Skills = [new AgentSkill { Id = "hello", Name = "Hello", Description = "Greets." }],
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
});

var app = builder.Build();
// app.UseAuthentication();   // needed when an agent has an authorization policy
app.MapA2A();                 // cards, JSON-RPC, REST, push JWKS
app.MapA2AGrpc();             // gRPC
app.Run();
```

Notes:

- `supportedInterfaces` on the cards is generated for you (JSON-RPC, REST, and gRPC when the agent is in `GrpcPorts`).
- `AddAgent(name, publicCard, extendedCard, authorizationPolicy)`: the policy is an ASP.NET Core policy name; `null` means open. To serve an extended card, pass a factory and set `Capabilities.ExtendedAgentCard = true` on the public card.
- Every request needs the `A2A-Version: 1.0` header. Cards are exempt.
- The task owner comes from `CallerIdentity.GetOwner(user)`. Callers only see their own tasks (per tenant for bearer tokens, per key for API keys).
