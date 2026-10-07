# AI Chat Playground

A .NET MAUI sample for trying `Microsoft.Extensions.AI` across on-device and
optional Azure OpenAI providers. The app runs on Android, iOS, Mac Catalyst,
and Windows (not native macOS); available providers vary by platform.

## Features

### Chat

Switch between providers, stream responses, request structured JSON, and try
optional tools. `Microsoft.Maui.Essentials.AI` provides on-device text chat
through `AppleIntelligenceChatClient`. On iOS and Mac Catalyst 27+, image
attachments require a ready vision-capable model. Azure uses the Responses
API and also supports image input, reasoning summaries, and an optional
image-generation tool. Apple does not generate images or fall back to Azure.

Chat displays the model ID reported by the provider, including during replay.
Save the current chat locally, import or export recordings, and replay them
offline. The **More** menu includes a bundled example; **New** replaces the
current recording. Uncheck tools you do not need, or choose **None** to disable
tool calls.

The header's **Logs** button toggles the local, bounded diagnostics sidebar
(also available on Embeddings and Images). It groups built-in logs and spans
by trace, including existing tool-invocation lifecycle logs from MEAI and Apple.
Apple's native tool callbacks retain the originating request's trace/span.
Chat and embeddings emit spans, while the pinned MEAI version
provides image logging only. Model and usage data appear only when emitted
by the provider. Trace payload logging and sensitive telemetry capture are
disabled; exceptions may still contain sensitive data. Nothing is exported.

### Embeddings

Import documents, build an index, and search it using Apple's on-device
`NLEmbeddingGenerator` or a configured Azure provider.

### Images

Generate images from text or edit a source image with a configured Azure provider.

Chat, Embeddings, and Images each have their own tab and services. Replay
works without credentials or a model.

## Build and run

Install the repo's pinned .NET 10 SDK, the matching MAUI workload, and Xcode 27
for Apple builds. From the repository root on macOS, build and run the Mac
Catalyst sample with:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -c Debug -t:Build,Run -p:UseXcode27Preview=true
```

For iOS, use `-f net10.0-ios27.0` instead. Apple Intelligence text chat requires
iOS or Mac Catalyst 26+ on a supported device with the model enabled. Apple
image input requires 27+ and a ready vision-capable model. Apple embeddings
require the English sentence-embedding asset, which may be absent on
simulators. Azure-backed features also run on Android and Windows.

## Optional Azure configuration

No cloud credentials are needed for the on-device providers or Replay. To
enable Azure, set an Azure OpenAI endpoint (typically ending in
`/openai/v1/`), an API key, and at least one deployment name in user secrets.
The project uses user-secrets ID `2727d4aa-a3a5-484b-9447-91604761972b`.
From the repository root, for example:

```powershell
dotnet user-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:ApiKey" "<key>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:DeploymentName" "<chat-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:ImageDeploymentName" "<image-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:EmbeddingDeploymentName" "<embedding-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Only set the deployment names you intend to use: `AI:DeploymentName` enables
Chat, `AI:ImageDeploymentName` enables Images and Chat's image-generation tool,
and `AI:EmbeddingDeploymentName` enables Embeddings. The endpoint and key are
required only when at least one Azure deployment name is configured.
User secrets are **embedded in Debug builds** for device testing: never
distribute those builds or commit keys. Azure prompts, images, and indexed
text may leave the device and incur charges.
