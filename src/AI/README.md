# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions)
and [`Microsoft.Extensions.DataIngestion`](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)
- **Document ingestion** — `AppleVisionRecognizeDocumentsReader` maps images and PDFs to standard ingestion sections, headers,
  paragraphs, and tables for RAG on iOS/Mac Catalyst/macOS 26+

### Platform Support

| Platform | Chat (`IChatClient`) | Embeddings (`IEmbeddingGenerator`) | Document reading (`IngestionDocumentReader`) |
|----------|----------------------|--------------------------------------|-----------------------------------------------|
| iOS 26+ | Apple Intelligence | NL Embeddings | Apple Vision |
| Mac Catalyst 26+ | Apple Intelligence | NL Embeddings | Apple Vision |
| macOS 26+ | Apple Intelligence | NL Embeddings | Apple Vision |
| Android | Coming soon | Coming soon | Not supported |
| Windows | Coming soon | Coming soon | Not supported |

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Maui.Essentials.AI;

// Register in MauiProgram.cs
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());

// Use via DI
var client = serviceProvider.GetRequiredService<IChatClient>();
var response = await client.GetResponseAsync("Plan a weekend trip to Portland");

// On iOS/Mac Catalyst/macOS 26+, read images or PDFs into ingestion documents.
IngestionDocumentReader reader = new AppleVisionRecognizeDocumentsReader();
var document = await reader.ReadAsync(new FileInfo("receipt.pdf"), "receipt-123");
```

## Packages

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Essentials.AI` | On-device AI APIs for MAUI |

## Building

```bash
# macOS (builds Swift bindings + .NET library)
dotnet build src/AI/EssentialsAI.slnf

# Windows (CI only — the official pipeline downloads macOS-built
# native artifacts automatically. Local Windows builds require CI=true
# or TF_BUILD=true for the pre-built artifact path to activate.)
```

The CI pipeline handles the macOS → Windows artifact flow automatically. See `.github/workflows/ci-essentialsai.yml` for details.

### Xcode 27 builds

CI builds the native Swift library with Xcode 27 while keeping the package's normal Apple target frameworks unchanged.
To opt into Apple 27 reference packs locally, install .NET 10.0.401 and matching workloads, select Xcode 27 with
`DEVELOPER_DIR`, and build with `-p:UseXcode27Preview=true -f net10.0-maccatalyst27.0` (or `ios27.0` / `macos27.0`).
`UseXcode27Preview` only changes the target-framework list; it does not install or select Xcode. For normal Apple 26.x
reference packs with Xcode 27, the native CI job passes `ValidateXcodeVersion=false` to bypass the older version check.

### Document reader limitations

The reader deliberately omits geometry, confidence, typed barcodes/entities/lists, request options, raw observations,
and progress/streaming. List text maps to paragraphs; no provider metadata or native result types are public.
The internal client implements the required subset of the proposed `IDocumentExtractionClient` contract;
neither that client nor its models are public.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **Shared document recognition** — internal `AppleVisionRecognizeDocumentsProcessor` owns the full native snapshot, request
  controls, capabilities, image orientation, PDF page rendering, and cancellation.
  `RecognizeDocumentsRequestNative` is the single native request wrapper; `RecognizeDocumentsRequestSnapshotNative`
  carries its JSON snapshot rather than an Apple observation.
- **Transitional document extraction** — `AppleVisionRecognizeDocumentsReader` consumes the internal
  `AppleVisionRecognizeDocumentsClient` page stream with default options and projects typed pages into ingestion.
  The client's portable canonical mapper is the only snapshot interpreter. It validates hierarchy, geometry, table
  ranges/spans, and bounded logical grids while retaining list containers/items and nested cell content.
  Observation groups isolate local paths; the mapper creates a reading-order sequence from the original root paragraphs
  (including column order), replacing overlapping titles, tables, and list items while retaining the full source hierarchy.
  The ingestion projection only converts these ordered rich elements to standard ingestion types; it never reads raw JSON.
  The internal result retains cloned page/observation/node snapshots, normalized bottom-left geometry, confidence,
  source dimensions, PDF facts, page totals/progress, and request controls/capabilities. Provider property bags retain
  native field names for list markers, barcode data/flags, detected data, languages, candidates, words, and diagnostics.
  `Internal.DocumentExtraction` contains 19 internal normalized types adapted from
  [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), pinned to
  [`a215825ae2c96723e922e068c226ff77122c7c94`](https://github.com/luisquintanilla/extensions/tree/a215825ae2c96723e922e068c226ff77122c7c94/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions).
  This is only the required subset, not an external namespace, dependency, assembly, or public API.
  It follows the pinned constructors, nullable property bags/usage, float geometry, open string kinds, standalone
  table-cell `Content`/nested elements, client services/disposal, and direct page-result progress members.
  Apple request controls live under `apple.vision.*` in options' additional properties; validation snapshots their arrays
  before asynchronous recognition, while the normalized options' `Clone()` intentionally retains shallow values.
  Provider-specific `AppleVisionRecognizeDocumentsObservationSnapshot`/node companions preserve the complete tree,
  local paths, parents, nested lists, raw data, and native-double bounds used for unchanged `.7`/`.55`/`.85` ordering decisions.
  Pages retain observations under `apple.vision.observations` and the typed `AppleVisionRecognizeDocumentsPdfPageInfo`
  under `apple.pdf.pageInfo`, alongside existing PDF facts; none are invented normalized members.
  Adoption of the eventual official package will require dependency/API compatibility review and migration of these
  provider companions, not simply changing visibility. No image model or middleware/DI infrastructure is copied.
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

Portable document mapping, projection, and client orchestration tests access the shipped assembly through its friend
assembly declaration, not linked copies. The portable option validator creates the same request-support DTO passed
by the Apple-only wiring to the processor.

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
