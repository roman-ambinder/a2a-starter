#!/usr/bin/env bash
# Release smoke test: installs Ambinder.A2A packages into two fresh projects (outside the repo), runs the getting-started agent and
# calls it with the getting-started client. Pass means the client prints "Hello, smoke!".
# Usage: eng/smoke-test.sh <version> [local-feed-dir]
#   Without a feed: waits (up to 20 minutes) until the version is visible on nuget.org, then restores from nuget.org only.
#   With a feed (dry run): restores from that folder plus nuget.org (for the third-party dependencies).
# Environment: SMOKE_PORT (default 5100) is the port of the agent.
set -euo pipefail

version="${1:?usage: smoke-test.sh <version> [local-feed-dir]}"
feed="${2:-}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port="${SMOKE_PORT:-5100}"
base="http://localhost:$port"

# Everything lives outside the checkout, so the repo's Directory.Build.props and Directory.Packages.props do not apply.
work="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/a2a-smoke.XXXXXX")"
agent_pid=""
cleanup() {
  if [ -n "$agent_pid" ] && kill -0 "$agent_pid" 2>/dev/null; then kill "$agent_pid" 2>/dev/null || true; fi
}
fail() {
  echo "ERROR: $*" >&2
  if [ -f "$work/agent.log" ]; then echo "---- agent log ----" >&2; tail -n 50 "$work/agent.log" >&2; fi
  exit 1
}
trap cleanup EXIT

export NUGET_PACKAGES="$work/nuget-packages"        # nothing from a global cache
export NUGET_HTTP_CACHE_PATH="$work/nuget-http"     # no cached 404 for a version that was just published
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if [ -z "$feed" ]; then
  lower="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
  # The packages the two smoke projects need, with their Ambinder dependencies (they are pushed in alphabetical order).
  for id in ambinder.a2a.aspnetcore ambinder.a2a.server ambinder.a2a.protocol ambinder.a2a.client; do
    ok=""
    for attempt in $(seq 1 40); do   # every 30 s, up to 20 minutes
      if curl -fsS "https://api.nuget.org/v3-flatcontainer/$id/index.json" 2>/dev/null | jq -e --arg v "$lower" '.versions | index($v)' >/dev/null 2>&1; then
        ok=1; break
      fi
      echo "Waiting for $id $version on nuget.org (attempt $attempt of 40)"
      sleep 30
    done
    [ -n "$ok" ] || fail "$id $version did not appear on nuget.org within 20 minutes"
    echo "$id $version is visible on nuget.org"
  done
  sources='<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
else
  feed="$(cd "$feed" && pwd)"
  sources="<add key=\"local\" value=\"$feed\" />
    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
fi

cat > "$work/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    $sources
  </packageSources>
</configuration>
XML
echo '<Project />' > "$work/Directory.Build.props"
echo '<Project />' > "$work/Directory.Packages.props"

cd "$work"

echo "== Agent: dotnet new web + Ambinder.A2A.AspNetCore $version"
dotnet new web -n SmokeAgent -o SmokeAgent --no-restore >/dev/null
dotnet add SmokeAgent package Ambinder.A2A.AspNetCore --version "$version" >/dev/null
cp "$root/samples/GettingStarted.Agent/Program.cs" SmokeAgent/Program.cs
dotnet build SmokeAgent -c Release --nologo -v:q || fail "the agent does not build against the packages"

(
  cd SmokeAgent
  export A2A__PublicBaseUrl="$base" ASPNETCORE_ENVIRONMENT=Production
  exec dotnet bin/Release/net10.0/SmokeAgent.dll
) > "$work/agent.log" 2>&1 &
agent_pid=$!

echo "== Waiting for $base/.well-known/agent-card.json"
up=""
for _ in $(seq 1 60); do
  kill -0 "$agent_pid" 2>/dev/null || fail "the agent exited before it served the agent card"
  if curl -fsS "$base/.well-known/agent-card.json" -o "$work/card.json" 2>/dev/null; then up=1; break; fi
  sleep 1
done
[ -n "$up" ] || fail "no agent card within 60 seconds"
echo "Agent card: $(head -c 200 "$work/card.json")"

echo "== Client: dotnet new console + Ambinder.A2A.Client $version"
dotnet new console -n SmokeClient -o SmokeClient --no-restore >/dev/null
dotnet add SmokeClient package Ambinder.A2A.Client --version "$version" >/dev/null
cp "$root/samples/GettingStarted.Client/Program.cs" SmokeClient/Program.cs
output="$(dotnet run --project SmokeClient -c Release -- "$base" smoke)" || fail "the client failed: $output"
echo "Client output: $output"
printf '%s\n' "$output" | grep -Fxq 'Hello, smoke!' || fail "expected 'Hello, smoke!', got '$output'"
echo "OK: smoke test passed for Ambinder.A2A $version"
