# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|-------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | 27+ with vision-capable model | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Android 8.0+ on supported AICore devices | ✅ Gemini Nano (ML Kit GenAI Prompt) | ✅ Image prompts | 🔜 Coming soon |
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

// Register the platform-native chat client
#if ANDROID
builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
#elif IOS || MACCATALYST || MACOS
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

### Token usage

Token usage requires the native Swift bridge to be built with Xcode 27 or
later and the app to run on Apple OS 27 or later. Usage is returned through
the standard `Microsoft.Extensions.AI` response APIs:

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
introduced in OS 27. Counts come directly from Foundation Models; the client
does not reconstruct or estimate missing usage.

### OpenTelemetry

Reference `Microsoft.Extensions.AI` to use its built-in telemetry middleware,
just as with an Azure OpenAI client:

```csharp
using var client = new AppleIntelligenceChatClient().AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Configure your application's OpenTelemetry listeners or exporters for the
`MyApp.AI` activity source and meter. The middleware emits provider/model
metadata, request duration, and native usage through standard `gen_ai` spans
and metrics. Fully enumerate streaming responses to capture final usage.
Streaming also produces time-to-first-chunk and time-per-output-chunk metrics.
Prompts and responses are excluded by the explicit sensitive-data setting.

With the repository's pinned M.E.AI 10.4.1, input/output tokens are span tags
and `gen_ai.client.token.usage` measurements; cached input is also a span tag
(`gen_ai.usage.cache_read.input_tokens`). Total and reasoning counts remain
available on `UsageDetails`, but do not have separate built-in span tags or
metrics. On OS 26, request timing and model metadata remain available without
token usage. Provider-specific response IDs and finish reasons are emitted
only when the provider supplies them.

### Android capabilities

`GeminiNanoChatClient` supports text and image prompts, incremental text streaming,
system instructions on compatible Nano versions, prompt-guided JSON responses for
a DTO-oriented schema subset, and Nano V4 thinking
through `ChatOptions.Reasoning`. AICore does not support native multi-turn
sessions, so supplied chat history is flattened into each request.

The client does not retain conversation or inference state. Each concurrent call
uses its own ML Kit model session. JSON schemas are included in the request and
the completed response is parsed and validated against the requested schema.

The public surface remains `Microsoft.Extensions.AI` plus
`GeminiNanoChatClient`; ML Kit and coroutine types are internal. The Kotlin
bridge and its pinned ML Kit runtime assets are built and packaged through
`AndroidGradleProject`. Consumers do not need Gradle or a separate GenAI binding
package.

Schemas support objects, arrays, primitives, nullable types, enums, numeric
bounds, and collection size limits. Unsupported schema constructs fail before
inference; invalid generated JSON fails after generation. This does not use
ML Kit's compile-time Kotlin typed-output API or guarantee constrained decoding.

The current ML Kit beta does not expose dynamic .NET tool calling. Unsupported
`ChatOptions` values fail explicitly rather than being ignored. `ChatToolMode.None`
disables supplied tool definitions.

AICore and the model must already be ready for inference. If Gemini Nano is
downloadable but not ready, prepare it through AICore and retry. Model errors are
returned to the caller; the client never switches to a cloud model.

### Embeddings for semantic search

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

### Tool diagnostics

Pass an `ILoggerFactory` to `AppleIntelligenceChatClient` to receive existing
tool lifecycle logs at Debug (arguments/results require Trace). Native tool
callbacks preserve the originating .NET execution context, so their logs and
async tool code inherit any active chat trace/span, including one created by
`UseOpenTelemetry`. The client does not invent correlation when no span exists.

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence requires iOS 26+, macOS 26+, or Mac Catalyst 26+
- Image input additionally requires iOS, macOS, or Mac Catalyst 27+ with a ready vision model
- Android requires API 26+, current Google system services/AICore, a locked
  bootloader, and a device listed in
  [Prompt API device support](https://developers.google.com/ml-kit/genai#prompt-device)
- Android emulators do not include AICore and cannot run Gemini Nano inference
- The Android package minimum is API 26; apps referencing it must also target
  API 26 or later

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Sample app](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Image-input playground and macOS 27 test checklist](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
