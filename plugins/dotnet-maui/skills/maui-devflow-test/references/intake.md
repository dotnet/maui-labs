# Conversational Intake

Use this route when the user describes a journey in words — "check that a promo
code lowers the total" — instead of pointing at a recording or a committed
flow. The goal of intake is a structure the user can correct in one reply, not
a draft.

## Step 1 — Restate as numbered steps

Write the journey back as numbered steps, in the user's own vocabulary. Do not
rename their screens, buttons, or business terms, and do not add steps they did
not describe. One user intention per step; if a sentence contains two
intentions, split it and say that you split it.

## Step 2 — Mark every step

Append exactly one marker to each step:

| Marker | Meaning |
| --- | --- |
| `[known]` | The AutomationId, postcondition, or reset contract is already in evidence — a tree dump, a committed flow, or something the user stated. |
| `[unknown: selector]` | No stable element identity for the target. |
| `[unknown: oracle]` | No observable postcondition that proves the step worked. |
| `[unknown: reset]` | No stated way to return the app or backend to the starting state. |

`[known]` means *observed*, not *plausible*. A control that "is probably called
`SubmitButton`" is `[unknown: selector]`.

## Step 3 — Ask one combined question

Collect every marker into a single message, following
[clarification-policy.md](clarification-policy.md). Name the step number, the
missing fact, and the smallest answer that unblocks it.

> Steps 1 and 4 are clear. Three things are still unknown:
>
> - Step 2 `[unknown: selector]` — which control applies the code? If it has an
>   AutomationId, that name is enough.
> - Step 3 `[unknown: oracle]` — what proves the discount applied: the total on
>   screen, or an order record you can query?
> - Step 5 `[unknown: reset]` — how does the cart get back to empty between
>   runs, or should the flow assume a fresh test tenant?
>
> Answer whichever you know; I will propose a bounded look for the rest.

Do not send one question per step. Do not open a questionnaire, and do not
guess a default in order to keep the conversation moving.

## Step 4 — Propose a bounded exploration budget

When a remaining `[unknown]` is UI-discoverable, propose a budget and state the
numbers in chat before submitting anything:

```json
{ "maxActions": 12, "maxDurationSeconds": 120, "allowedScopes": ["/cart", "/checkout"] }
```

`allowedScopes` lists named routes or screens. "The whole app", `"*"`, and an
omitted scope list are all invalid proposals. Size `maxActions` to the number
of unknowns, not to the size of the app.

The budget itself must already be present as `explorationBudget` on the `plan`
you passed to `maui_test_author begin`; it cannot be added to a live session. If
you began without one, `abandon` and begin again with the budget in the plan —
the broker refuses every exploration step with `exploration-budget-required`
until the plan declares it.

Then request the matching grant with the `exploration-request` operation of
`maui_test_author`. Its `explorationScope` may list only `tap`, `scroll`,
`navigate`, and `back`; anything wider is refused. Then wait. An exploration
request is a request; chat approval is not authorization, and
`awaiting-approval` is not `approved`.

## Step 5 — Explore within the approved budget

Once a human approves the exploration grant, take the discovery steps one at a
time with `maui_test_explore`, naming the scope you are exploring and the
navigation action. The budget is **enforced by the broker**, not by this skill:

- Each authorized step is charged against the session plan's
  `explorationBudget`, clamped by broker policy, by a server-side counter. When
  `maxActions` is spent, or
  the `maxDurationSeconds` window that opened on the first step has elapsed,
  the next step is refused with `exploration-budget-exhausted`. Running over is
  a refusal, not something to apologize for afterwards.
- The remaining allowance comes back on every `maui_test_explore` result, and
  `maui_test_status` reports it when given the authoring session's access
  request — session id, read capability, and a complete envelope. Quote that
  number rather than your own tally.
- A scope that is not in the approved `allowedScopes` is refused with
  `exploration-scope-denied`, and the broker caps an over-generous plan at its
  own policy limit.
- A step must name what it will touch, or it is refused with
  `exploration-scope-denied` before any budget is spent. A tap or scroll needs a
  selector with a durable key such as an `AutomationId`; a navigate needs a
  route. A text-only selector has no durable key, so it would let one approved
  tap stand in for a tap on any other unkeyed element.
- Exploration only taps, scrolls, navigates, and goes back. Filling text,
  asserting, appending to the draft, committing, and running each need their
  own approval, and the grant must be an exploration grant: one that also
  permits drafting, running, or committing is refused.
- Exploration grants carry the `exploration` side-effect class and are spendable
  only here. `maui_test_action` refuses them, and the authorization an
  exploration step mints dispatches exactly one navigation step matching the
  action, element, and route it approved, so one unit of budget can never replay
  a wider flow and there is no way to redeem the approval on a
  route that skips the counter — and an ordinary action grant is
  refused by `maui_test_explore` for the same reason.
- The broker also bounds what it will honour: `maxActions` is clamped to its own
  policy ceiling and the window to ten minutes, so an over-generous plan buys
  nothing extra. `maui_test_status` reports the clamped numbers, not the ask.

Report what you found and how much of the allowance the broker says is left.

## Step 6 — Draft only when nothing is unknown

Every step must be `[known]` before the author route starts. An unanswered
selector, oracle, or reset is a stop, not a default. If the user declines to
answer, record the gap as an explicit limitation and stop with an inert
partial restatement instead of a draft.

## What stays outside the enforced budget

The broker enforces the count, the clock, and the scope. It does not judge
whether a step was a good idea, so two things remain yours:

- **Size the proposal honestly.** `maxActions` should match the number of
  unknowns, not the size of the app. The broker will not object to a wasteful
  budget a human approved.
- **Stop when the question is answered**, even with allowance left. Unspent
  budget is not an invitation.

Never describe exploration as unbounded or as something the agent polices
itself — the count, the window, the scope list, and the navigation-only action
set are all checked server-side before each step is authorized.

## Worked example

User: *"Make sure a promo code lowers the total."*

1. Open the cart with one item in it. `[unknown: reset]`
2. Enter the promo code. `[unknown: selector]`
3. Apply it. `[unknown: selector]`
4. The order total drops by ten percent. `[unknown: oracle]`

One combined question covers the reset contract, both selectors, and whether
the total on screen is sufficient proof or an order-service check is required.
If the user answers only the selectors, propose a 12-action, 120-second,
`/cart`-scoped exploration for the reset contract, submit
`exploration-request`, and hold. Nothing is drafted until step 4 has an oracle
— or until the user accepts, in writing, that the result will be reported as
**none — not independently verified**.
