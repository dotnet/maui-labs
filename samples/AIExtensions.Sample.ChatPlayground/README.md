# AIExtensions.Sample.ChatPlayground

`AIExtensions.Sample.ChatPlayground` has three focused .NET MAUI playground tabs: **Chat** (`IChatClient`), **Embeddings** (`IEmbeddingGenerator<string, Embedding<float>>`), and **Images** (`IImageGenerator`). Each lists registered real providers by descriptor and uses the selected provider directly. Chat can replay saved conversations offline; live requests never use mock responses or cloud fallbacks. Completed chat interactions are automatically recorded in app-private storage.

> **Local-only credential warning:** Debug builds embed the existing shared local `secrets.json` so a device app can read it. Release builds do not embed user secrets. Never distribute, publish, share, or commit a Debug build that embeds secrets.

## Architecture

| Area | Responsibility |
| --- | --- |
| `MauiProgram` | Registers independent chat, embedding, and image providers, recording middleware, tools, view models, and the Debug-only DevFlow agent. Without a live chat provider, Chat opens in Replay; the Embeddings and Images tabs still work with their configured providers. |
| `Services/` | `DescribedChatClient` exposes a `ChatClientDescriptor` through `IChatClient.GetService`, including capability and replay metadata. `ImageInputService` loads picked or packaged images; `PlaygroundTools` provides real local date/time and calculator functions. |
| `Features/Recording/` | Versioned recording models and serializer, a storage-agnostic `IChatRecordingSession`, and record/replay `IChatClient` wrappers. This folder has no archive, index, or MAUI dependencies. |
| `Features/Library/` | Per-chat atomic storage, catalog, active record/replay session, import, and export. Exposes read-only `IChatLibrary` to independent indexers. |
| `Features/Search/` | Descriptor-based `IEmbeddingGenerator` registrations, replaceable model-keyed indexes, and local Contains/cosine search. Depends only on `IChatLibrary` and recording models, never changes recording JSON or activates a chat. |
| `Features/Images/` | Lazy, described `IImageGenerator` wrappers and a portable generation service for prompts, edits, request options, and image results. Does not depend on chat recordings or MAUI controls. |
| `Features/Storage/` | Atomic file writes shared by the library and derived search indexes. |
| `Features/Chat/` | `ChatConversation` coordinates protocol history (`ChatTurnExecutor`), visible entry IDs (`TranscriptEmitter`), and response projection (`ChatResponseProjector`). It emits typed `TranscriptChange` values rather than MAUI view models. |
| `Controls/` | Reusable anchored popup, info tip, and compact settings layout shared by the three tabs. |
| `ViewModels/` | Separate chat, saved-chat search, embedding, and image presentation state. Settings builds the chat client selector and `ChatOptions`; Main coordinates chat requests and files. |
| `MainPage`, `EmbeddingPage`, `ImagePage`, and `Views/` | Three tabs with compiled bindings. Replay's Find chats is a compact popup anchored to its button, not a root-level dialog. The embedding and image settings panels become overlays at compact widths. |

The chat selector uses the real `IChatClient`; `ChatClientOption` supplies only display bindings and stable radio-button IDs. Embedding and image providers likewise expose metadata through their respective generator's `GetService`, with no parallel factory list; their underlying models are created lazily on first use. **Contains** is an explicit text-search mode, not a fake embedding generator. `Models/PlaygroundJsonContext` supplies trim-safe JSON schema metadata.

Builders put `.UseRecording(recording)` first so the outermost wrapper sees the final response after logging, function invocation, and optional Azure image generation. The registered Azure image generator is shared with the Images tab and can also become a chat tool; its middleware preserves user images and keeps generated images in later requests. Apple chat has no image-generation fallback. The descriptor marks Replay explicitly, and `ReplayChatClient` has no recording wrapper or provider reference.

Live and replay turns share one per-turn, unbounded channel for every visible change, including user input. The producer does not wait for UI rendering; the view model drains the channel before marking a turn complete, and errors and cancellation reach the reader. A transcript bubble is not a protocol `ChatMessage`: only `ChatAreaViewModel` creates MAUI `ImageSource` objects. Plain .NET tests compile the chat, recording, library, search, and storage features plus settings without starting a device.

Protocol history retains portable content, not provider-owned `RawRepresentation` objects. Image-generation responses can repeat native reasoning items; forwarding those objects on the next turn causes Azure to reject duplicate item IDs. The visible response and its recording still retain their text, tool calls, and image bytes.

## Embeddings and images

The **Embeddings** tab selects a real registered embedding generator, or local **Contains** text search. It can generate a vector for entered text, optionally request a dimension count, build/update the selected model's saved-chat index, and search it. Its selected method and dimensions are shared with Replay's Find chats popup. The same generator builds each model-keyed index and embeds the corresponding query. Contains compares full saved message text without generating vectors. Neither tab silently falls back to another model after an error.

The **Images** tab selects a registered real `IImageGenerator`. Enter a prompt to generate images; attach a chosen file or the packaged sample image to edit when the provider supports edits. Count, size, and media type are optional, so leaving them at provider defaults sends no request options. Generation can be cancelled and displays returned image bytes or HTTP(S) image URIs. Errors, including unsupported provider options and missing images, are visible. Results are not added to saved chats or persisted by this tab. Azure image generation requires `AI:ImageDeploymentName` but not a configured chat client.

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

## Saved chats, search, and replay

Every completed real-client response, streaming or non-streaming, is atomically saved in its own versioned recording under `FileSystem.AppDataDirectory/chat-playground/chats/<id>.json`. A small `chat-playground/catalog.json` tracks titles, order, and the active chat. **New** starts a blank chat but keeps completed chats in the library; a second New on an empty chat does not create a duplicate. **Export** prepares a JSON snapshot in cache and opens the system share sheet to save a copy to Files or another destination. Opening the share sheet does not confirm the export succeeded. Exporting is optional for retaining a chat locally, but the temporary export itself may be evicted.

Selecting **Replay** replaces the composer with **Find chats**, **Restart**, **Play all**, and **Next turn**. Find chats opens a small popup anchored to that button. It uses the method and optional dimensions selected in the Embeddings tab; Contains is selected initially until you choose another method. Typing filters saved chat titles and message text after 400 ms; Return searches immediately. With a semantic provider selected, the same generator indexes saved texts and embeds each buffered query, and the popup shows indexing progress. A result previews the matching message (or latest assistant reply while browsing); selecting it opens and replays the chat without contacting a chat model. **Import file** in the chat header validates an external recording, saves it as a new chat, and plays it. A cancelled picker or invalid file preserves the active chat. The current chat is restored and replayed on startup. Restart rewinds without deleting the recording; Next turn advances one interaction, and Play all restarts from the beginning. After playback, select a real client to continue. Live sending is blocked during partial playback or after Restart. **Stop** cancels playback and lets you retry. Export, Restart, Play all, and Next turn are disabled for an empty chat; Find chats and Import file remain available.

Search derives each user and assistant text message once from the recorded request history and completed responses; streamed fragments are assembled into completed text. It excludes system instructions, tool calls and results, reasoning, and inline image bytes. Image-only turns remain in the library but need accompanying text for text or semantic matching. Contains uses full normalized messages so matches can cross embedding-chunk boundaries. Semantic indexes split messages into sentence-sized excerpts of at most 360 characters. The local text index and each descriptor's model-keyed vector index live separately under `FileSystem.AppDataDirectory/chat-playground/indexes/`, outside recording schema v2. Index filenames contain a hash of the descriptor's `IndexIdentity`, not a provider URL. New and Import do **not** build vectors; selecting a provider lazily creates or updates only its own index. Changed model identities or requested dimensions get fresh indexes even when vector dimensions match; damaged or incompatible indexes are rebuilt. Provider errors are shown without silently returning Contains matches, and recordings remain intact.

On iOS and Mac Catalyst, `Microsoft.Maui.Essentials.AI.NLEmbeddingGenerator` provides local English NaturalLanguage embeddings (on older Apple OS versions than `AppleIntelligenceChatClient` chat); its selection is independent of the chat client. On Android, Contains is always available; semantic search requires a configured generator such as Azure. To use Azure OpenAI `text-embedding-3-small`, configure `AI:EmbeddingDeploymentName` and select it in Embeddings. **There is no confirmation dialog:** using the Azure generator sends saved user/assistant text and buffered search terms off-device and may incur charges. System instructions, tools, reasoning, and images are excluded. Select Contains or an on-device generator before searching if text must stay local. No remote provider is used as a fallback, and vectors from different identities are never compared.

Chats and derived indexes remain in app-private data until the app data is cleared or the app is uninstalled; this sample does not yet include per-chat deletion. Large recordings can contain inline image bytes, so exported chats are useful as backups. App data can participate in OS backups. Recordings, search snippets, and cloud embeddings may contain private text; review your backup settings and provider selection before storing or sharing sensitive content.

`ReplayChatClient` verifies each request's messages and streaming mode before returning its saved response. Live and replayed responses use the same `IChatClient` execution and rendering path; only the user-message source differs (composer input versus saved request).

The on-disk data identifies `chat-client-playground-recording` schema version 2 and is validated when loaded. Earlier recording files are not supported. The request matcher compares messages and their contents at a JSON path and reports the first mismatch (for example, `$.messages[1].contents[0].text`). Options are stored for reference but are not matched during replay; `Microsoft.Extensions.AI` omits executable tools from their JSON representation. Each interaction records whether it was streaming, so replay rejects calling the wrong `IChatClient` API. Non-streaming recording calls the inner `GetResponseAsync` directly; it is never silently converted to streaming.

This is intentionally a **sample-contained subset**, not a general recording package. Messages (including selected image bytes and tool call/results), streaming updates, responses, and informational options use the `Microsoft.Extensions.AI` JSON contracts via `AIJsonUtilities.DefaultOptions`. A JSON data URI preserves each image's bytes and media type. Unsupported polymorphic content fails during serialization or load rather than being silently converted.

Provider-owned `RawRepresentation` values are ignored by the library's JSON contracts. Treat exported JSON as sensitive local data and do not share it without review.

`tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests` replays chats captured from the deployed models without device, credentials, or network access: plain and structured JSON responses (streaming and non-streaming), function calls, image input, a visible reasoning summary, and generated image bytes. Multi-turn tests cover tool and image continuation, changing response modes, and instructions. Archive tests cover import, New, restart, and reopening saved chats. Search tests use fake embedding generators to verify model isolation, private-payload exclusions, progress, buffering, and recovery. Image service tests cover lazy provider creation, generation and edits, options, invalid outputs, and cancellation. The committed reasoning fixtures omit opaque protected reasoning payloads.

## Azure OpenAI setup

The sample reuses user-secrets ID `2727d4aa-a3a5-484b-9447-91604761972b`. The current deployments are `gpt-6-luna` for chat (Responses API: text, tools, image input, and optional reasoning summaries) and `gpt-image-1-mini` for image generation (Images API). Configure local secrets without printing or copying the endpoint or key:

```bash
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:Endpoint" "<Azure endpoint>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ApiKey" "<API key>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:DeploymentName" "gpt-6-luna"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ImageDeploymentName" "gpt-image-1-mini"
# Optional: use your actual deployment name if it differs from the model name.
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:EmbeddingDeploymentName" "text-embedding-3-small"
# Change this identity if the Azure deployment changes model or revision in place.
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:EmbeddingIndexRevision" "my-model-revision"
```

Cloud image input combines `TextContent` and real image bytes in `DataContent` inside the same user `ChatMessage`; image-only messages are also supported. The round add button opens an anchored attachment menu with **Choose image** (then opens the system picker) and **Use sample image** (a packaged .NET bot PNG for repeatable DevFlow and manual tests). The selected image remains visible in the sent user bubble. The picker accepts JPEG, PNG, GIF, and WebP files up to 20 MB and reports unsupported formats directly. The menu uses `Controls/PopupMenu.Content`, an attached property for buttons on a grid-backed page. It accepts arbitrary XAML content, anchors above or below the trigger, closes on a nested button action, and dismisses when tapping the dimmed backdrop without activating the chat. An unpadded outer grid hosts the full-page scrim while the inner grid retains the content padding and safe area. `PopupMenuView.xaml` defines the visual control template and inserts menu content with a `ContentPresenter`; `PopupMenu.cs` owns the attached behavior. `Controls/InfoTip` holds the reusable help affordance; `Views/` contains app-specific chat UI. MAUI's native `ContextFlyout` only handles desktop context clicks and does not accept arbitrary mobile menu layouts.

To reuse the popup on another button in a grid-backed page, declare its menu content inline:

```xml
<Button Text="Actions">
    <controls:PopupMenu.Content>
        <VerticalStackLayout Spacing="8">
            <Button Text="Import" Command="{Binding ImportCommand}" />
        </VerticalStackLayout>
    </controls:PopupMenu.Content>
</Button>
```

For `AI:Endpoint`, use Azure OpenAI's OpenAI-compatible v1 URL (for example, `https://<resource>.openai.azure.com/openai/v1/`). Configure `AI:DeploymentName` for chat, `AI:ImageDeploymentName` for images, and `AI:EmbeddingDeploymentName` for embeddings independently; each configured provider requires `AI:Endpoint` and `AI:ApiKey`. Azure chat uses `GetResponsesClient().AsIChatClient(deployment)` and, when an image generator is registered, `.UseImageGenerationPreservingInputs()` to keep original user images alongside generated images. The Images tab uses the same `GetImageClient(imageDeployment).AsIImageGenerator()` directly. Azure's image endpoint rejects `ImageGenerationOptions.ResponseFormat`, so the sample does not send it. A resource-root URL is rejected rather than silently sent to the wrong API.

## Local provider slot

The sample registers `AppleIntelligenceChatClient` on supported iOS and Mac Catalyst 26+ systems, with logging and function invocation. On other platforms or unsupported OS versions it does not register Apple; configure Azure OpenAI to run the sample there. The first real Apple request confirms model availability.

Add Windows AI, Android CoreAI, or any custom real `IChatClient` inside `MauiProgram.AddChatClients`; include recording and descriptor builder extensions so the sidebar discovers it and its turns are saved automatically:

```csharp
services.AddSingleton<IChatClient>(provider =>
    new MyLocalChatClient()
        .AsBuilder()
        .UseRecording(provider.GetRequiredService<IChatRecordingSession>())
        .UseDescriptor(new ChatClientDescriptor(
            "My local provider",
            "My local provider is ready.",
            SupportsImageInput: true))
        .UseLogging(provider.GetRequiredService<ILoggerFactory>())
        .UseFunctionInvocation()
        .Build());
```

The selected provider receives all requested `ChatOptions`, so it defines support for tools, tool modes, multiple calls, structured output, and generation controls. Set `SupportsImageInput` accurately because it is the only behavior metadata gates.

`Microsoft.Maui.Essentials.AI` supplies both `AppleIntelligenceChatClient` for Apple Intelligence chats (iOS/Mac Catalyst 26+) and `NLEmbeddingGenerator` for on-device NaturalLanguage search (iOS 13+/Mac Catalyst 13.1+). The two capabilities are independent. Without a live chat provider, Chat starts in Replay; you can still use a separately configured embedding or image generator.

To add more embedding providers, register one descriptor and generator factory per model in `MauiProgram.AddChatSearch`. No view model, picker, or search branch needs changing:

```csharp
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
    new DescribedEmbeddingGenerator(
        () => new MyEmbeddingGenerator(),
        new ChatSearchDescriptor(
            "local-model-v2", "My local index", "Search by meaning on this device.",
            "my-local-model/english/v2", ChatSearchDataLocation.OnDevice)));
```

Use `ChatSearchDataLocation.Remote` for any generator that sends chat text off-device; label its data flow clearly. `IndexIdentity` must identify the model, language, and revision, not just a UI label: change it whenever a generator or remote deployment is swapped, including same-dimension models. IDs and model identities must be distinct across registrations; duplicate registrations fail instead of sharing another model's vectors. The built-in Apple identity includes the NaturalLanguage implementation version and OS version, while the Azure identity includes endpoint, deployment, and the optional `AI:EmbeddingIndexRevision`. To add another image provider, register a `DescribedImageGenerator` with its `ImageGeneratorDescriptor` in `MauiProgram.AddImageGenerators`.

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

Mac Catalyst builds include `com.apple.security.files.user-selected.read-write` for the Import file and Choose image pickers while sandboxed; MAUI opens selected documents in place, even though this sample only reads them. Debug builds also include the `com.apple.security.network.server` entitlement required by DevFlow's local server.

The UI follows the device's light or dark appearance. At compact widths, the settings sidebar opens as a full-width phone overlay (or a sidebar-sized tablet overlay) with a backdrop that blocks accidental chat actions. On narrow phones, New and Export move below the title and status so the header remains readable. The composer keeps its add, prompt, and send controls on one line.
