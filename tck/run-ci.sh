#!/usr/bin/env bash
# Build and start the SUT, run the A2A TCK against it, grade the report.
# Usage: bash tck/run-ci.sh <tck-ref>   (a commit SHA, or "main" for the weekly drift run)
# Exit code is the checker's exit code. Leaves $RUNNER_TEMP/a2a-tck/reports-<transport>/ and $RUNNER_TEMP/sut.log.
set -euo pipefail

tck_ref="${1:?usage: run-ci.sh <tck-ref>}"
sut_port="${SUT_PORT:-9999}"
grpc_port="${SUT_GRPC_PORT:-$((sut_port + 1))}"
temp="${RUNNER_TEMP:?RUNNER_TEMP must be set}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sut_log="$temp/sut.log"
tck_dir="$temp/a2a-tck"
sut_pid=""

cleanup() {
  if [ -n "$sut_pid" ]; then
    # dotnet run spawns a child process; stop the whole tree.
    pkill -P "$sut_pid" 2>/dev/null || true
    kill "$sut_pid" 2>/dev/null || true
    wait "$sut_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

cd "$repo"
dotnet build tests/A2A.Tck.Sut -c Release
dotnet run --no-build --project tests/A2A.Tck.Sut -c Release -- --port "$sut_port" --grpc-port "$grpc_port" >"$sut_log" 2>&1 &
sut_pid=$!

ready=0
for _ in $(seq 1 60); do
  if ! kill -0 "$sut_pid" 2>/dev/null; then
    break
  fi
  code="$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:${sut_port}/.well-known/agent-card.json" || true)"
  if [ "$code" = "200" ]; then
    ready=1
    break
  fi
  sleep 1
done
if [ "$ready" != "1" ]; then
  echo "SUT did not become ready on port ${sut_port}. Last 100 lines of $sut_log:" >&2
  tail -n 100 "$sut_log" >&2 || true
  exit 1
fi

rm -rf "$tck_dir"
git clone --quiet https://github.com/a2aproject/a2a-tck.git "$tck_dir"
git -C "$tck_dir" checkout --quiet "$tck_ref"
echo "TCK at $(git -C "$tck_dir" rev-parse HEAD)"

cd "$tck_dir"
uv sync
# The TCK exits non-zero when tests fail, and overwrites reports/ on every run: grading is the checker's job.
report_args=()
for transport in jsonrpc http_json grpc; do
  rm -rf reports
  uv run ./run_tck.py --sut-host "http://localhost:${sut_port}" --transport "$transport" || echo "run_tck.py ($transport) exited with $?"
  mkdir -p "reports-$transport"
  cp reports/* "reports-$transport/"
  report_args+=(--report "$transport=$tck_dir/reports-$transport/compatibility.json")
done

cd "$repo"
status=0
python3 -I tck/check-report.py --known-issues tck/known-issues.json --behaviors tck/sut-behaviors.yaml "${report_args[@]}" || status=$?
exit "$status"
