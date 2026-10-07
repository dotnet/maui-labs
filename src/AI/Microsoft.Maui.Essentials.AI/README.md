# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Android 8.0+ on supported AICore devices | ✅ Gemini Nano (ML Kit GenAI Prompt) | 🔜 Coming soon |
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

// Register the platform-native chat client
#if ANDROID
builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
#else
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
#endif
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

### Android capabilities

`GeminiNanoChatClient` supports text and image prompts, incremental text streaming,
system instructions on compatible Nano versions, schema-based JSON responses for
the same DTO-oriented subset supported by the Apple client, and Nano V4 thinking
through `ChatOptions.Reasoning`. AICore does not support native multi-turn
sessions, so supplied chat history is flattened into each request.

The client does not retain conversation or inference state. Each concurrent call
uses its own ML Kit model session. JSON schemas are included in the request and
the completed response is parsed and validated against the requested schema.

The public surface remains `Microsoft.Extensions.AI` plus
`GeminiNanoChatClient`; ML Kit, coroutine, model-management, and schema-provider
types are contained inside the package's Kotlin native bridge.

The current ML Kit beta does not expose dynamic .NET tool calling. Unsupported
`ChatOptions` values fail explicitly rather than being ignored.

### Embeddings for semantic search

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+
- Android requires API 26+, current Google system services/AICore, a locked
  bootloader, and a device listed in
  [Prompt API device support](https://developers.google.com/ml-kit/genai#prompt-device)
- Android emulators do not include AICore and cannot run Gemini Nano inference

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
