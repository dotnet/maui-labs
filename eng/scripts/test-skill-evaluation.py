"""Exercise workflow Bash with synthetic reports and a stub gh; no LLM or API calls."""

import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = yaml.safe_load(
    (ROOT / ".github" / "workflows" / "skill-evaluation.yml").read_text(encoding="utf-8")
)
STEPS = WORKFLOW["jobs"]["evaluate"]["steps"]
BASH = shutil.which("bash")
POST = next(step for step in STEPS if step["name"] == "Post results to PR")
ENTRIES = json.dumps([{"plugin": "tooling", "skills_path": "plugins/tooling/skills"}])


def render_script(script):
    # Only trusted workflow expressions are replaced; no PR inputs are executed.
    return re.sub(r"\$\{\{.*?\}\}", "test", script)


class EvaluationReportingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if BASH is None:
            raise RuntimeError("Bash is required (use Git Bash on Windows).")

    def run_bash(self, script, directory, env=None):
        return subprocess.run(
            [BASH, "--noprofile", "--norc", "-e", "-o", "pipefail"],
            input=script,
            text=True,
            encoding="utf-8",
            capture_output=True,
            cwd=directory,
            env=os.environ | {"BASH_ENV": ""} | (env or {}),
            timeout=30,
        )

    def run_report(self, discovery, outcome, entries=ENTRIES, reports=None, gh_exit=0):
        with tempfile.TemporaryDirectory(prefix="eval-report-") as directory:
            root = Path(directory)
            for plugin, files in (reports or {}).items():
                target = root / "artifacts" / "eval-results" / plugin / "timestamp"
                target.mkdir(parents=True)
                (target / "results.json").write_text("{}", encoding="utf-8")
                for name, text in files.items():
                    (target / name).write_text(text, encoding="utf-8")

            result = self.run_bash(
                'gh() { printf "%s\\n" "$@" > gh-args.txt; '
                'cp comment-body.md posted-body.md; return "$GH_EXIT"; }\n'
                + render_script(POST["run"]),
                root,
                {
                    "DISCOVERY_OUTCOME": discovery,
                    "EVALUATION_OUTCOME": outcome,
                    "ENTRIES": entries,
                    "GH_EXIT": str(gh_exit),
                },
            )
            self.assertEqual(result.stderr, "")
            body = (root / "comment-body.md").read_text(encoding="utf-8")
            self.assertEqual(body, (root / "posted-body.md").read_text(encoding="utf-8"))
            self.assertEqual(
                (root / "gh-args.txt").read_text(encoding="utf-8").splitlines(),
                ["api", "repos/test/issues/test/comments", "-X", "POST", "-F",
                 "body=@comment-body.md"],
            )
            return result.returncode, body

    def test_empty_discovery(self):
        code, body = self.run_report("success", "skipped", "[]")
        self.assertEqual(code, 0)
        self.assertIn("No skills with eval.yaml", body)
        self.assertNotIn("failed", body)

    def test_failed_discovery(self):
        for outcome in ("failure", "cancelled", "skipped", ""):
            with self.subTest(outcome=outcome):
                code, body = self.run_report(outcome, "skipped", "")
                self.assertEqual(code, 1)
                self.assertIn("failed or did not complete", body)
                self.assertNotIn("No skills with eval.yaml", body)

    def test_failed_discovery_with_empty_entries_is_not_empty_success(self):
        code, body = self.run_report("failure", "skipped", "[]")
        self.assertEqual(code, 1)
        self.assertNotIn("No skills with eval.yaml", body)

    def test_model_startup_failure(self):
        code, body = self.run_report("success", "failure")
        self.assertEqual(code, 1)
        self.assertIn("failed or did not complete", body)
        self.assertIn("View run logs", body)
        self.assertNotIn("No skills with eval.yaml", body)

    def test_incomplete_evaluation_without_results(self):
        for outcome in ("cancelled", "skipped", ""):
            with self.subTest(outcome=outcome):
                code, body = self.run_report("success", outcome)
                self.assertEqual(code, 1)
                self.assertNotIn("No skills with eval.yaml", body)

    def test_missing_output_fails_closed(self):
        code, body = self.run_report("success", "success")
        self.assertEqual(code, 1)
        self.assertIn("View run logs", body)

    def test_current_report_filename(self):
        code, body = self.run_report(
            "success", "success", reports={"tooling": {"summary.md": "CURRENT REPORT"}}
        )
        self.assertEqual(code, 0)
        self.assertIn("CURRENT REPORT", body)
        self.assertNotIn("partial", body)

    def test_legacy_report_filename(self):
        code, body = self.run_report(
            "success", "success", reports={"tooling": {"results.md": "LEGACY REPORT"}}
        )
        self.assertEqual(code, 0)
        self.assertIn("LEGACY REPORT", body)

    def test_current_report_takes_precedence(self):
        code, body = self.run_report(
            "success", "success",
            reports={"tooling": {"summary.md": "CURRENT REPORT", "results.md": "OLD REPORT"}},
        )
        self.assertEqual(code, 0)
        self.assertIn("CURRENT REPORT", body)
        self.assertNotIn("OLD REPORT", body)

    def test_partial_results_warn(self):
        code, body = self.run_report(
            "success", "failure", reports={"tooling": {"summary.md": "PARTIAL REPORT"}}
        )
        self.assertEqual(code, 0)  # The failed evaluation step already fails the job.
        self.assertIn("may be partial", body)
        self.assertIn("PARTIAL REPORT", body)

    def test_multiple_plugin_results(self):
        code, body = self.run_report(
            "success", "success",
            entries=json.dumps([{"plugin": "tooling"}, {"plugin": "maui"}]),
            reports={
                "tooling": {"summary.md": "TOOLING REPORT"},
                "maui": {"summary.md": "MAUI REPORT"},
            },
        )
        self.assertEqual(code, 0)
        self.assertIn("TOOLING REPORT", body)
        self.assertIn("MAUI REPORT", body)

    def test_reports_without_markdown(self):
        code, body = self.run_report("success", "success", reports={"tooling": {}})
        self.assertEqual(code, 0)
        self.assertIn("uploaded results.json", body)

    def test_comment_api_failure_is_not_swallowed(self):
        code, _ = self.run_report("success", "skipped", "[]", gh_exit=7)
        self.assertEqual(code, 7)

    def test_explicit_models_and_outcome_binding(self):
        step = next(step for step in STEPS if step["name"] == "Run evaluation")
        self.assertEqual(step["id"], "run-evaluation")
        self.assertIn("--model gpt-5.6-terra", step["run"])
        self.assertIn("--judge-model gpt-5.6-sol", step["run"])
        self.assertNotIn("continue-on-error", step)
        self.assertEqual(POST["if"], "always()")
        self.assertEqual(POST["env"]["DISCOVERY_OUTCOME"], "${{ steps.discover.outcome }}")
        self.assertEqual(POST["env"]["EVALUATION_OUTCOME"], "${{ steps.run-evaluation.outcome }}")
        self.assertEqual(POST["env"]["ENTRIES"], "${{ steps.discover.outputs.entries }}")

    def test_failed_job_posts_failed_commit_status(self):
        step = next(step for step in STEPS if step["name"] == "Update commit status")
        self.assertEqual(step["if"], "always()")
        for status, expected in (("success", "success"), ("failure", "failure"),
                                 ("cancelled", "failure")):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                script = step["run"].replace("${{ job.status }}", status)
                result = self.run_bash(
                    'gh() { printf "%s\\n" "$@"; }\n' + render_script(script), directory
                )
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn(f"state={expected}", result.stdout.splitlines())

    def test_comment_workflow_keeps_permission_gate_and_pr_data_checkout(self):
        # BaseLoader keeps YAML's "on" key a string rather than a YAML 1.1 boolean.
        triggers = yaml.load(
            (ROOT / ".github" / "workflows" / "skill-evaluation.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )["on"]
        self.assertEqual(triggers["issue_comment"]["types"], ["created"])
        self.assertNotIn("workflow_dispatch", triggers)
        gate = WORKFLOW["jobs"]["gate"]
        self.assertIn("github.event_name == 'issue_comment'", gate["if"])
        permission = next(step for step in gate["steps"] if step["id"] == "check")
        for role in ("admin", "maintain", "write"):
            self.assertIn(f'"$PERMISSION" == "{role}"', permission["run"])
        self.assertIn('echo "should_run=false"', permission["run"])
        evaluate = WORKFLOW["jobs"]["evaluate"]
        self.assertEqual(evaluate["needs"], "gate")
        self.assertEqual(evaluate["if"], "needs.gate.outputs.should_run == 'true'")
        checkout = next(step for step in STEPS if step["name"] == "Checkout PR")
        self.assertEqual(checkout["with"]["ref"], "${{ needs.gate.outputs.pr_head_sha }}")
        self.assertIs(checkout["with"]["persist-credentials"], False)

    def test_all_workflow_scripts_parse(self):
        for job in WORKFLOW["jobs"].values():
            for step in job["steps"]:
                if "run" not in step:
                    continue
                with self.subTest(step=step["name"]):
                    result = subprocess.run(
                        [BASH, "--noprofile", "--norc", "-n"],
                        input=render_script(step["run"]),
                        text=True, encoding="utf-8", capture_output=True, timeout=30,
                        env=os.environ | {"BASH_ENV": ""},
                    )
                    self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
