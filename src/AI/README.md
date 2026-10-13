# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Native token usage** — standard `ChatResponse.Usage` and streaming `UsageContent` on Apple OS 27+, without estimated counts
- **Telemetry** — compatible with M.E.AI's built-in `UseOpenTelemetry` spans, token metrics, and streaming timings
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Apple sampling options

On OS 26+, standard `ChatOptions.TopP`, `TopK`, `Temperature`, `Seed`, and
`MaxOutputTokens` map to Foundation Models generation options. With no sampling
hints the default remains greedy. `Temperature` or `Seed` alone enables
unrestricted random sampling so those hints are not ignored. `TopP` must be
finite and in `[0, 1]`, temperature finite and nonnegative, and `TopK` and
`MaxOutputTokens` positive.
Supplying both `TopK` and `TopP` throws `ArgumentException` before native
generation. This intentional caller behavior change is specific to the Apple
adapter, which selects one Foundation Models sampling mode (previously TopP
was ignored), not a restriction on all `IChatClient` providers.

### Platform Support

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|-------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Android | 🔜 Coming soon | Not available | 🔜 Coming soon |
| Windows | 🔜 Coming soon | Not available | 🔜 Coming soon |

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

### Xcode 27 builds

CI builds the native Swift library on an Xcode 27 runner while keeping the
package's normal Apple target frameworks unchanged. To opt into Apple 27
reference packs locally, install the .NET 10.0.401 SDK and matching workloads,
use Xcode 27 (no `DEVELOPER_DIR` override when it is the default), and build with
`-p:UseXcode27Preview=true -f net10.0-maccatalyst27.0` (or the corresponding
`ios27.0` / `macos27.0` framework). `UseXcode27Preview` only changes this
repo's target-framework list; it does not install or select Xcode. When
building the normal Apple 26.x reference packs with Xcode 27, the native
CI job passes `ValidateXcodeVersion=false` to bypass the older packs' Xcode
version check.

### Shared sampling tests

The device-test `ChatClientOptionsTestsBase<T>` runs the same individual
sampling cases against `AppleIntelligenceChatClient` and the existing opt-in
OpenAI Responses client in both response paths. These cases check option
acceptance and nonempty output; Apple-specific native DTO mapping, range
validation, and combined-filter rejection are tested separately.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)
- [Apple Foundation Models Image Input](../../docs/ai/apple-foundation-models-image-input.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
