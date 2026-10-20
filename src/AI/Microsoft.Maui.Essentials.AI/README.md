# Microsoft.Maui.Essentials.AI

On-device chat and text embeddings through
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions),
backed by Apple Intelligence and NaturalLanguage. Chat supports streaming,
structured JSON, native tool execution, and image input on supported systems.
No cloud service is used.

> **Experimental:** this package ships as a preview. APIs may change.

## Requirements and platform support

Requires .NET 10 and matching MAUI workloads.

| Platform | Chat | Text embeddings |
|----------|------|-----------------|
| iOS | Apple Intelligence, OS 26+ | NaturalLanguage, OS 13+ |
| Mac Catalyst | Apple Intelligence, OS 26+ | NaturalLanguage, OS 13.1+ |
| macOS | Apple Intelligence, OS 26+ | NaturalLanguage, OS 10.15+ |
| Windows | Coming soon; details to be determined | Not supported |
| Android | Coming soon; details to be determined | Not supported |

Chat requires supported hardware with Apple Intelligence available and enabled.
Image input, token usage metadata, and explicit native tool-calling modes require
OS 27+ and a bridge built with Xcode 27. Image input also requires a ready
vision-capable model.

## Install

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Apple on-device chat and text embeddings |

```bash
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

## Quick start

Register the chat client only on a supported Apple runtime. For other platforms,
leave chat unavailable or supply an explicit app-selected fallback.

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

Check availability with the concrete client before offering chat:

```csharp
var status = await AppleIntelligenceChatClient.GetModelStatusAsync();
if (status.Availability != ChatModelAvailability.Available)
    Console.WriteLine(status.UnavailableReason);
```

Use the injected `IChatClient` (`chat`) for requests:

```csharp
var response = await chat.GetResponseAsync("Plan a day trip to Tokyo");
Console.WriteLine(response.Text);

await foreach (var update in chat.GetStreamingResponseAsync("Suggest a walking route"))
    Console.Write(update.Text);
```

### Structured responses and tools

```csharp
using System.ComponentModel;

var trip = await chat.GetResponseAsync<TripSuggestion>("Suggest a day trip near Seattle.");
Console.WriteLine($"{trip.Result.Destination}: {trip.Result.Reason}");

var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeather, name: nameof(GetWeather))],
};
var weather = await chat.GetResponseAsync("What's the weather in Boston?", options);
Console.WriteLine(weather.Text);

[Description("Get demonstration weather for a city.")]
static string GetWeather(
    [Description("The city to check.")] string city) => $"{city}: sunny.";

[Description("A day trip recommendation.")]
public sealed record TripSuggestion(
    [property: Description("The destination name.")] string Destination,
    [property: Description("A short reason to visit.")] string Reason);
```

Apple executes registered `AIFunction` tools natively. For trimmed/AOT apps,
supply source-generated JSON serializer options for structured response types.
See the shared guide for tool-mode, schema, sampling, history, and cancellation
policies.

### Embeddings

On Apple platforms:

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
- [Canonical Apple JSON Schema profile](https://github.com/dotnet/maui-labs/blob/main/docs/ai/apple-json-schema-profile.md) — supported schemas, optionality, numeric fidelity, and actionable rejections.
- [Chat Playground](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground)
- [Essentials AI sample](https://github.com/dotnet/maui-labs/tree/main/samples/EssentialsAISample)
- [Source and contributor build instructions](https://github.com/dotnet/maui-labs/tree/main/src/AI)
