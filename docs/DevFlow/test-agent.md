# Restricted DevFlow test-agent protocol

> **Experimental preview.** `test-agent` is a deliberately restricted MCP profile for drafting
> and running reviewable DevFlow tests. It is not a general-purpose app automation or source-editing
> interface.

For the conversational skill that guides this protocol without granting
authority, see [conversational collaborative testing](conversational-testing.md).

Start it with:

```text
maui devflow mcp --profile test-agent
```

The default `maui devflow mcp` profile remains `full` and is backward compatible.

## Tool inventory

The restricted profile exposes only:

- `maui_test_agents`, `maui_test_status`, and `maui_test_capabilities` for exact-target
  discovery;
- `maui_test_author`, `maui_test_action`, `maui_test_assertion`, and `maui_test_validate`;
- `maui_test_explore` for bounded, budget-enforced navigation steps;
- `maui_test_run`, `maui_test_trace`, `maui_test_failure`, and `maui_test_improvements`;
- `maui_test_patch` for **inert** proposal/preview/reject storage;
- `maui_test_layout_diagnostics` for a bounded, read-only structural layout scan of one exact
  target. It is **experimental**, and no other tool in this profile depends on it.

It does not expose SecureStorage or preference mutation, files, raw network detail or bodies, CDP
evaluation/source, generic invoke/extension actions, arbitrary property mutation, shell/process
access, evidence capture, source proposals, source apply, repair apply, or lease takeover.

### Read authority tiers

Two read tiers exist, and the dividing line is whether a tool reads broker-owned authoring session
state — not how much it returns.

| Tier | Tools | Requires |
|------|-------|----------|
| Pre-capability discovery | `maui_test_agents`, `maui_test_status`, `maui_test_capabilities`, `maui_test_layout_diagnostics` | An exact `agentId` + `agentInstanceId` |
| Session read | `maui_test_improvements`, `maui_test_validate`, `maui_test_trace`, `maui_test_failure`, and every mutating tool | A complete envelope with the session's read capability |

`maui_test_layout_diagnostics` is intentionally **pre-capability**. It reads only the live app's
structure, exactly as `maui_test_status` does, so requiring an envelope would force an authoring
draft to exist before a read-only structural scan — making a pure read create broker session state
as a side effect and blocking the conversational "look at the screen first, decide what to test
after" flow. `maui_test_improvements` requires an envelope because it analyses the session's draft
plan and flow.

What bounds the layout scan is its projection, not a capability: it runs with evidence off,
suppression matching off, and text privacy at its only accepted value (`none`), and the response
carries no source paths, control text or values, raw evidence, policy reasons, screenshots, logs,
network data, system evidence, authoring session state, or mutation authority. The response states
this tier in a `readAuthority` block so a caller never has to infer it.

## Explicit targets and envelopes

Every effectful request names both `agentId` and `agentInstanceId`; there is no most-recent,
port-only, or default-target fallback. Requests use `MauiTestAgentRequestEnvelope` v1:

```json
{
  "schema": 1,
  "requestId": "req_...",
  "idempotencyKey": "uuid-or-opaque-key",
  "target": { "agentId": "stable-id", "agentInstanceId": "process-instance" },
  "correlation": {
    "authoringSessionId": "author_...",
    "planId": "plan_...",
    "planRevision": 3,
    "planDigest": "sha256...",
    "flowId": "flow_...",
    "flowRevision": 4,
    "flowDigest": "sha256...",
    "runId": null
  },
  "provenance": {
    "actorKind": "agent",
    "actorId": "host-session-agent",
    "channel": "mcp",
    "provider": "host-owned"
  },
  "intent": "Verify the saved profile",
  "approvalGrantId": "grant_opaque_when_mutating",
  "deadlineMs": 30000,
  "policyVersion": "test-agent-policy-v1"
}
```

`intent` is a short user-visible audit description, not hidden reasoning. The broker returns
typed code/category/retryability errors and safe artifact references. It does not store prompts,
raw app text, screenshots, source, logs, or secret values.

## Human approval and grants

An agent may begin an authoring session, submit a plan or exploration request, and inspect
read-only structure using the session's read capability. It cannot issue a mutation grant.

A submitted `approval-request` is persisted by the broker and held against the exact running app
until it is decided or its window closes. `maui devflow approve --list` shows the user-visible
intent, target, actions, selectors, routes, side-effect classes, action/value limits, and
expiration. The default pending-review window is ten minutes; the issued grant has its own shorter
expiration. There is no Inspector inbox or editor panel for these requests in this layer — the CLI
below is the whole decision surface.

> **Current approval availability:** the only approval issuer in this layer is the
> `maui devflow approve` CLI documented below. It requires the owner-only approval token the
> broker writes to its local state file, so approval is unavailable whenever that token is absent.
> The standalone browser and chat remain read-only and non-authoritative; they cannot approve,
> narrow, or issue a usable grant. Editor-embedded approval clients are a later layer and are not
> present here, so nothing in this layer should be described as offering one.

### `maui devflow approve` — an issuer, not a boundary

```
maui devflow approve --list                       # show what the agent is asking for
maui devflow approve <approval-request-id>        # approve it (default grant lifetime 300s)
maui devflow approve <id> --grant-seconds 120     # narrower grant lifetime (1-900)
maui devflow approve <id> --reject --reason scope-too-broad
```

The command reads the owner-only native-host approval token from the local broker state file, then
performs the same two-step ceremony an IDE extension performs: mint a single-use, digest-bound
confirmation capability, then present it on the decision call. Decisions it makes are recorded on
the `host` approval channel with `decidedBy: "cli/maui-cli"`.

> **This command is operator convenience, not an authorization boundary.**
> A DevFlow MCP agent runs as the same OS user with the same file permissions, so it can read the
> same broker token and call the same routes. Nothing this command does proves that a human rather
> than a local agent process made the decision, and nothing it could do would prove that on a shared
> user account:
>
> - A one-time code derived from the approval digest would be theater — every input to that digest
>   is already in the agent's own session snapshot.
> - A TTY check is defeated by ConPTY, `node-pty`, or `AllocConsole`.
> - A non-interactive mode gated on an environment secret is defeated by reading the environment.
>
> The command's value is availability and reviewability: without it, a user with no IDE extension
> has no way at all to approve a commit or a run, and no record of who approved. It does not change
> what an agent that already controls the account can do. `--yes` skips the interactive prompt for
> exactly this reason: the prompt was never a protection, so hiding it behind a flag costs nothing.

The `decidedBy` field on an approval record and its audit entry names the issuing *surface*
(`workbench/inspector-server`, `cli/maui-cli`), never the operating-system user. It is **self-asserted
provenance, not attested evidence**: any holder of the broker's native-host approval token can send
any label, and on a shared user account that includes the agent. Read it as a way to recover intent
from an audit trail, not as proof of which surface decided. It is deliberately excluded from the
confirmation digest and participates in no authorization check.

A trusted native host lets a human reduce the requested scope and issues an opaque, short-lived
grant server-side only after revalidating the live app instance and current plan/flow revision. The
browser never receives the host-approval token, confirmation capability, or opaque grant. The agent
retrieves the grant through `maui_test_author status` using its read capability. Typing `approved`
in agent chat has no authorization meaning and never creates a grant.

For a new agent-authored test, send the complete inert plan, steps, and assertions in
`maui_test_author begin`. The flow has two human decisions: approve the reviewed commit, then
separately approve one run. When native approval is unavailable, authoring stops at the inert draft
or pending request. Draft-change and assertion requests remain inert records for an existing human
demonstration or recorded draft.

Request grants one stage at a time. Draft changes alter `flowDigest`, and commit advances the plan
and flow revisions, so future-stage grants requested against an earlier snapshot become stale by
design. Every request envelope also needs a positive bounded `deadlineMs`.
Selector scope entries use canonical `automationId:<id>`,
`scopedItem:<collection>:<itemKey>:<automationId>`, or `typeIndex:<type>:<index>` keys. The
protocol also accepts equivalent typed selector objects and normalizes them to those keys.

A `run` approval is always single-purpose and single-use: the broker forces `maxActionCount` to 1
and refuses a run scope that bundles any other action, so one human "approve this run" decision
authorizes exactly one dispatch. Cancelling a run needs its own approval; reading run status is
read-only and needs none.

For a plan with `sideEffectPolicy: non-replayable`, an approved `run` grant is the explicit one-shot
human authorization required for that single run. It does not permit a retry, repeated replay,
repair validation, or downstream continuation. Use `test-tenant-resettable` only when the host
actually provides and verifies the declared reset/seed contract.

The broker
binds it to:

- actor, provider, channel, exact agent process, app build, and seed/backend state only when a
  trusted live reset host attests those optional fingerprints;
- plan and flow IDs, revisions, and digests;
- allowed typed actions, durable selectors, routes, side-effect classes, action count, and value
  length limit;
- expiry, nonce, and policy version.

The broker consumes a single-use grant atomically before dispatch, or decrements a bounded
exploration grant. Changed instance/build/plan/flow, a changed attested seed/backend fingerprint,
a scope mismatch, an expired grant, or
a reused idempotency key fails before app dispatch. A lost response is reported as
`unknown-completion`; it is never retried automatically.

## Workflow

1. Use `maui_test_agents`, then copy an exact target into `maui_test_capabilities`.
2. Use `maui_test_author begin` with the complete proposed plan and flow. This is inert broker-owned
   authoring and does not mutate the app or source.
3. Use `maui_test_improvements` for bounded value-free selector discovery. It returns types,
   AutomationIds/native identities, uniqueness, and fragility, but never UI text, values, source,
   runtime IDs, bounds, or screenshots. Read-only discovery needs no mutation grant.
4. Present the complete draft, then submit one `commit` approval request.
5. The operator reviews the exact scope with `maui devflow approve --list` and decides with
   `maui devflow approve <id>`; browser and chat cannot substitute for that step.
6. Use `maui_test_author await-approval` only after the approval request has been presented to an
   approval host. Do **not** call `maui_test_author await-approval`, poll for an approval, or
   attempt a grant-dependent flow when no approval host is available; keep the draft inert instead.
7. Only after a bound host-issued commit grant may the agent commit, refresh status, and request a
   separate run approval.
8. Only after that distinct host-issued run grant may it start exactly one run and use its
   run capability for status, trace, failure classification, or cancellation.

When the committed flow contains `devicePreconditions` or `deviceSteps`, the run approval record
adds a bounded **Exact device changes** section. The native confirmation repeats every item, and
the dispatch must match the same committed flow digest. A changed coordinate, native selector,
permission, battery/network setting, orientation, or ordering therefore requires a new review.
Selector-repair validation does not inherit this authority and refuses flows with device mutations.

After each driven step, hard assertions use bounded settlement polling (up to roughly 2.25 seconds
with the default runner settings) so asynchronous MAUI layout, collection, and binding updates are
observed without adding arbitrary fixed sleeps.

When extending an existing draft instead of authoring it completely at `begin`, request only the
specific draft-change or assertion scope needed for that incremental edit. Those authoring scopes
are normalized to `authoring`, so plan vocabulary such as `non-replayable` cannot cause a corrective
reapproval loop.

`maui_test_patch` accepts proposal, preview, and reject only. `apply` and `approve` are rejected.
The current profile reports run pause/continue as unsupported rather than pretending to provide
them.

## Repair boundary

`maui_test_patch` is intentionally an **inert** review channel. It may retain a structured
selector-repair proposal, return its preview, or mark it rejected inside the bounded authoring
session. It cannot classify a live repair as eligible, mint a repair validation or approval grant,
apply a flow patch, write Markdown/history/source, run verification, or rollback. Requests named
`approve`, `apply`, and `rollback` fail closed with `patch-apply-forbidden`.

Moving a separately broker-owned repair proposal through validation, approval, apply, verification,
and rollback requires a human review ceremony that **this layer does not ship**. Agent-originated
proposals stay visibly agent-originated and inert. This boundary also means no model can weaken
assertions, change expected values/actions/order, or obtain app source-write authority through the
test-agent profile.

## XAML and C# source-proposal boundary

The restricted profile cannot analyze, create, preview, approve, apply, acknowledge, report,
verify, or roll back an XAML or C# source proposal. In particular, `maui_test_patch` cannot carry
a source operation or turn a source/flow proposal into another proposal type. **Source apply is
deferred: no surface in this layer applies a source change**, and a source approval would never
grant a selector repair, nor a flow-repair approval source writing. The broker never writes C#
source.

## Audit and privacy

The broker retains a bounded append-only journal of intent digest, policy decision, grant digest,
action/result digest, target, revision, and run IDs. Human approval and rejection entries also carry
`decidedBy`, the self-asserted bounded label of the issuing surface. Retention and capacity are
bounded. Raw
secrets, prompts, source, screenshots, UI text, logs, network content, and imported artifact
content are excluded. Imported data and UI/log/network observations are untrusted diagnostic input
and never become policy instructions or execution authority.

See [the protocol schema](spec/schemas/maui-test-agent-protocol-v1.json),
[human-authored testing](testing.md), and [the Testing package README](../../src/DevFlow/Microsoft.Maui.DevFlow.Testing/README.md).
