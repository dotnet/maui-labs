# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Platform Support

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Android | 🔜 Coming soon | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon |

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

## Device tests

The shared chat response tests require a nonempty `ChatResponse.ModelId`. The
streaming tests check every `ChatResponseUpdate`, including metadata-only updates,
for a nonempty, consistent model ID and verify the aggregated response retains it.
Apple Intelligence and OpenAI inherit the same assertions.

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst --filter "FullyQualifiedName~ModelId"
```

For live OpenAI testing, add `-p:EnableOpenAIClient=true`. Configure the device
test project's user secrets (`808cc184-141a-409e-addd-565c973dbce6`) with
`AI:ApiKey`, `AI:Endpoint`, `AI:DeploymentName`, and `AI:EmbeddingDeploymentName`.
Use an OpenAI-compatible Responses endpoint, such as Azure OpenAI's `/openai/v1/`
endpoint. The OpenAI tests use `GetResponsesClient().AsIChatClient(...)`, matching
the chat playground's Responses API path rather than the older Chat Completions
adapter.

The older Chat Completions adapter has a separately tracked
[streaming model-ID defect](https://github.com/dotnet/extensions/issues/7812):
an empty model field on the initial annotation is retained for the entire
adapted stream, despite later native completion chunks carrying model IDs.
This was reproduced with adapter versions 10.4.1 and 10.10.1. These tests and
the chat playground use the Responses adapter, not the affected adapter.

These tests contact real models and require Apple Intelligence to be available
for the Apple cases. Debug builds embed the configured user secrets; do not
distribute the resulting test app.

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
