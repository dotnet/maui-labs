# Essentials.AI

On-device chat and embeddings for .NET MAUI through `Microsoft.Extensions.AI`.
This is the contributor README; see the package documentation below for consumer APIs.

> **Experimental:** packages ship as previews; APIs may change.

## Package and usage

| Package | Consumer documentation |
|---------|------------------------|
| `Microsoft.Maui.Essentials.AI` | [Install and use the package](Microsoft.Maui.Essentials.AI/README.md) |

The package README owns platform requirements, setup, and API examples.

The [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
compares providers interactively.

## Build

Use the .NET 10 SDK pinned in the repository's `global.json` and matching
MAUI workloads. Apple native bindings require Xcode.

The library always targets `net10.0` and Android. Apple targets are added on
non-Linux hosts, using either the default reference packs or the OS 27 packs
when `UseXcode27Preview=true`. Windows is appended only on Windows hosts.
The Chat Playground and device tests use the same platform conditions, without
the portable `net10.0` target or native macOS app target.

```sh
# macOS: native Swift bindings and managed projects
dotnet build src/AI/EssentialsAI.slnf

# Apple 27 reference packs, with Xcode 27 and matching workloads installed
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use the corresponding `ios27.0` or `macos27.0` framework for those platforms.
`UseXcode27Preview` selects this repository's target-framework list; it does not
install or select Xcode. When Xcode 27 is the default, no `DEVELOPER_DIR` override
is needed. For normal Apple 26.x reference packs with Xcode 27, the native CI job
passes `ValidateXcodeVersion=false` to bypass the older packs' Xcode version check.

Full solution builds on Windows require prebuilt Apple native artifacts.
The prebuilt-artifact path activates only with `CI=true` or `TF_BUILD=true`;
setting a flag alone does not supply the artifacts. CI builds them on macOS
before packing/signing on Windows. See
[ci-essentialsai.yml](../../.github/workflows/ci-essentialsai.yml) and the
[official pipeline](../../eng/pipelines/devflow-official.yml) for the artifact flow.

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
Apple device/runtime and model availability; portable tests do not run a model.

Run every model-free provider binding on Mac Catalyst:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -c Release \
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

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -c Debug -p:EnableOpenAITests=true --logger trx
```

Use a filter only for diagnosis, never as the final validation result:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -c Debug -p:EnableOpenAIClient=true \
  --filter "FullyQualifiedName~OpenAIChatClient" --logger trx
```

- `AppleNative/EssentialsAI/`: Swift bridge and binding definitions, producing `.xcframework` bundles.
- `Microsoft.Maui.Essentials.AI/Platform/`: platform clients and streaming helpers.
- `JsonStreamChunker`, `PlainTextStreamChunker`, and `StreamingResponseHandler`: cumulative-response deltas and streaming updates.
- `AppleBindings.targets`: native artifact build and packaging.

Further design documentation:
[Local chat provider contract](../../docs/ai/local-chat-provider.md),
[Apple image input](../../docs/ai/apple-foundation-models-image-input.md) and
[JSON stream chunking](../../docs/ai/json-stream-chunker-design.md).
