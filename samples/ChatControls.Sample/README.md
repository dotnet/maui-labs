# MAUI Chat Controls sample

This sample demonstrates the provider-neutral `Microsoft.Maui.Chat` model rendered by the native
`Microsoft.Maui.Chat.Controls` `ChatView`. It does not use an AI model, agent framework, or chat
provider: one `ObservableChatConversation` drives the chat, and a participant simulator injects
messages and states.

## What it demonstrates

- Multiple participants, avatars, participant names, timestamps, and delivery states
- Text, image, file, recorded audio, and custom `MessageContent`
- A custom `GardenTaskContent` rendered by a XAML `GenericChatContentTemplate`
- A participant-simulator sidebar for text, image, file, task, sticker, multipart, grouped, and streamed messages
- Standard-bubble and bare-content presentation (task cards replace the bubble; stickers have no bubble)
- Real platform recording and speech-to-text, plus a deterministic simulated-microphone mode for DevFlow
- Staging, sending, and playing audio attachments alongside image/file attachments and the native picker
- Independent Priya/Diego typing states, conversation busy state, send failure, and delivery-state controls
- Interactive composer sends with asynchronous `Sending` → `Sent` → `Delivered` transitions and a
  slow-send mode that exercises the stop button
- Suggestions, file attachments, custom empty/header templates, and clear/reset actions
- Light, dark, and system themes
- `MauiChat.*` resource overrides in `App.xaml`

## Run

```bash
dotnet build samples/ChatControls.Sample/ChatControls.Sample.csproj \
  -f net10.0-maccatalyst

dotnet build samples/ChatControls.Sample/ChatControls.Sample.csproj \
  -f net10.0-android
```

The chat implementation references only the neutral chat packages; it has no AI dependency.
Start with `MainPage.xaml` for the control and `TeamChatViewModel.cs` for the provider-neutral
conversation API. Debug builds additionally reference the DevFlow agent so the
sample can be inspected and driven with `maui devflow`; Release builds omit that development-only
reference.

> This package and sample are experimental and may change before a stable release.
