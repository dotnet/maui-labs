# Microsoft.Maui.Essentials.AI

On-device chat and text embeddings through
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions),
backed by Apple Intelligence, Apple NaturalLanguage, Windows AI, and Gemini Nano.
Chat capabilities vary by platform; Apple supports native tool execution and
eligible image input, Windows supports structured JSON, and Android supports
in-memory image input. No cloud service is used.

> **Experimental:** this package ships as a preview. APIs may change.

## Requirements and platform support

Requires .NET 10 and matching MAUI workloads.

| Platform | Chat (`IChatClient`) | Image input | Embeddings (`IEmbeddingGenerator`) |
|----------|---------------------|-------------|-----------------------------------|
| iOS 26+ | Apple Intelligence, including tools and structured responses | OS 27+ with a ready vision model | NaturalLanguage, OS 13+ |
| Mac Catalyst 26+ | Apple Intelligence, including tools and structured responses | OS 27+ with a ready vision model | NaturalLanguage, OS 13.1+ |
| macOS 26+ | Apple Intelligence, including tools and structured responses | OS 27+ with a ready vision model | NaturalLanguage, OS 10.15+ |
| Windows 11 24H2+ | Windows AI on supported Copilot+ hardware, including structured responses | Not supported | Not supported |
| Android API 26+ | Gemini Nano on a supported AICore device | In-memory images | Not supported |

Chat requires supported hardware with Apple Intelligence available and enabled.
Image input, token usage metadata, and explicit native tool-calling modes require
OS 27+ and a bridge built with Xcode 27. Image input also requires a ready
vision-capable model.

Windows chat requires an MSIX-packaged app declaring `systemAIModels`, a
compatible Windows App SDK, and the applicable model-access configuration.

## Install

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Apple, Windows, and Android on-device chat, plus Apple text embeddings |

```bash
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

Register the appropriate client on a supported platform:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

#if IOS || MACCATALYST || MACOS
if (OperatingSystem.IsIOSVersionAtLeast(26) ||
    OperatingSystem.IsMacCatalystVersionAtLeast(26) ||
    OperatingSystem.IsMacOSVersionAtLeast(26))
{
    builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
}
#elif ANDROID
builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
#endif
```

On Android, start and monitor a model download only after an explicit app or
user decision:

```csharp
var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
if (status.Availability == ChatModelAvailability.NotReady)
{
    await GeminiNanoChatClient.PrepareModelAsync(cancellationToken);
}
```

Gemini Nano supports text, in-memory images, streaming, and input-token usage.
It rejects structured output and required tool invocation; Auto tool definitions
are ignored and historical calls/results are replayed as text. `PrepareModelAsync`
waits for the explicit download to finish. Use `DownloadModelAsync` when the app
needs to display ML Kit's native progress, and query model status afterward
before enabling generation.

## Quick start

Check availability with the concrete client before offering chat:

```csharp
var status = await AppleIntelligenceChatClient.GetModelStatusAsync();
if (status.Availability != ChatModelAvailability.Available)
    Console.WriteLine(status.UnavailableReason);
```

Register and explicitly prepare Windows AI in a supported packaged app:

```csharp
#if WINDOWS
if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
{
    builder.Services.AddSingleton<IChatClient>(new WindowsAIChatClient());
}
#endif
```

```csharp
var status = await WindowsAIChatClient.GetModelStatusAsync();
if (status.Availability == ChatModelAvailability.NotReady)
    await WindowsAIChatClient.EnsureReadyAsync(cancellationToken);
```

Ordinary generation never initiates Windows model setup or download.

Use the injected `IChatClient` (`chat`) for requests:

```csharp
var response = await chat.GetResponseAsync("Plan a day trip to Tokyo");
Console.WriteLine(response.Text);

await foreach (var update in chat.GetStreamingResponseAsync("Suggest a walking route"))
    Console.Write(update.Text);
```

Structured responses stream only when the native provider emits partial
updates; otherwise a completed response can arrive as one update.

### Structured responses

Use `GetResponseAsync<T>` to generate a schema and deserialize a typed response:

```csharp
using System.ComponentModel;
using System.Text.Json.Serialization;

var response = await chat.GetResponseAsync<TripSuggestion>(
    "Suggest a day trip near Seattle.",
    AppJsonContext.Default.Options);

Console.WriteLine($"{response.Result.Destination}: {response.Result.Reason}");

[Description("A day trip recommendation.")]
public sealed record TripSuggestion(
    [property: Description("The destination name.")] string Destination,
    [property: Description("A short reason to visit.")] string Reason);

[JsonSerializable(typeof(TripSuggestion))]
internal partial class AppJsonContext : JsonSerializerContext;
```

Native schema constraints apply. Structured responses may arrive as one final
update when a provider emits no incremental progress. For trimmed/AOT apps,
pass source-generated `JsonSerializerOptions`, as above, for every structured
response type.

### Apple tools

Supply `AIFunction` tools through standard `ChatOptions`. The native client
invokes them and returns their results to the model:

```csharp
using System.ComponentModel;

var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeather, name: nameof(GetWeather))],
};
var weather = await chat.GetResponseAsync("What's the weather in Boston?", options);
Console.WriteLine(weather.Text);

[Description("Get demonstration weather for a city.")]
static string GetWeather(
    [Description("The city to check.")] string city) => $"{city}: sunny.";
```

Apple executes registered `AIFunction` tools natively. See the shared guide for
tool-mode, schema, sampling, history, and cancellation policies.
Set `ChatOptions.ToolMode` to `ChatToolMode.None` to disable tools for a request.

### Apple image input

Image input requires Apple OS 27 and a ready vision-capable model:

```csharp
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe this photo."),
    new DataContent(pngBytes, "image/png"),
]);
var response = await chat.GetResponseAsync([message]);
```

For native images, set `RawRepresentation` on the `DataContent` to a `CGImage`,
`UIImage` (iOS/Mac Catalyst), or `NSImage` (macOS), while retaining valid encoded
bytes for recording and cross-platform use. Local `file://` image `UriContent`
is supported; remote URLs are rejected rather than fetched silently.
On Apple OS 26, the client is text-only and image requests throw an explicit error.

### Token usage

Apple token usage requires OS 27 and a native bridge built with Xcode 27+:

```csharp
var response = await chat.GetResponseAsync("Summarize this text.");
Console.WriteLine($"Input: {response.Usage?.InputTokenCount}");
Console.WriteLine($"Output: {response.Usage?.OutputTokenCount}");
Console.WriteLine($"Cached input: {response.Usage?.CachedInputTokenCount}");
Console.WriteLine($"Reasoning: {response.Usage?.ReasoningTokenCount}");
Console.WriteLine($"Total: {response.Usage?.TotalTokenCount}");
```

Streaming includes a final `UsageContent` update. Use `ToChatResponseAsync()`
to aggregate the stream and read `ChatResponse.Usage`. Usage is per request;
accumulate it with `UsageDetails.Add` for conversation totals. Counts come
from Foundation Models, not estimates, and usage is `null` on Apple OS 26.

## Diagnostics

Reference `Microsoft.Extensions.AI` to use its telemetry middleware:

```csharp
var instrumentedChat = chat.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Use the wrapped client for requests, or return it from the provider factory.
The same middleware works with every `IChatClient`; provider-specific timing,
metadata, usage, and error behavior is documented in the
[local chat provider contract](https://github.com/dotnet/maui-labs/blob/main/docs/ai/local-chat-provider.md).

To construct and instrument the Apple client directly:

```csharp
using var client = new AppleIntelligenceChatClient().AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Configure your application's listeners or exporters for the `MyApp.AI` activity
source and meter. Middleware records request timings, model metadata, and usage
when supplied by the provider. Fully enumerate streams to capture final usage.
Streaming also produces time-to-first-chunk and time-per-output-chunk metrics.
The explicit sensitive-data setting above excludes prompt and response content.

With the repository's pinned M.E.AI 10.4.1, input/output tokens are span tags
and `gen_ai.client.token.usage` measurements; cached input is also a span tag
(`gen_ai.usage.cache_read.input_tokens`). Total and reasoning counts remain
available on `UsageDetails`, without separate built-in span tags or metrics.
On OS 26, timing and model metadata remain available without token usage.
Response IDs and finish reasons are emitted only when supplied by the provider.

For Apple tool diagnostics, supply an `ILoggerFactory` to the client.
Lifecycle logs use Debug; arguments and results require Trace. Native callbacks
preserve the originating .NET execution context, including an active chat
trace/span and async tool code. No correlation is invented when no span exists.

## Embeddings

```csharp
using var generator = new NLEmbeddingGenerator();
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

The default generator uses English sentence embeddings. Select another supported
language with the `NLLanguage` constructor or wrap an existing `NLEmbedding`.
The corresponding model asset must be installed.

## Guides and samples

- [Chat provider implementation guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/local-chat-provider.md) — shared behavior and provider policy.
- [Apple Foundation Models](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models.md) — Apple toolchain, images, availability, bridge mechanics, and validation.
- [Windows AI language models](https://github.com/dotnet/maui-labs/blob/main/docs/ai/windows-ai-language-models.md) — Windows packaging, readiness, dependencies, and validation.
- [Gemini Nano Android](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md) — Android binding, AICore readiness, download, and validation.
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Source and contributor build instructions](https://github.com/dotnet/maui-labs/tree/main/src/AI)
