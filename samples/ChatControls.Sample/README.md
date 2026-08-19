# MAUI Chat Controls sample

This sample demonstrates the provider-neutral `Microsoft.Maui.Chat.Controls` package. It does not use
an AI model, agent framework, or chat provider: an `ObservableChatConversation` is enough to drive the
same native chat shell used by AI applications.

## What it demonstrates

- A drop-in `ChatView` with a virtualized message list and composer
- Multiple participants, avatars, participant names, timestamps, and delivery states
- Text, image, file, and custom `MessageContent`
- A custom `GardenTaskContent` rendered by a XAML `GenericChatContentTemplate`
- A participant-simulator sidebar for text, image, file, task, sticker, multipart, grouped, and streamed messages
- Standard-bubble and bare-content presentation (task cards replace the bubble; stickers have no bubble)
- Deterministic staging and sending of outgoing image/file attachments, alongside the native picker
- Independent Priya/Diego typing states, conversation busy state, send failure, and delivery-state controls
- Interactive composer sends with asynchronous `Sending` → `Sent` → `Delivered` transitions
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

The chat implementation references only `Microsoft.Maui.Chat.Controls`; it has no AI dependency.
Start with `MainPage.xaml` for control composition and `TeamChatViewModel.cs` for the
provider-neutral conversation API. Debug builds additionally reference the DevFlow agent so the
sample can be inspected and driven with `maui devflow`; Release builds omit that development-only
reference.

> This package and sample are experimental and may change before a stable release.
