"""Tests the structural oracle; does not pretend these are agent migrations."""

import importlib.util
from pathlib import Path
import plistlib
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("runner", Path(__file__).with_name("run_evals.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class FixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="xcode27-oracle-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "app"

    def migrate_oracle(self, kind="simple"):
        runner.fixture(self.root, kind)
        project = self.root / "MyApp.csproj"
        project.write_text(project.read_text()
                           .replace("""== 'ios'">14.0<""", """== 'ios'">15.0<""")
                           .replace("""== 'maccatalyst'">15.0<""", """== 'maccatalyst'">17.0<""")
                           .replace(">10.0.100<", ">10.0.110<"))
        if kind == "central":
            owner = self.root / "Directory.Packages.props"
            owner.write_text(owner.read_text().replace('Version="10.0.100"', 'Version="10.0.110"'))
        for platform in ("iOS", "MacCatalyst"):
            directory = self.root / "Platforms" / platform
            plist = directory / "Info.plist"
            data = plistlib.loads(plist.read_bytes())
            data["UIApplicationSceneManifest"] = {
                "UIApplicationSupportsMultipleScenes": False,
                "UISceneConfigurations": {"UIWindowSceneSessionRoleApplication": [{
                    "UISceneConfigurationName": "__MAUI_DEFAULT_SCENE_CONFIGURATION__",
                    "UISceneDelegateClassName": "SceneDelegate",
                }]},
            }
            plist.write_bytes(plistlib.dumps(data))
            (directory / "SceneDelegate.cs").write_text('using Foundation;\nusing Microsoft.Maui;\nnamespace MigrationFixture;\n[Register("SceneDelegate")]\npublic class SceneDelegate : MauiUISceneDelegate {}\n')

    def checks(self, kind="simple"):
        return runner.grade(self.root, kind, {})

    def test_pre_migration_fixture_fails_migration_oracle(self):
        runner.fixture(self.root, "simple")
        failures = [x["text"] for x in self.checks() if not x["passed"]]
        self.assertIn("iOS: correct manifest", failures)
        self.assertIn("MacCatalyst: registered MAUI scene delegate", failures)
        self.assertIn("Compatible .NET 10 MAUI servicing version", failures)

    def test_correct_structure_passes(self):
        self.migrate_oracle()
        self.assertTrue(all(x["passed"] for x in self.checks()), self.checks())

    def test_whitespace_version_is_rejected(self):
        self.migrate_oracle()
        project = self.root / "MyApp.csproj"
        project.write_text(project.read_text().replace(">15.0<", ">\n 15.0\n<"))
        self.assertFalse(next(x["passed"] for x in self.checks() if x["text"] == "iOS: exact inline minimum"))

    def test_duplicate_manifest_is_rejected(self):
        self.migrate_oracle()
        plist = self.root / "Platforms/iOS/Info.plist"
        text = plist.read_text()
        start = text.index("<dict>") + len("<dict>")
        text = text[:start] + "\n<key>UIApplicationSceneManifest</key><dict/>\n" + text[start:]
        plist.write_text(text)
        self.assertFalse(next(x["passed"] for x in self.checks() if x["text"] == "iOS: no duplicate root keys"))

    def test_central_versions_and_higher_minima_pass(self):
        self.migrate_oracle("central")
        self.assertTrue(all(x["passed"] for x in self.checks("central")), self.checks("central"))

    def test_audit_detects_changed_and_added_files(self):
        runner.fixture(self.root, "simple")
        before = runner.snapshot(self.root)
        self.assertTrue(runner.grade(self.root, "simple", before, audit=True)[0]["passed"])
        (self.root / "unexpected.cs").write_text("// unexpected edit\n")
        self.assertFalse(runner.grade(self.root, "simple", before, audit=True)[0]["passed"])

    def test_audit_detects_workflow_and_skill_mutations(self):
        runner.fixture(self.root, "simple")
        runner.install_skill(self.root)
        for relative in (".github/workflows/ci.yml", f".github/skills/{runner.NAME}/SKILL.md"):
            before = runner.snapshot(self.root)
            path = self.root / relative
            path.write_text(path.read_text() + "\nchanged\n")
            self.assertFalse(runner.grade(self.root, "simple", before, audit=True)[0]["passed"], relative)

    def test_audit_detects_new_build_outputs(self):
        runner.fixture(self.root, "simple")
        before = runner.snapshot(self.root, include_build_outputs=True)
        (self.root / "obj").mkdir()
        (self.root / "obj/generated.json").write_text("{}")
        self.assertFalse(runner.grade(self.root, "simple", before, audit=True)[0]["passed"])

    def test_trigger_requires_successful_matching_tool_completion(self):
        start = {"type": "tool.execution_start", "data": {"toolName": "skill", "toolCallId": "1", "arguments": {"skill": runner.NAME}}}
        self.assertEqual([], runner.invoked([start]))
        done = {"type": "tool.execution_complete", "data": {"toolCallId": "1", "success": True}}
        self.assertEqual([start], runner.invoked([start, done]))

    def test_migration_installs_only_candidate(self):
        self.root.mkdir()
        runner.install_skill(self.root)
        self.assertEqual([runner.NAME], [p.name for p in (self.root / ".github/skills").iterdir()])

    def test_trigger_installs_competing_catalog(self):
        self.root.mkdir()
        runner.install_skill(self.root, competing=True)
        expected = {p.name for p in runner.SKILL.parent.iterdir() if p.is_dir()}
        actual = {p.name for p in (self.root / ".github/skills").iterdir()}
        self.assertEqual(expected, actual)
        self.assertGreater(len(actual), 1)


if __name__ == "__main__":
    unittest.main()
