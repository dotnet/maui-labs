# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

> The document APIs in this branch are a non-shipping prototype of the proposed extensions contract. This checkout cannot pack the
> prototype library or copied Microsoft projects. Existing published package features are separate from this evaluation.

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
The reader projects a typed page stream from public `AppleVisionRecognizeDocumentsClient`, backed by
`AppleVisionRecognizeDocumentsProcessor`. Its single native request wrapper, `RecognizeDocumentsRequestNative`,
returns a `RecognizeDocumentsRequestSnapshotNative` JSON snapshot, not an Apple observation.
One canonical internal mapper maps the supported intersection; the reader preserves Vision's
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

### Extract structured documents with the proposed client API

This branch is a **non-shipping prototype**, not a NuGet-ready implementation. It file-links the complete, unchanged
`Microsoft.Extensions.DocumentExtraction.Abstractions` and `Microsoft.Extensions.DocumentExtraction` projects from
[dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588). Original namespaces and public accessibility remain intact.
No reduced private contract or rewritten model is used.

```csharp
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;

using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
await using var input = File.OpenRead("receipt.pdf");
await foreach (var update in client.ExtractPagesAsync(input, "application/pdf"))
{
    Console.WriteLine($"Page {update.Page.PageNumber}: {update.Page.Text}");
    foreach (var table in update.Page.Elements.OfType<DocumentTable>())
        Console.WriteLine($"{table.RowCount} rows, {table.ColumnCount} columns");
}
```

The provider implements both `ExtractAsync` and `ExtractPagesAsync`, standard service discovery, and caller-owned stream semantics.
Titles, paragraphs, actual table cells/spans/nested content, and normalized geometry map directly. Unsupported features remain absent:
no fabricated figure bytes, table roles, model selection, token usage, or custom barcode/list block kinds. Apple-specific information
is not attached to `AdditionalProperties`; the existing `RawRepresentation` slot contains the bounded native snapshot.

The unchanged proposal middleware provides builders, configuration, dependency injection, logging, and OpenTelemetry.
The unchanged upstream `OcrDocumentReader` is available alongside the custom reader for comparison.
See [the capability gaps](../../../docs/ai/apple-document-extraction-gaps.md) before deciding release visibility.

The [AI Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
includes extraction, native-reader, and upstream-OCR-reader modes. Azure Document Intelligence and Foundry Mistral are sample-only,
explicitly selected providers; processing uploads immediately, with inline disclosure and no automatic fallback.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence chat and Apple Vision document recognition require iOS 26+, macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
