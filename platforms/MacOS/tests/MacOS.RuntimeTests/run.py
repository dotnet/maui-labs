"""Build and execute registered AppKit scenarios; no third-party Python dependencies."""

import argparse
from contextlib import contextmanager
from dataclasses import dataclass
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys


HOST = Path(__file__).resolve().parent
REPO = HOST.parents[3]
IDENTIFIER = re.compile(r"[a-z][a-z0-9-]*\Z")


def contained(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()) or path == root.resolve():
        raise ValueError(f"Path escapes its root: {relative}")
    return path


def positive(value):
    return type(value) is int and value > 0


def load_manifest(name):
    if not IDENTIFIER.fullmatch(name):
        raise ValueError(f"Invalid scenario id: {name}")
    path = HOST / "Scenarios" / name / "scenario.json"
    manifest = json.loads(path.read_text())
    if manifest["name"] != name or not positive(manifest["expectedCases"]):
        raise ValueError("Manifest needs the selected id and a positive expectedCases.")
    if "expectedAssertions" in manifest and not positive(manifest["expectedAssertions"]):
        raise ValueError("expectedAssertions must be positive when specified.")
    return manifest


def validate_result(manifest, result, exit_code, expectation=None):
    expected_code = expectation["exitCode"] if expectation else 0
    if exit_code != expected_code or result.get("exitCode") != expected_code:
        raise ValueError(f"Unexpected exit: process={exit_code}, result={result.get('exitCode')}, expected={expected_code}")
    if result.get("scenario") != manifest["name"] or not positive(result.get("assertions")):
        raise ValueError("Wrong scenario or no actual assertions executed.")
    if result.get("expectedCases") != manifest["expectedCases"]:
        raise ValueError("Host and manifest case expectations disagree.")
    if result.get("expectedAssertions") != manifest.get("expectedAssertions"):
        raise ValueError("Host and manifest assertion expectations disagree.")
    if type(result.get("cases")) is not int or result["cases"] < 0:
        raise ValueError("Invalid completed case count.")
    if expectation:
        if expected_code == 0 or not expectation.get("failureId") or not expectation.get("message"):
            raise ValueError("Baseline must declare a concrete nonzero regression.")
        for key in ("outcome", "failureId", "message"):
            if result.get(key) != expectation[key]:
                raise ValueError(f"Baseline {key} did not match: {result.get(key)!r}")
    else:
        if result.get("outcome") != "passed" or result.get("failureId") is not None:
            raise ValueError("Fixed run did not report success.")
        if result["cases"] != manifest["expectedCases"]:
            raise ValueError("Fixed run did not complete every declared case.")
        if "expectedAssertions" in manifest and result["assertions"] != manifest["expectedAssertions"]:
            raise ValueError("Fixed assertion count did not match.")


@dataclass(frozen=True)
class BuildOutput:
    executable: Path
    bundle: Path
    log: Path
    binlog: Path


def read_build_output(metadata, log, binlog, app_root=None):
    paths = metadata.read_text(encoding="utf-8-sig").splitlines()
    if len(paths) != 2 or any(not path or not Path(path).is_absolute() for path in paths):
        raise ValueError("Build metadata must contain exactly one absolute bundle and executable path.")
    bundle, executable = (Path(path).resolve(strict=True) for path in paths)
    if not bundle.is_dir() or bundle.suffix != ".app" or not executable.is_file():
        raise ValueError("Build metadata must identify an existing .app and native executable.")
    if executable.parent != bundle / "Contents" / "MacOS":
        raise ValueError("Native executable must be inside the reported app bundle.")
    if app_root is not None and not bundle.is_relative_to(Path(app_root).resolve()):
        raise ValueError("Reported bundle is outside the requested app_root.")
    return BuildOutput(executable, bundle, log, binlog)


class RuntimeRunner:
    """Shared stage primitives for scenario drivers (build, publish, launch, source overlays)."""

    def __init__(self, name, evidence, maui_version=""):
        self.manifest = load_manifest(name)
        self.name = name
        self.evidence = Path(evidence).resolve()
        self.evidence.mkdir(parents=True, exist_ok=True)
        self.project = HOST / "MacOS.RuntimeTests.csproj"
        self.maui_version = maui_version
        if maui_version and not re.fullmatch(r"\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?", maui_version):
            raise ValueError("Invalid MAUI version.")
        self.env = dict(os.environ)
        self.env.setdefault("NUGET_PACKAGES", str(REPO / ".packages"))
        self.completed = []

    def command(self, args, log, timeout=600, allow_failure=False):
        log.parent.mkdir(parents=True, exist_ok=True)
        print("+", " ".join(map(str, args)), flush=True)
        with log.open("w") as stream:
            process = subprocess.Popen(
                list(map(str, args)), cwd=REPO, env=self.env, stdout=stream,
                stderr=subprocess.STDOUT, start_new_session=os.name == "posix")
            try:
                code = process.wait(timeout)
            except subprocess.TimeoutExpired:
                if os.name == "posix":
                    os.killpg(process.pid, signal.SIGKILL)
                else:
                    process.kill()
                process.wait()
                raise RuntimeError(f"Timed out; terminated owned process {process.pid}. See {log}")
        if code and not allow_failure:
            print(log.read_text()[-12000:], file=sys.stderr)
            raise RuntimeError(f"Command exited {code}; see {log}")
        return code

    def stage_directory(self, stage):
        if not IDENTIFIER.fullmatch(stage):
            raise ValueError(f"Invalid stage id: {stage}")
        directory = self.evidence / stage
        directory.mkdir(parents=True, exist_ok=True)
        return directory

    def build(self, stage, properties=None, publish=False, configuration="Debug", extra_args=(), app_root=None):
        directory = self.stage_directory(stage)
        metadata = directory / "sdk-build-output.txt"
        if metadata.exists():
            raise RuntimeError(f"Refusing stale build metadata: {metadata}")
        properties = dict(properties or {})
        owned_properties = ("runtimetestbuildmetadata", "runtimetestbuildtarget")
        if any(str(key).casefold() in owned_properties for key in properties):
            raise ValueError("The runner owns RuntimeTestBuildMetadata and RuntimeTestBuildTarget.")
        extra_args = tuple(map(str, extra_args))
        if any(argument.lstrip(" '\"").startswith("@") or
               any(name in argument.casefold() for name in owned_properties)
               for argument in extra_args):
            raise ValueError("Extra arguments cannot override runner-owned properties or use response files.")
        properties["RuntimeTestBuildMetadata"] = str(metadata)
        properties["RuntimeTestBuildTarget"] = "Publish" if publish else "Build"
        properties.setdefault("RuntimeTestScenario", self.name)
        properties.setdefault("ContinuousIntegrationBuild", "true")
        properties.setdefault("ValidateXcodeVersion", "true")
        if properties["RuntimeTestScenario"] != self.name:
            raise ValueError("Build selector must match the registered scenario.")
        if self.maui_version:
            properties["MicrosoftMauiControlsVersion"] = self.maui_version
        arguments = []
        for key, value in properties.items():
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key) or "\n" in str(value):
                raise ValueError(f"Invalid MSBuild property: {key}")
            arguments.append(f"-p:{key}={value}")
        binlog = directory / "build.binlog"
        log = directory / "build.log"
        self.command([REPO / "eng/common/dotnet.sh", "publish" if publish else "build",
                      self.project, "-c", configuration, "-m:1", "-nr:false", f"-bl:{binlog}",
                      *arguments, *extra_args], log)
        output = read_build_output(metadata, log, binlog, app_root)
        executable = output.executable
        (directory / "build-output.json").write_text(json.dumps({
            "executable": str(output.executable), "bundle": str(output.bundle),
            "properties": properties, "configuration": configuration, "publish": publish
        }, indent=2))
        self.command([executable, "--list"], directory / "registry.json", timeout=30)
        registry = json.loads((directory / "registry.json").read_text())
        selected = [item for item in registry if item["Name"] == self.name]
        if len(selected) != 1 or selected[0]["ExpectedCases"] != self.manifest["expectedCases"]:
            raise RuntimeError("Built host does not register the declared scenario/case count.")
        if selected[0]["ExpectedAssertions"] != self.manifest.get("expectedAssertions"):
            raise RuntimeError("Built host assertion expectation differs from the manifest.")
        unknown_log = directory / "unknown-selector.log"
        unknown = self.command([executable, "--scenario", "__unregistered_scenario__",
                                "--evidence", directory / "unknown"], unknown_log, timeout=30, allow_failure=True)
        if unknown != 1 or "Unknown scenario: __unregistered_scenario__" not in unknown_log.read_text():
            raise RuntimeError("Host did not reject an unregistered selector.")
        return output

    def launch(self, output, stage, expectation=None, required_evidence=None):
        directory = self.stage_directory(stage)
        result_path = directory / "result.json"
        if result_path.exists():
            raise RuntimeError(f"Refusing stale terminal evidence: {result_path}")
        log = directory / "runtime.log"
        code = self.command([output.executable, "--scenario", self.name, "--evidence", directory],
                            log, timeout=self.manifest.get("timeoutSeconds", 120), allow_failure=True)
        print(log.read_text(), flush=True)
        if not result_path.is_file():
            raise RuntimeError("Native process did not produce terminal evidence.")
        result = json.loads(result_path.read_text())
        validate_result(self.manifest, result, code, expectation)
        required = list(self.manifest.get("evidence", []))
        required += expectation.get("evidence", []) if expectation else self.manifest.get("fixedEvidence", [])
        required += list(required_evidence or [])
        for name in required:
            path = contained(directory, name)
            if not path.is_file() or path.stat().st_size == 0:
                raise RuntimeError(f"Missing or empty runtime evidence: {path}")
        self.completed.append({"stage": stage, "baseline": expectation is not None, "result": result})
        return result

    @contextmanager
    def baseline_sources(self):
        baseline = self.manifest["baseline"]
        originals = {}
        try:
            for overlay in baseline.get("overlays", [baseline]):
                source = contained(REPO, overlay["source"])
                if source in originals:
                    raise ValueError("Baseline overlays must name distinct files.")
                originals[source] = source.read_bytes()
                if ("commit" in overlay) == ("replace" in overlay):
                    raise ValueError("Overlay requires exactly one pinned commit or exact replacement.")
                if "commit" in overlay:
                    commit = overlay["commit"]
                    if not re.fullmatch(r"[0-9a-f]{40}", commit):
                        raise ValueError("Baseline commit must be a full immutable SHA.")
                    self.command(["git", "fetch", "--depth=1", "origin", commit],
                                 self.evidence / f"fetch-{commit}.log")
                    content = subprocess.check_output(["git", "show", f"{commit}:{overlay['source']}"], cwd=REPO)
                else:
                    replacement = overlay["replace"]
                    text = originals[source].decode()
                    old, new = replacement["old"], replacement["new"]
                    if not old or old == new or text.count(old) != 1:
                        raise ValueError("Baseline replacement must match exactly once and change the source.")
                    content = text.replace(old, new).encode()
                source.write_bytes(content)
            yield
        finally:
            for source, content in originals.items():
                source.write_bytes(content)

    def run(self):
        driver = self.manifest.get("driver")
        if driver:
            path = contained(HOST / "Scenarios" / self.name, driver)
            spec = importlib.util.spec_from_file_location(f"runtime_{self.name}", path)
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            module.run(self)
        else:
            if "baseline" in self.manifest:
                with self.baseline_sources():
                    output = self.build("baseline")
                    self.launch(output, "baseline", self.manifest["baseline"])
            output = self.build("fixed")
            self.launch(output, "fixed")
        if not any(not item["baseline"] for item in self.completed):
            raise RuntimeError("No fixed scenario was executed and verified.")
        if "baseline" in self.manifest and not any(item["baseline"] for item in self.completed):
            raise RuntimeError("Declared baseline was not executed and verified.")
        (self.evidence / "summary.json").write_text(json.dumps(self.completed, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--maui-version", default="")
    parser.add_argument("--evidence", required=True)
    args = parser.parse_args()
    RuntimeRunner(args.scenario, args.evidence, args.maui_version).run()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL shared runtime runner: {error}", file=sys.stderr)
        sys.exit(1)
