# DevFlow protocol spec

This directory contains the canonical DevFlow protocol contract used by the MAUI implementation in this repository.

- `openapi.yaml` defines the versioned HTTP surface under `/api/v1/*` and is the canonical OpenAPI document, including logical storage root discovery and sandboxed file management. The current shared implementation advertises only the `appData` root.
- `broker-workflow-runs-v1.yaml` defines the separate local broker workflow-run surface under
  `/api/workflow-runs/*`. It is intentionally not part of the in-app agent OpenAPI document.
- `asyncapi.yaml` defines the streaming channels under `/ws/v1/*`
- `schemas/` contains the shared payload models
- `examples/` contains representative request and response payloads, including platform job listing and run requests

These spec files are intended to stay framework-agnostic so the same DevFlow contract can be implemented across MAUI and other UI stacks.

Do not commit a generated JSON copy of the OpenAPI document. If a consumer needs JSON, generate it from `openapi.yaml` as part of that workflow so there is only one source of truth.

The DevFlow unit tests parse `openapi.yaml` with OpenAPI tooling and validate YAML/JSON syntax plus `$ref` targets across this directory.

## Testing contracts

The public `Microsoft.Maui.DevFlow.Testing` package uses these provider-neutral contracts:

### Testing contract index

Every JSON document below has a stable `$id`; the `v1` name is not a promise that a newly required
semantic can be introduced without a new version or explicit capability negotiation. Status is
intentionally about the contract, not device qualification.

| Contract | Canonical document and stable ID | Status |
|---|---|---|
| Broker workflow operations | [broker-workflow-runs-v1.yaml](broker-workflow-runs-v1.yaml) (OpenAPI 3.1, `info.version: 1.0.0`) | Preview local-broker contract; explicit target and capability token required |
| Broker workflow run | [broker-workflow-run-v1.json](schemas/broker-workflow-run-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/broker-workflow-run-v1.json` | Preview contract |
| Test plan | [maui-test-plan-v1.json](schemas/maui-test-plan-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-plan-v1.json` | Preview contract; plan data never grants lifecycle authority |
| Flow run report | [maui-flow-run-report-v1.json](schemas/maui-flow-run-report-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-run-report-v1.json` | Preview contract; bounded/redacted diagnostics |
| Test execution manifest | [maui-test-execution-manifest-v1.json](schemas/maui-test-execution-manifest-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-execution-manifest-v1.json` | Preview contract; provider-neutral host/build/artifact/device/lifecycle facts |
| Flow triage | [maui-flow-triage-v1.json](schemas/maui-flow-triage-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-triage-v1.json` | Preview contract; deterministic safe diagnosis and inert next actions |
| Local reproduction | [maui-local-reproduction-v1.json](schemas/maui-local-reproduction-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-local-reproduction-v1.json` | Preview contract; bounded imported-to-local match decision with no repair authority |
| Artifact trust | [maui-artifact-trust-v1.json](schemas/maui-artifact-trust-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-artifact-trust-v1.json` | Preview contract; imports remain diagnostic-only until local reproduction |
| Broker artifact trust | [broker-artifact-trust-v1.json](schemas/broker-artifact-trust-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/broker-artifact-trust-v1.json` | Preview contract; bounded, capability-gated broker projection |
| Selector repair proposal | [maui-flow-repair-proposal-v1.json](schemas/maui-flow-repair-proposal-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-repair-proposal-v1.json` | Preview contract; proposal only, never auto-apply |
| Selector repair outcome | [maui-flow-repair-outcome-v1.json](schemas/maui-flow-repair-outcome-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-repair-outcome-v1.json` | Preview contract; human approval/verification lifecycle |
| XAML source proposal | [maui-xaml-source-proposal-v1.json](schemas/maui-xaml-source-proposal-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-xaml-source-proposal-v1.json` | Preview contract; explicit local-host apply only |
| C# source proposal | [maui-csharp-source-proposal-v1.json](schemas/maui-csharp-source-proposal-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-csharp-source-proposal-v1.json` | Preview contract; IDE-mediated apply only |
| Restricted test-agent | [maui-test-agent-protocol-v1.json](schemas/maui-test-agent-protocol-v1.json) — `https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-agent-protocol-v1.json` | Preview contract; human-issued grants and no generic automation authority |

- [Executable MAUI flow v2](schemas/maui-flow-v2.json) — the authoritative payload in a
  Markdown `json maui-test` block.
- [Test plan v1](schemas/maui-test-plan-v1.json) — non-executable goals, safety constraints,
  reset requirements, side-effect policy, expected clean-state preconditions, compensator
  declarations, independent business-oracle declarations, acceptance criteria, and approval
  metadata.
- [Flow run report v1](schemas/maui-flow-run-report-v1.json) — bounded run, target, reset,
  expected/observed preconditions, side-effect admission decision, compensation/oracle evidence,
  outcome, ordered actionability/command/assertion step evidence, typed failure, and artifact
  facts. Failure evidence links back through additive `manifest.flowRun` metadata in `.mauitrace`
  v1; it never adds a ZIP entry or changes the evidence allow-list.
- [Test execution manifest v1](schemas/maui-test-execution-manifest-v1.json) — provider-neutral,
  redacted host, build, artifact, device, lifecycle, ownership, and occurrence facts. Artifact
  references are relative to the artifact root; the contract has no raw-log, prompt, secret,
  machine-name, user-name, absolute-path, or device-serial fields.
- [Flow triage v1](schemas/maui-flow-triage-v1.json) — deterministic classification, evidence
  sufficiency, retryability, repair policy, local-reproduction requirement, inert allowed next
  actions, stable fingerprints, fixed-code explanation, and a safe execution-manifest projection.
  It never copies arbitrary exception, log, prompt, app, or device text into trusted output.
- [Local reproduction v1](schemas/maui-local-reproduction-v1.json) — the imported artifact digest
  and broker-minted opaque identity, local manifest/report digests, derived failure correspondence,
  failure and checkpoint fingerprints, exact-match reasons, and confined local artifact references.
  It explicitly records that no broker binding, proposal, approval, apply, validation, or rollback
  authority was persisted. Failure correspondence may guide ordinary developer worktree diagnosis
  but never upgrades artifact trust or authorizes broker repair.
- [Artifact trust v1](schemas/maui-artifact-trust-v1.json) — provider-neutral imported-artifact
  identity namespace, integrity-only facts, provenance policy/verification outcomes, safe
  projection, and local-reproduction binding. It defines `untrusted`, `attested`, and
  `locally-reproduced`; only the final state may pass a future proposal policy gate.
- [Broker workflow run v1](schemas/broker-workflow-run-v1.json) — broker lifecycle, explicit
  agent-instance targeting, idempotency, and per-run capability-token contracts.
- [Broker artifact trust v1](schemas/broker-artifact-trust-v1.json) — bounded import,
  capability-gated status/safe-projection/local-reproduction routes with no raw-content endpoint.
- [Workflow command ledger](schemas/workflow-command-ledger.json) — agent-instance-bound begin/end
  control, contiguous command envelopes, bounded duplicate receipts, and unknown-completion fencing.
- [Repair proposal v1](schemas/maui-flow-repair-proposal-v1.json) and
  [repair outcome v1](schemas/maui-flow-repair-outcome-v1.json) — reviewable selector-repair
  evidence and immutable outcomes.
- [XAML source proposal v1](schemas/maui-xaml-source-proposal-v1.json) — a distinct,
  local-host-only proposal for one static literal `AutomationId` addition/replacement. Source
  approval and source grants never approve a flow repair; flow selector follow-up remains a
  separate reviewed proposal.
- [C# source proposal v1](schemas/maui-csharp-source-proposal-v1.json) — a distinct,
  Roslyn-proven proposal for one direct object-initializer or literal-assignment `AutomationId`
  change. It is advisory only: the broker never writes C# source; a native IDE host applies and
  acknowledges the exact forward or rollback patch hashes.
- [Restricted test-agent protocol v1](schemas/maui-test-agent-protocol-v1.json) — provider-neutral
  request envelope, exact target/process identity, revision/run correlation, provenance,
  read-capability and opaque human-issued mutation-grant binding, typed errors, and bounded audit
  fields. It is used only by `maui devflow mcp --profile test-agent`; it does not add provider,
  source, device-lifecycle, repair-apply, or generic automation authority.

Schema 2 is the current executable flow contract. Schema 1 remains readable by the package and
can be preview-normalized to schema 2 without writing a file or inventing fingerprints, source
anchors, IDs, revisions, or live validation facts. Schema 3 is intentionally not defined yet.
Compatible readers and writers preserve unknown extension fields. Plans, reports, and repair
documents are data contracts only: a plan never authorizes reset or device lifecycle execution.

Flow schema 2 includes an optional stable `stepId`. Current recorders assign and preserve it;
legacy flows continue to identify steps by `seq`. Report, selector-health, repair, and triage
references prefer `stepId` and retain the sequence fallback. Fingerprint rule
`maui-flow-fingerprints-v1` defines:

- `testIdentityFingerprint` for stable test and step identity;
- `incidentFingerprint` for the recurring failure identity, excluding run ID, timestamps, source
  revision, app-build occurrence facts, and report digest; and
- `occurrenceFingerprint` for one run/commit/report occurrence.

Reordering steps that retain stable `stepId` values does not change incident identity. Imported
evidence is always diagnostic-only in triage: it reports `repairEligible: false` and requires a
fresh matching local reproduction before the existing repair policy may be evaluated as eligible.

Required capabilities and semantics are declared by a plan rather than changing flow schema 2.
Hosts must reject an unsupported required semantic before any mutation is attempted.

### Restricted test-agent authorization

The restricted MCP profile requires an explicit `agentId` and `agentInstanceId` for every
effectful request. Mutation grants are opaque, short-lived, and single-use (or atomically bounded
for approved exploration) and bind actor/provider/channel, app build and seed state, plan/flow
revision/digest, action/selector/route/side-effect/value/count limits, expiration, nonce, and
policy version. UI text, logs, network content, Markdown, screenshots, and imported artifacts are
untrusted data; none may widen a grant or policy. The audit contract retains only bounded IDs and
digests. See [test-agent.md](../test-agent.md).

### Imported artifacts

Foreign reports and `.mauitrace` v1 bundles are untrusted data. Their embedded IDs and provenance
are inert metadata; internal hashes provide integrity only. The pure Testing evaluator performs no
remote attestation lookup. A trusted host may supply independently verified facts matching its
configured repository/workflow/commit/digest policy to reach `attested`, but that state grants no
execution, replay, repair, or source authority. A separate, newly executed local run must match
the current flow/app/target/failure facts to reach `locally-reproduced`. Broker imports are
capability-token gated, bounded, memory-only safe projections; raw content has no broad endpoint.

### Side-effect admission

`sideEffectPolicy` is one of `none`, `app-state-resettable`, `test-tenant-resettable`,
`compensated`, or `non-replayable`. The broker and Testing runtime use the same pure admission
decision before a mutation lease or device mutation:

- `none` requires matching declared/observed preconditions.
- `app-state-resettable` additionally requires successful app-state reset evidence and a matching
  app-state seed fingerprint. It deliberately asks for no backend proof, because the flow changed
  no backend for such proof to be about. Use it when an in-app reset surface restores app state
  without restarting the process.
- `test-tenant-resettable` additionally requires successful app-state **and** backend/test-data
  reset evidence and matching fingerprints. Claim it only when a backend test tenant really is
  reset; an app-only reset cannot satisfy it.
- `compensated` additionally requires either that reset evidence or a successful result for the
  plan's declared compensator.
- `non-replayable` rejects automatic replay and repair validation. A distinct
  `manualOneShotAuthorization` context flag can admit one human run, never a repair-eligible run.

The context/checkpoint contract covers app build, app-state seed, backend/test-data seed, route,
window, modal, locale, theme, orientation, display profile, and applicable collection-item key.
Missing or mismatched declared evidence fails admission. A required independent business oracle
must succeed before a run or repair can be represented as verified. The broker workflow-run start
and snapshot contracts carry the additive `plan`, `context`, and `admission` fields; schema-2
manual flow requests remain supported and report `sideEffectPolicy: "unspecified"` with
`repairEligibility: false`.

## Extension discovery

Agents can expose app-specific diagnostics or automation under `/api/v1/ext/{namespace}/...`. Extension namespaces use reverse-domain notation such as `com.example.diagnostics`.

Extensions are discovered through `GET /api/v1/agent/capabilities`. The response includes an `extensions` object keyed by namespace. Each extension descriptor includes:

- `version`: semantic version for the extension descriptor contract
- `description`: human-readable summary
- `tools[]`: self-describing tool descriptors with `name`, `description`, `method`, `path`, optional JSON Schema `parameters`, optional JSON Schema `returns`, and optional behavior `annotations`

`GET /api/v1/agent/status` includes an `extensions` marker with `count` and `hash`. Clients can cache extension descriptors by hash and avoid fetching full capabilities when the marker has not changed.
