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
