# AI Chat Playground

A .NET MAUI sample for trying `Microsoft.Extensions.AI` across on-device and
optional Azure OpenAI providers. The app runs on Android, iOS, Mac Catalyst,
and Windows (not native macOS); available providers vary by platform.

## Features

### Chat

Switch between providers and stream responses. Apple and Azure also support
structured JSON and optional tools. `Microsoft.Maui.Essentials.AI` provides on-device text chat
through `AppleIntelligenceChatClient` on Apple platforms and `GeminiNanoChatClient`
on supported Android devices. On iOS and Mac Catalyst 27+, image
attachments require a ready vision-capable model. Azure uses the Responses
API and also supports image input, reasoning summaries, and an optional
image-generation tool. Apple does not generate images or fall back to Azure.

Chat displays the model ID reported by the provider, including during replay.
Save the current chat locally, import or export recordings, and replay them
offline. The **More** menu includes a bundled example; **New** replaces the
current recording. Uncheck tools you do not need, or choose **None** to disable
tool calls.

Gemini Nano is native-only: single-turn user text/images, no assistant/tool
history, no structured JSON, and no tools. Its JSON/tool controls are hidden.
After a request (or when selecting Gemini with an existing chat), select **New**
before sending another request. The playground does not drop or fake history.
System instructions require Nano V3+ and fail explicitly if unavailable.

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

Android requires API 24+. Gemini Nano is registered only on API 26+ and requires a supported
physical device with a locked bootloader and an updated, provisioned AICore
model. On API 26+ the Gemini slot is available, but its first request checks
actual model readiness and shows errors if the device or model is unavailable.
Android emulators can exercise the playground and Replay, not AICore inference.
The native bridge is built through .NET 10's `AndroidGradleProject`; a source
build requires the Android SDK and Java 17+.

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

## What to try

- **Chat:** Choose a provider, send text, and switch between streaming and
  single-response modes. The multiline prompt uses Enter/Return for a new line;
  select **Send** to submit it. Structured JSON requests a summary, key points,
  a free-form category, and a constrained sentiment. The common .NET schema
  leaves fields optional for other providers, while Apple and Azure OpenAI
  require all four in their native structured-output schemas. Gemini does not
  support structured JSON; stale JSON settings from another provider are not
  sent or used to interpret its plain-text response. The transcript displays the provider's actual JSON, including extra
  fields, without rewriting it through the request schema. Restoring an
  auto-saved chat keeps the selected live client; without one configured,
  Replay remains the offline default. Apple and Azure support optional tools.
  Date/time, calculator, and connection status start checked; connection
  status returns JSON with network access and connection types, not SSIDs or
  IP addresses. Gemini supports text and in-memory image input but not tools,
  image generation, structured output, or reasoning summaries; its JSON/tool
  controls are hidden and follow-up sends require **New**.
  **None** disables tool calls even when tools remain checked;
  **Auto** makes checked tools available, and a model may call them for
  unrelated prompts. Uncheck unneeded tools to prevent those calls. Azure
  can accept images and create them through a separately configured
  image-generation tool.
- **Embeddings:** Import documents, create an index, and search it. Apple
  on-device and configured Azure providers are available. Imported content
  and indexes are separate from chats.
- **Images:** Generate from text or edit a single source image and inspect the
  result. Configured Azure providers are offered; there is no on-device image
  provider in this branch. The Images tab does not save generated results.

The app saves one Chat recording locally for replay. Use the Chat **More**
menu to load a bundled example without credentials, or import/export a
recording; **New** replaces the current chat. Replay is read-only and returns
successive recorded turns without calling a model or checking new prompts.
Switching from a chat with images to a text-only provider requires clearing
that chat first.
Successful compilation and managed/JVM tests do not demonstrate Gemini
inference or image/thinking support on physical hardware.

## Organization

`Chat/`, `Embeddings/`, and `Images/` each contain their own services, views,
and view models. Each feature registers its page and selected real abstractions
with dependency injection. `MainWindow` composes the three registered pages
as tabs. Reusable controls are in `Views/`, and shared configuration, image
input, and atomic storage are in `Services/`. Chat recording and document
indexing remain separate.
