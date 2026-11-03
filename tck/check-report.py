"""Grade an A2A TCK JSON report. Exit 0 = pass, 1 = fail. Stdlib only."""
import argparse
import json
import sys
from datetime import date, datetime, timezone

REQUIRED_TRANSPORTS = ("jsonrpc", "http_json", "grpc")
ENTRY_FIELDS = ("requirement", "transport", "level", "issue", "reason", "expires")


def load_json(path, what):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f), None
    except (OSError, ValueError) as e:
        return None, f"FAIL: cannot read {what} {path}: {e}"


def check_transports(report, failures):
    per_transport = report.get("per_transport")
    if not isinstance(per_transport, dict):
        failures.append("FAIL: report has no per_transport section")
        return
    for t in REQUIRED_TRANSPORTS:
        stats = per_transport.get(t)
        if not isinstance(stats, dict):
            failures.append(f"FAIL: transport {t} missing from report")
        elif not stats.get("total"):
            failures.append(f"FAIL: transport {t} ran 0 tests")


def check_known_issues(entries, today, failures):
    """Returns a dict of valid SHOULD entries keyed by (requirement, transport)."""
    valid = {}
    if not isinstance(entries, list):
        failures.append("FAIL: known-issues file must be a JSON list")
        return valid
    for i, entry in enumerate(entries):
        if not isinstance(entry, dict):
            failures.append(f"FAIL: known issue #{i} is not an object")
            continue
        name = f"{entry.get('requirement')}/{entry.get('transport')}"
        missing = [f for f in ENTRY_FIELDS if not entry.get(f)]
        if missing:
            failures.append(f"FAIL: known issue {name} missing fields: {', '.join(missing)}")
            continue
        problems = []
        if entry["level"] != "SHOULD":
            problems.append(f"level is {entry['level']}, only SHOULD can be a known issue")
        if not str(entry["issue"]).startswith("https://"):
            problems.append("issue must be an https:// URL")
        try:
            expires = date.fromisoformat(str(entry["expires"]))
            if expires < today:
                problems.append(f"expired on {expires}")
        except ValueError:
            problems.append(f"unparsable expires '{entry['expires']}'")
        if problems:
            failures.append(f"FAIL: known issue {name}: {'; '.join(problems)}")
            continue
        valid[(entry["requirement"], entry["transport"])] = entry
    return valid


def check_requirements(report, valid_issues, failures, warnings):
    requirements = report.get("per_requirement")
    if not isinstance(requirements, dict):
        failures.append("FAIL: report has no per_requirement section")
        return
    failing_now = set()
    for req_id, req in requirements.items():
        level = req.get("level")
        for transport, status in (req.get("transports") or {}).items():
            if status == "FAIL":
                failing_now.add((req_id, transport))
                if level == "MUST":
                    failures.append(f"FAIL: MUST {req_id} failed on {transport}")
                elif level == "SHOULD" and (req_id, transport) not in valid_issues:
                    failures.append(
                        f"FAIL: SHOULD {req_id} failed on {transport} and is not in known issues")
            elif status == "SKIPPED" and level == "MUST":
                warnings.append(f"warn: MUST {req_id} skipped on {transport}")
    for key in valid_issues:
        if key not in failing_now:
            failures.append(
                f"FAIL: known issue {key[0]}/{key[1]} no longer fails; remove it")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True)
    parser.add_argument("--known-issues", required=True)
    parser.add_argument("--today", help="YYYY-MM-DD, defaults to today (UTC)")
    args = parser.parse_args(argv)

    try:
        today = (date.fromisoformat(args.today) if args.today
                 else datetime.now(timezone.utc).date())
    except ValueError:
        parser.error(f"invalid --today: {args.today}")

    failures, warnings = [], []
    report, error = load_json(args.report, "report")
    if error:
        failures.append(error)
    elif not isinstance(report, dict):
        failures.append("FAIL: report is not a JSON object")
        report = None
    known, error = load_json(args.known_issues, "known issues")
    if error:
        failures.append(error)
        known = []

    valid = check_known_issues(known, today, failures)
    if report is not None:
        check_transports(report, failures)
        check_requirements(report, valid, failures, warnings)

    for line in failures + warnings:
        print(line)
    print(f"{len(failures)} failure(s), {len(warnings)} warning(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
