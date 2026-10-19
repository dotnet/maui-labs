# Corrected document extraction adoption — 2026-10-14

Upstream: [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), pinned to
`1fc95b5b77b48e085b49cf25fd1b3e4797825fa9`.
Shared Documents correction: [dotnet/extensions#7835](https://github.com/dotnet/extensions/pull/7835),
`37dad1a0837de86317a716c81805560a1cea18dc`, imported upstream in `d830ad69fe7145eb086e9662bf50bea4f58286d2`.

## Why try again

The `d44c549…` reduced-contract experiment was reverted because geometry, spans, raw-provider access and several other surfaces were lost.
The correction explicitly restores layout fidelity across page-to-document finalization, rather than relying on provider metadata.
This experiment imports the complete corrected source, not a local substitute or a compatibility fork.

## Corrected surfaces to exercise

| Capability | Supported upstream representation |
|---|---|
| Source frame | `DocumentSourcePage`: originating page identity, optional dimensions/unit/origin, source operation |
| Geometry | `DocumentRegion`: exact producer-ordered polygon vertices in that frame; double precision |
| Confidence | `DocumentConfidence`: reported value/kind and optional scale, without normalization or inferred calibration |
| Layout/detail associations | `DocumentAnnotation`: explicit regions, confidence, roles, structured supplied values and raw inspection |
| Merged/empty cells | Spans, optional native role and annotations independent of nullable cell content; covered and empty addresses differ |
| Images | Bytes/source/description survive without requiring invented captions or nonempty Markdown |
| Structured extensions | `DocumentExtractionExtension.Value` reaches shared `DocumentExtensionElement.Value` |
| Source output | One operation-scoped `DocumentSourceOutput`, with explicit native/body availability and ownership |
| Options | Per-call `ModelId`, adapter settings, and `CaptureRawOutput` (default true); fresh requests, no shared mutation |
| Finalization | Original deterministic builder creates disposable `DocumentExtractionResult.Document` and owns registered streams |

These are real API mechanisms, not permission to invent provider facts. Native roles/confidence/model IDs remain absent when not supplied.
An annotation's opaque structured value is not a standardized barcode/list/entity schema. The provider must label its supplied information.

## Raw-output rules

Native results are borrowed; the model never implicitly disposes the SDK's object. Available original response bytes must be detached
from the HTTP response and become a document-owned artifact exactly once, even when pages share an operation.
Serializing a mapped result is never described as the original response body.

Apple Vision is local and has no HTTP response body. Our bounded bridge snapshot is a detached representation of the recognized output,
not the actual Swift `DocumentObservation` instance or an original wire response. The operation's original-content status is unavailable.
Azure's SDK result is likewise not an original response body unless that body was actually captured independently.
Mistral's detached received body can be retained as original content if ownership and failure/cancellation cleanup are handled.

`CaptureRawOutput=false` must remove every raw carrier, including nested annotations/regions/source-frame associations. It does not
erase genuine portable semantic content or typed geometry/confidence. Raw data must not enter automatic Markdown, logs, embeddings,
normalized inspection, or ingestion records. Inspection is a deliberate action before the owning result/document is disposed.

## Provider constraints

Use only actual intersections between native output and the corrected contract. No image crops, inferred confidence/header levels,
guessed table roles, estimated token usage, client fallbacks, or custom semantic metadata dictionaries are added.
Per-call provider settings must be explicitly supported or rejected; missing values use documented defaults without mutating them.
Concurrent extractions must have independent operation output and request settings.

The native Apple request stays `RecognizeDocumentsRequest`, with bounded list/table recursion, orientation-aware images,
internal PDFKit rendering, direct callback completion and cancellation. Geometry must preserve original vertices rather than
reconstructing rectangles or silently reordering points.

## Operational APIs still absent upstream

The correction does not restore usage/progress envelopes, old client-metadata conveniences, URI/AI-content overloads,
configuration/OpenTelemetry middleware, or `OcrDocumentReader`. Pages still enumerate, but that is not a promise that a cloud
operation extracts incrementally over the network. Any completed-page count in the playground is local presentation, not a claimed
provider total or billing statistic.

The concrete Apple reader remains a clearly local `IngestionDocumentReader` projection over the canonical finalized document.
The artifact lifetime does not transfer silently to DataIngestion values; returned text/image values must be independent where necessary.
First-class normalized lists/items/nesting and barcode/entity/language detection schemas remain separate upstream review work.

## Provenance and release

All 30 project artifacts, including original projects, API baselines and documentation, are copied byte-for-byte and hash-verified.
Original namespaces/accessibility remain intact. Local build adapters reproduce shared guards and assembly experimental markers only.
The copied projects and prototype Essentials.AI library remain non-shipping and non-packable pending API/provider review.

## Remaining upstream projection issue

The canonical hierarchy can retain a nested table as a merged cell's content, but the current merged-table HTML projection
HTML-encodes the child's complete `ToMarkdown()` output. Nested table markup is consequently rendered as literal Markdown or
escaped HTML rather than structural nested table HTML. This is a presentation loss, not loss of the typed table in the document.
`UpstreamNestedTableMarkdownTests` records the distinction without changing the verbatim upstream implementation.

## Local evidence

The final corrected implementation passes 485 portable Essentials.AI tests and 158 playground feature/settings tests.
Eleven real Mac Catalyst 27 document reader/client tests pass in both Debug and Release, including images, multi-page PDFs, merged tables,
native geometry, explicit raw opt-out, input ownership, cancellation and invalid input.
The full playground builds for Catalyst 27 and Android; the library also builds for iOS 27 and macOS 27.

The isolated Catalyst playground processed the bundled two-page PDF through page streaming, whole-document finalization
and the ingestion reader. Both extraction paths retained two normalized BottomLeft source frames. Disabling raw capture
kept geometry and semantic annotations while changing both raw statuses to Disabled. Explicit inspection with capture
enabled returned two bounded bridge snapshots, with NativeStatus Retained and ContentStatus Unavailable, without inspection errors.
The same playground processed the bundled image using both configured cloud adapters. Azure returned one page with native SDK
output retained and original-body capture unavailable. Mistral returned one page with native parsed JSON and detached original
body both retained, and reported model identity `mistral-ocr-4-0`. Explicit Mistral inspection read both retained representations
without errors or truncation. Both canonical results retained actual source polygons.

The native list fixture exposed repeated recursive projections of an item's own content. Markdown now reconciles those
using matching item text/marker and overlapping source geometry, not global text deduplication. Distinct same-text nested
items remain distinct; all original structured observations remain available in semantic annotations and captured snapshots.

Incremental Release device builds twice retained stale native AOT outputs and aborted before running tests. A targeted
device-project Clean allowed execution and exposed the list regression above; final Debug and clean Release native runs each
pass all eleven cases. Existing MAUI Compatibility IL2037 linker warnings remain unrelated to this prototype.
