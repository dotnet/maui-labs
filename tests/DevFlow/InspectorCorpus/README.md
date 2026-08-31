# Inspector selector-health corpus

Static fixtures for the DevFlow repair/advisory evaluator. `MauiPreviewQualificationCorpusRunner`
loads `corpus-manifest.json`, validates every case against
`schemas/selector-health-corpus-v1.json`, evaluates the fixture, and emits qualification samples.

**This corpus is static. Nothing in it is a device run.** Every number derived from it should be
read as "the evaluator behaves this way on hand-written fixtures", never as "the product behaves
this way on real applications".

**Most of it does not run the shipped analyzer either.** `EvaluateFixture` re-implements the
selector-health and repair-eligibility rules against the fixture JSON; it uses the diagnostic id
constants from `MauiSelectorHealthDiagnosticIds` but never calls `MauiSelectorHealthAnalyzer.Analyze`,
and `MatchesExpectations` compares those harness rules against `expect.diagnosticIds` authored beside
them. So `falseHeals`, `repairPrecision`, `repairRecall` and `abstention` are **self-consistency
checks**: they say the harness agrees with itself, not that the product is correct. Adding cases
raises their denominators without changing what they measure. The report says so directly —
`corpus.exercisesShippedAnalyzer` is `false`, each metric carries an `exercises` block, and the
`product-analyzer-coverage` gate stays `not-qualified`. The one exception is
`expectedFailureClass`: the observed label comes from the shipped `MauiFlowFailureClassifier.Classify`.
See `baselines/README.md` for what closing this gap would take.

## Case anatomy

| Field | Required | Meaning |
| --- | --- | --- |
| `schema` | yes | Always `1`. |
| `id` | yes | Stable case id, also the file name. |
| `kind` | yes | `baseline`, `mutation`, `no-repair`, `product-regression`, `repair-positive`, or `repair-negative`. |
| `disposition` | yes | `diagnostic-only`, `no-repair`, or `repair-eligible`. |
| `expectedFailureClass` | no | Hand-assigned ground truth (see below). |
| `provenance` | yes | Who labeled the case, when, how, and from what source. |
| `fixture` | yes | The evaluator input. |
| `expect` | yes | Expected diagnostic ids, candidate kinds, and repair eligibility. |

The case root is `additionalProperties: false`, so an unrecognized field fails the corpus rather
than being silently ignored.

## Provenance

Before this scheme existed, **who labeled a case and on what evidence was unrecorded.** Every case
now carries:

```json
"provenance": {
  "labeledBy": "devflow-maintainers",
  "labeledOn": "2026-08-15",
  "method": "hand-authored",
  "sourceKind": "synthetic",
  "reviewStatus": "unreviewed",
  "derivedFrom": "repair-positive-unique-locator-drift",
  "notes": "..."
}
```

| Field | Values | Notes |
| --- | --- | --- |
| `labeledBy` | free text, ≤128 chars | Team or role, not an individual identity. |
| `labeledOn` | `YYYY-MM-DD` | |
| `method` | `hand-authored`, `adapted-from-case`, `derived-from-replay`, `derived-from-incident` | `adapted-from-case` **must** set `derivedFrom`. |
| `sourceKind` | `synthetic`, `observed-local-run`, `observed-ci-run`, `reported-issue` | Only the two `observed-*` values mean a real run was involved. |
| `reviewStatus` | `unreviewed`, `peer-reviewed` | |

`TryReadProvenance` fails closed: a case with a missing or malformed provenance block is a corpus
error (`corpus-case-provenance-invalid`), not a warning.

The report surfaces `corpus.provenanceComplete` and `corpus.provenanceSourceCounts`. **Today every
case is `synthetic`**, so `provenanceSourceCounts` is a single `synthetic` bucket. That is the point:
the field makes the absence of observed evidence visible in the artifact instead of leaving it to be
inferred.

## Curated versus generated denominators

The runner also generates no-repair mutants from the curated seeds. These are **not independent
trials** — they are deterministic permutations (seed `20260802`) of a small seed set, so a clean
sweep across them is roughly one piece of evidence repeated, not N.

The report therefore splits every rate metric by source (`metrics.*.sourceCounts`) and splits the
corpus summary:

| Field | Current value |
| --- | --- |
| `curatedCases` | 58 |
| `curatedDerivedCases` | 30 |
| `curatedRepairPositiveCases` | 31 |
| `curatedNoRepairCases` | 16 |
| `generatedNoRepairCases` | 300 |
| `curatedClassificationLabeledCases` | 45 |
| `undeclaredProjectionCollisions` | 0 |
| `undeclaredShapeCollisions` | 7 |

Read `curatedCases` **with** `curatedDerivedCases`: 58 files are 28 original cases plus 30
restatements of one of them.

So the false-heal metric reads `0/316` = **16 curated + 300 generated**, and never `316` independent
observations.

### `curated-derived`: the repair-positive denominator is one seed restated

**30 of the 31 repair-positive cases are `adapted-from-case` copies of
`repair-positive-unique-locator-drift.json`.** They differ only in the *values* of `id`, `route`,
`oldSelector`, and `candidate` — values `EvaluateRepairFixture` never reads. (It reads whether
`candidate` is *present*, not what it contains.) Every one of them satisfies the same six
conjuncts for the same reason, so `repairPrecision` reading `31/31` rests on **one** independent
trial, not thirty-one. Growing the file count did not grow the evidence.

Rather than leave that to be discovered, the artifact records it:

- Cases whose `provenance.method` is `adapted-from-case` are emitted with sample source
  **`curated-derived`**, and such a case must name its seed in `provenance.derivedFrom`.
  `corpus.curatedDerivedCases` reports how many there are.
- `metrics.*.independentEvaluations` counts only `curated` and `device-backed` samples, and
  `metrics.*.independentConfidenceInterval` is the Wilson interval over *that* subset.
- **Every count gate compares `independentEvaluations`, not `denominator`, and every lower-bound
  gate reads `independentConfidenceInterval`, not the pooled one.** The pooled interval narrows
  toward certainty as clones are added, which is why it is disclosure only. `repair-precision`
  reports `repair-evaluation-count-insufficient` at an independent count of 1, exactly as it did
  before the 30 cases were added.
- `maui devflow flow qualify --accumulate` counts static evidence **once** no matter how many runs
  report it, because every accumulated run must share a corpus fingerprint — which covers the
  contents of every evaluated `.json` file under this directory (line endings normalised, and the
  manifest may not point a case into the unhashed `baselines/`) — and therefore re-reads the same
  files. Only `device-backed` counts are summed across runs.

**The split is still self-declared.** Nothing forces a case that copies a seed to say so. A clone
that simply omits `provenance.derivedFrom` and claims `hand-authored` would be counted as a 31st
independent trial. Two counters disclose that, and neither is a gate:

- `corpus.undeclaredProjectionCollisions` (currently `0`) counts undeclared cases whose
  **evaluation outputs** project identically onto another case's — same kind, disposition, repair
  eligibility, pass/fail, expected and observed class, classification basis, diagnostic ids,
  candidate kinds, and ineligibility codes.
- `corpus.undeclaredShapeCollisions` (currently `7`) counts undeclared cases whose **case-document
  shape** — every JSON key path in the case *file*, all values discarded — is a superset of another
  same-kind case's shape, or within two key paths of it. The shape spans the whole document, not
  just the `fixture` object: a clone that varies only its provenance notes or its expected
  diagnostic ids is still counted. It catches restatements the projection counter misses, because
  it ignores the values a clone would perturb to change its diagnostics.

Read these honestly. The first counter catches naive duplication — a copied file with the
provenance line deleted — which is exactly the mistake that produced the 30 derived cases in the
first place. It does **not** catch a determined clone: perturb an evidence-neutral fixture value
until the ineligibility codes differ and the projection no longer matches. The second is harder to
dodge, and neither is proof of anything. Containment alone was evadable in a single edit — add one
ignored key *and* delete one optional key and the two shapes become incomparable, so neither
contains the other — which is why the two-key tolerance exists. Adding keys alone never escapes,
however many: a superset is still a containment. Escaping takes an add-and-remove of three or more
key paths. Both counters are floors in the baseline diff (they must not grow)
and both are **disclosures, not rejections**. Neither establishes that a case is original; they
make an undeclared restatement something a reviewer has to argue for rather than something that
passes unremarked. Only review catches deliberate cloning.

Do not read the current `7` as a reassurance. Five of the seven are exact shape *equality*, not a
wider question — `xaml-advisory-duplicate/template-automation-id` against
`xaml-advisory-missing-automation-id`, `csharp-advisory-duplicate-automation-id` and
`csharp-no-proposal-template-factory` against `csharp-advisory-missing-automation-id`, and
`repair-no-orientation-seed-display` against `repair-no-login-modal-locale-theme` — the strongest
signal this counter emits. The remaining two (`repair-no-unknown-completion-infrastructure`,
`repair-no-imported-untrusted-attested`) sit two key paths from that same seed. Reading those seven
files shows they are genuinely different evidence, asking the same structural question with
different content. That is a judgement made by reading them, not a fact the number establishes —
which is the whole point of publishing it.

Closing the repair-precision gate honestly needs ~100 *materially different* repair scenarios —
different failure shapes, different candidate sets, different checkpoint outcomes — or real
device-backed runs. Restating this seed again will not move `independentEvaluations`.

## `expectedFailureClass` ground truth

`expectedFailureClass` is the class **a maintainer expects the product to report**, judged from the
scenario alone, drawn from the closed `MauiFlowFailureClasses` set. It is compared against the class
the shipping `MauiFlowFailureClassifier` actually produces to build
`metrics.classificationMatrix` and `metrics.classificationAccuracy`.

Rules:

- **The ground truth never feeds the classifier.** `BuildFailureFacts` projects the fixture onto
  `MauiFlowFailureFacts` using fixture structure only and does not read `expectedFailureClass`.
- **Generated mutants are deliberately unlabeled.** Machine-generating both the input and its
  expected label would make the metric tautological, so the classification denominator is curated
  cases only.
- **Cases that do not model a runtime replay failure are unlabeled** (source advisories, grant/
  redaction policy cases, and the baseline healthy cases). `curatedClassificationLabeledCases`
  versus `curatedCases` shows that coverage gap explicitly.

### Known limitation: most of the accuracy headline is not inference

Where a fixture carries a `failure` field, that value is passed through as
`MauiFlowFailureFacts.FailureClass`, and the classifier **honours a known stamp before it falls back
to inference**. The same is true of `terminalOutcome` and of an `otherFailures` entry that names a
class: each short-circuits `Classify` before any inference runs. For those cases the metric measures
stamp-to-report mapping fidelity, not inference from raw evidence.

The split is not re-derived by the corpus runner from the fixture shape — `Classify` reports which
input decided the answer (`MauiFlowFailureClassification.Basis`), and only `inferred` counts. That
matters because there are three separate ways to hand the classifier its own answer, and a
fixture-shape heuristic missed two of them.

The report splits the two rather than pooling them:

| Bucket | Current value | Meaning |
| --- | --- | --- |
| `classificationMatrix.inferredSampleCount` / `inferredCorrect` | **8 / 8** | The classifier derived the class from replay facts. |
| `classificationMatrix.stampHonouredSampleCount` / `stampHonouredCorrect` | **37 / 34** | The evidence already named a known class. |
| `classificationAccuracy` (pooled) | 42/45 | Reported, but dominated by the stamped bucket. |
| `classificationAccuracy.independentEvaluations` | **8** | What the gate actually counts. |

**Read the pooled 42/45 as "the corpus mostly labels itself".** Genuine inference is exercised by 8
cases. The gate requires 100 independent, genuinely inferred evaluations and reports
`classification-evaluation-count-insufficient` until it has them.

Be sceptical of the 8 as well. Only 3 of them exercise a discriminating rule (duplicate automation
ids → `ambiguous`; recorded route ≠ observed route → `route-state-drift`; assertion mismatch →
`assertion-failed`). The other 5 resolve to `locator-not-found` purely because the fixture contains
a selector-shaped key, and all 5 are labelled `locator-not-found` — so they are correct by
construction. The independent classification evidence is not just small, it is concentrated on one
rule.

(The 3 errors sit in the stamped bucket: those cases carry a `failure` stamp that a maintainer judged
wrong, so honouring the stamp disagrees with ground truth. That disagreement is a real finding and is
visible in `classificationMatrix.cells`.)

### Class skew

31 of the 45 labeled cases are `locator-not-found` repair positives. A single pooled accuracy number
is therefore dominated by one class; read `metrics.classificationMatrix.perClass` for the per-class
precision and recall instead.

## Adding a case

1. Add `cases/<id>.json` with the full schema above, including `provenance`.
2. Add the id to `corpus-manifest.json`.
3. Run `dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests --filter FullyQualifiedName~PreviewQualificationTests`.
4. If the case changes a reported metric, regenerate `baselines/qualification.json` (see
   `baselines/README.md`).

Notes:

- Any id containing `virtualized` force-adds the `target-virtualized-unscoped` ineligibility code.
- `expect.candidateKinds` must match the evaluated candidate list exactly, in order.
- The case root is `additionalProperties: false` and the runner **enforces it**: an unrecognised
  root key fails the corpus with `corpus-case-unknown-property` rather than silently dropping the
  case out of a denominator.
- `SelectorHealthTests.Corpus_ContainsStaticSchemaAndAtLeastQuarterNoRepairCases` requires
  `noRepairCount * 4 >= caseCount`. At 16 no-repair and 58 total there are 6 cases of headroom;
  adding more non-no-repair cases needs matching no-repair cases.
- Never commit a contiguous secret-shaped literal, even as a fixture value. Use obvious
  placeholders.
