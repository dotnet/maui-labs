# Document extraction fixtures

Synthetic documents for the Apple Vision document extraction device tests. They
contain no real customer data; contact details and barcode payloads are made up.

The rendered files live in `Resources/Raw/DocumentExtraction/` and are packaged
into the device-test app, where tests open them with
`FileSystem.OpenAppPackageFileAsync`.

| Fixture | Content |
|---------|---------|
| `headings-and-paragraphs` | Title, section heading, and a multi-line paragraph |
| `flat-list` | A paragraph, a three-item list, and another paragraph |
| `single-item-list` | A one-item list |
| `nested-list` | Two top-level items with indented child items |
| `table` | A three-column table with a header row |
| `list-in-table` | Checklist bullets inside table cells |
| `detected-data` | URL, email, phone, address, date/time, and currency |
| `barcodes` | QR and Code 128 symbols |
| `mixed-document` | Title, link, table, list, and footer on one page |

`source-content.md` has the text of each fixture and `manifest.json` lists the
text each one contains. Fixture names describe the layout, not how Vision
classifies it. With request revision 1, for example:

- a multi-item list can contain a copy of the same list inside its own items;
- indented bullets can come back as one flat list;
- a single bullet, or bullets inside table cells, can come back as plain text.

## Regenerating the images

Requires `rsvg-convert` (from `librsvg`) and, for barcodes, macOS with Swift.

```bash
dir=tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests
for source in $dir/TestAssets/DocumentExtraction/*.svg; do
  rsvg-convert "$source" --output "$dir/Resources/Raw/DocumentExtraction/$(basename "$source" .svg).png"
done
swift $dir/TestAssets/DocumentExtraction/generate-barcodes.swift \
  $dir/Resources/Raw/DocumentExtraction/barcodes.png
```

## Running the tests

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -p:TargetFrameworks=net10.0-maccatalyst \
  --filter FullyQualifiedName~AppleVisionRecognizeDocumentsClientTests
```
