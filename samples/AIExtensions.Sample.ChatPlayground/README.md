# AIExtensions.Sample.ChatPlayground

`AIExtensions.Sample.ChatPlayground` is a single-page .NET MAUI reference sample for exercising real `Microsoft.Extensions.AI` `IChatClient` features. It lists the registered clients using each client's descriptor; the sample configures Apple Intelligence and Azure OpenAI. Live requests contain no mock responses, canned responses, or cloud fallback. Every completed interaction is automatically recorded in app-private storage.

> **Local-only credential warning:** Debug builds embed the existing shared local `secrets.json` so a device app can read it. Release builds do not embed user secrets. Never distribute, publish, share, or commit a Debug build that embeds secrets.

## Architecture

| Area | Responsibility |
| --- | --- |
| `MauiProgram` | Registers real Apple Intelligence and Azure OpenAI Responses API clients, recording middleware, tools, view models, and the Debug-only DevFlow agent. An unavailable/unconfigured provider is not registered; startup fails with a setup error if no live provider exists. |
| `Services/` | `DescribedChatClient` exposes a `ChatClientDescriptor` through `IChatClient.GetService`, including capability and replay metadata. `ImageInputService` loads picked or packaged images; `PlaygroundTools` provides real local date/time and calculator functions. |
| `Features/Recording/` | Versioned recording data, strict serializer, atomic auto-save, and record/replay `IChatClient` wrappers. This folder uses no MAUI or playground UI/provider types and can be reused from a terminal host. |
| `Features/Chat/` | `ChatConversation` coordinates protocol history (`ChatTurnExecutor`), visible entry IDs (`TranscriptEmitter`), and response projection (`ChatResponseProjector`). It emits typed `TranscriptChange` values rather than MAUI view models. |
| `ViewModels/` | Settings builds the client selector and `ChatOptions`; Main coordinates requests, cancellation, and chat files; Chat applies transcript changes and owns the composer and observable bubbles. |
| `MainPage` and `Views/` | Compiled-binding settings/chat panes and a dynamically measured `CollectionView`. Streaming bubbles invalidate layout before a delayed index-based scroll. |

The selected value is the real `IChatClient`; `ChatClientOption` only supplies descriptor bindings and stable radio-button IDs. Adding a described client in `MauiProgram.AddChatClients` adds it to the selector without another registration list. `Models/PlaygroundJsonContext` supplies trim-safe JSON schema metadata.

Builders put `.UseRecording(recording)` first so the outermost wrapper sees the final response after logging, function invocation, and Azure image generation. Azure's image generator becomes a chat tool without switching clients; its middleware preserves user images and keeps generated images in later requests. Apple chat has no image-generation fallback. The descriptor marks Replay explicitly, and `ReplayChatClient` has no recording wrapper or provider reference.

Live and replay turns share one per-turn, unbounded channel for every visible change, including user input. The producer does not wait for UI rendering; the view model drains the channel before marking a turn complete, and errors and cancellation reach the reader. A transcript bubble is not a protocol `ChatMessage`: only `ChatAreaViewModel` creates MAUI `ImageSource` objects. Plain .NET tests compile the entire chat and recording folders plus settings without starting a device.

Protocol history retains portable content, not provider-owned `RawRepresentation` objects. Image-generation responses can repeat native reasoning items; forwarding those objects on the next turn causes Azure to reject duplicate item IDs. The visible response and its recording still retain their text, tool calls, and image bytes.

## Chat response options

Two independent checkboxes select all four response paths:

| Streaming | Structured JSON | Behavior |
| --- | --- | --- |
| Off | Off | Plain text from `GetResponseAsync` |
| On | Off | Incremental text from `GetStreamingResponseAsync` |
| Off | On | Explicit-schema JSON from `GetResponseAsync` |
| On | On | Incremental explicit-schema JSON from `GetStreamingResponseAsync` |

Streaming is enabled by default. Structured JSON uses `ChatResponseFormat.ForJsonSchema<PlaygroundResponse>()`; there is no typed `GetResponseAsync<T>` helper path.

## Controls and tools

The sidebar exposes instructions, Streaming and Structured JSON checkboxes, individual tools, and inline tool mode radios (`Auto`, `None`, `Require any`). These bind to `ChatToolMode` directly. The multiple-tool-calls choice uses a nullable boolean: `Default` leaves `AllowMultipleToolCalls` null, while `Allow` and `Disallow` send explicit values. Generation controls include Temperature, TopP, TopK, MaxOutputTokens, FrequencyPenalty, PresencePenalty, Seed, and comma-separated StopSequences. Check a setting's box to send it; leave it unchecked to keep the provider option unset. Decimal settings use `.` as the separator so requests are consistent across device locales. Tap the circular info icon beside a setting or client for its explanation; desktop users can also hover for a tooltip. Replay hides live request options and explains that playback uses the options saved with each turn.

The Azure chat client offers a **Reasoning summary** checkbox (on by default) that asks for a medium-effort visible summary. A model may omit a summary, especially for simple prompts. Only returned `TextReasoningContent.Text` is displayed; opaque `ProtectedData` is never shown as text. Other chat clients are not sent reasoning options.

`ChatOptions`, including instructions, response format, tools, tool mode, multiple-tool-call, and generation options, are passed directly to the selected real chat `IChatClient`. Providers own support for each option and report unsupported settings through the request error UI. **Generate or edit image** adds `HostedImageGenerationTool` to Azure chat's options. The image middleware converts it to callable functions for the chat model, including in streaming conversations. Generated image bytes appear in assistant bubbles and are preserved in saved chats and offline replay. Leave the checkbox unchecked to prevent image-generation calls. The Apple client never offers the Azure image tool. The Apple client descriptor sets `SupportsImageInput` to `false`, while Azure chat sets it to `true`. With Apple selected, the add-image button is disabled and grayed out, and the status explains why. Update that metadata when a provider accepts images.

The local sample tools provide only real deterministic results: `get_current_local_datetime` and `calculate`; it deliberately does not pretend to provide weather or web-search data. The Azure image tool calls the real configured image model and may incur charges. Function calls appear as distinct tool bubbles with the function name, formatted arguments, and result. A bubble is updated when its matching result arrives.

System instructions are not displayed until a request actually uses them. The chat has one system-instructions bubble: later requests update that bubble if instructions change, and clearing instructions removes it. The single-line prompt sends on **Enter/Return**.

`ModelId` is deliberately not exposed. Azure model selection is represented by the configured deployment, while the selected local provider owns its own model selection; a shared input would not have equivalent semantics.

## Auto-save, load, and replay

Every completed real-client response, streaming or non-streaming, is recorded and atomically auto-saved to `FileSystem.AppDataDirectory/chat-playground.autosave.json`. Existing autosaves in `FileSystem.CacheDirectory` migrate on startup if no app-data autosave exists. The header keeps **New** and **Save** beside the title. **New** replaces the current chat with a blank one; use **Save** first if you need to keep the previous chat. **Save** prepares a JSON snapshot in cache and opens the system share sheet so you can save a copy to Files or another destination. Opening the share sheet does not itself confirm that you exported the file. The OS may evict the temporary export; clearing app data or uninstalling removes the autosave, so export chats you need to keep. App data can be included in device backups. Recordings may contain private prompts, tool arguments/results, and inline image bytes; review them and your backup settings before recording or sharing sensitive content.

Selecting **Replay** replaces the composer with **Load**, **Restart**, **Play all**, and **Next turn**. Load validates a saved JSON file and automatically plays its complete chat into view without contacting a model. Restart clears the displayed transcript and rewinds the current recording without deleting it; Next turn advances one interaction, and Play all restarts from the beginning and plays every turn. On app startup, an auto-saved chat is also played into view with Replay selected. Replay stays selected afterward; choose a real client to continue the chat once every turn has played. Live sending is blocked during partial playback or after Restart to avoid appending a response to incomplete history. **Stop** cancels playback and lets you retry. Save, Restart, Play all, and Next turn are disabled for an empty chat; Load is still available. A cancelled file picker or invalid file leaves the current chat intact.

`ReplayChatClient` verifies each request's messages and streaming mode before returning its saved response. Live and replayed responses use the same `IChatClient` execution and rendering path; only the user-message source differs (composer input versus saved request).

The on-disk data identifies `chat-client-playground-recording` schema version 2 and is validated when loaded. Earlier recording files are not supported. The request matcher compares messages and their contents at a JSON path and reports the first mismatch (for example, `$.messages[1].contents[0].text`). Options are stored for reference but are not matched during replay; `Microsoft.Extensions.AI` omits executable tools from their JSON representation. Each interaction records whether it was streaming, so replay rejects calling the wrong `IChatClient` API. Non-streaming recording calls the inner `GetResponseAsync` directly; it is never silently converted to streaming.

This is intentionally a **sample-contained subset**, not a general recording package. Messages (including selected image bytes and tool call/results), streaming updates, responses, and informational options use the `Microsoft.Extensions.AI` JSON contracts via `AIJsonUtilities.DefaultOptions`. A JSON data URI preserves each image's bytes and media type. Unsupported polymorphic content fails during serialization or load rather than being silently converted.

Provider-owned `RawRepresentation` values are ignored by the library's JSON contracts. Treat exported JSON as sensitive local data and do not share it without review.

`tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests` replays chats captured from the deployed models without device, credentials, or network access: plain and structured JSON responses (streaming and non-streaming), one and two function calls, image input, a visible reasoning summary, and generated image bytes. Multi-turn recordings cover streaming calculator followed by non-streaming date/time calls; an attached image edited into a generated image followed by a streamed description; and three turns that switch from streaming JSON to non-streaming JSON to plain streaming text while updating and clearing instructions. Tests check prior tool results, original and generated image bytes, exact replay ordering, instruction changes, and cursor safety. They also exercise the MAUI-free settings view model's client selection and request-option mapping. The committed reasoning fixtures omit opaque protected reasoning payloads; their visible summaries and responses came from live calls.

## Azure OpenAI setup

The sample reuses user-secrets ID `2727d4aa-a3a5-484b-9447-91604761972b`. The current deployments are `gpt-6-luna` for chat (Responses API: text, tools, image input, and optional reasoning summaries) and `gpt-image-1-mini` for image generation (Images API). Configure local secrets without printing or copying the endpoint or key:

```bash
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:Endpoint" "<Azure endpoint>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ApiKey" "<API key>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:DeploymentName" "gpt-6-luna"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ImageDeploymentName" "gpt-image-1-mini"
```

Cloud image input combines `TextContent` and real image bytes in `DataContent` inside the same user `ChatMessage`; image-only messages are also supported. The round add button opens an anchored attachment menu with **Choose image** (then opens the system picker) and **Use sample image** (a packaged .NET bot PNG for repeatable DevFlow and manual tests). The selected image remains visible in the sent user bubble. The picker accepts JPEG, PNG, GIF, and WebP files up to 20 MB and reports unsupported formats directly. The menu uses `Controls/PopupMenu.Content`, an attached property for buttons on a grid-backed page. It accepts arbitrary XAML content, anchors above or below the trigger, closes on a nested button action, and dismisses when tapping the dimmed backdrop without activating the chat. An unpadded outer grid hosts the full-page scrim while the inner grid retains the content padding and safe area. `PopupMenuView.xaml` defines the visual control template and inserts menu content with a `ContentPresenter`; `PopupMenu.cs` owns the attached behavior. `Controls/InfoTip` holds the reusable help affordance; `Views/` contains app-specific chat UI. MAUI's native `ContextFlyout` only handles desktop context clicks and does not accept arbitrary mobile menu layouts.

To reuse the popup on another button in a grid-backed page, declare its menu content inline:

```xml
<Button Text="Actions">
    <controls:PopupMenu.Content>
        <VerticalStackLayout Spacing="8">
            <Button Text="Load" Command="{Binding LoadCommand}" />
        </VerticalStackLayout>
    </controls:PopupMenu.Content>
</Button>
```

For `AI:Endpoint`, use Azure OpenAI's OpenAI-compatible v1 URL (for example, `https://<resource>.openai.azure.com/openai/v1/`). Azure chat uses `GetResponsesClient().AsIChatClient(deployment)` and requires `GetImageClient(imageDeployment).AsIImageGenerator()`. `.UseImageGenerationPreservingInputs()` explicitly selects `DataContentHandling.GeneratedImages` because the built-in `.UseImageGeneration()` uses `AllImages`, replacing user-supplied images before they reach the chat model. Do not set `ImageGenerationOptions.ResponseFormat` for this Azure image endpoint; it rejects that parameter. A resource-root URL is rejected rather than silently sent to the wrong API. If Azure credentials are present but `AI:ImageDeploymentName` is omitted, Azure client startup fails with a setup error.

## Local provider slot

The sample registers `AppleIntelligenceChatClient` on supported iOS and Mac Catalyst 26+ systems, with logging and function invocation. On other platforms or unsupported OS versions it does not register Apple; configure Azure OpenAI to run the sample there. The first real Apple request confirms model availability.

Add Windows AI, Android CoreAI, or any custom real `IChatClient` inside `MauiProgram.AddChatClients`; include recording and descriptor builder extensions so the sidebar discovers it and its turns are saved automatically:

```csharp
services.AddSingleton<IChatClient>(provider =>
    new MyLocalChatClient()
        .AsBuilder()
        .UseRecording(provider.GetRequiredService<ChatRecordingService>())
        .UseDescriptor(new ChatClientDescriptor(
            "My local provider",
            "My local provider is ready.",
            SupportsImageInput: true))
        .UseLogging(provider.GetRequiredService<ILoggerFactory>())
        .UseFunctionInvocation()
        .Build());
```

The selected provider receives all requested `ChatOptions`, so it defines support for tools, tool modes, multiple calls, structured output, and generation controls. Set `SupportsImageInput` accurately because it is the only behavior metadata gates.

`Microsoft.Maui.Essentials.AI` also includes `NLEmbeddingGenerator` for on-device NaturalLanguage embeddings. This is a chat-only sample, so it does not use embeddings.

## Run and verify

```bash
./eng/common/dotnet.sh build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
./eng/common/dotnet.sh build -t:Run samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst

# Or build and run on an iOS simulator; replace <simulator-udid> with a booted device's ID.
xcrun simctl list devices booted
./eng/common/dotnet.sh build -t:Run samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 "-p:_DeviceName=:v2:udid=<simulator-udid>"

# On Android, configure Azure OpenAI first; Apple Intelligence is not available.
./eng/common/dotnet.sh build -t:Run samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-android "-p:AdbTarget=-s <device-serial>"

# Target the correct agent when both apps are running.
maui devflow list
maui devflow --agent-port "<port>" ui query --automationId RequestStatusLabel

# Replay the committed live recordings without an app, credentials, or network.
./eng/common/dotnet.sh test tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests/Microsoft.Maui.AI.Chat.Tests.csproj
```

Mac Catalyst builds include `com.apple.security.files.user-selected.read-write` for the Load and Choose image pickers while sandboxed; MAUI opens selected documents in place, even though this sample only reads them. Debug builds also include the `com.apple.security.network.server` entitlement required by DevFlow's local server.

The UI follows the device's light or dark appearance. At compact widths, the settings sidebar opens as a full-width phone overlay (or a sidebar-sized tablet overlay) with a backdrop that blocks accidental chat actions. The composer keeps its add, prompt, and send controls on one line.
