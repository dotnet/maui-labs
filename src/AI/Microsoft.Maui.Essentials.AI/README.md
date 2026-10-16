# Microsoft.Maui.Essentials.AI

On-device chat and embeddings through
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions),
using Apple and Windows native models without a cloud service.
Android integration is **coming soon**; its capabilities and requirements
are not specified.

> **Experimental:** this package ships as a preview. APIs may change.

Requires .NET 10 and the MAUI workload.

## Configure chat

| Provider | Setup |
|----------|-------|
| Apple Intelligence | Enable Apple Intelligence on supported hardware with Apple OS 26+. Image input and usage metadata require OS 27+ and the corresponding native bridge. |
| Windows AI | Windows 11 24H2+ on supported Copilot+ hardware, an MSIX package declaring `systemAIModels`, and a compatible Windows App SDK. Stable Phi Silica access requires Microsoft's Limited Access Feature approval. |
| Android (coming soon) | Provider setup is not available yet. |

Register a supported chat provider with dependency injection:

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
| Apple Intelligence | Supported | OS 27+ with a ready vision model | Supported | Supported |
| Windows AI | Supported | Not supported | Not supported | Supported |
| Android (coming soon) | Not specified | Not specified | Not specified | Not specified |

Use the injected `IChatClient` (`chat`) for either provider:

```csharp
var response = await chat.GetResponseAsync("Plan a day trip to Tokyo");
Console.WriteLine(response.Text);

await foreach (var update in chat.GetStreamingResponseAsync("Suggest a walking route"))
{
    Console.Write(update.Text);
}
```

### Structured JSON

Use `GetResponseAsync<T>` to generate the schema and deserialize the response.
Descriptions on the record and its properties guide the model:

```csharp
using System.ComponentModel;

var response = await chat.GetResponseAsync<TripSuggestion>(
    "Suggest a day trip near Seattle.");

Console.WriteLine($"{response.Result.Destination}: {response.Result.Reason}");

[Description("A day trip recommendation.")]
public sealed record TripSuggestion(
    [property: Description("The destination name.")] string Destination,
    [property: Description("A short reason to visit.")] string Reason);
```

Native schema constraints apply. Structured responses may arrive as one final
update if the provider emits no incremental progress.
For trimmed/AOT apps, supply source-generated JSON serializer options for the
response type.

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

Apple Intelligence executes `AIFunction` tools supplied in `ChatOptions`.
The Windows provider rejects tool options.

```csharp
using System.ComponentModel;

var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeather, name: nameof(GetWeather))],
};
var response = await chat.GetResponseAsync("What's the weather in Boston?", options);

[Description("Get the demonstration weather for a city.")]
static string GetWeather(
    [Description("The city to check.")] string city) => $"{city}: sunny.";
```

This tool returns fixed demonstration data. Set `ToolMode = ChatToolMode.None`
to disable tools for a request.

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
Apple OS 26 ignores image content. Windows ignores non-text message content.
Unsupported message content is omitted, not replaced with synthetic descriptions;
errors processing supported content are still reported.

## Embeddings

Generate floating-point text vectors through `IEmbeddingGenerator`:

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

Image generation is separate from chat image input and tool calling.

| Platform | Native image generation |
|----------|-------------------------|
| Apple | Not supported |
| Windows | Coming soon |
| Android | Not specified |

Native image generation is not implemented in this version.

## Diagnostics

Reference `Microsoft.Extensions.AI` to add telemetry to either chat provider:

```csharp
var instrumentedChat = chat.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Use the wrapped client for requests, or return it from the provider factory.

Configure listeners/exporters for the `MyApp.AI` activity source and meter.
Telemetry includes timings, model metadata, and provider-supplied usage.
Fully enumerate streams for final usage; streaming also reports first-chunk
and per-chunk timings.
`EnableSensitiveData = false` excludes prompt and response content.

With M.E.AI 10.4.1, input/output tokens are span tags and
`gen_ai.client.token.usage` measurements; cached input is a span tag
(`gen_ai.usage.cache_read.input_tokens`). Total and reasoning counts remain
available on `UsageDetails`, without separate built-in span tags or metrics.
Apple OS 26 and Windows provide timings and model metadata without token usage.
Response IDs and finish reasons are recorded only when supplied by the provider.

For Apple tool logs, supply an `ILoggerFactory`: Debug records lifecycle events;
Trace includes arguments/results. Callbacks retain the request's execution
context and any active trace/span, including across async tool code.
