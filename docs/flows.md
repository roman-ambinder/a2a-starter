# Flows

One sequence diagram per flow. Each flow lists where the agent code is, the console command and the test.

Shorthand: `Client` is `samples/AgentClient` (library `src/A2A.Client`), `Server` is `samples/AgentServer` (library `src/A2A.Server`). Every request carries `A2A-Version: 1.0` unless noted. Run any command with `dotnet run --project samples/AgentClient -- <command>` after `./run-all.sh`. Back to the [README](../README.md).

The task manager is `src/A2A.Server/Tasks/TaskManager.cs`. The three transports are in `src/A2A.Server/Transports/` (`JsonRpc`, `Rest`, `Grpc`). Client flows are in `samples/AgentClient/FlowRunner.Basics.cs` and `FlowRunner.Advanced.cs`.

## 1. Discovery

Cards are public and need no version header or credentials. The root card is the `report` agent.

```mermaid
sequenceDiagram
    participant C as Client
    participant S as AgentServer :5200
    C->>S: GET /agents/{name}/.well-known/agent-card.json
    S-->>C: AgentCard (supportedInterfaces JSONRPC, HTTP+JSON, GRPC, skills, securitySchemes)
    Note over C: A2ACardResolver caches the card for 5 minutes
    Note over C: A2AClientFactory picks a binding from the card
```

- Agent code: `samples/AgentServer/Cards/AgentCards.cs`
- Command: `discover`
- Tests: `DiscoveryAndAuthTests` (`Cards_match_agent_table`, `Card_security_matches_config`)

## 2. Send (blocking)

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server (report agent)
    C->>S: SendMessage "Q3 sales"
    S->>S: create task, run ReportAgent
    Note over S: WORKING, report.md chunks, summary, data.csv
    S-->>C: Task COMPLETED with 3 artifacts
```

The call waits for a terminal state (`COMPLETED`, `FAILED`, `CANCELED`, `REJECTED`) or an interrupted state (`INPUT_REQUIRED`, `AUTH_REQUIRED`). The `echo` agent answers with a single `Message` and no task instead.

- Agent code: `samples/AgentServer/Agents/ReportAgent.cs`, `EchoAgent.cs`
- Command: `send`
- Tests: `ReportFlowTests.Report_blocking_send_returns_completed_with_three_artifacts`, `EchoFlowTests`

## 3. Stream

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: SendStreamingMessage (SSE, or server-streaming gRPC)
    S-->>C: Task (first event, state SUBMITTED or WORKING)
    S-->>C: StatusUpdate WORKING
    S-->>C: ArtifactUpdate report.md chunk 1, 2, 3 (append, lastChunk)
    S-->>C: ArtifactUpdate summary, data.csv
    S-->>C: StatusUpdate COMPLETED (final)
    Note over C,S: stream closes after a terminal or interrupted state
```

- Agent code: `ReportAgent.cs` (`sink.AddArtifactAsync(..., append, lastChunk, ct)`), SSE in `src/A2A.Server/Transports/Sse/`
- Command: `stream`
- Test: `ReportFlowTests.Report_stream_emits_working_chunks_and_completed_in_order`

## 4. Get and list

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: SendMessage returnImmediately=true
    S-->>C: Task (not finished)
    loop until terminal
        C->>S: GetTask id, historyLength
        S-->>C: Task
    end
    C->>S: ListTasks contextId, status, pageSize=1
    S-->>C: tasks[1], nextPageToken
    C->>S: ListTasks pageToken
    S-->>C: next page
```

`historyLength` 0 returns no history, unset returns all. `ListTasks` sorts by status timestamp (newest first), defaults to 50 per page (1 to 100) and only returns the caller's tasks.

- Code: `src/A2A.Server/Tasks/TaskManager.cs`, `InMemoryTaskStore.cs` (`ITaskStore.ListAsync`)
- Commands: `get`, `list`
- Tests: `ReportFlowTests.Report_return_immediately_then_get_task_until_completed`, `Report_list_tasks_filters_by_context_and_status`

## 5. Cancel

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: SendMessage returnImmediately=true
    S-->>C: Task WORKING
    C->>S: CancelTask id
    S->>S: cancel the agent token, IAgentHandler.CancelAsync
    S-->>C: Task CANCELED
    C->>S: CancelTask id again
    S-->>C: error TaskNotCancelable
```

A cancel on a finished task returns `TaskNotCancelable`. A fast agent may finish first; the console flow reports that, it is not a failure.

- Agent code: `ReportAgent.cs` (stops quietly on cancel), default `CancelAsync` in `src/A2A.Server/Agents/IAgentHandler.cs`
- Command: `cancel`
- Tests: `ReportCancelFlowTests.Report_cancel_mid_task_ends_canceled`, `ReportFlowTests.Report_cancel_completed_task_not_cancelable`

## 6. Resubscribe

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: SendStreamingMessage
    S-->>C: Task, first artifact chunk
    Note over C: client drops the connection
    S->>S: task keeps running
    C->>S: SubscribeToTask id
    S-->>C: Task snapshot (current state)
    S-->>C: remaining updates until COMPLETED
    C->>S: GetTask id
    S-->>C: Task COMPLETED with 3 artifacts
```

Subscribing to a task that is already terminal is refused with `UnsupportedOperation`.

- Code: `src/A2A.Server/Tasks/TaskEventBroker.cs`, `TaskManager.cs`
- Command: `resubscribe`
- Test: `ResubscribeTests.Resubscribe_after_disconnect_receives_terminal_event`

## 7. Multi-turn

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server (booking agent)
    C->>S: SendMessage "Book Paris"
    S-->>C: Task INPUT_REQUIRED "what date?"
    C->>S: SendMessage "2027-13-45" (taskId, contextId)
    S-->>C: Task INPUT_REQUIRED (invalid date, asks again)
    C->>S: SendMessage "2027-03-15" (same taskId, contextId)
    S-->>C: Task COMPLETED with booking artifact
```

The agent object lives for one execution only, so `BookingAgent` reads the city back from the stored task history.

- Agent code: `samples/AgentServer/Agents/BookingAgent.cs`
- Command: `multi-turn`
- Test: `BookingFlowTests.Booking_multi_turn_completes_on_same_task_and_context`

## 8. Auth-required step-up

```mermaid
sequenceDiagram
    participant W as Client (token a2a-client)
    participant A as Client (token a2a-approver)
    participant M as MockEntra :5100
    participant S as Server (secure agent)
    W->>M: client_credentials
    M-->>W: JWT roles A2A.Invoke
    W->>S: SendMessage "approve r-100" + A2A-Extensions audit
    S-->>W: Task AUTH_REQUIRED (needs Reports.Approve)
    A->>M: client_credentials
    M-->>A: JWT roles A2A.Invoke, Reports.Approve
    A->>S: SendMessage (same taskId)
    S-->>A: Task COMPLETED with approval artifact
    Note over S: no credentials gives HTTP 401, a2a-noroles gives 403, API key on secure gives 403
```

- Agent code: `samples/AgentServer/Agents/SecureAgent.cs`, policies in `samples/AgentServer/Auth/AuthSetup.cs`
- Command: `auth`
- Tests: `SecureFlowTests.Secure_client_token_pauses_auth_required`, `Secure_approver_token_resumes_and_completes`, `DiscoveryAndAuthTests` (401 and 403 cases)

## 9. Push notifications

```mermaid
sequenceDiagram
    participant C as Client
    participant R as WebhookReceiver :5300
    participant S as Server (report agent)
    C->>R: POST /api/subscriptions taskId *, token, credentials
    C->>S: SendMessage with push config (url, token, Bearer credentials)
    S-->>C: Task
    loop each task event
        S->>R: POST /webhook (StreamResponse JSON)
        Note right of S: Authorization Bearer, X-A2A-Notification-Token, X-A2A-Push-Signature (JWT)
        R->>S: GET /.well-known/a2a-push-jwks.json (cached)
        R-->>S: 200 stored
    end
    C->>R: GET /api/notifications?taskId=
    R-->>C: received events
    C->>S: Create, Get, List, Delete push config
```

The receiver checks credentials, token, JWT signature (`aud`, `body_sha256`, `task_id`) and lifetime. A failed check returns 401 and stores nothing. See the [WebhookReceiver README](../samples/WebhookReceiver/README.md).

- Code: `src/A2A.Server/Push/PushNotificationSender.cs`, `PushSigningKey.cs`, `samples/WebhookReceiver/`
- Command: `push`
- Tests: `PushFlowTests.Report_push_delivers_all_events_to_receiver`, `Push_config_crud_over_each_transport`, `ReceiverVerificationTests`

## 10. Extended card

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server (report agent)
    C->>S: GetExtendedAgentCard (no credentials)
    S-->>C: HTTP 401
    C->>S: GetExtendedAgentCard (token or API key)
    S-->>C: AgentCard with extra skill report-archive
    C->>S: GetExtendedAgentCard on echo agent
    S-->>C: error ExtendedAgentCardNotConfigured
```

- Code: `samples/AgentServer/Cards/AgentCards.cs` (`HasExtendedCard`), `src/A2A.Server/Hosting/AgentCardEndpoints.cs`
- Command: `extended-card`
- Tests: `DiscoveryAndAuthTests.Extended_card_requires_auth_and_adds_skills`, `Echo_extended_card_not_configured`

## 11. Extensions

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: SendMessage to echo (no extension)
    S-->>C: Message "echo: hi" without extensions
    C->>S: SendMessage to echo + A2A-Extensions timestamp
    S-->>C: Message with metadata timestamp, extensions lists the URI
    C->>S: SendMessage to secure without audit extension
    S-->>C: error ExtensionSupportRequired
    C->>S: SendMessage to secure + A2A-Extensions audit
    S-->>C: Task accepted
```

Only extensions that the card declares become active (`AgentRequestContext.ActiveExtensions`). An extension marked `required` on the card must be requested.

- Agent code: `samples/AgentServer/Agents/EchoAgent.cs`, `SecureAgent.cs`
- Command: `extensions`
- Tests: `EchoFlowTests.Echo_timestamp_extension_adds_metadata_only_when_requested`, `SecureFlowTests.Secure_without_audit_extension_fails_extension_required`

## 12. Errors

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    C->>S: GetTask unknown id
    S-->>C: TaskNotFound (-32001, HTTP 404, gRPC NOT_FOUND)
    C->>S: CancelTask on finished task
    S-->>C: TaskNotCancelable (-32002)
    C->>S: any request with A2A-Version 0.3
    S-->>C: VersionNotSupported (-32009)
    C->>S: image part sent to report agent
    S-->>C: ContentTypeNotSupported (-32005)
    C->>S: secure agent without audit extension
    S-->>C: ExtensionSupportRequired (-32008)
    C->>S: GetExtendedAgentCard on echo
    S-->>C: ExtendedAgentCardNotConfigured (-32007)
```

The same error maps to a JSON-RPC code, an HTTP status and a gRPC status with `ErrorInfo` (domain `a2a-protocol.org`). Auth failures are not A2A errors: they are HTTP 401 or 403 (gRPC `UNAUTHENTICATED` or `PERMISSION_DENIED`). The client throws `A2AException` for protocol errors and `A2AAuthException` for 401 and 403.

- Code: `src/A2A.Protocol/Errors/`, `src/A2A.Server/Transports/TransportErrors.cs`, `Grpc/GrpcErrorMapper.cs`, `Rest/RestErrorWriter.cs`
- Command: `errors`
- Tests: `ReportFlowTests` (`Report_rejects_image_input_with_content_type_not_supported`), `SecureFlowTests`, `EchoFlowTests`, and all of them together in `ConsoleFlowRunnerTests.All_flows_pass_against_test_host`
