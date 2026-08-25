# MAUI Chat Controls (Blazor Hybrid) sample

This sample mirrors [`ChatControls.Sample`](../ChatControls.Sample/) — the same participants,
delivery states, streaming, simulated microphone, custom `MessageContent`, and theme
scenarios — but the entire chat surface renders through **Blazor Hybrid** using the
provider-neutral `Microsoft.Maui.Chat.Controls.Blazor` package.

There is no AI dependency: an `ObservableChatConversation` from `Microsoft.Maui.Chat.Controls`
drives everything. Layer 2 (shipped in a follow-up PR) will add an AI bridge that composes
this same `<ChatView>` shell.

## What it demonstrates

- A drop-in Blazor `<ChatView>` with a virtualisation-friendly message list and composer.
- Multiple participants, avatars, participant names, timestamps, and delivery states.
- Text, image, file, recorded audio, and custom `MessageContent`.
- Two consumer-registered `MessageContentRenderer<T>` fragments for `GardenTaskContent` and
  `GardenStickerContent` — exercising the exact seam the future AI bridge will use.
- Standard-bubble and bare-content presentation (task cards replace the bubble; stickers
  drop the bubble entirely).
- Deterministic simulated microphone and speech recognition for DevFlow / CDP validation.
- Independent Priya/Diego typing states, conversation busy state, send failure, delivery
  state controls, and a slow-send mode that exercises the stop button.
- Suggestions, file attachments, streaming updates, edit-last / remove-last / reset actions.
- Light, dark, and system themes through `prefers-color-scheme` and an explicit
  `data-theme` opt-in.

The XAML page holds only the participant-simulator sidebar; every chat pixel is Blazor.

## Layout

```
ChatControls.BlazorHybrid.Sample/
├── ChatControls.BlazorHybrid.Sample.csproj    // Razor SDK, MAUI Blazor Hybrid
├── MauiProgram.cs                             // Adds BlazorWebView, ChatControls, DevFlow (Debug)
├── App.xaml{,.cs}                             // Shell + theme resources
├── MainPage.xaml{,.cs}                        // XAML sidebar + BlazorWebView host
├── Shared/                                    // Linked from ../ChatControls.Sample
│   ├── TeamChatViewModel.cs                   // Same neutral view model as the XAML sample
│   ├── GardenTaskContent.cs
│   ├── GardenStickerContent.cs
│   └── SimulatedVoiceServices.cs
├── Components/
│   ├── _Imports.razor
│   ├── Routes.razor                           // Mounts Chat.razor directly - no router needed
│   └── Pages/Chat.razor                       // <ChatView> + MessageContentRenderer fragments
├── Platforms/                                 // Standard MAUI platform folders
└── wwwroot/
    ├── index.html                             // Loads _content/.../mchat.css, sample.css
    └── sample.css                             // Sample-only palette overrides + garden CSS
```

## Run

```bash
dotnet build samples/ChatControls.BlazorHybrid.Sample/ChatControls.BlazorHybrid.Sample.csproj \
  -f net10.0-maccatalyst

dotnet build samples/ChatControls.BlazorHybrid.Sample/ChatControls.BlazorHybrid.Sample.csproj \
  -f net10.0-android
```

## DevFlow / CDP runtime validation playbook

Debug builds automatically start a DevFlow agent and register the Blazor CDP tools. From an
adjacent shell, drive the sample with `maui devflow`:

```bash
maui devflow status
maui devflow screenshot --out screenshots/empty.png
maui devflow tap --automation-id SendParticipantTextButton
maui devflow screenshot --out screenshots/one-message.png
maui devflow tap --automation-id StreamParticipantTextButton
maui devflow screenshot --out screenshots/streaming.png
maui devflow tap --automation-id FailNextSendCheck
maui devflow cdp evaluate --script "document.querySelector('.mchat-composer__textarea').focus(); document.execCommand('insertText', false, 'hi')"
maui devflow cdp evaluate --script "document.querySelector('.mchat-icon-btn--primary').click()"
maui devflow screenshot --out screenshots/send-failure.png
```

Full scenarios exercised in this round:

| Scenario | XAML sidebar action | Blazor DOM assertion |
| --- | --- | --- |
| Empty state | (fresh app) | `.mchat-welcome` visible |
| Text message | Send text | `.mchat-row--incoming .mchat-bubble` |
| Grouped run | Send text · 3× same participant | `.mchat-row--group-continuation` × 2 |
| Streaming | Stream text | `.mchat-bubble--streaming` present, then absent |
| Task card | Send task card | `.garden-task` inside `.mchat-bubble--bare` |
| Sticker | Send sticker | `.garden-sticker` inside `.mchat-row` |
| Attachment | Compose attachment (native picker) | `.mchat-attachment-chip` |
| Simulated audio | Toggle audio (composer) | `.mchat-audio` play/pause + `<audio>` element |
| Live speech | Toggle live speech (composer) | Composer text streams; final utterance sends |
| Send failure | Fail next send + type + Enter | `.mchat-composer__error` shows the safe string |
| Slow send / stop | Slow next send + type + Enter, then Stop | Stop button replaces send during flight |
| Theme swap | Cycle theme | `data-theme` attribute updates on `.mchat-root` |

> This sample is experimental and may change before the neutral Blazor package stabilises.
