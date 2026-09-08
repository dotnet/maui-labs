---
name: maui-essentials-ai
description: >-
  Adopt `Microsoft.Maui.Essentials.AI` for local/on-device MAUI AI. USE FOR: Apple Intelligence chat, `IChatClient`, iOS/macOS/Mac Catalyst 26+ checks, fallback UI, `NLEmbeddingGenerator`, local tool invocation, privacy/offline UX. DO NOT USE FOR: source-generated tools, cloud-only AI, UI debugging.
---

# MAUI Essentials AI

Use this skill when a MAUI app should use on-device AI through
`Microsoft.Maui.Essentials.AI` and `Microsoft.Extensions.AI` abstractions.

## Response Checklist

- Mention the package and primary types explicitly:
  `Microsoft.Maui.Essentials.AI`, `AppleIntelligenceChatClient`,
  `NLEmbeddingGenerator`.
- Keep platform support explicit (Apple chat support is iOS/macOS/Mac Catalyst
  26+; embeddings have broader Apple OS coverage).
- For app tools, show `UseFunctionInvocation` and route source-generated tool
  definitions to `maui-ai-tool-bindings` when appropriate.

## Platform Support

Chat support:

| Platform | Status |
| --- | --- |
| iOS 26+ | Apple Intelligence / Foundation Models |
| Mac Catalyst 26+ | Apple Intelligence |
| macOS 26+ | Apple Intelligence |
| Android | Coming soon |
| Windows | Coming soon |

Embedding support:

| Platform | Status |
| --- | --- |
| iOS 13+ | NaturalLanguage embeddings |
| Mac Catalyst 13.1+ | NaturalLanguage embeddings |
| macOS 10.15+ | NaturalLanguage embeddings |
| Android | Not supported |
| Windows | Not supported |

Version numbers for Apple Intelligence support reflect currently announced OS
versions; verify final released platform versions before shipping.

Design fallback behavior for unsupported platforms. Do not silently route private
data to a cloud model unless the user explicitly wants a cloud fallback.

## Install and Register

```bash
dotnet add package Microsoft.Maui.Essentials.AI --prerelease
```

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Essentials.AI;

#if IOS || MACCATALYST || MACOS
builder.Services.AddSingleton<NLEmbeddingGenerator>();
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    sp.GetRequiredService<NLEmbeddingGenerator>());

if (OperatingSystem.IsIOSVersionAtLeast(26) ||
    OperatingSystem.IsMacCatalystVersionAtLeast(26) ||
    OperatingSystem.IsMacOSVersionAtLeast(26))
{
    builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
}
else
{
    // Leave IChatClient unregistered and check availability before use,
    // or register an app-specific unavailable implementation.
}
#endif
```

Register platform-specific implementations conditionally if the app targets
unsupported platforms too. If the app's minimum Apple OS target is below 26,
guard chat registration with runtime OS checks or register an app-specific
unavailable implementation for older OS versions. For MAUI Labs AppKit
(`MACOS`), verify the Essentials.AI integration against the target package
before shipping.

## Chat Workflow

1. Inject `IChatClient` into a view model or service, not directly into a page
   unless the app architecture already does that.
2. Pass cancellation tokens from commands and lifecycle boundaries.
3. Use streaming responses for interactive UI:

   ```csharp
   await foreach (var update in _chat.GetStreamingResponseAsync(messages, cancellationToken))
   {
       MainThread.BeginInvokeOnMainThread(() => ResponseText += update.Text);
   }
   ```

4. Keep conversation state in an app service so it survives page recreation.
5. Surface model availability and failure states in the UI.

If your view model framework already marshals property changes to the UI thread,
use that convention. Otherwise, dispatch UI-bound property updates explicitly.

## Embeddings and Semantic Search

Use `NLEmbeddingGenerator` for local semantic search over app-owned content. The
default constructor uses Apple's English sentence embedding; pass a
`NaturalLanguage.NLLanguage` or an existing `NaturalLanguage.NLEmbedding` when
the app needs a different supported language or embedding:

```csharp
#if IOS || MACCATALYST || MACOS
var generator = new NLEmbeddingGenerator();
var embeddings = await generator.GenerateAsync(
    ["sunset beach", "mountain hiking"],
    cancellationToken);
#endif
```

Recommended shape:

- chunk app content into small searchable records;
- generate embeddings at ingest/update time;
- store vector plus metadata locally;
- compare query embeddings with cosine similarity;
- return source snippets and IDs so the UI can show grounded results.

Avoid regenerating embeddings for the full corpus on every search.

## Tool Use

Essentials AI returns `Microsoft.Extensions.AI` clients, so normal tool calling
patterns apply:

```csharp
var innerClient = serviceProvider.GetService<IChatClient>();
if (innerClient is null)
{
    // Chat is unavailable on this platform/OS version; hide or disable the feature.
    return;
}

var appTools = YourAppTools.Default.Tools; // Define with [AIToolSource]; see maui-ai-tool-bindings.

var client = innerClient.AsBuilder()
    .UseFunctionInvocation()
    .ConfigureOptions(options =>
    {
        options.Tools ??= [];
        foreach (var tool in appTools)
            options.Tools.Add(tool);
    })
    .Build(serviceProvider);
```

Use `maui-ai-tool-bindings` when tools should be generated from app methods with
`[ExportAIFunction]`, DI parameter binding, or AOT-friendly definitions.

## Privacy and UX Guardrails

- Explain that AI runs on device for supported Apple platforms.
- Ask before adding a cloud fallback.
- Keep prompts, embeddings, and tool outputs within the app's data handling
  policy.
- Show unsupported-device and model-unavailable states.
- Do not block the UI thread while generating responses or embeddings.
- Use approval-required tools for actions that mutate data, send messages,
  purchase, delete, or navigate unexpectedly.

## Validation Checklist

- The app target platform supports the requested AI capability or has an
  explicit fallback.
- AI clients are registered in DI and consumed from services/view models.
- Streaming, cancellation, and error states are handled.
- Semantic search stores metadata with embeddings and avoids full re-ingest per
  query.
- Tool use is scoped and user-approved for sensitive actions.
