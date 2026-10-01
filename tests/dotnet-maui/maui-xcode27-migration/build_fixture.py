#!/usr/bin/env python3
"""Compile an agent-edited fixture with an explicitly selected, existing Apple SDK."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import time

from run_evals import REPO, snapshot, write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--dotnet-root", type=Path, required=True)
    parser.add_argument("--sdk-version", required=True)
    parser.add_argument("--xcode", type=Path, required=True, help="Xcode .app, never global xcode-select")
    parser.add_argument("--framework", default="net10.0-ios")
    parser.add_argument("--rid", default="iossimulator-arm64")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--supplemental-preview", action="store_true")
    args = parser.parse_args()
    project = args.project.resolve()
    output = args.output.resolve()
    if output.exists() or output == REPO or REPO in output.parents:
        parser.error("Use a new output directory in session artifacts.")
    if not project.is_file():
        parser.error("Project does not exist.")
    dotnet = args.dotnet_root.resolve() / "dotnet"
    sdk = args.dotnet_root.resolve() / "sdk" / args.sdk_version
    if not dotnet.is_file() or not (sdk / "MSBuild.dll").is_file():
        parser.error("Selected SDK must already be installed; this script never installs tools.")
    output.mkdir(parents=True)
    before = snapshot(project.parent)
    env = os.environ.copy()
    env.update(DOTNET_ROOT=str(args.dotnet_root.resolve()), DOTNET_HOST_PATH=str(dotnet),
               DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR=str(sdk / "Sdks"),
               DEVELOPER_DIR=str(args.xcode.resolve() / "Contents/Developer"))
    # Explicit MSBuild selects this test SDK without rewriting the app's global.json.
    command = [str(dotnet), str(sdk / "MSBuild.dll"), str(project), "-restore", "-t:Rebuild",
               f"-p:TargetFramework={args.framework}", f"-p:RuntimeIdentifier={args.rid}", "-v:minimal"]
    if args.supplemental_preview:
        command += ["-p:NoWarn=XCODE_27_1_PREVIEW"]
    write_json(output / "command.json", {"command": command, "environment": {
        name: env[name] for name in ("DOTNET_ROOT", "DOTNET_HOST_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DEVELOPER_DIR")},
        "supplemental_preview_only": args.supplemental_preview,
        "sdk_selection_override": "Explicit MSBuild; project global.json remains unchanged, so this is not validation of its normal dotnet CLI SDK selection."})
    started = time.monotonic()
    with (output / "build.log").open("w") as log:
        result = subprocess.run(command, cwd=project.parent, env=env, stdout=log, stderr=subprocess.STDOUT)
    after = snapshot(project.parent)
    changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    report = {"exit_code": result.returncode, "seconds": time.monotonic() - started,
              "source_changed_paths": changed, "supplemental_preview_only": args.supplemental_preview}
    write_json(output / "result.json", report)
    print(json.dumps(report))
    raise SystemExit(result.returncode or bool(changed))


if __name__ == "__main__":
    main()
