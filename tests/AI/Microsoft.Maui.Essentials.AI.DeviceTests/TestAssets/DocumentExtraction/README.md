# Synthetic document extraction corpus

These synthetic source documents evaluate how Apple Vision represents common
document features inside `DocumentObservation.Container`. They contain no
customer documents or credentials. Contact details and barcode payloads are
fixture content, not test-service configuration.

[`source-content.md`](source-content.md) is the human-readable Markdown source
for the scenarios. The SVG files provide deterministic typography and geometry
for the committed PNG fixtures.

| Fixture | Purpose |
|---|---|
| `headings-and-paragraphs` | Title, section heading, and multi-line paragraph grouping |
| `flat-list` | A paragraph followed by a three-item list and another paragraph |
| `single-item-list` | A one-item list whose item and list regions may coincide |
| `nested-list` | Two top-level items with visibly indented child items |
| `table` | A regular three-column table with headers and body rows |
| `list-in-table` | Checklist bullets contained in separate table cells |
| `detected-data` | URL, email, phone, address, date/time, and currency entities |
| `barcodes` | QR and Code 128 symbols with known payloads |
| `mixed-document` | Title, link, table, list, and footer in one page |

The corresponding PNG files are generated from the SVG sources with:

```bash
for source in tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction/*.svg; do
  name="$(basename "$source" .svg)"
  rsvg-convert "$source" \
    --output "tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Resources/Raw/DocumentExtraction/$name.png"
done
```

`barcodes.png` is generated separately with:

```bash
swift tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/TestAssets/DocumentExtraction/generate-barcodes.swift \
  tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Resources/Raw/DocumentExtraction/barcodes.png
```

The PNG files are packaged into the device-test app and opened through
`FileSystem.OpenAppPackageFileAsync`.

Regeneration requires `rsvg-convert` from `librsvg`; barcode generation requires
macOS with Swift, AppKit, and Core Image.

## Run

```bash
dotnet test \
  tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst \
  --filter 'FullyQualifiedName~AppleVisionDocumentCorpusTests|FullyQualifiedName~AppleVisionListStructureTests'
```

Each Vision test writes a compact native/normalized model summary into the
DeviceRunners result event. Tests exercise native recognition separately from
the managed projection.

## Interpreting results

Fixture names describe source layout, not guaranteed Vision classifications.
The committed images use a controlled 1600 x 1200 layout; recognition can vary
with request revision and OS.

| Scenario | What to check |
|----------|---------------|
| Headings and paragraphs | Readable title/body text and source geometry; do not infer heading levels |
| Tables | Cell text, row/column addresses, spans, and geometry in extraction output |
| Detected data and barcodes | Native types/payloads where recognized; no normalized entity/barcode schema is promised |
| Mixed document | Coexistence of text and table content; do not assume cross-collection reading order |
| Lists | Native classification and self-projection safety, separately from normalized text |

Revision-1 list behavior motivates the fixtures:

- A multi-item list can expose duplicated paragraphs and same-polygon nested
  lists whose descendants re-enter the owning item's semantic container.
- Visually indented bullets and hyphens can be returned as one flat list;
  marker and geometry differences do not guarantee parent/child hierarchy.
- A standalone single bullet or bullets inside table cells may be text only,
  not `DocumentObservation.Container.List`.

The mapper projects supported text without duplicating exact self-projections.
List markers, detection details, and confidence remain native; they are not
custom normalized blocks or metadata schemas. Ancestor detection and depth/node
bounds protect native snapshot traversal. Tests should distinguish traversal
safety from a claim that arbitrary native graphs are completely captured.

Barcode assertions run on Mac Catalyst and physical iOS devices. The iOS
simulator case returns early because Vision barcode detection is not reliably
available there; that is not evidence that the fixture has no barcode.

When a new OS/request revision changes recognition, inspect both native and
normalized output before changing expectations. Genuine native improvements,
such as nested lists or removal of self-projections, should prompt a deliberate
review rather than preserving obsolete output shapes.

See the [extraction guide](../../../../../docs/ai/apple-document-extraction.md)
for mapping boundaries and native safety.
