# Committed qualification baseline

`qualification.json` is a real `maui devflow flow qualify` report for the static corpus on
`--platform android`, unedited apart from a `generatedAt` pinned to the epoch so the file does not
churn. It is committed so CI can fail on a **regression** rather than only on a threshold breach.

## What it currently says

```
status: not-qualified
corpus: 58 curated (31 repair-positive, 30 of them curated-derived; 16 no-repair), 300 generated
falseHeals:             0/316  (independent 16) [curated 0/16, generated 0/300]
repairPrecision:        31/31  (independent  1) [curated 1/1, curated-derived 30/30]
repairRecall:           31/31  (independent  1) [curated 1/1, curated-derived 30/30]
classificationAccuracy: 42/45  (independent  8) [curated 12/15, curated-derived 30/30]
selectorStability:      0/0    (no device evidence)
flakeFirstAttemptStability: not measured (no device evidence)
```

**Read the independent count, not the denominator.** `31/31` is one curated seed plus 30
`adapted-from-case` restatements of it; `42/45` is 8 genuinely inferred classifications plus 37 where
the fixture handed the classifier the answer. `tests/DevFlow/InspectorCorpus/README.md` explains both
in full. Every count gate compares `independentEvaluations`, and every lower-bound gate reads
`independentConfidenceInterval` — the interval over the independent subset alone, because the pooled
interval narrows every time a clone is added without any new fact being observed.

**`not-qualified` is the correct answer, not a defect to be fixed.** The static corpus cannot
satisfy gates that require real-device evidence, an independent review record, or n≥100 *independent*
repair evaluations. Committing a passing baseline would have required either fabricating device
evidence or lowering a threshold; both are the failure mode this baseline exists to detect.

Gates that are `not-qualified` in the baseline and why:

| Gate | Reason |
| --- | --- |
| `independent-review` | No review record is attached to a static corpus run. |
| `required-evidence` | No run report, recording, first-attempt, or artifact manifest. |
| `repair-precision` | **1** independent evaluation, threshold is 100. |
| `classification-accuracy` | **8** independent (genuinely inferred) evaluations, threshold is 100. |
| `zero-false-heals` | **16** independent no-repair evaluations, threshold is 300. |
| `selector-stability` | No device evidence. |
| `android-device-overhead` | No device evidence. |
| `android-tier1-first-attempts` | No Tier-1 flow declared and no device runs. |
| `product-analyzer-coverage` | The corpus scores repair and false heals with harness rules, not the shipped analyzer. See below. |

**`abstention` has no threshold gate of its own.** It is published as `316/316` and reads like a
perfect score, but no *threshold* gate consumes it: `product-analyzer-coverage` reads its provenance
and requires its denominator to be non-zero, and the baseline diff floors its numerator, denominator,
independent count and provenance. Nothing fails if the *rate* degrades — a run that abstained half
as often would still pass every gate. It is a descriptive counter of how often the harness declined
to propose a repair when none was expected, scored by the same harness-local rules as `falseHeals`.
Do not quote it as a result.

## The largest gap: the corpus does not exercise the shipped analyzer

`MauiPreviewQualificationCorpusRunner.EvaluateFixture` **re-implements** the selector-health and
repair-eligibility rules against the fixture JSON. It borrows the diagnostic id constants from
`MauiSelectorHealthDiagnosticIds` but never calls `MauiSelectorHealthAnalyzer.Analyze`, which is the
entry point the product actually uses. `MatchesExpectations` then compares those harness rules
against `expect.diagnosticIds` values that were hand-authored beside them.

**Consequence: `falseHeals`, `repairPrecision`, `repairRecall` and `abstention` are self-consistency
checks, not product tests.** A false-heal rate of `0/316` says the harness agrees with its own
expectations. It says nothing about whether the shipped analyzer would have proposed that repair.
Growing the corpus makes such a number look more authoritative while measuring the same thing, so
the size of the denominator must not be read as confidence in the product.

This is stated in the report itself rather than only here:

- `corpus.exercisesShippedAnalyzer` is `false`.
- Every rate metric carries `exercises: { component, kind }`. `kind` is `harness-local-rules` for
  the four metrics above and `shipped-analyzer` for `classificationAccuracy`.
- The `product-analyzer-coverage` gate is `not-qualified` and lists exactly which metrics are
  harness-scored. It is evaluated both per-run and over an accumulated verdict, so
  `--accumulate --fail-on-non-pass` sees it too. **It has no unqualified pass.** Nothing in the
  report can watch code run, so every kind other than `harness-local-rules` — a compile-time literal
  in the evaluator — ultimately rests on a label in the submitted data. A passing result therefore
  always carries `provenance-self-reported` and names whose word it is taking; an empty reason list
  would read as verification. It also stays `not-qualified` while `repairPrecision`, `repairRecall`,
  `falseHeals` or `abstention` carry no evidence at all, because absence of the metrics this gate
  exists to disclose is not coverage of them.
- `classificationAccuracy` is labelled `shipped-analyzer` only when **every** judged sample — curated
  and device-backed alike — carries an `observedFailureClassProducer` stamp naming what called the
  classifier. Without it the label degrades to `unknown` and the gate fails. Device-backed rows are
  not exempt: a device is no better placed than a fixture to say which code produced a label, and
  exempting them would let one stamped fixture speak for ninety-nine unstamped device rows. An
  **all**-device judged subset is a different claim and takes a different path — it publishes
  `sample-supplied` without a stamp check, because `sample-supplied` asserts only *who observed*
  the sample, never *which code classified it*.
  **The stamp is not a forgery guard.** A `--results` file is deserialised verbatim, so an author who
  writes `"observedFailureClassProducer": "MauiFlowFailureClassifier.Classify"` into a JSON file gets
  the strong label. Against a determined author it is exactly as strong as the source-name inference
  it replaced; what it removes is the *accidental* claim, where a hand-written or partial sample
  earned the product's name merely for not being device-backed. This is why the coverage gate has no
  unqualified pass.
- A judged subset that **mixes** stamped static rows with rows a run submitted publishes `unknown`.
  "Any statically scored sample wins" would be conservative only while the static kind is the weak
  one; for `classificationAccuracy` it is the strongest kind in the model, so one corpus fixture
  could otherwise upgrade a subset of run-supplied rows. `unknown` rather than `sample-supplied`
  because that is the honest answer — no single component produced the subset — and because the
  accumulator refuses a declared `sample-supplied` whose judged sources are not all `device-backed`,
  and it refuses the **whole report**. Publishing `sample-supplied` here would make a run's own
  honest understatement delete its stability and device evidence along with the label. The published
  component reads `... + submitting-run` and the note says it was downgraded.
- The label describes the **judged** subset, which is not always the whole denominator. When samples
  are pooled in beyond the judged set, the note says how many, and which reader sees them: the count
  and lower-bound gates read only the independent subset, and the baseline diff compares the pooled
  numerator and denominator of every rate. `falseHeals` adds a clause the other four do not, because
  it is the only rate whose **pooled numerator** is compared against a threshold by a *gate* —
  `zero-false-heals` compares `numerator`, not `independentNumerator`. That gate checks the
  independent count first and returns `not-qualified` without ever reading the numerator, which is
  the state this baseline is in, so the clause states the condition rather than implying the `0/316`
  headline is currently enforced by a gate. No gate compares a pooled *denominator* against a
  threshold; `product-analyzer-coverage` does read the pooled denominators, but only as "is there
  any evidence here at all", which can only widen the set of metrics it inspects.
- `MauiPreviewQualificationAccumulator` merges `exercises` conjunctively — the merged kind is the
  **weakest** any contributor declared, ranked `unknown` < `harness-local-rules` < `sample-supplied`
  < `shipped-analyzer`, so pooling can never upgrade what the merged number measures and the result
  does not depend on the order the runs happened to sort in. A contributing run that counted samples
  without declaring anything makes the merged kind `unknown`.
- The accumulator's provenance check is **one-directional**, and only on the axis it can actually
  decide. It refuses a run declaring `sample-supplied` — "a device observed this" — over judged
  sources that are not `device-backed` (`accumulate-provenance-mismatch`). It does **not** police
  the harness-versus-shipped axis, because nothing in a `sourceCounts` block can establish which
  code scored a sample; that is what the coverage gate reports instead. Understatements are accepted
  deliberately: a rejected run never reaches the gates at all, so refusing an honest under-claim
  during a mixed-version rollout would delete evidence rather than fail on it.

`classificationAccuracy` is the exception, with a caveat. The observed label does come from
`MauiFlowFailureClassifier.Classify`, the same entry point `MauiFlowRunner`, `WorkflowRunCoordinator`
and `MauiFlowTriage` call at runtime — that part is not theatre. But the harness still chooses the
classifier's **input**: `BuildFailureFacts` derives a `LegacyFailureKind` using its own
`InferLegacyFailureKind`, and for most static cases `Classify` then resolves that through the
`FromLegacyFailureKind` constant table, which is closer to a rename than to a decision. Only the
cases that force precedence resolution (currently the route-drift case) exercise the classifier's
real logic. Read the 8 independent evaluations as "the classifier is reachable and its constant
mapping is correct", not as "the classifier was tested".

**Fixing this is worth more than any further corpus growth.** Two parts, with different costs:

- **Repair *eligibility* is closeable today.** `Classify` already returns `RepairEligible` from the
  same facts `EvaluateRepairFixture` builds, so the runner could defer to it instead of deciding
  eligibility itself. This is a runner change, not a format change.
- **Selector-health *diagnostics* need the format redesign.** Calling
  `MauiSelectorHealthAnalyzer.Analyze` requires building a `MauiSelectorHealthAnalysisInput` from a
  `MauiFlow` plus `LiveElements` and a plan, and the current fixtures are ad-hoc JSON shapes
  (`recordedRoute`/`observedRoute`, `androidCandidateKinds`, `hardAssertion`) with no flow
  structure. That is a corpus format change.

`Corpus_KeepsTheAnalyzerCoverageDisclosureHonestWhenTheRunnerChanges` ties the disclosure to the
code — it scans the `MauiPreviewQualification*.cs` sources for a real call to
`MauiSelectorHealthAnalyzer.Analyze` and asserts `corpus.exercisesShippedAnalyzer` equals whether
one exists. Equality catches both directions: wiring the analyzer up without flipping the flag
fails, and flipping the flag without wiring it up fails.

Its limits are real and stated rather than papered over. It only sees that file glob, so wiring the
analyzer in from a differently named file would leave it green; and it matches the type by name —
bare, namespace-qualified or alias-qualified — so an indirection through a delegate, a `using
static`, a type alias or reflection would too. Matching by name cuts the other way as well: a field
or property that merely *carries* the name is counted, which can only make the disclosure overstate
and fail loudly. Under an equality assert a
**false negative** is the dangerous direction, because it makes the tripwire agree with a `false`
declaration instead of tightening it — which is why it **parses rather than greps**. Deciding "is
this text a call or a mention" by blanking string literals with regexes turned out to be a losing
game: quote parity has to be exactly right across verbatim, raw and interpolated forms, and three
separate attempts each left a shape (`"…$"`, `$@"…\"`, a hole containing braces) that swallowed the
rest of the file and hid a real call. A `CSharpSyntaxTree` settles that class of question by
construction — comments and literal text are trivia and can never be invocations, while an
interpolation hole is syntax and is therefore still seen. Two things a parse can still be blind to
are refused rather than guessed at: a file that does not parse, and a file carrying an `#if`, whose
inactive branch is trivia with no diagnostic at all. Both fail the test loudly instead of counting
zero calls and quietly agreeing with the declaration.
`Tripwire_SeesEveryWiredCallInEveryFileItScans` injects a call at every
`private static` in each scanned file and asserts the **count** matches, not merely that one
survived; an existential assert stayed green while a scanner lost 48 of 49 sites.

## Statistical power of the generated share

The 300 generated mutants are drawn from `generatedBaseFixtures` originals — currently **16** — under
`generatedSeedCount` seeds, currently **1**. They are one deterministic resampling of the curated
no-repair cases, repeatable but not repeated, and they contribute **0** independent evaluations.
Both numbers are published in `corpus` so the generated denominator cannot be quoted as 300 trials.

## Regression diffing

```powershell
maui devflow flow qualify `
  --corpus tests/DevFlow/InspectorCorpus `
  --platform android `
  --baseline tests/DevFlow/InspectorCorpus/baselines/qualification.json
```

The comparison fails (nonzero exit) when:

- `repairPrecision`, `repairRecall`, `recordingValidity`, `selectorStability`,
  `classificationAccuracy`, or `flakeFirstAttemptStability` **rate** drops below the baseline rate;
- any of those metrics' **denominator** or **independentEvaluations** falls below the baseline;
- the `falseHeals` **numerator** rises above the baseline (any escape is a regression);
- the `falseHeals` **denominator** or **independentEvaluations** falls below the baseline — otherwise
  a clean sweep could be manufactured by evaluating fewer cases, or by relabelling the independent
  ones as derived; `abstention` is guarded the same way;
- any of `corpus.curatedCases`, `curatedRepairPositiveCases`, `curatedNoRepairCases`,
  `curatedClassificationLabeledCases`, or `generatedNoRepairCases` falls below the baseline;
- `corpus.curatedOriginalCases` (curated minus derived) falls below the baseline — `curatedDerivedCases`
  itself is deliberately *not* frozen, because losing disclosure is not a regression but growing
  clones while originals stay flat is;
- `corpus.undeclaredProjectionCollisions` or `corpus.undeclaredShapeCollisions` rises above the
  baseline;
- `corpus.securityCorpus.caseCount`, `corpus.securityCorpus.passedCount`, or
  `metrics.privacySecurityEscapes.testCount` falls below the baseline — the privacy-security gate
  reports `pass` on three cases as happily as on eighteen, so without these floors deleting fifteen
  of them changes no gate status and no rate;
- `abstention`'s **numerator** falls below the baseline — it counts *correct* abstentions, so
  unlike `falseHeals` it must not shrink;
- a metric that had evidence in the baseline has none in the current run;
- any gate's status gets **worse** — ranked `pass` better than `not-qualified` better than `fail` —
  or the gate has disappeared. Comparing only gates that were passing meant the transition that
  matters most here, `not-qualified -> fail`, produced no regression line: a flow that finally ran
  and *failed* looked the same to CI as a flow that still had not run;
- a gate that is **absent from the baseline** reports `fail`. Adding a gate is how this branch made
  metrics visible, so a new `pass` or `not-qualified` gate is not a regression — but iterating only
  the baseline's gate list meant a newly added gate could report a measured failure and produce no
  line at all;
- with `--accumulate`, any accumulated metric regresses against its per-run baseline counterpart —
  including `accumulated.falseHeals` and `accumulated.abstention`, which are compared as counts.

A metric with **no** baseline evidence (denominator 0) is not protected by the rate comparison — it
has nothing to fall below. `selectorStability`, `flakeFirstAttemptStability` and `recordingValidity`
are in that state today, so of the rate floors listed above only `repairPrecision`, `repairRecall`
and `classificationAccuracy` are actually load-bearing right now. That is a consequence of having
no device evidence at all, not a design choice, and it stops being true the moment a real run is
committed.

The merge is also bounded: at most 512 run files are read, and a larger directory reports
`accumulate-directory-too-large` rather than silently spending minutes in the quadratic reference
election.

Improvements never fail. `.github/workflows/ci-devflow.yml` runs this on every DevFlow PR.

`PreviewQualificationTests.Baseline_MatchesAFreshlyGeneratedReport` regenerates the report from the
corpus on disk and asserts that the committed `corpus` block, the committed `status`, and every
metric the diff gates on (`repairPrecision`, `repairRecall`, `falseHeals`, `abstention`,
`classificationAccuracy`, `classificationMatrix`, `selectorStability`, `recordingValidity`,
`privacySecurityEscapes`) match exactly. It deliberately does not compare `metrics.runtimeOverhead`,
`generatedAt`, or the assembly-derived fingerprints, which are machine dependent.

Be clear about what that test does and does not do: it catches a **hand-edited or stale** baseline.
It does **not** catch a corpus weakened and re-baselined in the same commit — regenerating the file
makes both artifacts consistent again. What catches that is the set of monotone floors above:
`corpus.*`, `independentEvaluations`, and the gate-status comparison. Those floors are only as wide
as the fields listed, and a field with no floor is unprotected: the privacy/security corpus was
exactly that gap until its counts were floored, and any *future* evidence surface added to the
report will be too until someone adds it to the list. Reviewing what a change removes is still a
human job.

## Regenerating

Only regenerate when a change is intended to move a number, and say so in the PR:

```powershell
dotnet build src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj
dotnet run --project src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj --framework net10.0 --no-build -- `
  devflow flow qualify --corpus tests/DevFlow/InspectorCorpus --platform android `
  --output tests/DevFlow/InspectorCorpus/baselines/qualification.json --no-json
node -e "const f='tests/DevFlow/InspectorCorpus/baselines/qualification.json';const fs=require('fs');const d=JSON.parse(fs.readFileSync(f,'utf8'));d.generatedAt='1970-01-01T00:00:00+00:00';fs.writeFileSync(f,JSON.stringify(d,null,2)+'\n')"
```

`generatedAt` is pinned to the epoch so the file does not churn. `metrics.runtimeOverhead` and the
assembly-derived fingerprints are machine dependent; neither the diff nor the freshness test reads
them.

## Accumulating across runs

The stability gate needs ≥100 clean first attempts per Tier-1 flow, and the flow-QA harness
deliberately caps `--repeat` at 20. `--accumulate <dir>` merges evidence across **separate** runs
instead:

```powershell
maui devflow flow qualify --platform android --accumulate artifacts/qualification-accumulation
```

Each invocation writes `run-<fingerprint>.json` into the directory and rewrites `accumulated.json`.

A run is **rejected** from the merge when its contract version, platform, policy version, corpus
fingerprint, or any gated threshold disagrees with the reference run; when its own thresholds differ
from the compiled policy defaults (`accumulate-threshold-not-policy-default`); when it carries JSON
the contract does not model (`accumulate-unmodelled-evidence`); when a rate metric's source counts
do not add up to its totals (`accumulate-incoherent-metric`); or when its static evidence disagrees
with the reference under a matching corpus fingerprint (`accumulate-static-evidence-mismatch`).

The reference run is elected by the **actual acceptance predicate**: the run that admits the most
others wins, ties broken by recency. Grouping on a subset of the compared fields is not enough —
three ordinary runs differing only in `--generated-no-repair` share contract, platform, policy and
corpus fingerprint yet reject one another on static evidence, so such a group could win a vote and
then admit only one of its own members. Picking the oldest file was worse still: one leftover run
from a superseded corpus rejected every current run and the merge reported a near-empty result with
exit 0. Losing a run is never harmless — a discarded run never reaches the gates, so a flow that
measured a real stability failure disappears instead of failing. Election by majority is not a
proof: enough forged runs would out-vote the genuine ones. Forgery is already outside what a
self-reported file can be checked for; a stale file is an accident that happens.

**Static evidence is counted once, not summed.** Every accumulated run must share a corpus
fingerprint — which covers the *contents* of every evaluated `.json` file under the corpus root,
line endings normalised, and excludes only `baselines/` (generated from the fingerprint, so hashing
it would never converge) and markdown. The manifest is forbidden from resolving a case into
`baselines/`, so nothing evaluated is outside the hash. So its curated,
curated-derived, and generated counts are re-reads of the same files. Only `device-backed` counts
are summed across runs; an unrecognised source name is refused outright
(`accumulate-unknown-sample-source`) rather than quietly treated as one or the other. Running the
same static corpus 100 times — under 100 different `--mutation-seed` values, which do produce 100
distinct evidence fingerprints — still yields `independentEvaluations` of 1 for `repairPrecision`.
Accumulation is for real device runs; it cannot manufacture trials out of re-reading files.

**The source-name check is a spell-checker, not an authenticity check.** It rejects `null`, `""`,
`"   "`, `"Device-Backed"`, `"device-backed "` and any label the contract does not define. It does
**not** reject a fabricated `sourceCounts` entry that spells `device-backed` correctly — a
hand-written run file claiming 400 device-backed no-repair evaluations is merged verbatim. Nothing
in a self-reported JSON file can prove that a number came from a device. What the merge can do is
refuse the shapes that are obviously not evidence, and publish enough structure that a reviewer can
see what is being claimed. Trust the run files exactly as much as you trust the job that wrote them.

**Clean first attempts do accumulate**, because they are the one thing here that is a fresh device
observation per run. `accumulation.firstAttemptFlows` sums `cleanFirstAttempts` and
`passedFirstAttempts` per `flowId` across accepted runs, and `accumulated-tier1-first-attempts`
gates them at the same per-flow threshold as the single-run gate. A flow counts as device-backed
only if *every* contributing run said so. This is what makes 5 jobs × `--repeat 20` reach the ≥100
threshold without raising the cap. No run has produced any first-attempt evidence yet, so the
committed baseline reports none.

Because that sum is the only place new evidence enters, it carries the tightest checks:

- a run repeating one `flowId` in its flow list, or claiming more passes than clean attempts, is
  refused (`accumulate-incoherent-flow-evidence`) — otherwise pasting one 20/20 entry five times
  inside a single file reaches the 100-attempt threshold from 20 real attempts;
- a flow claiming `realDeviceEvidence` in a run that names no real device is refused
  (`accumulate-unattributed-device-evidence`);
- each merged flow publishes `contributingRuns` and `contributingDevices`, so 100 attempts arriving
  as 5 runs on 5 devices is legible as such — and 100 arriving as 5 runs on 1 device is too. Read
  `contributingDevices` precisely: it is the number of distinct real devices declared by the runs
  that contributed to that flow, not a claim that the flow executed on each of them. One run
  declaring five profiles reports five, on twenty attempts.

Runs must also agree on **what was tested**, not just how: `accumulate-product-identity-mismatch`
refuses to pool runs whose repository commit, testing package version, package id or fingerprint,
or tool version or fingerprint disagree. A stability number spanning a regression and its fix
describes neither build — and without this check, varying the claimed commit minted "independent"
runs just as effectively as varying `deviceFingerprint`, while looking like ordinary metadata.

Only a **contradiction** rejects. Every one of those fields is written as the literal string
`unknown` when the harness was never told the answer, so comparing raw strings read a plain static
run and a device-evidence run built from an artifact manifest as two different builds — and then
discarded whichever side the majority vote outnumbered, which for one device shard among static
runs is the only evidence that carries first attempts at all. An unasserted fact cannot contradict
anything, so it cannot reject. What it also cannot do is confirm that two runs describe one build,
so `unverifiedProductIdentity` lists exactly which identity facts were taken on trust. An empty
list means every accepted run named the same build. A non-empty list means the merge is pooling
runs that never said whether they agree — the number is still published, but it is weaker evidence
than the same number with an empty list, and CI that cares should assert on it.

**Runs must be distinguishable to count.** `accumulate-duplicate-run` rejects any run whose
evidence fingerprint — contract version, platform, status, identity fingerprints, corpus summary,
platform profiles, merged rates, and per-flow first attempts — matches one already accepted. This
is what stops the same `qualification.json` being submitted five times. The practical consequence
is that CI shards must record what actually distinguishes them: a per-shard
`profiles[].deviceFingerprint`. Two shards that both report a bare 20/20 on an unnamed device are
indistinguishable evidence, and the second is dropped rather than added. That is the fail-closed
direction — it undercounts rather than inflates — but it means an accumulate job that forgets to
stamp device identity will silently plateau at one run's worth of attempts.

Say the uncomfortable part plainly: `deviceFingerprint` is a string the harness writes, and five
copies of one run file with that string edited will merge to 100 attempts. Stamping it is only
sound when the *harness* derives it from the device it actually drove. If a human types it, the sum
is not evidence, and `contributingDevices` will not know the difference. This is a reason to
generate run files from CI, never by hand — not a property the merge can enforce.

**Per-flow verdicts are not pooled.** Both `android-tier1-first-attempts` and its accumulated twin
judge each flow on its own evidence and take the worst verdict. A flow with a sufficient sample
that misses the stability threshold is a `fail` even when a sibling flow has not run yet; pooling
the reason codes used to downgrade that to `not-qualified`, which reports exit 0 on a measured
regression. One consequence worth stating: a *merged* flow that reached the attempt threshold while
some contributing run declined to call it real-device evidence reports `fail`, not
`not-qualified` — "we have attempts we cannot attribute to a device" is treated as a failure of the
measurement rather than an absence of it. The direction is deliberate; the wording of the reason
code is what tells the two apart.
