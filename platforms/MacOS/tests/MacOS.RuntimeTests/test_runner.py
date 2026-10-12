import copy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from run import RuntimeRunner, contained, load_manifest, read_build_output, validate_result


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

    def test_selector_must_start_with_lowercase_letter(self):
        for name in ("1layout", "-layout", "Layout"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                load_manifest(name)


class BuildOutputTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.bundle = self.root / "Debug" / "App with spaces.app"
        self.executable = self.bundle / "Contents" / "MacOS" / "NativeApp"
        self.executable.parent.mkdir(parents=True)
        self.executable.touch()
        self.metadata = self.root / "sdk-build-output.txt"

    def write_metadata(self, *paths):
        self.metadata.write_text("\n".join(map(str, paths)) + "\n")

    def read_output(self):
        return read_build_output(self.metadata, self.root / "build.log", self.root / "build.binlog")

    def test_sdk_build_output_with_spaces(self):
        self.write_metadata(self.bundle, self.executable)
        output = self.read_output()
        self.assertEqual(output.bundle, self.bundle)
        self.assertEqual(output.executable, self.executable)

    def test_msbuild_utf8_bom_metadata_is_accepted(self):
        self.metadata.write_text(f"{self.bundle}\n{self.executable}\n", encoding="utf-8-sig")
        self.assertEqual(self.read_output().bundle, self.bundle)

    def test_callers_cannot_override_the_recording_target_or_destination(self):
        runner = RuntimeRunner("layout", self.root / "evidence")
        for key in ("RuntimeTestBuildTarget", "RuntimeTestBuildMetadata",
                    "runtimetestbuildtarget", "RUNTIMETESTBUILDMETADATA"):
            with self.subTest(key=key), patch.object(runner, "command") as command:
                with self.assertRaisesRegex(ValueError, "runner owns"):
                    runner.build("fixed", properties={key: "ignored"})
                command.assert_not_called()

    def test_extra_arguments_cannot_override_owned_properties(self):
        runner = RuntimeRunner("layout", self.root / "evidence")
        arguments = (
            "-p:RuntimeTestBuildTarget=Build",
            "/p:runtimetestbuildmetadata=old.txt",
            "--property:RuntimeTestBuildTarget=Build",
            "-property:Other=true;RUNTIMETESTBUILDMETADATA=old.txt",
            "/property:Other=true,RuntimeTestBuildTarget=Build",
            "--property=RuntimeTestBuildTarget=Build",
        )
        for argument in arguments:
            with self.subTest(argument=argument), patch.object(runner, "command") as command:
                with self.assertRaisesRegex(ValueError, "Extra arguments"):
                    runner.build("publish", publish=True, extra_args=(argument,))
                command.assert_not_called()

    def test_response_files_cannot_bypass_property_ownership(self):
        runner = RuntimeRunner("layout", self.root / "evidence")
        for argument in ("@build.rsp", ' "@build.rsp"'):
            with self.subTest(argument=argument), patch.object(runner, "command") as command:
                with self.assertRaisesRegex(ValueError, "response files"):
                    runner.build("publish", publish=True, extra_args=(argument,))
                command.assert_not_called()

    def test_publish_uses_sdk_output_without_publish_directory(self):
        runner = RuntimeRunner("layout", self.root / "evidence")

        def command(args, log, **kwargs):
            if log.name == "build.log":
                self.assertEqual(str(args[1]), "publish")
                self.assertIn("-p:RuntimeTestBuildTarget=Publish", args)
                self.assertIn("-p:CreatePackage=false", args)
                metadata_arg = next(str(arg) for arg in args if str(arg).startswith("-p:RuntimeTestBuildMetadata="))
                Path(metadata_arg.split("=", 1)[1]).write_text(f"{self.bundle}\n{self.executable}\n")
            elif log.name == "registry.json":
                log.write_text('[{"Name":"layout","ExpectedCases":6,"ExpectedAssertions":28}]')
            elif log.name == "unknown-selector.log":
                log.write_text("Unknown scenario: __unregistered_scenario__")
                return 1
            return 0

        with patch.object(runner, "command", side_effect=command):
            output = runner.build("publish", publish=True, extra_args=("-p:CreatePackage=false",))
        self.assertEqual(output.bundle, self.bundle)
        self.assertNotIn("publish", output.bundle.parts)

    def test_missing_metadata_rejected_even_with_existing_bundle(self):
        with self.assertRaises(FileNotFoundError):
            self.read_output()

    def test_stale_metadata_rejected_before_build(self):
        runner = RuntimeRunner("layout", self.root / "evidence")
        directory = runner.stage_directory("fixed")
        (directory / "sdk-build-output.txt").write_text(f"{self.bundle}\n{self.executable}\n")
        with patch.object(runner, "command") as command:
            with self.assertRaisesRegex(RuntimeError, "stale build metadata"):
                runner.build("fixed")
            command.assert_not_called()

    def test_missing_executable_rejected(self):
        self.write_metadata(self.bundle, self.executable.parent / "missing")
        with self.assertRaises(FileNotFoundError):
            self.read_output()

    def test_ambiguous_or_relative_metadata_rejected(self):
        for paths in ((self.bundle,), (self.bundle, self.executable, self.executable),
                      ("relative.app", self.executable), (self.bundle, "")):
            with self.subTest(paths=paths):
                self.write_metadata(*paths)
                with self.assertRaises(ValueError):
                    self.read_output()

    def test_executable_outside_bundle_rejected(self):
        other = self.root / "other"
        other.touch()
        self.write_metadata(self.bundle, other)
        with self.assertRaisesRegex(ValueError, "inside the reported app"):
            self.read_output()

    def test_app_root_is_enforced_when_supplied(self):
        self.write_metadata(self.bundle, self.executable)
        with self.assertRaisesRegex(ValueError, "outside the requested"):
            read_build_output(self.metadata, None, None, self.root / "other")


class SourceOverlayTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        repo = patch("run.REPO", self.root)
        repo.start()
        self.addCleanup(repo.stop)
        self.runner = RuntimeRunner("layout", self.root / "evidence")
        self.source = self.root / "Handler.cs"
        self.original = b"\xef\xbb\xbfFirst Old\r\nSecond Old\r\n"
        self.source.write_bytes(self.original)
        self.overlay = {"source": "Handler.cs", "replace": {"old": "First Old", "new": "First New"}}
        self.runner.manifest["baseline"] = {"overlays": [self.overlay]}

    def test_replacement_restores_exact_bytes_including_bom_and_newlines(self):
        with self.runner.baseline_sources():
            self.assertEqual(self.original.replace(b"First Old", b"First New"), self.source.read_bytes())
        self.assertEqual(self.original, self.source.read_bytes())

    def test_body_failure_restores_source(self):
        with self.assertRaisesRegex(RuntimeError, "build failed"):
            with self.runner.baseline_sources():
                raise RuntimeError("build failed")
        self.assertEqual(self.original, self.source.read_bytes())

    def test_second_overlay_setup_failure_restores_first_source(self):
        self.runner.manifest["baseline"]["overlays"].append(
            {"source": "Missing.cs", "replace": {"old": "Old", "new": "New"}})
        with self.assertRaises(FileNotFoundError):
            with self.runner.baseline_sources():
                self.fail("Invalid overlay unexpectedly applied.")
        self.assertEqual(self.original, self.source.read_bytes())

    def test_duplicate_source_rejected_and_restored(self):
        self.runner.manifest["baseline"]["overlays"].append(copy.deepcopy(self.overlay))
        with self.assertRaisesRegex(ValueError, "distinct files"):
            with self.runner.baseline_sources():
                self.fail("Duplicate overlay unexpectedly applied.")
        self.assertEqual(self.original, self.source.read_bytes())

    def test_replacement_requires_one_actual_change(self):
        for old, new in (("Missing", "New"), ("Old", "New"), ("First Old", "First Old"), ("", "New")):
            self.overlay["replace"] = {"old": old, "new": new}
            with self.subTest(old=old, new=new), self.assertRaisesRegex(ValueError, "match exactly once"):
                with self.runner.baseline_sources():
                    self.fail("Invalid replacement unexpectedly applied.")
            self.assertEqual(self.original, self.source.read_bytes())

    def test_short_commit_rejected_without_running_git(self):
        self.runner.manifest["baseline"] = {"source": "Handler.cs", "commit": "1234567"}
        with patch.object(self.runner, "command") as command:
            with self.assertRaisesRegex(ValueError, "full immutable SHA"):
                with self.runner.baseline_sources():
                    self.fail("Mutable commit unexpectedly applied.")
            command.assert_not_called()
        self.assertEqual(self.original, self.source.read_bytes())

    def test_git_show_failure_restores_source(self):
        self.runner.manifest["baseline"] = {"source": "Handler.cs", "commit": "a" * 40}
        with patch.object(self.runner, "command"), patch("run.subprocess.check_output",
                                                       side_effect=RuntimeError("show failed")):
            with self.assertRaisesRegex(RuntimeError, "show failed"):
                with self.runner.baseline_sources():
                    self.fail("Failed git show unexpectedly applied.")
        self.assertEqual(self.original, self.source.read_bytes())

    def test_evidence_or_overlay_paths_cannot_escape_root(self):
        for relative in (".", "../outside", self.root.parent / "outside"):
            with self.subTest(relative=relative), self.assertRaises(ValueError):
                contained(self.root, relative)


if __name__ == "__main__":
    unittest.main()
