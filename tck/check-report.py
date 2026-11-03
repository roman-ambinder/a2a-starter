"""Grade A2A TCK reports, one compatibility.json per transport. Exit 0 = pass, 1 = fail. Standard library only.

Rules (a problem fails the job):
 1. A MUST requirement fails and no valid known issue covers it.
 2. A SHOULD requirement fails and is not in the known issues.
 3. A MUST known issue is not a proven TCK bug (needs level, kind "tck-bug", spec, and an https: or draft: issue).
 4. A known issue has expired.
 5. A known issue no longer fails: remove it.
 6. A requirement listed under a claimed behaviour is skipped, not tested or missing.
 7. A known issue lacks suite, issue, reason or expires, or its suite is not "tck" or "acts".
A requirement with a top-level FAIL fails even when its transport entry says PASS (the TCK can overwrite that entry).
MAY failures are warnings. Skipped requirements are not problems unless a claimed behaviour lists them. Rules 3-5 read only entries with suite "tck"; "acts" entries are checked by the ACTS job.
"""
import argparse
import json
import sys
from datetime import date, datetime, timezone

BINDINGS = ("jsonrpc", "http_json", "grpc")
SUITES = ("tck", "acts")
LEVELS = ("MUST", "SHOULD", "MAY")
REQUIRED_FIELDS = ("suite", "requirement", "transport", "issue", "reason", "expires")
NOT_RUN = ("SKIPPED", "NOT TESTED")


def entry_name(entry):
    return f"{entry.get('requirement')}/{entry.get('transport')}"


def requirement_levels(reports):
    levels = {}
    for report in reports.values():
        for req_id, req in (report.get("per_requirement") or {}).items():
            if isinstance(req, dict) and req.get("level") in LEVELS:
                levels.setdefault(req_id, req["level"])
    return levels


def check_known_issues(known, levels, today, problems):
    """Validates every entry. Returns the valid "tck" entries keyed by (requirement, transport)."""
    entries = known.get("entries") if isinstance(known, dict) else None
    if not isinstance(entries, list):
        problems.append('known-issues file must be an object with an "entries" list')
        return {}
    valid, seen = {}, set()
    for i, entry in enumerate(entries):
        if not isinstance(entry, dict):
            problems.append(f"known issue #{i} is not an object")
            continue
        name = entry_name(entry)
        missing = [f for f in REQUIRED_FIELDS if not entry.get(f)]
        if missing:
            problems.append(f"known issue {name} missing fields: {', '.join(missing)}")
            continue
        if entry["suite"] not in SUITES:
            problems.append(f"known issue {name}: suite is {entry['suite']!r}, expected \"tck\" or \"acts\"")
            continue
        if entry["suite"] != "tck":
            continue
        issues = []
        level = entry.get("level") or levels.get(entry["requirement"], "SHOULD")
        if "MUST" in (entry.get("level"), levels.get(entry["requirement"])):
            if entry.get("level") != "MUST" or entry.get("kind") != "tck-bug" or not entry.get("spec"):
                issues.append('a MUST entry is allowed only as a proven TCK bug: it needs level "MUST", kind "tck-bug" and a non-empty spec')
        elif level != "SHOULD":
            issues.append(f"level is {level}, only SHOULD or a proven MUST can be a known issue")
        if level == "MUST" and levels.get(entry["requirement"]) not in (None, "MUST"):
            issues.append(f"level is MUST but the TCK reports {levels[entry['requirement']]}")
        if not str(entry["issue"]).startswith(("https://", "draft:")):
            issues.append("issue must start with https:// or draft:")
        try:
            expires = date.fromisoformat(str(entry["expires"]))
            if expires < today:
                issues.append(f"expired on {expires}")
        except ValueError:
            issues.append(f"unparsable expires '{entry['expires']}'")
        key = (entry["requirement"], entry["transport"])
        if key in seen:
            problems.append(f"duplicate known issue {name}")
            continue
        seen.add(key)
        if issues:
            problems.append(f"known issue {name}: {'; '.join(issues)}")
        else:
            valid[key] = {**entry, "level": level}
    return valid


def result_for(req, transport):
    """The result of one requirement on one transport; falls back to the top-level status."""
    transports = req.get("transports")
    if isinstance(transports, dict) and transport in transports:
        return transports[transport]
    return req.get("status")


def failing_transports(req, transport):
    """The transports this report counts a failure against: its own, plus non-binding ones such as agent_card."""
    transports = req.get("transports") if isinstance(req.get("transports"), dict) else {}
    scope = {transport} | {t for t in transports if t not in BINDINGS}
    failed = {t for t in scope if transports.get(t) == "FAIL"}
    # A top-level FAIL with a PASS (or no) transport entry is still a failure: the TCK lets a later passing test
    # overwrite the entry.
    if req.get("status") == "FAIL" and not failed and (not transports or transports.get(transport) == "PASS"):
        failed = {transport}
    return failed


def check(reports, known, behaviors, today):
    """Grades the reports. `reports` maps a transport name to its parsed compatibility.json.

    Returns (problems, warnings): lists of one-line messages. No problems means the job passes.
    """
    problems, warnings = [], []
    valid = check_known_issues(known, requirement_levels(reports), today, problems)
    failing_now = set()

    for transport, report in reports.items():
        stats = report.get("per_transport")
        if isinstance(stats, dict):
            if not isinstance(stats.get(transport), dict):
                problems.append(f"transport {transport} missing from the report")
            elif not stats[transport].get("total"):
                problems.append(f"transport {transport} ran 0 tests")
        requirements = report.get("per_requirement")
        if not isinstance(requirements, dict):
            problems.append(f"report for {transport} has no per_requirement section")
            continue

        for req_id, req in requirements.items():
            if not isinstance(req, dict):
                problems.append(f"requirement {req_id} is malformed (not an object)")
                continue
            level = req.get("level")
            for failed_on in sorted(failing_transports(req, transport)):
                if (req_id, failed_on) in failing_now:
                    continue  # a non-binding transport such as agent_card shows up in every report
                failing_now.add((req_id, failed_on))
                if level not in LEVELS:
                    problems.append(f"{req_id} failed on {failed_on} with unknown level {level!r}")
                elif level == "MAY":
                    warnings.append(f"MAY {req_id} failed on {failed_on}")
                else:
                    entry = valid.get((req_id, failed_on))
                    if entry is not None and entry["level"] == level:
                        continue
                    suffix = " and is not in known issues" if level == "SHOULD" else ""
                    problems.append(f"{level} {req_id} failed on {failed_on}{suffix}")

    covered = set(reports) | {t for _, t in failing_now if t not in BINDINGS}
    for (req_id, transport) in valid:
        if transport in covered and (req_id, transport) not in failing_now:
            problems.append(f"known issue {req_id}/{transport} no longer fails; remove it")

    for behavior in (behaviors or {}).get("behaviors", []):
        for req_id in behavior.get("requirements", []):
            for transport, report in reports.items():
                req = (report.get("per_requirement") or {}).get(req_id)
                result = result_for(req, transport) if isinstance(req, dict) else None
                if result is None or result in NOT_RUN:
                    problems.append(
                        f"behaviour {behavior.get('id')} lists {req_id}, but it is {result or 'missing from the report'} "
                        f"on {transport}: listed but not implemented fails, never skips")
    return problems, warnings


def load_json(path, what):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f), None
    except (OSError, ValueError) as e:
        return None, f"cannot read {what} {path}: {e}"


def parse_report_args(values, parser):
    pairs = {}
    for value in values:
        name, sep, path = value.partition("=")
        if not sep or not name or not path:
            parser.error(f"--report must be transport=path, got {value!r}")
        if name in pairs:
            parser.error(f"--report given twice for {name}")
        pairs[name] = path
    return pairs


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--report", action="append", required=True, metavar="TRANSPORT=PATH",
                        help="compatibility.json of one transport run; repeat per transport")
    parser.add_argument("--known-issues", required=True)
    parser.add_argument("--behaviors", help="sut-behaviors.yaml (JSON syntax); enables rule 6")
    parser.add_argument("--today", help="YYYY-MM-DD, defaults to today (UTC)")
    args = parser.parse_args(argv)

    try:
        today = date.fromisoformat(args.today) if args.today else datetime.now(timezone.utc).date()
    except ValueError:
        parser.error(f"invalid --today: {args.today}")

    errors, reports = [], {}
    for transport, path in parse_report_args(args.report, parser).items():
        report, error = load_json(path, f"{transport} report")
        if error:
            errors.append(error)
        elif not isinstance(report, dict):
            errors.append(f"{transport} report is not a JSON object")
        else:
            reports[transport] = report
    known, error = load_json(args.known_issues, "known issues")
    if error:
        errors.append(error)
        known = {"entries": []}
    behaviors = {"behaviors": []}
    if args.behaviors:
        behaviors, error = load_json(args.behaviors, "behaviours")
        if error:
            errors.append(error)
            behaviors = {"behaviors": []}

    problems, warnings = check(reports, known, behaviors, today)
    problems = errors + problems
    for line in problems:
        print(f"FAIL: {line}")
    for line in warnings:
        print(f"WARN: {line}")
    print(f"{len(problems)} failure(s), {len(warnings)} warning(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
