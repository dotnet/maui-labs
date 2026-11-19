"""Resource stages composed from the shared runtime runner; no separate probe app."""

import os
from pathlib import Path
from run import REPO, package_properties


PACKAGE_IDS = ("Microsoft.Maui.Platforms.MacOS", "Microsoft.Maui.Platforms.MacOS.Essentials")


def assert_package_references(path, version):
    references = [line.strip().split("|") for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    for package_id in PACKAGE_IDS:
        if references.count([package_id, version]) != 1:
            raise ValueError(f"Expected exactly one {package_id} reference at {version}: {references}")


def run(runner):
    packages = os.environ.get("RUNTIME_TEST_PACKAGES", str(REPO / "artifacts/runtime-packages"))
    properties = package_properties(packages)

    with runner.baseline_sources():
        output = runner.build("baseline", properties={"CreatePackage": "false"}, extra_args=("-t:Rebuild",))
        runner.launch(output, "baseline", runner.manifest["baseline"])

    for stage, publish in (("fixed", False), ("incremental", False), ("publish", True)):
        package_evidence = runner.stage_directory(stage) / "package-references.txt"
        output = runner.build(
            stage,
            properties={**properties, "CreatePackage": "false", "ResourcePackageEvidence": str(package_evidence)},
            publish=publish,
            extra_args=("-t:Rebuild",) if stage == "fixed" else (),
        )
        assert_package_references(package_evidence, properties["ResourceTestPackageVersion"])
        runner.launch(output, stage)

    for stage, overrides in (
        ("metadata-macos", {}),
        ("metadata-windows", {"ResourceAssertionPlatform": "windows"}),
        ("metadata-opt-out", {"EnableMacOSMauiResourceMapping": "false"}),
    ):
        arguments = {**properties, "RuntimeTestScenario": runner.name, **overrides}
        runner.command(
            [REPO / "eng/common/dotnet.sh", "msbuild", runner.project,
             "-t:AssertBundleResourceItems", "-m:1", "-nr:false",
             *(f"-p:{key}={value}" for key, value in arguments.items())],
            runner.stage_directory(stage) / "assertions.log",
        )
