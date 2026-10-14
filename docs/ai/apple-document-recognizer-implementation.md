# Apple RecognizeDocuments prototype

**Status:** Non-shipping experiment in #450.
**Native API:** Vision `RecognizeDocumentsRequest`.
**Platforms:** iOS, Mac Catalyst and macOS 26+.
**Proposal:** [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) at
`d44c549301d08119f86b73f3fcf58c8b65a2981e`.

## Current architecture

```text
image / PDF stream (caller-owned)
  -> AppleVisionRecognizeDocumentsProcessor
  -> internal RecognizeDocumentsRequestNative / bounded snapshot
  -> supported DocumentExtractionObservation values
  -> DocumentExtractionPage sequence
  -> original DocumentExtractionDocumentBuilder / ExtractAsync extension
  -> owned Microsoft.Extensions.Documents.Document
       -> playground inspection
       -> local AppleVisionRecognizeDocumentsReader projection to DataIngestion
```

Extraction no longer owns a parallel semantic tree. Providers implement one operation with identifier/media type/cancellation.
The extension consumes the same operation and finalizes a disposable result/document. The concrete reader disposes that result after
projecting supported canonical elements. Opaque references/evidence/artifacts are not used to smuggle native payloads.

## Source and build

The complete current Documents, Extraction.Abstractions and Extraction projects are preserved byte-for-byte under
`src/Libraries/DocumentExtraction/Upstream/`; the manifest records 28 files and their exact source hashes.
Local sibling projects file-link those files and supply only repository build adapters/shared guards.

The obsolete extraction-owned geometry/options/usage/metadata/tree, configuration/OpenTelemetry middleware and original OCR reader
were removed upstream and are not locally preserved as compatibility APIs. Native rendering/recognition implementation remains independent.
No copied project or prototype library is packable or shipping.

## Apple mapping and limitations

Native text and titles map to shared text/header observations. Actual table grid addresses and content map to table observations.
Native paragraph order and supported duplicate-structure replacement are retained. Unsupported merged-span semantics, geometry,
confidence, native request options, detailed barcodes/entities/words and raw-provider payloads are left absent rather than forced.
No image crops, artifacts, model IDs, header levels, table roles, cloud fallback or estimated usage are invented.

Provider observation IDs are extraction-local, deterministic and unique across pages/observations, because the upstream finalizer
validates identity globally. Stored provider Markdown and semantic rendering are distinct; native text is not evidence of original Markdown.

The Swift snapshot still needs bounded traversal: directly encoding Apple's Codable graph caused recursive list/table projections on
real framework output. Native completion runs directly from the task to avoid the previous worker-thread PDF callback deadlock.
Cancellation, orientation and finite/bounded PDF rendering remain unchanged.

## Playground

Documents follows the existing page/area/composer/settings and services/view-model conventions, with direct registrations of actual
reader/client abstractions. The latest rebased Chat diagnostics, image input and native token usage remain intact.

Extraction offers completed-page enumeration or finalization through the upstream extension. The reader mode compares local ingestion
adapters. There is no misleading option for the upstream `OcrDocumentReader`, which no longer exists.
No removed options, provider metadata or OpenTelemetry surface is reimplemented.
Cloud uploads remain immediate, explicit, disclosed selections; no credentials are committed and Debug binaries are not distributable.

## Review references

- [What changed upstream](document-extraction-upstream-refresh.md)
- [Apple/proposal feature gaps](apple-document-extraction-gaps.md)
- [Historical investigation](apple-document-extraction-feedback.md)

Historical tests/documentation describing the old proposal apply only to the archived snapshot, not this contract.
