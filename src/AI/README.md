# Essentials.AI

Build and test the Apple and Windows implementations of Essentials.AI.
Android integration is **coming soon**; its capabilities and requirements are
not specified.

> **Experimental:** packages ship as previews; APIs may change.

## Package and usage

| Package | Consumer documentation |
|---------|------------------------|
| `Microsoft.Maui.Essentials.AI` | [Install and use the package](Microsoft.Maui.Essentials.AI/README.md) |

The package README documents **Chat**, **Embeddings**, and **Image generation**
separately, with capability tables and usage examples.

The [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
compares providers interactively.

## Build

Use the .NET 10 SDK pinned in the repository's `global.json` and matching
MAUI workloads.

| Provider | Implementation | Native build requirements |
|----------|----------------|---------------------------|
| Apple Intelligence | `Platform/MaciOS` and the Swift bridge | macOS and Xcode; Xcode 27 for the OS 27 bridge |
| Windows AI | `Platform/Windows` and Windows App SDK | Windows and the SDK's Windows build tools |
| Android (coming soon) | Provider not available yet | Not specified |

The library always targets `net10.0` and Android. Apple targets are added on
non-Linux hosts, using either the default reference packs or the OS 27 packs
when `UseXcode27Preview=true`. Windows is appended only on Windows hosts.
The Chat Playground and device tests use the same platform conditions, without
the portable `net10.0` target or native macOS app target.
The existing Android target does not imply an available on-device provider.

### Apple

```sh
# macOS: native Swift bindings and managed projects
dotnet build src/AI/EssentialsAI.slnf

# Apple 27 reference packs, with Xcode 27 and matching workloads installed
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `ios27.0` or `macos27.0` for those platforms. `UseXcode27Preview` selects
reference-pack targets; it does not install or select Xcode. With Xcode 27
already selected, no `DEVELOPER_DIR` override is needed. Building against Apple
26.x reference packs with Xcode 27 requires `ValidateXcodeVersion=false`, as in CI.

### Windows

Build the Windows implementation without the Apple native build:

```powershell
dotnet build src\AI\Microsoft.Maui.Essentials.AI\Microsoft.Maui.Essentials.AI.csproj -f net10.0-windows10.0.19041.0
```

Full solution builds require Apple native artifacts built on macOS.
The prebuilt path activates with `CI=true` or `TF_BUILD=true`; those flags do not
supply the artifacts. CI downloads them before packing/signing on Windows. See
[ci-essentialsai.yml](../../.github/workflows/ci-essentialsai.yml) and the
[official pipeline](../../eng/pipelines/devflow-official.yml) for the artifact flow.

See the [Windows integration guide](../../docs/ai/WINDOWS-AI.md) for app packaging,
runtime selection, and model access.

## Tests and implementation

Portable tests live in `tests/AI/Microsoft.Maui.Essentials.AI.UnitTests`:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/Microsoft.Maui.Essentials.AI.UnitTests.csproj \
  -p:TargetFrameworks=net10.0 -p:IsTestProject=true
```

The overrides keep the build portable and enable Arcade's test infrastructure
for this existing test project without building the Apple targets.
Device tests use DeviceRunners with `dotnet test` and live in
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests`. They require an appropriate
Apple or Windows device/runtime and model availability; portable tests do not run a model.

- `Microsoft.Maui.Essentials.AI/Platform/MaciOS/`: Apple chat and embedding clients.
- `Microsoft.Maui.Essentials.AI/Platform/Windows/`: Windows chat client and model readiness.
- `AppleNative/EssentialsAI/` and `AppleBindings.targets`: Swift bridge, bindings, and `.xcframework` packaging.
- `JsonStreamChunker`, `PlainTextStreamChunker`, and `StreamingResponseHandler`: cumulative-response deltas and streaming updates.

Further design documentation:
[Apple image input](../../docs/ai/apple-foundation-models-image-input.md) and
[JSON stream chunking](../../docs/ai/json-stream-chunker-design.md).
