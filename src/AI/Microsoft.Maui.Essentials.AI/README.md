# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities, plus an experimental provider-neutral `IImageClassificationClient` contract:

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) | Image classification contract |
|----------|-------------------|----------------------------------|-------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | ✅ NL Embeddings | ✅ Provider-neutral contract |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Provider-neutral contract |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Provider-neutral contract |
| Android | 🔜 Coming soon | 🔜 Coming soon | ✅ Provider-neutral contract |
| Windows | 🔜 Coming soon | 🔜 Coming soon | ✅ Provider-neutral contract |

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

### Image classification

Applications depend on the contract and receive a provider from dependency injection:

```csharp
public static async Task<ImageClassificationPrediction?> ClassifyAsync(
    IImageClassificationClient classifier,
    Stream image,
    CancellationToken cancellationToken = default)
{
    ImageClassificationResult result = await classifier.ClassifyAsync(
        image,
        "image/jpeg",
        new ImageClassificationOptions { MaximumPredictions = 3 },
        cancellationToken);

    return result.Predictions.FirstOrDefault();
}
```

The stream remains owned by the caller. Clients read one encoded image from its current position and must not dispose or retain the stream. A provider should throw `NotSupportedException` for a valid image media type it cannot decode.

Provider identity is available from `ImageClassificationClientMetadata` through `GetService`. Each result retains its `ModelId` and can preserve provider-native response data through `RawRepresentation` and `AdditionalProperties`.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Sample app](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
