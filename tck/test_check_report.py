import datetime
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "check-report.py")
spec = importlib.util.spec_from_file_location("check_report", SCRIPT)
check_report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check_report)

TRANSPORTS = ("jsonrpc", "http_json", "grpc")
TODAY = datetime.date(2026, 11, 3)


def req(level, **results):
    """A requirement: its level and the result per transport (default PASS)."""
    return level, {t: results.get(t, "PASS") for t in TRANSPORTS}


def reports(**requirements):
    """One report per transport, like three TCK runs: a transport's run skips the other two."""
    result = {}
    for t in TRANSPORTS:
        per_requirement = {}
        for req_id, (level, outcome) in requirements.items():
            transports = {x: "SKIPPED" for x in TRANSPORTS}
            transports[t] = outcome[t]
            per_requirement[req_id] = {"level": level, "status": outcome[t], "transports": transports, "errors": []}
        result[t] = {
            "per_requirement": per_requirement,
            "per_transport": {x: {"total": 5, "passed": 5, "failed": 0, "skipped": 0} for x in TRANSPORTS},
        }
    return result


def issue(**overrides):
    entry = {
        "suite": "tck",
        "requirement": "REQ-S",
        "transport": "grpc",
        "level": "SHOULD",
        "issue": "https://github.com/a2aproject/a2a-tck/issues/1",
        "reason": "upstream bug",
        "expires": "2027-01-01",
    }
    entry.update(overrides)
    return entry


def must_bug(**overrides):
    fields = {"requirement": "REQ-M", "level": "MUST", "kind": "tck-bug", "spec": "v1.0.1 error table"}
    fields.update(overrides)
    return issue(**fields)


def behaviors(*pairs):
    return {"behaviors": [{"id": i, "requirements": list(r)} for i, r in pairs]}


class CheckTests(unittest.TestCase):
    def run_check(self, reps, entries=(), behaviours=None, today=TODAY):
        return check_report.check(reps, {"entries": list(entries)}, behaviours or {"behaviors": []}, today)

    def assert_problem(self, problems, *fragments):
        text = "\n".join(problems)
        for fragment in fragments:
            self.assertIn(fragment, text)

    def test_all_pass_has_no_problems(self):
        problems, warnings = self.run_check(reports(**{"REQ-M": req("MUST")}))
        self.assertEqual([], problems + warnings)

    # Rule 1
    def test_must_failure_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}))
        self.assertEqual(1, len(problems), problems)
        self.assert_problem(problems, "MUST REQ-M failed on grpc")

    def test_must_failure_on_each_transport_is_reported_once_per_transport(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL", jsonrpc="FAIL")}))
        self.assertEqual(2, len(problems), problems)

    # Rule 2
    def test_unlisted_should_failure_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}))
        self.assert_problem(problems, "SHOULD REQ-S failed on grpc", "not in known issues")

    def test_listed_should_failure_passes(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue()])
        self.assertEqual([], problems)

    def test_entry_for_another_transport_does_not_cover(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue(transport="jsonrpc")])
        self.assertEqual(2, len(problems), problems)  # unlisted failure, and the jsonrpc entry no longer fails

    def test_should_entry_without_level_is_taken_from_the_report(self):
        entry = issue()
        del entry["level"]
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [entry])
        self.assertEqual([], problems)

    # Rule 3
    def test_must_tck_bug_entry_covers_failure(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}), [must_bug()])
        self.assertEqual([], problems)

    def test_must_entry_accepts_a_draft_issue_reference(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}), [must_bug(issue="draft:tck-error-table")])
        self.assertEqual([], problems)

    def test_must_entry_without_kind_is_invalid_and_does_not_cover(self):
        entry = must_bug()
        del entry["kind"]
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}), [entry])
        self.assertEqual(2, len(problems), problems)
        self.assert_problem(problems, "tck-bug", "MUST REQ-M failed on grpc")

    def test_must_entry_without_spec_is_invalid(self):
        entry = must_bug()
        del entry["spec"]
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}), [entry])
        self.assert_problem(problems, "spec")

    def test_must_entry_with_should_level_is_invalid(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST", grpc="FAIL")}),
                                     [must_bug(level="SHOULD")])
        self.assert_problem(problems, "tck-bug", "MUST REQ-M failed on grpc")

    def test_issue_must_be_https_or_draft(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue(issue="http://x")])
        self.assert_problem(problems, "https://")

    # Rule 4
    def test_expired_entry_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}),
                                     [issue(expires="2026-11-02")])
        self.assert_problem(problems, "expired")

    def test_entry_expiring_today_is_still_valid(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}),
                                     [issue(expires="2026-11-03")])
        self.assertEqual([], problems)

    def test_unparsable_expires_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue(expires="soon")])
        self.assert_problem(problems, "expires")

    # Rule 5
    def test_entry_that_passes_now_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD")}), [issue()])
        self.assert_problem(problems, "REQ-S/grpc no longer fails; remove it")

    def test_must_entry_that_passes_now_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-M": req("MUST")}), [must_bug()])
        self.assert_problem(problems, "no longer fails")

    def test_entry_for_a_transport_without_a_report_is_not_checked(self):
        reps = reports(**{"REQ-S": req("SHOULD")})
        del reps["grpc"]
        problems, _ = self.run_check(reps, [issue()])
        self.assertEqual([], problems)

    # Rule 6
    def test_claimed_behaviour_that_is_skipped_fails(self):
        problems, _ = self.run_check(reports(R9=req("MUST", grpc="SKIPPED")), behaviours=behaviors(("tck-fake", ["R9"])))
        self.assertEqual(1, len(problems), problems)
        self.assert_problem(problems, "tck-fake", "R9", "grpc")

    def test_claimed_behaviour_that_is_not_tested_fails(self):
        problems, _ = self.run_check(reports(R9=req("MUST", jsonrpc="NOT TESTED")), behaviours=behaviors(("tck-fake", ["R9"])))
        self.assert_problem(problems, "tck-fake", "NOT TESTED")

    def test_claimed_behaviour_missing_from_the_report_fails(self):
        problems, _ = self.run_check(reports(R1=req("MUST")), behaviours=behaviors(("tck-fake", ["R9"])))
        self.assert_problem(problems, "tck-fake", "missing from the report")

    def test_claimed_behaviour_that_passes_is_fine(self):
        problems, _ = self.run_check(reports(R9=req("MUST")), behaviours=behaviors(("tck-ok", ["R9"]), ("tck-none", [])))
        self.assertEqual([], problems)

    # Rule 7
    def test_entry_missing_a_field_is_a_problem(self):
        for field in ("suite", "issue", "reason", "expires", "transport", "requirement"):
            entry = issue()
            del entry[field]
            problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [entry])
            self.assert_problem(problems, field)

    def test_entry_with_unknown_suite_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD")}), [issue(suite="other")])
        self.assert_problem(problems, "suite")

    def test_acts_entries_are_ignored_by_the_other_rules(self):
        acts = issue(suite="acts", expires="2020-01-01", issue="nonsense")
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD")}), [acts])
        self.assertEqual([], problems)

    def test_acts_entry_still_needs_its_fields(self):
        acts = issue(suite="acts")
        del acts["reason"]
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD")}), [acts])
        self.assert_problem(problems, "reason")

    def test_acts_entry_does_not_cover_a_tck_failure(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue(suite="acts")])
        self.assert_problem(problems, "SHOULD REQ-S failed on grpc")

    def test_duplicate_entry_is_a_problem(self):
        problems, _ = self.run_check(reports(**{"REQ-S": req("SHOULD", grpc="FAIL")}), [issue(), issue()])
        self.assert_problem(problems, "duplicate")

    def test_known_issues_must_be_an_object_with_entries(self):
        problems, _ = check_report.check(reports(), [], {"behaviors": []}, TODAY)
        self.assert_problem(problems, "entries")

    # MAY, skips and top-level status
    def test_may_failure_is_only_a_warning(self):
        problems, warnings = self.run_check(reports(**{"REQ-Y": req("MAY", grpc="FAIL")}))
        self.assertEqual([], problems)
        self.assert_problem(warnings, "MAY REQ-Y failed on grpc")

    def test_unclaimed_skipped_requirement_is_not_a_problem(self):
        problems, warnings = self.run_check(reports(**{"REQ-M": req("MUST", grpc="SKIPPED")}))
        self.assertEqual([], problems + warnings)

    def test_top_level_fail_with_a_passing_transport_is_a_problem(self):
        reps = reports(**{"REQ-M": req("MUST")})
        reps["http_json"]["per_requirement"]["REQ-M"]["status"] = "FAIL"
        problems, _ = self.run_check(reps)
        self.assert_problem(problems, "MUST REQ-M failed on http_json")

    def test_top_level_fail_can_be_covered_by_an_entry_for_that_transport(self):
        reps = reports(**{"REQ-M": req("MUST")})
        reps["http_json"]["per_requirement"]["REQ-M"]["status"] = "FAIL"
        problems, _ = self.run_check(reps, [must_bug(transport="http_json")])
        self.assertEqual([], problems)

    def test_failure_on_another_transport_in_this_run_is_not_counted(self):
        reps = reports(**{"REQ-M": req("MUST")})
        reps["grpc"]["per_requirement"]["REQ-M"]["transports"]["jsonrpc"] = "FAIL"
        problems, _ = self.run_check(reps)
        self.assertEqual([], problems)

    def test_agent_card_failure_counts_once_and_can_be_listed(self):
        reps = reports()
        for t in TRANSPORTS:
            reps[t]["per_requirement"]["CARD-1"] = {"level": "SHOULD", "status": "FAIL", "transports": {"agent_card": "FAIL"}}
        problems, _ = self.run_check(reps)
        self.assertEqual(1, len(problems), problems)
        problems, _ = self.run_check(reps, [issue(requirement="CARD-1", transport="agent_card")])
        self.assertEqual([], problems)

    def test_unknown_level_fail_is_reported(self):
        reps = reports(**{"REQ-X": req("MUST", grpc="FAIL")})
        del reps["grpc"]["per_requirement"]["REQ-X"]["level"]
        problems, _ = self.run_check(reps)
        self.assert_problem(problems, "unknown level")

    def test_malformed_requirement_is_reported(self):
        reps = reports()
        reps["grpc"]["per_requirement"]["REQ-X"] = "oops"
        problems, _ = self.run_check(reps)
        self.assert_problem(problems, "REQ-X")

    def test_transport_with_zero_tests_is_a_problem(self):
        reps = reports()
        reps["http_json"]["per_transport"]["http_json"]["total"] = 0
        problems, _ = self.run_check(reps)
        self.assert_problem(problems, "http_json ran 0 tests")

    def test_transport_missing_from_its_own_report_is_a_problem(self):
        reps = reports()
        del reps["grpc"]["per_transport"]["grpc"]
        problems, _ = self.run_check(reps)
        self.assert_problem(problems, "grpc missing")


class CommandLineTests(unittest.TestCase):
    def run_cli(self, reps, known=None, behaviours=None, today="2026-11-03", extra=()):
        with tempfile.TemporaryDirectory() as tmp:
            args = [sys.executable, "-I", SCRIPT, "--today", today]
            for t, report in reps.items():
                path = os.path.join(tmp, f"{t}.json")
                with open(path, "w") as f:
                    f.write(report if isinstance(report, str) else json.dumps(report))
                args += ["--report", f"{t}={path}"]
            known_path = os.path.join(tmp, "known.json")
            with open(known_path, "w") as f:
                json.dump(known if known is not None else {"entries": []}, f)
            args += ["--known-issues", known_path]
            if behaviours is not None:
                behaviors_path = os.path.join(tmp, "behaviors.yaml")
                with open(behaviors_path, "w") as f:
                    json.dump(behaviours, f)
                args += ["--behaviors", behaviors_path]
            proc = subprocess.run(args + list(extra), capture_output=True, text=True)
            return proc.returncode, proc.stdout + proc.stderr

    def test_passing_run_exits_0(self):
        code, out = self.run_cli(reports(**{"REQ-M": req("MUST")}))
        self.assertEqual(0, code, out)
        self.assertIn("0 failure(s)", out)

    def test_failing_run_exits_1_and_prints_a_fail_line(self):
        code, out = self.run_cli(reports(**{"REQ-M": req("MUST", grpc="FAIL")}))
        self.assertEqual(1, code, out)
        self.assertIn("FAIL: MUST REQ-M failed on grpc", out)

    def test_may_failure_prints_a_warning_and_exits_0(self):
        code, out = self.run_cli(reports(**{"REQ-Y": req("MAY", grpc="FAIL")}))
        self.assertEqual(0, code, out)
        self.assertIn("WARN: MAY REQ-Y failed on grpc", out)

    def test_unreadable_report_exits_1(self):
        reps = reports()
        reps["grpc"] = "not json"
        code, out = self.run_cli(reps)
        self.assertEqual(1, code, out)
        self.assertIn("FAIL:", out)
        self.assertNotIn("Traceback", out)

    def test_missing_report_file_exits_1(self):
        proc = subprocess.run(
            [sys.executable, "-I", SCRIPT, "--report", "grpc=/nonexistent/r.json", "--known-issues", "/nonexistent/k.json"],
            capture_output=True, text=True)
        self.assertEqual(1, proc.returncode, proc.stdout + proc.stderr)

    def test_report_argument_needs_a_transport_name(self):
        proc = subprocess.run(
            [sys.executable, "-I", SCRIPT, "--report", "r.json", "--known-issues", "k.json"], capture_output=True, text=True)
        self.assertEqual(2, proc.returncode)
        self.assertIn("transport=path", proc.stderr)

    def test_behaviours_file_is_checked(self):
        code, out = self.run_cli(reports(R9=req("MUST", grpc="SKIPPED")),
                                 behaviours={"behaviors": [{"id": "tck-fake", "requirements": ["R9"]}]})
        self.assertEqual(1, code, out)
        self.assertIn("tck-fake", out)

    def test_the_repository_files_are_well_formed(self):
        with open(os.path.join(HERE, "known-issues.json")) as f:
            known = json.load(f)
        with open(os.path.join(HERE, "sut-behaviors.yaml")) as f:
            behaviours = json.load(f)
        problems, _ = check_report.check({}, known, behaviours, TODAY)
        self.assertEqual([], problems)
        ids = [b["id"] for b in behaviours["behaviors"]]
        self.assertEqual(len(ids), len(set(ids)))
        for b in behaviours["behaviors"]:
            self.assertEqual({"id", "scenario", "response", "terminalState", "requirements"}, set(b))


if __name__ == "__main__":
    unittest.main()
