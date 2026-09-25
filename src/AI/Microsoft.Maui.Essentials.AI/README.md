# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`, experimental `IImageGenerator`) backed by on-device AI capabilities:

| Platform | Chat (`IChatClient`) | Embeddings (`IEmbeddingGenerator`) | Images (`IImageGenerator`) |
|----------|----------------------|-------------------------------------|----------------------------|
| iOS 26+ | Apple Intelligence | NL Embeddings | Not available |
| Mac Catalyst 26+ | Apple Intelligence | NL Embeddings | Not available |
| macOS 26+ | Apple Intelligence | NL Embeddings | Not available |
| Android | Not yet available | Not yet available | Not yet available |
| Windows 11 24H2+ | Windows AI language model | Not available | Windows AI image model (experimental) |

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

For a packaged Windows app with the `systemAIModels` capability, register
`new WindowsAIChatClient()` instead, or `new WindowsAIImageGenerator()` as an
`IImageGenerator`. The Windows language model accepts text and structured JSON,
not image inputs or function calls. The [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
demonstrates Windows chat and image generation alongside Apple and optional
Azure providers.

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

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+
- Windows AI requires Windows 11 24H2+, a packaged MSIX app with `systemAIModels`,
  and an available on-device model. Image generation currently requires an
  experimental Windows App SDK; this is not a Microsoft Store-ready dependency.

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Windows AI integration notes](https://github.com/dotnet/maui-labs/blob/main/docs/ai/WINDOWS-AI.md)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
