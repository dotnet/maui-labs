# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides `Microsoft.Extensions.AI` and experimental
`Microsoft.Extensions.DocumentExtraction` abstractions backed by on-device
Apple capabilities:

| Platform | Chat (`IChatClient`) | Embeddings (`IEmbeddingGenerator`) | Documents (`IDocumentExtractionClient`) |
|----------|----------------------|------------------------------------|-----------------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Apple Vision + PDFKit |
| Android | 🔜 Coming soon | 🔜 Coming soon | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon | 🔜 Coming soon |

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

### Structured document extraction

```csharp
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;

using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
await using var image = File.OpenRead("invoice.png");
var result = await client.ExtractAsync(image, "image/png");

foreach (var element in result.Pages[0].Elements)
{
    switch (element)
    {
        case DocumentTable table:
            Console.WriteLine($"Table: {table.RowCount} x {table.ColumnCount}");
            break;
        case AppleListElement list:
            Console.WriteLine($"List: {list.Items.Count} items");
            break;
        case AppleBarcodeElement barcode:
            Console.WriteLine($"{barcode.Symbology}: {barcode.PayloadString}");
            break;
    }
}
```

For PDFs, compose PDFKit page rendering with the same raw Vision client:

```csharp
using IDocumentExtractionClient client =
    new ApplePdfKitRenderingExtractionClient(
        new AppleVisionRecognizeDocumentsClient());

await using var pdf = File.OpenRead("invoice.pdf");
await foreach (var page in client.ExtractPagesAsync(pdf, "application/pdf"))
{
    Console.WriteLine($"Page {page.Page.PageNumber}: {page.Page.Text}");
}
```

The clients never fall back to another recognition engine. Apple-specific
lists, list items, barcodes, capabilities, and raw Vision JSON remain available
through the provider types and `RawRepresentation`.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Sample app](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Document extraction sample](https://github.com/dotnet/maui-labs/tree/main/samples/DocumentExtractionSample)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
