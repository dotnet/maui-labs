# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
| --- | --- | --- | --- |
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
public class MyViewModel(IChatClient chat)
{
    public async Task<string> AskAsync(string question)
    {
        var response = await chat.GetResponseAsync(question);
        return response.Text;
    }
}
```

### Streaming responses

```csharp
await foreach (var update in chat.GetStreamingResponseAsync("Plan a day trip to Tokyo"))
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

`GeminiNanoChatClient` supports text, in-memory image `DataContent`, streaming,
and supplied user/assistant conversation history. Include the earlier messages
in each request; the client does not retain them. Tools and JSON/structured
response formats are unsupported.

`ChatOptions.Instructions` and system messages require Nano V3+. Full thinking
output uses `Reasoning = new() { Output = ReasoningOutput.Full }` and requires
Nano V4+; effort levels and reasoning summaries are unsupported. Unavailable
model capabilities produce errors rather than falling back to another provider.
Only input token usage is reported.

For request mapping, native builds, and testing, see the
[Android implementation guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md).

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
- Gemini Nano requires API 26+, current Google system services/AICore, a locked
  bootloader, and a device listed in
  [Prompt API device support](https://developers.google.com/ml-kit/genai#prompt-device)
- Android emulators do not include AICore and cannot run Gemini Nano inference
- The Android package minimum is API 24. Guard Gemini Nano registration with
  `OperatingSystem.IsAndroidVersionAtLeast(26)` so other app features remain
  available on older devices. Constructing the client below API 26 throws
  `PlatformNotSupportedException`.

Apps with an Android minimum below API 26 also need
[native manifest configuration](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md#android-minimum-and-model-availability).
This permits installation on older devices; it does not make Gemini Nano
available there. Keep all Gemini Nano usage behind the API 26 runtime guard.

## Status

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.

## Links

- [Source code](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Sample app](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Image-input playground and macOS 27 test checklist](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
