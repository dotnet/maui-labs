# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions)
and [`Microsoft.Extensions.DataIngestion`](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)
- **Document ingestion** — `AppleVisionDocumentReader` maps images and PDFs to standard ingestion sections, headers,
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
IngestionDocumentReader reader = new AppleVisionDocumentReader();
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
A future official `IDocumentExtractionClient` is the likely home for these typed capabilities and provider escape hatches.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
