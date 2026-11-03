# Ambinder.A2A.Protocol

The A2A v1.0 data model for .NET: records for every message type, the JSON rules of the protocol and the error codes. It has no ASP.NET Core dependency, so both a server and a client can use it.

## What to copy

Reference or copy the whole project. Everything else in this repo builds on it.

| Area | Where |
|---|---|
| Models (`AgentCard`, `AgentTask`, `Message`, `Part`, `Artifact`, request and response records) | `Models/` |
| JSON options (camelCase, proto enum names, base64 bytes, UTC timestamps, no `kind` field) | `Json/A2AJson.cs` |
| Errors (`A2AException`, `A2AErrorCode`, `A2AAuthException`) | `Errors/` |
| Header names, binding names, version | `A2AConstants.cs` |
| Names of the spans, attributes, metrics and event that the libraries emit (`System.Diagnostics`, no OpenTelemetry package) | `A2ATelemetryNames.cs` |

C# name rule: the proto `TaskStatus` is `AgentTaskStatus` here, to avoid a clash with `System.Threading.Tasks.TaskStatus`.
Models are init-only records. Build them with object initializers.

## Extension points

None to implement. Use `A2AJson.Options` whenever you serialize or parse a model, so the wire format stays correct.
Throw `A2AException(A2AErrorCode.X, "message")` from agent code to return a protocol error.

## Minimal use

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
