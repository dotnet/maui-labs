# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)
- **Document extraction** — structured on-device recognition through Apple Vision, including paragraphs, tables, nested cells, lists, and barcodes
- **PDF extraction** — explicit PDFKit page rendering composed with the Apple Vision document client

### Platform Support

| Platform | Chat (`IChatClient`) | Embeddings (`IEmbeddingGenerator`) | Documents (`IDocumentExtractionClient`) |
|----------|----------------------|------------------------------------|-----------------------------------------|
| iOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| Android | 🔜 Coming soon | 🔜 Coming soon | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon | 🔜 Coming soon |

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

// Register in MauiProgram.cs
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());

// Use via DI
var client = serviceProvider.GetRequiredService<IChatClient>();
var response = await client.GetResponseAsync("Plan a weekend trip to Portland");
```

```csharp
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;

using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
await using var image = File.OpenRead("receipt.png");
var document = await client.ExtractAsync(image, "image/png");

foreach (var table in document.Pages[0].Elements.OfType<DocumentTable>())
{
    Console.WriteLine($"{table.RowCount} rows x {table.ColumnCount} columns");
}
```

## Packages

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Essentials.AI` | On-device AI APIs for MAUI |

## Building

```bash
# macOS (builds Swift bindings + .NET library)
dotnet build src/AI/EssentialsAI.slnf

# Windows (CI only — the Azure DevOps pipeline downloads macOS-built
# native artifacts automatically. Local Windows builds require CI=true
# or TF_BUILD=true for the pre-built artifact path to activate.)
```

The CI pipeline handles the macOS → Windows artifact flow automatically. See `.github/workflows/ci-essentialsai.yml` for details.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)
- [Apple Document Recognizer Implementation](../../docs/ai/apple-document-recognizer-implementation.md)
- [Apple Document Extraction Feedback](../../docs/ai/apple-document-extraction-feedback.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
