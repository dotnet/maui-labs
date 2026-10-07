# AI Chat Playground

A .NET MAUI sample for comparing `Microsoft.Extensions.AI` providers. It has
Chat, Embeddings, Images, and Documents tabs. `Microsoft.Maui.Essentials.AI` provides
`AppleIntelligenceChatClient` for on-device chat and `NLEmbeddingGenerator`
for on-device embeddings; Azure OpenAI is optional. The app targets Android,
iOS, and Mac Catalyst, plus a Windows target with Azure and offline Replay
only. It does **not** target native macOS. Available providers vary by
platform; saved Chat recordings can be replayed offline.

## Build and run

Install the repo's pinned .NET 10 SDK and MAUI workload. Apple Intelligence
Chat requires iOS or Mac Catalyst 26+ on a supported device with a compatible
Xcode; Apple NaturalLanguage embeddings have broader Apple OS support but appear
only when the English sentence-embedding asset is installed (it may be absent
on simulators).
Azure-backed features also run on Android and Windows.

From the repository root on macOS, build before running the Mac Catalyst target:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -t:Run -f net10.0-maccatalyst
```

For Xcode 27 preview builds, pass `-p:UseXcode27Preview=true` and select
`-f net10.0-ios27.0` or `-f net10.0-maccatalyst27.0`. Normal builds retain
the stable Apple targets. Both Apple apps register MAUI scene delegates.
Document mapping tests live in the existing Essentials.AI unit test project:
`dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/ --filter FullyQualifiedName~DocumentMappingTests`.

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

### Optional cloud document comparison

The Documents tab registers selectable `IngestionDocumentReader` providers:
Apple Vision RecognizeDocuments on-device on iOS/Mac Catalyst 26+, optional Azure Document Intelligence,
and optional Foundry Mistral Document AI. The Apple reader is `AppleVisionRecognizeDocumentsReader`,
backed by Vision's `RecognizeDocumentsRequest`.
Both cloud readers work on all platforms. On Android and Windows, only configured cloud readers are available.
Use the input area's **+** menu to select a PDF, PNG, JPEG, HEIC, or TIFF file (20 MB maximum),
or load the bundled two-page sample PDF or sample document image. Choose a reader in settings and select **Read**.
The attachment preview and remove/read/stop buttons use the same composer styles as Chat and Images. To compare, switch
readers and re-run the same selected file. Pressing **Read** with a cloud reader selected uploads the document immediately,
without a confirmation popup. There is **no automatic cloud fallback** if local
recognition fails. The result displays standard `IngestionDocument` pages,
element types/text, and Markdown. Azure paragraphs and tables follow reading
order from the SDK spans; merged cells and actual header kinds use HTML table
markup in the Markdown result.

Azure AI Document Intelligence uses a **separate API** from Azure OpenAI:
set its HTTPS resource root endpoint and subscription key explicitly. A compatible multi-service Foundry resource can host
both services, but an Azure OpenAI-only resource does not imply Document Intelligence support. The
sample uses the official `Azure.AI.DocumentIntelligence` 1.0.0 SDK and the
`prebuilt-layout` model (2024-11-30 service API).

**WIP dependency:** `Azure.AI.DocumentIntelligence` 1.0.0 is currently fetched
from NuGet.org using a temporary restore source, for example:

```sh
dotnet restore samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -p:RestoreAdditionalProjectSources=https://api.nuget.org/v3/index.json
```

The package must be mirrored to the approved dnceng proxy before CI can
restore this sample. Do not add NuGet.org to `NuGet.config` or embed credentials
in source files; the endpoint and key below belong in local user secrets.

```powershell
dotnet user-secrets set "AI:DocumentIntelligenceEndpoint" "https://<document-resource>.cognitiveservices.azure.com/" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:DocumentIntelligenceKey" "<document-resource-key>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Foundry Mistral Document AI reuses `AI:Endpoint` and `AI:ApiKey`; set `AI:DocumentDeploymentName` to an existing document-model
deployment (for example, a deployment of `mistral-ocr-4-0` or `mistral-document-ai-2512`). It does not use the Chat deployment.
The reader converts an Azure OpenAI resource hostname to its corresponding `.services.ai.azure.com` hostname and calls
`/providers/mistral/azure/ocr` on the resource root, not the OpenAI chat-completions API.

```powershell
dotnet user-secrets set "AI:DocumentDeploymentName" "<document-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Rebuild and relaunch after changing secrets because the Debug app embeds the settings at build time.
The Foundry reader projects pages, headings, text, and tables into standard ingestion elements without provider metadata.
It does not request image crops or expose geometry, confidence, or raw-provider results. It uses a five-minute deadline and bounded
retries for HTTP 503; it never switches to another reader. The Document Intelligence reader retains its 100-second deadline.

Cloud comparison uploads the **entire document** to Azure and may incur
charges. The sample embeds developer secrets in Debug app binaries; never
publish or distribute such binaries. Cancelling an in-flight operation cannot
undo a document already submitted to Azure. Neither credentials nor document
contents are logged.

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
- **Documents:** Use **+** to pick an image/PDF or load a bundled sample. Choose **Document extraction** to exercise the proposed
  `IDocumentExtractionClient` with whole-result or page-streaming calls, **Document reader** for the native ingestion projection,
  or **Upstream OCR reader** for the unchanged proposal adapter. Inspect normalized and provider-raw results separately.
  Only actual fields shared by the provider and contract are normalized. Selecting Azure uploads the document immediately.

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
recording and document indexing remain separate.

Documents follows the same page/area/composer/settings split as Images and Embeddings. Its view model owns selection and
request state; `DocumentReadingService` runs the reader and `DocumentExtractionService` runs the actual proposed client contract.
Both services are independent of MAUI controls. Extraction clients use the original builder, logging, and OpenTelemetry decorators,
and expose simple UI descriptors through standard `GetService`, matching the Chat/Embeddings conventions.
All three attachment composers share `AttachmentPreview`, and image/document loading shares the bounded `InputStreamReader`.
Provider-specific SDK/protocol adapters remain separate, behind actual `IngestionDocumentReader` and `IDocumentExtractionClient` abstractions.

The complete upstream abstraction and middleware projects are preserved verbatim under
[`../../src/Libraries/DocumentExtraction/Upstream/`](../../src/Libraries/DocumentExtraction/Upstream/), including original project files
and namespaces. Local non-shipping projects file-link them; no reduced internal replacement is compiled.
The original `OcrDocumentReader` is file-linked too, without silently improving its currently lossy projection.
See [the feature gaps](../../docs/ai/apple-document-extraction-gaps.md) for missing features on either side.

This checkout is not intended to produce distributable packages. The copied projects and prototype Essentials.AI library are non-packable.
Developer Debug builds embed local secrets and must never be distributed.

### Standard launch arguments

The existing .NET command-line configuration provider accepts `--name value` and `--name=value`.
For example, launch directly into Documents with `--page Documents`; arbitrary configuration keys remain available without a custom parser.
