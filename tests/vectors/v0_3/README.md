# A2A 0.3 test vectors

JSON fixtures for the 0.3 compatibility packages. The reference is the vendored v0.3.0 files in
`src/Compat.V0_3.Shared/spec/v0.3.0/` (`specification.md`, `a2a.json`, `a2a.proto`); `inventory.json` in this folder is the
checklist of names, codes and paths extracted from them. Maintainers' notes: "A2A 0.3 compatibility" in `docs/spec-decisions.md`.

JSON has no comments, so provenance is kept here, not in the files. Every fixture must have a row in the tables below
(`FixtureParseTests` checks that). Line numbers are lines of `specification.md` where the code block opens (the fence line).

## Layout

| Folder | Holds |
|---|---|
| `requests/` | JSON-RPC request envelopes (`jsonrpc`, `id`, `method`, `params`) |
| `responses/` | JSON-RPC success envelopes with a `result` (Task or Message) |
| `events/` | JSON-RPC envelopes of stream events, one per SSE `data:` frame (`result` is a Task, status-update or artifact-update) |
| `cards/` | Bare Agent Cards |
| `push/` | Push notification webhook bodies (bare Task JSON) |
| `errors/` | JSON-RPC error envelopes (with `id`) and the REST problem-details body |
| `captured/` | Real peer captures; empty until Task 23 (see `captured/README.md`) |

## Conventions

- Spec examples are copied with their JSON values unchanged. Whitespace is normalized (2-space indent) and the members keep the spec's order.
- Where a spec example is not valid JSON (comments, a trailing comma, `...` in a value) the minimal fix is applied and the Notes column says what changed.
- The spec's own examples are not all schema-valid. The history messages in 9.2 and 9.4 have no `kind`; the models accept that and write `kind` back, which `FixtureParseTests` allows.
- The SSE example in 9.3 is split into one file per `data:` frame; the `data: ` prefix is dropped.
- The spec has more code blocks (`ts`, `proto`, `typescript`, `none`) in sections 3 to 8. They are type or URL snippets, not JSON examples, and are not fixtures. The `GET .../agent-card.json` block in 9.1 is a URL, not JSON.

## Known issue: file parts in specification.md 9.2/9.3

Sections 9.2/9.3 show a file part as `{"kind": "file", "file": {"mimeType": "image/png", "data": "<base64-encoded-content>"}}`.
`a2a.json` `FileWithBytes` has `bytes` (and `FileWithUri` has `uri`); there is no `data` member on a file. The spec text and the schema disagree,
and the schema is the source of truth (plan, "Source of truth").

- The spec example is kept as published in the `*.spec-verbatim.json` fixtures. The current models reject them with `JsonException`
  (a file with neither `bytes` nor `uri`), and `FixtureParseTests` asserts exactly that. If the reader is ever made tolerant of `data`, those assertions must change together with the docs.
- Each has a corrected sibling with the same name minus `.spec-verbatim`, using the a2a.json shape (`bytes`, and the placeholder replaced by valid base64).
- Section 9.6 (upload) uses `bytes` correctly.

## Known issue: misplaced messageId in specification.md 9.4

The first 9.4 request puts `"messageId"` in `params`, beside `message`, instead of inside it. a2a.json `Message` requires `messageId` inside the message. The example is kept in
`requests/message-send.multi-turn.request-1.spec-verbatim.json`; the models do not throw on it, they drop the stray member and leave `Message.MessageId` null (the C# property is a non-nullable string) (asserted by `FixtureParseTests`).
The corrected sibling moves it into `message`. This file is a `spec-verbatim` fixture that does not throw; it is the one documented exception to the file-part rule above.

## Note: results without kind

The 9.5 submitted-task response (`responses/message-send.push-config.submitted.json`) has no `kind` on its Task `result`, though a2a.json requires it. The models read it as a Task, and the writer adds `kind`.

### File to source: spec examples and their corrected siblings

| File | Source | Notes |
|---|---|---|
| `cards/agent-card.sample.json` | specification.md 5.7 Sample Agent Card, line 415 | verbatim (whitespace normalized) |
| `events/message-stream.artifact-update.chunk-1.json` | specification.md 9.3 Streaming Task Execution (SSE), response frames, line 1370 | SSE `data:` frame payload; verbatim (whitespace normalized) |
| `events/message-stream.artifact-update.chunk-2.json` | specification.md 9.3 Streaming Task Execution (SSE), response frames, line 1370 | SSE `data:` frame payload; changed: trailing comma after `parts` removed (not valid JSON in the spec) |
| `events/message-stream.artifact-update.chunk-3-last.json` | specification.md 9.3 Streaming Task Execution (SSE), response frames, line 1370 | SSE `data:` frame payload; verbatim (whitespace normalized) |
| `events/message-stream.status-update.final.json` | specification.md 9.3 Streaming Task Execution (SSE), response frames, line 1370 | SSE `data:` frame payload; verbatim (whitespace normalized) |
| `events/message-stream.task-submitted.json` | derived from events/message-stream.task-submitted.spec-verbatim.json (9.3, line 1370) | changed: `data` renamed `bytes`; placeholder replaced by valid base64 `aGVsbG8=` |
| `events/message-stream.task-submitted.spec-verbatim.json` | specification.md 9.3 Streaming Task Execution (SSE), response frames, line 1370 | SSE `data:` frame payload; verbatim (whitespace normalized); KNOWN ISSUE: history file part uses {"mimeType","data"}, not a2a.json `bytes`; the model rejects it with JsonException |
| `push/task-completed.json` | specification.md 9.5 Push Notification Setup, webhook body (Task as JSON), line 1717 | changed: JSON comments removed (not valid JSON in the spec); `// ... other fields ...` is elision, removed with the comment |
| `requests/agent-get-authenticated-extended-card.json` | specification.md 9.1 Fetching Authenticated Extended Agent Card, line 1201 | verbatim (whitespace normalized) |
| `requests/message-send.basic.json` | specification.md 9.2 Basic Execution (task result variant), request, line 1219 | verbatim (whitespace normalized) |
| `requests/message-send.basic.message-variant.json` | specification.md 9.2 Basic Execution (message result variant), request, line 1290 | same JSON as requests/message-send.basic.json; the spec repeats it |
| `requests/message-send.file-upload.json` | specification.md 9.6 File Exchange, upload request, line 1740 | changed: JSON comments removed (not valid JSON in the spec); changed: `...` in the base64 `bytes` value removed so it decodes (truncated PNG header, not a real image) |
| `requests/message-send.multi-turn.request-1.json` | derived from requests/message-send.multi-turn.request-1.spec-verbatim.json (9.4, line 1486) | changed: `messageId` moved from `params` into `params.message` |
| `requests/message-send.multi-turn.request-1.spec-verbatim.json` | specification.md 9.4 Multi-Turn Interaction, first request, line 1486 | KNOWN ISSUE: `messageId` sits in `params` next to `message`, not inside `message` where a2a.json Message requires it; the reader ignores the stray member and the message gets a null messageId (no exception) |
| `requests/message-send.multi-turn.request-2.json` | specification.md 9.4 Multi-Turn Interaction, second request (blocking true), line 1547 | verbatim (whitespace normalized) |
| `requests/message-send.push-config.json` | specification.md 9.5 Push Notification Setup, request, line 1662 | changed: JSON comments removed (not valid JSON in the spec) |
| `requests/message-send.structured-data.json` | specification.md 9.7 Structured Data Exchange, request, line 1809 | verbatim (whitespace normalized) |
| `requests/message-stream.file-part.json` | derived from requests/message-stream.file-part.spec-verbatim.json (9.3, line 1338) | changed: `data` renamed `bytes` (a2a.json FileWithBytes); placeholder `<base64-encoded-content>` replaced by valid base64 `aGVsbG8=` |
| `requests/message-stream.file-part.spec-verbatim.json` | specification.md 9.3 Streaming Task Execution (SSE), request, line 1338 | verbatim; KNOWN ISSUE: file part uses {"mimeType","data"} where a2a.json FileWithBytes uses `bytes`; the model rejects it with JsonException |
| `responses/message-send.basic.message-result.json` | specification.md 9.2 Basic Execution, message response, line 1313 | verbatim (whitespace normalized) |
| `responses/message-send.basic.task-result.json` | specification.md 9.2 Basic Execution, task response, line 1242 | verbatim (whitespace normalized) |
| `responses/message-send.file-download.json` | specification.md 9.6 File Exchange, download response, line 1770 | changed: JSON comments removed (not valid JSON in the spec); the commented-out `bytes` alternative is dropped with the comments; the `uri` form is kept |
| `responses/message-send.multi-turn.completed.json` | specification.md 9.4 Multi-Turn Interaction, completed task response, line 1574 | verbatim (whitespace normalized) |
| `responses/message-send.multi-turn.input-required.json` | specification.md 9.4 Multi-Turn Interaction, input-required task response, line 1503 | verbatim (whitespace normalized) |
| `responses/message-send.push-config.submitted.json` | specification.md 9.5 Push Notification Setup, response, line 1696 | changed: JSON comments removed (not valid JSON in the spec); `// ... other fields ...` is elision, removed with the comment |
| `responses/message-send.structured-data.json` | specification.md 9.7 Structured Data Exchange, response, line 1845 | verbatim (whitespace normalized) |

### File to source: hand-written edge cases

| File | Source | Notes |
|---|---|---|
| `cards/agent-card.additional-interfaces-security.json` | hand-written, shape from a2a.json AgentCard / AgentInterface / SecurityScheme | card with additionalInterfaces (all three transports), every security scheme type, card and skill `security`, extension, signature |
| `cards/agent-card.minimal.json` | hand-written, shape from a2a.json AgentCard (required members only) | required members only; `preferredTransport` omitted (schema default JSONRPC) |
| `errors/jsonrpc-error.32001-task-not-found.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32001) | message text is the a2a.json default; data member present |
| `errors/jsonrpc-error.32002-task-not-cancelable.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32002) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32003-push-notification-not-supported.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32003) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32004-unsupported-operation.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32004) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32005-content-type-not-supported.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32005) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32006-invalid-agent-response.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32006) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32007-authenticated-extended-card-not-configured.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32007) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32600-invalid-request.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32600) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32601-method-not-found.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32601) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32602-invalid-params.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32602) | message text is the a2a.json default; data member present |
| `errors/jsonrpc-error.32603-internal.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32603) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32700-json-parse.json` | hand-written, shape from a2a.json JSONRPCErrorResponse / JSONRPCError (-32700) | message text is the a2a.json default; data member omitted |
| `errors/jsonrpc-error.32700-json-parse.null-id.json` | hand-written, shape from a2a.json JSONRPCErrorResponse (id null) | parse error with `id: null`, because the request id could not be read |
| `events/artifact-update.append-false.last-chunk-true.json` | hand-written, shape from a2a.json TaskArtifactUpdateEvent | append false, lastChunk true (whole artifact in one event) |
| `events/artifact-update.append-true.last-chunk-false.json` | hand-written, shape from a2a.json TaskArtifactUpdateEvent | append true, lastChunk false |
| `events/artifact-update.append-true.last-chunk-true.json` | hand-written, shape from a2a.json TaskArtifactUpdateEvent | append true, lastChunk true |
| `events/artifact-update.no-flags.json` | hand-written, shape from a2a.json TaskArtifactUpdateEvent | append and lastChunk omitted (both optional in a2a.json) |
| `events/status-update.final-false.json` | hand-written, shape from a2a.json TaskStatusUpdateEvent | status update, final false |
| `events/status-update.final-true.json` | hand-written, shape from a2a.json TaskStatusUpdateEvent | status update, final true |
| `push/task-completed.with-artifact.json` | hand-written, shape from a2a.json Task | webhook body: a bare Task with kind `task` (no JSON-RPC envelope), see docs/spec-decisions.md "Push notification body and headers" |
| `responses/message-send.message-data-part.json` | hand-written, shape from a2a.json Message / DataPart | data part with nested object, array, bool, number, null and part metadata |
| `responses/message-send.message-file-part-bytes.json` | hand-written, shape from a2a.json Message / FilePart / FileWithBytes / PartBase.metadata | file part with name, mimeType, bytes and part metadata |
| `responses/message-send.message-file-part-uri.json` | hand-written, shape from a2a.json Message / FilePart / FileWithUri / PartBase.metadata | file part with name, mimeType, uri and part metadata; message with referenceTaskIds, extensions, metadata |
| `responses/tasks-get.task-state-auth-required.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `auth-required` |
| `responses/tasks-get.task-state-canceled.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `canceled` |
| `responses/tasks-get.task-state-completed.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `completed` |
| `responses/tasks-get.task-state-failed.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `failed` |
| `responses/tasks-get.task-state-input-required.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `input-required` |
| `responses/tasks-get.task-state-rejected.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `rejected` |
| `responses/tasks-get.task-state-submitted.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `submitted` |
| `responses/tasks-get.task-state-unknown.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `unknown` |
| `responses/tasks-get.task-state-working.json` | hand-written, shape from a2a.json Task / TaskStatus / TaskState | task in state `working` |

### File to source: REST error body

| File | Source | Notes |
|---|---|---|
| `errors/rest-problem-details.task-not-found.json` | RFC 9457 problem details, as used by V03ProblemDetails. NOT from the vendored files | shape not from the spec: specification.md, a2a.json and a2a.proto define no REST error body (docs/spec-decisions.md, "REST error format"); `type` URI is an invented placeholder |

Hand-written means no spec example exists for the case. Each was written from the named a2a.json definition; values (ids, URLs, timestamps) are invented.

## REST problem details

The vendored files define no REST error body: `specification.md` only says errors "MUST" be "in a consistent JSON format that maps to A2A error types"
(lines 101 and 104), and `a2a.json` and `a2a.proto` have nothing. `errors/rest-problem-details.task-not-found.json` therefore uses the RFC 9457 form
that `V03ProblemDetails` models (`type`, `title`, `status`, `detail`, `instance`). Its shape is not from the spec, and the `type` URI is a placeholder.
See "REST error format" in `docs/spec-decisions.md`.

## Provenance gaps

- The 9.x examples are the only JSON the spec text gives for requests and results. There is no spec example for `tasks/get`, `tasks/cancel`, `tasks/resubscribe`
  or any push-config method request, nor for a stream `message` or `status-update` event with `final: false`. Those cases are hand-written or absent here.
- `final: false`, `append: true` with `lastChunk: false`, and the other artifact flag mixes are hand-written. The spec text never says when `final` is true (docs/spec-decisions.md).
- The 5.7 sample card says `protocolVersion` `0.2.9` (spec line 417) while the schema default is `0.3.0`; it is kept as published.
- JSON-RPC error messages in `errors/` use the `a2a.json` default text. `specification.md` 8.1/8.2 differs for five codes (see `inventory.json` `errorCodes`).
- No REST or gRPC (ProtoJSON) body fixtures exist yet. The spec shows only TypeScript/proto type sketches for them, and the text does not say whether REST bodies carry `kind`.
- `captured/` is empty. Real peer output may differ from the vendored spec.
