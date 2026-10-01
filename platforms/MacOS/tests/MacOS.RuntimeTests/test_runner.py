import copy
import unittest

from run import load_manifest, validate_result


class ResultContractTests(unittest.TestCase):
    def setUp(self):
        self.manifest = {"name": "layout", "expectedCases": 6}
        self.result = {
            "scenario": "layout", "outcome": "passed", "exitCode": 0,
            "assertions": 25, "cases": 6, "expectedCases": 6,
            "expectedAssertions": None, "failureId": None, "message": "All declared cases passed."
        }

    def test_complete_result(self):
        validate_result(self.manifest, self.result, 0)

    def test_zero_assertions_cannot_pass(self):
        self.result["assertions"] = 0
        with self.assertRaises(ValueError):
            validate_result(self.manifest, self.result, 0)

    def test_partial_cases_cannot_pass(self):
        self.result["cases"] = 5
        with self.assertRaises(ValueError):
            validate_result(self.manifest, self.result, 0)

    def test_wrong_scenario_cannot_pass(self):
        self.result["scenario"] = "another"
        with self.assertRaises(ValueError):
            validate_result(self.manifest, self.result, 0)

    def test_process_exit_must_match_result(self):
        with self.assertRaises(ValueError):
            validate_result(self.manifest, self.result, 1)

    def test_exact_assertion_count(self):
        self.manifest["expectedAssertions"] = 26
        self.result["expectedAssertions"] = 26
        with self.assertRaises(ValueError):
            validate_result(self.manifest, self.result, 0)

    def test_baseline_requires_exact_failure_not_any_nonzero(self):
        expectation = load_manifest("layout")["baseline"]
        baseline = copy.copy(self.result)
        baseline.update(outcome="failed", exitCode=1, cases=0,
                        failureId=expectation["failureId"], message=expectation["message"])
        validate_result(self.manifest, baseline, 1, expectation)
        baseline["failureId"] = "exception"
        baseline["message"] = "AppKit could not start"
        with self.assertRaises(ValueError):
            validate_result(self.manifest, baseline, 1, expectation)

    def test_unknown_manifest_rejected(self):
        with self.assertRaises(FileNotFoundError):
            load_manifest("unknown-scenario")

    def test_path_selector_rejected(self):
        with self.assertRaises(ValueError):
            load_manifest("../layout")


if __name__ == "__main__":
    unittest.main()
