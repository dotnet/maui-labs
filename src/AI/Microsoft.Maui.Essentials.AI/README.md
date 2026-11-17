# Microsoft.Maui.Essentials.AI

On-device chat and text embeddings for .NET MAUI through
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions),
backed by Apple Intelligence, Apple NaturalLanguage, Windows AI, and Gemini Nano.
No cloud service is used.

> **Experimental:** this package ships as a preview. APIs may change.

## Platform support

| Platform | Chat (`IChatClient`) | Image input | Embeddings (`IEmbeddingGenerator`) |
|----------|---------------------|-------------|-----------------------------------|
| iOS 26+ | Apple Intelligence, with tools and structured responses | OS 27+ | NaturalLanguage, iOS 13+ |
| Mac Catalyst 26+ | Apple Intelligence, with tools and structured responses | OS 27+ | NaturalLanguage, Mac Catalyst 13.1+ |
| macOS 26+ | Apple Intelligence, with tools and structured responses | OS 27+ | NaturalLanguage, macOS 10.15+ |
| Windows 11 24H2+ | Windows AI on Copilot+ hardware, with structured responses | Not supported | Not supported |
| Android API 26+ | Gemini Nano on supported AICore hardware, with structured responses; tools on capable models | In-memory images | Not supported |

Requires .NET 10 and the matching MAUI workloads.

## Install

```bash
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

## Set up a chat client

Register the client for the current platform, and check that its model is ready
before offering chat. Generation never downloads a model.

### Apple

Apple Intelligence must be available and enabled. Image input, token usage and
explicit tool modes need OS 27+ and a native bridge built with Xcode 27+. Response
and tool schemas that allow `null` values need OS 26.4+.

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
#endif
```

```csharp
var status = await AppleIntelligenceChatClient.GetModelStatusAsync();
if (status.Availability != ChatModelAvailability.Available)
    Console.WriteLine(status.Availability);
```

### Windows

The app must be MSIX-packaged, declare the `systemAIModels` capability, and use a
compatible Windows App SDK with the applicable model-access configuration.

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

### Android

```csharp
#if ANDROID
if (OperatingSystem.IsAndroidVersionAtLeast(26))
{
    builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
}
#endif
```

Download the model only after an explicit app or user decision.
`PrepareModelAsync` waits for the download to finish; use `DownloadModelAsync`
to show ML Kit's progress, then query the status again.

```csharp
var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
if (status.Availability == ChatModelAvailability.NotReady)
    await GeminiNanoChatClient.PrepareModelAsync(cancellationToken);
```

The status lists the model's capabilities, such as
`ChatModelCapability.StructuredOutput` and `ChatModelCapability.ToolCalling`.
Set `MaxOutputTokens` for longer responses: when it is unset, the default Nano V3
model stops after about 40 tokens.

## Chat

Use the injected `IChatClient`:

```csharp
var response = await chat.GetResponseAsync("Plan a day trip to Tokyo");
Console.WriteLine(response.Text);

await foreach (var update in chat.GetStreamingResponseAsync("Suggest a walking route"))
    Console.Write(update.Text);
```

## Structured responses

Use `GetResponseAsync<T>` to generate a schema and deserialize a typed response.
For trimmed and AOT apps, pass source-generated `JsonSerializerOptions`:

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

Each platform supports its own subset of JSON Schema; see the platform guides.
Streamed structured responses can arrive as one update when the platform doesn't
report partial output.

On Android, the selected model must report structured output support. As with
OpenAI, the schema root must be an object and the JSON is returned unchanged.
Schema-free JSON is not supported. Structured output works with streaming, tools
and thinking; see the
[structured output guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-structured-output.md)
for supported keywords.

## Tools

Supply `AIFunction` tools through `ChatOptions`. Set `ChatOptions.ToolMode` to
`ChatToolMode.None` to disable them for a request.

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

- **Apple** runs the tools itself and returns their results to the model.
- **Android** returns the model's tool calls, so wrap the client to run them:
  `chat.AsBuilder().UseFunctionInvocation().Build()`. Tools need a model that
  reports `ChatModelCapability.ToolCalling`, such as `gemini-nano-preview-fast`;
  the default Nano V3 model has no tool support. `ChatToolMode.RequireAny` is
  not supported. See the
  [tool-calling guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md#native-tool-calling).
- **Windows** doesn't support tools.

## Image input

Add images as `DataContent` parts:

```csharp
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe this photo."),
    new DataContent(pngBytes, "image/png"),
]);
var response = await chat.GetResponseAsync([message]);
```

- **Apple** needs OS 27+ and a ready vision-capable model; on OS 26 image
  requests throw. `RawRepresentation` can carry a `CGImage`, `UIImage` or
  `NSImage`, and local `file://` image `UriContent` is supported. Remote URLs are
  rejected rather than fetched.
- **Android** accepts in-memory images.

## Token usage

```csharp
var response = await chat.GetResponseAsync("Summarize this text.");
Console.WriteLine($"Input: {response.Usage?.InputTokenCount}");
Console.WriteLine($"Output: {response.Usage?.OutputTokenCount}");
```

Usage is per request; add it up with `UsageDetails.Add`. Streams end with a
`UsageContent` update, and `ToChatResponseAsync()` collects it into
`ChatResponse.Usage`.

- **Apple** reports input, output, cached-input, reasoning and total counts on
  OS 27+, and none on OS 26.
- **Android** reports input tokens.
- **Windows** doesn't report usage.

## Telemetry

Wrap any client with the `Microsoft.Extensions.AI` OpenTelemetry middleware:

```csharp
var instrumentedChat = chat.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "MyApp.AI",
        configure: telemetry => telemetry.EnableSensitiveData = false)
    .Build();
```

Configure listeners or exporters for the `MyApp.AI` activity source and meter.
The middleware records request timing, time to first chunk and between chunks,
model metadata and token usage. Fully enumerate streams to capture final usage.
With `EnableSensitiveData = false`, prompts and responses are excluded.

For Apple tool diagnostics, pass an `ILoggerFactory` to the client: lifecycle
logs use Debug, and tool arguments and results use Trace.

## Embeddings

```csharp
using var generator = new NLEmbeddingGenerator();
var embeddings = await generator.GenerateAsync(["sunset beach", "mountain hiking"]);
```

The default generator uses English sentence embeddings. Pass an `NLLanguage` to
the constructor for another supported language, or wrap an existing
`NLEmbedding`. The language's model asset must be installed.

## Document extraction

On iOS, Mac Catalyst, and macOS 26+, `AppleVisionRecognizeDocumentsClient` extracts
text, tables, and source geometry on-device with Apple Vision. It implements the
experimental `Microsoft.Extensions.DocumentExtraction` client and does not require
Apple Intelligence.

| Input | Support |
|-------|---------|
| PDF | Each page is rendered with PDFKit |
| JPEG, PNG, HEIC, TIFF | Orientation is honored; only the first frame is used |

```csharp
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

using DocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
await using var input = File.OpenRead("receipt.pdf");
ExtractionDocument result = await client.ExtractAsync(input, "receipt", "application/pdf");
foreach (var element in result.EnumerateContent().OfType<ExtractionDocumentTextElement>())
    Console.WriteLine(((TextContent)element.Content!).Text);
```

The input stream is not disposed. Data the shared model has no place for, such as
confidence, OCR candidates, list structure, and detected entities, is available as
JSON through `RawRepresentation`.

See the [Apple document extraction guide](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-document-extraction.md)
for architecture, request settings, and fidelity limits.

## Guides and samples

- [Apple Foundation Models](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-foundation-models.md) — availability, images, tools, and native bridge.
- [Windows AI language models](https://github.com/dotnet/maui-labs/blob/main/docs/ai/windows-ai-language-models.md) — packaging, readiness, and dependencies.
- [Gemini Nano Android](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-android.md) — AICore readiness, download, models, and tools.
- [Gemini Nano structured output](https://github.com/dotnet/maui-labs/blob/main/docs/ai/gemini-nano-structured-output.md) — supported schemas, streaming, tools, and thinking.
- [Chat provider contract](https://github.com/dotnet/maui-labs/blob/main/docs/ai/local-chat-provider.md) — shared behavior across providers.
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground) and [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Source and build instructions](https://github.com/dotnet/maui-labs/tree/main/src/AI)
