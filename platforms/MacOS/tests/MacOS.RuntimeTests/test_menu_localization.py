from contextlib import nullcontext
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

from run import HOST, load_manifest


spec = importlib.util.spec_from_file_location("menu_localization_driver", HOST / "Scenarios/menu-localization/driver.py")
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


class MenuDriverContractTests(unittest.TestCase):
    def test_native_launch_matrix_checks_each_requested_language_and_host_overrides(self):
        class Runner:
            env = {}
            manifest = load_manifest("menu-localization")

            def __init__(self):
                self.builds = []
                self.launches = []

            def baseline_sources(self):
                return nullcontext()

            def build(self, stage, **kwargs):
                self.builds.append((stage, kwargs))
                return stage

            def launch(self, output, stage, expectation=None, **kwargs):
                self.launches.append((output, stage, expectation, dict(self.env), kwargs))

        runner = Runner()
        packages = {"RuntimeTestsUseProjectReferences": "false"}
        with patch.object(driver, "package_properties", return_value=packages):
            driver.run(runner)

        self.assertEqual(len(runner.launches), 14)
        self.assertEqual(runner.launches[0][2], runner.manifest["baseline"])
        for output, stage, baseline, env, args in runner.launches:
            language = stage.rsplit("-", 1)[-1] if stage != "baseline" else "de"
            self.assertEqual(args["extra_args"], ("-AppleLanguages", f"({language}, en)"))
            expected = "en" if language == "zz" or stage == "english-only-de" else language
            self.assertEqual(env["MENU_TEST_EXPECTED_LANGUAGE"], expected)
            self.assertEqual(env["MENU_TEST_OVERRIDES"], "true" if stage == "overrides-de" else "false")
        self.assertEqual(runner.builds[2], ("package", {"properties": packages, "extra_args": ("-t:Rebuild",)}))
        self.assertEqual(runner.builds[3][1]["properties"]["MenuTestEnglishOnly"], "true")
        self.assertEqual(runner.builds[4][1]["properties"]["MenuTestOverrideLanguage"], "de")
        self.assertEqual(runner.builds[-1], ("after-overrides", {"properties": packages, "extra_args": ("-t:Rebuild",)}))
        self.assertTrue(all(build[1]["extra_args"] == ("-t:Rebuild",) for build in runner.builds))

    def test_each_shipped_language_has_the_complete_key_set_and_valid_app_name_templates(self):
        root = HOST.parents[1] / "src/MacOS/Resources/MauiMenuBar.bundle/Contents/Resources"
        english = self.read_strings(root / "en.lproj/MenuBar.strings")
        self.assertEqual(len(english), 17)
        for language in ("en", "de", "fr", "nl"):
            strings = self.read_strings(root / f"{language}.lproj/MenuBar.strings")
            self.assertEqual(set(strings), set(english))
            for key in ("AboutApp", "HideApp", "QuitApp"):
                self.assertEqual(strings[key].count("{0}"), 1)

    @staticmethod
    def read_strings(path):
        import re
        pairs = re.findall(r'^"([^"]+)" = "([^"]+)";$', Path(path).read_text(), re.MULTILINE)
        if len(pairs) != len(dict(pairs)):
            raise ValueError("Duplicate string key.")
        return dict(pairs)


if __name__ == "__main__":
    unittest.main()
