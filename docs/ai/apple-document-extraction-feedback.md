# Apple Document Extraction - Upstream Feedback

**Proposal reviewed:** `dotnet/extensions` PR #7588
**Pinned commit:** `a215825ae2c96723e922e068c226ff77122c7c94`
**Provider:** Apple Vision `RecognizeDocumentsRequest` with explicit PDFKit page
rendering

This document records abstraction and implementation issues discovered while
mapping Apple's document model. Findings should include a reproducer, impact,
workaround, and concrete upstream recommendation.

## Finding 1: Provider-defined document elements are not safely serializable

**Status:** Confirmed from the proposal source and System.Text.Json contract

**Impact:** High for provider extensibility

`DocumentElement` is public and abstract, so a provider can compile a custom
subtype such as `AppleBarcodeElement`. However, its polymorphic JSON contract
registers only:

- `DocumentBlock` as `"block"`
- `DocumentTable` as `"table"`
- `DocumentImage` as `"image"`

System.Text.Json rejects undeclared runtime subtypes by default. A provider can
create a custom `JsonTypeInfoResolver`, but it cannot guarantee that consumers,
logging middleware, telemetry middleware, and other pipeline components use
that resolver.

### Apple scenario

`RecognizeDocumentsRequest` returns first-class barcode observations with:

- Symbology.
- Text and binary payloads.
- GS1 carrier status.
- Color-inversion status.
- Supplemental payload and composite type.
- Barcode descriptor.
- Confidence and geometry.

The base abstraction has no barcode element.

### Prototype decision

The prototype will intentionally return provider-defined elements:

```csharp
public sealed class AppleBarcodeElement : DocumentElement;
public sealed class AppleListElement : DocumentElement;
public sealed class AppleListItemElement : DocumentElement;
```

An Apple-specific AOT-compatible JSON metadata/resolver helper will register
the discriminators `apple.barcode`, `apple.list`, and `apple.listItem`. This is
enough for the sample and consumers that opt into the Apple serializer.

### Known limitations

- `AIJsonUtilities.DefaultOptions` does not know the custom types.
- Generic logging and telemetry middleware may fail to serialize the result or
  reduce it to fallback JSON.
- A consumer using the base serializer without the Apple resolver cannot
  round-trip the custom elements.
- The provider cannot globally register the types without mutating shared
  serializer state.
- Keeping the imported proposal source verbatim prevents adding provider types
  to its `[JsonDerivedType]` attributes.

The custom elements are deliberate: they demonstrate the provider-extension
need and make the richer Apple data strongly typed during the prototype.

### Implementation evidence

The provider now returns `AppleBarcodeElement`, `AppleListElement`, and
`AppleListItemElement`. An Apple-specific source-generated JSON context and
converter round-trip all three types successfully. The generic base serializer
still cannot serialize them, so the middleware limitation remains.

### Recommendation

Consider one or more of:

1. Add a provider-neutral `DocumentBarcode`.
2. Add a supported registration mechanism for provider-defined element
   subtypes and discriminators.
3. Add a generic serializable custom element with an open kind.

An abstract CLR base alone is not a complete extensibility model when the
default serializer has a closed derived-type registry.

## Finding 2: Apple lists have no normalized hierarchy

**Status:** Confirmed from the Apple and proposal models

**Impact:** Medium to high for document fidelity

Apple exposes first-class lists, list items, marker strings, marker types,
geometry, and recursive item content capable of representing nested structure.
The proposal provides no list element and `DocumentPage.Elements` is flat.

### Prototype decision

- Map each Apple list to `AppleListElement`.
- Map each item to `AppleListItemElement`.
- Preserve marker text/type, geometry, and semantically distinct recursive item
  content as typed properties.
- Filter exact same-item paragraph/list projections from the normalized item
  while retaining them in the raw Apple snapshot.
- Preserve the complete native list/item container in `RawRepresentation`.
- Register both custom types through the Apple JSON resolver, subject to the
  limitations in Finding 1.

Device recognition of a generated numbered list successfully produced one
`AppleListElement` with three typed items.

A controlled indented-list fixture did not produce nested list hierarchy.
Vision returned one flat seven-item list while preserving bullet/hyphen marker
types and different horizontal positions. Consumers therefore cannot infer that
visual indentation will appear as recursive `List.Item.content`.

### Recommendation

Consider a provider-neutral list/list-item model or a generic hierarchical
container element.

## Finding 3: Detected data has no typed representation

**Status:** Confirmed

**Impact:** Medium

Apple associates typed email, phone, URL, address, and related matches with text
ranges. The normalized model can retain the text but not the detected entity,
range, or typed details.

### Prototype workaround

Store a compact JSON-safe representation on the owning block and keep the
native match in `RawRepresentation`.

### Recommendation

Consider a general annotation/entity collection with text ranges and an open
kind.

## Finding 4: Line, word, candidate, and range geometry is only raw data

**Status:** Confirmed

**Impact:** Medium

Apple exposes lines, words, N-best candidates, confidence, and a callable
`boundingRegion(for:)` API for character ranges. The proposal's smallest
normalized text unit is `DocumentBlock`, with one confidence value.

### Prototype workaround

Map selected text and confidence to the block. Preserve observations,
candidates, and callable range geometry through the native raw cursor.

### Recommendation

Clarify whether line/word/candidate detail is intentionally raw-only. If not,
consider text spans with ranges, geometry, candidates, and confidence.

## Finding 5: Result raw representation is not ignored during serialization

**Status:** Confirmed at the pinned commit

**Impact:** High

`DocumentPage`, `DocumentElement`, `DocumentTableCell`, and
`DocumentExtractionPageResult` mark `RawRepresentation` with `[JsonIgnore]`.
`DocumentExtractionResult.RawRepresentation` does not.

Logging middleware serializes the assembled result. An opaque Apple native
object may be serialized unexpectedly or reduced to `{}` by the middleware's
error fallback.

### Prototype workaround

Leave result-level `RawRepresentation` unset. Attach native cursors at page,
element, and cell levels.

### Recommendation

Add `[JsonIgnore]` to `DocumentExtractionResult.RawRepresentation`.

## Finding 6: Provider options cannot safely derive from the base options

**Status:** Confirmed at the pinned commit

**Impact:** High

`DocumentExtractionOptions` is not sealed, but `Clone()` is non-virtual and
constructs a base instance. The configuration decorator clones options, so a
provider-derived options instance is silently sliced.

### Prototype workaround

Use documented `AdditionalProperties` keys and typed extension methods.

### Recommendation

Either:

- make `Clone()` virtual and add a protected copy constructor, matching
  `ChatOptions`; or
- seal `DocumentExtractionOptions` so unsupported inheritance is explicit.

## Finding 7: Reading-order requirements may exceed provider output

**Status:** Requires device validation

**Impact:** Potentially high

`DocumentPage.Elements` promises reading order. Apple returns paragraphs,
tables, lists, and barcodes in separate collections. Their cross-collection
ordering is not documented.

### Prototype approach

Test multi-column pages and mixed inline tables/lists. Do not claim reading
order until a deterministic rule is validated.

### Recommendation

Clarify whether providers may return provider order, partial order, or unknown
order, and provide a way to report that fidelity.

## Current prototype evidence

Mac Catalyst device tests exercise the real OS 26 Vision request and currently
cover:

- plain document text and raw observation JSON;
- a structured two-column table;
- a three-item numbered list;
- a QR barcode with payload and symbology;
- custom-element JSON round-tripping;
- capability discovery;
- cancellation before execution;
- native Swift task cancellation and cancellation after a yielded page (the
  direct native cancellation probe is not part of the committed suite because
  Vision emits an expected OS error log that DeviceRunners treats as an
  infrastructure failure);
- unsupported media types;
- two-page PDFKit rendering, streaming progress, and recursive page-number
  rewriting.

The remaining high-value fidelity test is mixed multi-column reading order
across paragraphs, tables, lists, and barcodes.

## Finding 8: Vendored assembly identities will collide with future packages

**Status:** Known prototype constraint

The prototype embeds the vendored assemblies using their intended upstream
identities:

- `Microsoft.Extensions.DocumentExtraction.Abstractions`
- `Microsoft.Extensions.DocumentExtraction`

This avoids changing the copied implementation source and lets the proposed API
be tested as designed. Once official packages with those identities ship, an
application cannot safely reference both the prototype-bundled copies and the
official packages.

### Prototype mitigation

- Only the two DLLs are embedded in the Essentials.AI package; project PDB/XML
  outputs are excluded.
- Both vendored projects remain non-shipping and non-packable.
- The source is pinned to one PR commit and clearly marked as temporary.

### Required follow-up

Remove the bundled assemblies and replace the project references with official
package references before this provider advances beyond the prototype.

## Finding 9: Apple's recursive container output is not always an acyclic tree

**Status:** Confirmed on Mac Catalyst 26 with a real-world three-page checklist
PDF and controlled list fixtures

**Impact:** Critical for provider stability and raw-data serialization

`DocumentObservation.Container.Table.Cell.content` and
`DocumentObservation.Container.List.Item.content` are recursive containers.
The initial projection treated them as strict child trees. On the checklist,
Vision returned a list item's `content` containing another list whose item
`content` had the same transcript, title, polygon, and direct collection counts
as its ancestor:

```text
.../document/lists/0/items/0/content
  -> .../document/lists/0/items/0/content/lists/0/items/0/content
```

The source document does not contain recursively nested copies of the item.
This is an overlapping or self-referential semantic view in Vision's returned
model, not PDF recursion. Two independent consumers exposed the problem:

1. Encoding the Apple observation through its `Codable` conformance caused a
   native stack overflow.
2. Recursively projecting `content` without ancestor detection caused a second
   stack overflow. A temporary depth/node cap prevented the crash but produced
   20,000 projected nodes per page and 60,003 normalized sample nodes.

### Prototype workaround and evidence

- Retain the live native observation, but never invoke Apple's recursive
  `Codable` path.
- Serialize a safe flat snapshot with `JSONSerialization`.
- Track a non-recursive container fingerprint made from transcript, title,
  normalized polygon, and direct paragraph/table/list/barcode counts.
- Prune only when that fingerprint re-enters the active ancestor path.
- Keep depth 64 and 20,000-node limits as final safety guards.
- Report `structureTruncated`, projected node count, maximum depth, repeated
  container count, and the first ancestor/re-entry paths in page metadata and
  raw JSON.

DevFlow inspection of the fixed sample showed:

| Page | Projected nodes | Maximum depth | Repeated traversals pruned |
|---:|---:|---:|---:|
| 1 | 104 | 2 | 10 |
| 2 | 188 | 2 | 24 |
| 3 | 77 | 2 | 8 |

The cycle-safe app completed all pages, initially displayed 372 normalized tree
nodes, remained responsive, and no longer crashed. After the controlled corpus
identified exact list-item self-projections, filtering those duplicates reduced
the same PDF to 257 normalized nodes without changing the raw projected-node or
pruning diagnostics.

Controlled 1600 x 1200 list fixtures then isolated the behavior without PDFKit:

| Fixture | Vision classification | Projected nodes | Re-entries pruned |
|---|---|---:|---:|
| Three-item bullet list | One three-item list | 26 | 6 |
| Visually indented bullet/hyphen list | One flat seven-item list | 54 | 14 |
| Standalone one-item bullet | Text only | 5 | 0 |
| Bullets in separate table cells | Table cell text only | 29 | 0 |

The reusable sources, packaged images, tests, and full baseline are in the
[Apple Vision document corpus](../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction/README.md).

For every recognized list item, `item.content` returned two identical paragraph
nodes and a same-polygon list containing two identical copies of that item.
Descendant `content` then re-entered the same semantic container. This confirms
that the recursion was not introduced by PDF rendering or the managed mapper.

The normalized mapper now treats the owning `AppleListItemElement` as the leaf
for these exact self-projections. A child is filtered only when its polygon,
text/item string, and marker all match the owner; different nested content still
maps normally. The raw bounded snapshot retains the Apple-produced duplicates
for diagnostics.

### Remaining limitation

The fingerprint and self-projection filter identify semantic equivalence, not
native object identity. A genuinely nested container with identical text,
geometry, marker, and direct child counts could also be filtered or pruned and
reported. The live native observation remains available, but a claim that
arbitrary Apple observations can always be completely serialized is not
supportable with the current OS behavior.

### Recommendations

- File the checklist behavior with Apple because a framework-provided `Codable`
  conformance must not stack-overflow on framework-produced output.
- Document that provider raw representations may be live, non-serializable
  object graphs.
- Consider a provider-neutral extraction-fidelity/truncation marker rather than
  requiring every provider to invent an `AdditionalProperties` key.
- Require recursive providers to bound depth and node count even when the
  normalized abstraction itself is tree-shaped.
