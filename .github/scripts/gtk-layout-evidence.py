"""Run the two pinned, separately authorized GTK layout evidence probes on hosted Linux."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


CANDIDATE = "ba043896fd4d2ae123020dc43f241031b9a47699"
BASELINE = "4e9a5e7cecd3ad976b757551d4d323b4917dd08b"
DONOR = "3df3a488cc31dd7100020659ac08c613f826afe0"
ROOT = "platforms/Linux.Gtk4"
SOURCE = f"{ROOT}/src/Linux.Gtk4"
TESTS = f"{ROOT}/tests/Linux.Gtk4.Tests"
PAINT = f"{TESTS}/ContentViewClippingTests.cs"
WINDOW_TEST = f"{TESTS}/WindowSizingTests.cs"
WINDOW_FILES = {
    f"{SOURCE}/Platform/GtkMauiApplication.cs": (
        "9337d0fb9a1112e2d0676dd4fa4b935c961f851d",
        "10228698609a662470ecfa665594a0a3d6bf0b4a",
    ),
    f"{SOURCE}/Handlers/WindowHandler.cs": (
        "738c974a5ef9ccfc634d2a68bcba6300445c62b6",
        "bd7cb294a2499779543cdeb98a74b967e18c86e4",
    ),
}
PROTECTED = [
    f"{SOURCE}/Platform/GtkRootLayoutDriver.cs",
    f"{SOURCE}/Platform/GtkLayoutPanel.cs",
    *[f"{SOURCE}/Handlers/{name}.cs" for name in (
        "ContentViewHandler", "LayoutHandler", "GtkViewHandler", "ScrollViewHandler", "BorderHandler"
    )],
    *[f"{TESTS}/{name}.cs" for name in (
        "ContentViewClippingTests", "ContentViewRootLayoutTests", "ContentViewTransformTests",
        "GtkRootLayoutDriverTests", "GtkRootLayoutConstraintsTests"
    )],
]
NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def git(*args, data=None):
    return subprocess.check_output(["git", *args], input=data)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def file_hashes(paths):
    return {path: sha256(Path(path).read_bytes()) for path in paths}


def paths_from_git(*args):
    return [value.decode() for value in git(*args).split(b"\0") if value]


def verify_changes(expected):
    changed = set(paths_from_git("diff", "--name-only", "-z", "HEAD"))
    changed.update(paths_from_git("ls-files", "--others", "--exclude-standard", "-z"))
    require(changed == expected, f"Unexpected source overlay: {sorted(changed)}")


def parse_result(path, class_name, baseline):
    tree = ET.parse(path)
    counters = tree.find("t:ResultSummary/t:Counters", NAMESPACE)
    require(counters is not None, "Missing executed-test counters")
    expected = {
        "total": 1, "executed": 1, "passed": 0 if baseline else 1,
        "failed": 1 if baseline else 0, "notExecuted": 0,
        "error": 0, "timeout": 0, "aborted": 0,
    }
    require(all(int(counters.get(key, "-1")) == value for key, value in expected.items()),
            f"Unexpected test counters: {counters.attrib}")
    results = tree.findall("t:Results/t:UnitTestResult", NAMESPACE)
    require(len(results) == 1 and class_name in results[0].get("testName", ""),
            "Missing or wrong native test result")
    require(results[0].get("outcome") == ("Failed" if baseline else "Passed"), "Wrong test outcome")
    output = results[0].findtext("t:Output/t:StdOut", "", NAMESPACE)
    if baseline:
        require("initial-clipped: background=FFFFFFFF, inside=0000FFFF, header=0000FFFF, clipped=True" in output,
                "Baseline did not demonstrate white control and red forbidden-header pixels")
        error = results[0].findtext("t:Output/t:ErrorInfo/t:Message", "", NAMESPACE)
        require("Native paint did not respect clipping during initial-clipped; header=0000FFFF." in error,
                "Failure was not the expected clipping assertion")
    if class_name == "WindowSizingTests":
        require(output.count(": expected ") == 49, "Missing frozen window-sizing allocation stages")
    if class_name == "ContentViewClippingTests":
        phases = ["initial-clipped"] if baseline else [
            "initial-clipped", "unclipped", "clipped-again", "explicit-clip",
            "bounds-clip-after-clearing-geometry", "all-clipping-cleared",
        ]
        for phase in phases:
            require((path.parent / "paint" / f"{phase}.png").is_file(), f"Missing {phase} paint evidence")
    return {"counters": counters.attrib, "output": output}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["clipping-baseline", "window-compatibility"])
    mode = parser.parse_args().mode
    require(os.environ.get("GITHUB_ACTIONS") == "true" and os.name == "posix", "Hosted Linux only")
    require(os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "Automatic or manual retries are not authorized")
    baseline = mode == "clipping-baseline"
    tested_sha = BASELINE if baseline else CANDIDATE
    require(git("rev-parse", "HEAD").decode().strip() == tested_sha, "Wrong tested checkout")
    require(not git("status", "--porcelain").strip(), "Tested checkout must start clean")
    production_paths = paths_from_git("ls-files", "-z", f"{ROOT}/src")
    production_before = file_hashes(production_paths)
    protected_before = {} if baseline else file_hashes(PROTECTED)
    subprocess.run(["git", "fetch", "--no-tags", "--depth=1", "origin", CANDIDATE if baseline else DONOR],
                   check=True, timeout=120)

    if baseline:
        fixture = git("show", f"{CANDIDATE}:{PAINT}")
        require(sha256(fixture) == "f0b2441317ac8c1cbc88228766bed4c840aede3baf28ff865a45f4203916bc54",
                "Corrected clipping fixture hash mismatch")
        Path(PAINT).write_bytes(fixture)
        allowed = {PAINT}
        overlay = {"fixture_sha256": sha256(fixture), "fixture_source": CANDIDATE}
    else:
        for path, (before, after) in WINDOW_FILES.items():
            require(git("rev-parse", f"HEAD:{path}").decode().strip() == before, f"Wrong original blob: {path}")
            require(git("rev-parse", f"{DONOR}:{path}").decode().strip() == after, f"Wrong donor blob: {path}")
        patch = git("diff", "--binary", "--full-index", CANDIDATE, DONOR, "--",
                    f"{SOURCE}/Platform/GtkMauiApplication.cs", f"{SOURCE}/Handlers/WindowHandler.cs")
        require(len(patch) == 3537 and sha256(patch) ==
                "0199df6d3f7508765760ec5742179928ef5f78dad192f3d27a355aa581e54b6e", "Window patch mismatch")
        fixture = git("show", f"{DONOR}:{WINDOW_TEST}")
        require(len(fixture) == 8274 and sha256(fixture) ==
                "78d44a0dd4023172a2fe16b4a896b338060074b2422d6d351202533e8d0af693", "Window fixture mismatch")
        require(not Path(WINDOW_TEST).exists(), "Refusing to replace a candidate window fixture")
        git("apply", "--check", data=patch)
        git("apply", data=patch)
        for path, (_, after) in WINDOW_FILES.items():
            require(git("hash-object", path).decode().strip() == after, f"Applied blob mismatch: {path}")
        Path(WINDOW_TEST).write_bytes(fixture)
        allowed = {*WINDOW_FILES, WINDOW_TEST}
        overlay = {"patch_sha256": sha256(patch), "fixture_sha256": sha256(fixture), "donor": DONOR}

    verify_changes(allowed)
    unaffected = {path: digest for path, digest in production_before.items() if path not in allowed}
    require(file_hashes(unaffected) == unaffected, "Unaffected production source changed")
    require(file_hashes(protected_before) == protected_before, "Protected candidate source changed")
    source_after = file_hashes(production_paths)
    evidence = Path("artifacts/TestResults") / mode
    evidence.mkdir(parents=True)
    manifest = {
        "mode": mode, "workflow_run_sha": os.environ["GITHUB_SHA"], "run_id": os.environ["GITHUB_RUN_ID"],
        "tested_checkout_sha": tested_sha, "tested_checkout_tree": git("rev-parse", "HEAD^{tree}").decode().strip(),
        "candidate_sha": CANDIDATE, "runner_sha256": sha256(Path(__file__).read_bytes()),
        "overlay": overlay, "overlay_file_sha256": file_hashes(sorted(allowed)),
        "production_before": production_before, "production_after": source_after,
        "protected_candidate_sha256": protected_before, "results": {},
    }

    def save_manifest():
        (evidence / "provenance.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")

    save_manifest()
    project = f"{TESTS}/Linux.Gtk4.Tests.csproj"
    subprocess.run(["dotnet", "build", project, "--configuration", "Release", "--nologo"],
                   check=True, timeout=600)
    assemblies = sorted(Path("artifacts/bin").glob("**/Release/net10.0/Microsoft.Maui.Platforms.Linux.Gtk4*.dll"))
    require(any(path.name.endswith(".Tests.dll") for path in assemblies), "Missing test assembly")
    require(any(path.name == "Microsoft.Maui.Platforms.Linux.Gtk4.dll" for path in assemblies),
            "Missing platform assembly")
    manifest["assembly_sha256"] = file_hashes([str(path) for path in assemblies])
    save_manifest()
    classes = ["ContentViewClippingTests"] if baseline else [
        "WindowSizingTests", "ContentViewClippingTests", "ContentViewRootLayoutTests",
        "ContentViewTransformTests", "GtkRootLayoutDriverTests",
    ]
    for class_name in classes:
        results = evidence / class_name
        results.mkdir()
        env = {
            **os.environ, "RUN_GTK_RUNTIME_TESTS": "1", "GDK_BACKEND": "x11",
            "GSK_RENDERER": "cairo", "GTK_TEST_ARTIFACTS": str((results / "paint").resolve()),
        }
        result = subprocess.run([
            "dbus-run-session", "--", "xvfb-run", "--auto-servernum", "dotnet", "test", project,
            "--configuration", "Release", "--no-build", "--no-restore",
            "--filter", f"FullyQualifiedName~{class_name}",
            "--logger", f"trx;LogFileName={class_name}.trx", "--logger", "console;verbosity=detailed",
            "--results-directory", str(results),
        ], env=env, timeout=300)
        require(result.returncode == (1 if baseline else 0), f"Unexpected test exit: {result.returncode}")
        manifest["results"][class_name] = parse_result(results / f"{class_name}.trx", class_name, baseline)
        save_manifest()
    verify_changes(allowed)
    require(file_hashes(production_paths) == source_after, "Build/test changed production source")
    require(file_hashes(protected_before) == protected_before, "Build/test changed protected candidate files")
    require(file_hashes(sorted(allowed)) == manifest["overlay_file_sha256"], "Build/test changed overlay files")
    require(file_hashes(manifest["assembly_sha256"]) == manifest["assembly_sha256"], "Test assemblies changed")
    manifest["source_verified_after_tests"] = True
    save_manifest()
    print(f"Verified {mode}: {json.dumps({name: value['counters'] for name, value in manifest['results'].items()})}")


if __name__ == "__main__":
    main()
