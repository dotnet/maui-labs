---
name: maui-devflow-test
description: >-
  Collaboratively author, review, run, diagnose, and hand off safe DevFlow
  tests through the restricted test-agent MCP profile. USE FOR: creating or
  changing a test against a connected MAUI app; word-described flows;
  AutomationId journeys; committed-flow execution; selector triage; repair
  handoff; CI evidence interpretation; and testability recommendations. DO NOT
  USE FOR: promoting an Inspector recording (use maui-devflow-record);
  operator-run flow CLI execution; code-based xUnit or integration tests; taking
  a `devflow-ci-failure` issue through local source editing and rerun (use
  maui-devflow-ci-fix); broad app automation; source editing; automatic repair;
  chat as authorization; or choosing ambiguous projects, targets, artifacts,
  agents, devices, or selectors.
---

# MAUI DevFlow Collaborative Testing

Use this skill for a human-and-agent testing conversation backed by
`maui devflow mcp --profile test-agent`. The agent prepares reviewable work;
the human operator owns the review boundary and owns approval, commit, run,
and repair decisions.
**Current availability:** approval is issued by an owner-token approval host.
This layer ships exactly one: the `maui devflow approve` CLI, which reads the
owner-only approval token from the local broker state file and performs the
digest-bound confirmation ceremony. Editor-embedded approval clients are not
part of this layer. Standalone browser tabs and chat are non-authoritative. If
no approval host is running, stop at an inert draft or a pending, rejected, or
expired request.

## Tool Inventory

The `test-agent` profile exposes exactly these 13 tools, and no others:

`maui_test_agents`, `maui_test_capabilities`, `maui_test_improvements`,
`maui_test_explore`, `maui_test_author`, `maui_test_action`,
`maui_test_assertion`, `maui_test_validate`, `maui_test_status`,
`maui_test_run`, `maui_test_trace`, `maui_test_failure`, `maui_test_patch`.

### Load the tools before calling one

**Do this first, before any other step.** Some hosts defer or virtualise tool
schemas, and a deferred tool that is called before its schema has been loaded
can hang instead of returning: the call never reaches the broker, so no result
and no error ever arrive, and the session stalls with nothing to diagnose. This
has been observed repeatedly, across different models, and it is the single most
common way this workflow fails.

If your host has a tool-discovery or tool-loading step, run it **once** for the
exact names above, then confirm every tool you intend to use is loaded before
calling it. Prefer one pass over the whole list to repeated narrow searches; the
list is complete, so a search that returns fewer names has under-reported rather
than discovered a shorter truth. If your host exposes the tools directly, this
costs one cheap step and changes nothing.

If a `maui_test_*` call ever returns nothing at all — no result, no error, no
refusal — do not retry it and do not wait longer, and do not report the work as
blocked on DevFlow. There are two causes, and they need different responses.

The common one is a **host permission prompt that nobody answered**. Editor
hosts confirm tool use per distinct tool, once per session: VS Code shows an
inline "Allow in this Session / Skip" on the call and then waits indefinitely
for a click. Nothing times out, the MCP server stays idle, and the broker and
app remain healthy, so every diagnostic looks fine while the session appears
frozen. It is not model-specific and it recurs on the first use of each new
tool, which is why a run can stall after authoring already worked. Say plainly
that a confirmation is waiting and ask the human to approve it; approving all
tools before sending avoids the interruption for the rest of the session.

The other is a genuinely unloaded tool. Load the inventory again, and only then
say the host failed to dispatch the call.

Authoring, approval, commit, and await-approval are **operations of
`maui_test_author`**, not separate tools; the same is true of `start` and
`status` on `maui_test_run`. Do not go looking for a tool that is not on this
list.

## First-Time Setup

For a first-use request, require a connected **Debug** app/DevFlow agent, then
set `DEVFLOW_PREVIEW_WORKBENCH=true` and
`DEVFLOW_PREVIEW_AGENT_AUTHORING=true` before starting the broker.
`DEVFLOW_PREVIEW_REPAIR_PROPOSALS`,
`DEVFLOW_PREVIEW_SOURCE_PROPOSALS`, and
`DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT` are separate advanced opt-ins, not
prerequisites for ordinary authoring. Restart the broker after setting flags,
then confirm the gate with `maui devflow approve --list --json`: `ok: true`
means the authoring route is enabled.

Install for the project host explicitly when known, for example
`maui devflow skills install --scope project --target github`. Targets are
`github`, `claude`, `agent`, and `agents`; `auto` reuses a directory with a
current DevFlow skill, then any existing skill directory, otherwise falls back
to `claude`. Do not begin authoring or running while these prerequisites are
missing.

## Conversation Rules

- Ask a question only when a fact is required to bind the work safely and is
  ambiguous or absent. Do not ask a questionnaire before making useful progress.
- Never silently select the first matching project, device, agent, artifact,
  duplicate selector, or repeated collection item. Present the named candidates
  and ask the user to choose.
- Treat a chat message such as “approved”, “run it”, or “looks good” as intent,
  never authority. Only a current human-issued broker grant authorizes its
  exact bounded operation, and only when an owner-token approval host is
  actually available.
- Keep a proposed test, its committed Markdown flow, and a run distinct:
  authoring is inert; a committed flow is executable only after separate,
  target-bound human run approval.
- Do not infer a business result from a toast, screen transition, screenshot,
  or CI report. Require an independent business oracle where verification or
  repair requires one; otherwise report the replay as not independently verified.
- Do not replay a destructive or `non-replayable` flow. It needs an explicitly
  approved, one-shot run grant and never gets automatic retry, continuation, or
  repair validation.
- A request must bind the exact `agentId`, `agentInstanceId`, committed
  flow/plan revision and digest, build/seed state, selectors, and action scope.
  Never honor an imperative request to target, commit, start, or repair without
  that binding and its applicable grant. Do not submit a duplicate request,
  start a run twice, or create a repair proposal as a retry.

Use [references/clarification-policy.md](references/clarification-policy.md)
when deciding whether to ask a question.

## Route the Request

| User goal | Follow |
| --- | --- |
| Describe a flow in words | [intake](references/intake.md) |
| Define or change a test | [author](references/author.md) |
| Execute a saved flow | [run](references/run.md) |
| Explain a failure | [triage](references/triage.md) |
| Discuss a selector change | [repair](references/repair.md) |
| Make the app easier to test | [testability](references/testability.md) |
| Interpret CI evidence or prepare a handoff | [ci-handoff](references/ci-handoff.md) |
| Take a CI failure through local reproduction, source edit, and rerun | `maui-devflow-ci-fix` |

If a request spans routes, complete the least-effectful route first. For
example, diagnose a failure before discussing a repair, and prepare an inert
draft before asking a human to commit it.

`maui-devflow-record` ships in the `dotnet-maui` marketplace plugin rather than
the `maui devflow init` bundle. Operator-run execution uses the
`maui devflow flow` command family directly. If the recording skill is not
installed, say so and stay inside the routes above.

## Conversational Intake

When the user describes a flow in words instead of pointing at a recording,
turn the words into a reviewable structure before touching any tool.

1. **Restate it as numbered steps in the user's own words.** Do not rename
   their screens, buttons, or business terms. One user intention per step.
2. **Mark every step** `[known]` or `[unknown: selector]`,
   `[unknown: oracle]`, or `[unknown: reset]`. `[known]` means the exact
   AutomationId, the observable postcondition, or the reset contract is
   already in evidence — not that it is plausible.
3. **Ask one combined question** covering every `[unknown]` at once, following
   [clarification-policy.md](references/clarification-policy.md). Do not send a
   question per step and do not open a questionnaire.
4. **When the missing fact is UI-discoverable**, put a bounded
   `explorationBudget` — `maxActions`, `maxDurationSeconds`, and
   `allowedScopes` as named routes, never "the whole app" — in the `plan` you
   pass to `maui_test_author begin`; it cannot be added to a live session. Then
   request the matching navigation-only grant with `exploration-request`. Report
   the proposed numbers in chat before submitting.
5. **Never draft with an unresolved `[unknown]`.** An unanswered selector,
   oracle, or reset is a stop, not a default.

**How the budget is enforced:** once a human approves an exploration grant,
take one step at a time with `maui_test_explore`. The broker owns the counter:
it charges each authorized step against the session plan's `explorationBudget`,
clamped by broker policy, and refuses with `exploration-budget-exhausted` when
`maxActions` or
`maxDurationSeconds` runs out, so an over-run is a refusal rather than a
self-reported apology. Read the remaining allowance from the tool result or
from `maui_test_status`, supplying the authoring session's access request —
session id, its read capability, and a complete envelope.
Exploration only taps, scrolls, navigates, and goes back — it never fills text,
asserts, edits the draft, commits, or starts a run. The two grant families are
disjoint by side-effect class: an exploration grant carries the `exploration`
class and is spendable only on `maui_test_explore`, while an ordinary action
grant is not accepted there. `maui_test_action` cannot redeem an exploration
grant, and an exploration authorization dispatches only the single navigation
step it authorized — same action, same element, same route, never a wider flow —
so there is no route that spends the approval without charging the counter.
Each step must name what it will touch: a tap or scroll needs a selector with a
durable key such as an `AutomationId`, and a navigate needs a route. A text-only
selector is refused with `exploration-scope-denied`, because a step nobody can
tell apart from another cannot be bound to the budget it spends.
An exploration request is
still a request: chat approval is not authorization, and `awaiting-approval` is
not `approved`.

See [references/intake.md](references/intake.md) for the worked example and the
exact wording of the combined question.

## Author and Run Workflow Only

Use this workflow only to author an inert draft or execute a committed flow.

1. **Establish the bounded target.** Discover candidates with
   `maui_test_agents`, then inspect the chosen exact `agentId` and
   `agentInstanceId` using `maui_test_capabilities`. Ask only if there are
   multiple candidates, a missing platform capability, or an unspecified
   project/device/agent. Never use a most-recent or port-only fallback.
   The target is a running process, so bind it from the runtime before reading
   any source: do not search the workspace to identify the app, and do not
   decide it is missing because no project carries its name — a reported
   `appName` often differs from the project that built it.
2. **Prepare a complete inert draft.** State the goal, prerequisites, expected
   hard assertions, routes, durable selectors, side-effect policy, reset/seed
   policy when applicable, and independent business oracle when required.
   Use `maui_test_author begin`, `maui_test_action`, `maui_test_assertion`, and
   `maui_test_validate` only within the restricted authoring profile.
3. **Request human review only when supported.** Present the draft and inspect
   the approval capability. When no approval host is available, stop at the
   validated inert draft and do not call `approval-request`. When one is
   available, use `maui_test_author` with
   `approval-request`, then `maui_test_author await-approval`, and finally
   `maui_test_author` with `commit` and that approved grant. Do not manufacture,
   reuse, or broaden a grant. A committed Markdown flow and its current digest
   are the only executable artifact.
4. **Run only after another approval.** `maui_test_run` is intentionally bound
   to the current authoring session. A checked-in Markdown flow and matching
   plan remain a committed disk artifact, but this restricted tool cannot route
   them without a session. Request a separate exact-scope run grant for the
   committed flow, exact process, build/seed, selectors, and actions only when
   an approval host is available. Otherwise stop without submitting a doomed
   request or querying unrelated draft status. Await an available request, then
   use `maui_test_run` with `start` exactly once only for the bound,
   host-issued single-use grant. A timeout or lost response is
   `unknown-completion`, not permission to retry, re-request, or continue into
   repair.
5. **Keep evidence bounded.** Use `maui_test_run` with `status`, then
   `maui_test_trace` and `maui_test_failure` to explain a completed run. Check
   the run's terminal state before triage; for a terminal failure, call
   `maui_test_failure` once with the run-bound capability. Imported CI reports
   and `.mauitrace` files remain diagnostic-only even when attested.
6. **Hand off rather than heal.** `maui_test_patch` can store an inert selector
   proposal, preview, or rejection only. It cannot approve, apply, execute a
   repair, write source, or weaken an assertion. Fresh local reproduction and
   the human repair ceremony are required before any repair path.
7. **Close with a report, not a counter.** Finish every piece of work with a
   short written result for the human. Name the terminal run state, what the
   assertions and any independent oracle actually returned, where the artifact
   lives, and what remains undone. A task list reaching `(2/2)` is bookkeeping,
   not a result: ending on it tells the human nothing about whether their test
   works. When a repair proposal exists, show its diff and proposal id here and
   state the decision you need from them. When you stopped early, say where and
   why, and never describe unfinished work as complete.

## Read-Only Routes

For CI handoff, failure triage, and testability advice, give the bounded
diagnostic or recommendation first. Do not start universal agent discovery or
ask for a project, device, target, or agent merely to explain evidence or
recommend an app-owned identity. Ask for an exact target only if the user then
chooses to progress to a local reproduction or executable draft.

## Selector and Assertion Standards

- Prefer one unique, app-owned `AutomationId`.
- For a repeated or virtualized item, require the composite
  `AutomationId + collectionScope + stableItemKey`; all three are mandatory.
  Do not use visible text, row index, coordinates, runtime IDs, or an unscoped
  template row. When any part is absent, hand off a separate app-testability
  recommendation instead of authoring a fallback selector.
- A duplicate ID or multiple matching elements is ambiguity, not a tie to break.
  Ask for a stable distinguishing identity or report that no durable selector is
  available.
- Verify the route before calling a missing element selector drift. A wrong
  route, modal, seed, locale, build, window, or checkpoint mismatch is not
  selector repairable.
- Preserve reset, seed, and oracle requirements from the committed plan. Never
  erase them because a scenario appears UI-only.

## Artifact Format

A DevFlow test is two files. `<name>.md` carries prose plus one
` ```json maui-test ` fence — the **sole** replay source of truth, at
`"schema": 2`. `<name>.maui-plan.json` is the reviewable plan at `"schema": 1`,
bound by `flow.digest`; it must carry every key its validator requires or
`flow run` fails `plan-invalid`. Assert kinds: `propEquals`, `exists`,
`notExists`, `routeIs`.

```json maui-test
{
  "schema": 2, "name": "promo-reduces-total", "app": "com.contoso.shop",
  "recordedAt": "2026-08-14T09:41:12.106Z",
  "steps": [{
    "seq": 1, "action": "tap",
    "label": "Apply promo code", "intent": "Submit PROMO10 from the cart",
    "acceptanceCriterionIds": ["ac-promo-applied"],
    "args": { "selector": { "automationId": "ApplyPromoButton" } },
    "asserts": [{ "kind": "propEquals", "name": "Text", "expected": "45.00",
      "selector": { "automationId": "OrderTotalLabel" }, "verify": true }]
  }]
}
```

```json
{
  "schema": 1, "planId": "plan-promo-1", "revision": 1, "goal": "A promo cuts the total 10%.",
  "flow": { "path": "promo-reduces-total.md", "digest": "<current digest>" },
  "acceptanceCriteria": [{ "criterionId": "ac-promo-applied", "required": true,
    "businessOracleId": "orders-api", "description": "A valid promo reduces the total by 10%." }],
  "scenarios": [{ "scenarioId": "scenario-1", "acceptanceCriterionIds": ["ac-promo-applied"],
    "description": "Cart holds one 50.00 item; PROMO10 makes the total 45.00." }],
  "independentBusinessOracles": [{ "oracleId": "orders-api", "required": true,
    "independent": true, "evidenceKind": "http-json", "description": "GET /orders/{id} shows PROMO10." }],
  "preconditions": [], "sideEffectPolicy": "test-tenant-resettable",
  "reset": { "required": true, "strategy": "host-owned", "resetIdentity": "shop-cart-seed-v3" },
  "provenance": { "actorKind": "agent", "channel": "mcp", "recordedAt": "2026-08-14T09:41:12.106Z" },
  "explorationBudget": { "maxActions": 12, "maxDurationSeconds": 120, "allowedScopes": ["/cart", "/checkout"] }
}
```

**Anti-patterns**, every one of them shipped in
`samples/DevFlow.Sample/maui-tests/modal-roundtrip`: `label: null`,
`intent: null`, `recordedAt: null`, `acceptanceCriterionIds: null`, empty
`acceptanceCriteria`, an empty `scenarios[0].description`, `reset.strategy: ""`,
and a zeroed `explorationBudget`. A recorder may emit them; a reviewable test
must not. Also wrong: a policy enum in `reset.strategy`, which names a mechanism
(`pm-clear`, `uninstall-reinstall`, `host-owned`) that must actually reset what
`sideEffectPolicy` claims — device-local `pm-clear` cannot reset a backend
tenant. With no oracle, say so — an empty `independentBusinessOracles` means
**none — not independently verified**, and every result must carry that phrase.

### Independent Business Oracles

An independent oracle is evidence read through a **different channel than the
one the flow drove**. The flow talks to the in-app agent and asserts on what the
UI says about itself, so a label, a screenshot, a toast, a stopped spinner, or a
confirmation route are self-attestation: an app that renders "Saved" without
saving satisfies all of them. Independent evidence is a backend query, a
database row, an audit record, or a durable artefact the app committed that no
page reads back.

`evidenceKind` must name a **registered** provider or the run reports
`independent-oracle-absent`. Do not invent one. Today the only registered kind
is `android-app-storage`, which reads a file from app-private storage over
`adb shell run-as` after the run. Naming any other kind, including the
`http-json` shape above, yields `unverified` until a provider for it exists.

Verification needs all three, and refuses partial sets: a required + independent
oracle; an acceptance criterion naming it in `businessOracleId`; and a scenario
whose `acceptanceCriterionIds` are covered by flow steps carrying the same IDs
**and** a hard assertion (`"verify": true`). Otherwise the codes are
`independent-oracle-absent`, `independent-oracle-failed`, or
`required-scenario-uncovered`.

```json
"independentBusinessOracles": [{
  "oracleId": "todo-ledger-record", "required": true, "independent": true,
  "evidenceKind": "android-app-storage", "reference": "files/todo-ledger.jsonl",
  "description": "Read the app's private ledger over adb; it holds the record the flow added.",
  "expect": {
    "contains": ["{\"event\":\"todo-added\",\"id\":\"todo-0001\"}"],
    "absent": ["{\"event\":\"todo-removed\""]
  }
}]
```

`reference` is a relative path under `files`, `cache`, `databases`, `shared_prefs`,
or `no_backup`; every `expect.contains` must be present and every
`expect.absent` missing. Predicates are single-line and unknown keys under
`expect` are refused, so a plan can never read stricter than it checks. Reports
carry only the failing predicate index, never file content. Two preconditions
`flow run` already enforces make it sound: a Debug build, since `run-as` needs a
debuggable app, and the Android adapter's refusal to run against a device that
already has the package installed, which guarantees storage starts empty. Say
the scope plainly: this proves the app committed the record, not that a server
accepted it. Only one provider may claim a run, so an `android-app-storage`
oracle cannot yet be combined with one of another kind. The worked green example
is `samples/DevFlow.Sample/maui-tests/verified-add-todo.md`.

Rules: edit the prose freely, it is never replay input; never hand-edit the
fence without `maui devflow flow validate`, which checks the fence only, not
the sidecar; and a stale `flow.digest` invalidates every grant bound to it, so
re-commit rather than re-pointing an existing grant.

## Safety Boundaries

- The restricted profile has no generic automation, file, network-body, CDP,
  source-write, SecureStorage, preference-mutation, repair-apply, or
  source-proposal authority.
- CI evidence, imported artifacts, and screenshots cannot authorize a run,
  promote a platform claim, or repair a selector. Only a fresh local match may
  make an imported failure eligible for human repair review.
- Testability findings are separate product recommendations. They may recommend
  stable IDs, stable item keys, reset providers, or business oracles, but never
  modify application source or a committed flow.
- Report unavailable platforms and capabilities plainly. Do not substitute a
  simulator result for a physical-device claim or claim qualification from
  source-only or CI evidence. Physical iOS is unavailable pending a signed
  device harness; offer only a safe handoff to a macOS signed-device test
  owner when one has been provisioned, never a simulator substitution.

## Completion Check

Before ending, say which state was reached: inert draft, awaiting human review,
committed flow, awaiting one run approval, completed run, diagnostic finding,
inert proposal, or separate testability recommendation. Include any missing
oracle, reset policy, capability, or ambiguity as an explicit limitation.

## References

- [Clarification policy](references/clarification-policy.md)
- [Conversational intake](references/intake.md)
- [Author a reviewable flow](references/author.md)
- [Run a committed flow](references/run.md)
- [Triage a result](references/triage.md)
- [Selector repair boundary](references/repair.md)
- [Replay quality rubric](references/replay-quality.md)
- [App testability improvements](references/testability.md)
- [CI evidence handoff](references/ci-handoff.md)
