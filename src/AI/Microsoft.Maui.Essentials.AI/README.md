# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions)
abstractions (`IChatClient`, `IEmbeddingGenerator`) and an
[`IngestionDocumentReader`](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.Abstractions)
implementation backed by on-device capabilities:

| Platform | Chat (`IChatClient`) | Embeddings (`IEmbeddingGenerator`) | Document reading (`IngestionDocumentReader`) |
|----------|----------------------|--------------------------------------|-----------------------------------------------|
| iOS 26+ | Apple Intelligence (Foundation Models) | NL Embeddings | Apple Vision |
| Mac Catalyst 26+ | Apple Intelligence | NL Embeddings | Apple Vision |
| macOS 26+ | Apple Intelligence | NL Embeddings | Apple Vision |
| Android | Coming soon | Coming soon | Not supported |
| Windows | Coming soon | Coming soon | Not supported |

## Getting Started

### 1. Install the package

```
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

### 2. Register services

```csharp
var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();

// Register Apple Intelligence chat client (iOS/macOS/Mac Catalyst)
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
```

### 3. Use in your app

```csharp
public class MyViewModel
{
    private readonly IChatClient _chat;

    public MyViewModel(IChatClient chat)
    {
        _chat = chat;
    }

    public async Task<string> AskAsync(string question)
    {
        var response = await _chat.GetResponseAsync(question);
        return response.Text;
    }
}
```

### Streaming responses

```csharp
await foreach (var update in _chat.GetStreamingResponseAsync("Plan a day trip to Tokyo"))
{
    Console.Write(update.Text);
}
```

### Embeddings for semantic search

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

### Read an image or PDF for ingestion

On iOS 26+, Mac Catalyst 26+, or macOS 26+, use `AppleVisionDocumentReader` as
an `IngestionDocumentReader`. Recognition runs locally through Apple's Vision
`RecognizeDocumentsRequest`. A PDF is rendered internally with PDFKit and produces
one numbered `IngestionDocumentSection` per page; an image produces one section.

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Maui.Essentials.AI;

IngestionDocumentReader reader = new AppleVisionDocumentReader();
var document = await reader.ReadAsync(
    new FileInfo("receipt.pdf"),
    identifier: "receipts/2026/october");

// Pass document to your DataIngestion chunker and writer for RAG.
foreach (var section in document.Sections)
{
    Console.WriteLine($"Page {section.PageNumber}: {section.GetMarkdown()}");
}
```

For streams, specify the media type explicitly:
`reader.ReadAsync(stream, "receipts/2026/october", "application/pdf", cancellationToken)`.
Supported image types include PNG, JPEG, HEIC, and TIFF. Supply a cancellation
token for long or multi-page documents. The reader preserves the supplied
identifier and returns standard headers, paragraphs, and tables; list text
becomes paragraphs because the current ingestion model has no list element.
Unsupported platforms or document types have no cloud fallback.

This focused reader does not surface geometry, confidence, barcodes, detected
entities, provider options, raw observations, or progress. A future official
`IDocumentExtractionClient` could provide typed versions of these, including
streaming/progress and raw-provider escape hatches, without adding ad-hoc
metadata to the ingestion document.

The [AI Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
has an optional Documents tab for comparing the local reader with Azure AI
Document Intelligence's `prebuilt-layout` model. That cloud client belongs
only to the sample; using it requires separate configuration and an explicit
request to upload the document.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence chat and Apple Vision document recognition require iOS 26+,
  macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [AI Playground (including document comparison)](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
