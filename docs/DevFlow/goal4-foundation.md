# Goal 4: bounded upstream foundation

Experimental, off by default, **not qualified** on any platform.

This is an incremental adaptation of the testing/evidence invariants developed
on `morning4coffe-dev/maui-labs` at `fb2db33e`. It is not a wholesale import of
the old testing-platform, Inspector Workbench or Mobile Canvas stack.

## Immutable plan and execution evidence

`PreviewFlowPlan` snapshots the existing schema-1 flow using source-generated
JSON metadata. Its flow digest covers the snapshot, while its plan digest also
binds the plan ID/revision and exact agent/process/build/seed/checkpoint.
Changing either the original object or a returned copy cannot change the plan.
A plan is inert and is not an approval.

The internal `PreviewFlowRunner` is disabled by default and has no production
CLI, MCP or Inspector caller. Its host must atomically consume a current,
single-use native-human run grant, enforce unique live AutomationIds, and own
reset/lifecycle/cleanup. This increment provides no grant issuer or device
adapter and does not make the existing full MCP automation profile restricted.

Each runner instance permits one attempt only. A lost response is unknown
completion, never an automatic retry. UI success and independent business-oracle
success are distinct; cleanup cannot rewrite the primary outcome. Evidence
contains digests and bounded outcomes, not UI values, source paths or raw logs.
Every result remains diagnostic-only and confers no repair or source authority.

The initial scope excludes fill, property/environment changes, repeated items,
destructive/non-replayable operations, selector fallback and automatic repair.
Repeated items require a future composite AutomationId + collection scope +
stable item key contract, not an index or text fallback.

## Review and ownership

The native host owner must provide and validate a real approval issuer and exact
target binding before live execution is exposed. The app owner supplies stable
identities, reset/seed and an independent business oracle. The platform operator
owns fresh local reproduction; imported CI evidence never authorizes it.
Physical iOS is unavailable pending a signed-device harness. Simulator evidence
cannot substitute for physical-device qualification.

Host unit tests use in-memory fakes only. They establish contract behavior, not
device functionality, safe business replay, native human approval, or qualification.
The existing legacy flow API is unchanged. No new shipping package is introduced.

## Restricted MCP increment

`maui devflow mcp --profile test-agent` requires
`DEVFLOW_PREVIEW_AGENT_AUTHORING=true`; the `agent-authoring` kill switch wins.
The default remains `full` with its existing behavior.

This initial upstream profile exposes **only** `maui_test_author` (`begin`)
and `maui_test_validate`. Both validate supplied JSON offline and return inert
plan digests. No agent discovery or app access occurs. Target fields describe
intent, not observations. No session is persisted. Missing/unknown JSON fields,
duplicate properties, unsafe selectors and unsupported actions fail closed.

There is no approval-request, approval issuer, commit, run, repair, source,
file, network-body, CDP, device or generic action tool in this profile. Native
approval is explicitly unavailable. Do not substitute the full profile or the
fork's CLI approval command to bypass this boundary. Full live authoring and
native approval remain separate, unimplemented upstream increments.

## CI evidence and local reproduction

The optional `maui-devflow-ci-triage` skill is distributed by the tooling plugin
and bundled in the CLI. Explicit `maui devflow skills install` includes it;
`maui devflow init` does not. Its read-only PowerShell issue resolver is
forward-ported from fork `fb2db33e` without importing the privileged publisher.
Offline fixture tests cover both production and disjoint nonqualified demo
envelopes. Producer/consumer integration remains a later CI increment.

`PreviewFailureCorrespondence` compares supplied identities and preserves
indeterminate when a source fingerprint or other required fact is missing.
Matching facts are not attestation, qualification, a broker reproduction record
or a grant. The skill stops at an owner-bound handoff; it does not recommend
unavailable fork execution/repair commands.

## Diagnostic qualification tooling

`maui devflow qualification assess <input.json>` is opt-in through
`DEVFLOW_PREVIEW_QUALIFICATION=true` and disabled by the `qualification` kill
switch. It reads at most 1 MiB, never contacts an app, and returns JSON.
Exit 1 means disabled/invalid input; exit 2 means **not-qualified**, including
when supplied numeric claims satisfy all metric gates. There is no qualifying
exit 0 in this initial diagnostic-only increment.

The deterministic corpus is `tests/DevFlow/Goal4Foundation`. It checks the
fork policy's 95% Wilson precision lower bound, 100-evaluation minima, zero
false heals across 300 no-repair cases, 90% classification, 99% selector and
per-flow first-attempt stability, ECE <= 0.05 and host p95 <= 250ms.
It does not certify provenance, reviews, privacy testing, first-attempt integrity,
device overhead or platform execution. Caller-supplied claims are never attestation.
