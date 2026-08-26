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

## DevFlow / CDP runtime validation results

Debug builds automatically start a DevFlow agent and register the Blazor CDP tools. This
scenario matrix was executed end-to-end on Mac Catalyst with the `maui devflow` CLI and
CDP `Runtime.evaluate` walking the WebView DOM. Every row was captured as evidence in the
PR that introduced the sample.

| Scenario | XAML sidebar action | Blazor DOM assertion | Result |
| --- | --- | --- | --- |
| Empty state | Clear conversation | `.mchat-welcome` visible, `.mchat-row` count = 0, `.mchat-suggestion` count = 3 | ✅ |
| Populated seed | Reset conversation | `.mchat-row` count = 6, participant chrome + timestamps | ✅ |
| Streamed text | Stream text | last `.mchat-text-content` grows in place while `.mchat-bubble--streaming` toggles | ✅ |
| Task card (bare) | Send task card | `.garden-task` inside `.mchat-bubble--bare`, no bubble background | ✅ |
| Sticker (bare) | Send sticker | `.garden-sticker` with `aria-label`, `.mchat-bubble--bare` | ✅ |
| Image render | Send photo | `<img>` inside `.mchat-media`, `alt` from `MediaMessageContent.AltText` | ✅ |
| File card | Send file | `.mchat-file-card` with file-name + size, `role="group"`, `aria-label` = alt text | ✅ |
| Simulated audio → stage | Composer 🎤 → ■ | `.mchat-icon-btn--recording` while capturing, then `.mchat-attachment-chip` = `simulated-recording.wav` | ✅ |
| Send audio → play/pause | ➤ then click ▶ on message | `.mchat-audio` renders with `aria-label`, `<audio>` element, `.mchat-audio__toggle` aria toggles | ✅ |
| Live speech → auto-submit | Composer 🗣 | text streams into `.mchat-composer__textarea`; final utterance is treated as terminal-before-submit (recognizer detached, `IsListening`/`IsLiveSpeechEnabled` cleared so `CanSubmit` accepts), then the ordinary `SendAsync` path fires exactly once and a new `.mchat-row` appears | ✅ |
| Live speech → `AutoSubmitLiveSpeech="false"` | Composer 🗣 | final utterance stays in `.mchat-composer__textarea`; recognizer is detached and `IsListening` is cleared so the user can submit manually | ✅ |
| Typing (one) | ✔ Priya typing | `.mchat-typing[role="status"]` = "Priya is typing…" | ✅ |
| Typing (two) | ✔ Priya + ✔ Diego typing | `.mchat-typing` = "Priya and Diego are typing…" | ✅ |
| Delivery status | Apply delivery status | last message shows sending/sent/delivered/read glyphs and `aria-label` | ✅ |
| Failed send | ✔ Fail next send + submit | `.mchat-composer__error[role="alert"]` = default safe string, `mchat-composer__textarea.value` preserved | ✅ |
| Slow send | ✔ Slow next send + submit | `.mchat-icon-btn--danger` (Stop) replaces primary send button with `aria-label="Stop"` | ✅ |
| Cancel slow send | Click Stop mid-flight | outgoing message removed by handler, error stays null, draft preserved for a re-submit | ✅ |
| Suggestion send | Tap suggestion chip | welcome hides, first row shows the suggestion's `Prompt` text | ✅ |
| Attachment removal | ✕ on staged chip | chip removed from `.mchat-attachments` on next render | ✅ |
| Sticky-bottom follow | Rapid 5× send text | list stays scrolled to bottom (measured via `body.scrollHeight - scrollTop - clientHeight < 96 px`) | ✅ |
| Sticky-scroll starting from empty | Clear + rapid 40× send | first message triggers auto-scroll despite `.mchat-chat-page__body` having no overflow at bind time; list stays anchored | ✅ |
| Sticky-scroll — no snap when user scrolled up | Fill, scroll to top, add one | `scrollTop` stays 0, list does NOT snap to bottom (verified `scrollDist` grew from 1067 → 1109 without `scrollTop` changing) | ✅ |
| Sticky-scroll — follow resumes at bottom | Scroll to bottom, add one | `scrollDist < 96 px` after add | ✅ |
| Dark theme | `data-theme="dark"` on `.mchat-root` | palette switches: dark surface, purple outgoing bubble, dark composer, readable text-on-dark | ✅ |
| Light theme | `data-theme="light"` | palette switches back to the light default | ✅ |
| ARIA on message list | | `.mchat-message-list[role="log"][aria-live="polite"]` | ✅ |
| ARIA on send button | | primary composer button has `aria-label="Send message"` | ✅ |
| **Single-mic — speech blocks audio button** | Composer 🗣 (start speech) | `button[aria-label="Stop live speech"]` visible + enabled AND `button[aria-label="Record audio"].disabled === true`. Trying `.click()` on the disabled audio button is a DOM no-op (verified) — mic stays with speech. | ✅ |
| **Single-mic — audio blocks speech button** | Composer 🎤 (start audio) | `.mchat-icon-btn--recording` (Stop recording) enabled AND `button[aria-label="Start live speech"].disabled === true`. | ✅ |
| **Single-mic — audio→speech preempt** | Composer 🎤 then 🗣 via `.click()` | `StartLiveSpeechAsync` awaits `EnsureAudioStoppedAsync` (cancels `_audioCts` + awaits `recorder.CancelAsync()`) before subscribing; final DOM shows only one active modality, `.mchat-attachment-chip` count did not change, `_activeRecorder` cleared. | ✅ |
| **Single-mic — speech→audio preempt** | Composer 🗣 then 🎤 via `.click()` | `StartAudioRecordingAsync` awaits `EnsureLiveSpeechStoppedAsync` (cancels `_speechCts` + unsubscribes + awaits `recognizer.StopAsync()`) before starting; final DOM shows only one active modality, recognizer handler count = 0. | ✅ |
| **Single-mic — chained toggles converge** | Rapid Stop speech + Start audio in one JS frame | Final state is single-owner-idle — no stuck `IsListening` / `IsRecordingAudio` / `IsAudioStarting` / `IsSpeechStarting`, no console errors. | ✅ |
| **Same-modality startup guard (audio)** | Triple-click `Record audio` in one JS frame | Only one active recording (`.mchat-icon-btn--recording` present, `Stop recording` visible + enabled, `Start live speech` disabled). No duplicate `recorder.StartAsync` invocation because `IsAudioStarting` disables the DOM button AND `ToggleAudioCaptureAsync` early-returns for programmatic bypass. | ✅ |
| **Same-modality startup guard (speech)** | Triple-click `Start live speech` in one JS frame | Only one active listener (`Stop live speech` visible + enabled, `Record audio` disabled). No duplicate `recognizer.StartAsync` invocation. | ✅ |
| **Audio transcribing window (transcribing)** | | While `recorder.StopAsync` is awaiting: `IsTranscribingAudio` true, `IsRecordingAudio` false, both toggle buttons disabled, `.mchat-composer__send` disabled, `CanPickAttachments` false. Late `StopAsync` result staged only if the audio operation identity still matches; a speech preemption during the window drops the stale attachment. | ✅ (unit test via `StopGate`) |
| **Speech stop-processing window (stopping)** | | While `recognizer.StopAsync` is awaiting: `IsSpeechStopping` true, `IsLiveSpeechEnabled`/`IsListening` still true until the finally clears them, both toggle buttons disabled, send disabled. Late `StopAsync` on a preempted operation clears nothing. | ✅ (unit test via `StopGate`) |
| **Speech preempts audio Stop — await cleanup** | Fires speech toggle while `recorder.StopAsync` gate is still pending | `recognizer.StartAsync` MUST NOT be called until `recorder.StopAsync` returns. Preemptor awaits `_audioCleanupTask` (the whole stop lifecycle including attachment staging) before touching the mic. Once resumed, attachment is staged, then recognizer starts. | ✅ (unit test) |
| **Audio preempts speech Stop — await cleanup** | Fires audio toggle while `recognizer.StopAsync` gate is still pending | `recorder.StartAsync` MUST NOT be called until `recognizer.StopAsync` returns. Preemptor awaits `_speechCleanupTask`. | ✅ (unit test) |
| **Multiple preemptors — single-flight cleanup** | Three audio toggles fire in one JS frame while speech is stopping | All await the SAME `_speechCleanupTask`. `recognizer.StopAsync` called exactly ONCE; subsequent audio toggles observe `IsAudioStarting` and early-return so `recorder.StartAsync` also fires exactly once. | ✅ (unit test) |




Reference invocations (adapt the agent port to whatever `maui devflow list` reports for
this sample):

```bash
export MCHAT_PORT=10224
maui devflow ui screenshot --agent-port $MCHAT_PORT --output empty.png --overwrite
maui devflow ui tap --automationId StreamParticipantTextButton --agent-port $MCHAT_PORT
maui devflow webview Runtime evaluate 'document.querySelectorAll(".mchat-row").length' \
  --agent-port $MCHAT_PORT
maui devflow webview Runtime evaluate 'document.querySelector(".mchat-typing").getAttribute("role")' \
  --agent-port $MCHAT_PORT
```

> This sample is experimental and may change before the neutral Blazor package stabilises.
