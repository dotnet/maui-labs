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

Build the project directly; its native Swift bridge is built with it:

```sh
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

See the [Windows integration guide](../../docs/ai/WINDOWS-AI.md) for app packaging,
runtime selection, and model access.

### Full product build

To build all product projects instead of one target:

```sh
dotnet build src/AI/EssentialsAI.slnf
```

This is an alternative, not a prerequisite for project-level builds.
Full builds on Windows require Apple native artifacts built on macOS.
The prebuilt path activates with `CI=true` or `TF_BUILD=true`; those flags do not
supply the artifacts. CI downloads them before packing/signing on Windows. See
[ci-essentialsai.yml](../../.github/workflows/ci-essentialsai.yml) and the
[official pipeline](../../eng/pipelines/devflow-official.yml) for the artifact flow.

## Tests and implementation

Android source builds require Java 17+ and the Android SDK. See the
[Gemini Nano Android guide](../../docs/ai/gemini-nano-android.md) for native build
commands and binding details.

Portable tests live in `tests/AI/Microsoft.Maui.Essentials.AI.UnitTests`:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/Microsoft.Maui.Essentials.AI.UnitTests.csproj
```

This restores and builds the test project and its references automatically.
Optionally add `-p:TargetFrameworks=net10.0` to limit the referenced library's
multi-target restore to the portable framework.
Device tests use DeviceRunners with `dotnet test` and live in
`tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests`. They require an appropriate
Apple or Windows device/runtime and model availability; portable tests do not run a model.

Shared chat suites provide the OpenAI baseline for message conversion,
instructions, streaming, structured output, cancellation, and usage.
Apple and Windows bind to applicable suites; future Android coverage should use
the same bases. Native API and OS-version checks remain provider-specific.

For live cloud baselines, configure the device project's user secrets:
`AI:ApiKey`, `AI:Endpoint` (Azure `/openai/v1/`), `AI:DeploymentName`, and
`AI:EmbeddingDeploymentName`. Build in Debug with `-p:EnableOpenAIClient=true`
and select the `OpenAIChatClient` tests. These make real requests against
`gpt-6-luna`; sampling tests disable reasoning when setting temperature.
Conflicting instructions have no shared last-wins guarantee.

| Provider | Provider-specific tests |
|----------|-------------------------|
| Apple Intelligence | .NET device tests in `Tests/MaciOS`; Swift bridge tests below run independently of .NET. |
| Windows AI | Status handling, streaming lifecycle, and history conversion in `Tests/Windows`; native execution requires Windows. |
| Android (coming soon) | No provider-specific suite yet. |

Run native Swift tests on macOS 26+ with Xcode 27 (also run in CI):

```sh
swift test --package-path src/AI/AppleNative
```

These compile the production Swift bridge and test sampling, schema conversion,
and native error callbacks. To also exercise real model generation and streaming
on a host with Apple Intelligence enabled, prefix the command with
`ESSENTIALSAI_LIVE_TESTS=1`.

- `Microsoft.Maui.Essentials.AI/Platform/MaciOS/`: Apple chat and embedding clients.
- `Microsoft.Maui.Essentials.AI/Platform/Windows/`: Windows chat client and model readiness.
- `AppleNative/EssentialsAI/` and `AppleBindings.targets`: Swift bridge, bindings, and `.xcframework` packaging.
- `JsonStreamChunker`, `PlainTextStreamChunker`, and `StreamingResponseHandler`: cumulative-response deltas and streaming updates.

Further design documentation:
[Apple image input](../../docs/ai/apple-foundation-models-image-input.md) and
[JSON stream chunking](../../docs/ai/json-stream-chunker-design.md).
