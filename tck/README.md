# A2A TCK in CI

This folder runs the official [A2A TCK](https://github.com/a2aproject/a2a-tck) against `tests/Ambinder.A2A.Tck.Sut`.
The SUT serves JSON-RPC, REST and gRPC. The job runs the TCK once per transport and grades each report.

## Files

| File | Purpose |
|---|---|
| `TCK_VERSION` | The TCK commit (40-char SHA). The TCK has no releases, so we pin by SHA. |
| `known-issues.json` | Failures that come from a TCK bug. Each entry expires. |
| `sut-behaviors.yaml` | The scenarios the SUT claims to implement. YAML in JSON syntax, so tools read it with a JSON parser. |
| `check-report.py` | Grades the per-transport `compatibility.json` reports. Standard library only. |
| `test_check_report.py` | Tests for the checker. |
| `run-ci.sh` | Starts the SUT, runs the TCK per transport, copies each report, runs the checker. CI and local runs both use it. |

## What the checker fails on

The run fails (exit 1) when:

1. a `MUST` requirement fails and no valid known issue covers it;
2. a `SHOULD` requirement fails and is not in `known-issues.json`;
3. a `MUST` known issue is not a proven TCK bug (see below);
4. a known issue has expired;
5. a known issue no longer fails ("remove it");
6. a requirement listed under a claimed behaviour is skipped, not tested or missing ("listed but not implemented fails, never skips");
7. a known issue lacks `suite`, `issue`, `reason` or `expires`, or its `suite` is not `tck` or `acts`.

A requirement whose top-level `status` is `FAIL` also fails, even when its transport entry says `PASS`
(the TCK lets a later passing test overwrite the transport entry).
`MAY` failures are printed as warnings only. A skipped requirement is not a problem unless a claimed behaviour lists it.

## Known issues

Add an entry only for a failure caused by a TCK bug. Never for our own failure.

```json
{ "suite": "tck", "requirement": "CORE-XXX-001", "transport": "grpc", "level": "SHOULD",
  "issue": "https://github.com/a2aproject/a2a-tck/issues/NNN", "reason": "One line: why this is a TCK bug.", "expires": "2027-01-31" }
```

- `suite` is `tck` for this job. `acts` entries (package 16) are ignored by the rules for failures, passes and MUST bugs.
- `issue` is an `https://` link to the upstream issue. Until the issue is posted, use a `draft:` reference and an expiry of at most two weeks.
- A `MUST` entry also needs `"level": "MUST"`, `"kind": "tck-bug"` and `"spec"`: the spec text the TCK contradicts.
- When the TCK fixes the bug, the entry starts to pass and the checker tells you to remove it.

## Claimed behaviours

Each scenario in the TCK's `scenarios/*.feature` that the SUT implements has one entry in `sut-behaviors.yaml`
(`id` = messageId prefix, `scenario`, `response`, `terminalState`, `requirements`).
`requirements` lists the TCK requirement ids whose tests send that prefix. If one of them is skipped or missing, the job fails.

To add a behaviour:

1. Add one branch (one row of the table) in `tests/Ambinder.A2A.Tck.Sut/TckAgent.cs`.
2. Add one entry to `sut-behaviors.yaml`.

`TckSutBehaviorTests` checks that the two lists match. A message id that starts with `tck-` or `test-` and matches no
behaviour ends the task as `FAILED`, so a missing behaviour fails the TCK test instead of passing.

## SUT choices

- Push notifications are on. The SUT sets `Push.AllowPrivateNetworks = true`, because the TCK webhook listens on `localhost`.
- The extended card is off. The TCK cannot send credentials, and the library requires authentication for
  `GetExtendedAgentCard` when the capability is on. With the capability off, the TCK skips those tests.
  Owner decision 2026-11-03; also recorded in `docs/spec-decisions.md`.

## Bump the TCK version

1. Get the new commit: `git ls-remote https://github.com/a2aproject/a2a-tck.git refs/heads/main`.
2. Write the 40-char SHA and a newline to `TCK_VERSION`.
3. Run the job locally (below). Fix SDK failures. Add known issues only for proven TCK bugs.

## Run locally

Needs `dotnet`, `uv` and `python3`. CI runs the same script (`.github/workflows/tck.yml`).

```bash
mkdir -p /tmp/a2a-tck-run
RUNNER_TEMP=/tmp/a2a-tck-run bash tck/run-ci.sh            # the pinned TCK
RUNNER_TEMP=/tmp/a2a-tck-run bash tck/run-ci.sh main       # TCK main (the weekly run)
```

Reports and `checker-output.txt` land in `tck-reports/<transport>/` (ignored by version control). The exit code is the checker's.

Do not pass `--level should` to the TCK: it runs only the SHOULD tests.
