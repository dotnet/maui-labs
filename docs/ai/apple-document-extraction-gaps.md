# Apple Vision and the current document contract

This non-shipping prototype uses the exact source of [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588)
at `d44c549301d08119f86b73f3fcf58c8b65a2981e`, including its shared Documents prerequisite.
See [the upstream change breakdown](document-extraction-upstream-refresh.md) for the breaking changes from our previous snapshot.

## Intersection policy

Normalize only actual native features that match the proposed contract. Do not add provider-specific metadata, hide raw payloads inside
opaque references/evidence/extensions/artifacts, infer confidence or table roles, crop figures, or silently switch providers.
Unsupported native features remain internal or absent and are documented below. Copied Microsoft source stays unchanged.

`AppleVisionRecognizeDocumentsClient` implements the single page operation. The core finalizer creates an owned shared Documents document.
`AppleVisionRecognizeDocumentsReader` projects that canonical document into existing DataIngestion elements and disposes the extraction result.
Native image/PDF processing remains shared; there is no second raw-to-ingestion interpretation.

## Current capability matrix

| Apple or proposal feature | Treatment |
|---|---|
| Image/PDF input | PNG, JPEG, HEIC, TIFF and PDF; caller retains stream ownership |
| Identity | Caller identifier flows through extraction and finalized document; provider observation IDs are unique within the extraction |
| Page processing | PDFKit renders actual PDF pages internally; page sequence is native processing, not a claim of incremental network OCR |
| Text/title | Actual text observations; titles become shared header elements without invented hierarchy levels |
| Paragraph order | Native paragraph sequence, replacing duplicate title/table projections without sorting columns spatially |
| Tables | Actual grid positions and cell content represented by addressed cells |
| Merged cells | Anchor content remains at the actual address; covered positions stay empty. Row/column span semantics are not represented by the new contract |
| Nested cell structure | Only what the single-content-observation cell shape can faithfully hold; native flattened cell text is available without inventing a hierarchy |
| Markdown | Exact provider Markdown only when actually supplied. Apple text is not relabeled as original Markdown; semantic rendering is distinct |
| Finalized content | Original shared Documents hierarchy and ownership, not a local result model |
| Cancellation | Propagated through input reading, native task completion, page enumeration, and finalization |
| Resources | Input remains caller-owned; finalized result/document is disposed after use; no artificial artifact streams are created |
| Evidence/page references | Left absent unless there is a genuine compatible opaque source reference; no geometry/raw-data encoding workaround |
| Builders/DI/logging | Actual current upstream middleware, including lifecycle-only logging; no removed options or OpenTelemetry facade |

## Apple capabilities no longer modeled by the proposal

These are genuine Apple features, but no matching typed normalized surface exists in the selected contract:

- Bounding polygons, coordinate origins/units, source image dimensions and confidence.
- List-item hierarchy/markers and structured barcode fields/payloads/GS1 flags.
- Detected links, email, telephone, addresses, calendar events, money, flights and measurements.
- Line/word candidates, their geometry/confidence, recognition languages and text alignment.
- Observation identity/confidence, recursion diagnostics and native raw snapshots.
- PDF labels, rotation and rendering facts.
- Request revision, recognition languages/custom words/correction, barcode controls and region of interest.

No provider key schema is added to compensate. Opaque extension observations can carry honest stored text under the upstream rules,
but are not a general raw JSON or metadata transport. These distinctions must be reviewed before claiming full native feature coverage.

## Proposal features Apple does not supply

`RecognizeDocumentsRequest` does not extract figure bytes/captions or return document-owned artifact streams.
No image or artifact is synthesized. Source Markdown is not returned by this request; generated semantic display text is not labeled
as original provider Markdown. No model/deployment override or token/billing usage is invented.

The current cell model lacks spans and role classification. The provider does not repeat merged content, guess header rows, or use
opaque evidence to approximate those missing semantics. Shared Documents can represent richer element ownership, but does not make
unavailable Apple data real.

## Input and native limitations

Only the first image of a multi-frame TIFF/HEIC input is processed; iterating multi-image containers is a separate input-layer capability.
PDF page iteration is supported explicitly. No embedded-PDF-text fallback or second OCR engine is used.

Apple's synthesized Codable graph can recurse through real `Table.Cell.content`/`List.Item.content` projections.
The internal bridge retains bounded traversal and ancestor re-entry detection. No normalized property bag is created for diagnostics.
Direct completion avoids the previous worker-thread page-two deadlock.

## DataIngestion bridge

The proposal deleted its original `OcrDocumentReader` and explicitly includes no DataIngestion migration.
The retained Apple reader is a local adapter over the new canonical shared document, not a copied upstream bridge.
The sample's reader comparison is separate from extraction finalization so reduced ingestion fidelity is visible.
Artifact ownership does not transfer silently into DataIngestion objects.

## Release status

This is an API experiment, not a shipping package. All copied projects and the prototype library remain non-shipping/non-packable.
Public/internal visibility and official package references are later release decisions; no private rewritten contract is maintained.
