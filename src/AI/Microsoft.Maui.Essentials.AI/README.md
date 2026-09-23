# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|-------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | 27+ with vision-capable model | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Android | 🔜 Coming soon | Not available | 🔜 Coming soon |
| Windows | 🔜 Coming soon | Not available | 🔜 Coming soon |

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

### Image input on Apple 27+

Attach portable image bytes with `Microsoft.Extensions.AI.DataContent`:

```csharp
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe this photo."),
    new DataContent(pngBytes, "image/png"),
]);
var response = await _chat.GetResponseAsync([message]);
```

On Apple, a `CGImage` (or `UIImage` on iOS/Mac Catalyst, `NSImage` on macOS) can be passed through without re-decoding by setting `RawRepresentation` on the `DataContent`; retain valid encoded bytes for recording and cross-platform consumers. Local `file://` `UriContent` is supported, while remote image URLs are rejected rather than fetched silently. Image prompting requires runtime OS 27 and a vision-capable, ready Apple Intelligence model; on 26 the client remains text-only and an image request throws an explicit error. Image *generation* is not provided by this Apple chat client.

### Embeddings for semantic search

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+
- Image input additionally requires iOS, macOS, or Mac Catalyst 27+ with a ready vision model

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Sample app](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Image-input playground and macOS 27 test checklist](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
