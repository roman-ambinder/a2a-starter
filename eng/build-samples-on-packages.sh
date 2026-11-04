#!/usr/bin/env bash
# Builds the getting-started and demo samples against the packed Ambinder.A2A.* packages instead of the projects in src/.
# Needs the packages first: dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages
# Usage: eng/build-samples-on-packages.sh [version]   (default: the version of the Ambinder.A2A.Protocol package in artifacts/packages)
# Restores into a fresh package folder, so the local feed (eng/samples-feed/NuGet.config) is really used and nothing is taken from the
# global cache, and builds into a temporary --artifacts-path, so bin/ and obj/ of the normal build stay untouched.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
feed="$root/artifacts/packages"
version="${1:-${A2A_PACKAGE_VERSION:-}}"
if [ -z "$version" ]; then
  protocol="$(ls "$feed"/Ambinder.A2A.Protocol.*.nupkg 2>/dev/null | head -n 1 || true)"
  [ -n "$protocol" ] || { echo "ERROR: no Ambinder.A2A.Protocol package in $feed; run dotnet pack first or pass a version." >&2; exit 1; }
  version="$(basename "$protocol" .nupkg)"
  version="${version#Ambinder.A2A.Protocol.}"
fi
echo "Samples on packages, version $version"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

projects=(
  samples/GettingStarted.Agent/GettingStarted.Agent.csproj
  samples/GettingStarted.Client/GettingStarted.Client.csproj
  samples/AgentServer/AgentServer.csproj
  samples/AgentClient/AgentClient.csproj
)
props=(-p:A2AUsePackages=true "-p:A2APackageVersion=$version")

for project in "${projects[@]}"; do
  name="$(basename "$project" .csproj)"
  echo "== $name"
  dotnet restore "$root/$project" "${props[@]}" --configfile "$root/eng/samples-feed/NuGet.config" \
    --packages "$work/packages" --artifacts-path "$work/artifacts" --nologo -v:q
  dotnet build "$root/$project" -c Release --no-restore "${props[@]}" --artifacts-path "$work/artifacts" --nologo -v:q

  # Every Ambinder.A2A.* library of the product in the resolved graph must be a package (sample projects such as SampleTelemetry stay projects).
  assets="$(find "$work/artifacts/obj/$name" -name project.assets.json | head -n 1)"
  [ -n "$assets" ] || { echo "ERROR: no project.assets.json for $name" >&2; exit 1; }
  packages="$(grep -E '^\s{4}"Ambinder\.A2A\.[^"]*/[^"]*": \{' "$assets" | grep -v '"Ambinder\.A2A\.Samples\.' | sed -E 's/^\s*"([^"]*)".*/\1/' || true)"
  [ -n "$packages" ] || { echo "ERROR: $name resolved no Ambinder.A2A.* library" >&2; exit 1; }
  while IFS= read -r lib; do
    type="$(grep -A12 -F "    \"$lib\": {" "$assets" | grep -m1 '"type"' | sed -E 's/.*"type": "([^"]*)".*/\1/')"
    echo "   $lib: $type"
    [ "$type" = "package" ] || { echo "ERROR: $lib is a $type in $name, expected a package" >&2; exit 1; }
  done <<< "$packages"
done
echo "OK: all samples built on the $version packages."
