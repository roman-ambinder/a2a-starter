import json
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "check-report.py")
TRANSPORTS = ("jsonrpc", "http_json", "grpc")


def requirement(level, **transport_overrides):
    transports = {t: "PASS" for t in TRANSPORTS}
    transports.update(transport_overrides)
    return {"level": level, "status": "PASS", "transports": transports, "errors": []}


def per_transport(**totals):
    result = {t: {"total": 5, "passed": 5, "failed": 0, "skipped": 0} for t in TRANSPORTS}
    for name, total in totals.items():
        result[name]["total"] = total
    return result


def issue(**overrides):
    entry = {
        "requirement": "REQ-S",
        "transport": "grpc",
        "level": "SHOULD",
        "issue": "https://github.com/a2aproject/a2a-tck/issues/1",
        "reason": "upstream bug",
        "expires": "2027-01-01",
    }
    entry.update(overrides)
    return entry


class CheckReportTests(unittest.TestCase):
    def run_checker(self, report=None, known=None, today="2026-11-03", raw_report=None):
        with tempfile.TemporaryDirectory() as tmp:
            report_path = os.path.join(tmp, "report.json")
            known_path = os.path.join(tmp, "known.json")
            if raw_report is not None:
                with open(report_path, "w") as f:
                    f.write(raw_report)
            elif report is not None:
                with open(report_path, "w") as f:
                    json.dump(report, f)
            with open(known_path, "w") as f:
                json.dump(known or [], f)
            proc = subprocess.run(
                [sys.executable, "-I", SCRIPT, "--report", report_path,
                 "--known-issues", known_path, "--today", today],
                capture_output=True, text=True)
            return proc.returncode, proc.stdout + proc.stderr

    def base(self, **reqs):
        return {"per_requirement": reqs, "per_transport": per_transport()}

    def test_all_pass_exits_0(self):
        code, out = self.run_checker(self.base(**{"REQ-M": requirement("MUST")}))
        self.assertEqual(code, 0, out)

    def test_missing_report_exits_1(self):
        code, out = self.run_checker(report=None)
        self.assertEqual(code, 1, out)
        self.assertIn("FAIL:", out)

    def test_not_json_exits_1(self):
        code, out = self.run_checker(raw_report="not json")
        self.assertEqual(code, 1, out)

    def test_missing_transport_exits_1(self):
        report = self.base()
        del report["per_transport"]["grpc"]
        code, out = self.run_checker(report)
        self.assertEqual(code, 1, out)
        self.assertIn("grpc", out)

    def test_zero_total_transport_exits_1(self):
        report = {"per_requirement": {}, "per_transport": per_transport(http_json=0)}
        code, out = self.run_checker(report)
        self.assertEqual(code, 1, out)
        self.assertIn("http_json", out)

    def test_must_fail_exits_1_even_if_listed(self):
        report = self.base(**{"REQ-M": requirement("MUST", grpc="FAIL")})
        known = [issue(requirement="REQ-M", level="MUST")]
        code, out = self.run_checker(report, known)
        self.assertEqual(code, 1, out)
        self.assertGreaterEqual(out.count("FAIL:"), 2, out)
        self.assertIn("REQ-M", out)
        self.assertIn("MUST", out)

    def test_should_fail_unlisted_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        code, out = self.run_checker(report)
        self.assertEqual(code, 1, out)
        self.assertIn("REQ-S", out)

    def test_should_fail_listed_exits_0(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        code, out = self.run_checker(report, [issue()])
        self.assertEqual(code, 0, out)

    def test_expired_entry_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        code, out = self.run_checker(report, [issue(expires="2026-11-30")], today="2026-12-01")
        self.assertEqual(code, 1, out)
        self.assertIn("expired", out)

    def test_entry_that_passes_now_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD")})
        code, out = self.run_checker(report, [issue()])
        self.assertEqual(code, 1, out)
        self.assertIn("no longer fails", out)

    def test_malformed_entry_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        entry = issue()
        del entry["expires"]
        code, out = self.run_checker(report, [entry])
        self.assertEqual(code, 1, out)

    def test_non_https_issue_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        code, out = self.run_checker(report, [issue(issue="http://x")])
        self.assertEqual(code, 1, out)

    def test_unparsable_expires_exits_1(self):
        report = self.base(**{"REQ-S": requirement("SHOULD", grpc="FAIL")})
        code, out = self.run_checker(report, [issue(expires="soon")])
        self.assertEqual(code, 1, out)

    def test_must_skipped_warns_but_passes(self):
        report = self.base(**{"REQ-M": requirement("MUST", grpc="SKIPPED")})
        code, out = self.run_checker(report)
        self.assertEqual(code, 0, out)
        self.assertIn("warn:", out)

    def test_may_fail_ignored(self):
        report = self.base(**{"REQ-Y": requirement("MAY", grpc="FAIL")})
        code, out = self.run_checker(report)
        self.assertEqual(code, 0, out)


if __name__ == "__main__":
    unittest.main()
