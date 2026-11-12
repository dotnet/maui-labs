# Apple document extraction

Apple Vision's `RecognizeDocumentsRequest` extracts document text and structure
on iOS, Mac Catalyst, and macOS 26+. It runs on-device and does not require
Apple Intelligence.

> **Experimental:** the extraction contracts and provider APIs may change.

For installation and usage, see the
[package README](../../src/AI/Microsoft.Maui.Essentials.AI/README.md#document-extraction).
For builds and test commands, see the
[contributor guide](../../src/AI/README.md).

## Architecture

```text
caller-owned image or PDF stream
  -> Apple platform processor (image orientation / internal PDFKit rendering)
  -> Swift RecognizeDocumentsRequest
  -> cycle-safe native observation snapshot
  -> portable mapping to a completed ExtractionDocument
      -> structured extraction and explicit native inspection
```

`AppleVisionRecognizeDocumentsClient` implements the official abstract
`DocumentExtractionClient`. `ExtractAsync` returns one completed,
non-disposable `ExtractionDocument`; PDF page iteration is internal, not a
public streaming or progress contract.

The portable client and mapping live in
`src/AI/Microsoft.Maui.Essentials.AI/DocumentExtraction/`, separate from the
Vision/PDFKit processor in `Platform/MaciOS/`.
The Swift bridge separates snapshot data, bounded traversal, and encoding.

The Microsoft Documents and Extraction dependencies are official packages,
currently `11.0.0-preview.1.26560.3`. Dependency versions are maintained in
`Directory.Packages.props`; there is no local copy of the shared contracts.

## Inputs and request settings

- PDF pages are rendered internally with PDFKit, using finite rendering bounds
  and bounded scale.
- JPEG, PNG, HEIC, and TIFF inputs preserve image orientation. Multi-frame
  raster files use only the first frame.
- Caller streams remain caller-owned. Cancellation flows through native
  recognition and PDF processing.
- Per-call `DocumentExtractionOptions.AdditionalProperties` accepts supported
  `apple.vision.*` settings for recognition languages, custom words, language
  correction/detection, candidate count, minimum text height, barcode detection,
  symbologies, composite coalescing, region of interest, and request revision.
  Unknown keys and invalid values are rejected. Request settings do not mutate
  shared client defaults.
- `ExtractorName` selection is not supported by this native request.

## Normalized content and fidelity

The mapper uses only information actually supplied by Vision and representable
in the shared model. It does not hide missing features in arbitrary metadata.

| Feature | Representation |
|---------|----------------|
| Titles and paragraphs | Local text content and supported text roles; no invented heading levels |
| Source pages | Explicit pages, including blank pages, with source coordinate frames |
| Geometry | Exact ordered double-precision polygon vertices on source references |
| Tables | Independent cell sources, merged spans, empty anchors, and sections of mixed cell content |
| Confidence and OCR candidates | Native snapshot only; no normalized confidence or candidate schema |
| Lists, markers, and nesting | Supported text is projected once; list-specific structure remains native |
| Barcodes and detected entities | Native details, not a normalized barcode/entity schema or opaque text metadata |
| Words, lines, alignment, and recognition languages | Native details where captured |
| Figure crops and captions | Not supplied by this request; not fabricated |

Apple source frames use a normalized `1 x 1` coordinate space with a bottom-left
origin. These dimensions are not raster pixel dimensions. Rotated polygons
must not be replaced with bounding rectangles or reordered.

## Native inspection and safety

Native representations are borrowed supporting data, not semantic payloads.
The Apple bridge provides detached JSON snapshots, not live Swift
`DocumentObservation` instances. There is no HTTP response body to retain.
Raw output is not automatically included in normalized serialization,
logging or embedding input. There is no mutable client
last-response state.

### Native leaf encoding

The bridge uses Apple's `Encodable` implementations for Vision enum names,
rather than maintaining switches for barcode symbologies, list markers,
alignment, and composite types. For example, Apple's encoder produces
`{"qr": {}}`; the snapshot keeps the case key `"qr"` for those payload-free
values. Encoding failures propagate through the native error callback.

On OS 27+, detected-data semantic details are retained as Apple's complete
encoded JSON under `detectedData[].details`. For example:

```json
{"details":{"moneyAmount":{"_0":{"amount":12.5,"currency":"USD"}}}}
```

The bridge does not enumerate those cases or select their payload fields.
Additional cases and fields emitted by Apple's encoder can therefore pass
through to C# as `JsonElement` without new mapping code. This depends on what
the running framework actually encodes; it does not make arbitrary new
document container kinds automatically supported.

DataDetection's `Encodable` conformances start at OS 27, not OS 26.
OS 26 retains explicit semantic-detail/highlight mappings, including their
older flattened JSON shape. Consumers of native inspection must not assume
the same shape across OS versions. Neither shape is normalized document metadata.

Vision's recursive list/table containers can expose overlapping or
self-referential semantic views. A list item's content can contain duplicated
paragraphs and a same-polygon list whose descendants re-enter that item's
container. Calling the framework's recursive `Codable` implementation on such
output can overflow the native stack.

The bridge therefore controls container traversal rather than applying native
`Codable` to the whole graph. Leaf encoding does not walk those containers.
Traversal detects ancestor re-entry using a
non-recursive fingerprint of text, title, polygon, and direct collection
counts. Depth and node limits are final safety guards. Snapshot diagnostics
report truncation, traversal counts, and re-entry paths.

The mapper filters exact list-item self-projections using matching text,
geometry, and markers; different nested content is retained. The bounded native
snapshot retains diagnostic detail independently of the normalized projection.
Fingerprint equivalence is not native object identity: genuinely nested
identical containers can also be pruned. A snapshot is consequently not a
guarantee that every native field or arbitrary recursive graph is captured.

## Playground comparison

The [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
calls the selected extraction client directly. The configured examples are Apple Vision
and one cloud comparison, Foundry Mistral. Mistral uses the published
`CommunityToolkit.DocumentExtraction.Mistral` package (`1.0.0-preview`) and
the shared `AI:Endpoint` / `AI:ApiKey` configuration plus
`AI:DocumentDeploymentName`.

The sample constructs the Toolkit client directly. Transport defaults and
provider mapping belong to the Toolkit, without a sample-specific cloud policy
or response wrapper. The normalized view and explicit native JSON inspection
are separate. Inspection serializes the full available native result on demand,
without an output cap or cache; this does not remove the Apple bridge's
native traversal safety bounds.
The UI treats every registered client the same: names and descriptions are display
metadata, not capability flags. Per-call options are forwarded unchanged; each client
validates what it supports. There are no cloud/local or Apple-specific branches in the
settings or execution path.

## Capability gaps and upstream feedback

These are current fidelity boundaries, not requests to reproduce an older API:

- **Lists:** a provider-neutral list/item representation would let consumers
  retain actual markers and hierarchy when supplied. Visual indentation alone
  must not be interpreted as native nesting.
- **Detected entities and OCR detail:** typed text-range associations could
  represent actual native detections, candidates, and line/word geometry
  without inventing universal metadata schemas.
- **Reading order:** Vision supplies paragraphs, tables, lists, and barcodes
  in separate collections. Their cross-collection order is not documented;
  mixed-column reading order needs validation rather than an unconditional
  fidelity claim.
- **Recursive native output:** framework-produced containers can re-enter
  ancestors. This is worth reporting to Apple, especially where native
  `Codable` overflows. Consumers also benefit from an explicit extraction
  fidelity/truncation signal when safe traversal cannot preserve every node.
- **Raw representation semantics:** native objects may be non-serializable
  graphs; a bridge snapshot is not the original object or response bytes.
  Explicit inspection must remain distinct from semantic content.

The shared contract does not provide generalized confidence/annotation fields,
first-class list/barcode/entity schemas, public page streaming, raw-capture
controls, response-body ownership, or structural Markdown rendering. The
provider does not recreate those APIs locally.

Use the [synthetic document corpus](../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction/README.md)
to reproduce native classification and recursion behavior. It distinguishes
fixture intent, native observations, and normalized assertions.
