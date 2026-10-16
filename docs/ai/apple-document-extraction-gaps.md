# Apple Vision / corrected Extraction feature coverage

Current contract: [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) at
`1fc95b5b77b48e085b49cf25fd1b3e4797825fa9`, including its corrected shared Documents prerequisite.
See [the correction report](document-extraction-corrected-api.md) for the adoption and raw-output rules.

## Policy

Use the explicit supported API surface rather than a rewritten private model or arbitrary `Metadata` keys.
Preserve actual native information where the corrected contract has a corresponding field or intended structured extension.
Never fabricate native figure bytes, confidence, cell roles, hierarchy levels or model identities to populate a field.
Structured provider values are opaque supplied data, not a claim of standardized entity recognition.

## Restored opportunities

The corrected API can preserve exact geometry/source frames, actual confidence, merged spans/empty-cell anchors, native cell roles,
captionless images, structured extension values, request overrides and operation-scoped raw output.
These are no longer inherent contract gaps, unlike the reverted `d44c549…` slice.
The provider and tests must demonstrate that those values survive finalization and disposal boundaries.

Apple's source coordinates are normalized with bottom-left origin. A normalized `1 x 1` frame describes that coordinate space;
it is not a fabricated raster size. The implementation must not reduce rotated polygons to rectangles or infer a score from word candidates.
Merged cells retain their ranges rather than repeating the anchor content in covered positions. Apple does not provide standardized cell roles.

## Remaining genuine mismatches

| Native feature | Remaining limitation |
|---|---|
| Lists/items/nested item content and marker kinds | No first-class normalized list schema; structured extensions/annotations may preserve supplied fields without portable list semantics |
| Barcode symbologies, text/binary payloads, GS1/composite/supplemental details | No standardized barcode schema; raw/structured provider details remain explicitly provider-specific |
| Links/email/phones/addresses/calendar/money/flights/tracking/measurements/payment identifiers | No normalized detected-entity schema; no promise consumers can interpret values without provider knowledge |
| Lines/words/candidates/languages/alignment | No portable OCR text-candidate model; optional actual supporting details/raw output are not first-class text structure |
| Native request controls | Generic per-call seam is restored, but Apple keys/types and supported flags are adapter behavior, not universal options |
| Native Swift object | Bridge retains a detached bounded snapshot, not a live `DocumentObservation`; no original network response exists for this local API |
| Document-wide recognition graph | Apple containers may re-enter ancestor projections; bounded traversal is necessary and not a claim that arbitrary graphs are lossless |
| PDF facts | Labels/rotation/render DPI are internal input-layer facts; not intrinsic recognition or semantic-document properties |
| Multi-frame TIFF/HEIC | Only the first raster frame is processed; explicit PDF page iteration is supported |

`RecognizeDocumentsRequest` does not extract figure/image bytes or captions. No `DocumentExtractionImage` is invented from a screenshot crop.
Other providers may return actual image bytes without captions, and the corrected empty-Markdown contract must not drop them.

## Ingestion and operational limits

The upstream `OcrDocumentReader` remains removed. Our concrete Apple reader is a local projection over the finalized shared document.
That ingestion model cannot express all geometry/confidence/spans/structured fields; no opaque metadata is added to disguise the reduction.
Empty and covered table positions remain distinct in the rich shared model even if the ingestion matrix cannot express the full distinction.

Upstream usage/progress/client-metadata conveniences, configuration/OpenTelemetry middleware and URI/AI-content overloads remain absent.
Completed-page UI counts are presentation only. Cloud extraction may finish its remote operation before enumerating pages.
Caller input stays caller-owned; rich results/documents and owned original-response streams are disposed after use.

## Review boundary

This is a non-shipping, non-packable prototype with exact copied source. Public/internal release decisions and official package
dependencies are deferred until provider acceptance. Missing native facts remain absent; missing standardized schemas remain documented.
