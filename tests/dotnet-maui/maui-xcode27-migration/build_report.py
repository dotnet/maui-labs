#!/usr/bin/env python3
"""Adapt Copilot evidence to the official Anthropic benchmark/review viewer."""

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

from run_evals import NAME, REPO, SKILL, events, write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True, help="One completed paired iteration")
    parser.add_argument("--output", type=Path, required=True, help="New session-artifact directory")
    parser.add_argument("--skill-creator", type=Path, required=True, help="Official aggregate_benchmark.py, generate_review.py and viewer.html directory")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() or output == REPO or REPO in output.parents:
        parser.error("Output must be a new directory outside the checkout.")
    output.mkdir(parents=True)
    models = set()
    raw = {}
    for case in sorted(args.input.iterdir()):
        metadata_path = case / "eval_metadata.json"
        if not metadata_path.exists():
            continue
        metadata = json.loads(metadata_path.read_text())
        for config in ("with_skill", "without_skill"):
            source = case / config
            if not (source / "grading.json").exists():
                continue
            target = output / f"eval-{metadata['eval_id']}-{case.name}" / config / "run-1"
            target.mkdir(parents=True)
            write_json(target.parent.parent / "eval_metadata.json", metadata)
            for filename in ("grading.json", "timing.json", "execution.json"):
                shutil.copy2(source / filename, target / filename)
            rendered = target / "outputs"
            rendered.mkdir()
            shutil.copy2(source / "response.md", rendered / "response.md")
            if (source / "qualitative-grading.json").exists():
                shutil.copy2(source / "qualitative-grading.json", rendered / "qualitative-review.json")
            bundle = []
            for path in sorted((source / "outputs").rglob("*")):
                relative = path.relative_to(source / "outputs")
                if not path.is_file() or any(part in (".git", "bin", "obj") for part in relative.parts):
                    continue
                if relative.parts[:2] in ((".github", "skills"), (".claude", "skills")):
                    continue
                if path.suffix in (".cs", ".csproj", ".props", ".targets", ".plist", ".json", ".md", ".yml", ".yaml"):
                    bundle.append(f"===== {path.relative_to(source / 'outputs')} =====\n{path.read_text()}\n")
            (rendered / "project-files.txt").write_text("\n".join(bundle))
            (target / "evidence-location.txt").write_text(str(source.resolve()) + "\n")
            execution = json.loads((source / "execution.json").read_text())
            models.update(execution["models"])
            trace = events(source / "transcript.jsonl")
            raw[(metadata["eval_id"], config)] = {
                "tool_calls": sum(e.get("type") == "tool.execution_start" for e in trace),
                "errors": sum(e.get("type") == "tool.execution_complete" and e.get("data", {}).get("success") is False for e in trace),
            }
    subprocess.run([sys.executable, str(args.skill_creator / "aggregate_benchmark.py"), str(output),
                    "--skill-name", NAME, "--skill-path", str(SKILL)], check=True)
    path = output / "benchmark.json"
    benchmark = json.loads(path.read_text())
    benchmark["metadata"].update(executor_model=", ".join(sorted(models)), analyzer_model="programmatic structural grader; qualitative review separate", runs_per_configuration=1)
    for run in benchmark["runs"]:
        # Upstream defaults unreported telemetry to zero; retain unknown instead.
        run["result"]["tokens"] = None
        run["result"].update(raw[(run["eval_id"], run["configuration"])])
    for config in ("with_skill", "without_skill"):
        if config in benchmark["run_summary"]:
            benchmark["run_summary"][config].pop("tokens", None)
    benchmark["run_summary"].get("delta", {}).pop("tokens", None)
    benchmark["notes"] = [
        "One execution per scenario/configuration. This does not estimate run-to-run variance or prove reliability.",
        "Only structural and explicit no-edit assertions are automatically graded; callback semantics and runtime correctness require the separate qualitative/build evidence.",
        "Token usage was not reported by the executor's result event. It is unknown, not zero.",
        "Baseline receives the same task and fixture but no migration skill. Network/source research remains available in both configurations.",
        "Skill invocation is checked against successful tool execution, not response wording.",
    ]
    provenance = args.input / "origins.json"
    if provenance.exists():
        shutil.copy2(provenance, output / "input-provenance.json")
        benchmark["notes"].append(json.loads(provenance.read_text())["note"])
    write_json(path, benchmark)
    lines = [f"# {NAME} benchmark", "", f"Executor: {benchmark['metadata']['executor_model']}; one run per configuration.", "",
             "| Case | Configuration | Assertions passed | Seconds |", "|---|---|---:|---:|"]
    for run in benchmark["runs"]:
        result = run["result"]
        lines.append(f"| {run['eval_id']} | {run['configuration']} | {result['passed']}/{result['total']} | {result['time_seconds']:.1f} |")
    lines.extend(["", *benchmark["notes"]])
    (output / "benchmark.md").write_text("\n".join(lines) + "\n")
    subprocess.run([sys.executable, str(args.skill_creator / "generate_review.py"), str(output),
                    "--skill-name", NAME, "--benchmark", str(path), "--static", str(output / "review.html")], check=True)


if __name__ == "__main__":
    main()
