---
name: maui-devflow-ci
description: >-
  Wire DevFlow flow execution into GitHub Actions safely. USE FOR: authoring or
  reviewing a workflow that runs `maui devflow flow` on CI; least-privilege
  `permissions:` for jobs that touch PR-author-controlled code; label-gated
  device jobs; artifact naming and retention for flow-run and .mauitrace
  evidence; splitting untrusted execution from a trusted `workflow_run`
  publisher. DO NOT USE FOR: diagnosing an already-red CI run (use
  maui-devflow-ci-triage); running flows locally (use `maui devflow flow`);
  authoring or repairing flows (use maui-devflow-test); promoting a recording
  (use maui-devflow-record); or general repository CI unrelated to DevFlow.
---

# DevFlow in CI

## Purpose

DevFlow CI jobs build and execute code from the pull request under test. That
makes them untrusted by construction. This skill covers how to run them anyway
without handing a PR author a write token.

## When to Use

- A workflow that runs `maui devflow flow` is being authored or reviewed.
- Job `permissions:`, label gating, or a `workflow_run` publisher split needs
  deciding.
- Flow-run and `.mauitrace` evidence needs an artifact naming and retention
  policy.

## When Not to Use

| Situation | Use instead |
| --- | --- |
| Diagnosing an already-red run | `maui-devflow-ci-triage` |
| Running flows locally | `maui devflow flow` commands |
| Authoring or repairing flows | `maui-devflow-test` |
| Promoting a recording | `maui-devflow-record` |
| Repository CI unrelated to DevFlow | none of these skills |

## The Core Rule

**A job that executes PR-author-controlled code holds no write scope.** In this
repository `.github/workflows/devflow-integration.yml` declares
`permissions: {}` at workflow level precisely because it builds and runs
`samples/**` and `eng/devflow/**` from the PR. Every job inherits nothing and
adds only what it needs to read.

Anything that must write — an issue, a comment, a check — goes in a **separate
trusted workflow** triggered by `workflow_run`, which checks its own code out
from the default branch. `devflow-failure-publisher.yml` is the worked example:
`permissions: { actions: read, contents: read, issues: write }`, gated to
same-repo default-branch runs, and it refuses to publish when the upstream run
came from a pull request.

Never "fix" a permissions error by widening the untrusted job.

## Inputs

- Which flows are in scope, and whether each has a committed plan sidecar.
- Which platforms are required, and which are opt-in.
- Whether the job may install packages or touch a device, and what cleans up.
- Where evidence goes, and who is allowed to read it.

## Workflow

### 1. Gate device jobs behind a label

Device and simulator legs are slow and flaky-prone. Gate them on an explicit
label rather than running them on every push — this repository uses
`integration-tests`, `flow-pilot`, `windows-flow-qa`, `apple-flow-qa`, and
`appkit-flow-qa` for exactly that. Say in the PR template which label runs what.

### 2. Validate before you drive

Put a static, no-device job first. `maui devflow flow validate` needs no app
and no runner with hardware, so a malformed flow fails in seconds instead of
after a twelve-minute build.

### 3. Execute with an immutable output directory

```yaml
- name: Run flow
  run: |
    maui devflow flow run maui-tests/promo-reduces-total.md \
      --project src/Shop/Shop.csproj --platform android -f net10.0-android \
      --output "${{ runner.temp }}/flow-run-1" \
      --cleanup uninstall --evidence-on-failure
```

Pass `--platform` explicitly. `flow run` defaults to `android` when it is
omitted, and the TFM does not select the platform for you.

Do not add `--evidence-screenshot` to a shared CI job without an explicit
decision: screenshot pixels are never redacted and the artifact may be
downloadable by anyone who can read the run.

### 4. Name artifacts so they can be correlated

Include both the run ID and the attempt, as the integration workflow does:

```yaml
- uses: actions/upload-artifact@v7
  if: always()
  with:
    name: devflow-flow-android-${{ github.run_id }}-${{ github.run_attempt }}
    path: ${{ runner.temp }}/flow-run-1
```

`if: always()` matters — a failing run is exactly the one whose evidence you
need. A re-run without the attempt number silently overwrites the first
failure, which is the evidence a triage agent wants most.

### 5. Qualify the corpus

```bash
maui devflow flow qualify --platform android \
  --corpus tests/DevFlow/InspectorCorpus \
  --artifact-manifest "${ARTIFACT_ROOT}/manifest.json" \
  -o qualification.json --fail-on-non-pass
```

Pass `--corpus` and `--platform`: the corpus default is a maui-labs-relative
path and the platform default is `maccatalyst`, so an unqualified invocation in
a consumer repository silently qualifies an empty corpus on the wrong platform.
`qualify` never replays and never applies a change, so it is safe in a
low-privilege job. Use it to gate merges on evidence quality rather than on a
single green run.

### 6. Publish findings from a trusted workflow only

```yaml
on:
  workflow_run:
    workflows: ["DevFlow Integration"]
    types: [completed]
permissions:
  actions: read
  contents: read
  issues: write
```

Then verify inside the job: same repository, default branch, and
`pull_requests[0] == null` before writing anything.

## Tool Reality

- **No MCP tool writes to GitHub.** Nothing in the DevFlow tool surface opens
  an issue, posts a comment, or creates a pull request. Use the `gh` CLI in a
  trusted workflow, or hand the finding to a human. Do not describe a tool that
  files the bug for you.
- **No `maui build` / `maui run` / `maui deploy`.** Build with
  `dotnet build -f <tfm>`, or let `maui devflow flow run` do it.
- **`maui android install` is SDK setup**, not APK installation. In CI it
  provisions the Android SDK; use `adb install` for the app.
- **Physical iOS is not covered** by a simulator leg. Never let a simulator
  result stand in for a signed-device claim.

## Validation

- No job that builds PR code holds a write permission.
- Every publishing workflow checks out its own code from the default branch.
- Artifact names carry `run_id` and `run_attempt`, and upload with
  `if: always()`.
- Screenshot evidence is opt-in and justified in the workflow file.
- The workflow fails closed: a missing plan sidecar or a stale `flow.digest`
  fails the job rather than skipping the check.

## Completion Check

State which jobs run untrusted code, what each may write, which labels gate
which platforms, and where evidence lands. Name anything you could not verify
from the workflow files themselves.
