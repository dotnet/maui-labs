# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence on Apple platforms and Gemini Nano through ML Kit GenAI Prompt on Android
- **Streaming** — native text deltas on Android, and progressive JSON/text snapshots on Apple platforms
- **Tool calling** — function-calling support for Apple Intelligence; ML Kit Prompt beta4 does not expose native tools
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Platform Support

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Android 8.0+ on supported AICore devices | ✅ Gemini Nano | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon |

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

// Register in MauiProgram.cs
#if ANDROID
builder.Services.AddSingleton<IChatClient>(new GeminiNanoChatClient());
#elif IOS || MACCATALYST || MACOS
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
#endif

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

The product solution builds the
[`Chat Playground`](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
and its portable chat tests. `samples/EssentialsAISample` is unchanged and is
not the Gemini Nano validation sample.

### Android native build

.NET 10's `AndroidGradleProject` builds the Kotlin bridge automatically using
the checked-in wrapper. Gradle also exports the pinned Prompt/Common beta4 and
Schema alpha1 archives; .NET packages those runtime assets without generating
public ML Kit bindings. Published NuGets supply the shared Kotlin, coroutine,
AndroidX, and Google dependencies. No local unpublished GenAI packages are used.

Native lifecycle tests run as part of `assembleRelease`. To build only Android:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj -f net10.0-android
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-android
```

### Xcode 27 builds

CI builds the native Swift library on an Xcode 27 runner while keeping the
package's normal Apple target frameworks unchanged. To opt into Apple 27
reference packs locally, install the .NET 10.0.401 SDK and matching workloads,
select Xcode 27 with `DEVELOPER_DIR`, and build with
`-p:UseXcode27Preview=true -f net10.0-maccatalyst27.0` (or the corresponding
`ios27.0` / `macos27.0` framework). `UseXcode27Preview` only changes this
repo's target-framework list; it does not install or select Xcode. When
building the normal Apple 26.x reference packs with Xcode 27, the native
CI job passes `ValidateXcodeVersion=false` to bypass the older packs' Xcode
version check.

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **Kotlin Android bridge** (`AndroidNative/EssentialsAI/`) — wraps stateless per-request ML Kit GenAI Prompt inference, streaming, cancellation, and Nano V4 thinking behind a Java-friendly AAR
- **`GeminiNanoChatClient`** — the only Android public API; maps `Microsoft.Extensions.AI` messages/options to the internal Kotlin bridge
- **Structured output** — shared Apple schema normalization; Android uses prompt guidance and strict managed validation rather than a mutable Kotlin provider registry
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Building Android from source also requires Java 17+ and the Android SDK; `AndroidGradleProject` builds the Kotlin AAR automatically
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+
- Gemini Nano requires a device listed in [ML Kit GenAI device support](https://developers.google.com/ml-kit/genai#prompt-device), current AICore system services, and a locked bootloader
- Android emulators do not provide AICore/Gemini Nano inference
- Android apps referencing this package require API 26+. Successful AICore
  inference, image input, thinking, and parallel inference still require a
  provisioned supported physical device; JVM/managed tests do not prove model
  availability.

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
