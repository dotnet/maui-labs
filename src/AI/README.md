# Essentials.AI contributor guide

Build, test, and contribute to `Microsoft.Maui.Essentials.AI`, which provides
on-device chat and embeddings through `Microsoft.Extensions.AI`.

> **Experimental:** packages ship as previews; APIs may change.

## Documentation

| Document | Purpose |
|----------|---------|
| [Package README](Microsoft.Maui.Essentials.AI/README.md) | Consumer installation, setup, examples, and capability tables |
| [Chat provider implementation guide](../../docs/ai/local-chat-provider.md) | Shared provider rules and Apple implementation targets |
| [Apple Foundation Models](../../docs/ai/apple-foundation-models.md) | Apple toolchain, availability, images, native bridge, and validation |
| [Canonical Apple JSON Schema profile](../../docs/ai/apple-json-schema-profile.md) | Managed schema compiler contract and native mapping |
| [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md) | Interactive provider comparison and diagnostics |

Apple chat is supported on iOS, Mac Catalyst, and macOS 26+ with Apple Intelligence
enabled. NaturalLanguage embeddings require iOS 13+, Mac Catalyst 13.1+, or
macOS 10.15+. Windows and Android chat providers are **coming soon**; their behavior
remains **To be determined** in the shared implementation guide.

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Essentials.AI` | Apple on-device chat, tools, structured output, and text embeddings |

For installation and a quick start, see the [package README](Microsoft.Maui.Essentials.AI/README.md).

## Prerequisites

- The .NET SDK pinned in the repository `global.json`.
- Matching MAUI workloads.
- macOS with Xcode 27 for the Apple native bridge and OS 27 APIs.

## Build

### Apple

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` or `net10.0-macos27.0` for those platforms.
`UseXcode27Preview` selects the OS 27 reference packs; it does not install or
select Xcode.

### Full product

```bash
dotnet build src/AI/EssentialsAI.slnf
```

Official Windows packing consumes Apple native artifacts built by the macOS
job. `CI=true` or `TF_BUILD=true` selects the prebuilt path but does not create
or download those artifacts by itself.

## Test

### Portable

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/Microsoft.Maui.Essentials.AI.UnitTests.csproj \
  -p:TargetFrameworks=net10.0
```

### Apple native

```bash
swift test --package-path src/AI/AppleNative
```

Run live Foundation Models tests on supported hardware:

```bash
ESSENTIALSAI_LIVE_TESTS=1 swift test --package-path src/AI/AppleNative
```

### Device tests

DeviceRunners-based tests live in
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests`.
Use the appropriate Apple target. Model-dependent cases are tagged
`RequiresModel=true`; exclude them with:

```bash
--filter "RequiresModel!=true"
```

For live OpenAI baseline tests, configure the device-test user secrets:

- `AI:ApiKey`
- `AI:Endpoint`
- `AI:DeploymentName`
- `AI:EmbeddingDeploymentName`

Build Debug with `-p:EnableOpenAIClient=true` and run the
`OpenAIChatClient` suites. These make real requests against the configured
deployment.

## Architecture

- `Microsoft.Maui.Essentials.AI/Platform/MaciOS/` — managed Apple chat and
  embedding clients.
- `AppleNative/EssentialsAI/` — Swift Foundation Models bridge.
- `AppleNative/ApiDefinitions.cs` and `AppleBindings.targets` — bindings and
  native artifact packaging.
- `AppleNative/EssentialsAI/PlainTextDeltas.swift` and
  `StructuredJsonDeltas.swift` — native append-only streaming conversion.
- `Microsoft.Maui.Essentials.AI/Platform/StreamingResponseHandler.cs` —
  managed streaming update transport.
- `tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Tests/` — shared
  OpenAI-baseline suites and provider-specific overrides.

Keep provider-neutral behavior in the shared test bases. Put native SDK,
marshalling, OS-version, availability, and provider-policy checks in the
provider-specific suites.
