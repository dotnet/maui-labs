# DevFlow human-authored tests

> Experimental preview. A plan describes intent and safety; only a committed Markdown flow is
> executable.

For an agent-facing, ambiguity-aware conversation layer over this lifecycle, see
[conversational collaborative testing](conversational-testing.md). It does not
expand the restricted test-agent profile or replace human grants.

For release-like/uninstrumented builds and OS-owned UI, use the separate
[Appium black-box smoke lane](appium-smoke-testing.md). It is not a DevFlow flow executor and
cannot qualify or repair a semantic flow.

For repeatable Android, Windows, iOS Simulator/Mac Catalyst, and separately labeled experimental
AppKit host handoff commands, see [platform flow QA](flow-qa.md). Physical iOS remains
unavailable/pending until a signed-device harness exists; Simulator evidence never certifies it.
Apple Test Agent
status is explicitly reported as pending until a returned macOS capability and Tier-1 artifact set
is reviewed; it is never reported as a passing platform result from Windows or source-only checks.

Before opening the Inspector, set `DEVFLOW_PREVIEW_WORKBENCH=true` in the broker process
environment. Set `DEVFLOW_PREVIEW_AGENT_AUTHORING=true` for agent handoffs and
`DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT=true` for diagnostic imports, then restart the broker.
Open `maui devflow inspect`, confirm **Tests** is visible, then follow **Goal**, **Steps**, and
**Review**.

1. Enter the required Goal, then choose **Record steps**. This is the only required field before
   recording. Optional metadata is separated into collapsed identity, outcome, setup/safety, and
   review groups.
2. Stop recording to open **Review**. The draft remains available even when it cannot yet
   be saved as a managed test.
3. Review the compact recorded-step list. Select one step to rename, reorder, remove, or edit its
   selector. **Record more steps** appends another recording to the existing draft.
4. Add an expected result to the selected step: target exists, property equals, route equals, or
   observation-only page changed. Secret-shaped values are not prefilled or persisted.
5. Choose **Check test**, inspect **Review changes**, then explicitly choose **Save test**.
   Save never runs a test.

### Work with your coding agent

From **Goal**, select **Prepare a draft with your agent**. VS Code and GitHub Copilot Canvas send
the bounded request directly to the host agent; a standalone browser copies it for pasting into
agent chat. The agent can prepare a complete draft and expected results, then its request appears
under **Agent requests**. This preview can review or reject the request, but cannot grant trusted
approval. Browser or chat confirmation never authorizes a save or run.

After a failure, **Results & import** offers **Diagnose this failure with your agent**. The agent can explain
the bounded failure and prepare an inert control-update suggestion, but it cannot apply the
repair. Copying the prompt creates a ten-minute, run-bound handoff containing the exact failed run,
target, and read capabilities. The agent calls `maui_test_failure` directly; it does not create a
draft, search for a “latest” run, migrate a flow, or infer that omitted checkpoint facts are
mismatches. Only an explicitly eligible pre-dispatch missing-selector result may continue to one
inert selector proposal. **Repair** keeps review, validation, approval, and apply under human control. In
**Improve**, the equivalent agent prompt asks for a read-only quality review of fragile controls,
missing expected results, and incomplete coverage.

The five workflow tabs reveal only the current usable action. **Results & import** remains
available before a run so a bounded diagnostic result can be imported without pretending it was
executed locally. Missing prerequisites link back to
the tab that can satisfy them instead of showing disabled future controls. Recording/recovery,
additional outcome checks, run/evidence details, compatibility replay, diagnostic import, and
technical trace data stay collapsed until they become relevant or the user explicitly opens them.

The tab row also enforces prerequisites: Steps requires a Goal; Review requires recorded steps; Run
requires a saved test with an expected result; Agent requests requires request history; Repair
requires a failed local result; Improve requires a loaded flow; and Source requires a selected
source-mapped control. Disabled tabs explain their unlock condition and are skipped by keyboard tab
navigation. A terminal local run completes **Results & import**; importing a diagnostic result uses
the file action inside that tab and cannot unlock or bypass Goal, Steps, Review, or Run.

The plan sidecar is:

```text
maui-tests/<flow-base>.maui-plan.json
```

It uses [test-plan-v1](spec/schemas/maui-test-plan-v1.json), includes the canonical flow
filename/digest, and preserves additive unknown fields. Save/commit uses the loaded flow digest,
plan revision, and plan digest for optimistic concurrency. A stale revision requires explicit
overwrite confirmation.

All local writes are confined to the registered project’s top-level `maui-tests` directory, reject
symbolic links/reparse points, cap files at 1 MB, and stage both flow and plan before replacing the
committed bundle. Browser hosts download artifacts when a workspace cannot be resolved. VS Code and
Canvas may save the same bounded bundle through the host bridge; no host gets an arbitrary path.

Saving or committing never starts replay. Run/Trace and **Improve** are separate, read-only
diagnostic surfaces; they never rewrite the loaded flow, select a fallback, apply a repair, or
write application source.

Recording retains only successful semantic interactions that can be replayed. A tap on a plain
page or layout container with no interactive role or tap gesture is ignored rather than becoming
a fragile type/index step. Interactive controls and gesture-bearing containers remain recordable.

Run readiness separates admission blockers from verification limitations. A stale canonical
flow/plan digest binding must be reviewed and saved again before Run unlocks. A missing independent
business oracle is non-blocking for ordinary replay: the run may execute, but its result is reported
as not independently verified.

## Production command-line execution

`maui devflow flow run` executes a committed Markdown flow and its matching
`.maui-plan.json` sidecar through the canonical `MauiFlowRunner`. Each adapter validates its host,
artifact/runtime contract, exact target, and launch capability before install or launch:

| `--platform` | Status | Supported artifact and host |
|---|---|---|
| `android` | v1 | APK on one exact connected Android target; use `--device` when multiple are online |
| `windows` (`winui`) | v1 | Explicitly unpackaged official WinUI `.exe` on an active, connected, unlocked Windows desktop |
| `ios` (`ios-simulator`) | v1 | `ios-simulator` `.app` on macOS; `--device` is the exact Simulator UDID when multiple are available |
| `maccatalyst` (`mac-catalyst`) | v1 | `mac-catalyst` desktop `.app` launched directly on macOS |
| `macos` (`appkit`) | Experimental | Distinct `macos-appkit` desktop `.app`; never counts as Mac Catalyst |
| `wpf` | Experimental | Distinct unpackaged WPF `.exe`; never counts as official WinUI |

```powershell
maui devflow flow run maui-tests\checkout.md --project src\MyApp\MyApp.csproj --platform android --device emulator-5554 --output artifacts\devflow\checkout

maui devflow flow run maui-tests\checkout.md --project src\MyApp\MyApp.csproj --platform windows --framework net10.0-windows10.0.19041.0

maui devflow flow run maui-tests/checkout.md --project src/MyApp/MyApp.csproj --platform ios --device A1B2C3D4-E5F6-47A8-9B01-23456789ABCD

maui devflow flow run maui-tests/checkout.md --project src/MyApp/MyApp.csproj --platform maccatalyst
```

The output directory contains `execution-manifest.json`, `flow-run.json`, and
`report.junit.xml`. When the app build fails, it also contains `app-build.log`: a bounded, redacted
copy of the MSBuild output, referenced from both the report and the manifest as `app-build-log`.
The default cleanup terminates only the exact package/process launched by this
invocation. `--cleanup uninstall` removes an Android or iOS Simulator package only when this
invocation installed it; desktop adapters terminate their owned process but never install or remove
a package. AAB, physical iOS/IPA, MSIX, AppInstaller, packaged Windows, cloud farms, and automatic
pick-first device selection remain unsupported. Plans requiring reset or compensation fail closed
unless a matching allow-listed state evidence provider is registered; missing independent
business-oracle evidence produces `unverified`, never a certified pass.

A plan that declares a `checkpoint` is enforced after launch, not before it. A host that cannot
observe clean app state before the app exists records
`replayEligibility.preconditions.observationDeferredUntilLaunch: true`, and the replay is admitted
only provisionally: run verification, repair validation, repair eligibility, and downstream
continuation all stay closed until the real observation arrives. The run pays that debt in a
`verify-preconditions` stage that runs immediately after `validate-agent`, once the agent is bound
and the live route, window, modal, locale, theme, orientation, and display profile can actually be
read. A declaration the live app does not satisfy fails the run there. The deferral only excuses a
missing observation — a conflict between what the plan declared and what a state-evidence provider
supplied is decided before launch and stays decided.

Because that checkpoint is compared at every step, a `route`, `window`, or `modal` declared in the
plan is an invariant of the whole run rather than merely its entry state. Declare those fields only
where they hold for every step; a flow that deliberately navigates away will otherwise classify its
failures as `route-state-drift` rather than `locator-not-found`.

### Deterministic triage

`flow triage` reads only bounded schema-1 execution output and calls the shared
`MauiFlowTriageAnalyzer`. Supplied files are treated as imported evidence, so the result always
remains diagnostic-only and can never report `repairEligible: true`.

```powershell
maui devflow flow triage `
  --manifest artifacts\devflow\checkout\execution-manifest.json `
  --report artifacts\devflow\checkout\flow-run.json `
  --format json `
  --output artifacts\devflow\checkout\triage.json

maui devflow flow triage `
  --manifest artifacts\devflow\checkout\execution-manifest.json `
  --report artifacts\devflow\checkout\flow-run.json `
  --format markdown
```

JSON uses the versioned flow-triage contract. Markdown is generated only from that safe projection;
neither format copies logs, exception text, app text, prompts, secrets, absolute paths, or device
serials.

### App crash versus agent loss

A crash destroys the DevFlow agent channel, so from inside DevFlow a dead app and a wedged agent
look identical. After a run ends, the platform adapter reaches the same device over a second
transport and asks the platform what happened to the process this run launched. On Android that is
`adb shell pidof`, `adb shell dumpsys activity exit-info`, and the `crash` logcat buffer filtered to
the launched pid; on desktop it is the process handle the host already owns. The result is recorded
in `appProcess` on the run report.

DevFlow reports the failure class `app-crash`, which triages to the `app-regression` disposition,
**only** when both of the following hold:

- the process under test was observed to be gone, **and**
- the platform independently named an abnormal reason (`crash`, `crash-native`, `anr`) or held a
  crash record bound to that pid.

Everything weaker stays as it was. An agent that stopped answering, a process that is simply
missing, a non-zero exit code, `am force-stop` (`user-requested`), and `kill` (`signaled`) are each
insufficient, because none of them separates an application fault from a harness teardown, a device
reboot, or an operator kill. A disconnect with no crash evidence remains `agent-disconnected` and
stays `inconclusive` — DevFlow does not upgrade uncertainty into an accusation.

A proven crash also never displaces a refusal to run or a fail-closed verdict. An invalid flow is
still `flow-invalid`, a cancelled run is still `cancelled`, a timed-out run is still `timeout`, and
an unconfirmed mutation is still `unknown-completion`. Knowing the app died does not prove the
mutation completed, so the `unknown-completion` exit category is never relaxed. A failed owned
cleanup no longer competes for the crash's place either: it is recorded on the second axis
described in [Two outcomes: the run and the cleanup](#two-outcomes-the-run-and-the-cleanup).

Evidence must also be shown to belong to *this* run. Platform exit records outlive the run that
produced them and operating systems recycle process ids, so a record is used only when it matches
the launched pid **and** is no older than the run. That age is computed entirely on the device's own
clock, so no host clock is ever compared to a device clock. When the launched pid is unknown, the
device clock cannot be read, or the transport cannot answer, the probe records that it could not
observe rather than reporting an exit it did not see.

Crash text is bounded and redacted through the same report redactor as everything else: at most
twelve excerpt lines, no absolute paths — host or device-side — no tokens, no device serials.

### Two outcomes: the run and the cleanup

A `flow run` invocation answers two independent questions, and the artifacts keep them apart.

The first is semantic: what did the run conclude about the app under test? That answer lives in
`outcome`, `failure`, and `verification` on the flow-run report, and in `outcome.status`,
`outcome.verified`, and `outcome.unknownCompletion` on the execution manifest.

The second is operational: did the host manage to tear down what it owned? Owned platform teardown
and removal of the build output this invocation created are recorded as `secondaryFailures` — a
bounded, identifier-only list of `{ phase, code, class, retryable }` mirrored on both artifacts —
plus the matching `cleanup` and `artifact-cleanup` lifecycle stages and a
`secondary-infrastructure-failure` event. Both cleanup phases can fail in the same run, and both are
listed.

```json
{ "outcome": { "status": "passed", "verified": true },
  "failure": null,
  "secondaryFailures": [
    { "phase": "cleanup", "code": "cleanup-exception", "class": "infrastructure", "retryable": true }
  ],
  "exitCategory": "infrastructure-failure" }
```

`exitCategory` composes the two. A cleanup failure can only make the command fail, never pass:

| Primary run verdict | Owned cleanup failed | Command `exitCategory` |
| --- | --- | --- |
| `pass` | yes | `infrastructure-failure` |
| `unverified` | yes | `infrastructure-failure` |
| `test-failure` | yes | `test-failure` |
| `unknown-completion` | yes | `unknown-completion` |
| `invalid-configuration`, `unsupported`, `infrastructure-failure` | yes | unchanged |

The last three rows are the point of the rule. Relabelling a real `test-failure` as infrastructure
would invite an automatic retry of a genuine regression, and relabelling `unknown-completion` or a
refusal would turn a fail-closed verdict into a retryable environment problem. In those rows the
cleanup failure stays fully visible through `secondaryFailures`, the message, the lifecycle stages,
and the JUnit cleanup case — it just does not get to rewrite what the run concluded.
`secondaryFailures` and the lifecycle stages are the authoritative record; the matching run events
are convenience and, like every other event, can be dropped when a very long run is trimmed to fit
the report size limit.

Nothing about cleanup ever enables an automatic action. Triage derives `retryable` from the primary
failure only, and a repair proposed from a run whose teardown did not complete is refused with
`owned-cleanup-incomplete`, because the target state the replay would start from is not the state
the evidence describes. An artifact imported from another host carries the same refusal as
`ownedCleanupIncomplete`, which is deliberately three-valued: `true` refuses, `false` is the
producer stating there was nothing to report, and absent — for an artifact written before this
contract, which records no cleanup facts at all — stays unknown rather than being read as a clean
teardown the artifact never claimed.

Owned cleanup is also the only kind of infrastructure failure that never displaces the run's own
verdict, and the two are distinguishable in the artifact. A later, non-cleanup stage of the
invocation can still fail — a post-run oracle fault, or a cancellation after the replay reached a
terminal state — and there the report *is* restated, because leaving it asserting a result the
command refuses to stand behind would publish an unprovable claim. When that happens the displaced
verdict is preserved as a `primaryExecutionOutcome` extension object, alongside a
`primary-outcome-restated` event:

```json
{ "outcome": { "status": "unknown-completion", "verified": false },
  "failure": { "class": "unknown-completion", "phase": "post-run-verification" },
  "primaryExecutionOutcome": {
    "displacedBy": "restatement",
    "exitCategory": "pass", "status": "passed", "verified": true
  },
  "secondaryFailures": [] }
```

So the two axes read differently on purpose. A non-empty `secondaryFailures` means the run's verdict
in `outcome` is its own and cleanup failed around it. A `primaryExecutionOutcome` means the verdict
in `outcome` is a restatement, and names what it replaced. A restatement never happens for cleanup,
so a cleanup-failed run carries no `primaryExecutionOutcome`.

`displacedBy` is what tells the two apart, and it is required precisely because
`secondaryFailures` cannot: producers before this contract wrote `primaryExecutionOutcome` to hold
the verdict a cleanup failure had overwritten, and wrote no `secondaryFailures` array at all — which
a reader is required to treat exactly like an empty one. `displacedBy: "restatement"` means the
object came from this producer and `outcome` is a restatement over a *non-cleanup* failure; its
absence means the older, cleanup meaning. The `primary-outcome-restated` event says the same thing
in prose, but events are the first thing dropped when a long run is trimmed to the report size
limit, so it is not a substitute. The object itself survives: the redactor allowlists it down to its
identifier-only members, and `secondaryFailures` is bounded rather than trimmed.

An artifact imported from another host is read for cleanup facts through `secondaryFailures` only.
The legacy signal — `primaryExecutionOutcome` with no `displacedBy` — is deliberately not honoured
there: it dates from before cleanup was recorded separately, and reading it would make a
pre-contract artifact refuse reproductions that the same artifact never asked to refuse.

A restatement is also refused outright where it could only destroy evidence. `pass`, `unverified`,
and `unsupported` have no honest restatement — a report cannot be made to claim a pass it never
observed, and `unsupported` has no term in the run-report contract — so the report is left exactly
as the run recorded it instead of being rewritten to an infrastructure error that would hide the
symptom. For `pass` and `unverified` the command then adopts the report's own verdict instead of the
one it was about to claim: the disagreement is real, and it is never resolved in the direction that
turns a recorded failure into a green build.

JUnit represents both axes rather than collapsing them. The `maui.devflow/flow` case keeps the run's
real result — `<failure>` for a test failure, `<skipped>` for `unverified`, nothing for a pass — and
each secondary failure adds one `maui.devflow.cleanup` case with an `<error>`. The suite therefore
still goes red, `failures` still counts only real assertion failures, and the
`devflow.exitCategory`, `devflow.primaryExitCategory`, and `devflow.secondaryFailures` properties
carry the composition for anyone gating on it.

> Runs produced before this contract wrote `primaryExecutionOutcome` for a cleanup failure too,
> because cleanup overwrote the primary verdict. Current runs never write it for cleanup — that is
> what `secondaryFailures` is for — and readers keep accepting the older meaning so existing
> artifacts stay readable.

> **Compatibility, in the other direction.** Before this contract, a passing run whose cleanup failed
> was published as `outcome.status: "infrastructure-error"`. It is now published as
> `outcome.status: "passed"` with a non-empty `secondaryFailures` and
> `exitCategory: "infrastructure-failure"`. The schema version is unchanged because the shape is
> purely additive, but the *meaning* of `outcome.status` alone is not: a consumer that gates on
> `outcome.status == "passed"` and has not been taught about `secondaryFailures` will read one of
> these runs as green. Any external dashboard, pinned CI branch, or harness that consumes
> `flow-run.json` must gate on `exitCategory`, or on `outcome.status` **and** `secondaryFailures`
> together. The process exit code was already the authoritative verdict and is unaffected.

### Expected evidence

A flow can declare which artifacts a run is expected to produce. Declarations are optional and
additive, so flows that predate the feature stay valid and produce byte-identical reports.

```json
{
  "schema": 2,
  "name": "checkout",
  "expectedEvidence": [
    { "id": "oracle", "kind": "business-oracle", "reference": "order-persisted" }
  ],
  "steps": [
    {
      "seq": 1,
      "action": "tap",
      "expectedEvidence": [
        { "kind": "visual-tree", "note": "reviewers need the tree if this step regresses" }
      ]
    }
  ]
}
```

Kinds are `screenshot`, `visual-tree`, `logs`, `failure-evidence`, `device-recording`, `run-report`, and
`business-oracle` (which requires `reference`, the oracle id). The run report gains an
`expectedEvidence` block recording one three-state check per declaration: `satisfied`,
`unsatisfied`, or `not-applicable`.

**What this verifies:** that the declared artifact category exists for this run. That is all.

**What this explicitly does not verify:**

- It is **not** a golden image and **not** a screenshot diff. No artifact content is compared
  against any baseline. A screenshot of a completely broken screen satisfies a `screenshot`
  expectation.
- It normally **does not cause evidence to be captured.** Satisfaction is measured against what
  the configured capture produced. `device-recording` is the explicit exception: it requests an
  MP4 of the surrounding device for the full run, is listed as an exact device change during
  Inspector/test-agent review, and cannot run without that digest-bound human approval. Reports
  carry only a safe run-relative artifact name, backed by the broker-owned run artifact directory.
  Inspector also retains a bounded private copy and requires the matching run capability to
  download it. The default broker stores run artifacts under
  `~/.mauidevflow/recordings/workflow-runs/` and prunes broker-owned run directories to the same
  bounded terminal-run retention window.
- It **never changes the run outcome, the failure class, or the exit category.** The block is
  reviewer information beside the verdict, not a second verdict.
- `screenshot`, `visual-tree`, `logs`, and `failure-evidence` are failure-scoped, because DevFlow
  collects them only when a run fails. On a passing run they record `not-applicable` rather than a
  false miss.

A step's legacy `screenshot` field is read as a step-scoped `screenshot` expectation, so the field
is no longer inert. Prefer `expectedEvidence` in new flows. A pre-existing flow that already sets
`screenshot` therefore starts emitting an `expectedEvidence` block where it emitted none before;
no committed flow in this repository uses the field, but a golden report pinned elsewhere will
change the first time one adopts it.

When the report has to be trimmed to fit its size limit, `declared` keeps the pre-trim count and
`allSatisfied` is forced to `false`, because the dropped checks are unknown rather than passing. The
trim is recorded as an `expected-evidence-checks` omission.

### Independent business oracles: how a run becomes verified

A clean replay is not a verified test. `flow run` reports `exitCategory: "unverified"` when the
steps all passed but nothing outside the app's own UI confirmed that the business result actually
happened:

```json
{ "ok": false, "exitCategory": "unverified",
  "message": "The execution passed, but independent verification requirements were not satisfied: independent-oracle-absent, required-scenario-uncovered. (Flow replay passed.)" }
```

This is deliberate. The flow drives the app through the DevFlow agent and asserts on what the UI
reports about itself, so a screenshot, a label, a toast, a spinner that stops, or a navigation to a
confirmation page are all **self-attestation**: an app that renders "Saved" without saving anything
satisfies every one of them. An independent business oracle is evidence gathered through a
different channel than the one the flow drove — an API query, a database row, a server-side audit
record, or, for a purely local app, a durable artefact the app committed and no page reads back.

Three things must line up, and all three are refused if you only supply some of them:

1. **A required, independent oracle** in the plan's `independentBusinessOracles`, whose
   `evidenceKind` matches a registered state-evidence provider. Without one you get
   `independent-oracle-absent`; if the provider evaluates it as false you get
   `independent-oracle-failed`.
2. **An acceptance criterion** that names that oracle in `businessOracleId`.
3. **A scenario** whose `acceptanceCriterionIds` are all covered by flow steps that carry the same
   `acceptanceCriterionIds` *and* a hard assertion (`"verify": true`). An uncovered scenario is
   `required-scenario-uncovered`.

`android-app-storage` is the built-in provider. It reads a file from the app's private Android
storage over `adb shell run-as`, outside the agent channel, after the run and inside the bounded
post-run evaluation window. The declared `reference` is a relative path under `files`, `cache`,
`databases`, `shared_prefs`, or `no_backup`; `expect.contains` entries must all be present in the
file and `expect.absent` entries must all be missing. Predicates are single-line, unrecognised keys
under `expect` are refused rather than ignored, and reports never echo the file's contents, only the
index of the predicate that failed.

Two enforced preconditions make it sound, and both come from `flow run` rather than the provider:
the run must be a Debug build, so `run-as` can reach app storage at all; and the Android adapter
refuses a device that already has the package installed (`android-preexisting-app-unsafe`), so app
storage is necessarily empty when the run starts and anything read afterwards was written by this
run.

The worked example lives in `samples/DevFlow.Sample/maui-tests/verified-add-todo.md` and its
sidecar. The app writes a `todo-ledger.jsonl` from its domain layer, which no page reads, and the
plan declares:

```json
"acceptanceCriteria": [
  {
    "criterionId": "todo-committed",
    "description": "The added todo is committed to the app's durable ledger, not merely rendered in the list.",
    "required": true,
    "businessOracleId": "todo-ledger-record"
  }
],
"independentBusinessOracles": [
  {
    "oracleId": "todo-ledger-record",
    "description": "Read the app's private todo ledger over adb and confirm it holds the exact record for the todo the flow added, and no removal.",
    "required": true,
    "independent": true,
    "evidenceKind": "android-app-storage",
    "reference": "files/todo-ledger.jsonl",
    "expect": {
      "contains": ["{\"event\":\"todo-added\",\"id\":\"todo-0001\",\"title\":\"Ledger verified item\",\"completed\":false}"],
      "absent": ["{\"event\":\"todo-removed\""]
    }
  }
]
```

The matching flow step carries both halves of the coverage contract:

```json
{
  "seq": 2,
  "action": "tap",
  "args": { "selector": { "automationId": "AddButton" } },
  "acceptanceCriterionIds": ["todo-committed"],
  "asserts": [
    { "kind": "propEquals", "selector": { "automationId": "CountLabel" },
      "name": "Text", "expected": "4 items, 0 completed", "verify": true }
  ]
}
```

A satisfied run reports `exitCategory: "pass"`, `outcome.verified: true`, and JUnit
`skipped="0"`. Point `expect.contains` at a record the app never writes and the same UI assertions
still pass, but the run returns `unverified` with `independent-oracle-failed` — that is the gate
working, not a bug.

Be honest about scope. `android-app-storage` is independent of the UI and of the automation
channel, not of the app process: it proves the app committed the record, not that a server accepted
it, and it asserts what the file holds when it is read rather than tracking each write. For an app
with a backend, query the backend instead and register a provider for it. The provider interface is
`IFlowStateEvidenceProvider`; a provider supplies post-run evidence bound to the exact run, device,
build, flow, and time window, and can never make a run verified on its own — the plan's coverage
requirements still have to be met. Only one provider may claim a given run, so a plan cannot yet mix
`android-app-storage` with an oracle of another kind.

### Local reproduction handoff

`flow reproduce` imports one `flow-run.json` or `.mauitrace` through
`ArtifactTrustImportService`, then invokes the same production `FlowExecutionCoordinator` used by
`flow run`. It does not contain a second runner.

```powershell
maui devflow flow reproduce maui-tests\checkout.md `
  --plan maui-tests\checkout.maui-plan.json `
  --project src\MyApp\MyApp.csproj `
  --platform android `
  --device emulator-5554 `
  --import downloaded\flow-run.json `
  --output artifacts\devflow\checkout-reproduction
```

The platform, framework, configuration, device, cleanup, agent-wait, and failure-evidence options
match `flow run`. `--output` is required and must be new or empty. The directory receives the normal
`execution-manifest.json`, `flow-run.json`, and `report.junit.xml`, plus the bounded
`local-reproduction.json`. The latter contains only the imported digest and fresh opaque identity,
local manifest/report digests, match reason codes, missing-fact codes, failure/step/checkpoint
fingerprints, and relative local artifact references.

Matching is fail-closed. A stale flow/plan binding, unsupported import, flow/app build/app source/
package/platform/device-profile/failure/checkpoint mismatch, missing comparison fact, infrastructure
failure, unknown completion, unsupported target, invalid configuration, or a missing independent
oracle for a selector-repair handoff cannot establish a match. Missing facts are reported as
insufficient; they are not guessed to be mismatches. Current app-source identity is emitted only
for a clean Git project source tree with a verifiable commit. A dirty or unverifiable project tree
deliberately omits that fact. `.mauitrace` v1 is accepted for safe diagnostics, but it normally cannot establish
an exact match because it does not retain the flow, source, and checkpoint facts required by the
evaluator.

#### Signed package identity is per-occurrence

A signed Android APK carries a fresh `packageDigest` for every build, so two occurrences of the
same commit never agree on it, and `appBuildFingerprint` is derived from that digest and cannot
agree either. When the import and the local run agree on flow, plan, app source, platform, and
device profile, the evaluator therefore sets those two mismatches aside and asks a narrower
question: do the two occurrences carry the same *payload*? `normalizedPayloadDigest` publishes a
signing-insensitive digest of the deployed package to make that question answerable, and the
reproduction reports one of three refusals:

| Reason code | Meaning |
|---|---|
| `normalized-payload-identity-unavailable` | One or both sides published no normalized payload digest, so the question cannot be asked. |
| `normalized-payload-identity-differs` | Both sides published one and they differ. |
| `normalized-payload-identity-unproven` | Both sides published the same digest, but a normalized payload digest is not yet established as a cross-occurrence identity on this platform. |

All three are blocking. The third is the honest state of the art, and it has been re-measured since
the agent session identity became build-scoped: two `flow run` invocations of one flow, run back to
back on one Android device against one clean commit with no edit in between, still produced two
distinct normalized payload digests, so agreement has never been observed and cannot be treated as
proof of sameness. The instability is a property of .NET Android packaging rather than of `flow
run`'s build isolation: three consecutive no-op `dotnet build` invocations of the same project at its
natural output path — no DevFlow host project, no redirected `OutputPath`, a fixed session id —
produced three distinct embedded-assembly packages. Two contributors were measured. Every repack
rewrites the ZIP local-header DOS timestamps and the Unix `UT` extra fields of the
`lib/<abi>/lib_*.dll.so` entries, so two packages whose entries are all byte-identical and
identically ordered still differ; this one moves `packageDigest` only, because the normalized digest
covers entry names, uncompressed lengths and content hashes and excludes timestamps by construction.
The contributor that moves the normalized digest is the second: 354 Kotlin metadata entries
(`commonMain/default/linkdata/**.knm`) appear or disappear depending on whether the referenced
Android class libraries re-run `_ResolveLibraryProjectImports`, and those entries are real payload
that cannot be normalized away. An earlier claim that two ordinary `dotnet build` invocations were
byte-identical across all 879 non-signature entries is retracted: it compared the 24 MB FastDev
package, which does not contain the app assemblies at all, rather than the 95 MB embedded-assembly
package `flow run` deploys. The digest ships as a diagnostic fact only; it does not rescue a
`packageDigest` mismatch today.

The command stops immediately after evaluation. It cannot create, approve, apply, validate, or
rollback a selector or source proposal. Even an exact CLI match is diagnostic-only: its
CLI-to-broker binding and capability are memory-only and cannot unlock repair. Continue in this
order:

1. Open the Inspector with the committed test and original diagnostic:

```powershell
maui devflow inspect --test maui-tests\checkout.md --trace downloaded\flow-run.json
```

2. Import the original diagnostic through the Workbench **Trace** path.
3. Choose **Reproduce locally** to prepare the broker-owned run check. Approve it only through a
   trusted VS Code or Copilot Canvas native host when `nativeApproval` is available; otherwise keep
   it inert and do not promise that it can run.
4. Verify that the broker-owned reproduction matches the current flow, app, target, failure, and
   checkpoint facts.
5. Only then open **Repair** review.

`local-reproduction.json` therefore records `brokerBindingPersisted: false`,
`approvalGranted: false`, and `proposalCreated: false`; it is not broker or repair authority.

## Restricted AI test authoring

`maui devflow mcp --profile test-agent` can create broker-owned drafts through a restricted typed
protocol. It never receives broad app automation, SecureStorage, raw files/network/CDP/source
access, generic action invocation, or repair/source apply authority. Every effectful request names
the exact agent process and uses a human-issued grant bound to the target/build/seed and current
plan/flow revision/digest. The agent may request bounded exploration, but the trusted native-host
approval backend/client required to issue a usable grant is currently unavailable. Until it exists,
the agent can only produce inert drafts, diagnostics, and pending/rejected/expired requests. A
trusted VS Code or Copilot Canvas host can complete the explicit native confirmation; browser and
chat cannot. See [test-agent.md](test-agent.md).

## Selector health

In the Inspector, Improve starts with a single **Scan test** action only after a flow exists.
Live-tree input, filters, and coverage stay in optional expanders; findings are not shown before a
scan.

The Improve tab runs the deterministic `MauiSelectorHealthAnalyzer` against the loaded flow/plan,
optional bounded live tree, and optional retained run history. It groups stable findings by
severity, category, step, source, and platform. **Rescan** is explicit; a flow/plan edit marks the
previous result stale.

`MauiElementFingerprint` is value-free: it retains identity/context, source/topology, collection,
and normalized geometry facts, but never rendered text, entered values, or screenshots. Candidate
priority is fixed: unique app-owned AutomationId, scoped stable item key, authoritative native id,
role/type under a stable ancestor, current source plus topology, and locale-bound exact text.
Runtime IDs, coordinates, type/index alone, screenshot similarity, ambiguous matches, and
unscoped virtualized rows are rejected.

For repeated controls, keep the child `AutomationId` and bind an app-owned stable model ID on the
item-template root:

```csharp
border.SetBinding(
    Microsoft.Maui.DevFlow.Agent.Core.DevFlowTest.StableItemKeyProperty,
    nameof(TodoItem.Id));
```

DevFlow propagates that identity to the realized child controls and records the executable
composite `AutomationId + collectionScope + stableItemKey`. This avoids visible text and
type/index ordering while still identifying one repeated item after reset or virtualization.
The raw app item ID is SHA-256 pseudonymized inside the in-app agent before it reaches a flow,
report, Inspector, or MCP response. The item ID must remain stable for the reset seed declared by
the test.

Candidate rank is a transparent deterministic rule score, not a probability. Calibration is
always `uncalibrated` in this preview. The stable diagnostic IDs are `DFSH001` through `DFSH011`:
duplicate/missing IDs, fragile selector forms, localization, template/virtualization, source
anchor, native divergence, platform parity, assertion gaps, acceptance-criterion coverage, and
route/platform coverage. No model or LLM ranks candidates. See the Testing package README for the
full ID table and score weights.

Synthetic baseline, mutation, and explicit no-repair fixtures live under
[`tests/DevFlow/InspectorCorpus/`](../../tests/DevFlow/InspectorCorpus/). They require no emulator;
at least one quarter are product-regression/no-repair cases so diagnostic work cannot be mistaken
for automatic healing.

## Imported run reports and evidence

Imported `flow-run.json` and `.mauitrace` v1 files are diagnostics, not executable test inputs.
The broker assigns each accepted import a fresh opaque `imported-artifact` identity, retains only a
bounded redacted projection in memory, and returns a per-artifact capability token. It never
replays the file, writes it to the workspace, or appends repair history. Raw imported bytes have no
download route.

Every import starts `untrusted`. A trusted host may mark it `attested` only after independently
verifying caller-supplied provenance facts against configured repository, workflow, commit, and
digest policy; the core does not call CI, GitHub, or issuer APIs. ZIP/report entry hashes prove
integrity only. Neither matching embedded IDs nor embedded provenance can upgrade trust.

`attested` remains diagnostic-only. An artifact becomes `locally-reproduced` only when a **new**
broker-owned local run matches the current flow digest, app build/source and package fingerprints,
platform/device profile, and relevant failure code, step, and checkpoints. Only that state can
pass the future repair/source proposal gate, and it still requires separate human approval. The
Inspector **Trace** imports only through the same bounded broker trust route and displays the
isolated projection in captured/read-only mode. It never preserves raw bytes, adopts embedded IDs,
or starts a replay. **Reproduce locally** opens a separate broker preflight and requires normal
human confirmation; it does not auto-start or bind trust. A browser-only host that cannot establish
current source fingerprints says so rather than fabricating a locally-reproduced state. Repair
candidate generation, source proposals, and apply remain unavailable. The host-side
`flow reproduce` command above can produce a separate review handoff, but its binding is not
inserted into this memory-only broker store.

## Human-approved selector repair

In the Inspector, Repair presents one lifecycle action at a time: check the latest failed run,
create an inert proposal, preview it, run validation, approve, then apply. Reasons, proof, and
policy details stay collapsed until requested.

Repair is deliberately narrower than diagnostics. Only a **pre-dispatch** primary
`locator-not-found` from a current local run, or imported evidence that has been
`locally-reproduced`, can enter repair classification. The recorded pre-step checkpoint must
match the current build, agent instance, seed/backend state, route, window, modal, locale, theme,
orientation, display profile, and collection key. Unknown completion, infrastructure, capability,
state, secret/data, actionability, ambiguity, and assertion failures are never repairable.

The deterministic selector-health shortlist is the only candidate source. A candidate must resolve
exactly once now, match its value-free semantic fingerprint, pass kind/risk gates, and retain its
full score/rationale/evidence with `uncalibrated` calibration. The existing active selector remains
unchanged until a separate proposal passes preview, a human issues a bounded validation grant, and
the platform lifecycle performs a hard reset plus an in-memory override replay. Validation requires
unchanged hard assertions and an independent business oracle; it never commits a flow.

An optional plan `repairPolicy` can narrow candidate kinds, permit locale-bound exact text only
with its explicit `localization` risk flag, and set shortlist/score-gap bounds. It cannot allow
runtime IDs, coordinates, type/index selectors, ambiguity, stale source, platform divergence, or
unscoped template/virtualized rows.

Apply requires a single-use human approval grant bound to the proposal, patch digest, base flow
path/digest/revision, target, policy, and expiry. The host performs a selector-only compare-and-swap
write, preserving Markdown prose where feasible and atomically updating the flow, plan sidecar, and
redacted history. It then requires three clean reset/oracle verification replays. Failure enters
`rollback-required`; rollback writes a new revision restoring the previous selector.

History is append-only and hash-linked at:

```text
maui-tests/.devflow/<flow-id>.repair-history.jsonl
```

It is bounded, path-confined, and stores only redacted IDs/digests, candidate kinds/scores/risk
codes, and outcomes—never prompts, source text, selectors containing user text, screenshots, or
secrets. Repair never changes assertions, expected values, actions, order, app XAML/C#, or a
`.mauitrace` format. There is no automatic apply and no model call in the repair core.

## Reviewed XAML and C# AutomationId source proposals

In the Inspector, Source first asks for a selected mapped control. **Check source** validates the
new AutomationId before **Create source proposal** appears; preview, approval, and apply/download
are shown sequentially. Technical source, build, host, and safety facts stay collapsed.

XAML source proposals are not flow repairs. The only preview operation is adding or replacing a
static literal `AutomationId` on one exact direct element declaration. The pure eligibility
evaluator requires a current unambiguous source map and matching hash, a project-contained
non-generated/non-linked `.xaml` path with no reparse-point escape, a safe test-ID grammar, and
unique project and current-live scope evidence.

It rejects bindings/resources/markup extensions, conditional/generated declarations,
DataTemplate/ControlTemplate/style/setter/resource scopes, BindableLayout/repeaters/virtualized
items, and native/WebView synthetic nodes. IDs may not be visible text, localized text, user data,
or dynamically derived values. Advisory analyzer IDs `DFXAML001` (missing static interactive ID),
`DFXAML002` (duplicate ID), and `DFXAML003` (template/style/repeater ID) are diagnostics only;
they never offer an automatic code fix.

The source proposal carries an exact diff and digests, but history stores only redacted,
hash-linked IDs/digests/state. A human-issued single-use source grant binds the proposal, patch,
file hash, project identity, affected flow references, host, and expiry. Only an explicit local
host action can compare-and-swap atomically; there is no force apply, commit, merge, C# edit, or
automatic selector update.

## Prototype-study research assessment (local only)

The Inspector Results card can export a bounded local prototype-study journal for evaluating the
novice-first Goal → Steps → Review → Run → Results journey. It records only an allow-listed
set of lifecycle events in browser `sessionStorage`: timestamps, safe provenance enums, booleans,
bounded counts/durations, and locally pseudonymized run/proposal references. It does not record
Goals, flow text, selectors, typed values, source, screenshots, prompts, reviewer identity, URLs,
device serials, payloads, or secrets.

This is **not telemetry**. It has no network egress or upload path, is scoped to the current tab,
is explicitly downloadable as a file-only JSON document, and can be explicitly cleared. The
summary reports authoring and review timing, selector durability counts, in-session replay
stability, failure classifications and diagnosis proxy, repair decisions, Improve usage, and
missing/insufficient measurements. It is intentionally marked `localSessionOnly: true`.

Prototype-study evidence complements, but never contributes to, digest-bound flow-run reports,
device artifacts, or `maui devflow flow qualify` accounting. In particular, it does not change
`maui-preview-qualification-v1` required semantics, turn a browser session into device evidence,
or make a platform qualified. Use qualification reports and returned device artifacts for those
claims; use the local journal only to assess Workbench authoring and human-involvement outcomes.

## Android engineering-preview qualification

`maui devflow flow qualify` is a **read-only** evidence-accounting command. It validates the
static Inspector corpus, generates deterministic no-repair variants, measures bounded host
operations, and combines optional redacted qualification evidence and flow-pilot manifests. It
does not start a broker, connect to an app, replay a flow, extract an artifact, invoke a model, or
apply a repair/source proposal.

```powershell
maui devflow flow qualify `
  --platform android `
  --corpus tests/DevFlow/InspectorCorpus `
  --output artifacts/devflow/qualification.json `
  --json
```

The report uses
[preview-qualification-v1](spec/schemas/maui-preview-qualification-v1.json). It fingerprints
corpus/package/tool/policy/build data rather than copying content, distinguishes `curated`,
`generated`, and `device-backed` samples, and includes denominators, exclusions, Wilson 95%
confidence intervals, repair precision/recall, calibration buckets, artifact references, review
status, and feature-flag state.

The static corpus contributes deterministic policy evidence only. Its generated cases are **not**
independent runs and never count as Android device evidence. The command intentionally returns
`not-qualified`, rather than `pass`, when real-device reports, recording/schema completeness,
review records, or Android overhead evidence are absent. Use `--fail-on-non-pass` only for an
explicit caller policy; the preview workflow is advisory and is not a required PR gate.

The Android gate requires all of the following:

- repair precision at least 95% using the lower bound of a two-sided 95% Wilson interval;
- zero false heals across at least 300 explicit no-repair evaluations;
- at least 99% selector stability in the declared Android scope;
- ECE at most 0.05 before any probability-like confidence is displayed;
- zero privacy/security escapes;
- valid corpus/report/recording/first-attempt/artifact evidence and approved plan,
  rubber-duck, and independent-review records;
- at least 100 **clean, real-device, first-attempt** executions for every declared Tier-1 flow,
  with at least 99% first-attempt stability.

Only first attempts are counted. Diagnostic reruns never replace them. Infrastructure outcomes are
excluded only when they carry a recorded deterministic exclusion reason; otherwise they count in
the denominator. Emulator/AVD artifacts are retained as useful pilot evidence but explicitly
remain `not-qualified` for the real-device gate.

`MauiPreviewFeatureFlagConfiguration` defines local opt-in flags for the Workbench, agent
authoring, repair proposals, source proposals, and trace import/export, plus
`DEVFLOW_PREVIEW_KILL_SWITCHES` for disabling those named surfaces. Defaults are off. The
configuration always forces auto-repair apply, auto-source apply, model-provider use, telemetry
egress, and required-PR-gate state to `false`; a qualification report records the effective
state.

After source apply, every affected official target TFM buildable on the host must build. The host
then rebuilds/relaunches, confirms source remap and runtime uniqueness, replays affected flows,
and verifies an independent oracle. iOS and Mac Catalyst verification unavailable on Windows is
recorded as `pending-external-qa`; it is never reported as passed. The optional experimental
`net10.0-macos` AppKit target can also be `pending-external-qa`, but it never satisfies or
replaces the required Mac Catalyst target. Verification failure enters `rollback-required`, and
rollback restores the exact prior bytes atomically. Any selector change after a successful runtime
remap is a new, separately approved flow-repair proposal.

### Roslyn-proven C#

The Source tab's **C#** mode is advisory and accepts only a current mapped, project-contained,
non-generated/non-linked `.cs` declaration whose Roslyn semantic model resolves to a supported
MAUI actionable control. The accepted syntax subset is a direct object initializer or a direct
`.AutomationId = "literal"` assignment on one resolvable local or member. It creates one minimal
forward patch and one inverse patch without formatting or rewriting a wider document.
When a running element has no C# source map, VS Code may supply its active saved C# declaration
(file, line, column, and short hash); the broker still confines it to the registered project,
recomputes the hash, and proves the selected runtime/semantic control type match.

It rejects DataTemplate/ControlTemplate/repeater/item-factory/BindableLayout declarations,
collections/lambdas/factories, virtualized items, generated/linked/outside-project documents,
conditional/preprocessor branches, reflection/dynamic construction, Shell/native/WebView nodes,
and computed/bound/localized/user-derived or duplicate IDs. `DFCS001`, `DFCS002`, and `DFCS003`
are advisory diagnostics only and never provide auto-apply.

The broker never writes C# source. After a human grant it records `awaiting-host-apply`; VS Code
opens a native diff, applies the exact patch through the IDE, and acknowledges pre/post hashes and
patch digest. Browser hosts download a patch only, Canvas reports C# apply unsupported, and MCP
cannot approve, apply, acknowledge, or source-write. Failed verification requires a separately
granted IDE-mediated inverse-patch acknowledgment and a new redacted history event.

## Required platform completion matrix

An Android engineering pilot is not an all-platform preview or release. The required completion
gate has four independent runtime lanes:

| Platform | Current status | Completion evidence |
|---|---|---|
| Android | Engineering pilot; **not-qualified** without real-device evidence | Clean real-device first attempts, the Tier-1 corpus, reset/oracle, selector/repair/source, privacy, report, and artifact gates |
| iOS | Not yet qualified | macOS-hosted Simulator/device-agent lifecycle and the same shared semantic contracts |
| Mac Catalyst | Not yet qualified | macOS lifecycle, shared corpus, source remap/replay/oracle, and artifact fingerprints |
| Windows | Implemented second-platform contract; local QA is currently **infrastructure-blocked** until run from an active desktop, and is **not qualified** | Debug Windows build, owned-process-only reset/seed/relaunch, process-scoped agent/checkpoint verification, shared Tier-1 repetitions, UIA/dialog/WebView capability contracts, report/artifact parity, and source/repair/trust validation |

AppKit, WPF, and GTK are experimental results reported separately. The AppKit fixture is an
advisory `macos --experimental` host lane with an explicit `backend=appkit` manifest; it never
waives a required Android, iOS, Mac Catalyst, or Windows gate. Package-consumer compilation proves only API/package
compatibility and never reports runtime QA. The macOS package-consumer publish gate is likewise compile-only; a returned macOS QA handoff
remains required before Apple runtime qualification. The checked-in iOS Simulator and Mac Catalyst
commands are documented in [platform flow QA](flow-qa.md) and
[Apple XCTest QA handoff](apple-xctest-spike.md).

The Appium project is an independent black-box smoke lane for release-like builds and OS-owned UI.
It is not the canonical `MauiFlowRunner` kernel and can neither repair a flow nor turn an Android,
Apple, or Windows qualification status into pass. See
[Appium black-box smoke tests](appium-smoke-testing.md).

## Preview controls, privacy, and compatibility

Preview feature flags are opt-in and default off. `DEVFLOW_PREVIEW_KILL_SWITCHES` can disable the
Workbench, agent authoring, repair proposals, source proposals, or trace import/export. No flag
can enable auto-repair apply, auto-source apply, model-provider use, telemetry egress, or a
required PR gate; those states are forcibly false and recorded in qualification evidence.

Reports, imported artifacts, repair/source history, and evidence are redacted, bounded, and
diagnostic-only until a separately authorized local workflow reaches its explicit trust and
approval gate. See [evidence privacy and artifact trust](evidence.md) and the
[preview compatibility policy](compatibility.md). Public API and schema changes remain preview
changes, but intentional binary, source, or semantic breaks require the `breaking-change` label,
migration notes, and a reviewed baseline update.
