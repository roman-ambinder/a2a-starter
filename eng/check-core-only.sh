#!/usr/bin/env bash
# Publishes the core-only smoke app (Server + Client packages only) and fails if the output contains
# ASP.NET Core, gRPC, protobuf or Azure files, or the runtime config needs the ASP.NET Core shared framework.
# Then runs the published app. Usage: eng/check-core-only.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

dotnet publish "$root/tests/Ambinder.A2A.CoreOnly.Smoke" -c Release -o "$out" --nologo -v:q

echo "Published files:"
( cd "$out" && ls -1 *.dll | sed 's/^/  /' )

bad=0
for pattern in 'Microsoft.AspNetCore.*.dll' 'Grpc.*.dll' 'Google.Protobuf*.dll' 'Azure.*.dll'; do
  # shellcheck disable=SC2231
  for f in "$out"/$pattern; do
    if [ -e "$f" ]; then
      echo "ERROR: unexpected file in core-only output: $(basename "$f")" >&2
      bad=1
    fi
  done
done

if grep -q 'Microsoft.AspNetCore.App' "$out"/*.runtimeconfig.json; then
  echo "ERROR: runtimeconfig.json names Microsoft.AspNetCore.App" >&2
  bad=1
fi

if [ "$bad" -ne 0 ]; then
  exit 1
fi

dotnet "$out/Ambinder.A2A.CoreOnly.Smoke.dll"
echo "OK: core packages run without ASP.NET Core, gRPC or Azure."
