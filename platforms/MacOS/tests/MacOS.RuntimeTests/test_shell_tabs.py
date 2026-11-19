import copy
import unittest

from run import load_manifest, validate_result


class ShellTabsContractTests(unittest.TestCase):
    def setUp(self):
        self.manifest = load_manifest("shell-tabs")
        self.baseline = self.manifest["baseline"]
        self.result = {
            "scenario": "shell-tabs",
            "outcome": "passed",
            "exitCode": 0,
            "assertions": 63,
            "cases": 1,
            "expectedCases": 1,
            "expectedAssertions": 63,
            "failureId": None,
            "message": "All declared cases passed.",
        }

    def test_only_approved_renderer_files_are_overlaid(self):
        self.assertEqual(self.baseline["overlays"], [
            {
                "source": "platforms/MacOS/src/MacOS/Handlers/ShellHandler.cs",
                "commit": "b0767ac6d972e2aaa5168339632822604c02a1f0",
            },
            {
                "source": "platforms/MacOS/src/MacOS/Handlers/ShellHandler.Tabs.cs",
                "commit": "b0767ac6d972e2aaa5168339632822604c02a1f0",
            },
        ])

    def test_fixed_requires_all_63_assertions_and_final_case(self):
        validate_result(self.manifest, self.result, 0)
        for key, value in (("assertions", 62), ("assertions", 64), ("cases", 0)):
            result = copy.copy(self.result)
            result[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                validate_result(self.manifest, result, 0)

    def test_baseline_accepts_three_mounts_not_the_failed_two_mount_intermediate(self):
        self.assertEqual(self.baseline["failureId"], "shell-tabs.single-click-attachment")
        self.assertEqual(self.baseline["message"],
                         "native click attaches the selected page once (actual 3)")
        self.assertEqual(self.baseline["outcome"], "failed")
        self.assertEqual(self.baseline["exitCode"], 1)
        result = copy.copy(self.result)
        result.update(outcome="failed", exitCode=1, cases=0, assertions=10,
                      failureId=self.baseline["failureId"], message=self.baseline["message"])
        validate_result(self.manifest, result, 1, self.baseline)
        for count in (1, 2, 4):
            result["message"] = f"native click attaches the selected page once (actual {count})"
            with self.subTest(count=count), self.assertRaises(ValueError):
                validate_result(self.manifest, result, 1, self.baseline)

    def test_unrelated_failure_cannot_satisfy_baseline(self):
        result = copy.copy(self.result)
        result.update(outcome="failed", exitCode=1, cases=0, assertions=1,
                      failureId="exception", message="AppKit startup failed")
        with self.assertRaises(ValueError):
            validate_result(self.manifest, result, 1, self.baseline)

    def test_runtime_evidence_is_required(self):
        self.assertTrue({
            "initial.png", "selected-sixth.png", "assertions.txt",
            "click-refresh.json", "click-refresh-count.txt", "click-refresh-traces.txt",
        }.issubset(self.manifest["evidence"]))
        self.assertTrue({
            "disposed-view-state.txt", "reentrant-destination-state.txt",
            "reentrant-different-destination.png", "wide-sidebar.png",
            "handler-rebind.png",
        }.issubset(self.manifest["fixedEvidence"]))
