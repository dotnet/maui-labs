# Microsoft.Maui.DevFlow.Testing

> ⚠️ **Experimental preview** — APIs and flow contracts may change before a stable release.

`Microsoft.Maui.DevFlow.Testing` provides the framework-neutral implementation of DevFlow
Markdown workflow tests. It owns flow contracts, Markdown parsing and serialization, validation,
recording, selector actionability, and replay reports. It does not depend on the CLI, broker,
Inspector, MCP, a test framework, or app/device lifecycle orchestration.

## Install

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Testing" Version="0.1.0-preview.*" />
```

The package targets `net9.0` and references `Microsoft.Maui.DevFlow.Driver`. It can be consumed by
`net9.0`, `net10.0`, and compatible MAUI test hosts.

The public API is previewed with a committed compatibility baseline. See the
[DevFlow preview compatibility policy](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/compatibility.md) before updating a
public signature or versioned contract.

## Test-framework-neutral quick start

The package contains no xUnit, NUnit, MSTest, CLI, broker, or provider dependency. Put the
framework-neutral method below behind the assertion/attribute mechanism your test host already
uses:

```csharp
using Microsoft.Maui.DevFlow.Testing;

static void ValidateLoginFlow()
{
    var result = FlowMarkdown.Parse(File.ReadAllText("maui-tests/login.md"));
    if (!result.Ok)
        throw new InvalidOperationException(result.Error);

    var validation = MauiFlowValidator.Validate(result.Flow!);
    if (!validation.Ok)
        throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
}
```

For example, xUnit calls it from a `[Fact]`, NUnit calls it from a `[Test]`, and MSTest calls it
from a `[TestMethod]`. Each framework owns its own async lifecycle, fixture, reset, and assertion
policy; the package supplies the same contracts and runner to all of them.

## Use a flow contract

```csharp
using Microsoft.Maui.DevFlow.Testing;

var parsed = FlowMarkdown.Parse(File.ReadAllText("maui-tests/login.md"));
if (!parsed.Ok)
    throw new InvalidOperationException(parsed.Error);

var validation = FlowValidator.Validate(parsed.Flow!);
if (!validation.Ok)
    throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
```

Use `MauiFlowRunner` with a DevFlow `AgentClient` (or an `IMauiFlowDriver`) when a host has already
arranged app lifecycle, connection, reset, and test data. It is the canonical execution engine and
returns a bounded `MauiFlowRunReport`:

```csharp
var runner = new MauiFlowRunner(agent, new MauiFlowRunnerOptions
{
    RunId = "run_example",
    ArtifactRoot = "artifacts/devflow",
});

MauiFlowRunReport report = await runner.RunAsync(parsed.Flow!);
if (report.Outcome?.Status != MauiFlowRunOutcomes.Passed)
    throw new InvalidOperationException(report.Failure?.Code);
```

The report contains ordered lifecycle events, step timing, selector/candidate and actionability
facts, fenced mutation receipts, assertion disclosure state, typed failure classification, and
artifact references. Lists and JSON output are bounded (1 MB by default); every omission is
explicit. String values are disclosed only for safe scalars. Typed text and sensitive values are
represented by a redacted descriptor with type, length, and SHA-256 digest.

`FlowReplayer` remains a compatibility facade. It delegates to `MauiFlowRunner` and returns the
legacy `FlowReplayReport` shape with additive `report`, `reportPath`, and `reportDigest` fields.
This keeps existing CLI, MCP, and Inspector consumers compatible while allowing new hosts to use
the structured result directly.

## Replay safety, reset evidence, and business oracles

`MauiTestPlan` can declare a side-effect policy. The package defines data contracts and deterministic
admission only; it does **not** publish reset, device, backend, compensator, or oracle execution
interfaces.

| Policy | Admission rule |
|---|---|
| `none` | Expected and observed clean-state checkpoints must match. |
| `app-state-resettable` | Matching checkpoints plus successful app-state reset evidence with a matching app-state seed fingerprint. No backend proof is required, because no backend was changed. |
| `test-tenant-resettable` | Matching checkpoints plus successful app-state and backend/test-data reset evidence with matching seed fingerprints. |
| `compensated` | Matching checkpoints plus either matching successful reset evidence or a successful outcome for the plan's declared compensator. |
| `non-replayable` | No automatic replay, continuation, or repair validation. A caller can admit one human run only with `manualOneShotAuthorization: true`; it is never repair-eligible. |

Plans declare expected build, app-state seed, backend/test-data seed, route, window, modal, locale,
theme, orientation, display profile, and (when applicable) collection-item key. Hosts provide the
observed checkpoint in `MauiFlowRunContext`. A missing declared observation or a mismatch denies
admission before the runner sends a mutating command.

The public evaluator first runs `MauiTestPlanValidator.Validate`. An invalid plan cannot authorize
replay. Oracle declarations require stable IDs, required oracles must set `Independent = true`, and
observed oracle results count only when `Independent == true`.

```csharp
var decision = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest
{
    Plan = plan,
    Context = new MauiFlowRunContext
    {
        Intent = MauiFlowReplayIntents.OrdinaryReplay,
        Preconditions = new MauiFlowReplayPreconditions
        {
            Expected = expectedCheckpoint,
            Observed = observedCheckpoint,
        },
        Reset = hostReportedReset,
        BusinessOracles =
        [
            new MauiIndependentBusinessOracleResult
            {
                OracleId = "order-recorded",
                Succeeded = true,
                Independent = true,
            },
        ],
    },
});

if (!decision.OrdinaryReplayAllowed)
    throw new InvalidOperationException(string.Join("; ", decision.Reasons.Select(x => x.Code)));
```

An independent business oracle must be declared and succeed before a passing run or repair can be
marked verified. `MauiFlowRunReport` retains the reset result, expected/observed preconditions,
compensator outcome, oracle results, and `replayEligibility` reasons. These values are redacted and
bounded with the rest of the report.

Independent verification also requires every declared scenario's acceptance criteria and every
required acceptance criterion to be linked to a hard assertion in the executable flow. A required
criterion that names a business oracle must reference an oracle declared as both required and
independent; otherwise the plan is invalid. Use
`MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(request, flow)` for executable-flow coverage.
Coverage gaps in an otherwise valid plan do not block ordinary execution; they keep a passing
execution explicitly unverified. `outcome.status` records execution, while `verification.verified` records
independent proof; `outcome.verified` remains the compatibility mirror and must agree.

Legacy schema-1/schema-2 manual flows with no plan remain ordinary-replay compatible. Their report
explicitly records `sideEffectPolicy: "unspecified"` and `repairEligibility: false`; hosts should
surface that warning rather than treating the run as repair-verified.

## Stable step identity

`FlowStep.Seq` remains the ordered integer sequence and is the only identity used by APIs that
accept an integer `stepSequence`. `FlowStep.StepId` is an optional stable identity used by newer
reports and repair proposals. It must be unique, at most 128 characters, and contain only letters,
digits, `-`, `_`, `.`, or `:` with no surrounding whitespace. Purely numeric IDs are reserved for
legacy sequence lookup. Recorder-generated sequence-shaped IDs use the canonical `step-NNNN`
value for their own `Seq`; a different step cannot claim that alias.

Compatible readers preserve `StepId` when present and fall back to `Seq` for legacy flows. Repair
generation resolves legacy sequence input but emits the stable ID when the target step has one.
This prevents a user-authored `StepId="1"` from redirecting an integer `stepSequence: 1` request.

## Schemas and compatibility

The formal contracts are maintained with the DevFlow protocol specification:

- [Executable flow v2](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-v2.json)
- [Test plan v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-plan-v1.json)
- [Flow run report v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-run-report-v1.json)
- [Test execution manifest v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-execution-manifest-v1.json)
- [Flow triage v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-triage-v1.json)
- [Preview qualification report v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-preview-qualification-v1.json)
- [Artifact trust v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-artifact-trust-v1.json)
- [Repair proposal v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-repair-proposal-v1.json)
  and [repair outcome v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-flow-repair-outcome-v1.json)
- [XAML source proposal v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-xaml-source-proposal-v1.json)
- [C# source proposal v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-csharp-source-proposal-v1.json)
- [Restricted test-agent protocol v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-agent-protocol-v1.json)

Schema 1 and schema 2 executable flows remain readable. Schema 2 is current; use
`MauiFlowMigration.Preview` to inspect a schema-1-to-schema-2 normalization without writing a
file. The preview preserves unknown fields and never creates fingerprints, source anchors, IDs,
revisions, or live validation facts. Schema 3 is not implemented.

```csharp
var preview = MauiFlowMigration.Preview(parsed.Flow!);
if (preview.WriteRequired)
{
    // Show preview.Changes and preview.Warnings before any host writes a file.
    var normalized = preview.NormalizedFlow!;
}
```

`MauiTestPlan`, `MauiFlowRunReport`, `MauiFlowRepairProposal`, `MauiFlowRepairOutcome`, and
`MauiXamlSourceProposal`, and `MauiCSharpSourceProposal` are provider-neutral data contracts. Plans declare side-effect policy,
reset expectations, capability requirements, and approval metadata, but they do not expose a
public reset, launch, install, broker, or device lifecycle interface. Validate future required
semantics explicitly before a run:

```csharp
var requirements = MauiFlowRequirementValidator.Validate(plan.Requirements, availableCapabilities);
if (!requirements.IsValid)
    throw new InvalidOperationException(requirements.Errors[0].Message);
```

Executable flows use `MauiFlowJsonContext`; plans, reports, repair contracts, and migration
previews use `MauiTestingJsonContext`. Both are source-generated and compatible readers and
writers retain unknown extension fields to support future additive versions.

`MauiFlowFailureClassifier` is pure and deterministic: it maps observed terminal state,
precondition facts, and legacy `FailureKind` strings to one typed code/category/phase. It does not
retry mutations. `MauiFlowRepairEligibilityEvaluator` and
`MauiFlowRepairProposalGenerator` are also pure: they accept only a pre-dispatch
`locator-not-found` with every matching checkpoint/trust/side-effect/oracle/prior-resolution
fact, consume only the deterministic selector-health list, and produce a separate selector-only
patch with invariant proof. They do not query a device, call a model, activate a candidate, write
a flow, apply a patch, or weaken assertions/actions/values/order. Hosts own validation grants,
lifecycle reset, compare-and-swap persistence, verification, and rollback.

`MauiFlowTriageAnalyzer` combines the same classifier and repair policy with a redacted
`MauiTestExecutionManifest`. It reports evidence sufficiency, stable test/incident/occurrence
fingerprints, retryability, inert allowed next actions, and whether local reproduction is
required. Imported evidence always remains diagnostic-only and cannot be repair-eligible.
Execution-manifest fact objects such as `device`, `build`, or `lifecycle` are omitted when the
corresponding stage has no facts; early and preflight failures do not serialize empty claims.
Triage uses `failure.class` for canonical routing while retaining a more specific `failure.code`
for diagnostics. Incident identity includes stable platform, runtime-kind, and broad device-profile
facts, but excludes exact device identity, OS version, run ID, timestamps, and source revision.

`MauiXamlSourceEligibilityAnalyzer` is also pure and provider-neutral. It accepts supplied
source text, mapping, filesystem-safety, runtime-scope, and uniqueness facts and returns explicit
ineligibility codes plus an exact declaration identity/span. It reads no workspace files, calls no
device/provider/model, issues no source grant, and never writes source. A CLI/IDE local host owns
the separate AutomationId-only proposal, approval, atomic CAS, verification, and rollback
lifecycle. Source and flow-repair approval are intentionally not interchangeable.

`MauiCSharpSourceEligibilityAnalyzer` consumes the same source identity, safe-ID, and uniqueness
policy plus supplied Roslyn semantic facts. It accepts only a direct initializer or direct literal
assignment and reports explicit template/repeater/factory/dynamic/conditional/generated/linked
rejections. It never loads a workspace, applies a patch, or invokes a model. A host-owned Roslyn
adapter creates the exact forward/inverse proposal; the broker records only an IDE host's
pre-hash/post-hash/patch-digest acknowledgment and never writes C# source.

## Restricted test-agent protocol contracts

`MauiTestAgentRequestEnvelope`, target/correlation/provenance records, broker-owned approval
request/decision projections, mutation scope/grant requests, typed errors, patch records, and audit entries are provider-neutral public data
contracts. They are used by the CLI's optional `maui devflow mcp --profile test-agent` boundary;
they do not invoke providers, launch/reset devices, read workspace source, or apply a repair.

A host issues an opaque mutation grant only after explicit human approval. The broker validates
the exact target process, app build, optional host-attested seed/backend state, actor/provider, plan/flow revision/digest, allowed
typed action/selector/route/side-effect/value/count scope, expiration, nonce, and policy version
before dispatch. Read-only structural operations use a distinct read capability. The contracts
represent opaque IDs and safe digests only; they never require a consumer to retain prompts,
secrets, UI text, screenshots, raw logs, raw network content, or source.

`MauiTestingJsonContext` includes source-generated metadata for every test-agent protocol contract
and retains additive extension fields. See
[Restricted test-agent protocol](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/test-agent.md) for the tool inventory and
approval workflow.

## Selector health and fingerprints

`MauiElementFingerprint` is versioned, value-free evidence captured at recording and run
resolution points. It records app/build/platform and route/window/modal/locale/theme/orientation/
display context, managed and authoritative native identity, source-anchor state, topology,
collection scope/key, normalized bounds, observation time, and capability version. It does **not**
store rendered text, entered values, screenshots, or raw runtime object ids.

`MauiSelectorCandidateGenerator` uses fixed priority: unique app-owned AutomationId; stable item
key scoped to one collection; authoritative native identity; role/type under a stable ancestor;
current source anchor corroborated by topology; then explicit-locale exact text with an explicit
unique live-match proof. Runtime ids,
coordinates, type/index alone, screenshots, ambiguous matches, and unscoped virtualized rows are
rejected. Candidates are diagnostic evidence; exactly one committed flow selector still drives
normal replay, and ambiguity still fails.

A stable-item candidate becomes executable when it also carries the repeated child
`AutomationId`. `FlowSelector` stores this as `automationId`, `collectionScope`, and
`stableItemKey`; `FlowActionabilityEngine` first queries the repeated AutomationId and then filters
to the one app-supplied item identity. Apps provide that identity with the Agent.Core
`DevFlowTest.StableItemKey` attached property on the item-template root. Agent.Core bounds and
SHA-256 pseudonymizes the raw key before any selector evidence leaves the app process.

The displayed `deterministicRankScore` is a transparent rule score, **not a probability**.
Components use rule version `selector-ranker-v1`: app-owned identifier (0.45), scope (0.20),
managed/native agreement (0.12), source anchor (0.10), topology (0.08), and geometry (0.05),
minus localization, virtualization, stale-source, platform-divergence, and ambiguity penalties.
`calibration.state` remains `uncalibrated` until a future benchmark gate.

`MauiSelectorHealthAnalyzer` is pure and emits these stable IDs:

| ID | Meaning |
|---|---|
| `DFSH001` | Duplicate reachable actionable AutomationId |
| `DFSH002` | Recorded actionable target lacks a durable identity |
| `DFSH003` | Runtime-id or type/index selector |
| `DFSH004` | Localized/dynamic exact-text selector risk |
| `DFSH005` | Template, CollectionView, or virtualization/index risk |
| `DFSH006` | Missing, stale, or ambiguous source anchor |
| `DFSH007` | Managed/native automation identity divergence |
| `DFSH008` | Required-platform candidate missing or divergent |
| `DFSH009` | Action lacks a meaningful hard postcondition |
| `DFSH010` | Required plan criterion lacks hard-assertion coverage |
| `DFSH011` | Route/platform selector coverage summary |

Selector health never changes a committed selector, applies a repair, writes source, invokes a
model, or falls back to another candidate during replay.

## Imported artifact trust

`MauiArtifactTrustEvaluator` classifies imported `flow-run.json` reports and `.mauitrace` v1
evidence with three explicit states:

| State | Meaning |
|---|---|
| `untrusted` | Default for every import. It may create bounded diagnostics only. |
| `attested` | A trusted host supplied independently verified provenance facts matching its configured repository, workflow, commit, and digest policy. It is still diagnostic-only. |
| `locally-reproduced` | A new local run matched the current flow digest, app build/source and package fingerprints, target profile, and relevant failure code, step, and checkpoints. |

The public `MauiArtifactProvenanceSubject`, `MauiArtifactTrustPolicy`, and
`MauiArtifactVerifiedProvenanceFacts` contracts are provider-neutral. The package performs no
network calls or issuer verification: a host must verify facts first and pass them to the pure
evaluator. ZIP/report-internal hashes establish integrity only; embedded IDs, digests, or
provenance fields never upgrade trust by themselves.

Imported identities use the distinct `imported-artifact` namespace. Bind a qualifying local run
with `MauiLocalReproductionFacts` and `MauiLocalReproductionExpectation`; the resulting
`MauiLocalReproductionBinding` refers to the imported failure by a generated opaque key, never an
embedded run/flow/proposal ID. Future repair and source services must call
`MauiArtifactProposalPolicy.CanCreateProposal` (or the repair/source forwarding gates), which
fails closed unless the record is `locally-reproduced`.

The serialized `MauiLocalReproductionReport.FailureCorrespondence` separately states whether the
imported and new local failure code, class, step, and checkpoints were the same, different, absent,
or indeterminate. It is derived from evaluator reason and omission codes and cannot be upgraded by a
caller-supplied value. `same-failure` supports an ordinary developer worktree investigation even
when volatile package identity prevents a full trust-state match; it never satisfies
`MauiArtifactProposalPolicy` or issues broker repair authority.

The CLI's `maui devflow flow validate <flow.md>` command uses this same parser and validator
without connecting to or driving an app.

## Human-authored plan sidecars

The canonical executable artifact remains `maui-tests/<name>.md`. A local host may store its
non-executable human plan next to it as `maui-tests/<name>.maui-plan.json`, using
[test-plan-v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-test-plan-v1.json). A plan includes its
`planId`, revision, bound flow path/digest, goal, scenarios, assumptions, preconditions, reset and
side-effect policy, acceptance criteria, requirements, provenance, and reviews.

`MauiTestPlanValidator.Validate` and `ValidateJson` validate the package-owned contract without
performing I/O. Hosts must additionally check their workspace boundary, current flow digest,
revision/digest compare-and-swap values, symlinks/reparse points, and atomic write behavior. The
Testing package intentionally never writes a workspace file.

Use `MauiFlowValidator.Validate` for the public canonical flow validation name (the established
`FlowValidator` facade remains compatible). An authoring host can use
`MauiFlowAssertionVerifier.VerifyAsync` for optional current-state feedback. It applies the same
strict selector resolution and scalar comparison behavior as `MauiFlowRunner`; `pageChanged`
remains observation-only and is never verified as a hard assertion.

## Platform support and qualification boundary

The package's local-feed consumer project compiles the matrix below when the host and workload are
available. A successful compile means only that the `net9.0` package asset can be referenced; it
does not launch an app, simulator, or device and is not runtime QA.

| Consumer | Compile coverage | Runtime qualification status |
|---|---|---|
| .NET 9 / .NET 10 test host and CLI | Required local-feed consumer and CLI builds | Preview API compatibility coverage only |
| Android | Windows host/workload consumer compile | Engineering pilot; **not-qualified** without the required real-device first-attempt evidence |
| iOS | macOS host/workload consumer compile | Required all-platform gate; not yet qualified |
| Mac Catalyst | macOS host/workload consumer compile | Required all-platform gate; not yet qualified |
| Windows | Windows host consumer compile | Required all-platform gate; not yet qualified |
| macOS AppKit | macOS host/workload consumer compile | Experimental AppKit fixture; `backend=appkit` artifacts are separately reported and never substitute for Mac Catalyst |
| WPF and GTK | No Testing package runtime claim | Experimental; separately reported and never substitutes for Windows or another required gate |

All-platform completion requires Android, iOS, Mac Catalyst, and Windows to pass their declared
runtime, reset, oracle, privacy, repair/source-review, and artifact gates. The Android engineering
preview is not all-platform completion. The experimental AppKit host handoff is separately
documented in [platform flow QA](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/flow-qa.md); its artifacts explicitly state
`backend=appkit` and never qualify Mac Catalyst.

The package deliberately does not publish reset, install, launch, broker, or device orchestration
interfaces. Hosts own those operations and invoke the same flow runtime after the app is ready.

The repository's Android integration fixture is the first host implementation. It performs
build/install/reset/seed/forward/launch and checkpoint verification internally, then passes the
verified context to `MauiFlowRunner`; it does not add a second runner or expose lifecycle APIs from
this package. Its `Category=FlowPilot` integration selector executes the committed Tier-1 corpus
three times from clean state and emits a redacted artifact manifest without changing the first
attempt outcome during later diagnostics. See `src/DevFlow/README.md` for emulator prerequisites,
workflow dispatch, artifact interpretation, and the explicit local commands.

## Preview qualification gates

`MauiPreviewQualificationGateEvaluator` is a pure, provider-neutral accounting component for the
Android engineering preview. `MauiPreviewQualificationCorpusRunner` validates
`tests/DevFlow/InspectorCorpus`, evaluates its static selector/repair/source-policy fixtures, and
deterministically creates at least 300 generated no-repair evaluations. Generated samples retain
`source: "generated"` and `realDevice: false`; they are never treated as independent physical or
device-backed executions.

The versioned output is [preview qualification v1](https://raw.githubusercontent.com/dotnet/maui-labs/main/docs/DevFlow/spec/schemas/maui-preview-qualification-v1.json).
It records fingerprints, profiles, review/flag state, thresholds, sample counts, Wilson 95%
intervals, ECE/Brier buckets when a probability-like confidence exists, report/trace/diagnosis
sizes, host p50/p95 measurements, device-overhead absence, exclusions, and artifact hashes.
`MauiPreviewQualificationArtifactManifestReader` consumes only the manifest's bounded metadata:
it never follows report paths, extracts ZIPs, trusts embedded provenance, or upgrades an emulator
to a real device.

The evaluator returns `not-qualified` for missing evidence rather than assuming success. Android
qualification requires approved plan/rubber-duck/independent review records, complete report and
first-attempt evidence, zero security/privacy escapes, 95% conservative repair precision, zero
false heals over 300 no-repair cases, 99% selector stability, calibrated ECE at or below 0.05
before probability-like display, and 100 clean real-device first attempts per Tier-1 flow. It has
no model provider, telemetry egress, automatic repair/source apply, or required-PR-gate behavior.
