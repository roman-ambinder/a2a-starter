#!/usr/bin/env bash
# Coverage gate (spec 12 section 6).
#
#   gate.sh --coverage FILE [--coverage FILE ...] [--thresholds FILE] [--baseline FILE] [--touched A,B] [--summary-out FILE]
#
# Reads Cobertura XML (one package element per assembly) and checks every package listed in thresholds.json against its
# line and branch threshold. A listed package that has no data in the run (it does not exist yet) is skipped with a notice.
# With --baseline (a summary JSON written earlier by --summary-out, for example from master) it also fails when the line
# coverage of the touched packages, taken together, dropped by more than 1 point. Without a baseline the delta check is
# skipped with a notice. --touched defaults to every gated package that has data. Exit 0 = pass, 1 = gate failed, 2 = usage.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
thresholds="$here/thresholds.json"
baseline=""
touched=""
summary_out=""
coverage=()

while [ $# -gt 0 ]; do
  case "$1" in
    --coverage) coverage+=("${2:?--coverage needs a file}"); shift 2 ;;
    --thresholds) thresholds="${2:?}"; shift 2 ;;
    --baseline) baseline="${2:?}"; shift 2 ;;
    --touched) touched="${2:?}"; shift 2 ;;
    --summary-out) summary_out="${2:?}"; shift 2 ;;
    *) echo "gate.sh: unknown argument '$1'" >&2; exit 2 ;;
  esac
done
[ "${#coverage[@]}" -gt 0 ] || { echo "gate.sh: --coverage is required" >&2; exit 2; }

exec python3 -I - "$thresholds" "$baseline" "$touched" "$summary_out" "${coverage[@]}" <<'PY'
import json, os, sys
import xml.etree.ElementTree as ET

thresholds_path, baseline_path, touched_arg, summary_out, *cov_files = sys.argv[1:]
gha = os.environ.get("GITHUB_ACTIONS") == "true"

def notice(msg):
    print(("::notice::" if gha else "NOTICE: ") + msg)

def error(msg):
    print(("::error::" if gha else "ERROR: ") + msg)

for f in cov_files:
    if not os.path.isfile(f):
        error(f"coverage file not found: {f}")
        sys.exit(1)

with open(thresholds_path, encoding="utf-8") as fh:
    thresholds = json.load(fh)["packages"]

# package -> {(file, line): [hits, cond_covered, cond_total]}; max over duplicates (merged runs, nested/async classes).
pkgs = {}
for f in cov_files:
    for p in ET.parse(f).getroot().iter("package"):
        lines = pkgs.setdefault(p.get("name"), {})
        for cls in p.iter("class"):
            fname = cls.get("filename", "")
            for ln in cls.iter("line"):
                key = (fname, ln.get("number"))
                hits = int(ln.get("hits", "0"))
                cc, ct = 0, 0
                cond = ln.get("condition-coverage")
                if (ln.get("branch") or "").lower() == "true" and cond and "(" in cond:
                    cc, ct = (int(x) for x in cond[cond.index("(") + 1:cond.index(")")].split("/"))
                cur = lines.get(key)
                if cur is None:
                    lines[key] = [hits, cc, ct]
                else:
                    cur[0] = max(cur[0], hits)
                    if ct > cur[2]:
                        cur[1], cur[2] = cc, ct
                    elif ct == cur[2]:
                        cur[1] = max(cur[1], cc)

summary = {}
for name, lines in pkgs.items():
    summary[name] = {
        "lines_covered": sum(1 for v in lines.values() if v[0] > 0),
        "lines_valid": len(lines),
        "branches_covered": sum(v[1] for v in lines.values()),
        "branches_valid": sum(v[2] for v in lines.values()),
    }

def pct(c, v):
    return 100.0 if v == 0 else 100.0 * c / v

failed = False
print(f"{'package':<46} {'line':>8} {'min':>5} {'branch':>8} {'min':>5}")
gated = []
for name, th in thresholds.items():
    s = summary.get(name)
    if s is None or s["lines_valid"] == 0:
        notice(f"{name}: no coverage data, scope skipped (package does not exist yet or was not part of this run)")
        continue
    gated.append(name)
    lp = pct(s["lines_covered"], s["lines_valid"])
    bp = pct(s["branches_covered"], s["branches_valid"])
    print(f"{name:<46} {lp:7.2f}% {th['line']:>4}% {bp:7.2f}% {th['branch']:>4}%")
    if lp < th["line"]:
        error(f"{name}: line coverage {lp:.2f}% is under the {th['line']}% threshold")
        failed = True
    if bp < th["branch"]:
        error(f"{name}: branch coverage {bp:.2f}% is under the {th['branch']}% threshold")
        failed = True

if summary_out:
    with open(summary_out, "w", encoding="utf-8") as fh:
        json.dump({"packages": summary}, fh, indent=2, sort_keys=True)

# Delta against the baseline summary.
base = None
if not baseline_path:
    notice("coverage delta check skipped: no baseline summary given")
elif not os.path.isfile(baseline_path):
    notice(f"coverage delta check skipped: baseline summary '{baseline_path}' not found")
else:
    with open(baseline_path, encoding="utf-8") as fh:
        base = json.load(fh)["packages"]
if base is not None:
    touched = [t for t in touched_arg.split(",") if t] if touched_arg else gated
    touched = [t for t in touched if t in summary and t in base]
    if not touched:
        notice("coverage delta check skipped: none of the touched packages is in both this run and the baseline")
    else:
        nc = sum(summary[t]["lines_covered"] for t in touched); nv = sum(summary[t]["lines_valid"] for t in touched)
        bc = sum(base[t]["lines_covered"] for t in touched); bv = sum(base[t]["lines_valid"] for t in touched)
        now, then = pct(nc, nv), pct(bc, bv)
        print(f"touched packages ({', '.join(touched)}): line coverage {now:.2f}% vs baseline {then:.2f}% ({now - then:+.2f} points)")
        if then - now > 1.0:
            error(f"coverage of the touched packages dropped {then - now:.2f} points against the baseline (limit 1.00)")
            failed = True

print("FAIL" if failed else "PASS")
sys.exit(1 if failed else 0)
PY
