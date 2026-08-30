# Author a Reviewable Flow

Authoring through `maui devflow mcp --profile test-agent` is restricted,
broker-owned, and inert. It can prepare a plan, flow, actions, assertions, and
review request. It does not run the app, write application source, or commit
without a human-issued grant.

## Inputs for a Complete Draft

Build the draft from facts already supplied. Ask only for missing material that
changes the draft:

1. Goal and named acceptance criterion.
2. Exact selected project and target process when more than one exists.
3. Preconditions, route, and hard postconditions.
4. Durable selectors: unique app-owned `AutomationId`, or, for every repeated
   item, the complete `AutomationId + collectionScope + stableItemKey`
   composite. Do not author a partial composite.
5. Side-effect policy: `none`, `app-state-resettable`, `test-tenant-resettable`, `compensated`,
   or `non-replayable`. Choose `app-state-resettable` when the app exposes an in-process reset
   surface that restores app state without restarting the process; it proves the app-state reset
   only. Claim `test-tenant-resettable` only when a backend test tenant is genuinely reset too,
   because that policy demands backend proof an app-only reset can never supply.
6. A reset/seed provider for repeatable or mutating paths when applicable.
7. An independent business oracle when a verified pass or repair eligibility is
   required.

Do not use text, type/index, coordinates, screenshots, or a duplicate
AutomationId as a durable selector. If no durable selector exists, preserve the
finding and route to app testability; do not create a brittle executable flow.

## Repair-Admissible Drafts

Repair admission is decided by the draft, not by the failure. A textbook
pre-dispatch `locator-not-found` is still refused when the draft forecloses
repair, and the refusal appears only later at triage as
`non-replayable-repair-prohibited`, `independent-oracle-absent`,
`verification-flow-missing`, or `manual-one-shot-authorized`. Decide these
before `begin`, because no later evidence can recover them:

- A `sideEffectPolicy` other than `non-replayable`, with a reset/seed provider
  that genuinely resets the state the flow mutates. For an app that exposes an
  in-process reset surface and touches no backend, that is `app-state-resettable`;
  `test-tenant-resettable` additionally demands backend reset proof, so claiming it
  without a real backend tenant makes admission unsatisfiable rather than stricter.
- A business oracle that is both `required` and `independent`, so a candidate
  repair can be validated against evidence the UI cannot fabricate.
- A repeatable run rather than a one-shot review request.

When the human asks for a test that repairs itself, says a selector drifts, or
expects the agent to fix breakage later, confirm these three before `begin` and
name what each one costs. When the human still chooses `non-replayable` or has
no independent oracle, author it and say plainly that the flow can never produce
a repair proposal, so future drift will always need a human edit.

### Never guess the reset contract

`reset.resetIdentity` and `reset.seedFingerprint` digest owner, strategy, app,
device, and build. They are the broker's to state and cannot be derived from
source, from a seed constant in the app, or from a previous run. Do not invent
them.

Run `maui_test_validate` in `live` mode and read the `resetOffer` it returns.
Copy `strategy`, `resetIdentity`, and `seedFingerprint` **verbatim** into the
plan's `reset` block, and use the `sideEffectPolicy` the offer names. Admission
compares declared against attested values and fails closed on any mismatch, so a
guessed identity costs a one-shot run grant and forces the draft to be abandoned
and re-authored. Live validation also reports `admission.declaredMissing`; treat
a non-empty list as a blocking defect in the draft, not a warning.

If `ownerAvailable` is false there is no conforming reset owner: say so and
either declare `reset.required = false`, stating that repeated runs are not
independent and repair is foreclosed, or stop and ask the human.

## Request Envelope

Every restricted call carries one envelope. Correlation and capability fields sit
**on the envelope**, not inside the tool's own arguments, and a call that omits
them is refused for a reason that reads like a validation error rather than a
misplaced field. Use this shape:

```jsonc
{
  "schema": 1,
  "requestId": "req-<intent>-<unique>",
  "idempotencyKey": "idem-<intent>-<unique>",
  "intent": "<what this call is for>",
  "policyVersion": "test-agent-policy-v1",
  "deadlineMs": 120000,
  "target": { "agentId": "...", "agentInstanceId": "...", "appBuildFingerprint": "..." },
  "correlation": {
    "authoringSessionId": "...",   // snapshot.sessionId from begin
    "planId": "...", "planRevision": 1,
    "flowId": "...", "flowRevision": 1,
    "planDigest": "...", "flowDigest": "...",
    "runId": "..."                 // only once a run exists; run status and
                                   // failure are refused without it
  },
  "readCapabilityId": "...",       // from begin; an envelope field, not an argument
  "approvalGrantId": "..."         // the grantId await-approval returned, for
                                   // commit, run start, run status, and patch
}
```

`begin` returns every correlation value; read them from its snapshot rather than
reusing what you sent. **Commit advances the plan and flow revisions**, so
refresh `planRevision`, `flowRevision`, `planDigest`, and `flowDigest` from the
commit snapshot before requesting a run — a stale revision is refused as
`mutation-grant-stale`.

Approval scopes are checked against the approval kind: a `commit` request must
scope `allowedActions` to exactly `["author-commit"]` with `maxActionCount: 1`,
and a `run` request to exactly `["run"]`. Anything wider is
`approval-request-scope-denied`. Keep `approvalExpiresAt` inside the session
lifetime; a far-future expiry is refused.

A human needs minutes to read a scope, so leave room for one. Open the session
with the longest `durationSeconds` the policy allows rather than a value sized
to the work, and request a grant materially shorter than the session's remaining
lifetime. A session and a grant of the same length can only be approved
instantaneously: the grant is measured from the moment the human decides, so any
real review time pushes it past the session end, where it is capped. Do not
respond to a capped or refused approval by resubmitting a shorter one in a loop
— submit one request and wait for that exact request.

## Restricted Authoring Sequence

The target app is a **running process**, not a folder. Identify it with
`maui_test_agents` before reading any source. Do not search the workspace to
find out which app to test, and do not conclude an app is absent because no
project matches its name: the `appName` a device reports is the running app's
name, and it routinely differs from the project or solution that produced it
(`MauiTodo` may ship from `DevFlow.Sample`). Searching first wastes the turn and
invents a mismatch the runtime would have settled in one call. Read source only
after a target is bound, and only to confirm selectors you already observed.

1. Call `maui_test_agents`; if candidates are ambiguous, ask the human to
   choose one. If it reports no targets, say the app is not running and stop —
   do not substitute a project you found on disk.
2. Call `maui_test_capabilities` with the selected exact `agentId` and
   `agentInstanceId`. Stop if the required platform or operation is unsupported.
3. Use `maui_test_improvements` for value-free selector health facts when
   needed. It is discovery, not authority to choose an ambiguous match.
4. Use `maui_test_author begin` with the complete inert plan and flow.
5. Use `maui_test_action` and `maui_test_assertion` only when refining the
   current draft, then use `maui_test_validate`.
6. Present the whole draft, including limitations. First inspect the reported
   approval capability. When native-host approval is unavailable, stop after
   validation with the inert draft and **do not call** `approval-request`; a
   request that cannot be decided only creates a misleading failed workflow.
   When an owner-token approval host such as `maui devflow approve` is
   available, call `maui_test_author` with `approval-request` for one
   **commit** approval and give the human its `reviewCommand` (`maui devflow
   approve <approval-request-id>`).
7. Use `maui_test_author await-approval` with the returned request ID. If it is
   pending, continue waiting for that request rather than submitting a duplicate.
8. Call `maui_test_author` with `commit` only with the approved broker grant
   from an actually available approval host.
   Refresh status and record the current flow and plan revisions/digests.
   **`commit` commits the authoring session, not the workspace.** It advances
   revisions and digests in broker memory and stamps `committedAt`. It writes
   **no** Markdown flow and **no** plan sidecar to disk, and the whole session
   is lost if the broker restarts. The response carries a `persistence` block
   that says so; read it before reporting. Say "committed draft revision N,
   flow digest …" and never "committed `<name>.md` and its sidecar", never
   quote a file path, and never claim files exist. Writing files is a separate
   workspace commit that this layer does not perform. If the human asked for a
   file on disk, say plainly that this step did not produce one.

## Never Hand Off a Live Authoring Session

`begin` returns the session's read capability exactly once. It is an opaque
bearer secret held in the calling context; the broker stores only its hash, so
it **cannot be recovered, re-issued, or looked up** — that is the point of a
capability, not a defect.

Consequences, which are not obvious until the work is already lost:

- **Do the whole session in one context.** Author, validate, request approval,
  await it, commit, and start the run from the same place that called `begin`.
- **Never delegate a step to a sub-agent or background agent.** The capability
  does not travel with the instruction, so the receiving context gets a session
  it can read nothing from, and the human's approval becomes unconsumable.
  Spawning a second agent to recover from the first loses it again.
- **Never re-request an approval to work around a lost capability.** The new
  request is just as unconsumable, and it spends the human's attention twice.
- If the capability is genuinely gone, say so plainly, state that the draft and
  any session-committed revision are unrecoverable because nothing was written
  to disk, and begin a fresh session. Do not imply the prior work can be
  restored.
- A broker restart ends every session for the same reason. Prefer finishing a
  session promptly over leaving a draft open across unrelated work.

Typing approval in chat has no authorization meaning. Never synthesize a grant
ID, continue from a stale revision, or use a commit grant for a later run.
Never submit a second request while the first request is pending; await the
same request. An imperative “target this and commit” instruction is not a
grant and must leave the draft inert.

## Draft Quality

Hard assertions should express meaningful state such as target existence,
property equality, or route equality. A page-changed observation is not a hard
business assertion. Keep secrets, raw UI text, screenshots, and source out of
the authoring payload.

When the user wants an app code change such as an `AutomationId`, label it a
separate testability recommendation. The restricted profile cannot create,
approve, or apply a source proposal.
