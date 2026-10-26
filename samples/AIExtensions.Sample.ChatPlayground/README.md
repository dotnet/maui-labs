# AI Chat Playground

A .NET MAUI sample for comparing on-device Apple Intelligence with optional
Azure OpenAI providers. It runs on Android, iOS, Mac Catalyst, and Windows
(not native macOS); providers vary by platform.

## What you can try

| Tab | Features | Providers |
|-----|----------|-----------|
| Chat | Text, streaming, structured JSON, optional tools and image input | Apple Intelligence (iOS/Mac Catalyst), Azure OpenAI, offline Replay |
| Embeddings | Import documents, index them, and search | Apple NaturalLanguage (iOS/Mac Catalyst), Azure OpenAI |
| Images | Generate images or edit a source image | Azure OpenAI |

Apple image input requires OS 27 and a ready vision-capable model. Apple does
not generate images or fall back to Azure. Azure chat uses the Responses API
and supports image input, reasoning summaries, and an optional image-generation
tool. Structured responses stream only when the provider emits incremental
updates. Each tab has its own services.

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
Apple builds require Xcode 27. On macOS, from the repository root:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` for iOS and select a device with `--device <device-id>`.
Use `--list-devices` to list available targets. Apple Intelligence text chat requires iOS or Mac
Catalyst 26+ on a supported device with the model enabled. Image input requires
27+ and a ready vision model. Apple sentence embeddings require the English
language asset, which may be absent on simulators.

On Windows:

```powershell
dotnet run --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj -f net10.0-windows10.0.19041.0
```

The Windows sample remains unpackaged and offers Azure-backed features and
offline Chat Replay, not on-device AI. Azure and Replay also run on Android.

With Java and the Android SDK installed:

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
