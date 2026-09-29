# Apple Document Recognizer - Proposed Implementation

**Author:** Matthew Leibowitz
**Date:** 2026-08-20
**Status:** Prototype implemented
**Targets:** iOS 26+, Mac Catalyst 26+, macOS 26+

## Summary

Implement Apple's Vision
[`RecognizeDocumentsRequest`](https://developer.apple.com/documentation/vision/recognizedocumentsrequest)
as one `IDocumentExtractionClient` in `Microsoft.Maui.Essentials.AI`.

`AppleVisionDocumentExtractionClient` accepts supported images and PDFs. Images
go directly to `RecognizeDocumentsRequest`; PDF pages are rendered internally
with PDFKit and passed to that same request. The client never falls back to a
different OCR engine.

The public provider surface is intentionally small: one client class plus the
members required by `IDocumentExtractionClient`. Apple-specific structure uses
the proposed abstraction's existing extensibility points:

- provider-defined `DocumentBlockKind` values such as `listItem` and `barcode`;
- `AdditionalProperties` for markers, barcode payloads, detected data, and
  provider diagnostics;
- `JsonElement` in `RawRepresentation` for the complete bounded Apple snapshot.

The Swift bridge returns one JSON payload to managed code. It cannot safely use
Apple's synthesized `Codable` implementation directly: a real checklist PDF
caused Apple's encoder to recurse through repeated
`Table.Cell.content`/`List.Item.content` containers and stack overflow. The
bridge therefore still performs a bounded custom projection before encoding.

## Goals

- Import and exercise the proposed `Microsoft.Extensions.DocumentExtraction`
  API from `dotnet/extensions` PR #7588.
- Implement a direct client for `RecognizeDocumentsRequest`.
- Accept common image formats and PDFs through the same client.
- Keep PDFKit rendering private to the Apple client.
- Map titles, paragraphs, tables, cells, lists, list items, and barcodes.
- Preserve lines, words, candidates, detected data, geometry, and request
  metadata.
- Keep provider-specific data available without adding public element types.
- Support cancellation, concurrent requests, trimming, and Native AOT.
- Integrate the client into the cross-platform AI playground.
- Use only Apple frameworks and the repository's existing Swift bridge.

## Non-goals

- No `RecognizeTextRequest` client.
- No `VNRecognizeTextRequest` fallback.
- No automatic engine selection.
- No automatic use of embedded PDF text.
- No PDF text-layer extraction client in the initial implementation.
- No separate barcode request; `RecognizeDocumentsRequest` already returns
  barcodes.
- No DataScanner, ImageAnalyzer, Live Text, or Foundation Models integration.
- No third-party OCR, PDF, binding, or native dependencies.
- No visionOS target.
- No public PDF wrapper, PDF rendering options, raw node wrapper, capabilities
  type, custom serializer, or Apple-specific document-element subclasses.

## Selected architecture

```text
image/jpeg | image/png | image/heic | image/tiff | application/pdf
    |
    v
AppleVisionDocumentExtractionClient
    |
    ├─ image: RecognizeDocumentsRequest
    └─ PDF: internal PDFKit page rendering
             └─ RecognizeDocumentsRequest per rendered page
    |
    v
DocumentExtractionResult
    ├─ DocumentBlock
    │   ├─ title / paragraph
    │   ├─ provider kind "listItem"
    │   └─ provider kind "barcode"
    ├─ DocumentTable / DocumentTableCell
    └─ JsonElement RawRepresentation
```

## Public API

```csharp
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentExtractionClient
    : IDocumentExtractionClient
{
}
```

The client:

- accepts PNG, JPEG, HEIC, TIFF, and PDF streams;
- creates one `RecognizeDocumentsRequest` for an image or per rendered PDF page;
- never invokes another Vision request;
- streams one update per page;
- implements `ExtractAsync` by aggregating the same page stream;
- exposes standard `DocumentExtractionClientMetadata` through `GetService`;
- does not dispose the caller's stream.

Everything else is private or internal. In particular, PDF rendering settings,
capability enumeration, safe-snapshot DTOs, option keys, and the managed mapper
are implementation details rather than package API.

## Vision request options

Provider-specific settings use documented keys in
`DocumentExtractionOptions.AdditionalProperties`, exposed through typed
extension methods rather than a derived options class.

Planned settings:

| Setting | Apple target |
|---|---|
| Recognition languages | `textRecognitionOptions.recognitionLanguages` |
| Custom words | `textRecognitionOptions.customWords` |
| Language correction | `textRecognitionOptions.useLanguageCorrection` |
| Automatic language detection | `textRecognitionOptions.automaticallyDetectLanguage` |
| Maximum candidate count | `textRecognitionOptions.maximumCandidateCount` |
| Minimum text height | `textRecognitionOptions.minimumTextHeightFraction` |
| Barcode symbologies | `barcodeDetectionOptions` |
| Region of interest | `ImageProcessingRequest.regionOfInterest` |
| Revision | `RecognizeDocumentsRequest.Revision` |

The implementation validates each option before crossing the native boundary.
Unsupported languages, revisions, or symbologies produce explicit errors.

## Capability discovery

The native bridge queries supported languages, barcode symbologies, and request
revisions for validation and device tests. That capability type remains
internal rather than expanding the package API.

`DocumentExtractionClientMetadata` reports:

- provider name: `apple.vision`;
- default model/request ID: `recognize-documents`;
- selected revision where the metadata model permits it.

## Input handling

### Images

Initial supported media types:

- `image/jpeg`
- `image/png`
- `image/heic`
- `image/tiff`

The client rejects `application/pdf`, `application/octet-stream`, and unknown
types rather than guessing.

Processing:

1. Validate the media type and readable stream.
2. Copy bytes without disposing or changing ownership of the stream.
3. Read EXIF orientation through ImageIO.
4. Pass `Data` and orientation to `RecognizeDocumentsRequest`.
5. Map the first document observation to `DocumentPage` number 1.
6. Preserve all returned observations if Vision returns more than one; the
   exact aggregation policy will be determined by corpus results.

### PDFs

The PDF path is a private branch inside `AppleVisionDocumentExtractionClient`.

Processing:

1. Copy the stream into `NSData`.
2. Construct `PdfDocument`.
3. Validate encryption, lock state, permissions, and page count.
4. For each `PdfPage`:
   - read `GetBoundsForBox`;
   - account for `Rotation`;
   - render through `Draw(PdfDisplayBox, CGContext)`;
   - enforce DPI and maximum pixel limits;
   - report both requested and effective DPI when pixel limits clamp a page;
   - pass the resulting image data to the same private Vision request method;
   - map the Vision result directly with the actual one-based page number;
   - attach PDF page metadata and raw JSON;
   - yield the page and release render resources.

PDFKit's embedded `PdfPage.Text` is intentionally ignored.

## Mapping `DocumentObservation`

| Apple value | Output |
|---|---|
| `document.text.transcript` | `DocumentPage.Text` |
| `document.title` | `DocumentBlock` with `DocumentBlockKind.Title` |
| `document.paragraphs` | `DocumentBlock` with `DocumentBlockKind.Paragraph` |
| `document.tables` | `DocumentTable` |
| table row/column ranges | Cell indexes and spans |
| table cell `content` | Recursive `DocumentTableCell.Elements` |
| list items | `DocumentBlock` with provider kind `listItem` |
| `document.barcodes` | `DocumentBlock` with provider kind `barcode` |
| normalized regions | Clockwise `DocumentBoundingRegion.Polygon` |
| observation confidence | Nearest valid normalized confidence field |
| detected data | `AdditionalProperties` plus raw JSON |
| lines, words, candidates | Raw JSON plus selected metadata |

## Reading order

Apple exposes paragraphs, tables, lists, and barcodes as separate collections,
while `DocumentPage.Elements` promises one reading-order sequence.

The mapper will:

1. Convert all top-level elements to a common normalized coordinate system.
2. Apply a deterministic spatial ordering algorithm.
3. Keep collection/index provenance in `AdditionalProperties` and raw JSON.
4. Record the ordering strategy in page `AdditionalProperties`.
5. Validate multi-column, inline-table, nested-list, and barcode cases before
   claiming reading-order fidelity.

The original Apple collections remain available through `RawRepresentation`.

## Native bridge

`RecognizeDocumentsRequest` is Swift-only and is not currently bound by
`dotnet/macios`. Extend the existing
`src/AI/AppleNative/EssentialsAI/EssentialsAI.xcodeproj`.

### Native types

- `VisionRecognizeDocumentsClientNative`
- `VisionDocumentOptionsNative`
- `VisionDocumentResultNative`
- `VisionDocumentCapabilitiesNative`

### Native result strategy

- Project the recursive hierarchy into a bounded JSON-safe snapshot in Swift.
- Return one `NSData` JSON payload to C#; do not bind per-node native classes.
- Give every node a stable path and parent path.
- Normalize polygon winding in one native conversion point.
- Build JSON from the bounded flat snapshot rather than invoking Apple's
  `Codable` implementation, which stack-overflowed on a real-world checklist.
- Detect repeated containers on the active ancestor path using transcript,
  title, geometry, and direct child counts. Expose projected-node count,
  maximum traversal depth, and pruned-container examples as diagnostics.
- Reuse `CancellationTokenNative` to wrap the Swift `Task`.
- Invoke completion directly from the Swift task. Managed continuations already
  run asynchronously; redispatching to `OperationQueue.current` deadlocked the
  second page when extraction started from a worker thread.

## Raw data preservation

### `RawRepresentation`

Attach cloned `JsonElement` values from the bounded snapshot to
`DocumentPage`, `DocumentBlock`, `DocumentTable`, and `DocumentTableCell`.
This keeps raw provider data inspectable without a provider-specific public
wrapper type. `DocumentExtractionResult.RawRepresentation` remains unset
because assembled page streams already preserve page-level raw JSON.

### `AdditionalProperties`

Keep values small and JSON-safe. Planned keys include:

- `apple.vision.request`
- `apple.vision.revision`
- `apple.vision.observationId`
- `apple.vision.sourceCollection`
- `apple.vision.sourceIndex`
- `apple.vision.structureTruncated`
- `apple.vision.projectedNodeCount`
- `apple.vision.maximumTraversalDepth`
- `apple.vision.repeatedContainersPruned`
- `apple.vision.repeatedContainerExamples`
- `apple.readingOrderStrategy`
- `detectedLanguages`
- `apple.textAlignment`
- `apple.textDirection`
- `apple.pdf.pageLabel`
- `apple.pdf.rotation`
- `apple.pdf.displayBox`
- `apple.pdf.renderDpi`

Typed data belonging to barcodes and lists uses open block kinds and
provider-prefixed property keys; the bounded raw JSON remains authoritative.

## Cancellation, concurrency, and disposal

- Create a new Vision request and Swift task per invocation.
- Register managed cancellation before starting native work.
- Map cancellation to `Task.cancel()`.
- Check cancellation before Vision execution, after it completes, and while
  mapping large node sets.
- Cancel native work when a consumer stops enumerating pages.
- Process PDF pages sequentially by default to bound memory.
- Release each PDF bitmap before rendering the next page.
- Never dispose the caller's input stream.
- Dispose the temporary native JSON result after cloning it into managed
  `JsonElement` values.

## Sample

Create:

- a first-class `Documents` feature in
  `samples/AIExtensions.Sample.ChatPlayground`, with Apple registered only on
  supported Apple OS versions and independently configured cloud clients on
  every platform.

The sample includes:

- image selection;
- PDF selection and page rendering;
- request option and capability display;
- extracted page text;
- a heterogeneous element tree;
- table and nested-cell views;
- provider-kind list-item and barcode rows;
- polygon overlays;
- raw Apple JSON inspection;
- DevFlow status, tree, screenshot, and log inspection in Debug builds;
- run and cancel controls;
- timing and page-progress display.

The integrated AI playground additionally provides:

- every selected PDF page as a scrollable image preview;
- a responsive two-pane inspector for page previews and structured results;
- synchronized selection between image regions and normalized JSON paths;
- an anchored Inspect popup for client details, normalized JSON, complete
  provider raw JSON, and selected-node raw JSON without modal navigation;
- provider selection between local Apple Vision and deployed Foundry Mistral
  OCR 4 using descriptor-backed radio choices and the same
  `IDocumentExtractionClient` result model;
- optional comparison with a deployed vision-capable Foundry model through the
  Responses API, using a strict page/block/table schema while explicitly
  reporting that model output has no trustworthy geometry. This client reuses
  the app's existing Chat deployment when it supports image input;
- explicit cloud comparison: `AI:DocumentDeploymentName` identifies the
  specialized Mistral OCR model (for example `mistral-ocr-4-0`), while
  `AI:DeploymentName` identifies the general vision/chat model. Both clients
  are independently selectable and failures are never retried through the
  other client;
- a packaged corpus page for repeatable, credential-free DevFlow smoke tests;
- portable orchestration tests with a fake `IDocumentExtractionClient`.

The reusable device-test corpus currently includes:

- headings and multi-line paragraphs;
- flat, one-item, visually indented, and table-contained lists;
- a regular 4 x 3 table;
- URL, email, phone, postal address, calendar, and money detection;
- QR and Code 128 barcodes with known payloads;
- a mixed page containing title, link, table, list, email, and dates.

The corpus stores source SVG/Swift artwork, packaged PNGs, a JSON manifest, and
an evidence baseline under
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction`.
Future additions should cover spanning cells, GS1/supplemental barcodes,
multi-column reading order, skew, CJK/RTL, and degraded scans.

## Tests

### Portable tests

- Image/PDF media-type validation.
- Option-key validation and cloning.
- Default JSON serialization for provider-defined block kinds and properties.
- Direct client registration and descriptor discovery.
- Unary/page-stream equivalence.
- Caller stream ownership.
- Playground media-type routing, streamed page aggregation, progress, client
  lifetime, and cancellation.

### Apple tests

- Swift bridge invocation on each target.
- Capability enumeration.
- Table and nested-cell mapping.
- Barcode payload and metadata mapping.
- Flat, single-item, visually indented, and table-contained list behavior.
- Exact list-item self-projection filtering in the normalized model.
- Ancestor re-entry pruning and bounded traversal diagnostics.
- Link, email, phone, postal address, calendar, and money preservation.
- Polygon winding and coordinate origin.
- Reading-order corpus cases.
- Cancellation before, during, and after Vision execution.
- PDF page rendering, rotation, dimensions, progress, and memory bounds.
- Raw JSON preservation through `JsonElement`.

## Implemented sequence

1. Vendor the two PR #7588 source projects at pinned commit
   `a215825ae2c96723e922e068c226ff77122c7c94`, including the package READMEs
   and API-baseline JSONs. The two local project files remain the only deliberate
   divergence because they replace dotnet/extensions Arcade plumbing with
   non-shipping maui-labs project references and shared-helper shims.
2. Add the Swift documents request, native options, capabilities, bounded JSON
   projection, and one JSON result wrapper.
3. Bind only the request, options, capabilities, cancellation, and JSON result
   boundary.
4. Add `AppleVisionDocumentExtractionClient`.
5. Add the normalized mapper using standard document elements, open block
   kinds, `AdditionalProperties`, and raw `JsonElement` values.
6. Keep PDFKit rendering internal to that client.
7. Add portable and Apple device tests.
8. Add the Documents feature to the shared AI playground.
9. Record implementation friction and proposed abstraction changes in
    `apple-document-extraction-feedback.md`.

## Validation completed

- Swift framework builds with Xcode 26.6 and the macOS 26.5 SDK.
- Provider builds for iOS, Mac Catalyst, and macOS.
- The AI playground builds for Android, iOS, and Mac Catalyst without warnings.
- The Mac Catalyst playground exposes DevFlow and remains responsive after
  processing a three-page real-world checklist PDF that previously crashed.
- Standard `dotnet run -- --page Documents --document ...` arguments select the
  page and drive deterministic Debug extraction through .NET command-line
  configuration.
- That PDF now projects 104, 188, and 77 snapshot nodes for its three pages,
  pruning 10, 24, and 8 repeated list-container traversals respectively at a
  maximum traversal depth of 2. The standard-element mapper produces 249
  normalized nodes.
- Ten corpus tests validate packaged fixtures, headings, tables, semantic data,
  QR/Code 128, mixed layouts, and four list forms. The controlled list fixtures
  reproduce Apple's self-list output without PDF rendering and verify that the
  normalized model removes only exact same-item projections.
- Mac Catalyst and iOS simulator suites each pass 22 Apple Vision tests covering:
  - text recognition;
  - tables and cells;
  - numbered lists;
  - QR barcodes;
  - default normalized JSON;
  - raw Apple JSON;
  - internal capability discovery;
  - cancellation lifetime;
  - real two-page PDF recognition;
  - rotated PDFs with non-zero crop-box origins;
  - direct page-number mapping.
- The Essentials.AI package includes only the two temporary document-extraction
  DLLs for each TFM, with no dependency on unpublished package IDs.

## Success criteria

- Images are processed only by `RecognizeDocumentsRequest`.
- PDFs are processed only through explicit PDFKit rendering plus the supplied
  documents client.
- `ExtractPagesAsync` yields one completed result per PDF page.
- `ExtractAsync` returns the same pages as the page stream.
- Tables and recursive cell content map to base types.
- Barcodes, lists, and list items map to dedicated Apple element types.
- Every Apple-only field remains available through typed properties, raw
  references, or documented metadata.
- Native observations never enter generic JSON serialization accidentally.
- Cancellation stops Swift work and releases rendered pages.
- Large PDFs retain at most one rendered page by default.
- The sample demonstrates the richer Apple model and its abstraction gaps.

## Feedback and limitations

Known abstraction limitations, serialization constraints, and upstream
recommendations are maintained separately in
[`apple-document-extraction-feedback.md`](apple-document-extraction-feedback.md).

## References

- [Document extraction API proposal](https://github.com/dotnet/extensions/pull/7588)
- [Pinned proposal interface](https://github.com/dotnet/extensions/blob/a215825ae2c96723e922e068c226ff77122c7c94/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions/IDocumentExtractionClient.cs)
- [RecognizeDocumentsRequest](https://developer.apple.com/documentation/vision/recognizedocumentsrequest)
- [Recognizing tables within a document](https://developer.apple.com/documentation/vision/recognize-tables-within-a-document)
- [Mistral OCR 4 model card](https://ai.azure.com/catalog/models/mistral-ocr-4-0)
- [Mistral OCR API](https://docs.mistral.ai/api/endpoint/ocr)
- [Azure Document Intelligence layout model](https://learn.microsoft.com/azure/ai-services/document-intelligence/prebuilt/layout?view=doc-intel-4.0.0)
- [Azure Content Understanding](https://learn.microsoft.com/azure/ai-services/content-understanding/overview)
- [Azure OpenAI Responses API file input](https://learn.microsoft.com/azure/foundry/openai/how-to/responses#file-input)
- [PDFDocument](https://developer.apple.com/documentation/pdfkit/pdfdocument)
- [PDFPage](https://developer.apple.com/documentation/pdfkit/pdfpage)
- [.NET PDFKit bindings](https://github.com/dotnet/macios/blob/0ce310ba92638fee3cdcb575c461a23659bcf2f9/src/pdfkit.cs)
