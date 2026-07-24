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

### Token usage

On Apple OS 27 and later, token usage is returned through the standard
`Microsoft.Extensions.AI` response APIs:

```csharp
var response = await _chat.GetResponseAsync("Summarize this text.");

if (response.Usage is { } usage)
{
    Console.WriteLine($"Input: {usage.InputTokenCount}");
    Console.WriteLine($"Output: {usage.OutputTokenCount}");
    Console.WriteLine($"Cached input: {usage.CachedInputTokenCount}");
    Console.WriteLine($"Reasoning: {usage.ReasoningTokenCount}");
    Console.WriteLine($"Total: {usage.TotalTokenCount}");
}
```

Streaming responses emit a final `UsageContent` update. Aggregate the stream
into a `ChatResponse` to read it from `ChatResponse.Usage`:

```csharp
var response = await _chat
    .GetStreamingResponseAsync("Plan a day trip to Tokyo")
    .ToChatResponseAsync();

Console.WriteLine($"Total tokens: {response.Usage?.TotalTokenCount}");
```

Each response reports usage for that request. To track a conversation or
application total, accumulate responses with `UsageDetails.Add`. Usage is
`null` on Apple OS 26 because the native Foundation Models usage API was
introduced in OS 27.

### Embeddings for semantic search

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

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
