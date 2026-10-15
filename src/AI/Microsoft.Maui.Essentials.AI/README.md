# Microsoft.Maui.Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required.

This package provides [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions (`IChatClient`, `IEmbeddingGenerator`) backed by on-device AI capabilities:

> This package is experimental and ships as `-preview`. APIs may change.

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
| --- | --- | --- | --- |
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | 27+ with vision-capable model | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Android 8.0+ on supported AICore devices | ✅ Gemini Nano (ML Kit GenAI Prompt) | ✅ Image prompts | 🔜 Coming soon |
| Windows 11 24H2+ | Windows AI text and structured JSON | Not supported | Not supported |

## Getting started

### 1. Install the package

```sh
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

### 2. Register services

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();

#if ANDROID
if (OperatingSystem.IsAndroidVersionAtLeast(26))
    builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
#elif IOS || MACCATALYST || MACOS
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
#elif WINDOWS
if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
    builder.Services.AddSingleton<IChatClient>(new WindowsAIChatClient());
#endif
```

Windows requires supported hardware and an MSIX package declaring
`systemAIModels`; stable Phi Silica runtime access also requires Microsoft's
Limited Access Feature approval. See the
[Windows integration guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/WINDOWS-AI.md).
Windows chat accepts text and structured JSON, not tools or image input.

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

## Chat

To continue a conversation, include its earlier `ChatMessage` values in the next
request. The clients do not retain conversation history between calls.

### Streaming responses

```csharp
await foreach (var update in chat.GetStreamingResponseAsync("Plan a day trip to Tokyo"))
{
    Console.Write(update.Text);
}
```

### Image input

Apple Intelligence and Gemini Nano both accept image input. Attach encoded
image bytes with `Microsoft.Extensions.AI.DataContent`:

```csharp
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe this photo."),
    new DataContent(pngBytes, "image/png"),
]);
var response = await chat.GetResponseAsync([message]);
```

Android accepts in-memory image bytes only. Apple also accepts local `file://`
images and native image handles; see the
[Apple image-input guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models-image-input.md)
for those options. Neither client downloads remote image URLs or generates
images. Image availability is listed in the platform table above.

### Token usage

Android reports input tokens only. Apple OS 27+ reports native input, output,
cached, reasoning, and total counts; Apple OS 26 returns no usage.
Unavailable counts stay unset rather than being estimated.

```csharp
var response = await chat.GetResponseAsync("Summarize this text.");

if (response.Usage is { } usage)
{
    Console.WriteLine($"Input: {usage.InputTokenCount}");
    Console.WriteLine($"Output: {usage.OutputTokenCount}");
}
```

Streaming responses emit a final `UsageContent` update. Aggregate the stream
into a `ChatResponse` to read it from `ChatResponse.Usage`:

```csharp
var response = await chat
    .GetStreamingResponseAsync("Plan a day trip to Tokyo")
    .ToChatResponseAsync();

Console.WriteLine($"Input tokens: {response.Usage?.InputTokenCount}");
```

Counts cover one request. Use `UsageDetails.Add` to accumulate usage across
requests.

### Provider-specific options

Apple Intelligence supports tools and structured JSON responses. Windows AI
supports structured JSON, but not tools. Structured responses stream only when
the native provider emits partial updates; otherwise the completed response can
arrive as one update.
Gemini Nano supports `Temperature`, `TopK`, `Seed`, and `MaxOutputTokens`, but
not tools or JSON response formats.

On Gemini, `ChatOptions.Instructions` and system messages require Nano V3+.
`Reasoning = new() { Output = ReasoningOutput.Full }` requires Nano V4+; effort
levels and reasoning summaries are unsupported. Unavailable capabilities
produce errors; there is no automatic cloud fallback.

## Embeddings

Use Apple's NaturalLanguage embeddings for on-device semantic search:

```csharp
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

## Diagnostics

### OpenTelemetry

Also reference `Microsoft.Extensions.AI` to wrap your chosen client with its
telemetry middleware:

```csharp
var instrumentedChat = chat.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Configure listeners/exporters for the `MyApp.AI` activity source and meter.
The middleware records timing, model metadata, and available native usage.
Fully enumerate streams to capture final usage. Sensitive prompt/response
capture is disabled in this example.

### Tool diagnostics

Pass an `ILoggerFactory` to `AppleIntelligenceChatClient` for tool lifecycle logs
at Debug level. Tool arguments/results require Trace level.

## Requirements

- .NET 10 and the MAUI workload (`dotnet workload install maui`).
- A supported device with the on-device model enabled and ready.
- Gemini requires current AICore services, a locked bootloader, and a
  [supported physical device](https://developers.google.com/ml-kit/genai#prompt-device).
  Android emulators cannot run Gemini inference.

The Android package minimum is API 24, but Gemini requires API 26. Apps targeting
older devices must guard client registration, as shown above, and apply the
[native manifest configuration](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md#android-minimum-and-model-availability).

## Links

- [Contributor and implementation guides](https://github.com/dotnet/maui-labs/tree/main/src/AI)
- [Chat Playground sample](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Windows integration](https://github.com/dotnet/maui-labs/blob/main/docs/ai/WINDOWS-AI.md)
- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ai-extensions)
