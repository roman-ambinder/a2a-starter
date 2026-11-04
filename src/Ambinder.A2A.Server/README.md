# Ambinder.A2A.Server

The host-neutral core of an A2A v1.0 server: agent registration, the task manager, task event streaming, cancel and resubscribe, push notification delivery (signed JWT) and the extension points for stores and authorization. It has no ASP.NET Core or gRPC dependency. For HTTP endpoints add [Ambinder.A2A.AspNetCore](https://www.nuget.org/packages/Ambinder.A2A.AspNetCore) (and [Ambinder.A2A.AspNetCore.Grpc](https://www.nuget.org/packages/Ambinder.A2A.AspNetCore.Grpc) for gRPC).

```
dotnet add package Ambinder.A2A.Server
```

## Extension points

| Interface | Purpose | Default |
|---|---|---|
| `IAgentHandler` | Your agent logic. One new instance per execution (and per cancel). | none, you write it |
| `ITaskStore` | Stores tasks. | `InMemoryTaskStore` |
| `IPushConfigStore` | Stores push notification configs. | `InMemoryPushConfigStore` |
| `IAgentAuthorizer` | Checks the agent's policy before an operation. | allow (`AgentAuthorizer` in the ASP.NET Core package) |
| `IOwnerResolver` | Decides who owns a task. | `DefaultOwnerResolver` |
| `ITaskEventObserver` | Sees every task event (the push sender is one). | push sender |

Register your own store before `AddA2AServerCore`; the library uses `TryAdd`. Telemetry uses only `System.Diagnostics`: add the `Ambinder.A2A.Server` activity source and meter to your own exporter.

## Example

An agent that replies with one message:

```csharp
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;

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

Register it (the web endpoints come from the ASP.NET Core package, which calls this for you):

```csharp
services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200")
    .AddAgent<HelloAgent>("hello", _ => new AgentCard { Name = "hello", Description = "Says hello.", Version = "1.0.0" });
```

For a task instead of a direct reply, call `sink.UpdateStatusAsync`, `sink.AddArtifactAsync`, then a terminal state.

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for a full host example, samples and guides.
