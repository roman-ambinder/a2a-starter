# A2A .NET 10 Reference — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A runnable, fully tested A2A v1.0 reference (server + client libraries, four demo agents, mock Entra, webhook receiver, console client) on .NET 10 using only stable NuGet packages and zero external runtime dependencies.

**Architecture:** `A2A.Protocol` holds v1.0 records and JSON rules. `A2A.Protocol.Grpc` compiles the official `a2a.proto` and maps to/from those records. `A2A.Server` hosts one transport-neutral request handler (`IA2ARequestHandler`) behind three thin adapters (JSON-RPC, REST, gRPC). `A2A.Client` mirrors it with one `IA2AClient` and three transport implementations. Samples hold all demo logic.

**Tech Stack:** .NET 10, ASP.NET Core, System.Text.Json, Grpc.AspNetCore / Grpc.Net.Client / Grpc.Tools, Google.Protobuf, Google.Api.CommonProtos, Grpc.StatusProto, JwtBearer, Microsoft.IdentityModel.JsonWebTokens, Microsoft.Extensions.AI.Abstractions, Azure.Identity, System.CommandLine, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-11-02-a2a-dotnet-reference-design.md` (read sections 1a and 12a first: Phase 1 scope and builder calls). Normative protocol: `proto/a2a.proto` (vendored in Task 1) and https://a2a-protocol.org/latest/specification/.

## Global Constraints

- Target `net10.0` only. `Nullable` enable, `TreatWarningsAsErrors` true, `ImplicitUsings` enable.
- **Stable NuGet packages only. No version with a `-` suffix.** Central package management in `Directory.Packages.props`, exact pins. Versions verified 2026-11-02:
  Grpc.AspNetCore 2.85.0, Grpc.Net.Client 2.85.0, Grpc.Tools 2.85.0, Grpc.StatusProto 2.85.0, Google.Protobuf 3.36.2, Google.Api.CommonProtos 2.17.0, Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14, Microsoft.IdentityModel.JsonWebTokens 8.23.0, Microsoft.Extensions.AI.Abstractions 10.11.0, Azure.Identity 1.21.0, System.CommandLine 2.0.14, xunit.v3 4.0.2, xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0. If a pin fails to restore, use the newest stable version and note it in the commit.
- No A2A SDK packages (`A2A`, `A2A.AspNetCore`, …) anywhere.
- Protocol version string `"1.0"`. Header `A2A-Version` (also accepted as query parameter `A2A-Version`); gRPC metadata key `a2a-version`. Missing or other value → `VersionNotSupportedError`. Agent card endpoints are exempt.
- Extensions header `A2A-Extensions` (comma-separated URIs); gRPC metadata `a2a-extensions`.
- JSON: camelCase field names, enum values as proto names (`TASK_STATE_WORKING`, `ROLE_USER`), `bytes` as base64, timestamps ISO 8601 UTC, nulls omitted, no `kind` field anywhere. Request/response content type `application/a2a+json` (also accept `application/json`). Streams: `text/event-stream`.
- `src/` must not reference `samples/`. `src/A2A.Protocol` must not reference ASP.NET Core.
- Phase 1 runs as a single instance; all state in memory. No real Entra, no real LLM provider code.
- Local ports (fixed): MockEntra `5100`; AgentServer HTTP (cards, JSON-RPC, REST) `5200`; AgentServer gRPC per agent: echo `5201`, report `5202`, booking `5203`, secure `5204`; WebhookReceiver `5300`. Integration tests use the same layout offset to base `15000` (env `A2A_TEST_PORT_BASE` overrides).
- Mock identity test values (Development only, live in `samples/MockEntra/appsettings.Development.json`):
  tenant `a2a00000-0000-0000-0000-000000000001`; audience `api://a2a-demo`; scope `api://a2a-demo/.default`;
  clients `a2a-client`/`dev-client-secret` roles `[A2A.Invoke]`, `a2a-approver`/`dev-approver-secret` roles `[A2A.Invoke, Reports.Approve]`, `a2a-noroles`/`dev-noroles-secret` roles `[]`.
  API key header `X-API-Key`, dev key `dev-api-key` (in `samples/AgentServer/appsettings.Development.json`).
- Commit after every task. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Wire Reference (copied from spec + proto; tasks refer here)

**Operations** (JSON-RPC method = gRPC rpc name). REST paths are relative to the agent's REST base URL.

| Operation | REST |
|---|---|
| SendMessage | `POST /message:send` |
| SendStreamingMessage | `POST /message:stream` (SSE) |
| GetTask | `GET /tasks/{id}` (`historyLength`) |
| ListTasks | `GET /tasks` (`contextId`, `status`, `pageSize`, `pageToken`, `historyLength`, `statusTimestampAfter`, `includeArtifacts`) |
| CancelTask | `POST /tasks/{id}:cancel` |
| SubscribeToTask | `GET /tasks/{id}:subscribe` (SSE; also accept `POST` — spec prose table says POST, proto says GET) |
| CreateTaskPushNotificationConfig | `POST /tasks/{taskId}/pushNotificationConfigs` |
| GetTaskPushNotificationConfig | `GET /tasks/{taskId}/pushNotificationConfigs/{id}` |
| ListTaskPushNotificationConfigs | `GET /tasks/{taskId}/pushNotificationConfigs` |
| DeleteTaskPushNotificationConfig | `DELETE /tasks/{taskId}/pushNotificationConfigs/{id}` |
| GetExtendedAgentCard | `GET /extendedAgentCard` |

**Errors**

| `A2AErrorCode` | JSON-RPC | HTTP | gRPC | ErrorInfo reason |
|---|---|---|---|---|
| ParseError | -32700 | 400 | INVALID_ARGUMENT | — |
| InvalidRequest | -32600 | 400 | INVALID_ARGUMENT | — |
| MethodNotFound | -32601 | 404 | UNIMPLEMENTED | — |
| InvalidParams | -32602 | 400 | INVALID_ARGUMENT | — |
| InternalError | -32603 | 500 | INTERNAL | — |
| TaskNotFound | -32001 | 404 | NOT_FOUND | TASK_NOT_FOUND |
| TaskNotCancelable | -32002 | 400 | FAILED_PRECONDITION | TASK_NOT_CANCELABLE |
| PushNotificationNotSupported | -32003 | 400 | FAILED_PRECONDITION | PUSH_NOTIFICATION_NOT_SUPPORTED |
| UnsupportedOperation | -32004 | 400 | FAILED_PRECONDITION | UNSUPPORTED_OPERATION |
| ContentTypeNotSupported | -32005 | 400 | INVALID_ARGUMENT | CONTENT_TYPE_NOT_SUPPORTED |
| InvalidAgentResponse | -32006 | 500 | INTERNAL | INVALID_AGENT_RESPONSE |
| ExtendedAgentCardNotConfigured | -32007 | 400 | FAILED_PRECONDITION | EXTENDED_AGENT_CARD_NOT_CONFIGURED |
| ExtensionSupportRequired | -32008 | 400 | FAILED_PRECONDITION | EXTENSION_SUPPORT_REQUIRED |
| VersionNotSupported | -32009 | 400 | FAILED_PRECONDITION | VERSION_NOT_SUPPORTED |

ErrorInfo `domain` = `a2a-protocol.org`. REST error body (google.rpc.Status JSON form):
`{"error":{"code":404,"status":"NOT_FOUND","message":"...","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"TASK_NOT_FOUND","domain":"a2a-protocol.org"}]}}`.
Auth failures are not A2A errors: HTTP 401/403 (JSON-RPC too), gRPC `UNAUTHENTICATED`/`PERMISSION_DENIED`.

**Streaming frames.** REST SSE: `data: <StreamResponse JSON>\n\n`. JSON-RPC SSE: `data: {"jsonrpc":"2.0","id":<request id>,"result":<StreamResponse>}\n\n`; an error ends the stream with one frame carrying `error`. First event is the `task` (or a single `message`); stream closes after a terminal or interrupted state.

**Semantics.** Blocking by default: SendMessage waits for terminal (`COMPLETED`, `FAILED`, `CANCELED`, `REJECTED`) or interrupted (`INPUT_REQUIRED`, `AUTH_REQUIRED`) state; `returnImmediately: true` returns right after task creation. ListTasks: default pageSize 50, valid 1–100, sorted by `status.timestamp` descending, `includeArtifacts` default false, only tasks visible to the caller. `historyLength` 0 → no history; unset → full history.

**Push delivery.** `POST {url}` with body = one StreamResponse, `Content-Type: application/a2a+json`, `Authorization: {authentication.scheme} {authentication.credentials}` when set, `X-A2A-Notification-Token: {token}` when set, `X-A2A-Push-Signature: <JWT>` (builder call 4). Timeout 15 s. 2xx = acknowledged.

**URL layout (AgentServer).** Root card `GET /.well-known/agent-card.json` (= report agent). Per agent `{name}`: card `GET /agents/{name}/.well-known/agent-card.json`; JSON-RPC `POST /agents/{name}/jsonrpc`; REST base `/agents/{name}/rest`; gRPC `http://localhost:{agent port}`. Card `supportedInterfaces` lists all three with `protocolBinding` `JSONRPC`, `HTTP+JSON`, `GRPC` and `protocolVersion` `"1.0"`.

## Review Focus

1. **Client drops a stream mid-task** (closes SSE or cancels gRPC call): the task keeps running, the subscriber is removed, and `SubscribeToTask` reattaches. Test in Task 6 (`Subscriber_cancellation_does_not_cancel_task`) and Task 16 (`Resubscribe_after_disconnect_receives_terminal_event`).
2. **Two messages race into the same task** (e.g. a double answer to `INPUT_REQUIRED`): one wins, the other gets `UnsupportedOperation`; task state is never corrupted. Test in Task 6 (`Concurrent_continuation_second_is_rejected`).
3. **Malformed parts or bodies** (part with zero or two of `text/raw/url/data`, invalid JSON, wrong enum string, body over 4 MB): `InvalidParams`/`ParseError`/HTTP 413, never 500. Tests in Task 2 (`Part_with_two_contents_fails_validation`) and Task 8 (`Malformed_requests_map_to_jsonrpc_errors`).
4. **Webhook down or slow:** push sender retries 3 times (1 s, 2 s, 4 s; configurable), never blocks task progress, logs and gives up. Test in Task 7 (`Failing_webhook_does_not_block_task_and_is_retried`).
5. **Tampered `pageToken` or out-of-range `pageSize`:** `InvalidParams`, not 500 or silent empty page. Test in Task 5 (`Invalid_page_token_throws_invalid_params`).

---

## File Structure

```
A2A.Reference.slnx
Directory.Build.props            # net10.0, nullable, warnings-as-errors
Directory.Packages.props         # pins above
global.json                      # sdk 10.0.401, rollForward latestFeature
.gitignore .editorconfig .env.example
proto/a2a.proto  proto/README.md # vendored spec proto + source commit
src/A2A.Protocol/
  Models/        AgentTask.cs Message.cs Part.cs Artifact.cs Events.cs AgentCard.cs Security.cs Requests.cs Responses.cs PushNotification.cs Enums.cs
  Json/          A2AJson.cs (options + source-gen context)
  Errors/        A2AErrorCode.cs A2AError.cs A2AException.cs
  A2AConstants.cs                # version, header names, content types, binding names
src/A2A.Protocol.Grpc/
  ProtoMapper.*.cs               # partial static class, one file per type group
src/A2A.Server/
  IA2ARequestHandler.cs A2ACallContext.cs
  Agents/        IAgentHandler.cs AgentRequestContext.cs IAgentEventSink.cs AgentRegistration.cs AgentCatalog.cs
  Tasks/         ITaskStore.cs InMemoryTaskStore.cs TaskEventBroker.cs TaskManager.cs TaskStateRules.cs PageToken.cs
  Push/          IPushConfigStore.cs InMemoryPushConfigStore.cs PushNotificationSender.cs WebhookUrlValidator.cs PushSigningKey.cs
  Auth/          IAgentAuthorizer.cs AgentAuthorizer.cs A2AAuthException.cs CallerIdentity.cs
  Transports/    ServiceParameters.cs JsonRpc/* Rest/* Grpc/* Sse/SseWriter.cs
  Hosting/       A2AServerBuilder.cs A2AServerOptions.cs EndpointRouteBuilderExtensions.cs
src/A2A.Client/
  IA2AClient.cs A2AClientOptions.cs A2AClientFactory.cs A2ACardResolver.cs
  Transports/    JsonRpcA2AClient.cs RestA2AClient.cs GrpcA2AClient.cs Sse/SseReader.cs
  Auth/          IA2ACredentialProvider.cs ApiKeyCredentialProvider.cs BearerTokenCredentialProvider.cs ClientCredentialsProvider.cs AzureIdentityCredentialProvider.cs
samples/MockEntra/       MockEntraApp.cs Program.cs TokenIssuer.cs appsettings*.json
samples/AgentServer/     AgentServerApp.cs Program.cs Cards/AgentCards.cs Agents/{Echo,Report,Booking,Secure}Agent.cs Llm/FakeChatClient.cs Llm/ChatClientRegistration.cs appsettings*.json
samples/WebhookReceiver/ WebhookReceiverApp.cs Program.cs NotificationStore.cs
samples/AgentClient/     Program.cs FlowRunner.cs Commands/*.cs
tests/A2A.Protocol.Tests/   Json/*Tests.cs Grpc/ProtoRoundTripTests.cs Errors/ErrorCatalogTests.cs
tests/A2A.Server.Tests/     Tasks/*Tests.cs Push/*Tests.cs
tests/A2A.Integration.Tests/ Fixtures/A2ATestHost.cs Fixtures/TransportMatrix.cs Flows/*Tests.cs
docker/  Dockerfile.{mockentra,agentserver,webhook,client}
docker-compose.yml run-all.sh run-all.ps1
README.md docs/flows.md docs/production.md  + README.md in each project
```

Each sample exposes a public static `Build(string[] args, Action<WebApplicationBuilder>? configure = null)` returning `WebApplication`; `Program.cs` is just `Build(args).Run()`. Integration tests start the real apps on real Kestrel ports via `Build`.

---

### Task 1: Solution scaffold and gRPC code generation

**Files:**
- Create: `A2A.Reference.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.editorconfig`
- Create: `proto/a2a.proto` (copy of `https://raw.githubusercontent.com/a2aproject/A2A/main/specification/a2a.proto`), `proto/README.md` (source URL, commit SHA from `git ls-remote https://github.com/a2aproject/A2A main`, date)
- Create: `src/A2A.Protocol/A2A.Protocol.csproj`, `src/A2A.Protocol.Grpc/A2A.Protocol.Grpc.csproj`
- Create: `tests/A2A.Protocol.Tests/A2A.Protocol.Tests.csproj`, `tests/A2A.Protocol.Tests/Grpc/GeneratedCodeTests.cs`

**Interfaces:**
- Produces: generated namespace `Lf.A2a.V1` (types `Task`, `Message`, `Part`, `AgentCard`, `StreamResponse`, `A2AService.A2AServiceBase`, `A2AService.A2AServiceClient`, …).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void Generated_service_has_all_eleven_operations()
{
    var methods = Lf.A2a.V1.A2AService.Descriptor.Methods.Select(m => m.Name).ToHashSet();
    Assert.Equal(new HashSet<string> { "SendMessage","SendStreamingMessage","GetTask","ListTasks","CancelTask",
        "SubscribeToTask","CreateTaskPushNotificationConfig","GetTaskPushNotificationConfig",
        "ListTaskPushNotificationConfigs","DeleteTaskPushNotificationConfig","GetExtendedAgentCard" }, methods);
}
```

- [ ] **Step 2: Run `dotnet test tests/A2A.Protocol.Tests` — expect build FAIL (`Lf` namespace not found).**
- [ ] **Step 3: Wire codegen.** `A2A.Protocol.Grpc.csproj`: `<Protobuf Include="../../proto/a2a.proto" ProtoRoot="../../proto" GrpcServices="Both" />`, references Google.Protobuf, Grpc.Net.Client, Grpc.AspNetCore.Server (via Grpc.AspNetCore), Grpc.Tools (PrivateAssets all), Google.Api.CommonProtos (supplies `google/api/*.proto` imports and the `Google.Api` types the generated code references). Test project references both src projects.
- [ ] **Step 4: Run `dotnet build A2A.Reference.slnx` and `dotnet test tests/A2A.Protocol.Tests` — expect PASS, 0 warnings.**
- [ ] **Step 5: Commit** `chore: scaffold solution and generate gRPC types from a2a.proto`

---

### Task 2: Protocol models and JSON

**Files:**
- Create: `src/A2A.Protocol/Models/*.cs`, `src/A2A.Protocol/Json/A2AJson.cs`, `src/A2A.Protocol/A2AConstants.cs`
- Test: `tests/A2A.Protocol.Tests/Json/ModelJsonTests.cs`

**Interfaces:**
- Produces (all `sealed record` with `init` properties, names = proto field names in PascalCase; collections `IReadOnlyList<T>` defaulting to empty; `google.protobuf.Struct`/`Value` → `JsonElement?`):
  - `AgentTask` (proto `Task`; C# name avoids clash with `System.Threading.Tasks.Task`): `Id`, `ContextId`, `Status`, `Artifacts`, `History`, `Metadata`.
  - `TaskStatus(TaskState State, Message? Message, DateTimeOffset? Timestamp)`; `enum TaskState` (all 9 proto values) and `enum Role` with `[JsonStringEnumMemberName("TASK_STATE_WORKING")]` etc.
  - `Message`: `MessageId`, `ContextId`, `TaskId`, `Role`, `Parts`, `Metadata`, `Extensions`, `ReferenceTaskIds`.
  - `Part`: `Text`, `Raw` (`byte[]?`), `Url`, `Data` (`JsonElement?`), `Metadata`, `Filename`, `MediaType`; factories `Part.FromText(string, string? mediaType = null)`, `Part.FromRaw(byte[], string mediaType, string? filename = null)`, `Part.FromUrl(string, string mediaType, string? filename = null)`, `Part.FromData(JsonElement)`; `bool IsValid` (exactly one content set); `string EffectiveMediaType` (`text/plain` for text, `application/json` for data, else `MediaType ?? "application/octet-stream"`).
  - `Artifact`, `TaskStatusUpdateEvent`, `TaskArtifactUpdateEvent` (`Append`, `LastChunk`), `StreamResponse` (`Task`, `Message`, `StatusUpdate`, `ArtifactUpdate`; exactly one), `SendMessageResponse` (`Task`, `Message`).
  - All request/response records from the proto: `SendMessageRequest`, `SendMessageConfiguration` (`AcceptedOutputModes`, `TaskPushNotificationConfig`, `HistoryLength`, `ReturnImmediately`), `GetTaskRequest`, `ListTasksRequest`, `ListTasksResponse`, `CancelTaskRequest`, `SubscribeToTaskRequest`, `TaskPushNotificationConfig`, `AuthenticationInfo`, `GetTaskPushNotificationConfigRequest`, `DeleteTaskPushNotificationConfigRequest`, `ListTaskPushNotificationConfigsRequest`, `ListTaskPushNotificationConfigsResponse`, `GetExtendedAgentCardRequest`.
  - `AgentCard` and children: `AgentInterface`, `AgentProvider`, `AgentCapabilities`, `AgentExtension`, `AgentSkill`, `AgentCardSignature`, `SecurityScheme` (one nullable property per oneof case: `ApiKeySecurityScheme`, `HttpAuthSecurityScheme`, `Oauth2SecurityScheme`, `OpenIdConnectSecurityScheme`, `MtlsSecurityScheme`), `OAuthFlows` (`AuthorizationCode`, `ClientCredentials`, `DeviceCode`), `SecurityRequirement(IReadOnlyDictionary<string, StringList> Schemes)`, `StringList(IReadOnlyList<string> List)`.
  - `static class A2AJson { JsonSerializerOptions Options; }` backed by a `JsonSerializerContext` covering every model.
  - `static class A2AConstants`: `ProtocolVersion = "1.0"`, `VersionHeader = "A2A-Version"`, `ExtensionsHeader = "A2A-Extensions"`, `GrpcVersionKey = "a2a-version"`, `GrpcExtensionsKey = "a2a-extensions"`, `ContentType = "application/a2a+json"`, `EventStream = "text/event-stream"`, `BindingJsonRpc = "JSONRPC"`, `BindingRest = "HTTP+JSON"`, `BindingGrpc = "GRPC"`, `WellKnownCardPath = "/.well-known/agent-card.json"`, `NotificationTokenHeader = "X-A2A-Notification-Token"`, `PushSignatureHeader = "X-A2A-Push-Signature"`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Task_state_serializes_as_proto_enum_name()
    => Assert.Equal("\"TASK_STATE_INPUT_REQUIRED\"", JsonSerializer.Serialize(TaskState.InputRequired, A2AJson.Options));

[Fact] public void Text_part_serializes_flat_without_kind()
    => Assert.Equal("""{"text":"hi"}""", JsonSerializer.Serialize(Part.FromText("hi"), A2AJson.Options));

[Fact] public void Raw_part_is_base64()
    => Assert.Contains("\"raw\":\"AQI=\"", JsonSerializer.Serialize(Part.FromRaw([1, 2], "application/octet-stream"), A2AJson.Options));

[Fact] public void Spec_push_example_deserializes()   // spec §6.x webhook example body
{
    var r = JsonSerializer.Deserialize<StreamResponse>("""{"statusUpdate":{"taskId":"t1","contextId":"c1","status":{"state":"TASK_STATE_COMPLETED","timestamp":"2024-03-15T18:30:00Z"}}}""", A2AJson.Options)!;
    Assert.Equal(TaskState.Completed, r.StatusUpdate!.Status.State);
}

[Fact] public void Part_with_two_contents_fails_validation()
    => Assert.False(new Part { Text = "a", Url = "https://x" }.IsValid);

[Fact] public void Unknown_enum_string_throws_JsonException()
    => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TaskStatus>("""{"state":"working"}""", A2AJson.Options));
```

- [ ] **Step 2: Run `dotnet test tests/A2A.Protocol.Tests --filter ModelJsonTests` — expect FAIL (types missing).**
- [ ] **Step 3: Implement the models, `A2AJson`, `A2AConstants`.** Options: `JsonSerializerDefaults.Web`, `DefaultIgnoreCondition = WhenWritingNull`, enum converter honoring `JsonStringEnumMemberName`, no integer enum fallback.
- [ ] **Step 4: Run tests — expect PASS.**
- [ ] **Step 5: Commit** `feat(protocol): add A2A v1.0 models and JSON rules`

---

### Task 3: Proto mapper and wire-conformance tests

**Files:**
- Create: `src/A2A.Protocol.Grpc/ProtoMapper.*.cs`
- Test: `tests/A2A.Protocol.Tests/Grpc/ProtoRoundTripTests.cs`, `tests/A2A.Protocol.Tests/Samples.cs` (one fully populated instance of every top-level model)

**Interfaces:**
- Consumes: Task 1 generated types, Task 2 models.
- Produces: `public static partial class ProtoMapper` with `ToProto(X)` / `FromProto(Lf.A2a.V1.X)` for every model in Task 2. Struct/Value ↔ `JsonElement` through `Google.Protobuf.JsonFormatter`/`JsonParser`.

- [ ] **Step 1: Write failing tests.** This is the drift guard: our System.Text.Json output must parse with the official proto JSON parser, which rejects unknown fields.

```csharp
public static TheoryData<object> AllSamples => Samples.All(); // AgentTask, Message (each Part kind), AgentCard (every security scheme + flow), StreamResponse (each case), every request/response

[Theory, MemberData(nameof(AllSamples))]
public void Our_json_parses_with_official_proto_parser(object sample)
{
    var json = JsonSerializer.Serialize(sample, sample.GetType(), A2AJson.Options);
    var proto = ProtoJson.Parse(sample.GetType(), json);           // test helper: JsonParser(Settings.Default.WithIgnoreUnknownFields(false))
    Assert.NotNull(proto);
}

[Theory, MemberData(nameof(AllSamples))]
public void Proto_round_trip_preserves_values(object sample)
{
    var back = ProtoMapper.FromProtoObject(ProtoMapper.ToProtoObject(sample));
    Assert.Equal(JsonSerializer.Serialize(sample, sample.GetType(), A2AJson.Options),
                 JsonSerializer.Serialize(back, back.GetType(), A2AJson.Options));
}

[Theory, MemberData(nameof(AllSamples))]
public void Official_formatter_output_deserializes_with_our_options(object sample)
{
    var protoJson = JsonFormatter.Default.Format(ProtoMapper.ToProtoObject(sample));
    var ours = JsonSerializer.Deserialize(protoJson, sample.GetType(), A2AJson.Options);
    Assert.Equal(JsonSerializer.Serialize(sample, sample.GetType(), A2AJson.Options),
                 JsonSerializer.Serialize(ours, sample.GetType(), A2AJson.Options));
}
```

`ToProtoObject(object)` / `FromProtoObject(IMessage)` are type-switch helpers inside `ProtoMapper` used by tests and by the gRPC adapter.

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement `ProtoMapper`.** Empty proto strings/0/false map to `null` where our record field is nullable; `Timestamp` ↔ `DateTimeOffset`; `bytes` ↔ `byte[]`.
- [ ] **Step 4: Run — expect PASS. Any failure means a model field name or enum value is wrong: fix the model, not the test.**
- [ ] **Step 5: Commit** `feat(protocol): map models to gRPC types with wire-conformance tests`

---

### Task 4: Error catalog

**Files:**
- Create: `src/A2A.Protocol/Errors/A2AErrorCode.cs`, `A2AError.cs`, `A2AException.cs`, `A2AAuthException.cs`
- Test: `tests/A2A.Protocol.Tests/Errors/ErrorCatalogTests.cs`

**Interfaces:**
- Produces:
  - `enum A2AErrorCode` with the 14 values from the Wire Reference, underlying int = JSON-RPC code.
  - `sealed record A2AErrorInfo(A2AErrorCode Code, int HttpStatus, string GrpcStatus, string? Reason)`; `static class A2AError { A2AErrorInfo Get(A2AErrorCode); A2AErrorCode? FromReason(string reason); const string Domain = "a2a-protocol.org"; }`. `GrpcStatus` uses `Grpc.Core.StatusCode` names as strings so `A2A.Protocol` stays free of gRPC.
  - `class A2AException(A2AErrorCode code, string message, JsonElement? data = null) : Exception` with `Code`, `Data`.
  - `class A2AAuthException(int httpStatus, string message) : Exception` with `HttpStatus` (401 or 403). Shared by server and client; not an A2A error code.

- [ ] **Step 1: Failing test** — one `[Theory]` with an `[InlineData]` row per table line asserting all four columns, plus `FromReason("TASK_NOT_FOUND") == TaskNotFound`.
- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS.**
- [ ] **Step 5: Commit** `feat(protocol): add A2A error catalog`

---

### Task 5: Task store and ListTasks paging

**Files:**
- Create: `src/A2A.Server/A2A.Server.csproj`, `Tasks/ITaskStore.cs`, `Tasks/InMemoryTaskStore.cs`, `Tasks/PageToken.cs`
- Test: `tests/A2A.Server.Tests/A2A.Server.Tests.csproj`, `tests/A2A.Server.Tests/Tasks/InMemoryTaskStoreTests.cs`

**Interfaces:**
- Produces:
  - `sealed record StoredTask(string AgentName, string Owner, string? Tenant, AgentTask Task)`.
  - `interface ITaskStore { ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct); ValueTask SaveAsync(StoredTask task, CancellationToken ct); ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct); }`
  - `PageToken.Encode(DateTimeOffset timestamp, string taskId) : string` (base64url of `ticks:id`) and `PageToken.TryDecode(string, out DateTimeOffset, out string) : bool`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public async Task List_is_sorted_by_status_timestamp_descending_and_paged() // 120 tasks; pageSize unset → 50, nextPageToken set, totalSize 120; third page has 20 and nextPageToken ""
[Fact] public async Task List_filters_by_owner_contextId_status_and_timestamp_after()
[Fact] public async Task List_omits_artifacts_unless_includeArtifacts_true()
[Fact] public async Task List_applies_historyLength_zero_as_no_history()
[Theory, InlineData(0), InlineData(101)]
public async Task Page_size_out_of_range_throws_invalid_params(int size)
    => Assert.Equal(A2AErrorCode.InvalidParams, (await Assert.ThrowsAsync<A2AException>(() => List(pageSize: size))).Code);
[Fact] public async Task Invalid_page_token_throws_invalid_params()     // "not-a-token" and a base64 of garbage
```

- [ ] **Step 2: Run `dotnet test tests/A2A.Server.Tests` — FAIL.** **Step 3: Implement** (`ConcurrentDictionary` keyed by `(agent, taskId)`; store clones so callers can't mutate stored state). **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(server): add in-memory task store with cursor paging`

---

### Task 6: Task manager, event broker, request handler

**Files:**
- Create: `src/A2A.Server/IA2ARequestHandler.cs`, `A2ACallContext.cs`, `Agents/*.cs`, `Tasks/TaskEventBroker.cs`, `Tasks/TaskManager.cs`, `Tasks/TaskStateRules.cs`, `Auth/CallerIdentity.cs`
- Test: `tests/A2A.Server.Tests/Tasks/TaskManagerTests.cs`, `tests/A2A.Server.Tests/Tasks/TaskEventBrokerTests.cs`, `tests/A2A.Server.Tests/TestAgents.cs` (scripted test agents)

**Interfaces:**
- Consumes: `ITaskStore` (Task 5), models, `A2AException`.
- Produces:
  - `sealed record A2ACallContext(string AgentName, ClaimsPrincipal User, string Owner, string? Tenant, string? ProtocolVersion, IReadOnlyList<string> RequestedExtensions)`. `Tenant` comes from the request's `tenant` field and is stored on `StoredTask`; Phase 1 does not isolate by it beyond that.
  - `CallerIdentity.GetOwner(ClaimsPrincipal user) : string` — bearer → `"tenant:{tid}"`; API key → `"apikey:{name}"`; unauthenticated → `"anonymous"` (spec builder note: Secure step-up uses a second app in the same tenant, so ownership is tenant-scoped).
  - `interface IA2ARequestHandler` — one method per operation, each `(A2ACallContext call, TRequest request, CancellationToken ct)`: `SendMessageAsync → Task<SendMessageResponse>`, `SendStreamingMessageAsync → IAsyncEnumerable<StreamResponse>`, `GetTaskAsync → Task<AgentTask>`, `ListTasksAsync → Task<ListTasksResponse>`, `CancelTaskAsync → Task<AgentTask>`, `SubscribeToTaskAsync → IAsyncEnumerable<StreamResponse>`, `CreateTaskPushNotificationConfigAsync`, `GetTaskPushNotificationConfigAsync`, `ListTaskPushNotificationConfigsAsync`, `DeleteTaskPushNotificationConfigAsync → Task`, `GetExtendedAgentCardAsync → Task<AgentCard>`. `TaskManager` implements it (push methods throw `UnsupportedOperation` until Task 7).
  - `interface IAgentHandler { ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct); ValueTask CancelAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct) => sink.UpdateStatusAsync(TaskState.Canceled, null, ct); }`
  - `sealed record AgentRequestContext(string AgentName, string TaskId, string ContextId, Message Message, AgentTask? CurrentTask, ClaimsPrincipal User, IReadOnlyList<string> ActiveExtensions, SendMessageConfiguration? Configuration)`.
  - `interface IAgentEventSink { ValueTask ReplyAsync(Message message, CancellationToken ct); ValueTask UpdateStatusAsync(TaskState state, Message? message, CancellationToken ct); ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, CancellationToken ct); }` — `ReplyAsync` is valid only as the agent's first and only event (direct-message result, no task stored).
  - `sealed record AgentRegistration(string Name, Type HandlerType, AgentCard PublicCard, AgentCard? ExtendedCard, string? AuthorizationPolicy)`; `AgentCatalog` (`Get(string name)` → registration or `TaskNotFound`-style lookup failure → HTTP 404 at transport level).
  - `TaskEventBroker`: `Publish(string agent, string taskId, StreamResponse evt)`, `IAsyncEnumerable<StreamResponse> Subscribe(string agent, string taskId, CancellationToken ct)` (bounded `Channel<T>` per subscriber, completed after terminal/interrupted state).

**Behavior the tests pin:**
- New message (no `taskId`) → new task (`Guid` ids; `contextId` from message or new), state `SUBMITTED` then whatever the agent emits; agent runs on a background `Task` with its own `CancellationTokenSource` (not the request's token).
- Message with `taskId`: unknown → `TaskNotFound`; owner mismatch → `TaskNotFound`; task in terminal state → `UnsupportedOperation`; task currently `WORKING`/`SUBMITTED` → `UnsupportedOperation`; `INPUT_REQUIRED`/`AUTH_REQUIRED` → resumes, message appended to history. A per-task `SemaphoreSlim` guards the state check + transition.
- Blocking vs `returnImmediately` as in Wire Reference. `historyLength` applied to returned task.
- Illegal transitions (from terminal) throw `InvalidAgentResponse` and fail the task.
- Agent throws → task `FAILED` with an agent message `"Agent error"` (no exception details leaked).
- Cancel: unknown → `TaskNotFound`; terminal → `TaskNotCancelable`; else cancels the CTS, calls `CancelAsync`, returns task in `CANCELED`.
- Subscribe: terminal task → `UnsupportedOperation`; else first event = current `task`, then live events.
- Parts invalid → `InvalidParams`; part `EffectiveMediaType` not in card `defaultInputModes` (or skill input modes) → `ContentTypeNotSupported`.
- Extension declared `required: true` on the card but not in `RequestedExtensions` → `ExtensionSupportRequired`. `ActiveExtensions` = requested ∩ declared.
- Extended card: card `capabilities.extendedAgentCard != true` or no `ExtendedCard` → `ExtendedAgentCardNotConfigured`.

- [ ] **Step 1: Failing tests** (names; assertions use the scripted agents in `TestAgents.cs`)

```
Send_with_direct_reply_returns_message_and_stores_no_task
Send_blocking_waits_for_completed_and_returns_artifacts
Send_return_immediately_returns_working_task
Send_blocking_returns_at_input_required
Continue_input_required_task_completes
Continue_terminal_task_is_rejected                 // UnsupportedOperation
Concurrent_continuation_second_is_rejected         // Review Focus 2: two parallel continuations → exactly one UnsupportedOperation
Other_owner_gets_task_not_found
Agent_exception_fails_task_without_leaking_details
Cancel_running_task_returns_canceled
Cancel_completed_task_throws_not_cancelable
Stream_yields_task_then_updates_then_closes_on_terminal
Subscribe_to_terminal_task_is_unsupported
Subscriber_cancellation_does_not_cancel_task       // Review Focus 1
Unsupported_input_media_type_throws_content_type_not_supported
Missing_required_extension_throws_extension_support_required
Extended_card_not_configured_throws
History_length_zero_returns_no_history
```

- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS (run 5× with `--no-build` to catch races).**
- [ ] **Step 5: Commit** `feat(server): add task manager, event broker and agent abstraction`

---

### Task 7: Push notification configs and sender

**Files:**
- Create: `src/A2A.Server/Push/*.cs`
- Modify: `src/A2A.Server/Tasks/TaskManager.cs` (implement 4 push operations; publish events to sender; accept `configuration.taskPushNotificationConfig` on send)
- Test: `tests/A2A.Server.Tests/Push/PushTests.cs`

**Interfaces:**
- Produces:
  - `interface IPushConfigStore` (CRUD + `ListAsync(agent, taskId, pageSize, pageToken)`), `InMemoryPushConfigStore`. Config ids: server-assigned `Guid` when empty.
  - `PushNotificationSender` (hosted service; `Channel<PushWorkItem>`; named `HttpClient` `"a2a-push"`); options `PushOptions { int MaxAttempts = 3; TimeSpan InitialBackoff = 1s; TimeSpan Timeout = 15s; bool AllowPrivateNetworks; }` (`AllowPrivateNetworks` true only in Development).
  - `WebhookUrlValidator.Validate(Uri url, bool allowPrivateNetworks)` → throws `InvalidParams` for non-http(s), and when `allowPrivateNetworks` is false: non-HTTPS, loopback, link-local, RFC1918, and unique-local IPv6 (checked after DNS resolution).
  - `PushSigningKey` (RSA 2048 generated at startup, `kid`), `GET /.well-known/a2a-push-jwks.json` is mapped in Task 8. Signature JWT claims: `iss` = server base URL, `aud` = webhook URL, `task_id`, `body_sha256` (base64url SHA-256 of body), `iat`, `exp` = iat + 5 min, RS256.
- Agent with card `capabilities.pushNotifications != true` → all push operations and send-with-push-config throw `PushNotificationNotSupported` (checked before task lookup).
- Delete is idempotent (deleting a missing config succeeds). Configs removed when task reaches terminal state after final delivery.

- [ ] **Step 1: Failing tests**

```
Create_get_list_delete_push_config_round_trip
Delete_is_idempotent
Push_on_agent_without_capability_throws_not_supported
Every_task_event_is_posted_as_stream_response        // stub HttpMessageHandler captures requests: body parses as StreamResponse; Authorization, X-A2A-Notification-Token, X-A2A-Push-Signature present
Push_signature_verifies_and_binds_body_hash
Failing_webhook_does_not_block_task_and_is_retried   // Review Focus 4: handler returns 500; task still completes on time; 3 attempts recorded; backoff set to 10 ms in test
Private_network_url_rejected_when_not_allowed        // http://127.0.0.1, http://10.0.0.5, http://[fd00::1], ftp://x
```

- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS.**
- [ ] **Step 5: Commit** `feat(server): add push notification configs and signed webhook delivery`

---

### Task 8: Server hosting API, service parameters, cards, JSON-RPC transport

**Files:**
- Create: `src/A2A.Server/Hosting/*.cs`, `Transports/ServiceParameters.cs`, `Transports/Sse/SseWriter.cs`, `Transports/JsonRpc/JsonRpcEndpoint.cs`, `Transports/JsonRpc/JsonRpcModels.cs`, `Auth/IAgentAuthorizer.cs`, `Auth/AgentAuthorizer.cs`
- Create: `tests/A2A.Integration.Tests/A2A.Integration.Tests.csproj`, `tests/A2A.Integration.Tests/Transport/JsonRpcTransportTests.cs`, `tests/A2A.Integration.Tests/Fixtures/MinimalServer.cs` (in-test host using `TestAgents`-style agents; not the sample app)

**Interfaces:**
- Consumes: `IA2ARequestHandler`, `AgentCatalog`, `A2AError`.
- Produces:
  - `IServiceCollection.AddA2AServer(Action<A2AServerOptions>) : A2AServerBuilder`; `A2AServerBuilder.AddAgent<THandler>(string name, Func<IServiceProvider, AgentCard> publicCard, Func<IServiceProvider, AgentCard>? extendedCard = null, string? authorizationPolicy = null) where THandler : class, IAgentHandler`.
  - `A2AServerOptions { string PublicBaseUrl; string? DefaultAgent; IDictionary<string,int> GrpcPorts; long MaxRequestBodyBytes = 4 * 1024 * 1024; PushOptions Push; }`.
  - `IEndpointRouteBuilder.MapA2A()` maps: root + per-agent cards, JSON-RPC route, REST routes (Task 9), push JWKS. gRPC mapped in Task 10.
  - `ServiceParameters.Read(HttpContext) : (string? Version, IReadOnlyList<string> Extensions)`; `ServiceParameters.EnsureSupportedVersion(string?)` throws `VersionNotSupported` unless `"1.0"`.
  - `interface IAgentAuthorizer { Task AuthorizeAsync(AgentRegistration agent, ClaimsPrincipal user, CancellationToken ct); }` — no policy → allow; unauthenticated → `A2AAuthException(401)`; policy fails → `A2AAuthException(403)`. Applied to every operation, including the extended card. Cards are anonymous.
  - JSON-RPC: request `{jsonrpc:"2.0", id, method, params}`; `params` deserialized to the operation's request record; result per operation's return type; `DeleteTaskPushNotificationConfig` result `{}`. Streaming methods respond `text/event-stream` with frames from the Wire Reference; client disconnect only ends that subscriber.
  - Version failure on JSON-RPC: HTTP 200 with JSON-RPC error `-32009` and `data: {"supportedVersions":["1.0"]}`.

- [ ] **Step 1: Failing tests** (raw `HttpClient`, no client library yet)

```
Card_is_served_at_root_and_per_agent_without_version_header
Card_lists_three_interfaces_with_protocol_version_1_0
SendMessage_over_jsonrpc_returns_task_result
SendStreamingMessage_over_jsonrpc_emits_jsonrpc_wrapped_sse_frames
Missing_version_header_returns_version_not_supported   // -32009
Version_as_query_parameter_is_accepted                 // ?A2A-Version=1.0
Malformed_requests_map_to_jsonrpc_errors               // Review Focus 3: "{", {"jsonrpc":"1.0"}, unknown method, params with two-content part, bad enum string → -32700/-32600/-32601/-32602/-32602; body > 4 MB → HTTP 413
A2A_errors_map_to_jsonrpc_codes                        // GetTask unknown id → -32001
Unknown_agent_returns_404
Unauthenticated_call_to_protected_agent_returns_401
```

- [ ] **Step 2: Run `dotnet test tests/A2A.Integration.Tests` — FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(server): add hosting API, agent cards and JSON-RPC transport`

---

### Task 9: REST transport (server)

**Files:**
- Create: `src/A2A.Server/Transports/Rest/RestEndpoints.cs`, `Transports/Rest/RestErrorWriter.cs`
- Test: `tests/A2A.Integration.Tests/Transport/RestTransportTests.cs`

**Interfaces:**
- Consumes: Task 8 hosting, `ServiceParameters`, `IAgentAuthorizer`.
- Produces: routes from the Wire Reference under `/agents/{name}/rest`. Query parameters: camelCase names, booleans `true/false`, timestamps ISO 8601, enum names as in JSON. If ASP.NET route literals containing `:` (e.g. `message:send`, `{id}:cancel`) do not bind, map one catch-all `{**path}` per verb and dispatch by parsing the path — keep the public paths identical either way. Errors written with `RestErrorWriter.Write(HttpContext, A2AException)` in the google.rpc.Status form. `DeleteTaskPushNotificationConfig` → `204`.

- [ ] **Step 1: Failing tests**

```
Every_operation_is_reachable_at_spec_path            // theory over the 11 rows of the Wire Reference
List_tasks_query_parameters_bind                     // ?status=TASK_STATE_COMPLETED&pageSize=2&includeArtifacts=true
Subscribe_accepts_get_and_post
Error_body_is_google_rpc_status_with_error_info      // GET /tasks/missing → 404, status NOT_FOUND, reason TASK_NOT_FOUND, domain a2a-protocol.org
Version_not_supported_is_400_failed_precondition
Stream_frames_are_bare_stream_responses
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(server): add HTTP+JSON REST transport`

---

### Task 10: gRPC transport (server)

**Files:**
- Create: `src/A2A.Server/Transports/Grpc/A2AGrpcService.cs`, `Transports/Grpc/GrpcErrorMapper.cs`, `Hosting/GrpcHostingExtensions.cs`
- Test: `tests/A2A.Integration.Tests/Transport/GrpcTransportTests.cs`

**Interfaces:**
- Consumes: `ProtoMapper`, `IA2ARequestHandler`, `A2AServerOptions.GrpcPorts`.
- Produces:
  - `WebApplicationBuilder.ConfigureA2AKestrel()` — listens on `PublicBaseUrl` port (HTTP/1.1 + HTTP/2) and each `GrpcPorts` value (HTTP/2 only, plaintext in Phase 1).
  - `IEndpointRouteBuilder.MapA2AGrpc()` — maps `A2AGrpcService : Lf.A2a.V1.A2AService.A2AServiceBase`; resolves the agent from `HttpContext.Connection.LocalPort` via `GrpcPorts` (unknown port → `NOT_FOUND`).
  - `GrpcErrorMapper.ToRpcException(A2AException)` — status from catalog; `google.rpc.Status` details with `ErrorInfo{reason, domain}` via Grpc.StatusProto. `A2AAuthException` → `UNAUTHENTICATED`/`PERMISSION_DENIED`.
  - Version from metadata `a2a-version`; extensions from `a2a-extensions`.

- [ ] **Step 1: Failing tests** (raw generated `A2AServiceClient` over `GrpcChannel`)

```
SendMessage_over_grpc_returns_task
SendStreamingMessage_over_grpc_streams_task_then_updates
Each_agent_port_routes_to_its_agent                  // two test agents on two ports return different agent names in metadata
Error_has_error_info_detail                          // GetTask missing → NOT_FOUND + ErrorInfo TASK_NOT_FOUND
Missing_version_metadata_fails_precondition
Cancelled_call_does_not_cancel_task
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(server): add gRPC transport with per-agent ports`

---

### Task 11: Client library — card resolver, credentials, JSON-RPC client

**Files:**
- Create: `src/A2A.Client/*` (except REST/gRPC clients)
- Test: `tests/A2A.Integration.Tests/Client/JsonRpcClientTests.cs`, `tests/A2A.Integration.Tests/Client/CredentialProviderTests.cs`

**Interfaces:**
- Produces:
  - `interface IA2AClient : IAsyncDisposable` — same 11 operations as `IA2ARequestHandler` without `A2ACallContext`, each with `CancellationToken ct = default`; streaming returns `IAsyncEnumerable<StreamResponse>`. Throws `A2AException` for protocol errors and `A2AAuthException` (Task 4) for 401/403, on every transport. Response that fails to parse or violates oneof → `A2AException(InvalidAgentResponse)`.
  - `sealed class A2AClientOptions { HttpClient? HttpClient; IA2ACredentialProvider? Credentials; IReadOnlyList<string> Extensions; string ProtocolVersion = "1.0"; string? PreferredBinding; X509Certificate2? ClientCertificate; }`.
  - `A2ACardResolver(HttpClient)` — `GetAgentCardAsync(Uri baseOrCardUrl, CancellationToken)`; caches per URL for 5 min.
  - `interface IA2ACredentialProvider { ValueTask<IReadOnlyDictionary<string,string>> GetHeadersAsync(AgentCard card, CancellationToken ct); }` — implementations: `ApiKeyCredentialProvider(string key)` (uses the card's `apiKeySecurityScheme.name`/`location`), `BearerTokenCredentialProvider(string token)`, `ClientCredentialsProvider(string clientId, string clientSecret, string scope, Uri? tokenUrlOverride = null)` (token URL from the card's `oauth2SecurityScheme.flows.clientCredentials.tokenUrl`; caches token until 60 s before expiry), `AzureIdentityCredentialProvider(TokenCredential credential, string scope)`.
  - `JsonRpcA2AClient(Uri endpoint, A2AClientOptions)`; `Sse/SseReader.ReadAsync(Stream, CancellationToken) : IAsyncEnumerable<string>` (handles multi-line `data:`, comments, CRLF).

- [ ] **Step 1: Failing tests** (against `MinimalServer` from Task 8)

```
All_eleven_operations_work_over_jsonrpc
Protocol_error_surfaces_as_A2AException_with_code
Unauthorized_surfaces_as_A2AAuthException_401
Malformed_server_response_throws_invalid_agent_response   // stub handler returns {"jsonrpc":"2.0","id":1,"result":{"task":{},"message":{}}}
SseReader_handles_multiline_and_crlf
ClientCredentialsProvider_reads_token_url_from_card_and_caches   // stub token endpoint hit once for two calls
ApiKeyCredentialProvider_uses_card_header_name
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(client): add card resolver, credential providers and JSON-RPC client`

---

### Task 12: Client library — REST and gRPC clients, factory

**Files:**
- Create: `src/A2A.Client/Transports/RestA2AClient.cs`, `Transports/GrpcA2AClient.cs`, `A2AClientFactory.cs`
- Test: `tests/A2A.Integration.Tests/Client/TransportParityTests.cs`

**Interfaces:**
- Produces: `RestA2AClient(Uri baseUrl, A2AClientOptions)`, `GrpcA2AClient(Uri address, A2AClientOptions)` (maps `RpcException` + `ErrorInfo` back to `A2AException` via `A2AError.FromReason`), `static A2AClientFactory.Create(AgentCard card, A2AClientOptions options) : IA2AClient` — picks `PreferredBinding` if present in `supportedInterfaces`, else the first interface in card order; throws `NotSupportedException` listing the card's bindings if none match.
- `tests/.../Fixtures/TransportMatrix.cs`: `public static TheoryData<string> Bindings => ["JSONRPC", "HTTP+JSON", "GRPC"];` used by every later integration test.

- [ ] **Step 1: Failing tests**

```
[Theory, MemberData(nameof(TransportMatrix.Bindings))] All_eleven_operations_work(string binding)
[Theory, ...] Errors_have_same_code_on_every_transport(string binding)   // TaskNotFound, TaskNotCancelable, VersionNotSupported (client configured with "0.3")
[Theory, ...] Auth_failures_have_same_status_on_every_transport(string binding)
Factory_prefers_requested_binding_and_falls_back_to_card_order
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(client): add REST and gRPC clients with transport parity tests`

---

### Task 13: Mock Entra

**Files:**
- Create: `samples/MockEntra/*` (`MockEntraApp.Build`, `TokenIssuer`, `MockEntraOptions`, `README.md`, `appsettings.json`, `appsettings.Development.json`)
- Test: `tests/A2A.Integration.Tests/MockEntra/MockEntraTests.cs`

**Interfaces:**
- Produces endpoints (paths copy real Entra):
  - `GET /{tenant}/v2.0/.well-known/openid-configuration` → `issuer` `{base}/{tenant}/v2.0`, `token_endpoint` `{base}/{tenant}/oauth2/v2.0/token`, `jwks_uri` `{base}/{tenant}/discovery/v2.0/keys`, `authorization_endpoint` `{base}/{tenant}/oauth2/v2.0/authorize` (returns 501), `id_token_signing_alg_values_supported` `["RS256"]`, `token_endpoint_auth_methods_supported` `["client_secret_post","client_secret_basic"]`.
  - `GET /{tenant}/discovery/v2.0/keys` → JWKS (RSA 2048 generated at startup, `kid`).
  - `POST /{tenant}/oauth2/v2.0/token` (form: `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`; also Basic auth) → `{"token_type":"Bearer","expires_in":3600,"access_token":"..."}`; errors use Entra's shape `{"error":"invalid_client","error_description":"AADSTS7000215: ..."}` (400/401).
  - Token claims: `iss`, `aud` (from scope minus `/.default`), `tid`, `azp`, `appid`, `oid` and `sub` (stable per client), `roles` (array; omitted if empty), `ver` `"2.0"`, `iat`, `nbf`, `exp`.
  - `GET /health/live`, `/health/ready`.
- `MockEntraApp.Build` throws `InvalidOperationException("MockEntra runs only in Development")` when the environment is not Development.

- [ ] **Step 1: Failing tests**

```
Discovery_document_matches_entra_shape
Client_credentials_token_has_entra_claims          // a2a-approver → roles contains Reports.Approve, tid matches, aud api://a2a-demo
Token_validates_with_jwt_bearer_against_discovery  // JwtBearer handler configured with Authority → principal authenticated
Wrong_secret_returns_invalid_client
Unknown_scope_returns_invalid_scope
Refuses_to_start_outside_development
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(samples): add Entra-shaped mock identity provider`

---

### Task 14: AgentServer host, auth, cards, and LLM seam

**Files:**
- Create: `samples/AgentServer/AgentServerApp.cs`, `Program.cs`, `Cards/AgentCards.cs`, `Auth/ApiKeyAuthenticationHandler.cs`, `Auth/AuthSetup.cs`, `Llm/FakeChatClient.cs`, `Llm/ChatClientRegistration.cs`, `appsettings.json`, `appsettings.Development.json`, `README.md`
- Create: `tests/A2A.Integration.Tests/Fixtures/A2ATestHost.cs` (starts MockEntra and AgentServer on test ports — WebhookReceiver is added in Task 17; `ICollectionFixture`; step delay 20 ms; push backoff 10 ms)
- Test: `tests/A2A.Integration.Tests/Flows/DiscoveryAndAuthTests.cs`

**Interfaces:**
- Consumes: Tasks 8–10 hosting, Task 13 mock Entra.
- Produces:
  - Config sections: `A2A` (`PublicBaseUrl`, `DefaultAgent` = `report`, `GrpcPorts`), `Auth` (`Authority` = `http://localhost:5100/a2a00000-0000-0000-0000-000000000001/v2.0`, `Audience` = `api://a2a-demo`, `TokenUrl`, `OpenIdConfigurationUrl`, `RequireHttpsMetadata` false in Development, `ApiKeys` map `{"dev": "dev-api-key"}`), `FakeAgents:StepDelay` (`00:00:00.5`), `Llm:Provider` (`Fake`).
  - Authentication: policy scheme `"A2A"` forwards to `"ApiKey"` when `X-API-Key` header is present, else `JwtBearer`. API key principal gets claim `a2a:apikey` = key name and role `A2A.Invoke`.
  - Policies: `"Invoke"` = authenticated + role `A2A.Invoke`; `"InvokeBearer"` = `"Invoke"` + scheme JwtBearer only.
  - `AgentCards.Build(string name, A2AServerOptions, AuthOptions, bool extended) : AgentCard` — security schemes from config: `"entra"` (oauth2 clientCredentials, `tokenUrl` from config, scopes `{"api://a2a-demo/.default": "Invoke A2A demo agents"}`), `"entraOidc"` (openIdConnect URL), `"apiKey"` (header `X-API-Key`).

  | Agent | Policy | securityRequirements | streaming | pushNotifications | extendedAgentCard | inputModes | extensions |
  |---|---|---|---|---|---|---|---|
  | echo | none | none | true | false | false | text/plain, application/json, `*/*` files | `https://example.com/a2a/ext/timestamp/v1` (required false) |
  | report | Invoke | entra OR apiKey | true | true | true | text/plain | — |
  | booking | Invoke | entra OR apiKey | true | true | false | text/plain | — |
  | secure | InvokeBearer | entra | true | true | true | text/plain | `https://example.com/a2a/ext/audit/v1` (required true) |

  - `ChatClientRegistration.AddDemoChatClient(IServiceCollection, IConfiguration)` — `Fake` → `FakeChatClient : Microsoft.Extensions.AI.IChatClient` (deterministic text from the prompt; streaming yields word chunks); `AzureOpenAI`/`OpenAI`/`Anthropic`/`Ollama` → throw at startup `NotSupportedException("LLM provider '<name>' is planned for Phase 2. Use Llm:Provider=Fake.")`.
  - Health endpoints `/health/live`, `/health/ready`.

- [ ] **Step 1: Failing tests** (`[Theory]` over `TransportMatrix.Bindings` where a call is made)

```
Root_card_is_report_agent
Card_security_matches_config                        // tokenUrl equals mock Entra token endpoint
Echo_allows_anonymous
Report_without_credentials_is_401
Report_with_noroles_token_is_403
Report_with_client_token_succeeds
Report_with_api_key_succeeds
Secure_rejects_api_key_with_403
Extended_card_requires_auth_and_adds_skills         // report: anonymous → 401; client token → card with extra skill "report-archive"
Echo_extended_card_not_configured                   // -32007
Mock_entra_down_gives_401_not_500                   // stop MockEntra before first token validation in a dedicated host instance
Unknown_llm_provider_fails_startup_with_phase2_message
```

- [ ] **Step 2: FAIL.** **Step 3: Implement** (agents registered as minimal stubs returning "not implemented" until Tasks 15–16). **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(samples): add agent server host with Entra and API key auth`

---

### Task 15: Echo and Report agents

**Files:**
- Create: `samples/AgentServer/Agents/EchoAgent.cs`, `Agents/ReportAgent.cs`
- Test: `tests/A2A.Integration.Tests/Flows/EchoFlowTests.cs`, `Flows/ReportFlowTests.cs`

**Interfaces:**
- Consumes: `IAgentHandler`, `IChatClient`, `FakeAgents:StepDelay`.
- Behavior:
  - **Echo**: replies with a direct `Message` (role agent, same `contextId`) whose parts mirror the input: text → `"echo: " + text`; file (raw/url) → same part; data → same JSON. If extension `https://example.com/a2a/ext/timestamp/v1` is active: `message.extensions = [uri]`, `message.metadata = {"https://example.com/a2a/ext/timestamp/v1": {"timestamp": "<ISO UTC>"}}`.
  - **Report** (topic = input text): `WORKING` ("Collecting data") → artifact `artifactId "report"`, `name "report.md"`, three `text/markdown` chunks (first `append:false`, then `append:true`, last `lastChunk:true`; text from `IChatClient`) → artifact `"summary"` data `{"topic": "...", "sections": 3}` → artifact `"data"` raw CSV bytes `mediaType "text/csv"`, `filename "data.csv"` → `COMPLETED`. `StepDelay` between steps. Input containing `fail` → `FAILED` with agent message `"Report generation failed for topic '<topic>'"`. Honors cancellation between steps.

- [ ] **Step 1: Failing tests** (all `[Theory]` over `TransportMatrix.Bindings`)

```
Echo_returns_direct_message_for_text_file_and_data_parts
Echo_timestamp_extension_adds_metadata_only_when_requested
Report_blocking_send_returns_completed_with_three_artifacts
Report_stream_emits_working_chunks_and_completed_in_order   // chunk append/lastChunk flags asserted
Report_return_immediately_then_get_task_until_completed
Report_list_tasks_filters_by_context_and_status
Report_cancel_mid_task_ends_canceled
Report_cancel_completed_task_not_cancelable
Report_fail_trigger_ends_failed
Report_rejects_image_input_with_content_type_not_supported
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(samples): add echo and report demo agents`

---

### Task 16: Booking and Secure agents, resubscribe

**Files:**
- Create: `samples/AgentServer/Agents/BookingAgent.cs`, `Agents/SecureAgent.cs`
- Test: `tests/A2A.Integration.Tests/Flows/BookingFlowTests.cs`, `Flows/SecureFlowTests.cs`, `Flows/ResubscribeTests.cs`

**Interfaces:**
- Behavior:
  - **Booking**: input `book <city>` (case-insensitive). Known cities `Paris`, `London`, `Tokyo`. Unknown city → `REJECTED` with message `"Sorry, we don't fly to <city>."`. Known → `INPUT_REQUIRED` with message `"Which date? (YYYY-MM-DD)"`. Follow-up message on same `taskId`: valid date → artifact `"booking"` data `{"city","date","confirmation":"BK-<6 uppercase hex>"}` → `COMPLETED`; invalid → `INPUT_REQUIRED` again with `"Please use YYYY-MM-DD."`. Input not matching `book <city>` → `INPUT_REQUIRED` `"Where to? Try 'book Paris'."`.
  - **Secure**: requires active audit extension (else `ExtensionSupportRequired` from Task 6 rules). Input `approve <id>` → `WORKING` → if caller lacks role `Reports.Approve` → `AUTH_REQUIRED` with message `"Role Reports.Approve required. Retry with an approver token."`; follow-up on same task from a caller with the role → artifact `"approval"` data `{"id","approvedBy":"<azp>"}` → `COMPLETED`. Extended card adds skill `"audit-log"`.

- [ ] **Step 1: Failing tests** (all `[Theory]` over bindings)

```
Booking_multi_turn_completes_on_same_task_and_context
Booking_invalid_date_asks_again
Booking_unknown_city_is_rejected
Secure_without_audit_extension_fails_extension_required
Secure_client_token_pauses_auth_required
Secure_approver_token_resumes_and_completes
Resubscribe_after_disconnect_receives_terminal_event     // Review Focus 1: start report stream, dispose after first artifact, SubscribeToTask → first event is task (WORKING), last is COMPLETED
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(samples): add booking and secure demo agents`

---

### Task 17: Webhook receiver

**Files:**
- Create: `samples/WebhookReceiver/*` (`WebhookReceiverApp.Build`, `NotificationStore`, `README.md`, appsettings)
- Modify: `tests/A2A.Integration.Tests/Fixtures/A2ATestHost.cs` (replace stub with real receiver)
- Test: `tests/A2A.Integration.Tests/Flows/PushFlowTests.cs`

**Interfaces:**
- Produces endpoints:
  - `POST /api/subscriptions` body `{"taskId":"...","token":"...","credentials":"..."}` — client registers what to expect (credentials are the Bearer value it gave the server). Returns 201.
  - `POST /webhook` — validates in order: `Authorization: Bearer <credentials>` matches a subscription; `X-A2A-Notification-Token` matches; `X-A2A-Push-Signature` JWT verifies against `{A2A server}/.well-known/a2a-push-jwks.json` (config `Receiver:TrustedServers`), `aud` = this webhook URL, `body_sha256` matches; `taskId` in payload matches the subscription. Any failure → 401 and nothing stored. Duplicate deliveries (same body hash) stored once.
  - `GET /api/notifications?taskId=...` → list of received StreamResponse JSON.
- [ ] **Step 1: Failing tests**

```
[Theory, bindings] Report_push_delivers_all_events_to_receiver   // last stored payload is statusUpdate COMPLETED
Receiver_rejects_bad_credentials_token_signature_and_task_mismatch
Receiver_deduplicates_redelivery
Push_config_crud_over_each_transport
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.**
- [ ] **Step 5: Commit** `feat(samples): add verifying webhook receiver`

---

### Task 18: Console client

**Files:**
- Create: `samples/AgentClient/Program.cs`, `FlowRunner.cs`, `Commands/*.cs`, `WireLogHandler.cs`, `appsettings.json`, `README.md`
- Test: `tests/A2A.Integration.Tests/Flows/ConsoleFlowRunnerTests.cs`

**Interfaces:**
- Consumes: `A2AClientFactory`, credential providers, receiver API.
- Produces:
  - Commands (System.CommandLine): `discover`, `send`, `stream`, `get`, `list`, `cancel`, `resubscribe`, `multi-turn`, `push`, `auth`, `extended-card`, `extensions`, `errors`, `all`.
  - Global options: `--server` (default `http://localhost:5200`), `--agent` (default per command), `--transport jsonrpc|rest|grpc|auto` (default `auto`), `--auth none|apikey|mock|entra` (default `mock`; `entra` uses `DefaultAzureCredential` + `--scope`), `--verbose` (prints every request/response body and headers with `Authorization` and `X-API-Key` values masked).
  - `FlowRunner` — one public method per command returning `Task<FlowResult>` (`record FlowResult(string Flow, bool Passed, IReadOnlyList<string> Steps)`); `all` runs every flow and exits non-zero if any fails. Program is a thin shell over `FlowRunner`.
- [ ] **Step 1: Failing tests**

```
[Theory, bindings] All_flows_pass_against_test_host(string binding)
Verbose_log_masks_secrets
```

- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS.** Then run the real apps: `./run-all.sh` is not ready yet, so start MockEntra, AgentServer, WebhookReceiver with `dotnet run --project ...` in three shells and run `dotnet run --project samples/AgentClient -- all --transport grpc` — expect every flow `PASS`.
- [ ] **Step 5: Commit** `feat(samples): add console client covering every flow`

---

### Task 19: Run scripts, Docker, Compose

**Files:**
- Create: `run-all.sh`, `run-all.ps1`, `docker/Dockerfile.mockentra`, `docker/Dockerfile.agentserver`, `docker/Dockerfile.webhook`, `docker/Dockerfile.client`, `docker-compose.yml`, `.env.example`, `.dockerignore`
- Test: `tests/A2A.Integration.Tests/Repo/ComposeFileTests.cs`

**Interfaces:**
- `run-all.sh` / `run-all.ps1`: build once, start MockEntra → wait for `/health/ready` → WebhookReceiver → AgentServer, print URLs, Ctrl+C stops all three.
- Dockerfiles: multi-stage (`mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`, `runtime:10.0` for client), non-root `USER app`, `ASPNETCORE_ENVIRONMENT=Development`.
- Compose: services `mock-entra` (5100), `webhook-receiver` (5300), `agent-server` (5200–5204), `client` (profile `tools`, `docker compose run --rm client all`), `ollama` (profile `ollama`, not wired to agents in Phase 1; comment says so). Healthchecks via `/health/ready`; `depends_on: condition: service_healthy`. In compose, `Auth:Authority` uses `http://mock-entra:5100/...` and `A2A:PublicBaseUrl` uses `http://agent-server:5200`; the mock issuer is configured to `http://mock-entra:5100` so tokens validate inside the network. `.env.example` documents Phase 2 keys (empty).
- [ ] **Step 1: Failing test** — `Compose_ports_match_documented_layout` parses `docker-compose.yml` text and asserts the port list and that `ASPNETCORE_ENVIRONMENT` is `Development` for `mock-entra`.
- [ ] **Step 2: FAIL.** **Step 3: Implement.** **Step 4: PASS**, then manual: `./run-all.sh` + `dotnet run --project samples/AgentClient -- all` → all PASS; `docker compose up -d --wait && docker compose run --rm client all` → all PASS (skip and note in commit if Docker is not installed).
- [ ] **Step 5: Commit** `feat: add run scripts and Docker Compose setup`

---

### Task 20: Documentation

**Files:**
- Create: `README.md`, `docs/flows.md`, `docs/production.md`; finish `README.md` in each `src/` and `samples/` project
- Test: `tests/A2A.Integration.Tests/Repo/DocsTests.cs`

**Content:**
- Root README: two-line A2A intro; quick start in three commands (`./run-all.sh`, `dotnet run --project samples/AgentClient -- all`, `dotnet test`); flow map table (flow → agent → console command → test class); transport and port table; mock identity values; "copy the server side" / "copy the client side" sections pointing at `src/A2A.Server` + `IAgentHandler`, `src/A2A.Client` + `A2AClientFactory`; Phase 1 limits (single instance, in-memory, mock auth, fake LLM); "Optional conformance" section: how to run the official A2A TCK against `http://localhost:5200` in CI (not part of `dotnet test`).
- `docs/flows.md`: one Mermaid sequence diagram per flow (discovery, send, stream, get/list, cancel, resubscribe, multi-turn, auth-required, push, extended card, extensions, errors).
- `docs/production.md`: every setting to change for Phase 2 (real Entra authority/audience/app registrations and role assignments, `Llm:Provider`, durable `ITaskStore`, TLS on HTTP and gRPC ports, push signing key source, `AllowPrivateNetworks=false`, OTLP), marked "Phase 2 — not implemented".
- [ ] **Step 1: Failing test** — `Every_console_command_and_flow_test_class_is_in_flow_map` (reads README; asserts each command name from `FlowRunner` and each `Flows/*Tests` class name appears) and `Relative_links_resolve`.
- [ ] **Step 2: FAIL.** **Step 3: Write docs.** **Step 4: PASS**, and full `dotnet test A2A.Reference.slnx` green with no network access beyond loopback.
- [ ] **Step 5: Commit** `docs: add README, flow guide and production notes`
