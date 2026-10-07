# Apple NaturalLanguage embedding evaluation

This is a local, synthetic, **native API** experiment, not a semantic-correctness
guarantee or a replacement for device tests. It uses the installed
`NaturalLanguage.NLEmbedding` models, without cloud inference or explicit
network/download requests.
`NLEmbeddingGenerator` is tested separately by the Mac Catalyst device tests.
The experiment was fixed before inspecting the ranks; the declared relevance
labels live in `corpus.json` (schema 1), whose byte-level SHA-256 is in the output.
The provenance for the comparison strategy is playground #516,
`1d58ce63d57e9454755e1e67cf5a1218aecda03a`,
`DocumentSearchService.cs`; this experiment does **not** change the sample.

From the repository root on a macOS host with Xcode:

```sh
mkdir -p artifacts/AppleEmbeddingEvaluation
xcrun swiftc -O tests/AI/AppleEmbeddingEvaluation/Evaluate.swift \
  -o artifacts/AppleEmbeddingEvaluation/evaluate
artifacts/AppleEmbeddingEvaluation/evaluate \
  --output artifacts/AppleEmbeddingEvaluation/results-local.json \
  --compare tests/AI/AppleEmbeddingEvaluation/results-macos26-arm64.json
# Optional absolute group-metric tolerance; default 0.01:
artifacts/AppleEmbeddingEvaluation/evaluate \
  --output artifacts/AppleEmbeddingEvaluation/results-local.json \
  --compare tests/AI/AppleEmbeddingEvaluation/results-macos26-arm64.json \
  --tolerance 0.01
```

The generated executable and local result stay in the ignored `artifacts` directory.
The retained baseline `results-macos26-arm64.json` was collected on macOS
26.7 (25G229), arm64, with Xcode 26.6 (17F113)/Swift 6.3.3 and English
sentence model revision 1, dimension 512.
It contains synthetic text only and OS/model metadata, not host identity.
The comparator rejects mismatched protocol version, corpus hash/schema, preprocessing, language,
kind, revision, dimensions, strategies, and groups. For each group and strategy,
it returns exit code 2 when **Recall@1, Recall@3, or MRR** decreases more than
0.01 absolute from the baseline; it does not detect improvements as regressions.
The machine-readable JSON retains every query's full ranked document list and
cosine scores, plus per-document attempted/invalid chunk counts. Availability
is explicit (`available: false`), not an empty scored run. A host without the
English sentence model exits 2 rather than producing success-shaped results.
The baseline is illustrative, not a cross-OS acceptance gate: running with
different installed model revisions intentionally refuses comparison.

## Protocol and interpretation

The short corpus has 24 documents and 24 queries, pre-labeled as paraphrase
(6), app intent (6), technical identifier (4), or adversarial opposites (8).
The separate pilot controlled corpus has six unique target records (two each with
the relevant sentence early/middle/late) and three distractors. All nine
documents share 70 copies of an unrelated filler sentence; position and
distractors are deliberate stressors, not naturalistic document sampling.
The pilot's position groups use different queries, so their differences cannot
be attributed to position alone. Protocol 2 adds a matched control: move every
target sentence to the beginning, middle, or end, using the **same six queries,
relevance labels, nine documents, and 70 filler sentences** in each run.
Distractor sentences stay in the middle. This additional control was designed
after the pilot; none of its text or relevance labels were changed to improve scores.
The reference 360 policy collapses whitespace, trims, slices at up to 360
**UTF-16 code units**, prefers the last ASCII space beyond the midpoint,
and has no overlap. 180/720 use the same policy; sentence1 is a native
`NLTokenizer(unit: .sentence)` span, while sentence3 combines up to three
sentences with one-sentence overlap. These are **characters, not tokens**.
All queries and chunks use the *same English sentence model* and
whitespace-collapse/trim preprocessing. Each document receives its **maximum
cosine among valid chunks**, just like the playground. Documents without valid
chunks are unranked and are counted as misses; unavailable query vectors fail
the run. Scores are descending, with exact ties broken by ascending document
ID. Recall@1/@3 counts any declared relevant document in the first 1/3; MRR
uses the first relevant document. Group scores are kept separate, not
collapsed into a flattering single number.

### Observed baseline: Recall@1 / Recall@3 / MRR

All six short strategies are identical (each short record occupies one chunk):

| Short group | n | Whole / all chunk policies |
| --- | ---: | --- |
| Paraphrase | 6 | 1.00 / 1.00 / 1.00 |
| App intent | 6 | 0.83 / 1.00 / 0.89 |
| Technical | 4 | 0.75 / 1.00 / 0.88 |
| Adversarial opposites | 8 | 0.75 / 0.88 / 0.84 |

| Pilot long strategy | Chunks across 9 docs | Early (n=2) | Middle (n=2) | Late (n=2) |
| --- | ---: | --- | --- | --- |
| Whole | 9 | .50 / 1.00 / .75 | 1.00 / 1.00 / 1.00 | .00 / .50 / .33 |
| Fixed 180 | 225 | .50 / 1.00 / .75 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| Playground 360 | 117 | .00 / .50 / .25 | .00 / .50 / .23 | 1.00 / 1.00 / 1.00 |
| Fixed 720 | 63 | .50 / .50 / .58 | .50 / .50 / .56 | .50 / 1.00 / .75 |
| One sentence | 639 | 1.00 / 1.00 / 1.00 | .50 / 1.00 / .75 | 1.00 / 1.00 / 1.00 |
| Three sentences, overlap one | 315 | 1.00 / 1.00 / 1.00 | .50 / 1.00 / .75 | .50 / 1.00 / .75 |

The stronger **matched-position control** produced these top-1 counts:

| Strategy | Early (same 6 queries) | Middle (same 6 queries) | Late (same 6 queries) |
| --- | ---: | ---: | ---: |
| Whole | 4/6 | 5/6 | 2/6 |
| Fixed 180 | 5/6 | 6/6 | 6/6 |
| Playground 360 | 2/6 | 1/6 | 5/6 |
| Fixed 720 | 1/6 | 2/6 | 2/6 |
| One sentence | 5/6 | 5/6 | 5/6 |
| Three sentences, overlap one | 4/6 | 4/6 | 4/6 |

Full Recall@3, MRR, ranks, and chunk counts are in the `matched_*` runs in
the JSON. Sentence chunks were position-stable here; the 180-character strategy
had the highest top-1 count. Neither result establishes a universal optimum.
These remain only six synthetic queries with intentionally repetitive documents,
not independent samples of 18 different information needs.

None of the generated retrieval chunks returned nil/invalid vectors on this
host. On the **short** corpus, q09 (offline map) was rank 3, q15 (encrypt)
rank 2, q17 (unpaid invoice) rank 4, and q23 (vegetarian pasta) rank 2.
Opposites and negation remain an especially poor basis for factual decisions:
semantic proximity does not imply the claim is true. In the artificial
long-content experiment, smaller or sentence-aware chunks often expose buried
content, but size, overlap, generic filler and max pooling change both
retrieval and cost. No single size or ranking threshold follows from these
six controlled cases. Retest with the application's own labeled data before
choosing chunk boundaries and verifying returned source text.

### Model and input probes

This host exposed sentence **and word** embeddings for English, German and
Italian; French, Spanish, Portuguese, Japanese, Chinese, Korean, Arabic,
Russian and Hindi were unavailable in this bounded probe. Availability is
device/OS-dependent. English word model returned a vector for `coffee` and
`coffees`, but nil for `HTTP 429`, `AES-256`, multiword input, an invented
word, `Coffee`, punctuation and emoji. Sentence model accepted these
nonempty probes (including punctuation and emoji), but returned nil for
empty/whitespace input. Never silently substitute word for sentence vectors
or pool word vectors into a sentence index: that changes the model space.
An experimental multilingual control uses one English/German/Italian
translation against each *individual language model*, with statuses and
within-model cosines in JSON. It does not claim cross-model alignment.

With 16/32/64/128/256/512 repeated prefix words and two distinct suffixes,
every pair was **different** (cosines .978/.988/.991/.992/.993/.992).
Moving the distinct word to the front also gave different vectors
(.977/.991/.997/.999/.9997/.9999). These are sensitivity measurements, not
proof of an undocumented word/token limit. A long vector existing does not
prove it preserves all details.

## Integration guidance

For `Microsoft.Maui.Essentials.AI`, use `NLEmbeddingGenerator` for local
embeddings on supported Apple OS versions. The underlying **sentence API**
starts at iOS/iPadOS 14+, Mac Catalyst 14+, or macOS 11+; the older availability
of the base `NLEmbedding` API does not establish sentence-model availability.
These are native API floors, not a claim that the current package runs on all
those versions: the native Swift chat bridge targets OS 26, and this evaluation
only ran on 26.7. Keep the configured language, **native model
kind/revision/dimension** and
preprocessing with every persisted index; check native availability and
rebuild an index after a model revision changes. Query and documents must use
the same installed model. Reject nil/empty/nonfinite vectors at ingestion and
query time; show the original source records and do not treat a cosine
ranking as a validated answer. The wrapper's current nil native word-vector
result is an **empty vector**, not an exception; consumers must check it.
`AppleIntelligenceChatClient` is a separate on-device chat capability on
Apple Intelligence-compatible 26+ OS versions, not part of this evaluation.
No production API change is implied without a design decision and more
representative evaluation.

To run just the wrapper parity tests with the device runner on a Mac:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -c Debug -p:UserSecretsId= \
  --logger trx --results-directory artifacts/TestResults \
  --filter FullyQualifiedName~NLEmbeddingGeneratorParityTests
```

Do not use private/user documents in fixture or retained outputs. The runner
does not explicitly request model assets; device tests need the MAUI/Mac Catalyst workloads
and the installed Xcode selected by the local build environment.
