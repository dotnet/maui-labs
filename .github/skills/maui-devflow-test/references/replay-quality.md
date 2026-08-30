# Replay Quality Rubric

Use this rubric to score a flow before proposing it, and to explain why a
recording that replays green is still weak. Score each dimension **strong**,
**adequate**, or **weak**, name the weakest dimension first, and never average
the scores into a single number.

## 1. Selector Tier

| Tier | Form | Verdict |
| --- | --- | --- |
| 1 | `automationId` | strong |
| 2 | Typed structural path scoped to a stable container | adequate |
| 3 | Visible text, index, document order, coordinates | weak |

Tier 3 is a defect even when it passes. A tier-3 selector on a repeated or
virtualized collection is always weak — recommend a stable model item key via
the [testability](testability.md) route.

## 2. Assertion Strength

| Level | Shape | Verdict |
| --- | --- | --- |
| Strong | `propEquals` on a value the feature actually changes | strong |
| Adequate | `notExists` or `routeIs` proving a state transition | adequate |
| Weak | `exists` on a control that was already on screen | weak |
| None | `pageChanged` only, or no `verify: true` assert at all | weak |

`pageChanged` is report-only and is not in the verifiable set. A step with no
`verify: true` assert proves that the action did not throw, nothing more.

## 3. Determinism

Strong when the flow declares a reset contract the host actually provides
(`reset.required: true` with a real `strategy` and `resetIdentity`), fixes its
seed data, and does not depend on wall-clock time, network latency, ordering of
asynchronous work, or leftover state from a previous run. Weak when
`reset.strategy` is empty, when the flow only passes on a freshly installed
app, or when it contains a sleep chosen by trial and error.

## 4. Evidence Completeness

Strong when the run produced a flow-run artifact with per-step results, a
screenshot or tree at the failing step, and a plan whose `flow.digest` matches
the file that ran. Weak when evidence is a console tail, when the digest is
stale, or when the artifact is imported and unverified — see
[ci-handoff](ci-handoff.md) for how imported evidence is labelled.

## 5. Flake Signal

Strong when the same flow, same build, same target has repeated results. Weak
when the only signal is one green run. Report the sample size you actually
have: "1 of 1 passing" is not "stable". When a flow has failed intermittently,
say so and quote the observed ratio rather than calling it flaky.

## Hard Rule

**No metric upgrades "not independently verified".** A flow with tier-1
selectors, strong assertions, a real reset contract, complete evidence, and
twenty consecutive green runs is still **not independently verified** if no
independent business oracle observed the business result. Quality is about
replay confidence; independence is about whether anything outside the UI
confirmed the outcome. Never trade one for the other, and never let a high
score soften the phrase in a reported result.

## Reporting Shape

> Selector tier: strong (all `automationId`).
> Assertion strength: weak — step 3 only asserts `exists` on a label that was
> already visible.
> Determinism: adequate — reset declared, seed fingerprint missing.
> Evidence: strong. Flake signal: weak — 1 of 1 run.
> Weakest dimension: assertion strength. Result remains **not independently
> verified** (no business oracle declared).
