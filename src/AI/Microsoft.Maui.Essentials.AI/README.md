# Microsoft.Maui.Essentials.AI

On-device chat and embeddings using platform-native models and
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions).
No cloud service is required.

> **Experimental:** this package ships as a preview. APIs may change.

## Supported platforms

| Platform | Chat (`IChatClient`) | Image input | Embeddings (`IEmbeddingGenerator`) |
|----------|---------------------|-------------|-----------------------------------|
| iOS / Mac Catalyst / macOS 26+ | Apple Intelligence, including tools and structured responses | OS 27+ with a ready vision model | Apple NaturalLanguage |
| Windows | Not supported | Not supported | Not supported |
| Android | Not supported | Not supported | Not supported |

The Apple chat client does not generate images. On-device models must be
enabled and available on a supported device.

## Install and use

Requires .NET 10 and the MAUI workload.

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Platform-native chat and embeddings through M.E.AI abstractions |

```sh
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

Register the Apple client in a supported iOS, macOS, or Mac Catalyst app:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
```

## Chat

Use the injected `IChatClient` (`chat` below) to request a response or stream:

```csharp
var response = await chat.GetResponseAsync("Plan a day trip to Tokyo");
Console.WriteLine(response.Text);

await foreach (var update in chat.GetStreamingResponseAsync("Suggest a walking route"))
{
    Console.Write(update.Text);
}
```

Structured responses stream only when the native provider emits partial
updates; otherwise a completed response can arrive as one update.

### Apple tools

Supply `AIFunction` tools through standard `ChatOptions`. The native client
invokes them and returns their results to the model:

```csharp
var options = new ChatOptions
{
    Tools =
    [
        AIFunctionFactory.Create(
            (string city) => $"Weather for {city}: sunny.",
            name: "GetWeather"),
    ],
};
var response = await chat.GetResponseAsync("What's the weather in Boston?", options);
```

This example returns fixed demonstration data. Set `ChatOptions.ToolMode` to `ChatToolMode.None` to
disable tools for a request.

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
var generator = new NLEmbeddingGenerator(NLEmbeddingType.Sentence);
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

The corresponding NaturalLanguage model asset must be installed on the device.

## Examples and source

- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Apple image input](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models-image-input.md)
- [Source and contributor build instructions](https://github.com/dotnet/maui-labs/tree/main/src/AI)
