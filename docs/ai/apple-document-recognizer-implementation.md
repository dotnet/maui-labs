# Apple Document Recognizer - Proposed Implementation

**Author:** Matthew Leibowitz
**Date:** 2026-08-20
**Status:** Prototype implemented
**Targets:** iOS 26+, Mac Catalyst 26+, macOS 26+

## Summary

Implement Apple's Vision
[`RecognizeDocumentsRequest`](https://developer.apple.com/documentation/vision/recognizedocumentsrequest)
as a raw `IDocumentExtractionClient` in `Microsoft.Maui.Essentials.AI`.

The Vision request accepts one image, so add a separate PDFKit wrapper client
that renders each PDF page and sends that image to the documents client. Neither
client performs fallback or chooses a different recognition engine.

Demonstrate Apple capabilities that are richer than the proposed base
abstraction with provider-specific elements:

- `AppleBarcodeElement`
- `AppleListElement`
- `AppleListItemElement`

Retain the complete live Apple observation behind native raw references even
when the normalized model cannot represent every field. Serialized raw
snapshots are bounded and explicitly report any recursive structure they prune.

## Goals

- Import and exercise the proposed `Microsoft.Extensions.DocumentExtraction`
  API from `dotnet/extensions` PR #7588.
- Implement a direct client for `RecognizeDocumentsRequest`.
- Accept common image formats and return one structured `DocumentPage`.
- Use PDFKit to turn a PDF into a page stream for the documents client.
- Map titles, paragraphs, tables, cells, lists, list items, and barcodes.
- Preserve lines, words, candidates, detected data, geometry, and request
  metadata.
- Demonstrate custom provider elements and record abstraction limitations.
- Support cancellation, concurrent requests, trimming, and Native AOT.
- Add an iOS/Mac Catalyst sample and a native AppKit macOS sample.
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

## Selected architecture

```text
image/jpeg | image/png | image/heic | image/tiff
    |
    v
AppleVisionRecognizeDocumentsClient
    |
    v
RecognizeDocumentsRequest
    |
    v
DocumentExtractionResult
    ├─ DocumentBlock
    ├─ DocumentTable / DocumentTableCell
    ├─ AppleListElement / AppleListItemElement
    └─ AppleBarcodeElement

application/pdf
    |
    v
ApplePdfKitRenderingExtractionClient
    |
    ├─ PDFKit opens and iterates pages
    ├─ each page is rendered to an image
    └─ each image is passed to AppleVisionRecognizeDocumentsClient
```

The clients have distinct media-type contracts:

| Client | Accepted input | Output cadence |
|---|---|---|
| `AppleVisionRecognizeDocumentsClient` | Supported `image/*` stream | One page |
| `ApplePdfKitRenderingExtractionClient` | `application/pdf` stream | One update per PDF page |

## Proposed public types

Names are provisional until implementation review.

### `AppleVisionRecognizeDocumentsClient`

```csharp
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionRecognizeDocumentsClient
    : IDocumentExtractionClient
{
}
```

The client:

- accepts only supported image media types;
- creates exactly one `RecognizeDocumentsRequest` per invocation;
- never invokes another Vision request;
- returns one `DocumentExtractionPageResult` from `ExtractPagesAsync`;
- implements `ExtractAsync` by aggregating that same page stream;
- exposes metadata and capabilities through `GetService`;
- does not dispose the caller's stream.

### `ApplePdfKitRenderingExtractionClient`

```csharp
public sealed class ApplePdfKitRenderingExtractionClient
    : DelegatingDocumentExtractionClient
{
    public ApplePdfKitRenderingExtractionClient(
        IDocumentExtractionClient pageClient,
        ApplePdfKitRenderingOptions? renderingOptions = null);
}
```

The wrapper:

- accepts only `application/pdf`;
- opens the PDF using `PdfDocument(NSData)`;
- rejects invalid or locked documents explicitly;
- renders pages sequentially;
- sends each rendered page to the supplied image client;
- renumbers the returned page and every nested bounding region to the actual
  one-based PDF page number;
- sets `PagesProcessed` and `TotalPages`;
- yields a page before rendering the next one;
- never examines `PdfPage.Text`;
- never substitutes a different inner client.

The wrapper owns and disposes the inner client, matching
`DelegatingDocumentExtractionClient`.

### `ApplePdfKitRenderingOptions`

```csharp
public sealed class ApplePdfKitRenderingOptions
{
    public double Dpi { get; set; } = 200;
    public int MaximumPixelDimension { get; set; } = 4096;
    public PdfDisplayBox DisplayBox { get; set; } = PdfDisplayBox.Crop;
    public bool IncludeAnnotations { get; set; } = true;
    public bool RespectCopyPermissions { get; set; } = true;
}
```

Render settings are constructor-level wrapper configuration. Per-request
Vision options remain in `DocumentExtractionOptions`.

## Apple-specific document elements

The custom elements intentionally demonstrate data returned by Apple that has
no typed home in the base abstraction.

### `AppleBarcodeElement`

```csharp
public sealed class AppleBarcodeElement : DocumentElement
{
    public AppleBarcodeElement(string symbology);

    public string Symbology { get; }
    public string? PayloadString { get; set; }
    public ReadOnlyMemory<byte>? PayloadData { get; set; }
    public bool? IsGs1DataCarrier { get; set; }
    public bool? IsColorInverted { get; set; }
    public string? SupplementalPayloadString { get; set; }
    public ReadOnlyMemory<byte>? SupplementalPayloadData { get; set; }
    public string? SupplementalCompositeType { get; set; }
}
```

Inherited properties carry:

- `BoundingRegion`
- `Confidence`
- `RawRepresentation`
- `AdditionalProperties`

The raw reference retains the complete Apple `BarcodeObservation`, including
its descriptor and any fields not projected onto the public element.

### `AppleListElement`

```csharp
public sealed class AppleListElement : DocumentElement
{
    public AppleListElement(
        IReadOnlyList<AppleListItemElement> items);

    public IReadOnlyList<AppleListItemElement> Items { get; }
}
```

The list element preserves:

- list geometry;
- ordered items;
- the native list container;
- semantically distinct nested structure exposed through item content.

### `AppleListItemElement`

```csharp
public sealed class AppleListItemElement : DocumentElement
{
    public AppleListItemElement(string text);

    public string Text { get; }
    public string? ItemString { get; set; }
    public string? MarkerString { get; set; }
    public string? MarkerType { get; set; }
    public IReadOnlyList<DocumentElement> Elements { get; set; } = [];
}
```

`Elements` maps semantically distinct children from the item's recursive Apple
`Container`, allowing an item to contain paragraphs, tables, nested lists, or
barcodes. It does not repeat the owning item when Vision re-emits that same line
as duplicate paragraphs and a same-polygon self-list.

The mapper does not assume that this recursive model is an acyclic tree. It
tracks non-recursive fingerprints for containers on the active ancestor path
and prunes an exact ancestor re-entry. A depth limit of 64 and a projected-node
limit of 20,000 remain as last-resort guards.

The raw snapshot retains the first Apple-produced self-list for investigation.
The normalized mapper filters it only when the child polygon, item text, and
marker match the owning item. This keeps provider data inspectable while
preventing duplicate nested UI and serialization output.

`AppleListElement` appears in `DocumentPage.Elements`. Its
`AppleListItemElement` children remain inside `Items` rather than being repeated
as top-level elements. Lists inside table cells appear in
`DocumentTableCell.Elements`.

### Serialization support

Provide an AOT-compatible source-generated JSON context and document-element
converter that registers:

| Discriminator | Type |
|---|---|
| `apple.barcode` | `AppleBarcodeElement` |
| `apple.list` | `AppleListElement` |
| `apple.listItem` | `AppleListItemElement` |

`AppleDocumentExtractionJson.Default` exposes cached, read-only options, while
`CreateOptions()` returns a mutable clone. The sample and Apple-specific
serialization APIs use these options. The base abstraction's default serializer
does not know provider-defined derived types; that limitation and its middleware
impact are tracked in
[`apple-document-extraction-feedback.md`](apple-document-extraction-feedback.md).

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

`GetService<AppleVisionDocumentCapabilities>()` returns immutable runtime
capabilities:

```csharp
public sealed class AppleVisionDocumentCapabilities
{
    public IReadOnlyList<string> RecognitionLanguages { get; }
    public IReadOnlyList<string> BarcodeSymbologies { get; }
    public IReadOnlyList<int> Revisions { get; }
}
```

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

The wrapper accepts only `application/pdf`.

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
   - encode or pass the resulting image data to the inner client;
   - rewrite page numbers recursively;
   - attach PDF page metadata and raw references;
   - yield the page and release render resources.

PDFKit's embedded `PdfPage.Text` is intentionally ignored by this wrapper.

## Mapping `DocumentObservation`

| Apple value | Output |
|---|---|
| `document.text.transcript` | `DocumentPage.Text` |
| `document.title` | `DocumentBlock` with `DocumentBlockKind.Title` |
| `document.paragraphs` | `DocumentBlock` with `DocumentBlockKind.Paragraph` |
| `document.tables` | `DocumentTable` |
| table row/column ranges | Cell indexes and spans |
| table cell `content` | Recursive `DocumentTableCell.Elements` |
| `document.lists` | `AppleListElement` |
| list items | `AppleListItemElement` |
| `document.barcodes` | `AppleBarcodeElement` |
| normalized regions | Clockwise `DocumentBoundingRegion.Polygon` |
| observation confidence | Nearest valid normalized confidence field |
| detected data | Apple metadata plus raw node reference |
| lines, words, candidates | Raw node reference plus selected metadata |

## Reading order

Apple exposes paragraphs, tables, lists, and barcodes as separate collections,
while `DocumentPage.Elements` promises one reading-order sequence.

The mapper will:

1. Convert all top-level elements to a common normalized coordinate system.
2. Apply a deterministic spatial ordering algorithm.
3. Keep collection/index provenance in the native node reference.
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
- `VisionDocumentPageNative`
- `VisionDocumentObservationNative`
- `VisionDocumentNodeNative`
- `VisionDocumentCapabilitiesNative`

### Native result strategy

- Retain one live Swift `DocumentObservation` wrapper per result observation.
- Project the recursive hierarchy into a flat Objective-C-compatible node array
  for managed mapping.
- Give every node a stable path and parent path.
- Normalize polygon winding in one native conversion point.
- Build lazy JSON from the bounded flat snapshot rather than invoking Apple's
  `Codable` implementation, which stack-overflowed on a real-world checklist.
- Detect repeated containers on the active ancestor path using transcript,
  title, geometry, and direct child counts. Expose projected-node count,
  maximum traversal depth, and pruned-container examples as diagnostics.
- Reuse `CancellationTokenNative` to wrap the Swift `Task`.
- Return callbacks on the caller's captured queue, matching the existing Apple
  Intelligence bridge.

## Raw data preservation

### `RawRepresentation`

Use an `AppleVisionDocumentNodeReference` containing:

- the retained native observation;
- the stable node path;
- lazy full-page JSON;
- lazy subtree JSON;
- callable character-range geometry.

Attach it to:

- `DocumentPage`;
- `DocumentBlock`;
- `DocumentTable`;
- `DocumentTableCell`;
- `AppleBarcodeElement`;
- `AppleListElement`;
- `AppleListItemElement`.

Leave `DocumentExtractionResult.RawRepresentation` unset because of the
serialization issue recorded in the feedback document.

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

Typed data belonging to barcodes and lists lives on their custom elements
rather than being flattened into property bags.

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
- Dispose native result handles when their managed references are released.
- The PDF wrapper owns and disposes its inner client.

## Sample

Create:

- `samples/DocumentExtractionSample` for iOS and Mac Catalyst.
- `samples/DocumentExtractionSample.MacOS` using the in-repo AppKit backend and
  linked shared UI/source.

The sample includes:

- image selection;
- PDF selection and page rendering;
- document-camera acquisition on iOS/Mac Catalyst;
- request option and capability display;
- extracted page text;
- a heterogeneous element tree;
- table and nested-cell views;
- dedicated barcode detail cards;
- nested list/list-item views;
- polygon overlays;
- raw Apple JSON inspection;
- DevFlow status, tree, screenshot, and log inspection in Debug builds;
- run and cancel controls;
- timing and page-progress display.

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
- Custom element construction.
- Apple JSON resolver round-trips for barcode/list/list-item elements.
- PDF page-number rewriting across all normalized and custom element types.
- Unary/page-stream equivalence.
- Caller stream ownership.

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
- Raw JSON and character-range geometry access.

## Implemented sequence

1. Vendor the two PR #7588 source projects at pinned commit
   `a215825ae2c96723e922e068c226ff77122c7c94`.
2. Add the Swift documents request, native options, capabilities, result
   wrappers, and node projection.
3. Add Objective-C binding definitions and native enums.
4. Add `AppleVisionRecognizeDocumentsClient`.
5. Add the normalized mapper and Apple custom elements.
6. Add the Apple source-generated JSON context and custom-element converter.
7. Add `ApplePdfKitRenderingExtractionClient`.
8. Add portable and Apple device tests.
9. Add the shared MAUI sample and AppKit wrapper sample.
10. Record implementation friction and proposed abstraction changes in
    `apple-document-extraction-feedback.md`.

## Validation completed

- Swift framework builds with Xcode 26.6 and the macOS 26.5 SDK.
- Provider builds for iOS, Mac Catalyst, and macOS.
- iOS and Mac Catalyst samples build without warnings.
- The AppKit sample builds and starts with the in-repo macOS backend.
- The Mac Catalyst sample exposes DevFlow and remains responsive after
  processing a three-page real-world checklist PDF that previously crashed.
- That PDF now projects 104, 188, and 77 native nodes for its three pages,
  pruning 10, 24, and 8 repeated list-container traversals respectively at a
  maximum traversal depth of 2. Exact self-projection filtering reduces the
  normalized sample tree from 372 to 257 nodes.
- Ten corpus tests validate packaged fixtures, headings, tables, semantic data,
  QR/Code 128, mixed layouts, and four list forms. The controlled list fixtures
  reproduce Apple's self-list output without PDF rendering and verify that the
  normalized model removes only exact same-item projections.
- The trimmed Mac Catalyst sample publishes successfully.
- Mac Catalyst and iOS simulator device suites cover:
  - text recognition;
  - tables and cells;
  - numbered lists;
  - QR barcodes;
  - custom-element JSON;
  - raw Apple JSON;
  - capabilities;
  - cancellation lifetime;
  - real two-page PDF recognition;
  - rotated PDFs with non-zero crop-box origins;
  - recursive page-number rewriting.
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
- [Pinned proposal interface](https://github.com/luisquintanilla/extensions/blob/a215825ae2c96723e922e068c226ff77122c7c94/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions/IDocumentExtractionClient.cs)
- [RecognizeDocumentsRequest](https://developer.apple.com/documentation/vision/recognizedocumentsrequest)
- [Recognizing tables within a document](https://developer.apple.com/documentation/vision/recognize-tables-within-a-document)
- [PDFDocument](https://developer.apple.com/documentation/pdfkit/pdfdocument)
- [PDFPage](https://developer.apple.com/documentation/pdfkit/pdfpage)
- [.NET PDFKit bindings](https://github.com/dotnet/macios/blob/0ce310ba92638fee3cdcb575c461a23659bcf2f9/src/pdfkit.cs)
