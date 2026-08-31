# Diagnose, Edit, and Verify

The imported run is a clue. The newly executed local run is the evidence used
to decide whether the working tree should change.

## Read the local result

Use the reproduction output, not the issue prose:

```powershell
maui devflow flow triage `
  --manifest <reproduction>\execution-manifest.json `
  --report <reproduction>\flow-run.json `
  --format markdown `
  --output <reproduction>\triage.md
```

Read `local-reproduction.json` as a comparison report. Do not treat
`matched: true` as source authority, and do not claim `locally-reproduced` when
the report did not.

Use `failureCorrespondence` rather than comparing the imported and local
failure fingerprints; those opaque fingerprints are intentionally occurrence
bound and are not equality keys.

- `same-failure`: code, class, step, and expected/observed checkpoints agree.
- `different-failure`: at least one of those facts differs.
- `no-local-failure`: the new local run did not produce failure facts.
- `indeterminate`: a required failure-comparison fact was unavailable.

Only `same-failure` can continue toward classification, and only when the
report has no separate flow, source, platform, runtime, evidence, completion,
or cleanup blocker.

The current local run must be terminal, complete, bound to its manifest, and
free of cleanup failures that would leave the next run on an unknown target.
Truncated evidence or unknown completion is a stop.

## Classify

### Test drift

Use `test-drift` only when the current app behavior still satisfies the
original acceptance criterion and the committed flow carries a stale selector,
precondition, route, or other test-owned fact.

For selector drift:

- the failure is pre-dispatch `locator-not-found`;
- the expected route, window, modal, seed, locale, theme and display profile
  match;
- current live inspection yields one durable app-owned AutomationId, or one
  scoped `AutomationId + collectionScope + stableItemKey`;
- the proposed selector identifies the same intended control;
- assertions, expected values, action order and business-oracle requirements
  remain unchanged.

If the missing selector is the action that should create the business outcome
and the required oracle failed, the evidence does not distinguish test drift
from a broken app. Classify as inconclusive unless separate current evidence
does.

### App regression

Use `app-regression` when the current test still expresses the intended
behavior but the app violates it. Examples:

- a hard assertion observes a wrong value;
- the independent business oracle fails after the UI reported success;
- the expected control or route was removed unintentionally;
- a handler, binding, navigation or domain operation fails in the app.

Keep the flow unchanged. Use normal code navigation and
`maui-devflow-debug` to fix the app, then rerun the original flow.

### Infrastructure

Build, install, launch, broker, device, agent binding, timeout before dispatch,
unsupported capability, or incomplete cleanup failures are infrastructure.
Repair the environment or hand it off; do not edit product or test behavior.

### Inconclusive

Use `inconclusive` when required facts are missing, multiple explanations fit,
the local failure differs from CI, or the current run cannot establish the
business outcome. Say what evidence would decide it and stop.

## Make the smallest worktree edit

The developer's request to fix the reproduced failure permits an ordinary
uncommitted workspace edit. It does not turn the imported artifact into
authority and does not grant a broker patch operation.

Before editing:

```powershell
git status --short
git diff -- <candidate-files>
```

If an existing user change overlaps the required lines, stop and ask how to
proceed.

For test drift:

1. edit only the proven stale fact in the `json maui-test` fence;
2. preserve prose unless it has become inaccurate;
3. preserve every action, assertion, expected value, `verify` flag, acceptance
   criterion, reset and oracle;
4. run:

   ```powershell
   maui devflow flow commit <flow.md> --plan <flow.maui-plan.json> --json
   maui devflow flow validate <flow.md> --json
   ```

For an app regression:

1. edit the smallest app source surface that caused the current failure;
2. run the narrowest existing build or unit test covering that code;
3. do not update the flow to accept the broken result.

For missing testability identity, add a unique stable app-owned
`AutomationId`. Repeated or virtualized items also need a stable item key and
collection scope. Do not replace them with text, coordinates or row index.

## Verify on the same target

Run the changed checkout with the same flow, project, platform, device,
configuration, seed/reset policy and business oracle. Use a new output
directory:

```powershell
maui devflow flow run <flow.md> `
  --plan <flow.maui-plan.json> `
  --project <app.csproj> `
  --platform <platform> `
  --device <device> `
  --output <verification-output> `
  --json
```

Interpret the terminal result precisely:

- verified pass: the flow and every required independent oracle passed;
- replay pass, not independently verified: UI assertions passed but no
  independent oracle established the business result;
- failure: report the new class and do not call the change fixed;
- unknown completion or cleanup failure: report the primary outcome and
  secondary cleanup failure separately.

Do not repeatedly mutate the test to chase a green result. After two
substantively different failed fixes, stop and present the evidence.

## Final handoff

Run:

```powershell
git diff --check
git diff --stat
git status --short
```

Show a bounded diff for the changed files. Report:

- classification and confidence;
- original local run id/output;
- root cause;
- exact files and behavior changed;
- post-fix run id/output and verification state;
- remaining evidence or platform limitations;
- `Worktree changes are uncommitted; review them in Source Control.`

Do not stage, commit, push, open a pull request, or close the issue as part of
this workflow.
