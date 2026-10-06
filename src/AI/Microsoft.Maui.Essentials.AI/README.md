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

On iOS 26+, Mac Catalyst 26+, or macOS 26+, use `AppleVisionRecognizeDocumentsReader` as an `IngestionDocumentReader`.
Recognition runs locally through Apple's Vision `RecognizeDocumentsRequest`. PDFKit renders PDFs internally,
producing one numbered `IngestionDocumentSection` per page; an image produces one section.
The reader projects a typed page stream from internal `AppleVisionRecognizeDocumentsClient`, backed by
`AppleVisionRecognizeDocumentsProcessor`. Its single native request wrapper, `RecognizeDocumentsRequestNative`,
returns a `RecognizeDocumentsRequestSnapshotNative` JSON snapshot, not an Apple observation.
One canonical internal mapper retains the complete native snapshot and rich structure; the reader preserves Vision's
paragraph/column order without interpreting raw JSON. The Swift binding, image orientation, PDF rendering, and
cancellation machinery remain shared.

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Maui.Essentials.AI;

IngestionDocumentReader reader = new AppleVisionRecognizeDocumentsReader();
var document = await reader.ReadAsync(
    new FileInfo("receipt.pdf"),
    identifier: "receipts/2026/october");

// Pass document to your DataIngestion chunker and writer for RAG.
foreach (var section in document.Sections)
{
    Console.WriteLine($"Page {section.PageNumber}: {section.GetMarkdown()}");
}
```

For streams, specify the media type: `reader.ReadAsync(stream, "receipts/2026/october", "application/pdf", cancellationToken)`.
Supported image types include PNG, JPEG, HEIC, and TIFF. Supply a cancellation token for long or multi-page documents.
The reader preserves the supplied identifier and maps standard headers, paragraphs, and tables with page numbers on
sections, elements, and cells. List text becomes paragraphs because the current ingestion model has no list element.

### Deliberate limitations

The reader omits geometry, confidence, typed barcodes/entities/lists, request options, raw observations, and
progress/streaming. It does not expose provider metadata or native results.
Internally, a required subset of 19 normalized types follows the proposed
[`IDocumentExtractionClient` contract in dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588),
pinned to [`a215825ae2c96723e922e068c226ff77122c7c94`](https://github.com/luisquintanilla/extensions/tree/a215825ae2c96723e922e068c226ff77122c7c94/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions).
These adaptations are all internal to MAUI, with no external extraction dependency or namespace.
Apple-specific companions and property bags retain full observations/node hierarchies, PDF facts, native-precision
ordering bounds, and `apple.vision.*` request options separately from normalized models.
Future official-package adoption requires compatibility review and provider-companion migration, not a visibility toggle.
No extraction API or model types are exposed to consumers; `AppleVisionRecognizeDocumentsReader` remains the only public document API.
Embedded images are not emitted because the recognition API does not provide usable image content.

The [AI Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
includes a Documents tab with selectable `IngestionDocumentReader` providers. The optional Azure AI Document
Intelligence reader is sample-only and requires explicit upload confirmation; it is never a fallback for Apple Vision.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence chat and Apple Vision document recognition require iOS 26+, macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
