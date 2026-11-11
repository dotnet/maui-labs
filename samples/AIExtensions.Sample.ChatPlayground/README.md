# AI Chat Playground

A .NET MAUI sample for comparing Apple Intelligence, Windows AI, Gemini Nano,
Azure OpenAI, and offline Replay through the same chat UI. It runs on Android,
iOS, Mac Catalyst, and Windows (not native macOS); providers vary by platform.

## What you can try

Chat, Embeddings, Images, and Documents have separate providers and capabilities.

### Chat

| Provider | Text | Image input | Tools | Structured JSON |
|----------|------|-------------|-------|-----------------|
| Apple Intelligence | iOS / Mac Catalyst 26+ on supported hardware | OS 27+ with a ready vision model | Supported | Supported |
| Windows AI | Windows 11 24H2+ on supported Copilot+ hardware | Not supported | Not supported | Supported |
| Gemini Nano | Android API 26+ on supported AICore hardware | In-memory images | Native Auto on capable Nano V4 profiles | Not supported |
| Azure OpenAI | Configured Responses API deployment | Supported by compatible deployments | Supported by compatible deployments | Supported by compatible deployments |
| Replay | Recorded responses | No new image input | Recorded activity, no execution | Recorded responses |

Native chat does not fall back to Azure. Structured responses stream only when
the selected provider emits incremental updates.

Chat shows the provider's model ID and auto-saves the recording locally.
**New** replaces the recording; **More** loads examples or imports/exports
recordings. Replay works offline; switch to a live provider to continue chatting.
Controls reflect provider capabilities. Uncheck tools or choose **None** to disable calls.
Switching providers preserves your Structured JSON preference, but providers
that opt out receive plain-text requests and their responses display as text.

### Embeddings

Generate text vectors, index imported documents, and search their content.

| Provider | Text vectors | Document indexing | Semantic search |
|----------|--------------|-------------------|-----------------|
| Apple NaturalLanguage | Supported with the language asset | Supported | Supported |
| Azure OpenAI | Configured embedding deployment | Supported | Supported |
| Windows on-device | Not supported | Not supported | Not supported |
| Android on-device | Not supported | Not supported | Not supported |

### Image generation

Generate or edit images using a configured Azure OpenAI deployment. Chat image
input does not imply image-generation support.

| Provider | Image generation |
|----------|------------------|
| Apple on-device | Not supported |
| Windows on-device | Coming soon |
| Android on-device | Not supported |
| Azure OpenAI | Supported by the configured image deployment |

### Documents API experiment

Documents evaluates the merged API from [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588)
using official packages `11.0.0-preview.1.26560.3` from NuGet.org. It retains completed-document extraction and native-reader modes, original services/view-model conventions,
and explicit provider selection.
All source bridges are removed. Foundry Mistral is the single cloud comparison provider, using
`CommunityToolkit.DocumentExtraction.Mistral/1.0.0-preview`. The sample does not need a second Azure document service.
Apple Vision remains our on-device provider of the same official contract. No private substitute API is used.
The cloud reader modes project those same Toolkit results rather than maintaining independent service protocols or HTML parsing.
NuGet clients and their options are constructed directly in `AddDocumentFeature`, not provider-specific wrapper classes.
The cloud reader registration uses a shared adapter for the required ingestion projection.
Mistral uses the Toolkit's own HTTP transport and defaults, with no playground policy wrapper, response cap or custom deadline.
Mistral table content remains literal Markdown/HTML without an addressed cell grid; native-only blocks/captions/confidence remain
explicitly inspectable instead of being repackaged into metadata.

The experiment distinguishes normalized shared-document content/layout from deliberate borrowed-native inspection.
Native inspection displays complete structured JSON on request and reports inspection errors directly;
there is no playground output cap, truncation policy or inspection cache.
The final API has no streaming, raw-capture toggle, original-body ownership contract or generic structural renderer.
Normalized output, logging and reader results do not automatically inspect native output.
The old upstream OCR reader, configuration/OpenTelemetry middleware and usage/progress envelope remain removed.
See [the correction report](../../docs/ai/document-extraction-corrected-api.md) for data ownership and provider limitations.
Select the Documents tab to compare the same PDF/image across providers.

Debug binaries embed local user secrets and must not be distributed. Selected cloud calls upload immediately with inline disclosure.
No automatic fallback or new cloud resource is created.

## Diagnostics

Each tab's **Logs** button toggles a local diagnostics sidebar; capture continues
while hidden. It starts collapsed. In narrow windows, tap outside to dismiss it.

App and provider logs and completed spans are grouped by trace, including MEAI
request logs and Apple tool events.
Apple callbacks retain the request's trace/span. Chat and embeddings emit spans;
the pinned MEAI version provides image logging only. Model/usage data come from
the provider. Payload logging and sensitive telemetry are disabled, but exceptions
may contain sensitive data. Diagnostics are not exported automatically.

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
See the [Windows AI language models guide](../../docs/ai/windows-ai-language-models.md)
for hardware, capability, packaging, and access requirements.

### Android

The Android app requires API 26+ and supported AICore hardware/configuration for
Gemini Nano. It supports text/image chat and native tool calling when the selected
model advertises that capability. Select **Preview fast** for the verified Nano V4
tool path; the default stable/full Nano V3 model has no native tool calling.
Tool controls follow the selected model's status. Auto and None are available;
Require any and structured JSON are unsupported. Tool-enabled text arrives at
the end of each model turn so ML Kit's raw function tokens never appear in chat.
Use **Download** in the readiness banner to prepare the model. Chat requests never
download assets or fall back to a cloud provider.
See the [Android guide](../../docs/ai/gemini-nano-android.md).

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-android --device <device-id>
```

### Model readiness

Choose a Gemini Nano model in the sidebar for the next send. The selection also
applies to readiness checks and downloads; it does not clear the conversation.

The banner disables Send until the selected on-device model is ready. Android
offers **Download**, Windows offers **Prepare**, and system-managed or ongoing
preparation offers **Check again**. Downloads show byte progress and transfer rate
when available. **Cancel** stops this app's request, but a shared system download
may continue. Status changes and operation errors appear in **Logs**.

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
dotnet user-secrets set "AI:DocumentDeploymentName" "<mistral-ocr-deployment>" --project $project
```

`AI:DeploymentName` enables Chat, `AI:EmbeddingDeploymentName` enables Embeddings,
and `AI:ImageDeploymentName` enables Images and Chat's image-generation tool for
providers that support tool calling. The endpoint and key are required only
when at least one Azure deployment is configured.
`AI:DocumentDeploymentName` enables Documents using the same `AI:Endpoint` and `AI:ApiKey`;
the Foundry OCR route is derived from that resource URL. No separate document endpoint or key is needed.

**Debug builds embed your local user secrets. Do not distribute these builds
or commit credentials.** Azure prompts, images, and indexed text may leave the
device and incur charges. Recordings may also contain sensitive data; review
them before sharing.

## Shared file input

Chat attachments and image edits share root-level `FileInputService` and `InputFile`.
Callers supply their picker options and use the shared `FileInputService.ImageFiles`
(JPEG, PNG, GIF, WebP) or `FileInputService.DocumentFiles` (PDF, JPEG, PNG, HEIC, TIFF)
presets rather than defining private file-type lists. Document extraction uses the document preset.
Packaged sample names are also supplied by the caller.

```csharp
var file = await files.PickAsync(new PickOptions
{
    PickerTitle = "Select an image",
    FileTypes = FileInputService.ImageFiles,
}, cancellationToken);

var sample = await files.LoadSampleAsync("playground_sample.png", cancellationToken);
```

For already-opened input, `InputFile.ReadAsync(stream, fileName, contentType, cancellationToken)`
copies the remaining input from the current stream position using standard stream copying
and leaves the caller's stream open. Feature-specific format support is still respected:
image chat/edit callers continue to accept only JPEG, PNG, GIF, and WebP.

Both composers use the shared `AttachmentPreview` view for the thumbnail, file name,
and removal command, retaining their existing automation IDs and accessible removal
descriptions. Future callers can reuse the same bindings and customize the preview aspect.

The document composer also reuses `AttachmentPreview` and the shared input service:

```csharp
var document = await files.PickAsync(new PickOptions
{
    PickerTitle = "Choose a document",
    FileTypes = FileInputService.DocumentFiles,
}, cancellationToken);
```
