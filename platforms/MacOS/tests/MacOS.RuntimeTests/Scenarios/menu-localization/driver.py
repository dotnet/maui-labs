"""Run each application language in a fresh native process."""

import os
from run import REPO, package_properties


def launch_language(runner, output, stage, language, expected=None, overrides=False, baseline=None):
    runner.env["MENU_TEST_EXPECTED_LANGUAGE"] = expected or language
    runner.env["MENU_TEST_OVERRIDES"] = "true" if overrides else "false"
    runner.launch(output, stage, baseline, extra_args=("-AppleLanguages", f"({language}, en)"))


def run(runner):
    with runner.baseline_sources():
        output = runner.build("baseline", extra_args=("-t:Rebuild",))
        launch_language(runner, output, "baseline", "de", baseline=runner.manifest["baseline"])

    output = runner.build("fixed", extra_args=("-t:Rebuild",))
    for language in ("de", "fr", "nl", "en", "zz"):
        launch_language(runner, output, f"fixed-{language}", language, "en" if language == "zz" else language)

    properties = package_properties(os.environ.get("RUNTIME_TEST_PACKAGES", str(REPO / "artifacts/runtime-packages")))
    output = runner.build("package", properties=properties, extra_args=("-t:Rebuild",))
    for language in ("de", "fr", "nl", "en", "zz"):
        launch_language(runner, output, f"package-{language}", language, "en" if language == "zz" else language)

    output = runner.build("english-only", properties={**properties, "MenuTestEnglishOnly": "true"},
                          extra_args=("-t:Rebuild",))
    launch_language(runner, output, "english-only-de", "de", "en")

    output = runner.build("overrides", properties={**properties, "MenuTestOverrides": "true",
                                                  "MenuTestOverrideLanguage": "de"},
                          extra_args=("-t:Rebuild",))
    launch_language(runner, output, "overrides-de", "de", overrides=True)

    output = runner.build("after-overrides", properties=properties, extra_args=("-t:Rebuild",))
    launch_language(runner, output, "after-overrides-de", "de")
