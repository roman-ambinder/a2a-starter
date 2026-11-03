# A2A TCK in CI

This folder runs the official [A2A TCK](https://github.com/a2aproject/a2a-tck) against `tests/A2A.Tck.Sut`.
The SUT serves JSON-RPC, REST and gRPC. The job runs the TCK once per transport and grades each report.

## Files

| File | Purpose |
|---|---|
| `TCK_VERSION` | The TCK commit (40-char SHA). The TCK has no releases, so we pin by SHA. |
| `known-issues.json` | Failures that come from a TCK bug. Each entry expires. |
| `sut-behaviors.yaml` | The scenarios the SUT claims to implement. YAML in JSON syntax, so tools read it with a JSON parser. |
| `check-report.py` | Grades the per-transport `compatibility.json` reports. Standard library only. |
| `test_check_report.py` | Tests for the checker. |
| `run-ci.sh` | Starts the SUT, runs the TCK per transport, runs the checker. |

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
`MAY` failures and skipped `MUST` requirements are printed as warnings only.

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

1. Add one branch (one row of the table) in `tests/A2A.Tck.Sut/TckAgent.cs`.
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

Needs `dotnet`, `uv` and `python3`.

```bash
git clone https://github.com/a2aproject/a2a-tck.git /tmp/a2a-tck
git -C /tmp/a2a-tck checkout "$(cat tck/TCK_VERSION)"

dotnet run --project tests/A2A.Tck.Sut -c Release -- --port 9999 --grpc-port 10000 &

cd /tmp/a2a-tck && uv sync
for t in jsonrpc http_json grpc; do
  rm -rf reports
  uv run ./run_tck.py --sut-host http://localhost:9999 --transport "$t" || true
  mkdir -p "reports-$t" && cp reports/* "reports-$t/"   # reports are overwritten on each run
done
cd -

python3 -I tck/check-report.py --known-issues tck/known-issues.json --behaviors tck/sut-behaviors.yaml \
  --report jsonrpc=/tmp/a2a-tck/reports-jsonrpc/compatibility.json \
  --report http_json=/tmp/a2a-tck/reports-http_json/compatibility.json \
  --report grpc=/tmp/a2a-tck/reports-grpc/compatibility.json
```

Do not pass `--level should` to the TCK: it runs only the SHOULD tests.
