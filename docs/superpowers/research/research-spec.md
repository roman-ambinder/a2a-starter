# A2A spec research (as of 2026-11-03)

## 1. Versions since pin
- Pinned: a2a.proto at commit 4735f48 (main HEAD, committed 2026-10-30; fetched 2026-11-02). Vendored file is byte-identical to current main (812 lines, diff empty). https://github.com/a2aproject/A2A/commits/main
- Latest release: v1.0.1, 2026-05-28 (changelog dated 2026-05-26). Previous: v1.0.0 2026-03-12; v0.3.0 2025-07-30. https://api.github.com/repos/a2aproject/A2A/releases
- v1.0.1 fixes (https://github.com/a2aproject/A2A/releases/tag/v1.0.1): prefer `application/a2a+json` in HTTP binding (#1753); transcoding-related error changes (#1627); TaskStatus values in spec text (#1801).
- No release after v1.0.1. Pin is 92 commits ahead of v1.0.1, but almost all are docs/CI/partners. https://api.github.com/repos/a2aproject/A2A/compare/v1.0.1...main
- Only proto-relevant change after v1.0.1: comment fix for AgentInterface.url (gRPC address is "hostname:port", #1997, 2026-07-21). No wire change.
- Spec-text changes after v1.0.1: clarify in-task authorization scope semantics (#2081, sec 7 AUTH_REQUIRED); fix Agent Card security sample (#2046); rename docs PushNotificationConfig -> TaskPushNotificationConfig (#1981); JCS canonicalization example made a valid card (#2319); pagination field names in migration docs (#2165).
- Roadmap (updated 2026-09-15): v1.1 (task history/timeline, event filtering, messages during working state), BiDi streaming, A2A CLI, elicitation. https://github.com/a2aproject/A2A/blob/main/docs/roadmap.md ; epic https://github.com/a2aproject/A2A/issues/1991 (TaskMessageUpdateEvent #1811, historyMode #1183, SSE resubscribe replay question #1635, message timestamps).
- Open (not accepted/merged) PRs: #2310 sender attribution field on Message; #1891 use POST for SubscribeToTask binding (see compat note); #2378 serve AgentCard at agent root. Treat as proposals only.

## 2. Proto diff
- Pinned vs main: NO difference. Latest saved at /private/tmp/claude-501/-Users-romanambinder-IdeaProjects-a2a-eample/2271cc8f-dd07-48e2-b1d4-8f263d1890a5/scratchpad/latest-a2a.proto
- Pinned vs v1.0.1 tag: only the AgentInterface.url comment (gRPC example "grpc.example.com:443").
- v1.0.0 -> v1.0.1 (saved as tag-v1.0.0.proto / tag-v1.0.1.proto in scratchpad): comment-only. AgentInterface.tenant doc now: opaque routing id; clients MUST put it in `tenant` of all requests; every request `tenant` field "must match the AgentInterface tenant when set".
- Package is lf.a2a.v1 (java_package com.google.lf.a2a.v1). AgentInterface has protocol_version (REQUIRED).
- So no missing messages/fields/RPCs vs main. Gaps would be v1.1 proposals only.

## 3. Easy-to-miss normative requirements (source: https://github.com/a2aproject/A2A/blob/main/docs/specification.md, section numbers from it)
Versioning (3.6)
- Client MUST send `A2A-Version: Major.Minor` on each request (may use query param `A2A-Version`). Empty header MUST be treated as 0.3 by server. Server MUST process using requested Major.Minor; unsupported -> VersionNotSupportedError. Patch not used in negotiation. SDKs MUST provide version/transport negotiation help.
Errors (3.3, 5.4)
- Every error MUST carry code/message/optional details; details entries MUST have `@type`. A2A-specific errors MUST include google.rpc.ErrorInfo (gRPC status.details and REST details, since several errors share HTTP 400).
- Mapping: TaskNotFound -32001/NOT_FOUND/404; TaskNotCancelable -32002/FAILED_PRECONDITION/400; PushNotificationNotSupported -32003/FAILED_PRECONDITION/400; UnsupportedOperation -32004/FAILED_PRECONDITION/400; ContentTypeNotSupported -32005/INVALID_ARGUMENT/400; InvalidAgentResponse -32006/INTERNAL/500; ExtendedAgentCardNotConfigured -32007/FAILED_PRECONDITION/400; ExtensionSupportRequired -32008/FAILED_PRECONDITION/400; VersionNotSupported -32009/FAILED_PRECONDITION/400.
- Capability gating MUST: pushNotifications false -> PushNotificationNotSupportedError on all 4 push ops; streaming false -> UnsupportedOperationError for SendStreamingMessage/SubscribeToTask; extendedAgentCard false -> UnsupportedOperationError; declared but unset -> ExtendedAgentCardNotConfiguredError.
- MUST NOT reveal existence of unauthorized resources; SHOULD NOT distinguish not-found vs not-authorized.
Tasks/contexts (3.4)
- Server MUST generate unique taskId; message with taskId MUST reference existing task else TaskNotFoundError; if only taskId given infer contextId; mismatching contextId/taskId MUST be rejected; unacceptable client contextId MUST be rejected (never silently replaced); new contextId MUST be returned.
- Cancel idempotent; duplicate cancel MAY give TaskNotFound if purged. Delete push config MUST be idempotent. SendMessage MAY dedupe by messageId.
SendMessage
- Blocking (return_immediately false/unset) MUST wait until terminal or interrupted (INPUT_REQUIRED/AUTH_REQUIRED); return_immediately true MUST return at once. History length: 0 = omit history; unset = no limit.
Streaming (3.1.2, 3.1.6, 3.5)
- Stream is either exactly one Message then close, or Task first then zero+ status/artifact events, closing at terminal/interrupted state. SubscribeToTask MUST send current Task as first event; MUST terminate at terminal state; subscribing to a terminal task is an error (UnsupportedOperation). Events MUST be delivered in order, MUST go to all active streams for the task; closing one stream MUST NOT affect others. SSE content-type text/event-stream. No replay of missed events is defined (open issue #1635).
- Messages are not reliable delivery; do not put outputs in Messages (use Artifacts).
ListTasks (3.1.4)
- MUST return only tasks visible to caller; cursor pagination; sort by status timestamp DESC; `nextPageToken` MUST always be present ("" on last page); includeArtifacts false (default) -> artifacts MUST be omitted entirely; authz scoping MUST apply even without contextId filter and BEFORE any lookup that leaks existence.
Push notifications (4.3, 7)
- Agent MUST attempt delivery at least once; payload is StreamResponse; MUST send configured authentication credentials; SHOULD retry w/ exponential backoff, 10-30 s timeout, SHOULD validate webhook URLs against SSRF. Client MUST validate webhook authenticity, return 2xx, validate task id matches an expected task; SHOULD be idempotent, HTTPS.
Extended card (3.1.11, 7)
- Operation MUST require authentication using schemes declared in public card; MUST validate permissions; clients SHOULD replace cached public card with extended card for the session; SHOULD NOT include internal secrets.
Agent card (8)
- MUST be available; well-known path `/.well-known/agent-card.json` (IANA appendix 14.3). SHOULD list supportedInterfaces in preference order; client MUST pick first supported, use that interface URL, and MUST set `tenant` in every request message exactly to the interface's tenant (omit if unset).
- Caching: SHOULD send Cache-Control max-age and ETag; clients SHOULD honor RFC 9111 and use If-None-Match.
- Signing (8.4): payload MUST be canonicalized with RFC 8785 JCS after removing default-valued fields (keep REQUIRED fields and explicitly-set `optional` fields; omit empty repeated, drop `signatures` field itself). JWS protected header MUST have alg, kid; typ SHOULD be "JOSE"; MAY have jku. Expired/revoked keys MUST NOT be used. Clients SHOULD verify when present.
Extensions (4.6, 3.2.6)
- `A2A-Extensions` header (HTTP), metadata key `a2a-extensions` (gRPC), comma-separated URIs. If agent card extension has required:true and client does not declare it -> ExtensionSupportRequiredError. Unsupported-version extension requested: agent SHOULD ignore unless required. Breaking extension change MUST use new URI.
Serialization (5)
- JSON MUST be camelCase; enums as ProtoJSON string names (TASK_STATE_*, ROLE_*); timestamps ISO 8601 UTC with Z only, ms precision SHOULD; REQUIRED fields MUST be present; ignore unknown fields (SHOULD). Content type `application/a2a+json` SHOULD be used for REST (v1.0.1).
Bindings
- Service parameters as HTTP headers / gRPC metadata (same names, lowercase in gRPC). REST: GET/DELETE use camelCase query params; nested objects not allowed in query; URL paths have no /v1 prefix (e.g. POST /message:send, GET /tasks/{id}, tenant prefix /{tenant}/...). JSON-RPC methods PascalCase (SendMessage, SendStreamingMessage, GetTask, ListTasks, CancelTask, SubscribeToTask, Create/Get/List/DeleteTaskPushNotificationConfig, GetExtendedAgentCard).
In-task auth (7)
- AUTH_REQUIRED: agent MUST use a Task, MUST set state, MUST include status message (unless negotiated); credentials out-of-band; AUTH_REQUIRED state alone MUST NOT be treated as authorization; credential does not authorize later messages unless defined (#2081).
Security
- TLS in production MUST; authenticate every request MUST; authz on every operation MUST; SHOULD rate-limit; logs MUST NOT contain secrets.

## 4. TCK / conformance
- a2aproject/a2a-tck https://github.com/a2aproject/a2a-tck : version 1.0.0 (pyproject), last push 2026-10-22; no GitHub releases. Python 3.11+, uv. Run: `uv venv; uv pip install -e .; ./run_tck.py --sut-host http://localhost:9999 [--transport grpc|jsonrpc|http_json] [--level must|should|may]`. Fetches `{host}/.well-known/agent-card.json`, tests every transport in supportedInterfaces. MUST = hard fail, SHOULD = xfail, MAY = skipped if capability undeclared. Reports in reports/ (compatibility.json/html, junit). Gherkin scenarios in scenarios/, SUT codegen (a2a-java target). https://raw.githubusercontent.com/a2aproject/a2a-tck/main/README.md
- Also a2aproject/a2a-itk (Integration Testing Kit for A2A SDKs, pushed 2026-10-26) https://github.com/a2aproject/a2a-itk ; a2a-inspector for manual checks; a2a-cli exists https://github.com/a2aproject/a2a-cli. Not verified in depth.

## 5. Compatibility / renames (0.3 -> 1.0)
Source: https://github.com/a2aproject/A2A/blob/main/docs/whats-new-v1.md and spec Appendix A.
- Methods: message/send -> SendMessage, message/stream -> SendStreamingMessage, tasks/get -> GetTask, tasks/resubscribe -> SubscribeToTask, agent/getAuthenticatedExtendedCard -> GetExtendedAgentCard; tasks/pushNotificationConfig/set -> CreateTaskPushNotificationConfig.
- Type renames: MessageSendParams->SendMessageRequest; SendMessageSuccessResponse->SendMessageResponse; SendStreamingMessageSuccessResponse->StreamResponse; ListTaskPushNotificationConfigSuccessResponse->ListTaskPushNotificationConfigsResponse; PushNotificationConfig->TaskPushNotificationConfig. Removal not before 0.5.0 per appendix (moot; 1.0 shipped). Spec says SDKs SHOULD keep deprecated aliases and removal not before next major.
- Enums: lowercase/kebab ("input-required") -> TASK_STATE_INPUT_REQUIRED; role user/agent -> ROLE_USER/ROLE_AGENT.
- Part unified (no kind discriminator; oneof text/raw/url/data); stream events use wrapper discriminator (StreamResponse oneof) not `kind`; Agent card: url/preferredTransport/additionalInterfaces -> supportedInterfaces[] with protocolVersion per interface; IDs are plain (no "tasks/{id}" compound names); pagination changed; errors via google.rpc.Status/ErrorInfo; REST drops /v1 prefix; tenant added.
- 0.3 peers: a client with empty A2A-Version is 0.3; server must serve 0.3 semantics if it advertises a 0.3 interface. Agent can expose multiple interfaces with different versions. An SDK that wants 0.3 interop needs a separate adapter (per whats-new section "Backward Compatibility Strategy #1401").
- Watch: PR #1891 may change SubscribeToTask REST verb to POST (currently GET `/tasks/{id}:subscribe`); unmerged.

## Sources
- https://api.github.com/repos/a2aproject/A2A/releases ; /commits ; /compare/v1.0.1...main
- https://raw.githubusercontent.com/a2aproject/A2A/main/specification/a2a.proto (and tags v1.0.0, v1.0.1)
- https://raw.githubusercontent.com/a2aproject/A2A/main/docs/specification.md
- https://raw.githubusercontent.com/a2aproject/A2A/main/docs/whats-new-v1.md
- https://raw.githubusercontent.com/a2aproject/A2A/main/docs/roadmap.md
- https://github.com/a2aproject/A2A/issues/1991
- https://github.com/a2aproject/a2a-tck , https://github.com/a2aproject/a2a-itk
Caveat: spec site (a2a-protocol.org) not fetched directly; the repo docs are its source.
