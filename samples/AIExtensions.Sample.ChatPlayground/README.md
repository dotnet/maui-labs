# AI Chat Playground

A .NET MAUI sample for comparing Apple Intelligence, Windows AI, Gemini Nano,
Azure OpenAI, and offline Replay through the same chat UI. It runs on Android,
iOS, Mac Catalyst, and Windows (not native macOS); providers vary by platform.

## What you can try

Chat, Embeddings, and Images have separate providers and capabilities.

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

Under each answer, a footer shows the model that answered and its token usage.
The chat auto-saves a recording locally. **New** replaces the recording; **More**
loads the example or imports/exports recordings. Replay works offline; switch to
a live provider to continue chatting. Settings apply to the next message without
starting a new chat. Controls reflect provider capabilities. Uncheck tools or
choose **None** to disable calls. Switching providers preserves your Structured
JSON preference, but providers that opt out receive plain-text requests and their
responses display as text.

#### Built on AIChatView

The Chat tab is the showcase for `AIChatView` (`Microsoft.Maui.AI.Chat.Controls`)
on the `Microsoft.Maui.AI.Chat` engine. The messages, tool and approval cards, and
the composer are all the control; the playground only configures it:

| Piece | Where | What it does |
|-------|-------|--------------|
| Session | `ChatViewModel` | One `AgentContext` for the page, bound to `AIChatView.Session`. **New** clears it; it is never recreated. |
| Router client | `PlaygroundChatClient` | The one `IChatClient` the engine talks to. It sends each request to the client selected in settings, with `ChatOptions` built from the settings for that request, or to the replay client while a recording replays. With **Streaming** off it asks for the whole response. Requests the client cannot take, such as images for a text-only client or an invalid setting, fail before anything is sent, and the chat shows the error with **Retry**. |
| Custom cards | `ChatPage.xaml` | A `BlockTemplate` for the `calculate` tool shows the call as arithmetic (`12 × 7 = 84`). `StructuredResponseTemplate` overrides `When` to pick assistant text that is a JSON object, and shows the structured response as a card with its JSON one tap away. |
| Slots | `ChatPage.xaml` | `MessagesHeaderTemplate` shows the system instructions in effect, `ResponseFooterTemplate` the model and usage, and `InputHeaderContent` the model readiness banner above the composer. |
| Attachments | `PlaygroundAttachmentPicker` | Registered as the `IChatAttachmentPicker`: choose an image or attach the bundled sample image. **Attach** appears only for clients that accept images. |
| Theme | `App.xaml` | Overrides a few `AIChat.Light.*` and `AIChat.Dark.*` colors to match the playground. Remove them to see the control's defaults. |

#### Tools and approvals

The settings enable the local date/time, calculator, and connection-status tools,
and Azure image generation. **Ask before running tools** puts each local tool
behind an approval card: **Approve** runs it, **Reject** tells the model it was
declined. The suggestions on an empty chat try the features the selected client
supports.

#### Recording and replay

Each live request is recorded with its response, including the tool calls and
results in it. Replay runs through the same engine path as a live chat: each
recorded user message is sent again and the replay client answers with the
recorded response, without contacting a model.

- **Play all** clears the chat and replays every turn; **Next turn** replays one;
  **Restart** rewinds. **Stop** interrupts the replay, clears the chat, and rewinds.
- Recorded approval decisions are applied to the approval cards. A turn whose
  approval the recording never answered is shown stopped.
- A recorded request that does not end with a user message, such as one from a
  client that sends tool results itself, is restored as history.
- While a replay is part-way through, sending stays disabled until it finishes or
  you start a new chat. The auto-saved chat replays when the app starts.

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
```

`AI:DeploymentName` enables Chat, `AI:EmbeddingDeploymentName` enables Embeddings,
and `AI:ImageDeploymentName` enables Images and Chat's image-generation tool for
providers that support tool calling. The endpoint and key are required only
when at least one Azure deployment is configured.

**Debug builds embed your local user secrets. Do not distribute these builds
or commit credentials.** Azure prompts, images, and indexed text may leave the
device and incur charges. Recordings may also contain sensitive data; review
them before sharing.

## Tests

The playground's tests run without MAUI workloads. They cover the router, the
replay driver, and the custom cards, and replay every recording in `TestData`
through the engine:

```sh
dotnet test tests/AIExtensions/AIExtensions.Sample.ChatPlayground.Tests
```
