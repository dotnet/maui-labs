---
name: maui-devflow-record
description: >-
  Prepare a human-driven DevFlow Inspector recording and promote the raw
  capture into a reviewable, named flow. USE FOR: turning a Workbench recording
  into a committed flow plus plan sidecar; deciding what to record before the
  human starts; upgrading recorder output (null labels, text selectors, bare
  exists asserts) into tier-1 selectors, stated intents, acceptance criteria,
  and a reset contract; scoring replay quality of a fresh recording. DO NOT USE
  FOR: driving the recording itself (no agent tool records — a human records in
  the Inspector); conversational authoring with no recording involved (use
  maui-devflow-test); operator CLI execution (use maui-devflow-run-cli); CI
  wiring (use maui-devflow-ci); reading a red CI run (use maui-devflow-ci-triage);
  screen-video capture, which is `maui_recording_start` and unrelated to tests.
---

# MAUI DevFlow Recording Promotion

## Purpose

A recorder captures what happened. A test states what must remain true. This
skill covers the gap: what to tell a human before they record, and how to turn
the capture they produce into a flow a reviewer can approve.

## When to Use

- A human has recorded, or is about to record, a journey in the Inspector.
- Recorder output needs upgrading: `null` labels, text selectors, bare
  `exists` asserts, no reset contract.
- Someone asks how good a fresh recording is before committing it.

## When Not to Use

| Situation | Use instead |
| --- | --- |
| Authoring a test from a description, with no recording | `maui-devflow-test` |
| Running a committed flow from a shell | `maui-devflow-run-cli` |
| Wiring flow execution into GitHub Actions | `maui-devflow-ci` |
| Reading a red CI run | `maui-devflow-ci-triage` |
| Screen-video capture (`maui_recording_start`) | `maui-devflow-debug` |

## Tool Reality — read before promising anything

The agent **cannot record**. Confirmed against this repository:

- There is **no `maui_test_record` MCP tool.** No tool in
  `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/` starts, steps, or stops a
  recording.
- There is **no `maui devflow flow record` verb.** `maui devflow flow` has
  exactly `validate`, `replay`, `run`, `reproduce`, `triage`, and `qualify`.
- Recording lives in the **Inspector Test Workbench**, opened with
  `maui devflow inspect [--agent <id>]`. Its stages are goal → record →
  review → run → results, backed by the Inspector's own
  `/api/flows/record/*` routes. A human drives them.
- `maui_recording_start` / `maui_recording_stop` / `maui_recording_status`
  capture **screen video**. They do not produce a flow. Never offer them as a
  way to record a test.

Say this plainly rather than implying an agent-side recorder exists. The
correct sentence is: "I cannot record; I will prepare the recording plan, then
promote what you capture."

## Inputs

Ask only for what changes the next safe step, and combine the questions:

- The app project and the exact connected target (agent ID and instance).
- The user goal in one sentence — this becomes the Workbench Goal stage.
- Whether the journey mutates data, and what resets it.
- Whether anything outside the UI can confirm the business result.

## Workflow

### 1. Before the human records

Give a numbered recording script in the user's own words, one intention per
step, and name for each step:

- the control to touch, by `AutomationId` when known;
- the observable postcondition to leave visible so the recorder captures it;
- any screen that must **not** be visited, so the capture stays scoped.

Also flag missing AutomationIds up front. A control with no stable identity
will record as a text or index selector, and promoting that is a defect the
user should fix first — route the fix through the `maui-devflow-test` skill's
`references/testability.md`.

### 2. After the human records

Read the captured flow file. Expect recorder-shaped defects and fix them
deliberately, one at a time, explaining each change:

| Recorder output | Promote to |
| --- | --- |
| `"label": null` | The business action, e.g. `Apply promo code` |
| `"intent": null` | Why the step exists, in one sentence |
| `"recordedAt": null` | The real capture timestamp |
| `"acceptanceCriterionIds": null` | The criterion IDs this step proves |
| Text, index, or coordinate selector | `automationId`, or a testability fix |
| No assert, or `exists` on an already-visible control | `propEquals` on a value the feature actually changes |
| `pageChanged` only | A verifiable kind — `propEquals`, `exists`, `notExists`, `routeIs` |
| `"reset": { "strategy": "" }` | A real strategy and `resetIdentity`, or an explicit stop |
| Zeroed `explorationBudget` | Real `maxActions`, `maxDurationSeconds`, named `allowedScopes` |

Never delete a recorded step to make the flow pass. If a step cannot be
promoted, say which one and why.

### 3. Write the plan sidecar

Every promoted flow needs `<name>.maui-plan.json` beside it. Its validator
requires all of `schema` (**1**, not the fence's `2`), `planId`, `revision`,
`flow`, `goal`, `scenarios`, `preconditions`, `reset`, `acceptanceCriteria`,
`sideEffectPolicy`, and `provenance` — a missing key fails `flow run` with
`plan-invalid`. With no oracle, declare `independentBusinessOracles` empty and
carry the phrase **not independently verified** into every later result. See
the Artifact Format section of the `maui-devflow-test` skill.

### 4. Validate, then hand off

```bash
maui devflow flow validate maui-tests/promo-reduces-total.md
```

Validation is local and drives nothing. **It checks the `.md` fence only** —
it never opens the sidecar, so a `Valid` result says nothing about the plan.
Check the plan by hand against the required keys above; the first machine check
of the sidecar happens inside `flow run` or `flow reproduce`.

Committing the promoted flow and running it are **separate human approvals** —
chat agreement is not authorization. Prepare the request; do not consume it.

## Validation

- `maui devflow flow validate` reports `Valid` with zero errors.
- The sidecar carries all eleven required keys, `flow.path` names the flow
  file, and `flow.digest` is current.
- No step retains a null `label`, null `intent`, or null
  `acceptanceCriterionIds`.
- Every selector is tier 1, or the exception is named and justified in prose.
- At least one step carries a `verify: true` assert on a value the feature
  changes.
- Score the result with the `maui-devflow-test` skill's
  `references/replay-quality.md` rubric and report the weakest dimension first.
  If that skill is not installed, score these five dimensions directly:
  selector tier, assertion strength, determinism, evidence completeness, and
  flake signal.

## Completion Check

State which state was reached: recording script prepared, capture promoted to
an inert draft, awaiting commit approval, or blocked on a missing AutomationId,
reset contract, or oracle. Name every unresolved gap as a limitation.
