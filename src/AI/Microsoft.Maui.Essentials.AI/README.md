# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Android | 🔜 Coming soon | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon |

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
using var generator = new NLEmbeddingGenerator(); // English sentence embeddings
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

Use sentence embeddings for short descriptions, FAQ matching, and paraphrase
retrieval. They measure relatedness, not factual equivalence: validate exact
identifiers, numbers, and exclusions separately. Keep query and document vectors
in the same model, language, and revision, and evaluate chunking on labeled queries
from your app. A cosine score is not a confidence percentage.

NaturalLanguage embeddings are separate from Apple Intelligence. Native API
availability does not by itself establish the minimum runtime of a packaged app
and its native dependencies. See the [Apple embedding guide](https://github.com/dotnet/docs-maui/blob/main/docs/ai/embeddings/apple.md)
for model selection, availability, and measured limitations.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Chat guide and provider comparison](https://github.com/dotnet/docs-maui/blob/main/docs/ai/chat.md)
- [Embedding guide and provider comparison](https://github.com/dotnet/docs-maui/blob/main/docs/ai/embeddings.md)
- [Apple chat best practices and image-input preview](https://github.com/dotnet/docs-maui/blob/main/docs/ai/chat/apple.md)
- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
