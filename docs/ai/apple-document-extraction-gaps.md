# Apple Vision / merged Extraction feature coverage

Current contract: [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), merged November 5.
This checkout pins main `f8ee27151b21e33793164b39de5bcebfdced81a4` while official packages await publication.
The [adoption report](document-extraction-corrected-api.md) also preserves the earlier experiments as history.

## Policy and supported intersection

Use the real abstract `DocumentExtractionClient` and `ExtractionDocument` types, not a private substitute or metadata-based compatibility
layer. The final model retains text/image payloads, local content, source frames/polygons, text roles, merged table spans and empty
anchors. Cells can contain a section of ordered mixed content. Producers explicitly add source pages, including blank pages.
Native observations remain borrowed supporting data, not semantic payloads or automatic log/embedding input.

Apple's normalized `1 x 1` frame with bottom-left origin describes coordinates, not raster size. Preserve double vertex order and
rotated polygons. Do not invent figure crops/captions, standard table roles, heading levels, confidence or model identity.

## Genuine mismatches

| Native feature / earlier experiment | Final representation or limitation |
|---|---|
| Confidence and OCR candidates | No normalized confidence/annotation fields; inspect the native node/bridge snapshot |
| Lists/items/nesting/marker kinds | No first-class list or generic extension envelope; supported text is projected once, details remain native |
| Barcode text/binary payload, GS1/composite details | No normalized barcode schema; native inspection, not opaque metadata on text |
| Detected addresses/links/email/calendar/money/flights/tracking etc. | No normalized detected-entity schema; retained native details are not universal semantic values |
| Words/lines/alignment/languages | No general OCR hierarchy or standardized details; inspect native observations |
| Per-page completion/progress | Only completed document extraction is public; PDF iteration stays private |
| Capture opt-out/native-body statuses | Removed; raw access is optional and borrowed, with no status or raw-body ownership contract |
| Original HTTP response body | No standard retention API; parsed cloud results are not mislabeled as original bytes |
| Raw Apple representation | Detached bounded JSON bridge snapshot, not a live Swift `DocumentObservation`; no HTTP body exists |
| Arbitrary recursive Apple graph | Native list/table projections can re-enter ancestors; depth/node limits prevent unsafe recursion |
| PDF label/rotation/render DPI | Internal input-rendering facts, not intrinsic recognition properties |
| Multi-frame TIFF/HEIC | Only first raster frame; PDF page iteration is explicit |
| Structural Markdown rendering | Removed upstream; ingestion must perform an explicitly local projection |

`RecognizeDocumentsRequest` does not return figure image bytes or captions. Other providers may supply actual captionless bytes,
which are retained in `DataContent`; image OCR text and `AlternativeText` are separate and neither is invented.

## Ingestion, lifetime and release

The concrete Apple reader maps the same exchange document to DataIngestion. That narrower model cannot carry all source geometry,
spans or native structures, and no opaque metadata is inserted to disguise this reduction. The original source frames and empty
anchors remain available from the extraction client. Covered positions are derived from actual anchor spans, not duplicated cells.

Caller inputs remain caller-owned; exchange documents are non-disposable and native representations are borrowed. Providers must
detach reusable SDK buffers/JSON before disposing their parsers or responses. There is no client last-response state.
Builders, delegation, DI and lifecycle logging remain. Usage, telemetry/configuration middleware, client metadata conveniences,
URI/AI-content input overloads and the upstream OCR reader are not reintroduced.

This remains a non-shipping, non-packable source bridge. When packages are published, replace local project references with official
centrally versioned packages and remove the 21 copied artifacts and temporary adapters without changing provider semantics.
