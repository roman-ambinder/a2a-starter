#!/usr/bin/env bash
# Builds once, then starts MockEntra (5100), WebhookReceiver (5300) and AgentServer (5200..5204).
# Ctrl+C stops all three. Then run:  dotnet run --project samples/AgentClient -- all
set -euo pipefail
cd "$(dirname "$0")"

export ASPNETCORE_ENVIRONMENT=Development
WAIT_SECONDS="${WAIT_SECONDS:-60}"
PIDS=()

port_busy() { # port
  if command -v lsof >/dev/null 2>&1; then lsof -nP -iTCP:"$1" -sTCP:LISTEN >/dev/null 2>&1
  else (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; fi
}

stop_all() {
  trap '' INT TERM EXIT
  if [ "${#PIDS[@]}" -gt 0 ]; then
    echo "Stopping..."
    kill -TERM "${PIDS[@]}" 2>/dev/null || true
    local i p alive
    for ((i = 0; i < 100; i++)); do # up to 10 s
      alive=0
      for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
      [ "$alive" -eq 0 ] && break
      sleep 0.1
    done
    kill -KILL "${PIDS[@]}" 2>/dev/null || true
    wait "${PIDS[@]}" 2>/dev/null || true
  fi
}
trap 'stop_all; exit 130' INT TERM
trap stop_all EXIT

start() { # name  (runs the built DLL directly: the PID is the app, no `dotnet run` host in between)
  echo "Starting $1..."
  dotnet "samples/$1/bin/Debug/net10.0/Ambinder.A2A.Samples.$1.dll" &
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

for port in 5100 5200 5201 5202 5203 5204 5300; do
  if port_busy "$port"; then
    echo "ERROR: port $port is already in use. Stop the other process (an earlier run-all?) and try again." >&2
    exit 1
  fi
done

dotnet build Ambinder.A2A.slnx --nologo -v q

start MockEntra
wait_ready MockEntra http://localhost:5100/health/ready "${PIDS[0]}"
start WebhookReceiver
wait_ready WebhookReceiver http://localhost:5300/health/ready "${PIDS[1]}"
start AgentServer
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
