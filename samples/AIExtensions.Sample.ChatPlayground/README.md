# AI Chat Playground

A .NET MAUI sample for comparing Apple Intelligence, Windows AI, Gemini Nano,
and Azure OpenAI through the same chat UI. It runs on Android, iOS, Mac Catalyst,
and Windows (not native macOS); providers vary by platform.

## What you can try

Chat, Embeddings, and Images have separate providers and capabilities.

### Chat

| Provider | Text | Image input | Tools | Structured JSON |
|----------|------|-------------|-------|-----------------|
| Apple Intelligence | iOS / Mac Catalyst 26+ on supported hardware | OS 27+ with a ready vision model | Supported | Supported |
| Windows AI | Windows 11 24H2+ on supported Copilot+ hardware | Not supported | Not supported | Supported |
| Gemini Nano | Android API 26+ on supported AICore hardware | In-memory images | Native Auto on capable Nano V4 profiles | Not supported |
| Azure OpenAI | Configured Responses API deployment | Supported by compatible deployments | Supported by compatible deployments | Supported by compatible deployments |

Native chat does not fall back to Azure. Structured responses stream only when
the selected provider emits incremental updates. Apple Intelligence runs tools
inside its own session instead of handing the calls back to the app, so it can't
ask before a tool runs (**Ask before running tools** is hidden while it is
selected) and isn't offered the chat's UI actions (`set_theme` and
`propose_notes`); its instructions and suggestions leave them out, and the notes
still reach it with each message. It also ends the whole answer when a tool
fails, so the calculator's schema lists its four operations for the model to
choose from.

Settings apply to the next message without starting a new chat. Controls reflect
provider capabilities. Uncheck tools or choose **None** to disable calls.
Switching providers preserves your Structured JSON preference, but providers that
opt out receive plain-text requests and their responses display as text. Under
each answer, a footer shows the model that answered, its token usage, and whether
you stopped it; each failed answer says why it failed, and the last offers **Retry**.

The conversation is saved as it changes and comes back when the app starts.
**More** starts a **New chat**, which clears the saved conversation and the notes,
and shows or hides the notes.

#### What the chat shows

The suggestions on an empty chat try what the selected client supports.

| Feature | Try | How it works |
|---------|-----|--------------|
| A tool call | "What time is it here?" | `get_current_local_datetime` has no template of its own, so it shows the generic tool card: the call, its arguments, and its result, or **No result** when the answer ended without one. |
| Several calls in one card | "Use the calculator: (12 × 7) + (30 ÷ 4)" | `CalculatorWorksheetHandler`, an `ActivityHandler`, folds every `calculate` call of an answer into one worksheet, a row per calculation. Rows fill in as their results arrive, and a calculation that failed or never ran says why; once the answer is stopped or fails, a row without a result says **No result**, also after the app restarts. |
| A typed tool card | "Am I online?" | `ConnectionStatusBlock` is a `[ToolBlock("get_connection_status")]` block: the source generator writes the handler that maps the call's result into it, and `options.AddGeneratedToolBlocks()` registers it. |
| A UI action | "Switch to dark mode" | `set_theme` runs in the app rather than in the model's client: it sets the app to the light, dark, or system theme. |
| Approvals | **Ask before running tools** in settings | Each local tool, and `set_theme`, waits behind an approval card. **Approve** runs it; **Reject** tells the model it was declined. Hidden for Apple Intelligence, which runs tools itself. |
| Reasoning | "Which is larger, 9.11 or 9.9?" | With **Reasoning summary** on, a model that summarizes its reasoning shows it muted above the answer, with the summary's headings, which the model marks with `**`, in bold. Reasoning that arrives only encrypted shows nothing. |
| Images | Attach an image, or "Generate an image of a cozy cabin" | Clients that accept images take one with the message. With an Azure image deployment, the model can generate or edit images. |
| Structured output | **Structured JSON** in settings | An answer that is a JSON object shows as a structured-response card, chosen by `StructuredResponseMatch`, a custom `BlockMatch`. |
| Shared notes | "Add a Paris packing list to my notes" | See [Notes](#notes). |

#### Notes

You and the model share notes, which are the agent's typed state
(`UIAgent<NotesState>`):

- You edit the notes in the notes bar. Each request tells the model what they
  say, in a message added just before yours in that request only, so the
  conversation doesn't keep old copies.
- The model changes them only by proposing new text with `propose_notes`, a
  manual UI action. The proposal is predictive state: the notes show it with
  added text highlighted and removed text struck through, and **Accept** or
  **Reject**, in the bar or on the proposal's card, decides. The model hears which.
  Stopping the answer, or a failure, rolls the proposal back.
- An answer that proposes more than once is reviewed one proposal at a time, in
  order. Once you decide one, in the bar or on its card, the bar shows the next
  with its changes to the notes as they are then.
- The bar stays hidden until the notes get text, change, or receive a proposal;
  **More** shows or hides it. On a phone it sits between the messages and the
  composer, and expands to an editor. In a wider window it is a column beside the
  messages.
- The notes are saved next to the conversation. Restoring the conversation can't
  replay your own edits or decisions, so the saved notes are put back after it,
  also when the conversation can't be restored. **New chat** deletes them.

#### How the chat is built

The chat is a plain `CollectionView` over the engine's turns
(`Microsoft.Maui.AI.Chat`) with block templates from
`Microsoft.Maui.AI.Chat.Controls`:

| Piece | Where | What it does |
|-------|-------|--------------|
| Session | `ChatViewModel` | One `AgentContext` for the page over a `UIAgent<NotesState>` with the worksheet handler, the generated tool blocks, the `set_theme` and `propose_notes` UI actions, and the saved thread. **New chat** clears it; it is never recreated. |
| Router client | `PlaygroundChatClient` | The one `IChatClient` the engine talks to. It sends each request to the client selected in settings, with `ChatOptions` built from the settings and the engine's UI actions, and the notes. With **Streaming** off it asks for the whole response. **Require any** applies to an answer's first request only, as in `FunctionInvokingChatClient`, so the model can answer the results of the app's UI actions instead of calling a tool again. Requests the client cannot take, such as images for a text-only client or an invalid setting, fail before anything is sent, and the chat shows the error with **Retry**. |
| Conversation | `Chat/Views/ChatArea.xaml` | Each turn lays out its request and response blocks with a `BindableLayout` and a `BlockTemplateSelector`, then says whether it was stopped or why it failed. |
| Templates | `Chat/Views/Templates/` | `ChatTemplates.xaml` has a `BlockTemplate` per kind of block, and the first that matches wins: your message, the answer, reasoning, images, the structured response, the worksheet, the connection card, the notes proposal, approvals, UI actions, and the generic tool card. The custom matches and views those templates use live next to it. |
| Custom blocks | `Chat/Services/Blocks/` | The calculator worksheet (an `ActivityHandler` that folds a response's `calculate` calls into one block) and the connection status card (a generated `[ToolBlock]`). |
| Composer | `ChatComposer.xaml` | An editor, **Attach** for clients that accept images, and **Send**, or **Stop** while an answer runs. |
| Saved conversation | `FileConversationThread` | The engine's thread (`UIAgentOptions.Thread`): it writes each finished turn's updates to a JSON file, and `RestoreAsync` shows them again when the app starts. |

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

Azure is optional. On-device providers need no cloud credentials.
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
device and incur charges. The saved conversation and notes may also contain
sensitive data.

## Tests

The playground's tests run without MAUI workloads. They drive the chat's router,
handlers, notes, and saved conversation through the real engine with scripted
responses:

```sh
dotnet test tests/AIExtensions/AIExtensions.Sample.ChatPlayground.Tests
```
