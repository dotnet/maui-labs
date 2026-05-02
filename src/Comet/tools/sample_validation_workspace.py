#!/usr/bin/env python3
"""Prepare and validate sample-runtime evidence without overclaiming verification."""

from __future__ import annotations

import argparse
import json
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
import shutil
import sys


SCHEMA_VERSION = 1
EVIDENCE_ROOT_NAME = "sample-validation"

SAMPLE_INVENTORY = [
    {
        "name": "CometMauiApp",
        "priority": "P0",
        "sample_type": "Comet",
        "surface_status": "Evolved",
        "comparison": "original CometMauiApp vs evolved CometMauiApp",
        "minimum_flows": [
            "Launch the app.",
            "Confirm the counter is visible.",
            "Increment the counter.",
            "Decrement the counter.",
            "Reset the counter.",
            "Exercise slider and toggle behavior.",
        ],
    },
    {
        "name": "CometBaristaNotes",
        "priority": "P0",
        "sample_type": "Comet",
        "surface_status": "Mixed",
        "comparison": "original CometBaristaNotes vs evolved CometBaristaNotes",
        "minimum_flows": [
            "Launch the app.",
            "Confirm tabs are visible.",
            "Confirm the dashboard renders.",
            "Navigate to bean detail.",
            "Exercise shot logging or input flow.",
            "Return via navigation.",
        ],
    },
    {
        "name": "Comet.Sample",
        "priority": "P1",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original Comet.Sample vs evolved Comet.Sample",
        "minimum_flows": [
            "Launch the app.",
            "Open the demo landing/navigation screen.",
            "Exercise representative control demos.",
            "Trigger state-changing interactions across major categories.",
        ],
    },
    {
        "name": "CometFeatureShowcase",
        "priority": "P1",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original CometFeatureShowcase vs evolved CometFeatureShowcase",
        "minimum_flows": [
            "Launch the app.",
            "Navigate between showcase features.",
            "Exercise each showcased interaction at least once.",
        ],
    },
    {
        "name": "CometProjectManager",
        "priority": "P1",
        "sample_type": "Comet",
        "surface_status": "Mixed",
        "comparison": "original CometProjectManager vs evolved CometProjectManager",
        "minimum_flows": [
            "Launch the app.",
            "Exercise shell/tab/navigation flow.",
            "Navigate between project and task detail screens.",
            "Exercise theme/toolkit interaction.",
            "Exercise edit/update flow.",
        ],
    },
    {
        "name": "MauiReference",
        "priority": "P1",
        "sample_type": "Pure MAUI reference",
        "surface_status": "N/A",
        "comparison": "original MauiReference vs evolved MauiReference only if touched",
        "minimum_flows": [
            "Launch the app.",
            "Exercise the key navigation/render paths used for reference comparison.",
        ],
    },
    {
        "name": "CometAllTheLists",
        "priority": "P2",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original CometAllTheLists vs evolved CometAllTheLists",
        "minimum_flows": [
            "Launch the app.",
            "Verify each list style is visible.",
            "Exercise selection, scroll, and update behavior.",
            "Confirm empty or populated states where present.",
        ],
    },
    {
        "name": "CometTaskApp",
        "priority": "P2",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original CometTaskApp vs evolved CometTaskApp",
        "minimum_flows": [
            "Launch the app.",
            "Exercise task navigation.",
            "Create, edit, and complete a task.",
            "Verify tab/state persistence behavior.",
        ],
    },
    {
        "name": "CometWeather",
        "priority": "P2",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original CometWeather vs evolved CometWeather",
        "minimum_flows": [
            "Launch the app.",
            "Confirm weather summary is visible.",
            "Exercise refresh or update flow.",
            "Exercise navigation/details if present.",
        ],
    },
    {
        "name": "CometStressTest",
        "priority": "P2",
        "sample_type": "Comet",
        "surface_status": "Legacy",
        "comparison": "original CometStressTest vs evolved CometStressTest",
        "minimum_flows": [
            "Launch the app.",
            "Select each representative category.",
            "Exercise representative stress scenarios without blanking or corruption.",
        ],
    },
]


STAGE_STATUSES = {
    "not_started",
    "build_verified",
    "launch_attempted",
    "baseline_captured",
    "runtime_blocked",
    "runtime_verified",
}

ISSUE_STATUSES = {
    "discovered",
    "fixed",
    "unresolved",
}

ISSUE_SEVERITIES = {
    "blocking",
    "serious",
    "minor",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    init_parser = subparsers.add_parser("init", help="Create the sample validation evidence scaffold.")
    init_parser.add_argument("--workspace-root", required=True, help="Session workspace root that will contain sample-validation/.")
    init_parser.add_argument("--repo-root", required=True, help="Repository root used for relative report references.")
    init_parser.add_argument("--force", action="store_true", help="Recreate the scaffold even if it already exists.")

    render_parser = subparsers.add_parser("render", help="Render the human-readable report from report JSON.")
    render_parser.add_argument("--workspace-root", required=True)

    validate_parser = subparsers.add_parser("validate", help="Validate the evidence report without overclaiming runtime verification.")
    validate_parser.add_argument("--workspace-root", required=True)

    return parser.parse_args()


def sample_template(sample: dict[str, object]) -> dict[str, object]:
    name = str(sample["name"])
    return {
        "name": name,
        "priority": sample["priority"],
        "sample_type": sample["sample_type"],
        "surface_status": sample["surface_status"],
        "comparison_target": sample["comparison"],
        "project_path": f"sample/{name}/{name}.csproj" if name != "MauiReference" else "sample/MauiReference/MauiReference.csproj",
        "minimum_flows": list(sample["minimum_flows"]),
        "overall_status": "not_started",
        "baseline": {
            "status": "not_started",
            "attempted_commands": [],
            "screenshots": [],
            "trees": [],
            "logs": [],
            "notes": [],
            "blockers": [],
        },
        "evolved": {
            "status": "not_started",
            "attempted_commands": [],
            "screenshots": [],
            "trees": [],
            "logs": [],
            "notes": [],
            "blockers": [],
        },
        "comparison": {
            "status": "not_started",
            "artifacts": [],
            "notes": [],
        },
        "checklist": [
            {
                "id": f"flow-{index:02d}",
                "title": flow,
                "status": "not_started",
                "evidence": [],
                "notes": [],
            }
            for index, flow in enumerate(sample["minimum_flows"], start=1)
        ],
        "issues": [],
        "paths": {
            "baseline_dir": f"baselines/{name}",
            "evolved_dir": f"evolved/{name}",
            "comparison_dir": f"comparisons/{name}",
            "checklist_file": f"checklists/{name}.md",
            "issues_file": f"issues/{name}.md",
        },
    }


def create_manifest(repo_root: Path) -> dict[str, object]:
    return {
        "schema_version": SCHEMA_VERSION,
        "generated_at_utc": utc_now(),
        "repo_root": str(repo_root),
        "rule": "Never claim runtime verification without retained evidence and rerun closure for discovered bugs.",
        "samples": [sample_template(sample) for sample in SAMPLE_INVENTORY],
    }


def ensure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def write_text(path: Path, text: str) -> None:
    ensure_directory(path.parent)
    path.write_text(text, encoding="utf-8")


def evidence_root(workspace_root: Path) -> Path:
    return workspace_root / EVIDENCE_ROOT_NAME


def report_json_path(workspace_root: Path) -> Path:
    return evidence_root(workspace_root) / "reports" / "sample-validation-report.json"


def report_markdown_path(workspace_root: Path) -> Path:
    return evidence_root(workspace_root) / "reports" / "sample-validation-report.md"


def load_manifest(workspace_root: Path) -> tuple[Path, dict[str, object]]:
    path = report_json_path(workspace_root)
    if not path.is_file():
        raise SystemExit(f"Sample validation report not found: {path}")

    return path, json.loads(path.read_text(encoding="utf-8"))


def save_manifest(workspace_root: Path, manifest: dict[str, object]) -> None:
    manifest = deepcopy(manifest)
    manifest["generated_at_utc"] = utc_now()
    path = report_json_path(workspace_root)
    write_text(path, json.dumps(manifest, indent=2) + "\n")


def init_workspace(workspace_root: Path, repo_root: Path, force: bool) -> int:
    root = evidence_root(workspace_root)
    if root.exists() and not force:
        raise SystemExit(f"Sample validation scaffold already exists: {root} (use --force to recreate it)")
    if root.exists() and force:
        shutil.rmtree(root)

    ensure_directory(root)
    for directory in ("baselines", "evolved", "comparisons", "checklists", "issues", "reports", "logs"):
        ensure_directory(root / directory)

    manifest = create_manifest(repo_root)

    for sample in manifest["samples"]:
        name = sample["name"]
        for stage_root in ("baselines", "evolved"):
            stage_dir = root / stage_root / name
            for child in ("screenshots", "trees", "logs", "notes"):
                ensure_directory(stage_dir / child)

        ensure_directory(root / "comparisons" / name)
        write_text(root / "issues" / f"{name}.md", build_issue_scaffold(sample))
        write_text(root / "checklists" / f"{name}.md", build_checklist_scaffold(sample))

    save_manifest(workspace_root, manifest)
    write_text(report_markdown_path(workspace_root), render_markdown(manifest))

    print(root)
    return 0


def build_issue_scaffold(sample: dict[str, object]) -> str:
    name = sample["name"]
    return "\n".join(
        [
            f"# {name} — validation issues",
            "",
            "Use this file to capture the bug-discovery loop for the sample.",
            "",
            "## Bug loop",
            "",
            "1. Discover a problem during runtime validation.",
            "2. Capture baseline screenshot/log/tree evidence.",
            "3. Classify the issue (blocking / serious / minor).",
            "4. Fix the issue.",
            "5. Rerun the affected flow.",
            "6. Recapture screenshots/logs after the fix.",
            "7. Record the disposition in the report JSON + markdown summary.",
            "",
            "## Entries",
            "",
            "- No issues recorded yet.",
            "",
        ]
    )


def build_checklist_scaffold(sample: dict[str, object]) -> str:
    lines = [
        f"# {sample['name']} — validation checklist",
        "",
        f"- Priority: {sample['priority']}",
        f"- Type: {sample['sample_type']}",
        f"- Surface status: {sample['surface_status']}",
        f"- Comparison target: {sample['comparison_target']}",
        "",
        "## Minimum flows",
        "",
    ]

    for flow in sample["minimum_flows"]:
        lines.append(f"- [ ] {flow}")

    lines.extend(
        [
            "",
            "## Evidence expectations",
            "",
            "- Baseline screenshot(s)",
            "- Baseline logs",
            "- Baseline notes/tree evidence as applicable",
            "- Evolved screenshot(s)",
            "- Evolved logs",
            "- Side-by-side comparison artifact(s)",
            "- Bug loop notes for discovered issues",
            "",
        ]
    )

    return "\n".join(lines)


def render_markdown(manifest: dict[str, object]) -> str:
    lines = [
        "# Sample validation report",
        "",
        f"- Generated: {manifest['generated_at_utc']}",
        f"- Repo root: {manifest['repo_root']}",
        f"- Rule: {manifest['rule']}",
        "",
        "## Samples",
        "",
    ]

    for sample in manifest["samples"]:
        lines.extend(render_sample_markdown(sample))

    return "\n".join(lines) + "\n"


def render_sample_markdown(sample: dict[str, object]) -> list[str]:
    baseline = sample["baseline"]
    evolved = sample["evolved"]
    comparison = sample["comparison"]
    issues = sample["issues"]

    lines = [
        f"### {sample['name']}",
        f"- Priority: {sample['priority']}",
        f"- Overall status: {sample['overall_status']}",
        f"- Baseline status: {baseline['status']}",
        f"- Evolved status: {evolved['status']}",
        f"- Comparison status: {comparison['status']}",
        f"- Minimum flows complete: {sum(1 for item in sample['checklist'] if item['status'] == 'done')}/{len(sample['checklist'])}",
        f"- Issues: {len(issues)}",
        "",
        "#### Minimum flows",
    ]

    for item in sample["checklist"]:
        lines.append(f"- [{checkbox(item['status'])}] {item['title']}")

    lines.extend(
        [
            "",
            "#### Evidence summary",
            f"- Baseline screenshots: {len(baseline['screenshots'])}",
            f"- Baseline logs: {len(baseline['logs'])}",
            f"- Evolved screenshots: {len(evolved['screenshots'])}",
            f"- Evolved logs: {len(evolved['logs'])}",
            f"- Comparison artifacts: {len(comparison['artifacts'])}",
        ]
    )

    if baseline["blockers"]:
        lines.extend(["", "#### Baseline blockers"])
        lines.extend([f"- {item}" for item in baseline["blockers"]])

    if evolved["blockers"]:
        lines.extend(["", "#### Evolved blockers"])
        lines.extend([f"- {item}" for item in evolved["blockers"]])

    if issues:
        lines.extend(["", "#### Issues"])
        for issue in issues:
            lines.append(f"- `{issue['id']}` [{issue['severity']}/{issue['status']}] {issue['title']}")

    lines.append("")
    return lines


def checkbox(status: str) -> str:
    return "x" if status == "done" else " "


def validate_manifest(manifest: dict[str, object], workspace_root: Path) -> list[str]:
    errors: list[str] = []

    if manifest.get("schema_version") != SCHEMA_VERSION:
        errors.append(f"Unsupported schema_version: {manifest.get('schema_version')}")

    for sample in manifest.get("samples", []):
        validate_sample(sample, workspace_root, errors)

    return errors


def validate_sample(sample: dict[str, object], workspace_root: Path, errors: list[str]) -> None:
    name = sample["name"]
    baseline = sample["baseline"]
    evolved = sample["evolved"]
    comparison = sample["comparison"]
    checklist = sample["checklist"]
    issues = sample["issues"]

    validate_stage(name, "baseline", baseline, workspace_root, errors)
    validate_stage(name, "evolved", evolved, workspace_root, errors)

    if comparison["status"] == "done" and not comparison["artifacts"]:
        errors.append(f"{name}: comparison marked done without artifacts.")

    for item in checklist:
        if item["status"] == "done" and not item["evidence"]:
            errors.append(f"{name}: checklist item '{item['title']}' is done without evidence.")

    for issue in issues:
        validate_issue(name, issue, workspace_root, errors)

    if evolved["status"] == "runtime_verified":
        if not has_baseline_evidence_or_blocker(baseline):
            errors.append(f"{name}: runtime_verified requires preserved baseline evidence or a precise blocker.")

        if not evolved["screenshots"]:
            errors.append(f"{name}: runtime_verified requires evolved screenshots.")

        if not evolved["logs"]:
            errors.append(f"{name}: runtime_verified requires evolved logs.")

        if not comparison["artifacts"]:
            errors.append(f"{name}: runtime_verified requires comparison artifacts.")

        incomplete_flows = [item["title"] for item in checklist if item["status"] != "done"]
        if incomplete_flows:
            errors.append(f"{name}: runtime_verified requires every checklist flow to be done. Remaining: {', '.join(incomplete_flows)}")

        unresolved = [issue["id"] for issue in issues if issue["status"] != "fixed"]
        if unresolved:
            errors.append(f"{name}: runtime_verified cannot keep unresolved issues open. Remaining: {', '.join(unresolved)}")


def validate_stage(sample_name: str, stage_name: str, stage: dict[str, object], workspace_root: Path, errors: list[str]) -> None:
    status = stage["status"]
    if status not in STAGE_STATUSES:
        errors.append(f"{sample_name}: {stage_name} has invalid status '{status}'.")
        return

    if status == "build_verified" and not stage["attempted_commands"]:
        errors.append(f"{sample_name}: {stage_name} build_verified requires attempted_commands.")

    if status == "launch_attempted" and not stage["attempted_commands"]:
        errors.append(f"{sample_name}: {stage_name} launch_attempted requires attempted_commands.")

    if status == "baseline_captured":
        if not stage["screenshots"]:
            errors.append(f"{sample_name}: {stage_name} baseline_captured requires screenshots.")
        if not stage["logs"]:
            errors.append(f"{sample_name}: {stage_name} baseline_captured requires logs.")

    if status == "runtime_blocked":
        if not stage["blockers"]:
            errors.append(f"{sample_name}: {stage_name} runtime_blocked requires blockers.")
        if not (stage["attempted_commands"] or stage["logs"]):
            errors.append(f"{sample_name}: {stage_name} runtime_blocked requires attempted_commands or logs.")

    if status == "runtime_verified":
        if not stage["screenshots"]:
            errors.append(f"{sample_name}: {stage_name} runtime_verified requires screenshots.")
        if not stage["logs"]:
            errors.append(f"{sample_name}: {stage_name} runtime_verified requires logs.")

    for field in ("screenshots", "trees", "logs"):
        for relative_path in stage.get(field, []):
            assert_artifact_exists(sample_name, relative_path, workspace_root, errors)


def validate_issue(sample_name: str, issue: dict[str, object], workspace_root: Path, errors: list[str]) -> None:
    if issue["status"] not in ISSUE_STATUSES:
        errors.append(f"{sample_name}: issue {issue.get('id', '<missing>')} has invalid status '{issue['status']}'.")

    if issue["severity"] not in ISSUE_SEVERITIES:
        errors.append(f"{sample_name}: issue {issue.get('id', '<missing>')} has invalid severity '{issue['severity']}'.")

    if not issue.get("title"):
        errors.append(f"{sample_name}: issue {issue.get('id', '<missing>')} is missing a title.")

    discovery_evidence = issue.get("discovery_evidence", [])
    rerun_evidence = issue.get("rerun_evidence", [])
    if issue["status"] in {"fixed", "unresolved"} and not discovery_evidence:
        errors.append(f"{sample_name}: issue {issue['id']} requires discovery evidence.")

    if issue["status"] == "fixed" and not rerun_evidence:
        errors.append(f"{sample_name}: fixed issue {issue['id']} requires rerun evidence.")

    for relative_path in discovery_evidence + rerun_evidence:
        assert_artifact_exists(sample_name, relative_path, workspace_root, errors)


def has_baseline_evidence_or_blocker(stage: dict[str, object]) -> bool:
    return bool(stage["screenshots"] and stage["logs"]) or bool(stage["blockers"] and (stage["attempted_commands"] or stage["logs"]))


def assert_artifact_exists(sample_name: str, relative_path: str, workspace_root: Path, errors: list[str]) -> None:
    artifact_path = evidence_root(workspace_root) / relative_path
    if not artifact_path.exists():
        errors.append(f"{sample_name}: referenced artifact does not exist: {artifact_path}")


def render_workspace(workspace_root: Path) -> int:
    _, manifest = load_manifest(workspace_root)
    write_text(report_markdown_path(workspace_root), render_markdown(manifest))
    print(report_markdown_path(workspace_root))
    return 0


def validate_workspace(workspace_root: Path) -> int:
    _, manifest = load_manifest(workspace_root)
    errors = validate_manifest(manifest, workspace_root)
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1

    print("sample validation report is structurally valid")
    return 0


def main() -> int:
    args = parse_args()
    workspace_root = Path(args.workspace_root).expanduser().resolve()

    if args.command == "init":
        repo_root = Path(args.repo_root).expanduser().resolve()
        return init_workspace(workspace_root, repo_root, args.force)

    if args.command == "render":
        return render_workspace(workspace_root)

    if args.command == "validate":
        return validate_workspace(workspace_root)

    raise SystemExit(f"Unsupported command: {args.command}")


if __name__ == "__main__":
    raise SystemExit(main())
