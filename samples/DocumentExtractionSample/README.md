# Apple Document Extraction Sample

This sample exercises the experimental `Microsoft.Extensions.DocumentExtraction`
API with the native Apple implementations in `Microsoft.Maui.Essentials.AI`.

## Features

- Image extraction with `AppleVisionRecognizeDocumentsClient`
- PDF page rendering with `ApplePdfKitRenderingExtractionClient`
- Paragraphs, tables, nested cells, lists, list items, and barcodes
- Polygon overlays for extracted image elements
- Apple Vision capability discovery
- Normalized result JSON and raw Apple observation JSON
- Cancellation and per-page PDF progress
- VisionKit document-camera capture on iOS and Mac Catalyst
- DevFlow inspection and logs in Debug builds

The sample does not fall back to another OCR engine. Apple Vision document
recognition requires iOS 26, Mac Catalyst 26, or macOS 26.

## Run

```bash
dotnet run --project samples/DocumentExtractionSample/DocumentExtractionSample.csproj \
  -f net10.0-maccatalyst
```

The native AppKit variant uses the experimental MAUI macOS backend:

```bash
dotnet run --project samples/DocumentExtractionSample.MacOS/DocumentExtractionSample.MacOS.csproj \
  -p:ValidateXcodeVersion=false
```

`ValidateXcodeVersion=false` is needed only when the locally installed Xcode is
newer than the exact version required by the installed .NET macOS workload.

## Inspect with DevFlow

The sample uses agent port `9240` by default. Override it with
`DEVFLOW_TEST_PORT` when launching the app.

```bash
maui devflow list
maui devflow ui status --agent-port 9240
maui devflow ui tree --agent-port 9240
maui devflow logs --agent-port 9240
```

When Vision prunes a repeated recursive container, the status label reports the
count and DevFlow logs include the page, node count, maximum depth, and first
ancestor/re-entry paths.
