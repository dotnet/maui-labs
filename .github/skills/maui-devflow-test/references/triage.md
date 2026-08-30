# Triage a Failed Flow

Triage is read-only analysis. First use `maui_test_run` with `status` for the
exact run; once it is terminal, use `maui_test_failure` and
`maui_test_trace`. Do not use diagnostics to issue a grant, start another run,
or silently amend the flow.

Give the bounded classification first. Do not ask for project, device, agent,
or target selection just to explain a failure; request the exact target only if
the user chooses to proceed to a local reproduction or executable draft.

## Classify Before Proposing a Selector Change

Check the recorded pre-step context first:

1. Current local run and current flow/plan digest.
2. Exact app process, build, seed/backend state, route, window, modal, locale,
   theme, orientation, display profile, and collection key.
3. Failure phase and code.
4. Hard assertion and independent business-oracle result, if required.

A route, modal, checkpoint, build, seed, capability, actionability, assertion,
or ambiguity failure is **not** selector drift. Explain the mismatch and route
it to setup, flow authoring, or app debugging as appropriate.

Only a primary pre-dispatch `locator-not-found` on a current local run can
enter the separate selector-repair discussion. Do not classify a post-dispatch
failure, timeout, unknown completion, CI-only failure, or missing oracle as
repairable.

## Imported and CI Evidence

An imported `flow-run.json` or `.mauitrace` starts untrusted. Attestation can
verify supplied provenance but remains diagnostic-only. It cannot run a flow,
authorize a repair, or make a CI result pass.

For repair discussion, an imported failure must match a **fresh,
broker-owned local reproduction** of the current flow digest, app/source and
package fingerprints, target profile, failure code, step, and checkpoints.
Until then, report the artifact as a diagnostic clue only.

## Triage Output

Provide a bounded summary: failure class, evidence used, confidence, what
cannot be concluded, and the next least-effectful route. Redact secrets,
entered values, raw UI text, screenshots, logs, and identifiers not needed for
the decision.
