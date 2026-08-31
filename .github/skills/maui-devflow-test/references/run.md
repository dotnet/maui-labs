# Run a Committed Flow

Only a committed Markdown flow with its current plan/flow digest can be
executed. Drafts, chat approval, imported artifacts, and CI status are not
execution authority.

## Preflight

1. Confirm the named committed flow, current revision/digest, and selected
   exact `agentId` plus `agentInstanceId`.
   A checked-in Markdown flow plus matching plan sidecar is a committed disk
   artifact, but `maui_test_run` is intentionally authoring-session-bound and
   cannot route that disk artifact without a session. Never use an unrelated
   active draft to claim that the checked-in flow itself is uncommitted.
2. Read `maui_test_capabilities`; stop rather than routing to an unsupported
   platform or action.
3. Check declared route, build, app instance, seed/backend, locale, display,
   and reset facts. Ask for a reset/seed provider when a repeatable or mutating
   flow needs one.
   **Confirm a reset owner exists before requesting a run approval.** When the
   plan sets `reset.required: true`, admission compares the declared
   `resetIdentity` and seed fingerprints against fingerprints an owner actually
   reports, and fails closed on `not-proven` or `-mismatch`. A one-shot run
   grant is consumed by that attempt even though no action ran, so an
   unsatisfiable reset contract costs the human an approval and produces no
   result. `maui_test_validate` with `mode: "live"` returns an `admission`
   block for exactly this: it names the declared strategy and identity, lists
   anything missing, and cannot confirm an owner exists. Treat
   `resetRequired: true` with no confirmed owner as a stop. Ask the human
   whether a reset owner is registered for this target, or agree to declare
   `reset.required: false` and say plainly that repeated runs are then not
   independent. Never invent a `resetIdentity`; a plausible-looking value that
   no owner reports fails admission exactly like an empty one.
4. State the verification limitation if no required independent business oracle
   is declared. Do not call the result a verified pass.
5. For `non-replayable`, prepare only a one-shot run review request. It cannot
   authorize a retry, continuation, compensating run, or repair validation.

## Human-Approved Execution

1. Request a separate `run` approval bound to the committed flow and exact
   target (`agentId` and `agentInstanceId`). The scope must match selectors,
   routes, action types/count, values, side effects, build/seed, plan/flow
   revision and digest, and expiry.
2. Direct the human to review the exact scope in the approval host. A chat response
   never replaces this grant. If capabilities report that trusted native-host
   approval is unavailable (including standalone browser or chat), do not
   submit a doomed approval request and do not query authoring-session status
   as a substitute. State that restricted-agent execution is unavailable,
   while keeping the committed disk flow distinct. The separate operator-owned
   `maui devflow flow run` CLI path may execute a valid committed flow outside
   this restricted approval protocol. Describe it as an explicit operator
   action, never as an approval bypass or an agent run.
3. Wait with `maui_test_author await-approval`, then call `maui_test_run` with
   `start` exactly once using a broker-issued, single-use grant only when an
   available approval host issued that exact bound grant.
4. Use `maui_test_run` with `status` and the run capability until the run is
   terminal. For a terminal failure, call `maui_test_failure` exactly once;
   use `maui_test_trace` for its bounded evidence projection.

If a response is lost, report `unknown-completion`. Never retry automatically;
the human must inspect the outcome and issue a new appropriate authorization if
another action is safe.
Never create a selector repair, repair request, or a second `start` as a
continuation from an uncertain or failed run.

## Result Language

Report completed, failed, cancelled, unknown-completion, or completed but not
independently verified. Do not upgrade simulator, CI, source-only, or
attested-artifact evidence to a physical-device or qualification claim.
Preserve the committed plan's reset, seed, and oracle requirements exactly;
never infer that they are unnecessary merely because the actions look UI-only.

A run is finished only when `maui_test_run status` reports a terminal state.
Nothing else counts: not a granted approval, not a submitted `start`, not a
corrected draft, and not a plan you believe would pass. If you keep a task list,
the run task stays open until you hold that terminal state, and abandoning a
draft moves it backwards rather than completing it. Ending a turn without a
terminal state is an unfinished run — say exactly where you stopped and what
remains, and never record it as done.
