# Apple AI evaluation: model selection, chunking, and grounding

These September 30, 2026 experiments support the
[Apple embedding guide](https://github.com/dotnet/docs-maui/blob/main/docs/ai/embeddings/apple.md)
and [Apple chat guide](https://github.com/dotnet/docs-maui/blob/main/docs/ai/chat/apple.md).
They distinguish Apple's documented capabilities, the Essentials.AI adapter's
behavior, and observations from a small synthetic corpus. They are not a general
model leaderboard or a promise of accuracy on application data.

## Environment and evidence

| Item | Observed environment |
| --- | --- |
| Host | macOS 26.7, build 25G229, arm64 |
| Toolchain | Xcode 26.6 (17F113), Swift 6.3.3, .NET SDK 10.0.401 |
| Native embeddings | English sentence revision 1, 512 dimensions; English word revision 1, 300 dimensions |
| Chat | Available on-device Foundation Models, exercised through `AppleIntelligenceChatClient` on Mac Catalyst 26.7 |
| Device probes | Debug, with `-p:UserSecretsId=` to avoid embedding local secrets |
| Baseline source | `23ea45e98`; playground chunk policy from [#516](https://github.com/dotnet/maui-labs/pull/516), `1d58ce63d57e9454755e1e67cf5a1218aecda03a` |

The [embedding runner, corpus, method, and raw results](../../tests/AI/AppleEmbeddingEvaluation/README.md)
are retained. Protocol 2 records the corpus SHA-256, native model identity,
all query rankings, invalid-vector counts, and grouped Recall@1, Recall@3, and
mean reciprocal rank. Repeating the full 30 corpus/strategy runs produced
identical rankings and metrics on this host.

The [wrapper parity tests](../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Tests/MaciOS/NLEmbeddingGeneratorParityTests.cs)
compare actual managed results with native vectors, including long input and a
borrowed word model. All five cases passed. This establishes parity for these
cases, not semantic accuracy. The evaluation makes no claim about older Apple
runtimes, other devices, Release/AOT inference, or future model revisions.

## What sentence embeddings were useful for

Apple's [similarity guide](https://developer.apple.com/documentation/naturallanguage/finding-similarities-between-pieces-of-text)
positions sentence embeddings for phrase/sentence similarity, paraphrases, and
retrieval such as matching a question to an FAQ. Word embeddings serve individual
word similarity and vocabulary-neighbor query expansion.

The fixed short corpus contains 24 records and 24 pre-labeled queries:

| Group | Relevant record ranked first | Relevant record in top three |
| --- | ---: | ---: |
| Paraphrases | 6/6 | 6/6 |
| App intents | 5/6 | 6/6 |
| Technical terms | 3/4 | 4/4 |
| Opposites and exclusions | 6/8 | 7/8 |

All six chunk policies give the same short-corpus results because these records
fit in one chunk. The default `NLEmbeddingGenerator` follows the native English
sentence model; the parity tests did not identify managed vector corruption or
an extra truncation/chunking step.

That does not make similarity a reliable decision rule. For example:

- "Meat-free pasta dinner" ranked the beef record first: cosine 0.637656 versus
  0.636588 for the vegetarian record.
- "Use AES-256 to protect stored data" ranked decryption above encryption:
  0.664514 versus 0.584647.
- The offline-map query found its relevant record at rank 3; the unpaid-invoice
  query found its relevant record at rank 4.

These failures justify validating exclusions, identifiers, and structured facts
outside vector ranking. They do not establish that every query with negation or
technical terminology fails. Scores and score gaps are not probabilities; there
is no universal confidence threshold established by these experiments.

## Chunking matters, but 360 is not an Apple limit

The playground policy normalizes whitespace and takes up to 360 UTF-16 code units,
preferentially breaking at a space, without overlap. The experiment reproduces
that policy and compares whole documents, 180/720-character variants, individual
sentences, and three-sentence windows with one sentence of overlap.

The initial long-document pilot used different topics in its early/middle/late
groups. To avoid attributing topic differences to position, protocol 2 also
moves the **same six target sentences** through early, middle, and late positions
in the same nine documents. Each document contains 70 copies of a filler
sentence; the three distractors stay fixed.

| Policy | Early top-1 | Middle top-1 | Late top-1 | Chunks across nine documents |
| --- | ---: | ---: | ---: | ---: |
| Whole document | 4/6 | 5/6 | 2/6 | 9 |
| Fixed 180 | 5/6 | 6/6 | 6/6 | 225 |
| Playground 360 | 2/6 | 1/6 | 5/6 | 117 |
| Fixed 720 | 1/6 | 2/6 | 2/6 | 63 |
| One sentence | 5/6 | 5/6 | 5/6 | 639 |
| Three sentences, overlap one | 4/6 | 4/6 | 4/6 | 315 |

The highest count here came from 180-character chunks; individual sentences were
position-stable. **Neither is a universal optimum.** There are only six distinct
synthetic information needs, not 18 independent queries, and the repetition is
a deliberate stressor. Every document is scored by its best chunk, as in the
playground; more chunks also mean more work and more opportunities for an
irrelevant chunk to score highly.

Evaluate sentence/passage boundaries, heading context, and overlap on the app's
own labeled queries. Preserve source IDs and spans, index away from the UI
thread, and regenerate vectors only when content or the index's model and
preprocessing fingerprint changes. This experiment did not benchmark heading
context, throughput, memory, energy, or a cloud embedding alternative.

## Select models explicitly

The word model returned vectors for `coffee` and `coffees`, but not `Coffee`,
multiword inputs, `HTTP 429`, `AES-256`, an invented word, punctuation, or emoji
in this probe. The sentence model returned vectors for the nonempty probes,
including punctuation and emoji, but not empty/whitespace input. A nonempty vector
alone does not demonstrate useful meaning.

Currently, the adapter turns a missing native vector into an empty managed vector.
Validate dimension, finiteness, and nonzero norm at ingestion and query time.
Do not silently replace failed sentence vectors with word vectors: they are
different spaces. If a feature needs word-neighbor expansion, treat that as a
separate, explicitly selected operation, not a replacement for its sentence index.

This host returned sentence and word models for English, German, and Italian.
The bounded French, Spanish, Portuguese, Japanese, Chinese, Korean, Arabic,
Russian, and Hindi probes returned unavailable. This is **not** a universal
language-support list. Check the requested model at runtime and keep the
configured language, model kind, revision, dimensions, and preprocessing with
the index. Native language metadata itself can be absent, as it was for the
Italian word model.

Length-sensitivity probes used 16 through 512 repeated words plus distinct
prefixes or suffixes. All tested pairs produced different vectors; these runs
did **not** establish an undocumented truncation cutoff. Conversely, a long input
producing a vector does not prove it retained each fact.

[`NLContextualEmbedding`](https://developer.apple.com/documentation/naturallanguage/nlcontextualembedding)
is not a drop-in upgrade for this generator. Apple describes its token-level
contextual vectors as features for classification/tagging and points to
`NLEmbedding` for semantic similarity. Its documented
[`maximumSequenceLength`](https://developer.apple.com/documentation/naturallanguage/nlcontextualembedding/maximumsequencelength)
and subword truncation rules must not be attributed to `NLEmbedding`.
This study did not benchmark contextual pooling.

Native API floors also differ:
[`sentenceEmbedding`](https://developer.apple.com/documentation/naturallanguage/nlembedding/sentenceembedding(for:))
starts at iOS/Mac Catalyst 14 and macOS 11, while
[`wordEmbedding`](https://developer.apple.com/documentation/naturallanguage/nlembedding/wordembedding(for:))
starts earlier. Those are not guarantees about a complete packaged app: this
repository's Swift chat bridge targets OS 26. Older-runtime package loading was
not validated here.

## Chat: distinguish transport correctness from factual correctness

The retained [grounding probes](../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Tests/MaciOS/AppleIntelligenceChatClientGroundingTests.cs)
use only synthetic reservations, a short conversation, and a deliberately missing
fact. They check actual field values as well as JSON parsing, and exercise
streaming and non-streaming separately.

The [two pre-fix runs](../../tests/AI/AppleChatEvaluation/results-before-stream-fix.json)
show why one successful run is insufficient. The first extracted all six
reservation cases correctly, but an absent-fact case invented `1234567890`.
Across the two runs, fabricated answers appeared in valid JSON in both modes.
A `found` Boolean and instructions not to invent data did not guarantee abstention.

The second run also exposed **adapter-produced malformed streaming JSON**.
A deterministic, model-free reproduction showed that `JsonStreamChunker` could
append a primitive sibling inside an open string or container in the first
snapshot. The fix emits complete primitive properties before opening a growable
value. Five new reproductions failed before the fix and passed afterward,
alongside all 53 chunker tests. This is an adapter correction, not a finding that
Apple's native guided-generation API produces those malformed snapshots.

The [two post-fix runs](../../tests/AI/AppleChatEvaluation/results-after-stream-fix.json)
contain 18 structured responses, all parseable as JSON, plus two correctly
reported context-window errors. They passed 16/20 ground-truth assertions:
two reservation cases had an incorrect departure field, and two absent-fact
cases invented an answer. The syntax correction did **not** fix model
faithfulness, and these two repeated runs do not estimate a general failure rate.

An initial overload probe consisting of a repeated single word failed language
checking, so it did not validate a context-window diagnostic. A subsequent
full-English document probe returned "Exceeded model context window size".
Neither experiment measures the exact cutoff. Apple's
[context guidance](https://developer.apple.com/documentation/foundationmodels/managing-the-context-window)
documents the budget and what consumes it.

Use source-grounded, narrow tasks; validate returned facts and retain an unknown
or failure state. Guided generation constrains structure, not truth. Avoid
using an LLM for arithmetic, identifier validation, or decisions that ordinary
application code can make. The native schema converter also uses an unordered
property dictionary: C# declaration order and greedy sampling should not be
treated as a promise of byte-identical responses across requests or processes.

These live quality probes use the existing `RequiresModel=true` trait and are
excluded by default CI. A false answer must remain a failed ground-truth
assertion, not become a passing test that expects a hallucination. Keep the
portable chunker regressions separate from model-sensitive quality results.

## Image input: documented preview, not measured recognition quality

[PR #405](https://github.com/dotnet/maui-labs/pull/405) adds image **input** to
`AppleIntelligenceChatClient`; it is not merged/released support or image
generation. Source inspection covered encoded `DataContent`, local-file
`UriContent`, supported native `RawRepresentation` handles, orientation,
image-bearing history, and remote-URL rejection.

Apple's [`Attachment`](https://developer.apple.com/documentation/foundationmodels/attachment)
API starts at OS 27. The adapter additionally requires a vision-capable model.
This host is OS 26.7 and had no connected physical OS 27 device. An installed
27 simulator or a successful SDK build is not evidence of successful image
recognition. **Live OS 27 vision inference was not run.**

Before making image-quality claims, run the existing gated image device test and
extend it with distinguishable synthetic images and explicit expected answers.
Compare byte/file/native-handle inputs, rotated images, prior-turn images,
streaming, and non-streaming. For small text or exact document fields, evaluate
OCR separately rather than assuming image chat is a document-extraction engine.
Apple's [multimodal guide](https://developer.apple.com/documentation/foundationmodels/analyzing-images-with-multimodal-prompting)
recommends task-specific prompts, structured labels, and considering a region of
interest. Native framework capabilities do not imply that every capability is
exposed by the Essentials.AI adapter.

## Reproduce and detect regressions

The [embedding README](../../tests/AI/AppleEmbeddingEvaluation/README.md) contains
the runner and baseline-comparison commands. An incompatible corpus/model
fingerprint is an explicit refusal to compare, not a pass; review and establish
a new baseline deliberately when changing models.

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests \
  --filter FullyQualifiedName~JsonStreamChunkerTests

dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst -c Debug -p:UserSecretsId= --logger trx \
  --results-directory artifacts/TestResults \
  --filter FullyQualifiedName~NLEmbeddingGeneratorParityTests

# Opt-in live quality evaluation: model failures produce a nonzero exit.
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst -c Debug -p:UserSecretsId= --logger trx \
  --results-directory artifacts/TestResults \
  --filter FullyQualifiedName~AppleIntelligenceChatClientGroundingTests
```

Retain outputs as well as pass/fail counts. The chat
[collector](../../tests/AI/AppleChatEvaluation/collect-results.py) extracts
synthetic observations from TRX without machine paths; collection success is
not a passing quality evaluation. Follow Apple's
[model-update guidance](https://developer.apple.com/documentation/foundationmodels/updating-prompts-for-new-model-versions):
re-evaluate prompts and record results after OS/model, adapter, schema, or
chunking changes. Use application-representative evaluation data before shipping.
