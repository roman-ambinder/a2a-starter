# Ambinder.A2A.Protocol

The A2A v1.0 data model for .NET: records for every protocol message, the JSON rules of the protocol (proto3 canonical JSON, source generated), the error codes and the telemetry names. It has no package dependencies and no ASP.NET Core dependency, so a server and a client can both use it.

```
dotnet add package Ambinder.A2A.Protocol
```

## What is in it

| Area | Types |
|---|---|
| Models | `AgentCard`, `AgentTask`, `Message`, `Part`, `Artifact`, request and response records. Init-only records; build them with object initializers. |
| JSON | `A2AJson.Options` (camelCase, proto enum names, base64 bytes, UTC timestamps, no `kind` field, empty lists omitted). `A2AJsonContext` is the source-generated context. |
| Errors | `A2AException`, `A2AErrorCode`, `A2AAuthException`. Throw `new A2AException(A2AErrorCode.X, "message")` from agent code to return a protocol error. |
| Constants | Header names, binding names and the protocol version (`A2AConstants`); span, metric and attribute names (`A2ATelemetryNames`). |

C# name rule: the proto `TaskStatus` is `AgentTaskStatus`, to avoid a clash with `System.Threading.Tasks.TaskStatus`. Use `A2AJson.Options` whenever you serialize or parse a model, so the wire format stays correct.

## Example

```csharp
using System.Text.Json;
using Ambinder.A2A.Protocol;

var request = new SendMessageRequest
{
    Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hello")] },
};
string json = JsonSerializer.Serialize(request, A2AJson.Options);
var back = JsonSerializer.Deserialize<SendMessageRequest>(json, A2AJson.Options);
```

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for the server and client packages, samples and guides.
