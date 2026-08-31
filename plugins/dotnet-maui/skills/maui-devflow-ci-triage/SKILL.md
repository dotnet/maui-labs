---
name: maui-devflow-ci-triage
description: >-
  Diagnose a red DevFlow CI run from its published artifacts and hand off a
  bounded local reproduction. USE FOR: reading a failed flow-run.json or
  .mauitrace from a CI artifact; separating an app defect from a selector
  defect from infrastructure flake; deciding whether evidence is sufficient;
  producing a deterministic triage report; preparing a repro command for a
  human. DO NOT USE FOR: authoring CI workflows or permissions (use
  maui-devflow-ci); running or repairing flows locally (use
  maui-devflow-run-cli or maui-devflow-test); promoting a recording (use
  maui-devflow-record); inspecting one already-downloaded artifact in isolation
  through the read-only trust projection (use maui-devflow-artifact); applying
  a selector repair; or treating CI evidence as authority to change source.
---

# DevFlow CI Triage

## Purpose

Turn a red CI run into a defensible statement about what failed, how strong the
evidence is, and what a human should run next — without pretending the CI
result is a verified reproduction.

## When to Use

- A DevFlow CI run went red and its artifacts are available.
- A failure needs separating into app defect, selector defect, or
  infrastructure flake.
- A human needs a bounded local reproduction command and a triage report.

## When Not to Use

| Situation | Use instead |
| --- | --- |
| Authoring workflows or permissions | `maui-devflow-ci` |
| Running or repairing flows locally | `maui-devflow-run-cli`, `maui-devflow-test` |
| Promoting a recording | `maui-devflow-record` |
| Inspecting one downloaded artifact in isolation | `maui-devflow-artifact` |
| Applying a selector repair | `maui-devflow-test` |

## The Trust Rule

Imported evidence is **untrusted** until something local agrees with it. The
artifact trust states are `untrusted`, `attested`, and `locally-reproduced`.

- CI produced it → `untrusted`.
- It carries a valid attestation → `attested`. Still not a reproduction.
- A fresh local run agreed → `locally-reproduced`.

CI is never execution authority. An attested artifact is not permission to
change a selector, and a red CI run is not proof that the app is broken.

## Inputs

Ask for these together, once:

- The artifact path — a `flow-run.json` report, an `execution-manifest.json`,
  or a redacted `.mauitrace` bundle.
- Which run and attempt it came from, since a re-run may have overwritten the
  first failure.
- Whether the same flow passes locally, and on which target.

If only a log tail is offered, say that a console tail is not sufficient
evidence and name the artifact you need.

## Workflow

### 1. Read the artifact, bounded and read-only

```
maui_artifact_inspect  file=<path>  kind=flow-run|mauitrace
```

or from the terminal:

```bash
maui devflow evidence inspect-trust artifacts/flow-run.json --kind flow-run
```

`--kind` is inferred from a `.json` or `.mauitrace` extension, so it is only
needed when the extension is something else. `evidence view` is a different
command: it renders an HTML report from a `.mauitrace` **bundle** and has no
`--kind`. Do not point it at a `flow-run.json`.

Report the identity, artifact kind, import time, integrity, verification,
projection, and local-reproduction state exactly as found. Do not fill a
missing field with a plausible value.

### 2. Produce a deterministic triage report

```bash
maui devflow flow triage --manifest artifacts/execution-manifest.json \
  --report artifacts/flow-run.json --format markdown -o artifacts/triage.md
```

`triage` drives nothing and never overwrites an existing output file.

### 3. Classify the failure

| Signal | Likely class |
| --- | --- |
| Step failed with element-not-found, control exists in the tree under a different identity | Selector defect — route to repair, do not apply it |
| Assert failed with a real value that contradicts the acceptance criterion | App defect |
| Failure before the first step; agent never bound; build or deploy error | Infrastructure |
| Same step passes on re-run with no change | Flake — report the observed ratio, not "flaky" |
| Plan digest does not match the flow that ran | Stale artifact — the result is not about the current flow |

Say which class, and say what would falsify it. When the evidence supports two
classes, report both rather than picking the convenient one.

### 4. Judge evidence sufficiency out loud

Score it with the `maui-devflow-test` skill's `references/replay-quality.md`
rubric: selector tier, assertion strength, determinism, evidence completeness,
flake signal. Those five dimensions stand on their own if that skill is not
installed. A green history does not upgrade a result that no independent
business oracle observed — that stays **not independently verified**.

### 5. Hand off a local reproduction

```bash
maui devflow flow reproduce maui-tests/promo-reduces-total.md \
  --import artifacts/flow-run.json --kind flow-run \
  --project src/Shop/Shop.csproj --platform android --output artifacts/repro-1
```

`reproduce` stops after trust evaluation. Give the human the exact command, the
target it needs, and what result would confirm or refute the classification.

## Tool Reality

- **No MCP tool files an issue, comments on a PR, or opens a pull request.**
  Use `gh` from a trusted context, or hand the finding to a human. Do not claim
  the bug has been reported.
- **No automatic repair.** `maui_test_patch` proposals are inert and require a
  human approval; a triage conclusion never authorizes a selector change.
- **No `maui devflow flow record`** and **no `maui_test_record`** — if triage
  concludes the flow needs re-recording, that is a human Inspector task; see
  maui-devflow-record.
- **A CI screenshot is unredacted.** Treat it as sensitive and do not paste its
  contents into an issue without checking.

## Validation

- Every claim cites a field in the artifact, or is labelled an inference.
- Trust state is reported verbatim, never upgraded by argument.
- The failure class names its falsifier.
- Missing evidence is listed explicitly instead of being worked around.
- No source change, selector change, or GitHub write was performed.

## Completion Check

State the failure class, the trust state, the evidence gaps, and the single
next command a human should run. If the evidence cannot support a conclusion,
say so and stop — an unsupported diagnosis is worse than none.
