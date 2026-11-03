# Package 03 (Spec conformance and TCK): remaining work, implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to carry out this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. All subagents run on model `sonnet` (see "Execution notes").

**Goal:** Finish package 03 as approved in `docs/superpowers/specs/2026-11-03-sdk-03-spec-conformance-design.md`. Cover only what PR #6 (`feat/spec-conformance`, merged as `4dca419`) did not do, and the places where PR #6 differs from the final spec.

**Architecture:** Most of the protocol rules are already in the library (PR #6). This plan adds four things. (1) The TCK runs in CI on every PR, per transport, with a report checker, a known-issues list and a claimed-behaviours file. (2) The two seams other packages need: `IA2AVersionDispatcher` and `IAgentCardContributor`. (3) The server-side rules PR #6 left open: tenant match, cancel and state-transition table (D13), trace id on internal errors, and the B13 card ETag. (4) Transport-matrix tests for every acceptance criterion that only has a single-transport or unit test today.

**Tech stack:** .NET 10, ASP.NET Core, Grpc.AspNetCore, xUnit v3 (Microsoft.Testing.Platform), System.Text.Json source generation. GitHub Actions. Python 3.11+ standard library only for the report checker. The TCK uses `uv`.

**Spec:** `docs/superpowers/specs/2026-11-03-sdk-03-spec-conformance-design.md` (approved 2026-11-03). Work plan: `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md`.

---

## 0. Before you start

### 0.1 Package 01 (rename) has landed

This plan assumes package 01's rename is merged: project folders, assemblies and namespaces are `Ambinder.A2A.*`, the solution is `Ambinder.A2A.slnx`, and the old `A2A.Server` is split into `Ambinder.A2A.Server` (core), `Ambinder.A2A.AspNetCore` (JSON-RPC, REST, card endpoints) and `Ambinder.A2A.AspNetCore.Grpc` (package 01 spec, "Design §1", rule 3).

**Check before Task 1:**

```bash
git grep -nE '\bnamespace A2A\.(Protocol|Server|Client)\b' -- src | head
```

Expected: no output. If there is output, package 01 has not landed. Stop and tell the owner.

**Path rule.** The paths in this plan are where package 01 is expected to put files. Package 01 may place a file in a different folder (for example, if the server split is still a follow-up PR). Before you edit a file, find its real path with `git ls-files | grep <FileName>`, and use that path. The table below maps the Phase 1 paths that PR #6 touched to the expected new ones.

| Phase 1 path (as in PR #6) | Expected path after package 01 |
|---|---|
| `src/A2A.Protocol/...` | `src/Ambinder.A2A.Protocol/...` |
| `src/A2A.Protocol.Grpc/ProtoMapper.*.cs` | `src/Grpc.Shared/ProtoMapper.*.cs` |
| `src/A2A.Server/Tasks/*.cs`, `src/A2A.Server/Agents/*.cs`, `src/A2A.Server/A2ACallContext.cs` | `src/Ambinder.A2A.Server/...` |
| `src/A2A.Server/Tasks/TaskStateRules.cs` | `src/Shared/TaskStateRules.cs` (package 01 rule 10, B12) or still `src/Ambinder.A2A.Server/Tasks/` |
| `src/A2A.Server/Transports/{ServiceParameters,TransportErrors,TransportRequest}.cs`, `Transports/JsonRpc/*`, `Transports/Rest/*`, `Hosting/*` | `src/Ambinder.A2A.AspNetCore/...` |
| `src/A2A.Server/Transports/Grpc/*` | `src/Ambinder.A2A.AspNetCore.Grpc/...` |
| `src/A2A.Client/...` (except gRPC) | `src/Ambinder.A2A.Client/...` |
| `src/A2A.Client/Transports/{GrpcA2AClient,GrpcErrorTranslator}.cs` | `src/Ambinder.A2A.Client.Grpc/...` |
| `tests/A2A.Tck.Sut/` | `tests/Ambinder.A2A.Tck.Sut/` |
| `tests/A2A.Integration.Tests/` | `tests/Ambinder.A2A.Integration.Tests/` |
| `tests/A2A.Protocol.Tests/`, `tests/A2A.Server.Tests/` | `tests/Ambinder.A2A.Protocol.Tests/`, `tests/Ambinder.A2A.Server.Tests/` |
| `A2A.Reference.slnx` | `Ambinder.A2A.slnx` |

**Steps that depend on package 01** (they would look different without it):

- Every file path and `namespace` / `using` line in this plan.
- Task 3: the SUT project name, its `ProjectReference`s (`Ambinder.A2A.AspNetCore` and `Ambinder.A2A.AspNetCore.Grpc`), and the solution entry.
- Tasks 4 and 5: the solution file name in the CI commands and the SUT project path.
- Task 8: `IA2AVersionDispatcher` goes in `Ambinder.A2A.AspNetCore`, because it takes `HttpContext` and package 01 rule 2 keeps ASP.NET Core out of `Ambinder.A2A.Server`.
- Task 9: the meter name. Package 01 renames meters to follow assembly names, so the protocol meter is `Ambinder.A2A.Protocol`.
- Task 13: `IAgentCardContributor` goes in `Ambinder.A2A.Server` (no ASP.NET Core types in its signature). The card endpoint that calls it is in `Ambinder.A2A.AspNetCore`.
- Task 17: `TaskStateRules` becomes internal linked source under `src/Shared/` (package 01 rule 10).

### 0.2 What PR #6 already did

PR #6 implemented an earlier copy of spec 03. This table shows what is done, so you do not redo it. "Done" means the code and at least one test exist on `master`.

| Spec item | State after PR #6 | Remaining in this plan |
|---|---|---|
| D1 TCK SUT | `tests/A2A.Tck.Sut` exists. It serves 3 transports and the scenario prefixes. Push is off, there is no extended card, and an unknown prefix completes the task. | Tasks 1–6: the `tck` CI job, the report checker, known issues, claimed behaviours, the weekly job, SUT capabilities |
| D2 server version | Done: patch accepted, empty means 0.3 with an actionable message, `supportedVersions` metadata on all transports | Task 7: negotiated `Major.Minor` on `A2ACallContext`, `error.type` on the request span. Task 8: `IA2AVersionDispatcher` |
| D2 client version | Selection by `protocolVersion` is done. The header still comes from the fixed `A2AClientOptions.ProtocolVersion`. | Task 7: send the selected interface's version, remove the option, "add Ambinder.A2A.Compat.V0_3" hint |
| D3 tolerant enums | Done (`ProtoEnumConverter`, counter, gRPC mapper, role check) | Task 9: make the converter internal; client tests for unknown `TaskState` |
| D4 error model | ErrorInfo on all transports; JSON-RPC `data` array; client maps by reason on all transports | Task 10: `A2AException.Details`, `metadata.traceId` on internal errors, message review, a catalog-wide matrix test |
| D5 extensions | Required check and echo on JSON-RPC, REST and gRPC (in each transport) | Task 11: a streaming echo test. The move into 09's middleware and the client `ActivatedExtensions` are blocked (section 0.3). |
| D6 ListTasks | Behaviour is mostly right in `InMemoryTaskStore` | Task 12: end-to-end matrix tests for every rule |
| D7 card caching | Cache-Control, ETag, Vary, 304, REST extended card `no-store` | Task 13: B13 ETag input, `IAgentCardContributor`, `no-store` on the JSON-RPC extended card, two-instance test |
| D8 tenant | Client side done (JSON-RPC, REST tested) | Task 14: server-side mismatch check, gRPC client test |
| D9 ids | `contextId` limits done (unit test) | Task 15: transport-matrix tests for every D9 rule |
| D10 OAuth flows | Done, with round-trip tests through `Samples.FullCard` | Task 16: `[Obsolete]` as the spec says |
| D11 proto pin | Done (`proto-drift` job, README hash) | Task 5: weekly check for a newer spec tag |
| D12 decisions doc | `docs/spec-decisions.md` exists with 17 rows | Task 18: add the missing rows, fix the ETag row |
| D13 state table | `TaskStateRules.CanAgentTransition` is looser than the table. A cancel of a `CANCELED` task throws. | Task 17 |
| README | "Conformance" section exists but says CI does not run the TCK | Task 6 |

### 0.3 Not in this plan: blocked on other packages

These parts of spec 03 need code from a package that does not exist yet. Do not build stand-ins. When the named package lands, its plan picks the item up. Each item is listed in the PR description so it is not lost.

| Spec item | Blocked on | What is left to do then |
|---|---|---|
| D5: extension check as one built-in middleware in `IA2AMiddleware` (also covers in-process calls) | 09 | Move `ServiceParameters.ActivateExtensions` calls from the three transports into the middleware. Keep the Task 11 tests green. |
| D5: client `A2AResponseInfo.ActivatedExtensions` | 10 | Expose the echoed list. Until then tests read the raw header or metadata (as PR #6's tests do). |
| D2: supported versions from registered client transport factories | 10 | Replace the constant set in Task 7 with the factory list. |
| D6 / D9: context scoping per agent, tenant, owner in the store; ListTasks validation moved out of the store | 05 | Add the "cannot join another owner's context" test; keep Task 12 tests green. |
| D7: ETag over the RFC 8785 (JCS) canonical payload | 04 (`JsonCanonicalizer`) | Swap the ETag input in Task 13 from the serializer output to JCS. The rule (unsigned payload plus sorted `kid`s) stays the same. |
| D8: which tenant the server trusts for isolation | 07, 05 | Nothing in this package. |
| D13: `AUTH_REQUIRED` continuation only for callers `ITaskContinuationPolicy` allows | 07 | Add the policy call to the continuation path. |
| D13: recovery moves `WORKING`/`SUBMITTED` to `FAILED` | 09 | Use `TransitionSource.Recovery` from Task 17. |
| D13: the same transition tests on the EF Core store | 05 | Run the Task 17 integration test against the EF Core store. |
| D1: SUT turns anonymous access on explicitly | 07 | Anonymous is allowed today, so the SUT needs nothing now. When 07 makes anonymous off by default, the SUT turns it on. |

### 0.4 Rules from package 02 (public API)

Package 02 (`docs/superpowers/plans/2026-11-03-sdk-02-public-api.md`) runs alongside this package, so either one may land first. Every new or changed public member in this plan follows the rules below, **whether or not package 02 has landed yet**. A reviewer checks them on every task. Rules marked "if 02 has landed" depend on files that package 02 creates.

1. **No positional public records** (spec 02 section 4, "Records and models"). `A2AAgentRoute` (Task 8) and `AgentCardContext` (Task 13) are `sealed record`s with `required` `init` properties, as shown in those tasks. This matters for `AgentCardContext`: spec 03 says package 09 adds a nullable `Request` to it later, and adding a positional parameter would be a breaking change.
2. **Complete XML docs** (spec 02 section 6). Every new public type and member has a `<summary>` that is not just the name, a `<param>` for each parameter, `<returns>` for each non-void return, and `<exception>` for the exceptions callers should expect. Implementer-facing interfaces (`IA2AVersionDispatcher`, `IAgentCardContributor`) also get `<remarks>`: when they are called, their lifetime, and whether calls can be concurrent. New options properties state their default. Once 02 has landed, a missing doc fails the build (CS1591) and `XmlDocQualityTests`.
3. **API files (if 02 has landed).** When `src/<project>/PublicAPI.Unshipped.txt` exists, each task that adds, changes or removes a public member updates it with the analyzer fix (02 plan Task 9.4), never by hand and never by regenerating the file. This applies to Tasks 7, 8, 9, 10, 13 and 14. Read the diff before you commit.
4. **Breaking-change records.** A public member that is removed or made internal gets a line under "Breaking changes" in `CHANGELOG.md` and a row in `docs/migration/phase1-map.csv` (format `old symbol,new symbol,package,change`; create the file with that header if it does not exist). This applies to `A2AClientOptions.ProtocolVersion` (Task 7), `ProtoEnumConverter<T>` (Task 9, unless 02 already recorded it) and `TaskStateRules.CanAgentTransition` (Task 17, only if `TaskStateRules` is still public when you get there).
5. **Logging.** New log calls in `src/` use `[LoggerMessage]` with a fixed `EventId` (spec 02 section 1, item 7: EventIds are API). Use the per-assembly block from the 02 plan (Task 7.3), unless `docs/policies/versioning.md` already lists a different scheme.
6. **Code rules in `src/`.** `.ConfigureAwait(false)` on every `await`; `TimeProvider` instead of `DateTime.UtcNow` / `DateTimeOffset.UtcNow`; no blocking on tasks; no `new HttpClient` (02 plan Tasks 7 and 8).
7. **JSON metadata.** Code outside `Ambinder.A2A.Protocol` never uses `A2AJsonContext` (02 makes it internal). Use the `A2AJson` typed accessor (for example `A2AJson.AgentCard`) when 02's Task 2 has landed. Before that, use `(JsonTypeInfo<AgentCard>)A2AJson.Options.GetTypeInfo(typeof(AgentCard))`. Do not use `A2AJsonContext.Default.AgentCard`: it lacks the "omit empty lists" modifier on `A2AJson.Options`, so it would change the card bytes and the ETag.
8. **No `InternalsVisibleTo` between shipped assemblies** (spec 01 rule 6, spec 02). `ServiceParameters`, `TransportRequest`, `TransportErrors` and `RequestValidation` are used by both `Ambinder.A2A.AspNetCore` and `Ambinder.A2A.AspNetCore.Grpc` (Phase 1 `A2AGrpcService` and `GrpcErrorMapper` call them). Use them the way package 01 shares them, normally as linked files in `src/Shared/` compiled `internal` (spec 01 rule 10). A new helper that two shipped assemblies need goes to `src/Shared/` the same way (Task 10). Never solve it by making a helper public or by adding `InternalsVisibleTo`.
9. **Deprecations** (spec 02 section 4). `[Obsolete]` carries a `DiagnosticId` (`A2AOBS001`, `A2AOBS002`, ...) and a pointer to the docs (Task 16).
10. **Names.** Spec 03 says package 02 sets the final names of `A2AAgentRoute` and `A2AException.Details`. The 02 plan does not name them. This plan uses those two names, and the 02 plan's API review checklist lists them for the review before `rc.1`.

---

## 1. File structure

New files:

| Path | Purpose |
|---|---|
| `tck/TCK_VERSION` | The `a2aproject/a2a-tck` commit SHA that CI checks out |
| `tck/known-issues.json` | Reviewed SHOULD failures caused by TCK bugs (starts empty) |
| `tck/sut-behaviors.yaml` | Claimed behaviours (JSON-syntax YAML, so the checker needs no extra Python package) |
| `tck/check-report.py` | Reads the per-transport `compatibility.json` files and decides pass or fail |
| `tck/test_check_report.py` | Unit tests for the checker (stdlib `unittest`) |
| `tck/testdata/*.json` | Small report fixtures for the checker tests |
| `tck/README.md` | How the TCK job works, how to add a behaviour or a known issue |
| `.github/workflows/tck-weekly.yml` | Weekly run against TCK `main` HEAD, plus a check for a new A2A spec tag |
| `src/Ambinder.A2A.AspNetCore/Hosting/IA2AVersionDispatcher.cs` | Version dispatcher seam (D2) |
| `src/Ambinder.A2A.Server/Hosting/IAgentCardContributor.cs` | Card contributor seam and `AgentCardContext` (D7) |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/VersionNegotiationTests.cs` | D2 matrix tests |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/ErrorModelTests.cs` | D4 matrix tests |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/ListTasksRulesTests.cs` | D6 matrix tests |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/TenantTests.cs` | D8 server tests |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/TaskIdRulesTests.cs` | D9 matrix tests |
| `tests/Ambinder.A2A.Integration.Tests/Conformance/TaskStateTransitionTests.cs` | D13 end-to-end tests |
| `tests/Ambinder.A2A.Server.Tests/Tasks/TaskStateRulesTests.cs` | D13 exhaustive table test |

Modified files are named in each task.

---

## Phase A: the TCK in CI (D1, D11 weekly part)

### Task 1: Pin the TCK and add the known-issues and behaviours files

**Files:**
- Create: `tck/TCK_VERSION`, `tck/known-issues.json`, `tck/sut-behaviors.yaml`, `tck/README.md`

- [ ] **Step 1: Pick the TCK commit.** The TCK has no releases, so it is pinned by SHA (spec D1 step 2). Get the current `main` HEAD:

```bash
git ls-remote https://github.com/a2aproject/a2a-tck.git refs/heads/main
```

When this plan was written, HEAD was `d4d6fae7887f88ef607fb461c13e3b0e1802f19b` (2026-10-22). Use the HEAD you see now, not this value. Write only the 40-character SHA and a newline to `tck/TCK_VERSION`.

- [ ] **Step 2: Read the TCK at that commit**, so the next files match what it really does:

```bash
git clone https://github.com/a2aproject/a2a-tck.git "$TMPDIR/a2a-tck" && git -C "$TMPDIR/a2a-tck" checkout "$(cat tck/TCK_VERSION)"
```

Read `README.md`, `run_tck.py`, `scenarios/*.feature`, `tests/compatibility/conftest.py`, and `tck/reporting/json_formatter.py`. Confirm these facts, which the plan relies on. If one is false, stop and report to the owner:
  - `run_tck.py --level should` passes `-m should` to pytest, so it runs **only** SHOULD tests (see the differences list, item 1). CI therefore runs **without** `--level`.
  - Reports go to `reports/compatibility.json`, `reports/junitreport.xml`, `reports/compatibility.html` and `reports/tck_report.html`.
  - `compatibility.json` has `per_requirement.<id>.{level, status, transports, errors, test_ids}`. `status` is one of `PASS`, `FAIL`, `SKIPPED`, `NOT TESTED`. `transports` maps a transport to `PASS`, `FAIL` or `SKIPPED`.
  - The TCK has no option to send credentials (only `--sut-host`, `--transport`, `--compatibility-report`, `--webhook-host`).
  - Push tests skip when the card does not declare `pushNotifications`. The webhook receiver listens on `--webhook-host` (default `localhost`).

- [ ] **Step 3: Write `tck/known-issues.json`:**

```json
{
  "entries": []
}
```

Entry shape (document it in `tck/README.md`; do not add an entry now). `suite` is required: `"tck"` for this package's TCK run, `"acts"` for the ACTS run in package 16. Package 16 plan Task 24 copies any ACTS exclusions from the kit into this file with `"suite": "acts"`. The `tck` checker below reads only `"tck"` entries; package 16's `itk/check-acts.py` reads only `"acts"` entries. Both apply the same rules (no MUST entry, expiry, "still fails"):

```json
{ "suite": "tck", "requirement": "CORE-XXX-001", "transport": "grpc", "issue": "https://github.com/a2aproject/a2a-tck/issues/NNN", "reason": "One line: why this is a TCK bug.", "expires": "2027-01-31" }
```

- [ ] **Step 4: Write `tck/sut-behaviors.yaml`.** It is YAML in JSON syntax (YAML 1.2 is a superset of JSON), so the checker reads it with `json` and needs no extra package. List one entry per scenario in the TCK's `scenarios/*.feature` that the SUT implements. For `requirements`, find the TCK requirement ids whose tests send that prefix (`grep -rn "<prefix>" tests/compatibility tck` in the TCK checkout). Example shape:

```json
{
  "behaviors": [
    { "id": "tck-complete-task", "scenario": "core_operations.feature: Complete the task", "response": "task", "terminalState": "TASK_STATE_COMPLETED", "requirements": ["<ids from the TCK>"] },
    { "id": "tck-message-response", "scenario": "core_operations.feature: Return message instead of task", "response": "message", "terminalState": null, "requirements": ["<ids>"] }
  ]
}
```

Cover every prefix the SUT's `switch` handles today (`tck-complete-task`, `tck-artifact-text`, `tck-artifact-file`, `tck-artifact-file-url`, `tck-artifact-data`, `tck-message-response`, `tck-input-required`, `tck-reject-task`, `tck-stream-001`, `tck-stream-002`, `tck-stream-003`, `tck-stream-ordering-001`, `tck-stream-artifact-text`, `tck-stream-artifact-file`, `tck-stream-artifact-chunked`, `test-resubscribe-message-id`), and any scenario at the pinned commit that the SUT does not handle yet (Task 3 adds them).

- [ ] **Step 5: Write `tck/README.md`.** Keep it short. Cover: what the job does; how to bump `TCK_VERSION`; the known-issues rule (only SHOULD failures caused by a TCK bug, with an upstream issue link and an expiry date; never a MUST, C3); how to add a behaviour (one branch in the SUT dispatcher plus one entry in `sut-behaviors.yaml`); how to run it locally (the commands from Task 4).

- [ ] **Step 6: Commit.**

```bash
git add tck/
git commit -m "test(tck): pin the TCK and add known-issues and claimed-behaviours files"
```

### Task 2: Report checker

**Files:**
- Create: `tck/check-report.py`, `tck/test_check_report.py`, `tck/testdata/`

The checker gets one `compatibility.json` per transport. It fails (exit 1) and prints one line per problem when:

1. any requirement with level `MUST` has status `FAIL` on that transport;
2. any `SHOULD` requirement has `FAIL` and is not in `known-issues.json` (with `"suite": "tck"`) for that transport;
3. a known-issues entry is for a `MUST` requirement;
4. a known-issues entry has expired (`expires` is before today, UTC);
5. a known-issues entry no longer fails (status `PASS`): "remove it";
6. a requirement listed under a claimed behaviour is `SKIPPED`, `NOT TESTED`, or missing from the report on that transport ("listed but not implemented fails, never skips", spec D1);
7. a known-issues entry is missing `suite`, `issue`, `reason` or `expires`, or its `suite` is not `"tck"` or `"acts"`.

Rules 3–5 apply only to entries with `"suite": "tck"`; entries with `"suite": "acts"` are skipped here (package 16 checks them). Rule 7 applies to every entry.

`MAY` failures are printed as warnings and do not fail the job.

- [ ] **Step 1: Write the failing tests first.** Create small report fixtures in `tck/testdata/` (a passing report; one with a MUST failure; one with a SHOULD failure; one where a claimed requirement is `SKIPPED`). Then write `tck/test_check_report.py` with stdlib `unittest`. It imports the checker by path with `importlib.util.spec_from_file_location`, because the file name has a hyphen. One test per rule above, for example:

```python
import importlib.util, json, pathlib, tempfile, unittest, datetime

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("check_report", HERE / "check-report.py")
check_report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check_report)

def report(reqs):
    return {"per_requirement": reqs}

class CheckReportTests(unittest.TestCase):
    def run_check(self, reports, known=None, behaviors=None, today=datetime.date(2026, 11, 3)):
        return check_report.check(reports, known or {"entries": []}, behaviors or {"behaviors": []}, today)

    def test_must_failure_fails(self):
        problems, _ = self.run_check({"jsonrpc": report({"R1": {"level": "MUST", "status": "FAIL", "transports": {"jsonrpc": "FAIL"}}})})
        self.assertEqual(1, len(problems))

    def test_claimed_behaviour_skipped_fails(self):
        behaviors = {"behaviors": [{"id": "tck-fake", "requirements": ["R9"]}]}
        problems, _ = self.run_check({"grpc": report({"R9": {"level": "MUST", "status": "SKIPPED", "transports": {"grpc": "SKIPPED"}}})}, behaviors=behaviors)
        self.assertIn("tck-fake", problems[0])

    # ...one test per rule 2-7, one that a MAY failure is only a warning,
    # and one that an entry with "suite": "acts" is ignored by rules 2-5
```

The `test_claimed_behaviour_skipped_fails` test is the acceptance check "a fake listed-but-missing behaviour shows the run fails, not skips".

- [ ] **Step 2: Run the tests and see them fail.**

```bash
python3 -I tck/test_check_report.py -v
```

Expected: errors, because `check-report.py` does not exist yet.

- [ ] **Step 3: Write `tck/check-report.py`.** Standard library only. Interface:

```
python3 -I tck/check-report.py --known-issues tck/known-issues.json --behaviors tck/sut-behaviors.yaml \
    --report jsonrpc=reports-jsonrpc/compatibility.json --report http_json=reports-http_json/compatibility.json --report grpc=reports-grpc/compatibility.json
```

Structure: a pure function `check(reports: dict[str, dict], known: dict, behaviors: dict, today: date) -> tuple[list[str], list[str]]` that returns `(problems, warnings)`, and a `main()` that parses arguments, loads the files, prints `FAIL: ...` and `WARN: ...` lines and a summary, and exits 1 when there are problems. Read a requirement's per-transport result from `transports[<transport>]` and fall back to `status`.

- [ ] **Step 4: Run the tests and see them pass.**

```bash
python3 -I tck/test_check_report.py -v
```

Expected: `OK`.

- [ ] **Step 5: Commit.**

```bash
git add tck/
git commit -m "test(tck): report checker for MUST, SHOULD, known issues and claimed behaviours"
```

### Task 3: Bring the SUT up to the spec

**Files:**
- Modify: `tests/Ambinder.A2A.Tck.Sut/Program.cs`, `tests/Ambinder.A2A.Tck.Sut/Ambinder.A2A.Tck.Sut.csproj`
- Test: `tests/Ambinder.A2A.Integration.Tests/Conformance/TckSutTests.cs` (new)

- [ ] **Step 1: Write a failing test** that the SUT fails a task whose `messageId` starts with `tck-` but matches no behaviour. Today the `default:` branch completes it, so a behaviour that is listed but not implemented could still pass a TCK test. Start the SUT in-process with `WebApplicationFactory` or the same pattern `A2ATestHost` uses, on ports from the test port base (no fixed ports, B2). Send `tck-not-a-real-behaviour-1` and assert the task state is `TASK_STATE_FAILED`. Add the integration test project's reference to the SUT project if needed.

- [ ] **Step 2: Run it and see it fail.**

```bash
dotnet test Ambinder.A2A.slnx --filter-class "*TckSutTests"
```

(If the test platform rejects `--filter-class`, use `-- --filter-class` or the filter syntax the repo's other docs use.)

- [ ] **Step 3: Change the SUT `default:` branch.** A `tck-` or `test-` prefix that no branch handles sets `TaskState.Failed` with the message `"SUT behaviour not implemented: <messageId>. Add it to tests/Ambinder.A2A.Tck.Sut and tck/sut-behaviors.yaml."`. Other messages still complete.

- [ ] **Step 4: Turn capabilities on** (spec D1: "streaming, push notifications and the extended card turned on").
  - Push: `Capabilities.PushNotifications = true`. Register push with `PushOptions.AllowPrivateNetworks = true`, because the TCK webhook listens on `localhost`. This is a SUT-only setting. Find the push registration call with `git grep -n "AllowPrivateNetworks" -- src samples`.
  - Extended card: the TCK cannot send credentials (Task 1 Step 2). The library always requires authentication for `GetExtendedAgentCard`. So turning the capability on makes the TCK's extended-card tests fail with 401 on every transport. **Do not turn it on.** Leave `ExtendedAgentCard` unset, so the TCK skips those tests as "capability not declared". Write this down in `tck/README.md`, and in `docs/spec-decisions.md` in Task 18. This differs from spec D1 (see the differences list, item 2); the owner approved it on 2026-11-03.

- [ ] **Step 5: Add any behaviour from the pinned TCK's `scenarios/` that the SUT lacks**, one `case` per scenario, matching the scenario text exactly. Add each one to `tck/sut-behaviors.yaml`. In particular, check for a long-running, cancelable behaviour: if the scenarios define one, implement it with `Task.Delay(..., ct)` so cancellation ends it.

- [ ] **Step 6: Make the ports configurable without code changes.** Keep `--port` and `--grpc-port`. The CI job passes them.

- [ ] **Step 7: Run the tests.**

```bash
dotnet test Ambinder.A2A.slnx
```

Expected: all pass.

- [ ] **Step 8: Commit.**

```bash
git add tests/ tck/
git commit -m "test(tck): SUT declares push, fails unknown behaviours, covers all scenarios"
```

### Task 4: The `tck` CI job

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add the job.** It runs on every PR. Do **not** use `continue-on-error` (spec anti-goal). "Advisory for two weeks" (C13) means the job is not yet a required check in branch protection; package 12 owns that list. Keep the repo's existing action majors (`actions/checkout@v7`, `actions/setup-dotnet@v5`). For Python, use `actions/setup-python` at its current major; check the latest major on the action's releases page before you pin it.

```yaml
  tck:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v7

      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json

      - uses: actions/setup-python@v5   # check the current major before you commit
        with:
          python-version: '3.12'

      - name: Report checker unit tests
        run: python3 -I tck/test_check_report.py -v

      - name: Build SUT
        run: dotnet build tests/Ambinder.A2A.Tck.Sut --configuration Release

      - name: Start SUT
        run: |
          dotnet run --project tests/Ambinder.A2A.Tck.Sut --configuration Release --no-build -- --port 9999 --grpc-port 10000 > "$RUNNER_TEMP/sut.log" 2>&1 &
          for i in $(seq 1 60); do curl -sf http://localhost:9999/.well-known/agent-card.json > /dev/null && exit 0; sleep 1; done
          cat "$RUNNER_TEMP/sut.log"; exit 1

      - name: Check out TCK
        run: |
          git clone https://github.com/a2aproject/a2a-tck.git "$RUNNER_TEMP/a2a-tck"
          git -C "$RUNNER_TEMP/a2a-tck" checkout "$(cat tck/TCK_VERSION)"
          cd "$RUNNER_TEMP/a2a-tck" && python3 -m pip install uv && uv venv && uv pip install -e .

      - name: Run TCK per transport
        run: |
          set -uo pipefail
          cd "$RUNNER_TEMP/a2a-tck"
          for t in jsonrpc http_json grpc; do
            rm -rf reports
            .venv/bin/python run_tck.py --sut-host http://localhost:9999 --transport "$t" || true   # the checker decides, below
            mkdir -p "$GITHUB_WORKSPACE/tck-reports/$t" && cp -r reports/. "$GITHUB_WORKSPACE/tck-reports/$t/"
          done

      - name: Check reports
        run: >
          python3 -I tck/check-report.py --known-issues tck/known-issues.json --behaviors tck/sut-behaviors.yaml
          --report jsonrpc=tck-reports/jsonrpc/compatibility.json
          --report http_json=tck-reports/http_json/compatibility.json
          --report grpc=tck-reports/grpc/compatibility.json

      - name: Upload reports
        if: always()
        uses: actions/upload-artifact@v4   # check the current major before you commit
        with:
          name: tck-reports
          path: |
            tck-reports/
            ${{ runner.temp }}/sut.log
```

Notes:
- `|| true` on the TCK run is not a hidden skip. The TCK's exit code treats SHOULD failures as xfail, so it cannot enforce "SHOULD fails too". The checker enforces it. A missing `compatibility.json` makes the checker fail (rule 6: claimed requirements missing).
- No `--level` flag: `--level should` would run only SHOULD tests and skip every MUST (Task 1 Step 2).
- The `dotnet run ... --no-build` path depends on package 01's project name.

- [ ] **Step 2: Try it locally** with the same commands (SUT in one terminal, the loop in another). Fix every MUST failure in the **library**, not in the SUT (spec anti-goal: "Conformance code only in the SUT or samples"). For each failure, add a regression test in the right test project before you fix it. If a SHOULD failure is a TCK bug, open (or find) the upstream issue and add a known-issues entry with an expiry date at most 90 days out. That date is my suggestion; the spec gives none.

- [ ] **Step 3: Commit.**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run the A2A TCK on every PR on all three transports"
```

### Task 5: Weekly TCK `main` run and spec tag check

**Files:**
- Create: `.github/workflows/tck-weekly.yml`

- [ ] **Step 1: Write the workflow.** Trigger: `schedule` (one weekly cron, for example `17 3 * * 1`) and `workflow_dispatch`. Permissions: `contents: read`, `issues: write`. Steps:
  1. The same steps as the `tck` job, but check out TCK `main` HEAD instead of `tck/TCK_VERSION`. If the checker fails, open an issue titled `TCK main: new failures (<date>)` with the checker output, using `gh issue create` and `GITHUB_TOKEN`. Look for an open issue with the same title prefix first and comment on it instead of opening a duplicate.
  2. Spec tag check (D11): `gh api repos/a2aproject/A2A/releases --jq '.[].tag_name'`. If a tag newer than `v1.0.1` exists, open the issue `A2A spec <tag> released` once (same duplicate check).
  This workflow never runs on PRs and blocks nothing.

- [ ] **Step 2: Lint the workflow** with `actionlint` if it is installed. If it is not, say in the PR that the workflow was not linted locally, and run it once with `workflow_dispatch` after merge.

- [ ] **Step 3: Commit.**

```bash
git add .github/workflows/tck-weekly.yml
git commit -m "ci: weekly TCK main run and A2A spec release check"
```

### Task 6: README and docs for the TCK

**Files:**
- Modify: `README.md` ("Conformance" section), `docs/spec-decisions.md` ("Checking conformance" section)

- [ ] **Step 1:** In `README.md`, replace "it does not run the TCK yet" with one sentence that CI runs the TCK on every PR on all three transports (job `tck`), with links to `tck/README.md` and `docs/spec-decisions.md`.
- [ ] **Step 2:** In `docs/spec-decisions.md` "Checking conformance", fix the paths after the rename, drop `--level should` from the example command, and add the `--transport` loop from Task 4.
- [ ] **Step 3:** Run the docs tests (the repo has `Repo/DocsTests.cs`):

```bash
dotnet test Ambinder.A2A.slnx
```

- [ ] **Step 4: Commit.**

```bash
git add README.md docs/spec-decisions.md
git commit -m "docs: conformance section links the TCK CI job"
```

---

## Phase B: protocol rules

Every test in this phase that touches the wire runs on JSON-RPC, REST and gRPC through `TransportMatrix.Bindings` (spec acceptance criteria). Follow the pattern in `Conformance/ExtensionNegotiationTests.cs`: `[Collection(A2ATestHostCollection.Name)]`, `host.CreateClientAsync(agent, binding, auth, extensions, ct)`, raw `HttpRequestMessage` for header checks, `Grpc.Net.Client` for gRPC metadata.

### Task 7: Version negotiation leftovers (D2)

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/Transports/ServiceParameters.cs`, `.../TransportRequest.cs`, `src/Ambinder.A2A.AspNetCore.Grpc/.../A2AGrpcService.cs`, `src/Ambinder.A2A.Client/A2AClientFactory.cs`, `.../A2AClientOptions.cs`, `.../Transports/ClientSupport.cs`, the gRPC client
- Create: `tests/Ambinder.A2A.Integration.Tests/Conformance/VersionNegotiationTests.cs`

- [ ] **Step 1: Write failing tests** in `VersionNegotiationTests`:
  - `Missing_version_is_0_3_and_not_supported(binding)`: no header gives `VersionNotSupported`; `Details["supportedVersions"] == "1.0"` (use `Data` until Task 10 adds `Details`); the message equals the spec D2 text exactly: `No A2A-Version header was sent, so the request is treated as A2A 0.3. This agent supports 1.0. Send the header 'A2A-Version: 1.0'.` (PR #6's text; it matches the spec apart from quote style).
  - `Unsupported_versions_are_rejected(binding, version)` for `"2.0"` and `"abc"`.
  - `Patch_version_is_accepted(binding)` for `"1.0.1"`. PR #6 tests this on JSON-RPC only.
  - `Negotiated_version_is_major_minor_on_the_call_context(binding)`: an agent records `A2ACallContext.ProtocolVersion` (or the authoring equivalent the agent can read). Send `1.0.1` and expect `"1.0"`.
  - `Client_sends_the_selected_interface_version`: a recording `HttpMessageHandler` (see `ClientSelectionTests.Recorder`) shows `A2A-Version: 1.0` when the card interface says `1.0.3`.
  - `Card_with_only_0_3_interfaces_says_add_compat`: the `NotSupportedException` message contains `Ambinder.A2A.Compat.V0_3`.
  - `Version_rejection_sets_error_type_on_the_request_activity`: with an `ActivityListener` on `Microsoft.AspNetCore`, the request activity has tag `error.type` = `VERSION_NOT_SUPPORTED`, and no `a2a.server` span exists. Do the same for a 401.

  To send a raw or missing header through the matrix on gRPC, build the call with `Grpc.Net.Client` and set or omit `a2a-version` metadata, as `ExtensionNegotiationTests` does.

- [ ] **Step 2: Run them and see the new ones fail.**

- [ ] **Step 3: Server.**
  - In `TransportRequest.CreateCallContext` (and wherever gRPC builds its call context), pass `ServiceParameters.NormalizeVersion(version)` instead of the raw value.
  - In each place that calls `EnsureSupportedVersion` and in the auth-failure path, catch the rejection there and set `Activity.Current?.SetTag("error.type", <reason>)` before writing the error. `<reason>` is the catalog reason (`VERSION_NOT_SUPPORTED`), or `"401"` / `"403"` for auth failures. Do it in one helper used by all three transports.

- [ ] **Step 4: Client.**
  - Remove `A2AClientOptions.ProtocolVersion`. Fix every use (`git grep -n "ProtocolVersion" -- src samples tests`).
  - `A2AClientFactory.Create` passes the selected interface's `Major.Minor` (from `IsSupportedVersion`'s parse; an empty value becomes `"1.0"`, keeping PR #6's choice) to each transport client, which sends it as `A2A-Version` (HTTP) or `a2a-version` (gRPC).
  - If the card has an interface whose version parses as `0.3`, add to the `NotSupportedException` message: `This card offers A2A 0.3 only for some interfaces; add the Ambinder.A2A.Compat.V0_3 package to call them.`
  - Keep the supported set as a single constant for now (`{"1.0"}`). Package 10 replaces it (section 0.3).

- [ ] **Step 5: Run all tests.** Expected: all pass.

```bash
dotnet test Ambinder.A2A.slnx
```

- [ ] **Step 6: Commit.**

```bash
git commit -am "feat: negotiated version on the call context; client sends the interface version"
```

### Task 8: `IA2AVersionDispatcher` seam (D2)

**Files:**
- Create: `src/Ambinder.A2A.AspNetCore/Hosting/IA2AVersionDispatcher.cs`
- Modify: the JSON-RPC and REST endpoints (where `EnsureSupportedVersion` is called), the `PublicAPI.Unshipped.txt` of `Ambinder.A2A.AspNetCore` if package 02 tracking is on
- Test: `VersionNegotiationTests` (add)

- [ ] **Step 1: Write failing tests.** Register a fake dispatcher with `CanHandle("0.3") == true` that writes `200` and a marker body. Then:
  - JSON-RPC and REST with no version header: the fake handles the request (status 200, marker body).
  - The same with header `2.0`: `VersionNotSupported` (the fake does not claim `2.0`).
  - With no dispatcher registered: `VersionNotSupported` (Task 7 test still passes).
  - gRPC is not tested here: the spec says gRPC needs no dispatcher.

- [ ] **Step 2: Add the interface**, with the members from the spec and the docs and record shape from section 0.4:

```csharp
namespace Ambinder.A2A.AspNetCore;

/// <summary>
/// Serves A2A requests for a protocol version the core does not support, for example 0.3 (added by
/// Ambinder.A2A.Compat.V0_3.AspNetCore). The core registers none.
/// </summary>
/// <remarks>
/// The JSON-RPC and REST endpoints ask each registered dispatcher, in registration order, before they reject a version.
/// Register implementations as singletons; calls can run concurrently. After 1.0, new members get a default implementation.
/// </remarks>
public interface IA2AVersionDispatcher
{
    /// <summary>Says whether this dispatcher serves requests that ask for the given protocol version.</summary>
    /// <param name="version">The requested version as <c>Major.Minor</c>, or <c>"0.3"</c> when the request has no version header.</param>
    /// <returns><see langword="true"/> if <see cref="HandleAsync"/> should handle the request.</returns>
    bool CanHandle(string version);

    /// <summary>Handles the whole HTTP request (JSON-RPC or REST) for the agent and writes the response.</summary>
    /// <param name="http">The request to handle.</param>
    /// <param name="agent">The agent the request is for.</param>
    /// <param name="version">The version <see cref="CanHandle"/> accepted.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>A task that completes when the response is written.</returns>
    Task HandleAsync(HttpContext http, A2AAgentRoute agent, string version, CancellationToken ct);
}
```

`A2AAgentRoute`: spec 03 says it is the resolved agent (name, registration, card) and leaves the name to package 02, which has not named it (section 0.4, item 10). Add a small public type next to the interface. It is a property record, not a positional one (section 0.4, item 1), and it does not expose the internal registration type:

```csharp
/// <summary>The agent an A2A request is routed to.</summary>
public sealed record A2AAgentRoute
{
    /// <summary>The agent's registered name, as used in its URLs.</summary>
    public required string Name { get; init; }

    /// <summary>The agent's public card.</summary>
    public required AgentCard Card { get; init; }
}
```

Say in the PR that the name and shape are open for package 02's API review.

- [ ] **Step 3: Wire it.** In the JSON-RPC and REST endpoints, before `EnsureSupportedVersion`: compute `NormalizeVersion(version)`. If it is not `1.0`, take the first `IA2AVersionDispatcher` from `RequestServices.GetServices<IA2AVersionDispatcher>()` whose `CanHandle(normalized ?? raw)` is true, call `HandleAsync`, and return. Otherwise fall through to `EnsureSupportedVersion` (which throws). Put this in one helper shared by both endpoints.

- [ ] **Step 4: Run all tests.** Expected: all pass.

- [ ] **Step 5: Commit.**

```bash
git commit -am "feat(aspnetcore): IA2AVersionDispatcher seam for other protocol versions"
```

### Task 9: Enum converter visibility and client reads (D3)

**Files:**
- Modify: `src/Ambinder.A2A.Protocol/Json/ProtoEnumConverter.cs`
- Test: `tests/Ambinder.A2A.Protocol.Tests/Json/ModelJsonTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Client/...`

- [ ] **Step 1: Make `ProtoEnumConverter<T>` `internal`.** The spec says "an internal ... ProtoEnumConverter<T>". The 02 plan (Task 2.3) does the same change. If it is already `internal`, skip this step. Otherwise make it internal here, and also add the consumer-context test from the 02 plan, Task 2.5 (`ConsumerJsonContextTests`: a consumer's own source-generated context that lists our models still compiles and writes enum names). If 02's test project does not exist yet, put the test in a test assembly that has no `InternalsVisibleTo` to `Ambinder.A2A.Protocol`. If that test fails, stop and ask the owner; do not make the converter public. Update the API file and the breaking-change records (section 0.4, items 3 and 4).
- [ ] **Step 2: Use the meter-name constant.** `new Meter("A2A.Protocol")` becomes `new Meter(A2ATelemetryNames.ProtocolMeter)`. After package 01 the constant's value is `Ambinder.A2A.Protocol`; check that it is.
- [ ] **Step 3: Client tests.** For each binding, a test agent (or a stub server for JSON-RPC and REST) returns a task whose state is a value this version does not know (`"TASK_STATE_FUTURE"` in JSON; number `99` in proto). Assert `GetTaskAsync` returns the task with `Status.State == TaskState.Unspecified` and does not throw. Assert `TaskStateRules.IsTerminal` and `IsInterrupted` are false for `Unspecified` (unit test).
- [ ] **Step 4: Run all tests and commit.**

```bash
git commit -am "refactor(protocol): ProtoEnumConverter is internal; client reads unknown task states"
```

### Task 10: Error model leftovers (D4)

**Files:**
- Modify: `src/Ambinder.A2A.Protocol/Errors/A2AException.cs`, `src/Ambinder.A2A.AspNetCore/Transports/TransportErrors.cs`, the three client error mappers, every `A2AException` message in `src/` that does not say what to do
- Create: `tests/Ambinder.A2A.Integration.Tests/Conformance/ErrorModelTests.cs`

- [ ] **Step 1: Write failing tests** in `ErrorModelTests`:
  - `Every_catalog_error_round_trips(binding, code)`: a test agent throws `new A2AException(code, "msg", data)` for every catalog code with a reason. The client on each binding gets the same `A2AErrorCode`, and `Details` holds the same string metadata. Skip this test if `Client/TransportParityTests.cs` already covers every code on every binding; check first.
  - `Unknown_exception_becomes_internal_error_with_trace_id(binding)`: the agent throws `InvalidOperationException`. The client gets `InternalError`, `Details["traceId"]` is 32 hex characters, and the message does not contain the exception text.
  - `Details_is_a_read_only_string_map`: unit test in Protocol tests.

- [ ] **Step 2: Add `A2AException.Details`.** Spec D4: `IReadOnlyDictionary<string, string> Details`. It is computed once from `Data` when `Data` is a JSON object: string values as they are, other values as raw JSON text (the same flattening `TransportErrors.ErrorMetadata` does). Move that flattening into one linked source file in `src/Shared/` (compiled `internal` into `Ambinder.A2A.Protocol` and `Ambinder.A2A.AspNetCore`, section 0.4, item 8) and call it from both places. Do not make it an internal helper in `Ambinder.A2A.Protocol` called from `Ambinder.A2A.AspNetCore`: that needs `InternalsVisibleTo` between shipped assemblies. It is empty when `Data` is null. Keep `Data`; package 02 decides the final names.

- [ ] **Step 3: Trace id on internal errors.** In `TransportErrors.Internal`: `var traceId = Activity.Current?.TraceId.ToHexString() ?? ActivityTraceId.CreateRandom().ToHexString();` Log the exception with the trace id as a structured property, through a `[LoggerMessage]` method with a fixed `EventId` (section 0.4, item 5). Return `new A2AException(InternalError, "Internal error. Quote trace id <id> when you report it.", data: {"traceId": "<id>"})`. Check that the gRPC path (`GrpcErrorMapper.Map`) goes through this helper; it does in PR #6.

- [ ] **Step 4: Message review** (spec D4, principle 3). List every message:

```bash
git grep -n 'new A2AException(' -- src
```

Each message must say what the caller should do (for example "Send ...", "Use ...", "Create ..."). Rewrite the ones that only state the problem. Update tests that assert on the old text. Add one test per error that this package added (version, tenant, extension, contextId, D13 transition) that asserts the full message text (spec "How it meets the six principles", item 3).

- [ ] **Step 5: Run all tests and commit.**

```bash
git commit -am "feat: A2AException.Details, trace id on internal errors, actionable error messages"
```

### Task 11: Extension echo on streams (D5)

**Files:**
- Test: `tests/Ambinder.A2A.Integration.Tests/Conformance/ExtensionNegotiationTests.cs`

- [ ] **Step 1: Add tests.** `SendStreamingMessage` on JSON-RPC and REST, with `A2A-Extensions` naming a declared extension: the response headers (read before the first SSE event) contain `A2A-Extensions`. On gRPC streaming, the response header metadata contains `a2a-extensions`.
- [ ] **Step 2: Run them.** If they fail, set the header before the SSE writer starts (in PR #6 it is set before params are read, so they should pass).
- [ ] **Step 3: Commit.**

```bash
git commit -am "test: extension echo on streaming responses"
```

### Task 12: ListTasks rules end to end (D6)

**Files:**
- Create: `tests/Ambinder.A2A.Integration.Tests/Conformance/ListTasksRulesTests.cs`
- Modify (only if a test fails): `src/Ambinder.A2A.Server/Tasks/InMemoryTaskStore.cs`, the JSON writers

- [ ] **Step 1: Write the tests**, each over `TransportMatrix.Bindings`, each with its own fresh owner identity so earlier tasks do not interfere:
  - `Last_page_has_empty_next_page_token`: on JSON-RPC and REST, check the **raw JSON** has `"nextPageToken":""` on the last page (the field is present). On gRPC, an empty string is fine (proto3 omits it).
  - `Tasks_are_ordered_by_status_timestamp_descending_then_id`: create tasks with a `FakeTimeProvider` (B2) so timestamps are known; include two tasks with the same timestamp to check the id tie-break.
  - `Artifacts_absent_unless_requested`: raw JSON has no `artifacts` key when `includeArtifacts` is unset or false; present when true.
  - `Page_size_out_of_range_is_invalid_params`: 0 and 101 give `InvalidParams`; unset gives at most 50.
  - `Other_owners_tasks_are_never_listed`: owner B lists with and without `contextId` = owner A's context id and sees none of A's tasks.
  - `History_length_zero_omits_history`: raw JSON has no `history` key; unset returns the full history.
- [ ] **Step 2: Run.** Fix any failure in the library and keep the fix small.
- [ ] **Step 3: Commit.**

```bash
git commit -am "test: ListTasks rules on all transports"
```

### Task 13: Card ETag (B13), card contributors, extended card no-store (D7)

**Files:**
- Create: `src/Ambinder.A2A.Server/Hosting/IAgentCardContributor.cs`
- Modify: `src/Ambinder.A2A.AspNetCore/Hosting/AgentCardEndpoints.cs`, the JSON-RPC `GetExtendedAgentCard` path, DI registration
- Test: `tests/Ambinder.A2A.Integration.Tests/Conformance/AgentCardCachingTests.cs`

- [ ] **Step 1: Write failing tests:**
  - `Etag_is_the_same_from_two_server_instances`: build two independent servers with the same card (use the repo's `MinimalServer` fixture or `TestServer`) and compare `ETag`.
  - `Etag_does_not_change_when_the_card_is_re_signed_with_the_same_key`: two cards that differ only in `Signatures[0].Signature`, with the same `kid` in the protected header. Same ETag.
  - `Etag_changes_when_the_signing_key_changes`: different `kid`, different ETag.
  - `Contributor_adds_a_member_before_caching`: a registered contributor adds a top-level `"x-test": 1`. The body has it, and the ETag differs from the card without the contributor.
  - `Contributor_may_not_change_an_existing_member`: in Debug builds, a contributor that changes `name` fails (the assert). Skip in Release.
  - `Jsonrpc_extended_card_is_not_stored_by_caches`: a JSON-RPC `GetExtendedAgentCard` response has `Cache-Control: private, no-store`, as the REST one already does.

- [ ] **Step 2: Add the seam** (spec D7), with the docs and record shape from section 0.4:

```csharp
namespace Ambinder.A2A.Server;

/// <summary>What an <see cref="IAgentCardContributor"/> is told about the card it edits.</summary>
public sealed record AgentCardContext
{
    /// <summary>The agent's registered name.</summary>
    public required string AgentName { get; init; }

    /// <summary>The card model before any contributor ran.</summary>
    public required AgentCard Card { get; init; }

    /// <summary><see langword="true"/> for the extended card. Always <see langword="false"/> in this version; see the remarks on <see cref="IAgentCardContributor"/>.</summary>
    public bool IsExtended { get; init; }
}

/// <summary>Adds members to an agent card's JSON before the card is cached and served.</summary>
/// <remarks>
/// Contributors run once per card, in registration order, on the public card endpoints only. A contributor may add new
/// top-level members, append to <c>supportedInterfaces</c> and <c>capabilities.extensions</c>, add new <c>securitySchemes</c>
/// keys, and set <c>securityRequirements</c> where the card has none. It must not change or remove anything already set.
/// Register implementations as singletons; calls for different agents can run concurrently.
/// </remarks>
public interface IAgentCardContributor
{
    /// <summary>Adds this contributor's members to the card JSON.</summary>
    /// <param name="card">The agent and card being served.</param>
    /// <param name="cardJson">The card JSON to add to. Changes are kept.</param>
    void Contribute(AgentCardContext card, JsonObject cardJson);
}
```

Spec 03 says package 09 adds a nullable `Request` to `AgentCardContext`. With a property record that is a non-breaking addition.

- [ ] **Step 3: Change the card endpoint.** Per card instance, once (keep the `ConditionalWeakTable`):
  1. Serialize the card to a `JsonObject` with the `A2AJson` metadata from section 0.4, item 7 (the typed accessor, or `A2AJson.Options` before package 02's Task 2). Never `A2AJsonContext`.
  2. Run each `IAgentCardContributor` from DI in order. In Debug, snapshot the object before each contributor and assert the add-only rules from the spec after it.
  3. The body bytes are the result.
  4. ETag bytes: exactly the rule in the package 04 plan, decision P6, so both plans produce the same value. `payload` = the result with the `signatures` member removed, serialized. Hash input = `payload`, then for each `kid` of the signatures in ordinal order: the byte `0x0A` followed by the UTF-8 bytes of the `kid`. An unsigned card hashes `payload` only. ETag = `"` + base64url(first 16 bytes of SHA-256(input)) + `"`. Read `kid` from the base64url-decoded `protected` header JSON, else from `header.kid`. Put this in one internal helper with the package 04 plan's name and signature, `AgentCardETag.Compute(ReadOnlySpan<byte> payload, IEnumerable<string> keyIds)`, in `src/Ambinder.A2A.Server/Signing/AgentCardETag.cs`, with a unit test that checks the value against `SHA256.HashData` and `Base64Url` computed in the test. If package 04 has already added that helper, use it and do not write a second one.
  5. Package 04 later replaces `payload` in step 4 with its RFC 8785 canonical form (section 0.3); the hash rule stays the same. Leave a one-line comment that says so.

  Contributors run only for the public card endpoints. The extended card is returned as a model by `GetExtendedAgentCard` on all three transports (proto on gRPC), so JSON contributions cannot reach it the same way on every transport. `IsExtended` is therefore always `false` in this package (differences list, item 5; approved by the owner on 2026-11-03).

- [ ] **Step 4: Extended card on JSON-RPC.** Set `Cache-Control: private, no-store` on the JSON-RPC response for `GetExtendedAgentCard`, as REST does.

- [ ] **Step 5: Run all tests and commit.**

```bash
git commit -am "feat: card ETag over the unsigned card and key ids; IAgentCardContributor"
```

### Task 14: Server-side tenant check (D8)

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/Hosting/A2AServerOptions.cs` (or wherever the options live after 01), `.../AgentCardInterfaces.cs`, `.../Transports/TransportRequest.cs`, the gRPC call-context path
- Create: `tests/Ambinder.A2A.Integration.Tests/Conformance/TenantTests.cs`

Today the server never writes a `tenant` on its interfaces, so there is nothing to match against. The spec does not say how a server declares an interface tenant. The owner approved this option on 2026-11-03 (differences list, item 4):

`A2AServerOptions.InterfaceTenants`: `IDictionary<string, string>` from agent name to tenant. When an agent has an entry, every interface `AgentCardInterfaces.For` writes for it gets that `Tenant`.

- [ ] **Step 1: Write failing tests** over `TransportMatrix.Bindings`, with an agent configured with tenant `t1`:
  - A request with `tenant = "t2"` gives `InvalidParams` with the message `tenant 't2' does not match this interface (tenant 't1'). Leave tenant empty or send 't1'.` To send a mismatched tenant you must bypass the client's own check: use raw HTTP for JSON-RPC and REST (REST path prefix `/t2/...`), and a raw gRPC call.
  - A request with empty `tenant`: `A2ACallContext.Tenant == "t1"` (the agent records it).
  - The card lists `tenant: "t1"` on all three interfaces.
  - Client side on gRPC: a different request tenant throws `ArgumentException` (PR #6 tests JSON-RPC and REST only).
- [ ] **Step 2: Implement** in one shared place (`TransportRequest.CreateCallContext` and the gRPC equivalent call one helper `ResolveTenant(agentName, requestTenant)`).
- [ ] **Step 3: Run all tests and commit.**

```bash
git commit -am "feat(server): reject a request tenant that does not match the interface"
```

### Task 15: Task and context id rules on all transports (D9)

**Files:**
- Create: `tests/Ambinder.A2A.Integration.Tests/Conformance/TaskIdRulesTests.cs`

- [ ] **Step 1: One test per rule**, each over `TransportMatrix.Bindings`:
  - `Unknown_task_id_is_task_not_found`.
  - `Other_owners_task_id_is_task_not_found` (same error, same message shape, no leak).
  - `Task_id_only_takes_context_from_the_task`.
  - `Different_task_and_context_ids_are_invalid_params`.
  - `Client_context_id_is_kept_on_a_new_task`.
  - `Context_id_over_256_or_with_control_chars_is_invalid_params` (PR #6 has only a TaskManager unit test).
  - `New_context_id_is_returned`.
  - `Follow_up_on_terminal_task_is_unsupported_operation`.
- [ ] **Step 2: Run.** Fix failures in the library.
- [ ] **Step 3: Commit.**

```bash
git commit -am "test: task and context id rules on all transports"
```

### Task 16: `[Obsolete]` on deprecated OAuth flows (D10)

**Files:**
- Modify: `src/Ambinder.A2A.Protocol/Models/Security.cs`, the proto mapper, `A2AJson.cs`, `tests/Ambinder.A2A.Protocol.Tests/Samples.cs`

- [ ] **Step 1:** Add `[Obsolete("Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.", DiagnosticId = "A2AOBS001")]` to the `ImplicitOAuthFlow` type and the `OAuthFlows.Implicit` property, and the same with `DiagnosticId = "A2AOBS002"` to the `PasswordOAuthFlow` type and the `OAuthFlows.Password` property. The message text is from spec D10. The ids follow spec 02 section 4 (one id per obsolete API). If `docs/policies/versioning.md` already lists other `A2AOBS` ids, take the next free ones. These are protocol fields, so they stay as long as the A2A spec has them; they are not removed in the next major.
- [ ] **Step 2:** The build has `TreatWarningsAsErrors`. With a `DiagnosticId`, the compiler reports the id instead of CS0618. Add `#pragma warning disable A2AOBS001, A2AOBS002` / `restore` around the mapper code, the `[JsonSerializable]` lines (if the generator warns) and the test sample builders. Do not disable them project-wide. If `docs/policies/versioning.md` exists (02 plan Task 13), add both ids to its deprecation table; otherwise the 02 plan adds them when it writes that page.
- [ ] **Step 3:** Run all tests. The round-trip tests from PR #6 must still pass unchanged.
- [ ] **Step 4: Commit.**

```bash
git commit -am "chore(protocol): mark implicit and password OAuth flows obsolete"
```

### Task 17: Task state transition table (D13)

**Files:**
- Modify: `TaskStateRules.cs` (move to `src/Shared/TaskStateRules.cs` as `internal` and link it into each project that uses it, if package 01 has not already done so), `src/Ambinder.A2A.Server/Tasks/AgentEventSink.cs`, `.../TaskManager.cs`
- Create: `tests/Ambinder.A2A.Server.Tests/Tasks/TaskStateRulesTests.cs`, `tests/Ambinder.A2A.Integration.Tests/Conformance/TaskStateTransitionTests.cs`

PR #6's `CanAgentTransition` allows any non-terminal state to move to any state except `UNSPECIFIED` and `SUBMITTED`. The spec table is stricter: from `INPUT_REQUIRED` or `AUTH_REQUIRED`, the agent itself may not move the task at all; only a continuation (server) or a cancel request may.

- [ ] **Step 1: Write the exhaustive unit test first.** The test holds the allowed set as a literal list, copied by hand from the spec table, and checks **every** `(from, to, source)` combination against `CanTransition`:

```csharp
public sealed class TaskStateRulesTests
{
    // Spec 03 D13. (from: null = new task)
    private static readonly HashSet<(TaskState? From, TaskState To, TransitionSource By)> Allowed = Build();

    private static HashSet<(TaskState?, TaskState, TransitionSource)> Build()
    {
        var s = new HashSet<(TaskState?, TaskState, TransitionSource)> { (null, TaskState.Submitted, TransitionSource.Server) };
        foreach (var from in new[] { TaskState.Submitted, TaskState.Working })
        {
            foreach (var to in new[] { TaskState.Working, TaskState.InputRequired, TaskState.AuthRequired, TaskState.Completed, TaskState.Failed, TaskState.Rejected, TaskState.Canceled })
                s.Add((from, to, TransitionSource.Agent));
            s.Add((from, TaskState.Canceled, TransitionSource.CancelRequest));
            s.Add((from, TaskState.Failed, TransitionSource.Recovery));
        }
        foreach (var from in new[] { TaskState.InputRequired, TaskState.AuthRequired })
        {
            s.Add((from, TaskState.Submitted, TransitionSource.Server));
            s.Add((from, TaskState.Working, TransitionSource.Server));
            s.Add((from, TaskState.Canceled, TransitionSource.CancelRequest));
        }
        return s;
    }

    public static TheoryData<TaskState?, TaskState, TransitionSource> AllCombinations()
    {
        var data = new TheoryData<TaskState?, TaskState, TransitionSource>();
        TaskState?[] froms = [null, .. Enum.GetValues<TaskState>().Cast<TaskState?>()];
        foreach (var from in froms)
            foreach (var to in Enum.GetValues<TaskState>())
                foreach (var by in Enum.GetValues<TransitionSource>())
                    data.Add(from, to, by);
        return data;
    }

    [Theory, MemberData(nameof(AllCombinations))]
    public void Transition_matches_the_spec_table(TaskState? from, TaskState to, TransitionSource by)
        => Assert.Equal(Allowed.Contains((from, to, by)), TaskStateRules.CanTransition(from, to, by));
}
```

(`WORKING → WORKING` by the agent is a progress update and is allowed. `SUBMITTED → SUBMITTED` is not in the table, so it is not allowed. Nothing leaves a terminal state. `UNSPECIFIED` is never a valid target, and it is never a valid source either.)

- [ ] **Step 2: Run it and see it fail** (the method does not exist).

- [ ] **Step 3: Implement** in `TaskStateRules`:

```csharp
internal enum TransitionSource { Server, Agent, CancelRequest, Recovery }

internal static bool CanTransition(TaskState? from, TaskState to, TransitionSource by)
{
    if (to == TaskState.Unspecified || !Enum.IsDefined(to)) return false;
    if (from is null) return to == TaskState.Submitted && by == TransitionSource.Server;
    var f = from.Value;
    if (f == TaskState.Unspecified || IsTerminal(f)) return false;
    if (IsInterrupted(f))
        return (to, by) is (TaskState.Submitted or TaskState.Working, TransitionSource.Server)
                        or (TaskState.Canceled, TransitionSource.CancelRequest);
    // SUBMITTED or WORKING
    return by switch
    {
        TransitionSource.Agent => to is not TaskState.Submitted,
        TransitionSource.CancelRequest => to == TaskState.Canceled,
        TransitionSource.Recovery => to == TaskState.Failed,
        _ => false,
    };
}
```

Remove `CanAgentTransition` and replace its callers with `CanTransition(from, to, TransitionSource.Agent)`.

- [ ] **Step 4: Run the unit test and see it pass.**

- [ ] **Step 5: Use the right source at each call site.**
  - `AgentEventSink.UpdateStatusAsync`: the sink knows its source. Add a `TransitionSource` field: `Agent` for sinks created for a run, `CancelRequest` for the sink `TaskManager.CancelTaskAsync` creates for `IAgentHandler.CancelAsync`. **This matters**: the default `CancelAsync` calls `sink.UpdateStatusAsync(Canceled)`. With source `Agent`, a cancel of an `INPUT_REQUIRED` task would become an illegal transition and end `FAILED`.
  - On an illegal transition: keep PR #6's behaviour (the task ends `FAILED`, `InvalidAgentResponse` is thrown). Make the message actionable: `Illegal task state transition {from} -> {to}. From {from} the agent may move to: {list}. See docs/spec-decisions.md.`
  - `TaskManager` continuation (sets `SUBMITTED`): check `CanTransition(state, Submitted, Server)` and throw `InvalidOperationException` if false (that would be a server bug).
  - `TaskManager.CancelTaskAsync` final commit: check `CanTransition(state, Canceled, CancelRequest)`.

- [ ] **Step 6: Cancel of an already canceled task returns the task** (spec D13 table, last row). In `CancelTaskAsync`, before the terminal check: if the state is `CANCELED`, return the stored task. Other terminal states still give `TaskNotCancelable`.

- [ ] **Step 7: End-to-end tests** in `TaskStateTransitionTests` over `TransportMatrix.Bindings`, with a test agent driven by `messageId` prefix:
  - The agent moves `INPUT_REQUIRED → COMPLETED` in the same run: the task ends `FAILED`.
  - A cancel of an `INPUT_REQUIRED` task ends `CANCELED` (not `FAILED`).
  - A continuation of an `INPUT_REQUIRED` task goes `SUBMITTED`, then `WORKING`, in the stream.
  - Cancel twice: the second cancel returns the `CANCELED` task.
  - Cancel a `COMPLETED` task: `TaskNotCancelable`.
  - The agent sets `SUBMITTED`: the task ends `FAILED`.

- [ ] **Step 8: Run all tests.** Look for existing tests or samples that relied on the looser rule (an agent finishing after `INPUT_REQUIRED` in the same run). Fix the test agent or sample, not the rule. List each such change in the PR description.

```bash
dotnet test Ambinder.A2A.slnx
```

- [ ] **Step 9: Commit.**

```bash
git commit -am "feat(server): task state transition table from spec 03 D13"
```

### Task 18: Spec decisions doc (D12)

**Files:**
- Modify: `docs/spec-decisions.md`

- [ ] **Step 1: Fix the card caching row.** The ETag is now base64url of the first 16 bytes of SHA-256 over the unsigned card payload (the card without `signatures`, after contributors) plus the sorted signing key ids (B13). JCS replaces the serializer output when package 04 lands.
- [ ] **Step 2: Add rows** (spec section, choice, reason), one each for:
  - Server tenant mismatch: `InvalidParams`; empty request tenant filled from the interface; how a server declares an interface tenant (`InterfaceTenants`, Task 14).
  - `A2AException.Details` holds the ErrorInfo metadata as strings.
  - Internal errors: generic message plus `metadata.traceId`.
  - Unknown `TaskState` on the client reads as `Unspecified`; it is neither terminal nor interrupted.
  - The extension check runs on every operation except card fetch (already there; check the wording).
  - Task state transition table (link to spec 03 D13); cancel of a `CANCELED` task returns the task.
  - Extended card: `Cache-Control: private, no-store` on JSON-RPC and REST.
  - TCK SUT: the extended card is not declared, because the TCK cannot send credentials (Task 3).
  - TCK run: no `--level` flag (it would skip MUST tests).
- [ ] **Step 3: Run the docs tests and commit.**

```bash
dotnet test Ambinder.A2A.slnx
git commit -am "docs: spec decisions for the rest of package 03"
```

### Task 19: Final check against the acceptance criteria

- [ ] **Step 1:** Run everything:

```bash
dotnet build Ambinder.A2A.slnx --configuration Release
dotnet test Ambinder.A2A.slnx --configuration Release
python3 -I tck/test_check_report.py -v
```

All must pass.

- [ ] **Step 2:** Run the TCK locally as in Task 4. The checker must pass with `tck/known-issues.json` holding no MUST entry.
- [ ] **Step 3:** Go through the spec's "Acceptance criteria" list one by one. For each, write the test name (or CI job) that proves it in the PR description. For each item in section 0.3, write "blocked on package NN". Do not tick a box that has no test.
- [ ] **Step 4:** Check the anti-goals: no `continue-on-error`; no skip list in the TCK run; no conformance logic only in the SUT; no new public type beyond `IA2AVersionDispatcher`, `A2AAgentRoute` (if added), `IAgentCardContributor`, `AgentCardContext`, `A2AException.Details`, `A2AServerOptions.InterfaceTenants`. Check section 0.4 for each of them: no positional record, full XML docs, API file updated (if 02 has landed), breaking changes recorded. If `tests/Ambinder.A2A.PublicApi.Tests` exists, it passes.

```bash
git grep -n "continue-on-error" -- .github
```

Expected: no output.

- [ ] **Step 5:** Push the branch and open the PR. List the blocked items (section 0.3) and the owner decisions (section 3) in its description.

---

## 2. Execution notes

- **How to run this plan:** use the superpowers:subagent-driven-development skill. One fresh subagent per task, with a review between tasks.
- **Model:** every subagent (implementer and reviewer) runs on model **`sonnet`**. Pass `model: "sonnet"` on every Agent call. The owner's rule: planning on Opus, execution on Sonnet.
- **Order:** Task 0 check, then Tasks 1 → 2 → 3 → 4 in order (the CI job needs the checker and the SUT). Tasks 5 and 6 can follow at any time after 4. In Phase B, Tasks 7 → 8 share files, so run them in order. Tasks 9–17 are independent of each other but touch shared test fixtures; run them one at a time to avoid merge conflicts. Task 18 comes after 13, 14 and 17. Task 19 comes last.
- **Branch:** one feature branch for the whole package (for example `feat/sdk-03-conformance`), created from `master` after package 01 merges. One commit per task, as written in each task.
- **TDD:** every task writes the failing test first. If a "failing" test passes at once, say so in the task report. It means PR #6 already covers it; keep the test and move on.
- **Fix the library, not the SUT:** a TCK failure is fixed in `src/`, with a regression test, never with a special case in `tests/Ambinder.A2A.Tck.Sut`.
- **Package 02 rules:** every reviewer checks section 0.4 on each task that touches public API, logging or JSON metadata.
- **Stop and ask the owner** when: package 01 has not landed (Task 0); a fact in Task 1 Step 2 is false; a MUST failure cannot be fixed without changing spec behaviour; the consumer-context test in Task 9 fails. The five decisions in section 3 are settled (owner approved, 2026-11-03); do not reopen them.
- **No fixed ports or sleeps** in new tests (B2). The CI SUT uses fixed ports because it runs alone in its job.
- **Sources:** the TCK facts in this plan were read from `a2aproject/a2a-tck` at commit `d4d6fae7887f88ef607fb461c13e3b0e1802f19b` (`README.md`, `run_tck.py`, `tests/compatibility/conftest.py`, `tck/reporting/`). Check them again at the commit you pin.

---

## 3. PR #6 compared with the final spec: for the owner

These are the places where PR #6, or the spec itself, differs from what spec 03 now says. The plan's proposal for each item is in brackets. **Items 1–5 were owner decisions: on 2026-11-03 the owner approved all five proposals as written.**

1. **The spec's TCK command skips every MUST test.** Spec D1 step 3 runs `run_tck.py ... --level should`. In the TCK, `--level should` passes `-m should` to pytest, so only SHOULD tests run. [Plan: run without `--level`; the checker fails on MUST and on unlisted SHOULD failures. This meets the spec's intent.]
2. **The extended card cannot be turned on in the SUT.** Spec D1 wants it on, and says the SUT accepts a static bearer token "if the TCK can send one". The TCK has no credentials option, and the library always requires auth for `GetExtendedAgentCard`. [Plan: leave it off, so the TCK skips those tests as "not declared". Record it in `docs/spec-decisions.md`. Revisit when the TCK can send credentials.]
3. **Empty `protocolVersion` on a card interface.** PR #6's client treats it as `1.0`. The spec only says "whose protocolVersion Major.Minor is supported". PR #6 records this in `docs/spec-decisions.md`. [Plan: keep it.]
4. **How a server declares an interface tenant.** D8 says the server rejects a tenant that does not match "the interface it arrived on", but today's server never writes a tenant on its interfaces, and the spec does not say where it comes from. [Plan: `A2AServerOptions.InterfaceTenants` (agent name → tenant). Package 07 may replace it.]
5. **`IAgentCardContributor` and the extended card.** Contributors edit card JSON, but the extended card is returned as a model by `GetExtendedAgentCard` on all three transports (proto on gRPC). JSON edits cannot reach it the same way on every transport. [Plan: contributors run on the public card endpoints only; `AgentCardContext.IsExtended` is always false for now. Packages 07 and 15 must not expect contributors to change the extended card.]
6. **ETag input.** PR #6 hashes the whole serialized card, signatures included. B13 says: the unsigned canonical payload plus sorted key ids. [Plan: Task 13, with the exact bytes of the package 04 plan's P6 and its `AgentCardETag` helper. JCS waits for package 04.]
7. **Error metadata name.** PR #6 keeps ErrorInfo metadata in `A2AException.Data` (a `JsonElement`). The spec names it `A2AException.Details` (read-only string map). [Plan: add `Details`, keep `Data`; package 02 picks the final names.]
8. **`ProtoEnumConverter<T>` is public** in PR #6. The spec says internal. [Plan: Task 9 makes it internal.]
9. **The client still sends a fixed version.** PR #6 kept `A2AClientOptions.ProtocolVersion`. The spec removes it and sends the selected interface's version. [Plan: Task 7.]
10. **State transitions are looser than D13.** PR #6's `CanAgentTransition` lets an agent move `INPUT_REQUIRED` to `COMPLETED` (and others) in the same run, and a cancel of a `CANCELED` task throws `TaskNotCancelable`. D13 forbids the first and returns the task for the second. [Plan: Task 17. Existing agents that finish after `INPUT_REQUIRED` in the same run will now end `FAILED`.]
11. **Extension rules live in each transport**, not in one shared place. The spec puts them in package 09's middleware, which does not exist yet. [Plan: keep PR #6's code; move it when 09 lands (section 0.3).]
12. **Internal errors carry no trace id** in PR #6. [Plan: Task 10.]
13. **No server-side tenant check** in PR #6 (client side only). [Plan: Task 14.]
14. **OAuth flows** are marked deprecated only in XML docs, not with `[Obsolete]` as the spec says. [Plan: Task 16.]
