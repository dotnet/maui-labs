# Apple RecognizeDocuments prototype

**Status:** Non-shipping API evaluation, revived in #450.
**Native API:** Vision `RecognizeDocumentsRequest`.
**Platforms:** iOS 26+, Mac Catalyst 26+, macOS 26+.
**Proposal:** [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), commit
`a215825ae2c96723e922e068c226ff77122c7c94`.

## Goals and boundaries

Exercise the real proposed Microsoft APIs as though their packages were available. Keep both complete proposed projects unchanged,
including namespaces and public accessibility, in a separate source snapshot. File-link them through non-shipping build adapters.
Do not substitute a smaller private contract, modify the proposal to accommodate Apple, or ship the copied assemblies.

The provider returns the intersection of genuine Apple output and standardized proposal fields. If either side lacks a matching feature,
leave it absent and record the gap. There are no fabricated image crops, inferred confidence/roles, custom barcode/list block kinds,
provider-property-bag projections, or fallback clients. The proposal's explicit raw slot holds the bounded native snapshot.

## Source layout

| Location | Responsibility |
|---|---|
| `src/Libraries/DocumentExtraction/Upstream/` | Both complete verbatim proposal projects and original `OcrDocumentReader` |
| `src/Libraries/DocumentExtraction/upstream-manifest.json` | Pinned file paths and hashes |
| `src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions/` | Local file-link build adapter, not a modified upstream project |
| `src/Libraries/Microsoft.Extensions.DocumentExtraction/` | Local file-link middleware build adapter |
| `src/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction/` | File-link build of the unchanged upstream reader adapter |
| `src/AI/AppleNative/EssentialsAI/RecognizeDocumentsRequest*.swift` | One request bridge and bounded native snapshot |
| `src/AI/AppleNative/RecognizeDocumentsRequestApiDefinitions.cs` | Internal Objective-C bindings |
| `src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleVisionRecognizeDocumentsProcessor.cs` | Image orientation, PDFKit pages, native dispatch/cancellation |
| `src/AI/Microsoft.Maui.Essentials.AI/Internal/DocumentExtraction/` | Canonical supported-field mapper and portable portion of the real client |
| `src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleVisionRecognizeDocumentsClient.cs` | Public Apple constructor/wiring |
| `src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleVisionRecognizeDocumentsReader.cs` | Public ingestion projection |
| `samples/AIExtensions.Sample.ChatPlayground/Documents/` | Actual extraction and ingestion UI, following other feature composition |

## Pipeline

```text
image stream / PDF stream
    -> internal image validation / PDFKit rendering
    -> RecognizeDocumentsRequestNative
    -> bounded native JSON snapshot
    -> AppleVisionRecognizeDocumentsMapper
    -> original DocumentPage / DocumentBlock / DocumentTable / DocumentTableCell
         -> ExtractAsync or ExtractPagesAsync
         -> AppleVisionRecognizeDocumentsReader (standard typed ingestion projection)
         -> original OcrDocumentReader (original full-page-text/image projection)
```

The client implements the original `IDocumentExtractionClient`, including standard service discovery and disposal.
Caller streams are never owned by the provider. PDF pages are processed sequentially with cancellation between pages.
The full-result path uses the original page-stream reducer rather than inventing a competing reduction convention.

The canonical mapper keeps native paragraph order, replacing overlapping title/table projections instead of sorting multi-column
content by geometry. Table dimensions follow logical native ranges (`lowerBound + count`); merged cells keep their actual spans.
Unsupported list/barcode/entity structure is not promoted into normalized elements.

## Native readiness

The bridge uses the exact Swift `RecognizeDocumentsRequest`, not a fictitious Objective-C `VNRecognizeDocumentsRequest`.
Text/barcode/revision controls and capability queries remain internally available with Apple terminology.
The prototype exposes only standard request options it can truthfully implement; unsupported model selection and provider-specific
option bags fail explicitly. No provisional key schema is pushed into the public contract.

Calling Apple's synthesized `Codable` directly is unsafe for framework-produced observations: the original checklist reproduced recursive
`Table.Cell.content` and `List.Item.content` projections and stack overflow. The snapshot has ancestor re-entry detection, a traversal-depth
limit, and a node-count limit. Diagnostics stay in raw JSON, not in standardized output metadata.

Native completion runs directly from the Swift task. Redispatching through `OperationQueue.current` previously deadlocked page two when
managed worker-thread code awaited the callback. The current bridge avoids that dispatch cycle and checks cancellation after serialization.

PDF rendering rejects non-finite bounds and clamps scale before integer conversion. Rendering uses the crop box, white background,
native page rotation, and bounded image dimensions. These are input-processing details, not extra document API fields.

## Playground composition

Documents uses `PlaygroundPageLayout`, the shared attachment preview, popup menus, and source-generated commands.
Its page composes an area, composer, and settings view. Services run the actual contracts independently of MAUI; the observable view model
owns input selection, status, cancellation, API/provider selection, and displayed output, matching Chat/Embeddings/Images.

The settings select:

1. **Document extraction:** Actual `IDocumentExtractionClient`, either whole-result or completed-page calls.
2. **Document reader:** The concrete `IngestionDocumentReader` projection.
3. **Upstream OCR reader:** The unchanged proposal `OcrDocumentReader` wrapping the selected extraction client.

Actual clients are registered in DI, decorated with the original builder/logging/OpenTelemetry pipeline, and described through `GetService`.
There is no provider-factory abstraction and no silent switch between local/cloud engines.
Normalized and provider-raw inspection are separate views of the actual result, not augmented metadata objects.

Azure Document Intelligence and Foundry Mistral are explicit sample-only comparisons using their existing live-tested transport/settings.
Cloud calls upload immediately when the selected operation runs; inline text discloses this, and Debug binaries embedding secrets must
not be distributed. Credentials are not committed. Neither cloud SDK is a dependency of the Apple implementation.

## Review evidence and gaps

Portable tests cover the actual proposal inventory/member shapes, standard builder/options/logging/telemetry/DI, source-content extensions,
reader behavior, native snapshot mapping, unsupported features, page progress, cancellation, and cloud protocol adapters.
Device tests execute both the concrete reader and real client against representative images/tables/lists/PDFs and the original upstream reader.

See [Apple/proposal feature gaps](apple-document-extraction-gaps.md) for all deliberately absent normalized fields and reader-adapter limitations.
The [historical feedback](apple-document-extraction-feedback.md) retains prior investigations but does not prescribe the current mapping policy.
