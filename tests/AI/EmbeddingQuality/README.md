# Embedding chunking experiments

These are real `NLEmbeddingGenerator` device tests, not mocked retrieval tests.
They use the **same `DocumentChunker` and three synthetic documents as the
Chat Playground**, plus the unchanged short and matched-position stress corpus
from `../AppleEmbeddingEvaluation`. No cloud service or user documents are used.

## Run and compare

From the repository root on a Mac with the pinned SDK, MAUI workloads, Xcode,
and an available English sentence model:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -c Debug -p:UserSecretsId= \
  -p:ValidateXcodeVersion=false -p:DeviceRunnersDataTimeout=900 \
  --filter 'Suite=EmbeddingChunking' \
  --logger 'trx;LogFileName=apple-chunking.trx' \
  --results-directory artifacts/EmbeddingChunking

python3 tests/AI/EmbeddingQuality/collect-chunking-results.py \
  artifacts/EmbeddingChunking/apple-chunking.trx \
  --output artifacts/EmbeddingChunking/results.json \
  --compare tests/AI/EmbeddingQuality/results-macos27-arm64.json
```

`ValidateXcodeVersion=false` is needed only when using newer Xcode than the
default reference packs support. The 900-second runner data timeout matters:
DeviceRunners publishes xUnit output when this approximately 75-second test
completes, so its default 30-second idle timeout can terminate a healthy run.
Do not enable OpenAI or embed user secrets for this local Apple experiment.

The test is tagged `RequiresModel=true`, so model-free CI excludes it. Each
invocation generates a **fresh set of vectors three times**, deduplicating
identical input only within a trial. It runs 14 policies against five scenarios:
24 short queries, the same six stress queries with target sentences moved to the
beginning/middle/end, and 18 queries over the playground's three documents.
The three placements are controls, not 18 independent information needs.

The device test checks finite, nonzero, consistent-dimensional vectors,
repeatability, and the reviewed **per-query rank ceilings** in
`apple-rank-baseline.json`. A single worsened relevant-document or supporting
passage rank fails; another query improving cannot hide it in an aggregate.
The corpus/options/labels fingerprint and native model kind, configured language,
revision, and dimensions must match. Different model identities require explicit
re-evaluation, not silent acceptance. The original baseline was measured on
Mac Catalyst on macOS 27.0.1, English sentence revision 1, 512 dimensions; the
runtime reports OS version `27.0.0`. A second app launch reproduced every rank.

**A regression baseline is not an acceptable product-quality threshold.**
Known poor ranks and missing passages remain visible. The separate
`Suite=EmbeddingCorpus` tests retain the earlier ideal 100%-Recall@5 diagnostic;
Apple's 360-unit stress case is a known miss, not a passing acceptance result.
No OpenAI measurements were repeated for this new chunking sweep.

## Metrics and evidence

Document score is the maximum chunk cosine, with deterministic document-ID ties.
Report Recall@1 through @5, Hit@1 through @5, Precision@1 through @5, MRR, and
each relevant document's rank. The sample additionally ranks **all chunks from
all documents**, requiring every annotated answer-essential span to occur
together in a chunk from the correct source.

For example, a wheelchair query asking *where* requires both `Willow Loop` and
the accessibility description. A fragment saying "suitable for wheelchairs"
without the location does not count. `EvidenceCovered=false` means no chunk
contains all required spans; a low rank alone is not the problem. This is
deterministic labeled span coverage, not a general entailment evaluator.

The original single-marker pilot was strengthened to multi-span labels after
review exposed that missing-context false positive. Protocol 2 results below
use the stronger labels. Source documents, queries, and relevant document IDs
were not changed to improve scores.

With only three sample documents, document Recall@3 and whole-document
Evidence@3 are trivially complete. They do **not** establish good retrieval.
Small-chunk evidence ranks and the nine-document stress controls reveal
different failure modes. Output includes chunk counts, distinct chunk counts,
and indexed UTF-16 units to expose expansion cost; shared within-trial caching
means the run's elapsed time is not a per-policy latency benchmark.

## Measured tradeoffs, not universal optimal sizes

All three trials had identical rankings. Selected protocol 2 results:

| Policy | Sample document top-1 | Sample supporting chunk top-5 | Sample chunks | Stress document top-1 |
|---|---:|---:|---:|---:|
| Whole document | 16/18 | 18/18* | 3 | 11/18 |
| 180 UTF-16 units, no overlap | 16/18 | 7/18 | 23 | 17/18 |
| 360 UTF-16 units, no overlap | 16/18 | 12/18 | 12 | 8/18 |
| 360 units, 90-unit overlap | 16/18 | 15/18 | 15 | 16/18 |
| 720 units, 180-unit overlap | 15/18 | 16/18 | 8 | 14/18 |
| One sentence, no overlap | 16/18 | 10/18 | 36 | 15/18 |
| Two sentences, no overlap | 17/18 | 15/18 | 18 | 15/18 |
| Three sentences, one-sentence overlap | 17/18 | 16/18 | 18 | 12/18 |

*Whole-document top-5 is guaranteed with three documents and is not a snippet
quality win.*

Practical starting points for **experiments**, not model guarantees:

- Try two-sentence windows with no overlap, then three sentences with one
  sentence of overlap when nearby context is needed. One sentence often loses
  the subject of a following sentence.
- Compare 360/90 against the old 360/0 setting. On this fixture, overlap recovered
  context and improved the stress ranking, at the cost of more indexed text.
- Do not extrapolate the strong 180-unit stress result to natural documents:
  small windows split answer-essential facts and performed poorly on passage
  coverage. A document can rank correctly for the wrong chunk.
- Keep several candidates and inspect their source text. Exact identifiers,
  numbers, negation, and mandatory exclusions need application-level validation;
  neither a larger cosine nor a universal cosine cutoff certifies an answer.

The sample deliberately retains 360/0 as its existing default, not as a claimed
optimum. Use its settings pane to change mode, size, and overlap, explicitly
index that configuration, search, then select a document to inspect full chunks.
Model/revision/dimension/chunking identities isolate indexes. The portable
sentence splitter is punctuation-based, not Apple's `NLTokenizer`; fixed sizes
and overlaps are UTF-16 units, not tokens. See the sample README for its output
budgets and tokenizer limitations.

Apple describes sentence embeddings as sentence-similarity vectors, not a
document retrieval model with a documented optimal chunk length:
[Finding similarities between pieces of text](https://developer.apple.com/documentation/naturallanguage/finding-similarities-between-pieces-of-text).
The wrapper parity tests compare native and managed vectors directly; chunking
cannot remove limitations already present in the native sentence model.

## Updating a baseline deliberately

Retain `results-macos27-arm64.json` as evidence; do not overwrite it to make a
failure green. The collector exits 2 for incomplete/failed runs, incompatible
identities, or a worsened rank. Portable negative controls cover rank and
answer-essential-span handling in `EmbeddingQualityTests`.

For an intentional corpus, model, or protocol change, collect a complete run
to a new result file and use `--baseline-output <new-path>` to generate a
candidate rank fixture. This option refuses to overwrite an existing file.
Review the per-query changes and repeat in a fresh process before replacing
the checked-in fixture. An improvement is allowed; a regression needs an
explicit explanation, not a lower global percentage.
