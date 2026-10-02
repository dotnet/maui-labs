"""Exercise profile import boundaries with real MSBuild evaluation, without a device."""

from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


COMET = Path(__file__).resolve().parents[1]
SOURCE_IMPORT = COMET / "eng/ComposeAndroid.targets"
PACKAGE_IMPORT = COMET / "src/Comet/PackAssets/Comet.Android.targets"
SUPPORT = COMET / "src/vendor/Microsoft.AndroidX.Compose/buildTransitive"
PROPERTIES = (
    "MicrosoftAndroidXComposeEnableBaselineProfile,"
    "_CometComposeBuildSupportImported,_MicrosoftAndroidXComposeBaselineProfile"
)


class ProfileBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="comet-profile-build-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.dotnet = os.environ.get("DOTNET", "dotnet")
        self.project = self.root / "Consumer.proj"

    def write_project(self, imports=(), **properties):
        project = ET.Element("Project")
        group = ET.SubElement(project, "PropertyGroup")
        defaults = {
            "TargetFramework": "net11.0-android",
            "AndroidApplication": "true",
            "AndroidLinkTool": "r8",
            "IntermediateOutputPath": str(self.root / "obj") + os.sep,
        }
        defaults.update(properties)
        for name, value in defaults.items():
            ET.SubElement(group, name).text = value
        for path in imports:
            ET.SubElement(project, "Import", Project=str(path))
        ET.ElementTree(project).write(self.project, encoding="unicode")

    def evaluate(self, target=None, expect_success=True):
        command = [
            self.dotnet, "msbuild", str(self.project), "-nologo",
            f"-getProperty:{PROPERTIES}",
            "-getItem:AndroidArtProfile,ProguardConfiguration",
        ]
        if target:
            command.append("-target:" + target)
        result = subprocess.run(
            command, cwd=COMET, text=True, capture_output=True, timeout=60,
            check=False,
        )
        if not expect_success:
            self.assertNotEqual(result.returncode, 0, result.stdout)
            return result.stdout + result.stderr
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return json.loads(result.stdout[result.stdout.index("{"):])

    def assert_one_profile(self, result):
        profiles = result["Items"]["AndroidArtProfile"]
        self.assertEqual(len(profiles), 1)
        self.assertTrue(Path(profiles[0]["FullPath"]).is_file())

    def package_entry_point(self):
        destination = self.root / "package/buildTransitive"
        destination.mkdir(parents=True)
        package = os.environ.get("COMET_PROFILE_PACKAGE")
        if package:
            with zipfile.ZipFile(package) as archive:
                for relative in (
                    "Comet.targets",
                    "compose/Microsoft.AndroidX.Compose.targets",
                    "compose/Microsoft.AndroidX.Compose.pro",
                    "compose/Microsoft.AndroidX.Compose.baseline-prof.txt",
                ):
                    path = destination / relative
                    path.parent.mkdir(exist_ok=True)
                    path.write_bytes(archive.read("buildTransitive/" + relative))
        else:
            shutil.copy2(PACKAGE_IMPORT, destination / "Comet.targets")
            shutil.copytree(SUPPORT, destination / "compose")
        return destination / "Comet.targets"

    def test_source_android_app_imports_once(self):
        self.write_project([SOURCE_IMPORT])
        self.assert_one_profile(self.evaluate())

    def test_non_comet_android_app_has_no_profile(self):
        self.write_project()
        self.assertEqual(self.evaluate()["Items"]["AndroidArtProfile"], [])

    def test_source_non_android_is_unchanged(self):
        for framework in ("net11.0-ios", "net11.0"):
            with self.subTest(framework=framework):
                self.write_project([SOURCE_IMPORT], TargetFramework=framework)
                self.assertEqual(self.evaluate()["Items"]["AndroidArtProfile"], [])

    def test_android_library_does_not_add_profile_or_rules(self):
        self.write_project([SOURCE_IMPORT], AndroidApplication="false")
        result = self.evaluate("_AddMicrosoftAndroidXComposeProguardConfiguration")
        self.assertEqual(result["Items"]["AndroidArtProfile"], [])
        self.assertEqual(result["Items"]["ProguardConfiguration"], [])

    def test_opt_out_keeps_jni_correctness_rules(self):
        self.write_project(
            [SOURCE_IMPORT], MicrosoftAndroidXComposeEnableBaselineProfile="false",
        )
        result = self.evaluate("_AddMicrosoftAndroidXComposeProguardConfiguration")
        self.assertEqual(result["Items"]["AndroidArtProfile"], [])
        rules = result["Items"]["ProguardConfiguration"]
        self.assertEqual(len(rules), 1)
        self.assertEqual(Path(rules[0]["FullPath"]), SUPPORT / "Microsoft.AndroidX.Compose.pro")

    def test_packaged_android_entry_point(self):
        self.write_project([self.package_entry_point()])
        self.assert_one_profile(self.evaluate())

    def test_packaged_non_android_is_unchanged(self):
        self.write_project([self.package_entry_point()], TargetFramework="net11.0")
        self.assertEqual(self.evaluate()["Items"]["AndroidArtProfile"], [])

    def test_source_and_package_import_do_not_duplicate_profiles(self):
        entry = self.package_entry_point()
        for imports in ([SOURCE_IMPORT, entry], [entry, SOURCE_IMPORT]):
            with self.subTest(imports=imports):
                self.write_project(imports)
                result = self.evaluate("_AddMicrosoftAndroidXComposeProguardConfiguration")
                self.assert_one_profile(result)
                self.assertEqual(len(result["Items"]["ProguardConfiguration"]), 1)

    def test_missing_correctness_rules_fail_even_with_profile_disabled(self):
        self.write_project(
            [SOURCE_IMPORT], MicrosoftAndroidXComposeEnableBaselineProfile="false",
            _MicrosoftAndroidXComposeProguardConfiguration=str(self.root / "missing.pro"),
        )
        output = self.evaluate("_AddMicrosoftAndroidXComposeProguardConfiguration", False)
        self.assertIn("was not found", output)

    def test_missing_profile_fails_explicitly(self):
        self.write_project(
            [SOURCE_IMPORT],
            _MicrosoftAndroidXComposeBaselineProfile=str(self.root / "missing-profile.txt"),
        )
        output = self.evaluate("_ValidateMicrosoftAndroidXComposeBaselineProfile", False)
        self.assertIn("Android ART profile", output)
        self.assertIn("was not found", output)

    def test_missing_profgen_fails_explicitly(self):
        self.write_project(
            [SOURCE_IMPORT],
            MicrosoftAndroidXComposeProfgenClasspath=str(self.root / "missing-profgen.jar"),
        )
        output = self.evaluate("_ValidateMicrosoftAndroidXComposeBaselineProfile", False)
        self.assertIn("profgen was not found", output)

    def test_non_r8_build_does_not_require_profgen(self):
        self.write_project(
            [SOURCE_IMPORT], AndroidLinkTool="d8",
            MicrosoftAndroidXComposeProfgenClasspath=str(self.root / "missing-profgen.jar"),
        )
        self.evaluate("_ValidateMicrosoftAndroidXComposeBaselineProfile")


if __name__ == "__main__":
    unittest.main()
