# Apple Vision and the proposed document APIs: supported intersection

This prototype uses the original `Microsoft.Extensions.DocumentExtraction` APIs from
[dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), pinned at `a215825ae2c96723e922e068c226ff77122c7c94`.
Both proposed projects are copied verbatim, file-linked, and non-shipping. There is no private substitute contract.

The provider is `AppleVisionRecognizeDocumentsClient`, backed by Apple's `RecognizeDocumentsRequest`. The reader is
`AppleVisionRecognizeDocumentsReader`; the playground also executes the unchanged upstream `OcrDocumentReader`.
All three paths share one native request implementation. None falls back to another provider.

## Mapping policy

Use a normalized property only when its meaning matches a feature actually returned by Apple. Leave unavailable properties null.
Do not infer confidence, crop figures, guess table roles, encode barcodes as invented block kinds, or attach Apple-only objects through
`AdditionalProperties`. Results, pages, blocks, cells, and page updates have no provider-specific property bags in the Apple provider.

The contract's explicit `RawRepresentation` slot contains a cloned, bounded native JSON snapshot. It is not a second normalized model.
Unsupported native detail can be inspected there, but is not promoted into standardized fields. The native snapshot cannot use Apple's
recursive Codable graph safely; finite traversal and ancestor re-entry checks are retained to prevent stack overflow on framework-produced
list/table content. This limitation and any pruned structure are recorded in the snapshot itself, not in normalized metadata.

## Supported normalized features

| Proposed feature | Apple implementation |
|---|---|
| Stream input | PNG, JPEG, HEIC, TIFF, and PDF; caller retains ownership |
| Pages and page text | Native document transcripts; PDFKit renders one PDF page at a time internally |
| Text blocks | Native titles and paragraphs, using the built-in `Title` and `Paragraph` kinds |
| Reading order | Native paragraph sequence, replacing overlapping title/table projections instead of sorting columns spatially |
| Tables and cells | Actual row/column ranges, spans, content, and supported nested text/table content |
| Cell roles | Left null; Apple does not supply the proposal's header/content role classification |
| Regions | Actual native polygons; normalized units and bottom-left origin, with normalized dimensions `1 x 1` |
| Confidence | Only a confidence value present on the actual source node; no promotion of observation or word confidence to paragraph confidence |
| Page streaming | One completed page update per recognized image/PDF page; no invented token or partial-word streaming |
| Progress | Pages processed and known total page count |
| Client metadata | Standard provider metadata and service discovery; no invented Apple model/deployment identity |
| Cancellation | Passed through input loading and native task cancellation; no callback redispatch onto a blocked operation queue |
| Builders and middleware | Original configuration, logging, OpenTelemetry, keyed registration, and delegating client implementations |

## Apple features without matching normalized proposal fields

| Apple capability | Current treatment |
|---|---|
| Typed lists, item hierarchy, marker strings/types | Not normalized as custom `list`/`listItem` block kinds; ordinary native paragraph text remains text |
| Barcode symbology, payload bytes, GS1/composite flags | No normalized barcode element; kept only in the explicit native snapshot |
| Detected links, email, telephone, addresses, calendar events, money, flights, measurements | No normalized entity/field model; not injected into blocks or metadata |
| Text lines, words, alternative candidates and their individual geometry/confidence | No matching normalized element types; not approximated as paragraphs |
| Recognition languages and text alignment | No normalized output properties; not added to `AdditionalProperties` |
| Observation UUID/confidence and repeated-container diagnostics | Snapshot detail, not copied onto page or block metadata |
| PDF labels, rotation, render DPI, source pixel size | Internal input-processing facts, not fabricated normalized document fields |
| Request revision, recognition languages/custom words/correction, barcode controls, region of interest | Native bridge remains capable, but the prototype exposes no custom request keys |

The generic `DocumentExtractionOptions` model remains exactly as proposed. `ModelId` is rejected by the Apple provider because
`RecognizeDocumentsRequest` does not expose model selection. Nonempty provider-specific option bags are explicitly rejected rather than
silently ignored. Native request controls can be considered later through the proposal's extensibility mechanism, but no key schema is
invented in this pass.

Image inputs represent one image observation, including the first frame of a multi-frame TIFF/HEIC container. Only PDFKit's real PDF page
sequence is exposed as multi-page input in this prototype. Multi-image container iteration remains an input-layer gap, not an invented
Vision document capability.

## Proposed features Apple does not provide

`DocumentImage` is left absent: this request does not return extracted figure/image bytes or captions. There is no crop/LLM approximation.
No image-caption block is synthesized. Token usage, billing usage, model/deployment override, and provider-grade table-cell roles are absent.
`DocumentTable.MarkdownRepresentation` stays null when Apple returned structured cells; ingestion Markdown is a presentation projection of
those real cells, not claimed as an Apple output.

## Ingestion differences worth reviewing upstream

The custom reader projects standardized title/paragraph/table elements into standard ingestion headers, paragraphs, and table grids.
Merged covered positions remain null. It adds no ingestion metadata.

The unmodified upstream `OcrDocumentReader` calls `ExtractAsync`, emits each page's full text as one paragraph, and copies extracted images.
It currently does not map title blocks or structured tables/cells, and does not set the page number on the section itself. Those are observable
proposal limitations, not locally patched behavior. The playground offers it separately so the team can compare all actual output shapes.

Raw page snapshots survive on `DocumentPage.RawRepresentation`. The original streaming reducer does not copy a page update's raw object
onto the assembled result's top-level raw slot; the provider does not change the reducer to hide this distinction.
The proposal marks page/element raw properties as ignored for JSON, but not the result's top-level raw property. The playground's
normalized-inspection serializer excludes that property locally and offers its contents in the separate raw view; the copied contract
and the API's own serializer behavior are not changed.

## Scope and release boundary

The public provider and reader are experimental prototype surfaces. Copied proposal assemblies and the prototype library cannot be packed
or shipped. First validate the complete API and provider intersection; decide internal/public visibility and release dependencies afterward.
No provider gap is resolved by modifying copied Microsoft source, inventing a normalized field, or forcing missing Apple capabilities.
