# Microsoft.Maui.Essentials.AI

Contributor guide for the `Microsoft.Maui.Essentials.AI` package: Apple Intelligence
chat, NaturalLanguage embeddings, and Gemini Nano chat through `Microsoft.Extensions.AI`.

For installation, platform support, and API examples, use the
[package README](Microsoft.Maui.Essentials.AI/README.md).

> This package is experimental and ships as `-preview`. APIs may change.

## Source layout

| Path | Purpose |
| --- | --- |
| `Microsoft.Maui.Essentials.AI/` | Managed providers, native bindings, and streaming utilities |
| `AppleNative/` | Swift bridge and Xcode project for Foundation Models |
| `AndroidNative/` | Kotlin bridge and Gradle project for ML Kit |

## Build and test

Run from the repository root with the SDK pinned in `global.json` and matching
MAUI workloads. Android builds need Java 17+ and the Android SDK. Apple native
builds need macOS and Xcode 27; Windows CI reuses the macOS-built artifacts.

```bash
dotnet build src/AI/EssentialsAI.slnf
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/
dotnet test tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests/
```

For an Android-only build:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj -f net10.0-android
```

Native Android tests run during the Gradle build. Model-dependent tests need
supported physical hardware; see the platform guides below for build and device
validation details.

## Samples

- [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md):
  compare providers, stream replies, use images, and record/replay conversations.
- [Essentials AI sample](../../samples/EssentialsAISample/):
  additional examples built alongside the library.

## Documentation

- [Apple build, image input, and validation](../../docs/ai/apple-foundation-models-image-input.md)
- [Gemini Nano Android implementation](../../docs/ai/gemini-nano-android.md)
- [JSON stream chunker design](../../docs/ai/json-stream-chunker-design.md)
