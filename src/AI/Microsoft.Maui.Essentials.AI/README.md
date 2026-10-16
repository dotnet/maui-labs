# Microsoft.Maui.Essentials.AI

On-device AI using platform-native models and
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions).
Providers cover Apple and Windows, with Android integration **coming soon**.
Apple Intelligence and Windows AI implement the same `IChatClient` interface
for text, native streaming updates, and schema-constrained JSON. No cloud
service is required.

> **Experimental:** this package ships as a preview. APIs may change.

## Provider availability

| Platform | On-device integration |
|----------|-----------------------|
| Apple | Apple Intelligence and NaturalLanguage |
| Windows | Windows AI |
| Android | Coming soon; provider details are not specified |

Android is a placeholder: its provider details, capabilities, and requirements
are not specified here. Chat, embeddings, and image generation are separate
capabilities; image input to a chat client does not imply image generation.
Models must be enabled and available on a supported device.

## Install

Requires .NET 10 and the MAUI workload.

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Platform-native chat and embeddings through M.E.AI abstractions |

```sh
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

## Configure chat

| Provider | Setup |
|----------|-------|
| Apple Intelligence | Enable Apple Intelligence on supported hardware with Apple OS 26+. Image input and usage metadata require OS 27+ and the corresponding native bridge. |
| Windows AI | Use supported Copilot+ hardware, an MSIX package declaring `systemAIModels`, and a compatible Windows App SDK. Stable Phi Silica access also requires Microsoft's Limited Access Feature approval. |
| Android (coming soon) | Provider setup is not available yet. |

Register the provider for the app's platform. The factory keeps client ownership
with dependency injection and rejects unsupported OS versions explicitly.

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

builder.Services.AddSingleton<IChatClient>(_ =>
{
#if IOS || MACCATALYST || MACOS
    if (OperatingSystem.IsIOSVersionAtLeast(26) ||
        OperatingSystem.IsMacCatalystVersionAtLeast(26) ||
        OperatingSystem.IsMacOSVersionAtLeast(26))
    {
        return new AppleIntelligenceChatClient();
    }
#elif WINDOWS
    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
    {
        return new WindowsAIChatClient();
    }
#endif

    throw new PlatformNotSupportedException("On-device chat requires a supported Apple or Windows system.");
});
```

Platform setup details:
[Apple image input and model readiness](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models-image-input.md)
and [Windows packaging, SDK selection, and access](https://github.com/dotnet/maui-labs/blob/main/docs/ai/WINDOWS-AI.md).

## Chat

| Provider | Text | Image input | Tools | Structured JSON |
|----------|------|-------------|-------|-----------------|
| Apple Intelligence | Supported on Apple OS 26+ | OS 27+ with a ready vision model | Supported | Supported |
| Windows AI | Supported on Windows 11 24H2+ with supported hardware | Not supported | Not supported | Supported |
| Android (coming soon) | Not specified | Not specified | Not specified | Not specified |

Use the injected `IChatClient` (`chat` below) in the same way for either provider:

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

### Structured JSON

Supply a response schema through standard `ChatOptions` for either provider:

```csharp
using System.Text.Json;

using var schema = JsonDocument.Parse("""
    {"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}
    """);
var options = new ChatOptions
{
    ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement),
};

var response = await chat.GetResponseAsync("Suggest a day trip destination.", options);
using var json = JsonDocument.Parse(response.Text);
Console.WriteLine(json.RootElement.GetProperty("answer").GetString());
```

Provider-specific schema constraints and native runtime limitations still apply;
there is no simulated token streaming or automatic switch to another provider.

### Usage metadata

Read usage when the selected provider supplies it:

```csharp
var response = await chat.GetResponseAsync("Summarize this text.");
Console.WriteLine($"Input: {response.Usage?.InputTokenCount}");
Console.WriteLine($"Output: {response.Usage?.OutputTokenCount}");
Console.WriteLine($"Cached input: {response.Usage?.CachedInputTokenCount}");
Console.WriteLine($"Reasoning: {response.Usage?.ReasoningTokenCount}");
Console.WriteLine($"Total: {response.Usage?.TotalTokenCount}");
```

Apple usage requires OS 27 and a native bridge built with Xcode 27+. Streaming
then includes a final `UsageContent` update. Use `ToChatResponseAsync()` to
aggregate the stream and read `ChatResponse.Usage`; accumulate per-request
counts with `UsageDetails.Add` for conversation totals. Usage is `null` on
Apple OS 26 and Windows; the package does not estimate token counts.

### Tool calling

Available with Apple Intelligence; the Windows chat provider rejects tool options.

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

### Image input

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

## Embeddings

Embeddings convert text to floating-point vectors through `IEmbeddingGenerator`.
They are independent of the chat provider.

| Provider | Text vectors | Multiple text inputs | Image embeddings |
|----------|--------------|----------------------|------------------|
| Apple NaturalLanguage | Supported | Supported | Not supported |
| Windows | Not supported | Not supported | Not supported |
| Android | Not specified | Not specified | Not specified |

```csharp
using var generator = new NLEmbeddingGenerator();
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

The default Apple generator uses English sentence embeddings. Select another
supported language with the `NLLanguage` constructor or wrap an existing
`NLEmbedding` instance. The corresponding model asset must be installed.

## Image generation

Image generation is a separate feature, not chat image input or tool calling.

| Platform | Native image generation |
|----------|-------------------------|
| Apple | Not supported |
| Windows | Coming soon |
| Android | Not specified |

No native image-generation implementation is included in this version.
The Windows work is separate; Android support and features are not specified.

## Diagnostics

Reference `Microsoft.Extensions.AI` to decorate either provider with its telemetry
middleware. Apply the wrapper as part of the chosen client's registration and
keep its lifetime aligned with the underlying client:

```csharp
var instrumentedChat = chat.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Use `instrumentedChat` for requests, or return it from the provider factory.

Configure your application's listeners or exporters for the `MyApp.AI` activity
source and meter. Middleware records request timings, model metadata, and usage
when supplied by the provider. Fully enumerate streams to capture final usage.
Streaming also produces time-to-first-chunk and time-per-output-chunk metrics.
The explicit sensitive-data setting above excludes prompt and response content.

With the repository's pinned M.E.AI 10.4.1, input/output tokens are span tags
and `gen_ai.client.token.usage` measurements; cached input is also a span tag
(`gen_ai.usage.cache_read.input_tokens`). Total and reasoning counts remain
available on `UsageDetails`, without separate built-in span tags or metrics.
On Apple OS 26 and Windows, timing and model metadata remain available without
token usage.
Response IDs and finish reasons are emitted only when supplied by the provider.

For Apple tool diagnostics, supply an `ILoggerFactory` to the client.
Lifecycle logs use Debug; arguments and results require Trace. Native callbacks
preserve the originating .NET execution context, including an active chat
trace/span and async tool code. No correlation is invented when no span exists.

## Examples and source

- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Apple image input](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models-image-input.md)
- [Windows integration](https://github.com/dotnet/maui-labs/blob/main/docs/ai/WINDOWS-AI.md)
- [Source and contributor build instructions](https://github.com/dotnet/maui-labs/tree/main/src/AI)
