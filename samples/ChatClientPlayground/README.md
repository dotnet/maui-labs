# ChatClientPlayground

`ChatClientPlayground` is a single-page .NET MAUI reference sample for exercising real `Microsoft.Extensions.AI` `IChatClient` features. It switches at runtime between keyed device-local and cloud slots, or a provider-free recording playback client. Live requests contain no mock clients, canned responses, or cloud fallback.

> **Local-only credential warning:** Debug builds embed the existing shared local `secrets.json` so a device app can read it. Release builds do not embed user secrets. Never distribute, publish, share, or commit a Debug build that embeds secrets.

## Architecture

- `MauiProgram` is the composition root. It registers exactly two real keyed `IChatClient` factories under `ChatClientKind.Local` and `ChatClientKind.Cloud`, wrapping each with logging, `UseFunctionInvocation()`, and `DescribedChatClient`.
- `DescribedChatClient` exposes its metadata-only `ChatClientDescriptor` through `GetService<ChatClientDescriptor>()` while forwarding every other service request to its real inner client. A factory throws a precise error rather than registering a fake client when its slot cannot be constructed.
- `Services/ChatClientService` resolves a `ChatClientKind` to `ChatClientSelection` (a real client, the provider-free recording client, or an actionable unavailable state). `MainViewModel` does not access `IServiceProvider`.
- `Services/ImageInputService` copies a picked image into memory or loads the packaged sample image so both paths create the same image message.
- `Services/PlaygroundTools` exposes deterministic local date/time and calculator `AIFunctionFactory.Create` tools.
- `Models/PlaygroundJsonContext` provides source-generated, trim-safe schema metadata for explicit-schema JSON responses.
- `Services/ChatClientService` optionally wraps either keyed real provider to record its responses and resolves the Recording client directly for playback. `Services/ChatRecordingService` manages one active recording and replay cursor, atomically auto-saves completed interactions to app cache, and replaces the active recording when a file is selected. The serializer and client wrappers live beside the other services, with their data types in `Models/`. This sample-contained subset does not reference the recording project used by other work.
- `ViewModels/SettingsPaneViewModel` and `ChatAreaViewModel` are registered in DI. `MainViewModel` receives both explicitly, wires their commands/events, and owns portable `List<ChatMessage>`, cancellation, request execution, and tool/response presentation. Settings owns provider, `ChatOptions`, and recording controls; Chat owns messages, composer, and attachment UI state.
- `MainPage` composes compiled-binding `SettingsPane` and `ChatArea` child view models. `ChatArea` keeps a `CollectionView` with `ItemSizingStrategy="MeasureAllItems"` and `KeepLastItemInView`; changed streaming items invalidate their own and the collection's measure before a delayed index-based scroll. Debug builds include the DevFlow agent.

## Response options

Two independent checkboxes select all four response paths:

| Streaming | Structured JSON | Behavior |
| --- | --- | --- |
| Off | Off | Plain text from `GetResponseAsync` |
| On | Off | Incremental text from `GetStreamingResponseAsync` |
| Off | On | Explicit-schema JSON from `GetResponseAsync` |
| On | On | Incremental explicit-schema JSON from `GetStreamingResponseAsync` |

Streaming is enabled by default. Structured JSON uses `ChatResponseFormat.ForJsonSchema<PlaygroundResponse>()`; there is no typed `GetResponseAsync<T>` helper path.

## Controls and tools

The sidebar exposes instructions, Streaming and Structured JSON checkboxes, individual tools, inline tool mode radios (`Auto`, `None`, `Require any`), and a tri-state multiple-tool-calls choice: `Default` leaves `AllowMultipleToolCalls` null, while `Allow` and `Disallow` send explicit values. Generation controls include Temperature, TopP, TopK, MaxOutputTokens, FrequencyPenalty, PresencePenalty, Seed, and comma-separated StopSequences. Check a setting's box to send it; leave it unchecked to keep the provider option unset. Decimal settings use `.` as the separator so requests are consistent across device locales. Hover or focus `(i)` beside a setting for concise help.

`ChatOptions`, including instructions, response format, tools, tool mode, multiple-tool-call, and generation options, are passed directly to the selected real `IChatClient`. Providers own support for each option and report unsupported settings through the request error UI. The only temporary metadata gate is image input: the Apple local registration currently sets `SupportsImageInput` to `false`, while Azure sets it to `true`. Update that metadata when a local or custom provider accepts images.

The sample tools provide only real deterministic in-app results: `get_current_local_datetime` and `calculate`; it deliberately does not pretend to provide weather or web-search data. Function calls appear as distinct tool bubbles with the function name, formatted arguments, and result. A bubble is updated when its matching result arrives.

System instructions are not displayed until a request actually uses them. The chat has one system-instructions bubble: later requests update that bubble if instructions change, and clearing instructions removes it. The single-line prompt sends on **Enter/Return**.

`ModelId` is deliberately not exposed. Azure model selection is represented by the configured deployment, while the selected local provider owns its own model selection; a shared input would not have equivalent semantics.

## Recording and replay

With **Local provider** or **Cloud** selected, the **RECORDING** section shows a round **Start recording** icon to record real streaming or non-streaming responses; the square **Stop recording** icon turns recording off without discarding it. Each completed interaction auto-saves to `FileSystem.CacheDirectory/chat-playground.autosave.json`. The reset icon starts a new empty recording; the save icon appears when a recording exists and copies it atomically to `FileSystem.AppDataDirectory/chat-playground.recording.json`. The icon tooltips and info tip explain these actions and paths. The OS may clear app cache; save recordings you need to keep. Debug recordings can contain private content; review them before sharing.

Select the third **Recording** client to play the active recording without invoking either model, even if a provider is unavailable. The **RECORDING** section then shows only playback actions: when the recording is empty, the folder icon chooses a JSON file; loading validates it and atomically replaces the cached active recording. When it contains interactions, the folder changes to **Clear recording**, which empties it before a different file can be selected. **Play** renders the next recorded interaction and its original request; the reset icon restarts from the beginning, and the save icon remains available for a populated recording. Live response, tool, generation, and chat-composer controls are disabled while Recording is selected. Switch to Local or Cloud to continue chatting with the replayed conversation history and your existing live settings. **Clear conversation** keeps the recording and restarts replay from the beginning. The low-level replay client still strictly matches message and option data against recorded `DataContent` when called through `IChatClient`.

The on-disk data identifies `chat-client-playground-recording` schema version 1 and is validated when loaded. The canonical request matcher compares messages, contents, and supported options at a JSON path and reports the first mismatch (for example, `$.messages[1].contents[0].text`). Each interaction records whether it was streaming, so replay rejects calling the wrong `IChatClient` API. Non-streaming recording calls the inner `GetResponseAsync` directly; it is never silently converted to streaming.

This is intentionally a **sample-contained subset**, not a general recording package. It supports the playground's `TextContent`, inline-base64 `DataContent` (including selected images), `FunctionCallContent`, `FunctionResultContent`, `ErrorContent`, and `UsageContent`, plus response-update role, finish reason, model/message/response/conversation metadata. It records the options exposed by this UI: instructions; temperature, token, sampling, penalty, seed, and stop-sequence settings; function tool name/description/input schema; multiple-tool-call and tool mode; and JSON response schema. Unsupported content, annotations, additional properties, tools, response formats, raw option factories, and arbitrary value types fail explicitly instead of being silently dropped.

Provider-owned `RawRepresentation` values are normalized out before persistence: the sample records only its allowlisted semantic fields and never serializes arbitrary provider objects. Recordings can nevertheless contain user prompts, tool arguments/results, and inline image bytes. Treat the app-data file as sensitive local data and do not share it without review.

## Azure OpenAI setup

The sample reuses user-secrets ID `2727d4aa-a3a5-484b-9447-91604761972b`. Configure the existing local secrets without printing or copying their values:

```bash
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:Endpoint" "<Azure endpoint>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ApiKey" "<API key>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:DeploymentName" "<chat deployment>"
```

Cloud image input combines `TextContent` and real image bytes in `DataContent` inside the same user `ChatMessage`; image-only messages are also supported. The round add button opens an in-app anchored attachment menu with **Choose image** (then opens the system picker) and **Use sample image** (a packaged .NET bot PNG for repeatable DevFlow and manual tests). The selected image remains visible in the sent user bubble. The picker accepts JPEG, PNG, GIF, and WebP files up to 20 MB and reports unsupported formats directly.

For `AI:Endpoint`, use either the Azure OpenAI resource root (for example, `https://<resource>.openai.azure.com/`) or the OpenAI-compatible endpoint ending in `/openai/v1/`. The composition root selects the matching SDK client and exposes both through the cloud `IChatClient` slot.

## Local provider slot

“Local” is an interchangeable keyed slot, not an Apple-specific category. The default local factory creates `AppleIntelligenceChatClient` on supported iOS and Mac Catalyst 26+ systems, with logging and function invocation. On other platforms or unsupported OS versions it throws an actionable construction error—never a fake client and never a cloud fallback. The first real Apple request confirms model availability.

Replace the local factory in `MauiProgram` with Windows AI, Android CoreAI, or any custom real `IChatClient`, then wrap it with its descriptor:

```csharp
builder.Services.AddKeyedSingleton<IChatClient>(ChatClientKind.Local, static (services, _) =>
    new DescribedChatClient(
        new MyLocalChatClient()
        .AsBuilder()
        .UseLogging(services.GetRequiredService<ILoggerFactory>())
        .UseFunctionInvocation()
        .Build(),
        new ChatClientDescriptor(
            "My local provider",
            "My local provider is ready.",
            SupportsImageInput: true)));
```

The selected provider receives all requested `ChatOptions`, so it defines support for tools, tool modes, multiple calls, structured output, and generation controls. Set `SupportsImageInput` accurately because it is the only behavior metadata gates.

`Microsoft.Maui.Essentials.AI` also includes `NLEmbeddingGenerator` for on-device NaturalLanguage embeddings. This is a chat-only sample, so it does not use embeddings.

## Run and verify

```bash
dotnet build samples/ChatClientPlayground/ChatClientPlayground.csproj -f net10.0-maccatalyst
dotnet build -t:Run samples/ChatClientPlayground/ChatClientPlayground.csproj -f net10.0-maccatalyst

# Debug build after launching the app
maui devflow list
maui devflow ui tree --depth 1
```

Mac Catalyst debug builds include the `com.apple.security.network.server` entitlement required by DevFlow's local server.

At compact widths, the settings sidebar becomes an overlay opened from the **Settings** button so the chat area remains usable on phones.
