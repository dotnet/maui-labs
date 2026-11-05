# Essentials.AI contributor guide

Build, test, and contribute to `Microsoft.Maui.Essentials.AI`, which provides
on-device chat and embeddings through `Microsoft.Extensions.AI`.

> **Experimental:** packages ship as previews; APIs may change.

## Documentation

| Document | Purpose |
|----------|---------|
| [Package README](Microsoft.Maui.Essentials.AI/README.md) | Consumer installation, setup, examples, and capability tables |
| [Chat provider implementation guide](../../docs/ai/local-chat-provider.md) | Shared provider rules and reviewed platform implementation targets |
| [Apple Foundation Models](../../docs/ai/apple-foundation-models.md) | Apple toolchain, availability, images, native bridge, and validation |
| [Windows AI language models](../../docs/ai/windows-ai-language-models.md) | Windows packaging, dependencies, model setup, and validation |
| [Gemini Nano Android](../../docs/ai/gemini-nano-android.md) | Android binding, AICore readiness, download, and validation |
| [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md) | Interactive provider comparison and diagnostics |

Apple chat is supported on iOS, Mac Catalyst, and macOS 26+ with Apple Intelligence
enabled. NaturalLanguage embeddings require iOS 13+, Mac Catalyst 13.1+, or
macOS 10.15+. Windows AI chat is supported on eligible Windows 11 24H2+
Copilot+ systems. Gemini Nano chat requires API 26+ and supported AICore
hardware.

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Apple, Windows, and Android on-device chat, plus Apple text embeddings |

For installation and a quick start, see the [package README](Microsoft.Maui.Essentials.AI/README.md).

## Prerequisites

- The .NET SDK pinned in the repository `global.json`.
- Matching MAUI workloads.
- macOS with Xcode 27 for the Apple native bridge and OS 27 APIs.
- Windows build and packaging tools for the Windows target and MSIX app heads.
- Java 17+ and the Android SDK for Android builds.

## Build

### Apple

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` or `net10.0-macos27.0` for those platforms.
`UseXcode27Preview` selects the OS 27 reference packs; it does not install or
select Xcode.

### Android

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-android
```

See the [Gemini Nano Android guide](../../docs/ai/gemini-nano-android.md) for
the native bridge, model readiness, and validation details.

The official pipeline's Android dependency step queries the SDK path resolved by
`InstallAndroidDependencies` and exports it as `ANDROID_HOME` and
`ANDROID_SDK_ROOT` for subsequent steps. This lets the standalone Kotlin bridge
build use the same SDK as .NET, even when the agent does not predefine those
environment variables. The step checks for the bridge's Android 36 platform
before running Gradle.
On Windows, it also queries `JavaSdkDirectory`, verifies the resolved
`bin/java.exe` can run, and exports `JAVA_HOME`. Standalone Gradle therefore
does not depend on Java being present on the agent's `PATH`.

### Windows

```powershell
dotnet build src\AI\Microsoft.Maui.Essentials.AI\Microsoft.Maui.Essentials.AI.csproj `
  -f net10.0-windows10.0.19041.0
```

Build and package Windows app heads on Windows. See the Windows platform guide
for MSIX, capability, runtime, and physical-device validation requirements.

### Full product

The library always targets `net10.0` and Android. Apple targets are added on
non-Linux hosts, using either the default reference packs or the OS 27 packs
when `UseXcode27Preview=true`. Windows is appended only on Windows hosts.
The Chat Playground and device tests use the same platform conditions, without
the portable `net10.0` target or native macOS app target.

```bash
dotnet build src/AI/EssentialsAI.slnf
```

For normal Apple 26.x reference packs with Xcode 27, the native CI job passes
`ValidateXcodeVersion=false` to bypass the older packs' Xcode version check.
When Xcode 27 is the default, no `DEVELOPER_DIR` override is needed.

Official Windows packing consumes Apple native artifacts built by the macOS
job. `CI=true` or `TF_BUILD=true` selects the prebuilt path but does not create
or download those artifacts by itself. See
[ci-essentialsai.yml](../../.github/workflows/ci-essentialsai.yml) and the
[official pipeline](../../eng/pipelines/devflow-official.yml) for the artifact flow.

## Test

### Portable

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/Microsoft.Maui.Essentials.AI.UnitTests.csproj \
  -p:TargetFrameworks=net10.0 -p:IsTestProject=true
```

The overrides keep the build portable and enable Arcade's test infrastructure
for this existing test project without building the Apple targets. Device tests
use DeviceRunners with `dotnet test` and live in
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests`. They require an appropriate
platform device/runtime and model availability; portable tests do not run a model.

### Apple native

```bash
swift test --package-path src/AI/AppleNative
```

### Android native

Android source builds require Java 17+ and the Android SDK. The Gradle bridge
tests run as part of the Android library build. See the
[Gemini Nano Android guide](../../docs/ai/gemini-nano-android.md) for commands
and binding details.

Run live Foundation Models tests on supported hardware:

```bash
ESSENTIALSAI_LIVE_TESTS=1 swift test --package-path src/AI/AppleNative
```

### Device tests

DeviceRunners-based tests live in
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests`.
Use the appropriate Apple, Android, or Windows target. Model-dependent cases
are tagged `RequiresModel=true`; exclude them with:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst27.0 -c Release -p:UseXcode27Preview=true \
  --filter "RequiresModel!=true" --logger trx
```

For live OpenAI baseline tests, configure the device-test user secrets:

- `AI:ApiKey`
- `AI:Endpoint`
- `AI:DeploymentName`
- `AI:EmbeddingDeploymentName`

`EnableOpenAIClient=true` and `EnableOpenAITests=true` both compile the OpenAI
fixtures. Use a Debug test run so the user-secrets file is embedded. The
unfiltered command runs the complete Apple and live OpenAI device assembly and
makes real requests against the configured deployments:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst27.0 -c Debug -p:UseXcode27Preview=true \
  -p:EnableOpenAITests=true --logger trx
```

Use filters only for diagnosis, never as the final validation result.

For example, isolate the OpenAI chat baseline while diagnosing a failure:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst27.0 -c Debug -p:UseXcode27Preview=true \
  -p:EnableOpenAIClient=true --filter "FullyQualifiedName~OpenAIChatClient" --logger trx
```

## Architecture

- `Microsoft.Maui.Essentials.AI/Platform/MaciOS/` — managed Apple chat and
  embedding clients.
- `AppleNative/EssentialsAI/` — Swift Foundation Models bridge.
- `AppleNative/ApiDefinitions.cs` and `AppleBindings.targets` — bindings and
  native artifact packaging.
- `AndroidNative/EssentialsAI/` — Kotlin Gemini Nano bridge and native tests.
- `Microsoft.Maui.Essentials.AI/Platform/Android/` — managed Android chat client
  and ML Kit callback adapters.
- `AppleNative/EssentialsAI/PlainTextDeltas.swift` and
  `StructuredJsonDeltas.swift` — native append-only streaming conversion.
- `Microsoft.Maui.Essentials.AI/Platform/Windows/` — Windows chat, explicit
  readiness, WinRT streaming, and request ownership.
- `Microsoft.Maui.Essentials.AI/Platform/StreamingResponseHandler.cs` —
  managed streaming update transport.
- `tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Tests/` — shared
  OpenAI-baseline suites and provider-specific overrides.

Keep provider-neutral behavior in the shared test bases. Put native SDK,
marshalling, OS-version, availability, and provider-policy checks in the
provider-specific suites.
