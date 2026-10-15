# Microsoft.Maui.Essentials.AI

On-device AI capabilities for .NET MAUI via [`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) abstractions.

> **Note:** This is the contributor/repo-browsing README. The NuGet consumer README with install instructions and full usage examples is at [`Microsoft.Maui.Essentials.AI/README.md`](Microsoft.Maui.Essentials.AI/README.md).

## Features

- **`IChatClient`** — backed by Apple Intelligence on Apple platforms and Gemini Nano through ML Kit GenAI Prompt on Android
- **Streaming** — native text deltas on Android, and progressive JSON/text snapshots on Apple platforms
- **Native token usage** — standard `ChatResponse.Usage` and streaming `UsageContent` on Apple OS 27+, without estimated counts
- **Telemetry** — compatible with M.E.AI's built-in `UseOpenTelemetry` spans, token metrics, and streaming timings
- **Tool calling** — function-calling support for Apple Intelligence; ML Kit Prompt beta4 does not expose native tools
- **Android native requests** — single-turn user text/images, native system instructions (Nano V3+), and full thinking output (Nano V4+); no structured output or assistant/tool history
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Platform Support

| Platform | Chat (IChatClient) | Image input | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|-------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | 27+ with vision-capable model | ✅ NL Embeddings |
| Android 8.0+ on supported AICore devices | ✅ Gemini Nano | ✅ Image prompts | 🔜 Coming soon |
| Windows | 🔜 Coming soon | Not available | 🔜 Coming soon |

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

// Register in MauiProgram.cs
#if ANDROID
if (OperatingSystem.IsAndroidVersionAtLeast(26))
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

Native request-mapping and lifecycle tests run as part of `assembleRelease`. To build only Android:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj -f net10.0-android
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-android
```

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

## Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) compiled via Xcode, producing `.xcframework` bundles
- **Kotlin Android bridge** (`AndroidNative/EssentialsAI/`) — builds native ML Kit requests and owns capability checks, streaming, cancellation, and per-request model cleanup behind a Java-friendly AAR
- **`GeminiNanoChatClient`** — the only Android public API; maps `Microsoft.Extensions.AI` roles/content/options to internal DTOs, prepending `ChatOptions.Instructions` as a system message like the Apple client
- **Native-only Android scope** — Kotlin combines ordered system text into `SystemInstruction` and maps user parts directly to native `Content`; assistant/tool/custom histories fail explicitly. No role tokens, prompt-prefix fallback, JSON schema guidance, structured-output validation, or tool middleware
- **Structured output** — Apple's existing schema transformation and native generation remain Apple-specific and unchanged
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization

## Documentation

- [JSON Stream Chunker Design](../../docs/ai/json-stream-chunker-design.md)
- [Apple Foundation Models Image Input](../../docs/ai/apple-foundation-models-image-input.md)

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Building Android from source also requires Java 17+ and the Android SDK; `AndroidGradleProject` builds the Kotlin AAR automatically
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+
- Gemini Nano requires a device listed in [ML Kit GenAI device support](https://developers.google.com/ml-kit/genai#prompt-device), current AICore system services, and a locked bootloader
- Android emulators do not provide AICore/Gemini Nano inference
- Android system instructions require Nano V3+, and full thinking output requires Nano V4+. Requests fail explicitly if the corresponding native capability is unavailable; no prompt workaround is used.
- Android apps can target API 24+, but Gemini Nano must be guarded with
  `OperatingSystem.IsAndroidVersionAtLeast(26)` and apps below API 26 need the
  native manifest overrides documented in the [consumer README](Microsoft.Maui.Essentials.AI/README.md#requirements). Successful AICore
  inference, image input, thinking, and parallel inference still require a
  provisioned supported physical device; JVM/managed tests do not prove model
  availability.

> ⚠️ **This package is experimental** (always ships as `-preview`). APIs may change between releases.
