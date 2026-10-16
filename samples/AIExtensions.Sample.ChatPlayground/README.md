# AI Chat Playground

A .NET MAUI sample for comparing Apple Intelligence, Windows AI, Azure OpenAI,
and offline Replay through the same chat UI. It runs on Android, iOS, Mac Catalyst,
and Windows (not native macOS); providers vary by platform.
An Android on-device provider is **coming soon**; its capabilities and setup
are not specified yet.

## What you can try

| Tab | Features | Providers |
|-----|----------|-----------|
| Chat | Text, streaming, structured JSON, optional tools and image input | Apple Intelligence (iOS/Mac Catalyst), Windows AI, Azure OpenAI, offline Replay |
| Embeddings | Import documents, index them, and search | Apple NaturalLanguage (iOS/Mac Catalyst), Azure OpenAI |
| Images | Generate images or edit a source image | Azure OpenAI |

### Providers

| Provider | Availability | Capabilities and setup |
|----------|--------------|------------------------|
| Apple Intelligence | iOS / Mac Catalyst 26+ on supported hardware | Text, streaming, structured JSON, and tools. Image input requires OS 27 and a ready vision model. NaturalLanguage embeddings require the language asset. |
| Windows AI | Windows 11 24H2+ on supported Copilot+ hardware | Text, streaming, and schema-constrained JSON. Requires MSIX packaging and model access; no tools, image input, or embeddings. |
| Android on-device (coming soon) | Not available yet | Provider details, capabilities, and requirements are not specified. |
| Azure OpenAI | All sample platforms with configured deployments | Responses API chat, tools, image input, and reasoning summaries; embeddings and image generation use their own deployments. |
| Replay | All sample platforms | Recorded conversations without credentials or a running model. |

The available Apple and Windows chat providers do not generate images or fall
back to Azure. Structured responses stream only when the selected provider emits
incremental updates.
Each tab has its own services and only exposes supported features.

Chat displays the model ID reported by the provider, including during replay.
It auto-saves the current recording locally. **New** replaces that recording;
**More** loads a bundled example or imports and exports recordings. Replay
works offline without credentials or a model. Select a live provider to
continue a conversation. Features are shown only when the selected provider
supports them. Uncheck unneeded tools or choose **None** to disable tool calls.
Switching providers preserves your Structured JSON preference, but providers
that opt out receive plain-text requests and their responses display as text.

The local, bounded diagnostics sidebar starts collapsed on Chat, Embeddings,
and Images. The header's **Logs** button toggles it without stopping diagnostic
capture while hidden. In narrower windows, tap outside the sidebar to dismiss it.
It groups built-in logs and completed spans by trace, including tool lifecycle logs from
MEAI and Apple. Apple's native tool callbacks retain the originating request's
trace/span. Chat and embeddings emit spans; the pinned MEAI version provides
image logging only. Model and usage data appear only when supplied by the provider.
Trace payload logging and sensitive telemetry capture are disabled, but
exceptions may still contain sensitive data. Diagnostics are not exported
automatically.

## Run the sample

Use the repository's pinned .NET 10 SDK and matching MAUI workload.
Run the following commands from the repository root.

### Apple

Apple builds require Xcode 27. For Mac Catalyst:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` for iOS and select a device with `--device <device-id>`.
Use `--list-devices` to list available targets. Enable Apple Intelligence and
install the English sentence-embedding asset for the corresponding features;
the asset may be absent on simulators.

### Windows

```powershell
dotnet run --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj -f net10.0-windows10.0.19041.0
```

Keep the Windows sample MSIX-packaged for on-device AI. It bundles an
experimental Windows App SDK runtime while the library uses stable APIs.
See the [Windows integration guide](../../docs/ai/WINDOWS-AI.md) for hardware,
capability and access requirements.

### Android

Android currently runs the Azure and Replay providers; the on-device provider
is coming soon. With Java and the Android SDK installed:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-android --device <device-id>
```

## Configure Azure OpenAI

Azure is optional. On-device providers and Replay need no cloud credentials.
Configure an endpoint (typically ending in `/openai/v1/`), a key, and only the
deployments you want to use. The project uses user-secrets ID
`2727d4aa-a3a5-484b-9447-91604761972b`. From the repository root:

```powershell
$project = "samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj"
dotnet user-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project $project
dotnet user-secrets set "AI:ApiKey" "<key>" --project $project
dotnet user-secrets set "AI:DeploymentName" "<chat-deployment>" --project $project
dotnet user-secrets set "AI:ImageDeploymentName" "<image-deployment>" --project $project
dotnet user-secrets set "AI:EmbeddingDeploymentName" "<embedding-deployment>" --project $project
```

`AI:DeploymentName` enables Chat, `AI:EmbeddingDeploymentName` enables Embeddings,
and `AI:ImageDeploymentName` enables Images and Chat's image-generation tool for
providers that support tool calling. The endpoint and key are required only
when at least one Azure deployment is configured.

**Debug builds embed your local user secrets. Do not distribute these builds
or commit credentials.** Azure prompts, images, and indexed text may leave the
device and incur charges. Recordings may also contain sensitive data; review
them before sharing.
