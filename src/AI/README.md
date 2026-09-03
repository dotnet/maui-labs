# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **`IImageClassificationClient`** — provider-neutral contracts for classifying encoded images
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Platform Support

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) | Image classification contract |
|----------|-------------------|----------------------------------|-------------------------------|
| iOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Provider-neutral contract |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Provider-neutral contract |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings | ✅ Provider-neutral contract |
| Android | 🔜 Coming soon | 🔜 Coming soon | ✅ Provider-neutral contract |
| Windows | 🔜 Coming soon | 🔜 Coming soon | ✅ Provider-neutral contract |

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

// Register in MauiProgram.cs
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());

// Use via DI
var client = serviceProvider.GetRequiredService<IChatClient>();
var response = await client.GetResponseAsync("Plan a weekend trip to Portland");
```

Image classification providers implement `IImageClassificationClient`; consumer code remains provider-neutral:

```csharp
await using Stream image = File.OpenRead("photo.jpg");
ImageClassificationResult result =
    await classifier.ClassifyImageAsync(image, "image/jpeg");

ImageClassificationPrediction? best = result.Predictions.FirstOrDefault();
```

`MaximumPredictions` is an at-most constraint. Confidence is optional, and providers without confidence support reject a non-null `MinimumConfidence`. `ChatClientImageClassificationClient` adapts a dedicated vision-capable `IChatClient` to a snapshotted label allowlist while preserving response ranking and the original `ChatResponse`. It prefers structured output and narrowly falls back to a top-level JSON string array when a client ignores the requested format.

Provider identity is exposed through `ImageClassificationClientMetadata`; individual results retain model provenance and optional raw or additional response metadata. Callers retain ownership of input streams and of any chat client passed to the adapter.

## Packages

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Essentials.AI` | On-device AI APIs for MAUI |

## Building

```bash
# macOS (builds Swift bindings + .NET library)
dotnet build src/AI/EssentialsAI.slnf

# Windows (CI only — the Azure DevOps pipeline downloads macOS-built
# native artifacts automatically. Local Windows builds require CI=true
# or TF_BUILD=true for the pre-built artifact path to activate.)
```

The CI pipeline handles the macOS → Windows artifact flow automatically. See `.github/workflows/ci-essentialsai.yml` for details.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
