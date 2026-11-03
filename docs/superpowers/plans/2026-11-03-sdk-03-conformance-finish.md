# Package 03 finish: TCK in CI and open acceptance items — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the official A2A TCK run in CI on every PR against `tests/A2A.Tck.Sut` with zero MUST failures, and close the package 03 acceptance items that commit `4dca419` left open.

**Architecture:** A Python report checker (`tck/check-report.py`, stdlib only) grades the TCK's `reports/compatibility.json` against `tck/known-issues.json`. A new `tck` CI job builds the SUT, runs the pinned TCK once over all three transports, and runs the checker. The remaining C# work adds the two seams the spec names (`IA2AVersionDispatcher`, `IAgentCardContributor`), one client message, and missing tests.

**Tech Stack:** .NET 10, xUnit v3 (existing test projects), ASP.NET Core, gRPC; Python 3.11+ with `uv` for the TCK; GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-03-sdk-03-spec-conformance-design.md` (approved 2026-11-03; ACTS moved to package 16).

## Global Constraints

- Spec version A2A v1.0.1. Core supported set is `{1.0}`.
- TCK pinned by commit SHA in `tck/TCK_VERSION`: `d4d6fae7887f88ef607fb461c13e3b0e1802f19b` (a2a-tck `main`, 2026-10-22).
- Python scripts run with `python -I` and need no packages outside the stdlib.
- A MUST failure may never be listed in `tck/known-issues.json`. A SHOULD entry needs an upstream issue URL and an `expires` date (C3).
- The `tck` job is advisory (`continue-on-error: true`) for two weeks after it lands, then required (C13). Package 12 owns the required-check list.
- Names stay `A2A.*` for now. Package 01 renames to `Ambinder.A2A.*` later; do not rename here.
- No new test may use fixed ports or sleeps (B2). The CI job may use fixed ports, because it starts a real process.
- Every public member gets an XML doc comment (`TreatWarningsAsErrors` is on).

## Decisions made while planning (Claude's calls; record each in `docs/spec-decisions.md` or the spec)

1. **One TCK run, no `--level`.** In the pinned TCK, `--level should` runs *only* SHOULD tests (`run_tck.py` `LEVEL_FILTERS`). The job runs `./run_tck.py --sut-host … --webhook-host localhost` with no `--level` and no `--transport` filter. That gives MUST, SHOULD and MAY on every interface in the card in one report. The checker grades by each requirement's `level`. This replaces the spec's "for each transport … `--level should`".
2. **Known-issue key is requirement id + transport.** The report groups results by `per_requirement[<id>].transports[<transport>]`, so entries use `requirement` and `transport`, not a pytest test id.
3. **No extended card on the SUT.** The TCK sends no auth token, so the SUT keeps `ExtendedAgentCard` off. The spec allowed this ("if the TCK can send one").
4. **Server-side tenant check (D8) is deferred.** No server interface declares a tenant today (`AgentCardInterfaces.For`), so the rule has nothing to check. It returns when package 09/07 adds interface tenants. Note it in `docs/spec-decisions.md`.
5. **ETag over the unsigned canonical payload plus key ids (B13) waits for package 04.** Card signing does not exist yet. The current ETag (hash of the card JSON) meets every other part of D7.
6. **`IA2AVersionDispatcher` lives in `A2A.Server.Hosting`.** No `A2A.AspNetCore` assembly exists yet. Package 01 moves it there.

## Review Focus

1. **The SUT crashes or never starts in CI.** Expected: the job fails with the SUT log attached, not a green run with an empty report. Pinned in Task 1 (missing report → exit 1) and Task 4 (readiness wait + log upload).
2. **The TCK skips a whole transport** (for example gRPC is missing from the card). Expected: failure, because a skipped transport is untested. Pinned in Task 1 (`per_transport` must hold `jsonrpc`, `http_json`, `grpc`, each with `total > 0`).
3. **A MUST requirement is SKIPPED on a transport the card declares.** Expected: reported as a warning line, not silently passed. Pinned in Task 1 (`warn:` output test).
4. **A known-issue entry is malformed** (missing `expires`, bad date, level MUST). Expected: checker fails with the entry named. Pinned in Task 1.
5. **A version header with spaces or a leading `v`** (`" 1.0 "`, `v1.0`). Expected: `" 1.0 "` is trimmed and accepted; `v1.0` gets `VersionNotSupported`. Pinned in Task 5.

---

### Task 1: TCK report checker

**Files:**
- Create: `tck/check-report.py`
- Create: `tck/test_check_report.py`
- Create: `tck/known-issues.json` (content `[]`)
- Create: `tck/TCK_VERSION` (one line: the SHA from Global Constraints)

**Interfaces:**
- Produces: CLI `python -I tck/check-report.py --report <path> --known-issues <path> [--today YYYY-MM-DD]`. Exit 0 = pass, 1 = fail. Prints one `FAIL: …` or `warn: …` line per finding, then a summary line.
- Report shape it reads (from TCK `tck/reporting/json_formatter.py`): `per_requirement: {<id>: {level: "MUST"|"SHOULD"|"MAY", status, transports: {<t>: "PASS"|"FAIL"|"SKIPPED"}, errors: [..]}}`, `per_transport: {<t>: {total, passed, failed, skipped}}`.
- Known-issue entry: `{"requirement": str, "transport": str, "level": "SHOULD", "issue": "https://github.com/…", "reason": str, "expires": "YYYY-MM-DD"}`.

Rules (each is one failure line):
- Report file missing or not JSON.
- `per_transport` lacks any of `jsonrpc`, `http_json`, `grpc`, or one has `total == 0`.
- MUST requirement with any transport `FAIL`.
- SHOULD requirement with a transport `FAIL` and no matching known issue.
- Known issue with `level` other than `SHOULD`, missing field, `issue` not starting `https://`, unparsable `expires`, `expires` < today.
- Known issue whose requirement/transport is no longer `FAIL` ("no longer fails; remove it").
- MUST requirement `SKIPPED` on a transport: `warn:` line only.
- MAY failures: ignored.

- [ ] **Step 1: Write failing tests** in `tck/test_check_report.py` (`unittest`, each test writes a tiny report + known-issues into a temp dir and runs the script with `subprocess.run([sys.executable, "-I", script, ...])`). Tests:
  - `test_all_pass_exits_0`
  - `test_missing_report_exits_1`
  - `test_missing_transport_exits_1` (report without `grpc`) and `test_zero_total_transport_exits_1`
  - `test_must_fail_exits_1_even_if_listed` (MUST FAIL plus a matching entry with level `MUST` → exit 1, output names both)
  - `test_should_fail_unlisted_exits_1`
  - `test_should_fail_listed_exits_0`
  - `test_expired_entry_exits_1` (`--today 2026-12-01`, `expires` `2026-11-30`)
  - `test_entry_that_passes_now_exits_1` (output contains `no longer fails`)
  - `test_malformed_entry_exits_1` (no `expires`)
  - `test_must_skipped_warns_but_passes` (exit 0, output contains `warn:`)
  - `test_may_fail_ignored`
- [ ] **Step 2: Run** `python3 -I -m unittest tck/test_check_report.py -v` (from repo root; the test file resolves `check-report.py` next to itself). Expected: every test FAILS (script missing).
- [ ] **Step 3: Implement `tck/check-report.py`** with `argparse`, `json`, `datetime.date`. `main(argv) -> int`. `--today` defaults to `date.today()` (UTC).
- [ ] **Step 4: Run** the same command. Expected: all PASS.
- [ ] **Step 5: Commit** `tck/` — `feat(tck): add TCK report checker and pin TCK version`.

### Task 2: SUT claims behaviours and fails on missing ones

**Files:**
- Modify: `tests/A2A.Tck.Sut/Program.cs` (move `TckAgent` to its own file)
- Create: `tests/A2A.Tck.Sut/TckAgent.cs`
- Create: `tck/sut-behaviors.yaml`
- Create: `tests/A2A.Integration.Tests/Conformance/TckSutBehaviorTests.cs`
- Modify: `tests/A2A.Integration.Tests/A2A.Integration.Tests.csproj` (ProjectReference to `../A2A.Tck.Sut/A2A.Tck.Sut.csproj`)
- Modify: `tests/A2A.Tck.Sut/A2A.Tck.Sut.csproj` (`InternalsVisibleTo A2A.Integration.Tests`)

**Interfaces:**
- Produces: `internal sealed class TckAgent : IAgentHandler` with `public static IReadOnlyList<string> Behaviors { get; }` — the messageId prefixes its dispatcher handles, in match order.
- `tck/sut-behaviors.yaml`: a top-level `behaviors:` list; each item has `id` (the prefix), `response` (`task` or `message`), `terminal` (`completed`, `input-required`, `rejected`, or `none`). Plain YAML that a line parser can read: `- id: …` then two indented keys. No YAML library.

Behaviour changes:
- The `default:` branch sets `TaskState.Failed` with message `"behaviour not implemented: <messageId>"`, instead of completing. That makes a listed-but-missing behaviour fail the TCK test (spec D1 "fails, never skips").
- Card turns on push: `Capabilities.PushNotifications = true`, and `o.Push.AllowPrivateNetworks = true` in `AddA2AServer` (TCK webhook receiver is on localhost).

- [ ] **Step 1: Write failing tests** in `TckSutBehaviorTests`:
  - `Yaml_lists_exactly_the_dispatcher_behaviors`: parse `tck/sut-behaviors.yaml` (find repo root by walking up to `A2A.Reference.slnx`); assert the set of ids equals `TckAgent.Behaviors`.
  - `Each_listed_behavior_reaches_its_terminal_state` (Theory over the yaml): run `TckAgent.ExecuteAsync` with a recording `IAgentEventSink` fake and messageId `<id>-1`; assert the last status matches `terminal` (`none` → a reply message was sent and no Failed status).
  - `Unknown_behavior_fails`: messageId `tck-not-a-behavior-1` → last status `Failed`, message text starts with `behaviour not implemented`.
  - `Card_declares_push_and_streaming`: `TckAgent.Card.Capabilities.PushNotifications == true` and `Streaming == true`.
- [ ] **Step 2: Run** `dotnet test tests/A2A.Integration.Tests --filter FullyQualifiedName~TckSutBehaviorTests`. Expected: FAIL (no `Behaviors`, no yaml).
- [ ] **Step 3: Implement** the file move, `Behaviors`, the `Failed` default, push settings, and the yaml (one entry per existing `case`).
- [ ] **Step 4: Run** the same filter. Expected: PASS. Then `dotnet test A2A.Reference.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(tck): SUT claims its behaviours and fails on unknown ones`.

### Task 3: Run the TCK locally and fix MUST failures

**Files:** depends on findings. Each fix goes in the `src/` file the failure points to, with a regression test in the matching test project (`tests/A2A.Integration.Tests/Conformance/` for wire behaviour on all transports via `TransportMatrix`, `tests/A2A.Protocol.Tests` for serialization, `tests/A2A.Server.Tests` for task rules).

**Interfaces:**
- Consumes: Task 1 checker, Task 2 SUT.
- Produces: a `reports/` run with zero MUST failures; `tck/known-issues.json` entries only for SHOULD failures that are TCK bugs, each with an upstream issue.

- [ ] **Step 1: Set up the TCK** in a scratch folder outside the repo: `git clone https://github.com/a2aproject/a2a-tck && git -C a2a-tck checkout $(cat tck/TCK_VERSION) && cd a2a-tck && uv venv && uv pip install -e .`
- [ ] **Step 2: Start the SUT**: `dotnet run --project tests/A2A.Tck.Sut -c Release -- --port 9999 --grpc-port 10000` (background).
- [ ] **Step 3: Run** `uv run ./run_tck.py --sut-host http://localhost:9999 --webhook-host localhost` in the TCK folder, then `python3 -I tck/check-report.py --report <tck>/reports/compatibility.json --known-issues tck/known-issues.json`. Record the report path and file name actually written (expected `reports/compatibility.json`; if different, fix Task 4's path).
- [ ] **Step 4: For each MUST failure**, in order of requirement id: write a regression test that fails the same way, run it (FAIL), fix the code, run it (PASS), commit with `fix(conformance): <requirement id> <short>`. If a failure is a SUT behaviour gap, fix `TckAgent` + `sut-behaviors.yaml` instead.
- [ ] **Step 5: For each SHOULD failure**: fix it the same way, or, if it is a TCK bug, file an upstream issue on `a2aproject/a2a-tck` (ask the owner before filing — it posts publicly) and add a known-issues entry with `expires` 90 days out.
- [ ] **Step 6: Re-run** Step 3. Expected: checker exit 0. Commit `tck/known-issues.json` if changed.

### Task 4: `tck` CI job and weekly drift job

**Files:**
- Modify: `.github/workflows/ci.yml` (new job `tck`)
- Create: `.github/workflows/conformance-weekly.yml`
- Create: `tck/run-ci.sh` (shared by both workflows)

**Interfaces:**
- Consumes: `tck/TCK_VERSION`, `tck/check-report.py`, the SUT.
- Produces: `tck/run-ci.sh <tck-ref>`: builds and starts the SUT on ports 9999/10000, waits until `GET http://localhost:9999/.well-known/agent-card.json` returns 200 (max 60 s, poll every 1 s; fail with the SUT log otherwise), checks out `a2aproject/a2a-tck` at `<tck-ref>` into `$RUNNER_TEMP/a2a-tck`, installs with `uv`, runs the TCK (Decision 1 flags), runs the checker with `python3 -I`, stops the SUT. Exit code = checker's. Leaves `$RUNNER_TEMP/a2a-tck/reports/` and `$RUNNER_TEMP/sut.log`.

- [ ] **Step 1: Write `tck/run-ci.sh`** (`set -euo pipefail`, `trap` that kills the SUT).
- [ ] **Step 2: Run it locally** with `bash tck/run-ci.sh "$(cat tck/TCK_VERSION)"` (set `RUNNER_TEMP` to a scratch dir). Expected: exit 0 (Task 3 made it green).
- [ ] **Step 3: Check the failure path**: run with the SUT port changed so readiness never succeeds (`SUT_PORT=1 bash tck/run-ci.sh …`, the script reads `SUT_PORT` with default 9999). Expected: exit non-zero within ~60 s and the log tail printed.
- [ ] **Step 4: Add job `tck`** to `ci.yml`: `runs-on: ubuntu-latest`, `timeout-minutes: 25`, `continue-on-error: true` with comment `# advisory until <landing date + 14 days>, then required (C13)`, steps: checkout, setup-dotnet (`global-json-file`), `astral-sh/setup-uv` (pinned major), Python 3.12, `bash tck/run-ci.sh "$(cat tck/TCK_VERSION)"`, then `actions/upload-artifact` with `if: always()` for `reports/` (html, junit, json) and `sut.log`.
- [ ] **Step 5: Add `conformance-weekly.yml`**: `schedule` (Monday 06:00 UTC) + `workflow_dispatch`; `permissions: contents: read, issues: write`. Job 1 runs `tck/run-ci.sh main`; on failure, `gh issue create` titled `TCK main: new failures (<date>)` with the checker output, unless an open issue with that title prefix exists. Job 2 (D11): `gh api repos/a2aproject/A2A/releases/latest --jq .tag_name`; if it is not `v1.0.1`, open an issue `A2A <tag> released` (same dedupe). Neither job runs on `pull_request`.
- [ ] **Step 6: Validate** both files with `actionlint` if installed. The real check is the first Actions run after the push in Task 9: the `tck` job completes and uploads the report.
- [ ] **Step 7: Commit** — `ci: run the A2A TCK on every PR and weekly against TCK main`.

### Task 5: Version dispatcher seam and version tests

**Files:**
- Create: `src/A2A.Server/Hosting/IA2AVersionDispatcher.cs`
- Modify: `src/A2A.Server/Transports/ServiceParameters.cs` (parse helper; trim)
- Modify: `src/A2A.Server/Transports/JsonRpc/JsonRpcEndpoint.cs`, `src/A2A.Server/Transports/Rest/RestEndpoints.cs` (ask dispatchers before `EnsureSupportedVersion`)
- Create: `tests/A2A.Integration.Tests/Conformance/VersionNegotiationTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IA2AVersionDispatcher
  {
      bool CanHandle(string version);                 // "0.3" for an empty header
      Task HandleAsync(HttpContext http, A2AAgentRoute agent, string version, CancellationToken ct);
  }
  public sealed record A2AAgentRoute(string Name, AgentRegistration Registration, AgentCard Card);
  ```
  Registered as `IEnumerable<IA2AVersionDispatcher>`; the core registers none. Version normalisation: `ServiceParameters.NormalizeVersion(string? raw) -> string` (empty → `"0.3"`, trims, `Major.Minor.Patch` → `Major.Minor`, anything else returned unchanged so it fails the supported check).
- gRPC: unchanged (spec: no dispatcher for gRPC).

- [ ] **Step 1: Write failing tests** (Theory over `TransportMatrix.Bindings` where the call goes through a client; raw `HttpClient` for header-level cases, as `ExtensionNegotiationTests` does):
  - `Missing_version_is_0_3_and_rejected`: error code `VersionNotSupported`, message contains `treated as A2A 0.3`, ErrorInfo metadata `supportedVersions` == `"1.0"`.
  - `Unsupported_versions_rejected` (`"2.0"`, `"abc"`, `"v1.0"`).
  - `Patch_and_spaces_accepted` (`"1.0.1"`, `" 1.0 "`).
  - `Registered_dispatcher_receives_empty_version` (JSON-RPC and REST): register a fake dispatcher with `CanHandle("0.3") == true` that writes status 299; send with no header; assert 299 and the fake saw `version == "0.3"` and the agent name.
- [ ] **Step 2: Run** `dotnet test tests/A2A.Integration.Tests --filter FullyQualifiedName~VersionNegotiationTests`. Expected: dispatcher and trim tests FAIL.
- [ ] **Step 3: Implement** the interface, record, `NormalizeVersion`, and the dispatcher lookup in both HTTP endpoints.
- [ ] **Step 4: Run** the filter, then the whole solution. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(server): add IA2AVersionDispatcher seam and version tests`.

### Task 6: Card contributor seam and caching tests

**Files:**
- Create: `src/A2A.Server/Hosting/IAgentCardContributor.cs`
- Modify: `src/A2A.Server/Hosting/AgentCardEndpoints.cs` (run contributors before the bytes and ETag are cached; same for the extended card)
- Modify: `tests/A2A.Integration.Tests/Conformance/AgentCardCachingTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IAgentCardContributor { void Contribute(AgentCardContext card, JsonObject cardJson); }
  public sealed record AgentCardContext(string AgentName, AgentCard Card, bool IsExtended);
  ```
  Contributors run in registration order. In `DEBUG` builds, after each contributor, `Debug.Assert` that every member present before is unchanged (compare `JsonNode.DeepEquals` per original top-level member, except appended entries in `supportedInterfaces` and `capabilities.extensions`, and new keys in `securitySchemes`).

- [ ] **Step 1: Write failing tests** in `AgentCardCachingTests`:
  - `Contributor_member_appears_and_changes_etag`: register a contributor adding `"x-test": 1`; card JSON has it; ETag differs from a host without the contributor.
  - `Contributors_run_in_registration_order` (second contributor sees the first one's member).
  - `Extended_card_is_no_store` on REST and JSON-RPC: `Cache-Control: private, no-store`.
  - `Etag_is_stable_across_two_hosts` (same config, two `A2ATestHost` instances, same ETag).
- [ ] **Step 2: Run** `dotnet test tests/A2A.Integration.Tests --filter FullyQualifiedName~AgentCardCachingTests`. Expected: contributor tests FAIL; check whether the JSON-RPC extended-card `no-store` test fails too (fix it if so).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the filter and the whole solution. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(server): add IAgentCardContributor seam and card caching tests`.

### Task 7: Client "add Compat" hint and remaining acceptance tests

**Files:**
- Modify: `src/A2A.Client/A2AClientFactory.cs:33-36`
- Modify: `tests/A2A.Integration.Tests/Conformance/ClientSelectionTests.cs`
- Modify or create tests as the audit below finds gaps: `tests/A2A.Protocol.Tests/Json/ModelJsonTests.cs`, a new `tests/A2A.Protocol.Tests/Grpc/OAuthFlowRoundTripTests.cs` (or the existing proto mapper test file), `tests/A2A.Integration.Tests/Conformance/TaskIdRulesTests.cs`, `tests/A2A.Integration.Tests/Conformance/ListTasksTests.cs`.

**Interfaces:**
- No-match message, when at least one skipped interface has Major.Minor `0.3`, ends with: ` Add the Ambinder.A2A.Compat.V0_3 package to talk to A2A 0.3 agents.`

- [ ] **Step 1: Write failing test** `Card_with_only_0_3_interfaces_suggests_compat` in `ClientSelectionTests`: `NotSupportedException` message contains `Ambinder.A2A.Compat.V0_3` and `JSONRPC/0.3`.
- [ ] **Step 2: Audit** each spec acceptance bullet against existing tests and list the gaps in the commit message. Known gaps to check first (from planning): OAuth `implicit`/`password` round-trip (only in `Samples.cs` today) for JSON→JSON, proto→proto, JSON→proto→JSON; D9 rules on all three transports (unknown `taskId` → `TaskNotFound`, also for another owner; mismatched `taskId`/`contextId` → `InvalidParams`; 257-char and control-char `contextId` → `InvalidParams`; terminal follow-up → `UnsupportedOperation`; new `contextId` returned); ListTasks on all transports (last page `nextPageToken == ""`, descending status timestamp, `artifacts` absent unless `includeArtifacts`); every catalog `A2AErrorCode` maps back to the same code on all transports.
- [ ] **Step 3: Write one failing test per gap** (Theory over `TransportMatrix.Bindings` for wire rules). Run; a test that already passes stays as coverage.
- [ ] **Step 4: Implement** the client message and any fix the new tests demand.
- [ ] **Step 5: Run** `dotnet test A2A.Reference.slnx`. Expected: PASS.
- [ ] **Step 6: Commit** — `test(conformance): cover remaining package 03 acceptance items`.

### Task 8: Docs

**Files:**
- Modify: `README.md` (section `## Conformance`, line ~190)
- Modify: `docs/spec-decisions.md`
- Modify: `docs/superpowers/specs/2026-11-03-sdk-03-spec-conformance-design.md` (D1 run flags; D8/D7 deferrals)

- [ ] **Step 1:** README "Conformance": three to five sentences. Says the `tck` CI job runs the official TCK (pinned in `tck/TCK_VERSION`) on every PR over JSON-RPC, HTTP+JSON and gRPC, links the workflow file and `tck/known-issues.json`, keeps the link to `docs/spec-decisions.md`, and gives the one-line local command `bash tck/run-ci.sh "$(cat tck/TCK_VERSION)"`. Remove "it does not run the TCK yet".
- [ ] **Step 2:** `docs/spec-decisions.md`: add rows for version header trim, server tenant check deferred (Decision 4), ETag rule pending package 04 (Decision 5); update "Checking conformance" to the `run-ci.sh` command and `--webhook-host localhost`.
- [ ] **Step 3:** Spec 03: in D1 replace step 3's command with Decision 1's and add Decision 2's key format to the known-issues bullet; mark Decision 3, 4, 5 inline as *(Claude's call, plan 2026-11-03)*.
- [ ] **Step 4: Commit** — `docs: conformance section, spec decisions and spec 03 updates`.

### Task 9: Final check and PR

- [ ] **Step 1:** `dotnet build A2A.Reference.slnx -c Release && dotnet test A2A.Reference.slnx -c Release --no-build` → PASS. `python3 -I -m unittest tck/test_check_report.py` → PASS. `bash tck/run-ci.sh "$(cat tck/TCK_VERSION)"` → exit 0.
- [ ] **Step 2:** Tick each acceptance box in spec 03 that is now met; leave B13 ETag and server tenant boxes open with "(package 04)" / "(package 09)".
- [ ] **Step 3:** Push `feat/spec-conformance` and open a PR to `master` (ask the owner first). The PR body lists the Claude's calls above.
