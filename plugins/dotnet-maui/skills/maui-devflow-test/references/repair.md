# Selector Repair Boundary

Repair is a human review lifecycle, not an agent action. The restricted
`maui_test_patch` tool can retain an inert selector proposal, preview it, or
reject it. `approve`, `apply`, and rollback requests fail closed.

## Eligibility Gate

Discuss a proposal only after all of these are true:

- The failure is a primary, pre-dispatch `locator-not-found`.
- It came from a current local run, or an imported failure has a fresh matching
  local reproduction.
- The recorded checkpoint matches current build, instance, reset/seed, route,
  window, modal, locale, theme, orientation, display profile, and collection
  key.
- The durable selector-health shortlist yields exactly one current candidate
  with a matching value-free semantic fingerprint.
- Hard assertions remain unchanged and a required independent business oracle
  is available for validation.
- That oracle actually **verified** on the failing run. A run whose business
  outcome never happened cannot support a repair: if the drifted selector sits
  on the step that commits the outcome, the oracle fails and admission refuses
  with `independent-oracle-failed`, because a drifted selector is then
  indistinguishable from an application that is genuinely broken. The repairable
  shape is a drifted **action** selector *after* the outcome was committed and
  independently verified.

Reject repair for ambiguity, wrong route, assertion failure, unknown completion,
capability/infrastructure failure, stale source, platform divergence, data or
secret errors, coordinates, runtime IDs, type/index selectors, visible-text
fallbacks, duplicate IDs, and unscoped virtualized rows.

## Human Ceremony

1. Store or preview one inert selector proposal with `maui_test_patch`.

   **Ask for the canonical patch first.** A proposal is accepted only when its
   `patchDigest` equals the one the broker rebuilds from the committed flow, and
   that digest covers canonical before/after flow digests — it cannot be derived
   from anything else the restricted protocol exposes, so never try to construct
   or guess it. Call `maui_test_patch` with `preview` and a `proposal` carrying
   only `sourceStepId` and the candidate `proposedSelector`. Nothing is stored
   and nothing is approved: the reply states the canonical `patch`, `patchDigest`,
   diff, and invariant proof. Submit `proposal` with that exact digest.

   This also needs the draft to have been authored with a `flow.path`. The patch
   channel is keyed on the flow's path, so a plan committed without one can never
   carry a repair however eligible its failure is. Declare `plan.flow.path` at
   `maui_test_author begin`.

   Read `maui_test_failure`'s `selectorRepair` block before proposing: it reports
   `eligible`, `proposalRecommended`, and, when refused, `admissionReasonCodes`.
   A refusal there is a fact about the run, not something a retry can change.
2. The human reviewer reviews the rationale, score, risks, and evidence.
3. The human issues a bounded validation grant. The platform lifecycle performs
   a hard reset and in-memory override replay; it does not commit a flow.
4. The human separately approves a selector-only compare-and-swap apply bound
   to the proposal, flow digest/revision, target, and expiry.
5. The host performs the clean reset/oracle verification replays or enters
   rollback-required.

The agent does not issue grants, apply the patch, alter actions/order/assertions,
or change application source. When a durable selector requires a new
`AutomationId` or stable item key, make a separate testability recommendation.

## Where the Agent Stops, and What It Hands Over

Steps 2–5 are human-gated on purpose. Validation replay, approval, and the
compare-and-swap apply each mutate a committed artifact or the device, and the
whole point of proposing rather than healing is that a human decides. There is
no agent-side route to them and you should not go looking for one: a refusal
there is the design working, not a capability gap.

What the agent owes the human is a decision they can actually take. Storing the
proposal is not the deliverable — presenting it is. End the work by showing:

- the proposal id and the step it repairs;
- the selector change, old to new, using the returned `diff.markdown`;
- the invariant proof: assertions, actions, values, and order all unchanged;
- what made the run a sound basis — the terminal failure class, and that the
  required independent oracle verified the business outcome;
- the single decision you need: approve this selector change, or reject it.

Say plainly that you cannot approve or apply it, and that the change stays inert
until they act. Never leave a stored proposal unmentioned: a repair nobody was
told about is indistinguishable from no repair at all.
