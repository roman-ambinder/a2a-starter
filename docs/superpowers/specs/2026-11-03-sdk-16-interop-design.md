# Package 16: Cross-SDK interop test suite

Date: 2026-11-03
Status: draft for approval
Milestone: M1
Size: M

## Intent

Passing the TCK proves our server follows the spec as the TCK reads it. It does not prove that our client works with the agents teams will call, or that their clients work with our agents. In practice the failures are between SDKs: different JSON shapes, different error forms, different readings of open spec points (pain point 2, with reports across .NET, Java, Go, JS and Python).

This package builds a CI suite that runs our client against the Python, JS and Java reference servers, and their clients against our server, on all three transports. It also adds golden JSON fixtures for every wire type, so JSON drift is caught by a fast test before any container starts. M1 exits only when this suite passes (work plan, M1 exit criteria).

## Findings addressed

- Pain point 2, "JSON serialization drift between SDKs".
- Pain point 7, "Binding differences: JSON-RPC vs REST vs gRPC, URL and tenant handling" (same scenario, every binding, every peer).
- Pain point 6, "Error formats and error mapping" (errors read the same across SDKs).
- Pain point 5, "Signed Agent Cards: canonicalization mismatch" (produces the cross-SDK signed cards package 04 needs).
- Pain point 21, "Testing and mocking agents" (recorded fixtures reused by product teams through package 12).
- R39 (part): the library is tested only against its own samples.

## Goals

1. A test project runs a fixed set of scenarios in two directions: our client against each peer server, and each peer client against our server.
2. Peers: Python `a2a-sdk` 1.3.0, JS `@a2a-js/sdk` 1.3.0, Java `a2a-java` 1.5.0.Final (versions from `research-sdks.md`), each pinned with a lockfile.
3. Every scenario runs on JSON-RPC, HTTP+JSON and gRPC, where the peer supports the transport. Unsupported cells are listed explicitly, with a reason.
4. Golden JSON fixtures cover every wire type in A2A 1.0. Our serializer reproduces each fixture exactly (JCS-equal), and our deserializer reads each fixture and each peer's own output.
5. Each peer's SDK parses and re-serializes every fixture; the suite reports differences per SDK and fails on new ones.
6. Known divergences are listed in one reviewed file with upstream links. A divergence that disappears fails the suite, so the list shrinks.
7. The suite produces cards signed by the Python and JS SDKs for package 04, and checks our signed cards in their verifiers.
8. Results are a readable matrix (peer × transport × direction × scenario) in the CI job summary, plus JUnit.
9. Any developer can run the suite locally with `dotnet test` and Docker, with no other setup.
10. The harness supports a protocol-version dimension, so package 15 can add 0.3 peers in M2 without changing the harness.

## Non-goals

- The TCK run and fixing spec gaps in the core. Package 03 (this suite reports bugs; 03 and the owning packages fix them).
- Card signing and verification code. Package 04 (this suite only feeds it vectors and checks interop).
- 0.3 scenarios and 0.3 peers. Package 15 adds them in M2 on top of this harness.
- Go, Rust or other SDKs. Possible later; the harness must make adding a peer a folder plus a matrix entry.
- Load, soak or performance comparison between SDKs. Package 14.
- Entra auth interop. Peers do not support our Entra setup out of the box; auth interop uses a static bearer token (see Design).
- Fixing other SDKs. We file upstream issues and record them.

## Anti-goals

- A suite so slow or flaky that people learn to ignore it. Every scenario must be deterministic: no sleeps, no LLMs, no timing-based asserts.
- Peer apps that use private SDK internals or patches. Peers use each SDK's public API only, like a product team would.
- Comparing JSON as strings, which fails on member order and hides real differences in noise.
- A silent "skip" for anything that fails. Every skip is in a reviewed file with a reason.
- Unpinned peer versions that change under us between runs.

## Constraints

- Spec A2A v1.0.1; proto at tag `v1.0.1` (work plan; package 03).
- Serialization rules (spec 5): camelCase, ProtoJSON enum names, UTC timestamps with `Z`, REQUIRED fields present, unknown fields ignored (`research-spec.md` section 3).
- Transports: JSON-RPC, HTTP+JSON, gRPC (principle 1). JS gRPC is Node only (`research-sdks.md` JS section), which is fine because peers run in containers.
- .NET 10, test platform Microsoft.Testing.Platform (`global.json`).
- Principle 4: no reflection in the product code. Test code may use reflection.
- Downloaded peer code is untrusted input: pinned versions with lockfiles, built in containers with no secrets, no network beyond the test network.

## Design

### Layout

```
interop/
  contract.md                 the interop agent contract (scenarios and agent behavior)
  matrix.json                 which cells run; skips with reasons
  known-divergences.json      reviewed differences, each with an upstream link and expiry
  peers/
    python/  server/ client/ fixtures-tool/  Dockerfile  pyproject.toml  uv.lock
    js/      server/ client/ fixtures-tool/  Dockerfile  package.json   package-lock.json
    java/    server/ client/ fixtures-tool/  Dockerfile  pom.xml (exact versions)
tests/
  Ambinder.A2A.Interop.Tests/  driver (xUnit), our peer server and client, report writer
  vectors/wire/v1.0/          golden JSON fixtures
  vectors/card-signing/       shared with package 04
```

*(writer's call: peers live in the same repo, so a change to scenarios and peers is one PR.)*

### The interop agent contract

All four peer servers (ours and three others) implement the same agent, so one scenario means the same thing everywhere. The agent reads the first text part of the user message and acts on a command word *(writer's call)*:

| Command | Behavior |
|---|---|
| `echo <text>` | Returns a Message with the text. No task. |
| `task <text>` | Creates a task, adds one artifact with the text, completes. |
| `input` | Task goes to `INPUT_REQUIRED`; the next message on the same task completes it with the received text as artifact. |
| `slow` | Task goes to `WORKING` and waits until canceled or until a `release` message arrives (no timers). |
| `chunks <n>` | Streams one artifact in n chunks (`append`, `lastChunk`; order is stream order, the proto has no `index` field, see package 10), then completes. |
| `fail` | Task goes to `FAILED` with a status message. |
| `data` | Returns a data part with fixed content: integers, floats, large integers within the safe range, Unicode, nested arrays and objects, `null`. |
| `raw` | Returns a raw part with fixed bytes and a file name. |
| `ext` | Completes. Used with the second agent (below), whose card declares `urn:a2a:interop:required` as a required extension. |

Each peer server exposes:
- the main agent on all transports it supports, with streaming and push on, an extended card, and one interface with a tenant (`interop-tenant`);
- a second agent that declares a required extension;
- a signed card variant (Python and JS only, where the SDK signs).

Auth: public operations are anonymous; the extended card requires the bearer token `interop-test-token` *(writer's call; a fixed test value, not a secret, written in the contract file)*.

### Scenarios

Each scenario has an id, runs once per (peer, transport, direction), and asserts both the result and the wire details the spec fixes.

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

Scenarios that need features a peer SDK lacks are listed in `matrix.json` as skipped with a reason and, if it is an SDK bug, an upstream link.

### Direction A: our client against a peer server

- The driver starts the peer server container with Testcontainers for .NET, waits for the card URL to answer, then runs each scenario with our `IA2AClient` over the chosen transport. *(writer's call: Testcontainers, so `dotnet test` works locally with only Docker.)*
- The client is built the way product teams build it (package 10's `AddA2AClient(name, ...)`), not with internal constructors.

### Direction B: a peer client against our server

- The driver starts our interop server in process on a real port, reachable from containers (Testcontainers host port exposure).
- It runs the peer's scenario runner container: `runner <scenario-id> <card-url> <transport>`. The runner uses the peer SDK's public client, runs the scenario, and prints one JSON result object `{scenario, transport, ok, details, wire}` to stdout. The driver asserts on it. *(writer's call: one shared result format for all peers.)*
- Our interop server uses public SDK APIs only, like the TCK SUT (package 03). It may share agent code with the TCK SUT.

### Wire capture

- Both directions record raw HTTP bodies and headers (and gRPC messages as ProtoJSON) per scenario, through a recording `DelegatingHandler` on our client and middleware on our server. Failed scenarios attach the capture to the test output.
- Captures from passing runs can be promoted to fixtures with a script (`interop/promote-capture`), which is how peer output gets into `tests/vectors/`.

### Golden JSON fixtures

Folder `tests/vectors/wire/v1.0/`, one JSON file per case, grouped by type:
- Every message and request/response type in `proto/a2a.proto`: Task, TaskStatus, Message, each Part form (text, raw, url, data), Artifact, each StreamResponse variant, AgentCard (minimal and fully populated, every security scheme type, deprecated OAuth flows), push config and authentication info, every request and response, ListTasks pages.
- Every enum value.
- Edge cases: empty optional fields, empty lists, Unicode and escapes, numbers (`4326` vs `4326.0`, `1e-6`, large safe integers), timestamps with and without milliseconds, unknown fields, unknown enum values.
- Error bodies for each binding: JSON-RPC error object, REST `google.rpc.Status`, gRPC status details as ProtoJSON.

Each file has a sidecar `.meta.json` with: source (spec example, hand-written, or peer capture), spec section, and expected behavior (`roundtrip`, `read-only`, or `must-reject`).

Checks:
1. Our side (fast, no containers, runs in the normal unit test job): for each `roundtrip` fixture, deserialize with our serializer and serialize again; the result must be JCS-equal to the fixture (using `JsonCanonicalizer` from package 04, so member order does not matter but everything else does). `read-only` fixtures must deserialize. `must-reject` fixtures must fail with a clear error.
2. Peer side (in the interop job): each peer's `fixtures-tool` reads every fixture with its SDK, writes it back, and prints the results. The driver compares JCS forms and reports differences per SDK.
3. Peer output: each `fixtures-tool` also builds a fixed set of objects in code and serializes them. Our deserializer must read every one, and our model must equal the model read from the matching golden fixture. *(writer's call: this is how "tolerant inbound" is proven against real peer output.)*

### Known divergences

`interop/known-divergences.json` entries: peer, SDK version, scenario or fixture id, transport, what differs, upstream issue URL, and an `expires` date. Rules:
- A difference not in the file fails the suite.
- An entry that no longer reproduces fails the suite (remove it).
- An expired entry fails the suite (re-check upstream and extend or remove).
*(writer's call, same pattern as the TCK known-issues file in package 03.)*

Known starting entries to verify, from the research: Python returns whole numbers in `Part.data` as floats (`research-painpoints.md` pain point 2); divergent card canonicalization in Python and JS (pain point 5, A2A #2122).

### CI

- Unit test job (every PR): golden fixture check 1 (our side). Fast, no Docker.
- `interop-smoke` job (every PR): JSON-RPC only, both directions, scenarios CARD-01, SEND-02, STREAM-01, ERR-01, for each peer. Target under 10 minutes with cached peer images. *(writer's call)* It is advisory for the first two weeks after it lands, then a required check *(Claude's call, C13; package 12 owns the gate list)*.
- `interop-full` job: the full matrix, nightly, on release tags, before every release, and on PRs with the label `interop`. Required for publishing a release (package 01's release pipeline checks it). The full matrix does not gate every PR *(Claude's call, C15)*.
- Peer images are built from the Dockerfiles in `interop/peers/*` and cached in the GitHub Actions cache by a hash of the peer folder. No registry push. *(writer's call)*
- Peer version bumps: Dependabot (already in the repo, `.github/dependabot.yml`) watches `pyproject.toml`, `package.json` and `pom.xml` under `interop/peers`. A bump PR runs `interop-full`.
- Job summary: a Markdown matrix with one row per scenario and columns per (peer, transport, direction), cells pass / fail / skip (with reason link). JUnit for the test UI.

### Evaluate the official ITK first

`a2aproject/a2a-itk` ("Integration Testing Kit for A2A SDKs", `research-spec.md` section 4) was not checked in depth. Before building peers, the builder spends at most two days on it. If it already runs cross-SDK scenarios with pluggable SUTs on all three transports, we adopt it for the scenario part, contribute a .NET SUT upstream, and keep only the golden fixtures, the signed-card exchange and the reporting here. If not, we build the harness above. The choice and reasons go in `interop/README`. *(writer's call)*

## How it meets the six principles

1. Protocol: checks spec behavior across SDKs on every transport, not just against the TCK. Check: `interop-full` green for M1 exit.
2. .NET enabler: tests run with `dotnet test` and use the public client and server APIs, as teams do. Check: the interop project references only the public packages (no `InternalsVisibleTo`).
3. Easy: one command to run locally; failures show the wire capture and a readable matrix. Check: `interop/README` local run steps tested by a new contributor in under 10 minutes.
4. Performance: the PR smoke job stays fast; fixture checks are in-process. Check: smoke job duration tracked; fails the budget if over 15 minutes *(writer's call)*.
5. Modular: adding a peer is a folder, a Dockerfile and a `matrix.json` entry. Check: the 0.3 peers in package 15 are added without driver changes.
6. Integration pains: catches JSON drift, binding differences and error mapping between real SDKs. Check: each pain point listed above maps to at least one scenario or fixture group.

## Dependencies

Needs:
- 01: package names, and a release pipeline step that requires `interop-full`.
- 03: a conformant core and the TCK SUT patterns; `docs/spec-decisions.md` (divergences caused by open spec points are recorded there too).
- 04: `JsonCanonicalizer` for JCS comparison; the signing and verification APIs for SIGN-01.
- 10: `AddA2AClient(name, ...)` and `IA2AClientFactory`, used by direction A.
- 08: push sender behavior for PUSH-02 (the scenario runs on today's sender until 08 lands).

Provides:
- `tests/Ambinder.A2A.Interop.Tests`, the `interop-smoke` and `interop-full` CI jobs.
- `tests/vectors/wire/v1.0/` golden fixtures (to 03, 12, 15).
- Python and JS signed cards in `tests/vectors/card-signing/` (to 04).
- A harness with a protocol-version dimension and peer folders (to 15 for 0.3 peers).
- Fixtures and the recording handler that package 12 can ship to product teams as recorded-card and recorded-wire helpers.

## Acceptance criteria

- [ ] `interop/contract.md` describes the agent and every scenario; all four peer servers implement it.
- [ ] Peer servers, clients and fixture tools for Python 1.3.0, JS 1.3.0 and Java 1.5.0.Final, pinned with lockfiles.
- [ ] Every scenario in the table runs in both directions on all three transports per peer, or is in `matrix.json` with a reason.
- [ ] Golden fixtures exist for every message type in `proto/a2a.proto`; a test fails if a proto message has no fixture.
- [ ] Our serializer passes every `roundtrip` fixture JCS-equal; reads every `read-only` and peer-produced fixture; rejects every `must-reject` fixture.
- [ ] Peer fixture differences are either zero or in `known-divergences.json` with upstream links; expiry and "no longer reproduces" checks work (tested with a fake entry).
- [ ] SIGN-01 produces Python and JS signed cards into `tests/vectors/card-signing/` and records each verify status.
- [ ] `interop-smoke` runs on every PR and is a required check two weeks after it lands (C13); `interop-full` runs nightly, on tags, before release and on the `interop` label (C15); the job summary shows the matrix.
- [ ] `dotnet test tests/Ambinder.A2A.Interop.Tests` runs locally with only Docker installed.
- [ ] Upstream issues filed for each real divergence found, linked from the divergence file.
- [ ] Docs section (package 13): "Interop status" page generated from the last `interop-full` run *(writer's call)*.

## Left to the builder

- Peer server frameworks within each SDK (for example FastAPI or Starlette for Python, Express for JS, Quarkus reference or plain server for Java).
- Container base images, as long as they are pinned by digest.
- How the driver discovers scenarios (attributes, data files).
- Wire capture format details.
- Which spec examples become fixtures first, as long as coverage is complete at the end.

## Open questions

None. The interop gate split (Q1) is Claude's call C15 ("CI").
