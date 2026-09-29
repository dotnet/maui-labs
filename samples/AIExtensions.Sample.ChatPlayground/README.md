# AI Playground

A .NET MAUI sample for comparing `Microsoft.Extensions.AI` providers. It has
Chat, Embeddings, Images, and Documents tabs. `Microsoft.Maui.Essentials.AI` provides
`AppleIntelligenceChatClient` for on-device chat and `NLEmbeddingGenerator`
for on-device embeddings. The Documents tab exercises the experimental
`Microsoft.Extensions.DocumentExtraction` contract with Apple Vision/PDFKit
and optional Foundry Mistral OCR 4 and vision-model deployments. Azure OpenAI is optional. The app targets Android, iOS, and Mac
Catalyst, plus Windows. It does **not** target native macOS. Available providers
vary by platform; saved Chat recordings can be replayed offline.

## Build and run

Install the repo's pinned .NET 10 SDK and MAUI workload. Apple Intelligence
Chat requires iOS or Mac Catalyst 26+ on a supported device with a compatible
Xcode; Apple NaturalLanguage embeddings have broader Apple OS support but appear
only when the English sentence-embedding asset is installed (it may be absent
on simulators). Apple Vision document extraction requires iOS or Mac Catalyst
26+. Mistral OCR 4 provides the specialized cloud Documents provider from a
Microsoft Foundry resource. Without a configured Foundry provider, Android and
Windows show availability guidance rather than registering a fake or fallback
extractor. Azure-backed features also run on Android and Windows.

From the repository root on macOS, build before running the Mac Catalyst target:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -t:Run -f net10.0-maccatalyst
```

For Xcode 27 preview builds, pass `-p:UseXcode27Preview=true` and select
`-f net10.0-ios27.0` or `-f net10.0-maccatalyst27.0`. Normal builds retain
the stable Apple targets. Both Apple apps register MAUI scene delegates.

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
dotnet user-secrets set "AI:DocumentDeploymentName" "mistral-ocr-4-0" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:FoundryEndpoint" "https://<foundry-resource>.services.ai.azure.com/" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:FoundryApiKey" "<foundry-resource-key>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Only set the deployment names you intend to use: `AI:DeploymentName` enables
Chat, `AI:ImageDeploymentName` enables Images and Chat's image-generation tool,
`AI:EmbeddingDeploymentName` enables Embeddings, and
`AI:DocumentDeploymentName` identifies the specialized Mistral OCR document
model deployed in the Foundry resource, for example `mistral-ocr-4-0`.
Documents also exposes the existing `AI:DeploymentName` as a separate general
vision/chat provider when that deployment supports image input; only
vision-capable Responses models can accept PDF files. The Azure OpenAI endpoint
and key are required when at least one Chat, Images, or Embeddings deployment
name is configured. The Foundry resource endpoint and key are required with
`AI:DocumentDeploymentName`.

Both cloud Documents clients are registered independently when configured so
they can be selected and compared in the provider picker. Providers never
silently retry through one another or resend a failed document to another
model.
User secrets are **embedded in Debug builds** for device testing: never
distribute those builds or commit keys. Azure prompts, images, and indexed
text may leave the device and incur charges. Documents leave the device when
either Foundry provider is selected.

## What to try

- **Chat:** Choose a provider, send text, and switch between streaming and
  single-response modes. The multiline prompt uses Enter/Return for a new line;
  select **Send** to submit it. Structured JSON requests a summary, key points,
  a free-form category, and a constrained sentiment. The common .NET schema
  leaves fields optional for other providers, while Apple and Azure OpenAI
  require all four in their native structured-output schemas; key points may
  be empty. The transcript displays the provider's actual JSON, including extra
  fields, without rewriting it through the request schema. Restoring an
  auto-saved chat keeps the selected live client; without one configured,
  Replay remains the offline default. Apple and Azure support optional tools.
  Date/time, calculator, and connection status start checked; connection
  status returns JSON with network access and connection types, not SSIDs or
  IP addresses. **None** disables tool calls even when tools remain checked;
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
- **Documents:** Compare Apple Vision locally with two deployments in Microsoft
  Foundry: specialized `mistral-ocr-4-0` and a general vision-capable model.
  Mistral returns page Markdown, classified blocks, pixel boxes, tables,
  figures, and confidence. The general vision model provides a probabilistic
  semantic extraction path with text/tables but no provider-grade geometry.
  Choose an image or PDF, load
  the packaged conformance sample, or scan pages with VisionKit where supported.
  Both providers return structured pages, text, tables, barcodes, metadata, and
  geometry; Apple additionally exposes its provider-specific list model. Image inputs show
  a polygon overlay, while PDF inputs show every rendered page. Switch the
  inspector between Images, JSON, or both panes arranged horizontally or
  vertically. Selecting an overlay region selects and scrolls to its normalized
  JSON path; selecting a JSON row highlights and scrolls to its page region.
  Inspect full normalized JSON, bounded raw Apple JSON, and provider
  capabilities, or cancel a multi-page PDF while PDFKit renders and recognizes
  pages sequentially. The selected provider description clearly states whether
  document bytes remain local or are sent to Azure.

The app saves one Chat recording locally for replay. Use the Chat **More**
menu to load a bundled example without credentials, or import/export a
recording; **New** replaces the current chat. Replay is read-only and returns
successive recorded turns without calling a model or checking new prompts.
Switching from a chat with images to a text-only provider requires clearing
that chat first.

## Organization

`Chat/`, `Embeddings/`, `Images/`, and `Documents/` each contain their own
services, views, and view models. Each feature registers its page and selected
real abstractions with dependency injection. `MainWindow` composes the four
registered pages as tabs. Reusable controls are in `Views/`, and shared
configuration, image input, and atomic storage are in `Services/`. Chat
recording, embedding document indexing, and document extraction remain
separate.

The Documents feature currently references a vendored snapshot of the proposed
`Microsoft.Extensions.DocumentExtraction` API from dotnet/extensions#7588.
Those non-shipping projects are experimental scaffolding and must be replaced
with official package references before this sample or provider ships.

### Why these document providers

| Provider | Demo role |
|---|---|
| Apple Vision | Local/private document-native baseline |
| Mistral OCR 4 | Specialized document-native model deployed in the Foundry resource |
| Vision-capable Foundry model | General multimodal/semantic extraction baseline; reuses the Chat deployment when no document-specific override is set |
| Azure Document Intelligence | Valuable deterministic service comparison, but excluded from the active demo to avoid requiring a separate service resource |
| Content Understanding | Valuable analyzer and typed-field comparison, but excluded for now because it demonstrates an analyzer workflow rather than a deployed model |
