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
| `IOwnerResolver` | Decides who owns a task (see the notes below). | `DefaultOwnerResolver` |
| `ITaskEventObserver` | Sees every task event (the push sender is one). | push sender |
| `IA2ARequestHandler` | The A2A operations that all three transports call. | `TracingRequestHandler` wrapping `TaskManager` |

Replace a store by registering your type before `AddA2AServer`; the library uses `TryAdd`.

## Telemetry

The default `IA2ARequestHandler` is `TracingRequestHandler` (`Telemetry/`). It records one `a2a.server <Method>` span per operation, the same for all three transports (`A2ACallContext.Transport` carries the transport name). The task manager adds the `a2a.agent.run` span and task state events, and the push sender adds an `a2a.push.deliver` span. The library uses only `System.Diagnostics`: add the `A2A.Server` activity source and meter (`A2ATelemetryNames`) to your own exporter. Names, attributes, metrics and what is never recorded: [docs/production.md](../../docs/production.md#6-observability-otlp).
If you register your own `IA2ARequestHandler`, wrap it in `TracingRequestHandler` to keep the spans.

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
- The task owner comes from `IOwnerResolver` (default `DefaultOwnerResolver`): `tenant:{tid}/app:{azp}` for bearer tokens (one client app in one tenant), `apikey:{name}` for API keys, `user:{sub}` for bearer tokens without a tenant, `anonymous` when not authenticated. Callers only see, list, cancel, subscribe to and configure push for their own tasks; other tasks are `TaskNotFound`. One exception: another app of the same tenant may continue an `AUTH_REQUIRED` task (step-up). Register your own `IOwnerResolver` before `AddA2AServer` to change this.
- REST also accepts the tenant-prefixed paths from the proto (`/{tenant}/message:send`, `/{tenant}/tasks/{id}`, ...). The path tenant becomes the request's `tenant`; a different `tenant` in the body is `InvalidParams`.
