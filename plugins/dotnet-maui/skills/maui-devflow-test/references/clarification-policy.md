# Clarification Policy

The conversation should advance from available evidence. Ask a question only
when an answer changes a safety boundary, target binding, or durable test
meaning. Combine related unknowns into one short, concrete question.

## Route-Specific Questions

Agent/project/device discovery applies only while authoring a draft or preparing
an executable run. CI interpretation, read-only failure triage, and
testability advice first provide the bounded conclusion already supported by
the supplied evidence. Do not ask for a target on those routes unless the user
chooses to continue to local reproduction or an executable draft.

## Ask When a Fact Is Ambiguous

| Trigger | Ask for | Do not do |
| --- | --- | --- |
| Several MAUI projects or flows match | The project or committed flow name | Pick the first project or latest flow |
| Several agents, devices, or app instances are present | The exact displayed target | Use a port, most-recent, or default target |
| More than one selector matches | A stable differentiator | Pick by document order or coordinates |
| A collection is repeated or virtualized | Collection scope and stable model item key | Use visible text or an item index |
| Expected business result is unclear | The independent oracle and its expected result | Equate UI appearance with business success |
| Repeated execution can change data | Reset/seed provider or side-effect policy | Assume a reset exists |
| The flow is destructive or non-replayable | Whether to prepare a one-shot review request | Run, retry, or compensate automatically |
| The user wants the test to repair itself, or says a selector drifts | Confirmation of a replayable side-effect policy, a required independent oracle, and a repeatable run | Author `non-replayable` or a one-shot and leave repair silently foreclosed |
| A platform or action capability is absent | Whether to stop, use a supported target, or hand off | Claim a substitute platform result |
| An imported artifact is offered for repair | Whether a fresh local reproduction is available | Treat CI or attestation as execution authority |
| A flow is described in words and names a screen you have not seen | Which route it is, or approval for a bounded look at named routes | Assume a route name from the screen title |
| Intent is stated without an acceptance criterion | The single observable postcondition that proves the intent | Convert "it should work" into an `exists` assert |
| The user quotes on-screen text as the expected result | Whether the property must equal exactly that text, or whether the underlying fact should be proven by another oracle | Assume the quoted fragment is the whole rendered value |
| A required fact is UI-discoverable but unknown | Whether to submit a bounded `exploration-request`, and the `maxActions`, `maxDurationSeconds`, and named `allowedScopes` | Explore first and report the budget afterwards |
| The user states a durable design preference | Whether to record it for the rest of the session | Silently generalize a one-off choice into a rule |
| Several flows already cover the same user journey | Which flow is canonical and what happens to the others | Add a near-duplicate flow, or delete or overwrite an existing one |

## Preference Capture

A preference is a durable, non-safety choice the user states once and expects
to hold — a naming convention, a default platform for drafts, a preferred
oracle source, a house style for step labels.

- **Echo it once**, verbatim, on the turn it is stated:
  `Recorded preference: flow-naming = kebab-case verb-first`.
- **Reuse it silently** afterwards. Do not re-ask, and do not re-announce it on
  every turn.
- **Re-confirm only when applying it would change a safety boundary** — a
  target binding, an approval scope, a reset or side-effect policy, a
  destructive action, or an independent-oracle claim. Then ask again for that
  specific case and say why the recorded preference is not sufficient.
- **Never infer a preference from a single accepted suggestion.** The user
  agreeing to one selector is not a rule about selectors.
- A preference lives in the conversation only. It is not persisted, it does not
  travel to another session, and it never becomes part of a committed plan
  unless the user asks for that explicitly.

## Do Not Ask

Do not ask for preferences that do not affect the next safe step. For example,
if the user supplied one saved flow and one exact target, prepare a diagnostic
or authoring draft without asking for a general project overview.

Do not ask whether a chat approval is “real.” Explain that it expresses intent
only, then direct the human to the approval request with the displayed exact
scope.

## Question Shape

Name the ambiguity, show the available safe choices, and ask for the single
fact needed. For example:

> Two running targets match this app: Android emulator `Pixel_8` and Windows
> desktop. Which exact target should the human reviewer bind to this draft?

When no durable choice exists, do not turn the question into permission to
guess. Report the block and suggest the separate testability route.

## Business Oracle and Reset Questions

Ask for an independent business oracle when the user asks to claim verification
or proposes a repair. An ordinary replay may remain possible without one only
when policy permits it, but label the outcome **not independently verified**.

Ask for reset/seed details when a run is repeatable, mutates state, relies on
test data, or declares `test-tenant-resettable`. That policy is valid only when
the host actually provides and verifies its reset/seed contract.
