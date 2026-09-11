# Document extraction list fixtures

These source documents evaluate how Apple Vision represents common document
features inside `DocumentObservation.Container`.

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

Each Vision test writes a compact raw/normalized model summary into the
DeviceRunners result event. See [RESULTS.md](RESULTS.md) for the current
revision-1 baseline and interpretation.
