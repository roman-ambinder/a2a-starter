#!/usr/bin/env bash
# Self-test of eng/coverage/gate.sh against the sample Cobertura files in testdata/. Run in CI before the gate.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gate="$here/gate.sh"
td="$here/testdata"
th="$here/thresholds.json"
failed=0
total=0

# expect <name> <pass|fail> <output-grep-or-empty> -- gate args...
expect() {
  local name="$1" want="$2" pattern="$3"; shift 4
  total=$((total + 1))
  local out rc
  out="$(bash "$gate" --thresholds "$th" "$@" 2>&1)"; rc=$?
  local ok=1
  if [ "$want" = pass ] && [ "$rc" -ne 0 ]; then ok=0; fi
  if [ "$want" = fail ] && [ "$rc" -eq 0 ]; then ok=0; fi
  if [ -n "$pattern" ] && ! grep -qE -- "$pattern" <<<"$out"; then ok=0; fi
  if [ "$ok" -eq 1 ]; then echo "ok   - $name"; else echo "FAIL - $name (exit $rc, wanted $want, pattern '$pattern')"; echo "$out" | sed 's/^/       | /'; failed=$((failed + 1)); fi
}

expect "all scopes above threshold pass"            pass 'PASS'                      -- --coverage "$td/good.xml"
expect "line coverage under threshold fails"        fail 'Protocol.*line'            -- --coverage "$td/low-line.xml"
expect "branch coverage under threshold fails"      fail 'Protocol.*branch'          -- --coverage "$td/low-branch.xml"
expect "exactly at threshold passes"                pass 'PASS'                      -- --coverage "$td/exact-threshold.xml"
expect "scope without data is skipped with notice"  pass 'Stores.Cosmos.*skipped'    -- --coverage "$td/good.xml"
expect "no baseline: delta skipped with notice"     pass 'delta.*skipped'            -- --coverage "$td/good.xml"
expect "missing baseline file: delta skipped"       pass 'delta.*skipped'            -- --coverage "$td/good.xml" --baseline "$td/does-not-exist.json"
expect "drop over 1 point fails"                    fail 'dropped'                   -- --coverage "$td/good.xml" --baseline "$td/baseline-high.json"
expect "drop within 1 point passes"                 pass 'PASS'                      -- --coverage "$td/good.xml" --baseline "$td/baseline-close.json"
expect "untouched package drop is not checked"      pass 'PASS'                      -- --coverage "$td/good.xml" --baseline "$td/baseline-high.json" --touched Ambinder.A2A.Server
expect "touched package drop is checked"            fail 'dropped'                   -- --coverage "$td/good.xml" --baseline "$td/baseline-high.json" --touched Ambinder.A2A.Protocol
expect "missing coverage file fails"                fail 'not found'                 -- --coverage "$td/nope.xml"

# summary output round-trips as a baseline
total=$((total + 1))
tmp="$(mktemp -d)"
if bash "$gate" --thresholds "$th" --coverage "$td/good.xml" --summary-out "$tmp/s.json" >/dev/null 2>&1 \
   && bash "$gate" --thresholds "$th" --coverage "$td/good.xml" --baseline "$tmp/s.json" 2>&1 | grep -q PASS; then
  echo "ok   - summary-out is usable as baseline"
else
  echo "FAIL - summary-out is usable as baseline"; failed=$((failed + 1))
fi
rm -rf "$tmp"

echo "$((total - failed))/$total gate tests passed"
[ "$failed" -eq 0 ]
