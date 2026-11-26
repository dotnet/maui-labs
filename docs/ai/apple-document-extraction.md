# Apple document extraction

`AppleVisionRecognizeDocumentsClient` implements `DocumentExtractionClient`
from `Microsoft.Extensions.DocumentExtraction` using Apple Vision's
`RecognizeDocumentsRequest`. It runs on-device on iOS, Mac Catalyst, and
macOS 26+ and does not require Apple Intelligence.

> **Experimental:** the extraction contracts and this client may change.

For usage, see the
[package README](../../src/AI/Microsoft.Maui.Essentials.AI/README.md#document-extraction).
For build and test commands, see the [contributor guide](../../src/AI/README.md).

## How it works

```text
image or PDF stream (caller-owned)
  -> Platform/MaciOS processor: image orientation, or PDFKit page rendering
  -> Swift bridge: RecognizeDocumentsRequest, observation -> JSON snapshot
  -> DocumentExtraction/ mapper: snapshot -> ExtractionDocument
```

`ExtractAsync` returns one completed `ExtractionDocument`. PDFs are processed
page by page internally; there is no per-page streaming or progress API.
`ExtractionDocument.RawRepresentation` is a `JsonElement[]` with one native
snapshot per page, and each text element's `TextContent.RawRepresentation` is
the snapshot node it came from.

## Inputs and options

- **PDF:** each page is rendered with PDFKit at 200 DPI, capped at 4096 pixels
  on the longest side.
- **JPEG, PNG, HEIC, TIFF:** image orientation is honored. Only the first frame
  of a multi-frame file is used.
- The input stream is not disposed. Cancellation is checked between pages and
  passed to the native request.
- `DocumentExtractionOptions.ExtractorName` is not supported and throws
  `NotSupportedException`.
- `AdditionalProperties` accepts these `apple.vision.*` keys, mapped to the
  matching `RecognizeDocumentsRequest` properties:

  | Key | Type |
  |-----|------|
  | `apple.vision.recognitionLanguages` | `string[]` |
  | `apple.vision.customWords` | `string[]` |
  | `apple.vision.useLanguageCorrection` | `bool` |
  | `apple.vision.automaticallyDetectLanguage` | `bool` |
  | `apple.vision.maximumCandidateCount` | `int` (1–10) |
  | `apple.vision.minimumTextHeightFraction` | `float` (0–1) |
  | `apple.vision.barcodeDetectionEnabled` | `bool` (default `true`; Vision's own default is `false`) |
  | `apple.vision.barcodeSymbologies` | `string[]` |
  | `apple.vision.coalesceCompositeSymbologies` | `bool` |
  | `apple.vision.regionOfInterest` | `float[]` `[x, y, width, height]`, normalized |
  | `apple.vision.revision` | `int` |

  Unknown keys throw `NotSupportedException`; wrong types or out-of-range
  values throw `ArgumentException`.

## What maps to the shared model

| Vision output | Shared representation |
|---------------|-----------------------|
| Titles and paragraphs | Text elements; titles use the `heading` role |
| Pages | One page per input page, including blank pages, with its coordinate frame |
| Bounding regions | Polygons with the original vertex order, as doubles |
| Tables | Table elements with row/column spans, empty anchor cells, and mixed cell content |
| Lists | One text element per list, rendered with markers; item structure stays in the native snapshot |
| Confidence, OCR candidates, words, lines, languages | Native snapshot only |
| Barcodes | A text element with the payload string; symbology stays in the native snapshot |
| Detected data (dates, money, links, …) | Native snapshot only |
| Figures and captions | Not produced by this request |

Vision coordinates are a normalized `1 x 1` space with a bottom-left origin,
not pixels.

## Native snapshot

The Swift bridge serializes observations to JSON instead of returning Swift
objects. It walks list and table containers itself rather than calling
`Codable` on the whole `DocumentObservation`, because Vision can return list
items whose content re-enters an ancestor container; encoding that graph
recursively overflows the stack.

- **Cycle handling:** a node is skipped when its text, title, polygon, and
  child counts match an ancestor. Genuinely nested identical containers are
  skipped too. Traversal also stops at depth 64 or 20,000 nodes. The snapshot's
  diagnostics report truncation and skipped re-entries.
- **List items:** the mapper drops a list item's copy of itself (same text,
  polygon, and marker) and keeps different nested content.
- **Enum values** (barcode symbology, list marker, alignment, composite type)
  use the case name from Apple's own `JSONEncoder` output, so new cases need no
  bridge changes. For example, `{"qr": {}}` becomes `"qr"`.
- **Detected data details:** on OS 27+, the bridge stores Apple's encoded
  `semanticDetails` JSON unchanged under `detectedData[].details`, for example:

  ```json
  {"details":{"moneyAmount":{"_0":{"amount":12.5,"currency":"USD"}}}}
  ```

  DataDetection types are not `Encodable` on OS 26, so the bridge maps known
  cases to a flatter shape there. Code that reads native details must handle
  both shapes.

## Gaps in the shared contract

Data Vision provides that the `Microsoft.Extensions.Documents` model has no
place for, so it stays in the native snapshot:

- **Lists:** no list or list-item type to carry markers and nesting.
- **Text-level detail:** no way to attach confidence, alternate candidates,
  line/word geometry, or detected entities to a span of text.
- **Reading order:** Vision returns paragraphs, tables, lists, and barcodes as
  separate collections. The mapper follows paragraph order, substituting a
  table, list, or title where a paragraph overlaps it, then appends the rest.
  Apple does not document an order across collections.
- **Truncation:** no standard signal that a provider returned partial results.
- **Feature toggles:** `DocumentExtractionOptions` has no portable switch for
  optional recognizers such as barcodes, so this client enables barcode
  detection by default and exposes the opt-out only as a provider key.

## Playground

The [AI playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
**Documents** tab runs any registered `DocumentExtractionClient` against a
picked file. It registers this client and, when configured, Foundry Mistral
through `CommunityToolkit.DocumentExtraction.Mistral`.

## Test fixtures

The [document fixtures](../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction/README.md)
cover headings, lists, tables, detected data, and barcodes.
