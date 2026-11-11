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

### Documents API experiment

Documents evaluates the merged API from [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588)
using official packages `11.0.0-preview.1.26560.3` from NuGet.org. It retains completed-document extraction and native-reader modes, original services/view-model conventions,
and explicit provider selection.
All source bridges are removed. Foundry Mistral is the single cloud comparison provider, using
`CommunityToolkit.DocumentExtraction.Mistral/1.0.0-preview`. The sample does not need a second Azure document service.
Apple Vision remains our on-device provider of the same official contract. No private substitute API is used.
The cloud reader modes project those same Toolkit results rather than maintaining independent service protocols or HTML parsing.
NuGet clients and their options are constructed directly in `AddDocumentFeature`, not provider-specific wrapper classes.
The cloud reader registration uses a shared adapter. Only shared application safeguards and the required ingestion projection remain;
Mistral response limiting runs in the normal HTTP handler pipeline without a second forwarding client.
Mistral table content remains literal Markdown/HTML without an addressed cell grid; native-only blocks/captions/confidence remain
explicitly inspectable instead of being repackaged into metadata.

The experiment distinguishes normalized shared-document content/layout from deliberate borrowed-native inspection.
The final API has no streaming, raw-capture toggle, original-body ownership contract or generic structural renderer.
Normalized output, logging and reader results do not automatically inspect native output.
The old upstream OCR reader, configuration/OpenTelemetry middleware and usage/progress envelope remain removed.
See [the correction report](../../docs/ai/document-extraction-corrected-api.md) for data ownership and provider limitations.
Select the Documents tab to compare the same PDF/image across providers.

Debug binaries embed local user secrets and must not be distributed. Selected cloud calls upload immediately with inline disclosure.
No automatic fallback or new cloud resource is created.

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
dotnet user-secrets set "AI:DocumentDeploymentName" "<mistral-ocr-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Only set the deployment names you intend to use: `AI:DeploymentName` enables
Chat, `AI:ImageDeploymentName` enables Images and Chat's image-generation tool,
`AI:EmbeddingDeploymentName` enables Embeddings, and `AI:DocumentDeploymentName` enables Documents.
Documents reuses the same `AI:Endpoint` and `AI:ApiKey`; the native Foundry OCR route is derived from that resource URL.
No Document Intelligence endpoint/key or separately configured document endpoint is needed. The endpoint and key are
required only when at least one Azure deployment name is configured.
User secrets are **embedded in Debug builds** for device testing: never
distribute those builds or commit keys. Azure prompts, images, and indexed
text may leave the device and incur charges.

## Shared file input

Chat attachments, image edits and document extraction share root-level `FileInputService` and `InputFile`.
Callers supply their picker options and use the shared `FileInputService.ImageFiles` or `FileInputService.DocumentFiles`
presets rather than defining private file-type lists. Packaged sample names are also supplied by the caller.

```csharp
var file = await files.PickAsync(new PickOptions
{
    PickerTitle = "Choose a document",
    FileTypes = FileInputService.DocumentFiles,
}, cancellationToken);
```

For already-opened input, `InputFile.ReadAsync(stream, fileName, contentType, cancellationToken)` copies from the current stream position,
enforces the shared 20 MB limit, and leaves the caller's stream open. The common value replaces separate image/document byte records.
Feature-specific format support is still respected: image chat/edit filters do not offer HEIC/TIFF just because document OCR supports them.
