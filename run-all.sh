#!/usr/bin/env bash
# Builds once, then starts MockEntra (5100), WebhookReceiver (5300) and AgentServer (5200..5204).
# Ctrl+C stops all three. Then run:  dotnet run --project samples/AgentClient -- all
set -euo pipefail
cd "$(dirname "$0")"

export ASPNETCORE_ENVIRONMENT=Development
WAIT_SECONDS="${WAIT_SECONDS:-60}"
PIDS=()

stop_all() {
  trap '' INT TERM EXIT
  if [ "${#PIDS[@]}" -gt 0 ]; then
    echo "Stopping..."
    kill "${PIDS[@]}" 2>/dev/null || true
    wait "${PIDS[@]}" 2>/dev/null || true
  fi
}
trap 'stop_all; exit 130' INT TERM
trap stop_all EXIT

start() { # name project
  echo "Starting $1..."
  dotnet run --no-build --no-launch-profile --project "$2" &
  PIDS+=("$!")
}

wait_ready() { # name url pid
  local i
  for ((i = 0; i < WAIT_SECONDS; i++)); do
    if curl -fsS -o /dev/null "$2" 2>/dev/null; then echo "$1 is ready ($2)"; return 0; fi
    if ! kill -0 "$3" 2>/dev/null; then echo "ERROR: $1 exited before it was ready." >&2; return 1; fi
    sleep 1
  done
  echo "ERROR: $1 was not ready at $2 after ${WAIT_SECONDS}s." >&2
  return 1
}

dotnet build A2A.Reference.slnx --nologo -v q

start MockEntra samples/MockEntra
wait_ready MockEntra http://localhost:5100/health/ready "${PIDS[0]}"
start WebhookReceiver samples/WebhookReceiver
wait_ready WebhookReceiver http://localhost:5300/health/ready "${PIDS[1]}"
start AgentServer samples/AgentServer
wait_ready AgentServer http://localhost:5200/health/ready "${PIDS[2]}"

cat <<'MSG'

All three apps are running.
  MockEntra        http://localhost:5100
  AgentServer      http://localhost:5200  (gRPC 5201 echo, 5202 report, 5203 booking, 5204 secure)
  WebhookReceiver  http://localhost:5300
  Agent card       http://localhost:5200/.well-known/agent-card.json

Try:  dotnet run --project samples/AgentClient -- all
Press Ctrl+C to stop.
MSG

# Exit (and clean up) as soon as any app dies.
while kill -0 "${PIDS[0]}" "${PIDS[1]}" "${PIDS[2]}" 2>/dev/null; do sleep 1; done
echo "ERROR: an app exited unexpectedly." >&2
exit 1
