# AI Chat Playground

A .NET MAUI sample for comparing Apple Intelligence, Windows AI, Gemini Nano,
Azure OpenAI, and offline Replay through the same chat UI. It runs on Android,
iOS, Mac Catalyst, and Windows (not native macOS); providers vary by platform.

## What you can try

Chat, Agentic, Embeddings, and Images have separate providers and capabilities.

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
| Router client | `PlaygroundChatClient` | The one `IChatClient` the engine talks to. It sends each request to the client selected in settings, with `ChatOptions` built from the settings for that request, or to the replay client while a recording replays, and records each live exchange. With **Streaming** off it asks for the whole response. Requests the client cannot take, such as images for a text-only client or an invalid setting, fail before anything is sent, and the chat shows the error with **Retry**. |
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
results in it: the router records around the selected client's whole pipeline, so
the recording holds the exchange the engine saw. Replay runs through the same
engine path as a live chat: each
recorded user message is sent again and the replay client answers with the
recorded response, without contacting a model.

- **Play all** clears the chat and replays every turn; **Next turn** replays one;
  **Restart** rewinds. **Stop**, or stopping a replayed response in the chat,
  interrupts the replay, clears the chat, and rewinds.
- Recorded approval decisions are applied to the approval cards. A turn whose
  approval the recording never answered is shown stopped.
- A recorded request that does not end with a user message, such as one from a
  client that sends tool results itself, is restored as history.
- Responses stopped part-way are not recorded. When a recorded request continues
  a conversation that differs from the one shown, for example after a stopped
  turn, that conversation is restored before its message is sent again.
- While a replay is part-way through, sending stays disabled until it finishes or
  you start a new chat. The auto-saved chat replays when the app starts.

### Agentic

The Agentic tab shows the agentic UI of ASP.NET Core's
[AI components](https://learn.microsoft.com/aspnet/core/blazor/components/ai-components)
and of the [AgenticUI](https://github.com/danroth27/AgenticUI) sample natively, on
`AIChatView` and the engine, with any `IChatClient`. There is no server and no
AG-UI: tools that run on a server there are local tools here, and its state events
become a `StateMapper` that reads the tool calls as they stream.

Pick a scenario from the sidebar (**Scenarios** on a narrow screen). Each keeps its
own conversation while you switch, and each comes with a recorded run: **Example**
replays it through the same engine path as a live chat, without contacting a model,
so every scenario works offline. **New** starts over. A scenario with state shows it
in a panel beside the chat, or above the chat on a narrow screen, where it collapses
to its title.

The scenarios send to a live client chosen in the sidebar, Azure OpenAI when it is
configured. Every scenario except Reasoning needs a model that calls tools;
Reasoning shows a summary only from a reasoning model, such as one behind the Azure
OpenAI Responses API.

| Scenario | What it shows | Built with |
|----------|---------------|------------|
| Tool cards | Cards that show the call while a tool runs and fill in with its result | The built-in tool card, and a typed `[ToolBlock]` card registered with `options.AddGeneratedToolBlocks()` |
| Combined tool calls | Several calls of one response in one card that fills in row by row | `ContentBlockHandler<TState>` with `options.AddBlockHandler`, and a `BlockTemplate` for its block |
| Human in the loop | Approve or reject a tool call before it runs | `ApprovalRequiredAIFunction`, the built-in approval card, and a custom approval template |
| Frontend tools | Tools that run in the app: one at once, one when you press **Run** | `options.RegisterUIAction`, automatic and `UIActionInvocationMode.Manual` |
| Activity progress | A plan that updates in place in the chat and in a panel | `ActivityHandler<TBlock>`, `UIAgent<TState>`, and a `StateMapper` |
| Shared state | A recipe that you and the agent both edit | `UIAgent<TState>`, `AgentState<T>.Value`, and a `StateMapper` that calls `SetState` |
| Predictive state | A proposed document, previewed with its changes, that you accept or reject | A manual UI action, `SetPredictiveState`, and `AcceptPredictiveState` / `RejectPredictiveState` |
| Reasoning | The model's reasoning summary above its answer | `ChatOptions.Reasoning` and the built-in reasoning view |
| Persistence | A conversation that survives quitting the app | `options.Thread` (a file-backed `IConversationThread`) and `RestoreAsync()` |

#### Tool cards

`get_weather` and `get_local_time` run locally with a short delay, so each card
first shows the call ("Checking the weather in Seattle…") and then fills in.
`get_local_time` uses the built-in tool card. `get_weather` uses `WeatherToolBlock`,
a `[ToolBlock("get_weather")]` class whose `[ToolParameter]` and `[ToolResult]`
properties the source generator fills from the call and its result;
`options.AddGeneratedToolBlocks()` registers its handler, and a `BlockTemplate`
shows it as a weather card.

#### Combined tool calls

Ask for several cities and every `get_weather` call of the response lands in one
card, a row per city, each filled in as its result arrives, with the warmest city
as the headline. The scenario asks for one call at a time
(`AllowMultipleToolCalls = false`) so the card visibly grows; with parallel calls
the results of a batch arrive together and the card fills in at once.

To combine tool calls into one card:

1. Define a block that holds every call: a `ContentBlock` subclass.
2. Write a handler that claims the calls and their results and folds them into the
   block. The first call emits it; later calls and results update it. Mark the
   results handled too: a custom block is not one of the engine's function blocks,
   so nothing else shows them.
3. Register the handler, and give the block a template.

```csharp
public sealed class WeatherComparisonBlock : ContentBlock
{
    public List<(FunctionCallContent Call, FunctionResultContent? Result)> Rows { get; } = [];
}

public sealed class WeatherComparisonHandler : ContentBlockHandler<WeatherComparisonBlock>
{
    public override BlockMappingResult<WeatherComparisonBlock> Handle(
        BlockMappingContext context, WeatherComparisonBlock block)
    {
        var changed = false;
        foreach (var content in context.UnhandledContents)
        {
            if (content is FunctionCallContent { Name: "get_weather" } call)
            {
                context.MarkHandled(call);
                block.Rows.Add((call, null));
                changed = true;
            }
            else if (content is FunctionResultContent result &&
                block.Rows.FindIndex(row => row.Call.CallId == result.CallId) is >= 0 and var index)
            {
                context.MarkHandled(result);
                block.Rows[index] = (block.Rows[index].Call, result);
                changed = true;
            }
        }

        if (!changed)
            return BlockMappingResult<WeatherComparisonBlock>.Pass();
        if (string.IsNullOrEmpty(block.Id))
        {
            block.Id = "weather-" + context.Update.MessageId;
            return BlockMappingResult<WeatherComparisonBlock>.Emit(block, block); // the first call shows the card
        }

        return BlockMappingResult<WeatherComparisonBlock>.Update(block); // later calls and results update it
    }
}

var agent = new UIAgent(chatClient, options => options.AddBlockHandler(new WeatherComparisonHandler()));
```

```xml
<chat:AIChatView Session="{Binding Session}">
    <chat:BlockTemplate BlockType="{x:Type local:WeatherComparisonBlock}"
                        ViewType="{x:Type local:WeatherComparisonView}" />
</chat:AIChatView>
```

The view derives from `BlockView` and redraws in `OnBlockChanged`, which runs for
every update of its block. The playground's version is `WeatherComparisonHandler`,
`WeatherComparisonBlock`, and `WeatherComparisonView`.

#### Human in the loop

`book_meeting` and `send_email` are wrapped in `ApprovalRequiredAIFunction`, so the
model's call waits for you and the tool runs only once you approve it. `send_email`
uses the built-in approval card. `book_meeting` uses a custom approval template
(`BlockTemplate BlockType="ToolApprovalBlock" ToolName="book_meeting"`) that shows
the meeting as a calendar card and then its booking; its **Book it** and
**Don't book** buttons call the block's `Approve()` and `Reject()`.

#### Frontend tools

UI actions are tools that run in the app rather than in the model's client.
`set_accent_color` is registered with `options.RegisterUIAction` and runs as soon as
the model calls it: it sets `AIChat.Color.Accent` in this scenario's resources, so
the send button, buttons, and progress recolor live, and a custom template shows it
as a chip. `reset_accent_color` is registered with `UIActionInvocationMode.Manual`:
its built-in card waits until you press **Run**.

#### Activity progress

`PlanActivityHandler`, an `ActivityHandler<PlanActivityBlock>`, maps the
`create_plan` and `update_plan_step` calls into one activity block that updates in
place: completed steps get a check, the step running shows a spinner, and the
headline shows the step in progress. It claims those calls and their results, so no
tool cards appear. A `StateMapper` keeps the same plan as the agent's typed state
(`UIAgent<PlanState>`), which the **Plan** panel shows.

#### Shared state

The recipe is the agent's typed state (`UIAgent<RecipeState>`). Edit it in the
**Recipe** panel and the edit becomes `agent.State.Value` at once. Each request
carries the current recipe as a JSON message just before your message (as the
AgenticUI sample does), so the model sees your edits. The model changes the recipe
by calling `update_recipe` with the whole recipe; the `StateMapper` turns that call
into the new state with `SetState`, and the panel follows `AgentState<T>.OnChanged`.
**New** starts again from the original recipe.

#### Predictive state

Ask for a document and the agent calls `propose_document`, a manual UI action. As
the call streams, the `StateMapper` shows the proposal at once with
`SetPredictiveState`, and the **Document** panel marks what it adds and removes
compared with the document before it. The proposal's custom template sets
`Arguments["accepted"]` and calls `InvokeAsync()`; the action then calls
`AcceptPredictiveState` or `RejectPredictiveState`. If the turn stops or fails
before you decide, the engine rejects the prediction and the document rolls back.
The document also travels with each request as a JSON message, so your own edits
count.

#### Reasoning

Requests ask for a reasoning summary (`ChatOptions.Reasoning`). With a reasoning
model the built-in reasoning view shows the summary, collapsed, above the answer.
Reasoning that arrives without visible text, such as encrypted reasoning, gets no
row.

#### Persistence

`options.Thread` is `FileConversationThread`, an `IConversationThread` that saves
each committed turn's raw `ChatResponseUpdate`s as JSON with an atomic write.
Opening the scenario calls `RestoreAsync()`, so the conversation, its cards
included, comes back after you quit the app; **Reload from disk** reads it again
into a new agent, as launching does. **New** clears the file. **Example** replays the
recorded run, which then becomes the saved conversation.

#### How the scenarios are wired

Each scenario is a `PlaygroundScenario` (in `Scenarios/Core`, free of MAUI so the
tests share it) that creates its `UIAgent`, with its handlers, UI actions, state
mapper, and thread. The engine sends through a `PlaygroundChatClient` built for the
scenario: each request gets the scenario's instructions and tools as `ChatOptions`,
built again for every request round, and its state as a message added to that
request only. Recordings mark those messages, so a replay leaves them out of the
conversation it shows. Each scenario records the live exchanges of the current run
in its own recording (fresh each launch); its bundled example is in
`Resources/Raw/scenarios`.

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
replay driver, the custom cards, and the agentic scenarios, and replay every
recording in `TestData`, including each scenario's example, through the engine:

```sh
dotnet test tests/AIExtensions/AIExtensions.Sample.ChatPlayground.Tests
```
