# Interop agent contract

Status: binding for all peer servers (ours and Python, JS, Java). Source of truth in code: `tests/Ambinder.A2A.Interop.Tests/Agent/ContractPayloads.cs` and `InteropAgent.cs`; change both together.

## 1. Agent commands

The agent reads the first text part of the user message and acts on its first word.

| Command | Behavior |
|---|---|
| `echo <text>` | Returns a Message with the text. No task. |
| `task <text>` | Creates a task, adds one artifact with the text, completes. |
| `input` | Task goes to `INPUT_REQUIRED`; the next message on the same task completes it with the received text as artifact. |
| `slow` | Task goes to `WORKING` and waits until canceled or until a `release` message arrives (no timers). |
| `release` | See "As-built details" below. Releases a `slow` task. |
| `chunks <n>` | Streams one artifact in n chunks (`append`, `lastChunk`; order is stream order, the proto has no `index` field, see package 10), then completes. |
| `fail` | Task goes to `FAILED` with a status message. |
| `data` | Returns a data part with fixed content: integers, floats, large integers within the safe range, Unicode, nested arrays and objects, `null`. |
| `raw` | Returns a raw part with fixed bytes and a file name. |
| `ext` | Completes. Used with the second agent (below), whose card declares `urn:a2a:interop:required` as a required extension. |

### As-built details

These are facts of the implemented .NET agent. The spec only says "until a `release` message arrives". Other peers must behave the same.

- `release` is a NEW message with no `taskId`. It names the slow task as text `release <taskId>` or in `message.referenceTaskIds=[<taskId>]` (both may be given; ids are de-duplicated). A follow-up on a `WORKING` task is rejected by the server, so a release cannot be sent "on" the task. It is answered with a Message whose text is `released N` (N = number of distinct ids released) and it starts no task. A release that arrives before the slow task registers still works. The released `slow` task then adds one artifact with text `released` and completes.
- `chunks <n>`: n must be a positive integer, otherwise the task goes to `FAILED` with the status message `chunks needs a positive count`. Otherwise the task goes to `WORKING`, then one artifact (one fixed `artifactId` for all chunks) is streamed in n updates. Chunk i (1-based) carries one text part `chunk-<i>`; `append` is false for chunk 1 and true for the rest; `lastChunk` is true only for chunk n. Then the task goes to `COMPLETED`.
- `input`: the status message of `INPUT_REQUIRED` has the text `input required`. The follow-up completes the task with one artifact whose text is the whole text of the follow-up message.
- `fail`: the status message text is `interop failure`.
- `slow`/`task` artifacts: one text artifact, `lastChunk` true.
- An unknown command word makes the task go to `FAILED` with the status message `unknown command: <word>`.
- `ext` completes the task (no artifact).
- `data` and `raw` answer with a Message (role agent), no task.

### Fixed payloads

`data` returns one message with one data part holding exactly this JSON object:

```json
{
  "integer": 42,
  "negativeInteger": -7,
  "zero": 0,
  "float": 3.14159,
  "negativeFloat": -0.5,
  "smallFloat": 0.000001,
  "maxSafeInteger": 9007199254740991,
  "minSafeInteger": -9007199254740991,
  "unicode": "héllo wörld ✓ 日本語 😀",
  "escapes": "line1\nline2\t\"quoted\" back\\slash",
  "emptyString": "",
  "true": true,
  "false": false,
  "null": null,
  "array": [1, 2.5, "three", null, [4, 5], {"six": 6}],
  "nested": {"a": {"b": {"c": [true, null, {"d": "deep"}]}}, "empty": {}, "emptyArray": []}
}
```

`raw` returns one message with one raw part:

- bytes: `00 01 02 03 FC FD FE FF`
- base64 on the wire: `AAECA/z9/v8=`
- filename: `interop.bin`
- mediaType: `application/octet-stream`

## 2. What each peer server exposes

- The main agent (name `interop`; card at `/.well-known/agent-card.json`) on every transport the peer supports, with streaming and push on, an extended card, and one interface with the tenant `interop-tenant`. Our own host serves the tenant interface from a third agent, `tenant-agent`, whose interfaces all carry the tenant (Task 10).
- A second agent (name `required-ext`) whose card declares the extension `urn:a2a:interop:required` as required.
- A signed card variant (Python and JS only, where the SDK signs).

## 3. Auth

- Public operations are anonymous.
- The extended card requires `Bearer interop-test-token` (a fixed test value, not a secret).
- As built: a bad bearer token on a public operation is treated as anonymous; the operation succeeds. Only the extended card answers 401 / an auth error for a missing or bad token.
- LIST-01 sends `Bearer interop-test-token` on all its calls (the five sends and the list pages). Package 07 makes an anonymous caller's `ListTasks` an empty page by design (07 plan Task 5), so a listing scenario needs a caller identity. A peer driver that cannot set a header for LIST-01 records the cell as `skipped: no auth header support`, not as a failure.

## 4. Card URL layout per peer

- Main agent card: `<base>/.well-known/agent-card.json`.
- Second agent card (`required-ext`) and tenant agent card: each peer's README states its paths. Our own host: see `tests/Ambinder.A2A.Interop.Tests/Agent/InteropServerHost.cs`.
- Each peer's README (`interop/peers/<peer>/README.md`) must state its paths.

## 5. Scenarios

Each scenario has an id, runs once per (peer, transport, direction), and asserts both the result and the wire details the spec fixes. Scenarios that need features a peer SDK lacks are listed in `matrix.json` as skipped with a reason and, if it is an SDK bug, an upstream link.

| Id | What it checks |
|---|---|
| CARD-01 | Fetch `/.well-known/agent-card.json`; parse; interfaces, versions, tenants present. |
| CARD-02 | Extended card with the token; without it, the right auth error. |
| SEND-01 | `echo` blocking: Message returned. |
| SEND-02 | `task` blocking: completed Task with artifact. |
| SEND-03 | `task` with `returnImmediately`: Task returned at once; GetTask until completed. |
| TURN-01 | `input` then a follow-up with taskId: completed; contextId kept; contextId mismatch rejected. |
| STREAM-01 | `chunks 3` streaming: Task first, then events in order, stream closes at terminal state (no `final`). |
| SUB-01 | `slow`, then SubscribeToTask from a second connection: current Task first; `release`; both streams end. |
| CANCEL-01 | `slow`, CancelTask: `CANCELED`; second cancel is idempotent or `TaskNotFound`/`TaskNotCancelable` per spec. |
| LIST-01 | Create 5 tasks; ListTasks with pageSize 2: three pages, last `nextPageToken` is `""`, descending order, no artifacts unless asked. |
| PUSH-01 | Create, get, list, delete push config; delete twice is idempotent. |
| PUSH-02 | Push delivery to a webhook receiver in the harness: body is a `StreamResponse`, configured credentials sent. |
| ERR-01 | GetTask on an unknown id: `TaskNotFound` on every transport, with ErrorInfo reason `TASK_NOT_FOUND`. |
| ERR-02 | Request with `A2A-Version: 9.9`: `VersionNotSupported`. |
| EXT-01 | Second agent without the extension header: `ExtensionSupportRequired`; with it: success and the header echoed. |
| TEN-01 | Tenant interface: client sends the interface tenant; server accepts it. |
| DATA-01 | `data` and `raw`: values read back exactly (integers stay integers, bytes equal, Unicode equal). |
| SIGN-01 | Signed card from the peer verifies in our client (status recorded); our signed card verifies in the peer's verifier. Feeds `tests/vectors/card-signing/python` and `js` (package 04). |

## 6. Direction B runner CLI

```
runner <scenario-id> <card-url> <transport>
```

- `transport` is one of `jsonrpc|http_json|grpc`.
- Optional: `--webhook <url>` (PUSH-02) and `--version 1.0`.
- Prints exactly one JSON object on stdout:

```json
{"scenario":"SEND-02","transport":"jsonrpc","ok":true,"details":"...","wire":[{"dir":"out|in","headers":{},"body":"<json or string>"}]}
```

- Exit code 0 even when `ok` is false. Non-zero only for a crash.

## 7. Fixtures-tool CLI

- `fixtures-tool roundtrip <dir>` prints one JSON line per fixture: `{"id":"Task/completed-with-artifact","ok":true,"output":<json>}` or `{"id":...,"ok":false,"error":"..."}`.
- `fixtures-tool produce` prints `{"id":"<golden fixture id>","output":<json>}` for the fixed object set (Task 20).

## 8. Version dimension

Every cell carries `protocolVersion` (today only `1.0`). A peer declares its `protocolVersions` in `matrix.json`, a scenario declares the versions it supports, and a cell exists where both agree. Adding a version needs no driver change.
