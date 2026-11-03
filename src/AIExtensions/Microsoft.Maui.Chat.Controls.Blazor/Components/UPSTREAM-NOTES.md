# Upstream provenance

`Microsoft.Maui.Chat.Controls.Blazor` reuses the visual shape and structural patterns of
the ASP.NET `Microsoft.AspNetCore.Components.AI` package pinned at commit
[`31b20463068f8d9ad900393bf96c9a182c397216`](https://github.com/dotnet/aspnetcore/tree/31b20463068f8d9ad900393bf96c9a182c397216/src/Components/AI/src).

The upstream is MIT-licensed. Every file adapted from it carries the MIT header and a
comment identifying the upstream source path. The upstream AI engine (blocks, turns,
`UIAgent`, `AgentContext`, block containers, conversation-turn renderers) is **not**
copied into this package: this layer is provider-neutral by design. Layer 2 — an AI
Blazor bridge shipped separately — is where AI-specific renderers and cascades live.

## What we borrowed

| Adapted file in this package | Upstream reference |
| --- | --- |
| `wwwroot/mchat.css` | `src/Components/AI/src/wwwroot/ai-chat.css` — CSS classes renamed from `sc-ai-*` to `mchat-*` to align with `Microsoft.Maui.Chat.Controls` `MauiChat.*` XAML resource keys. Design tokens follow the upstream palette, plus new neutral tokens for participant chrome (`--mchat-avatar-size`, outgoing/incoming bubble colours) not present in the upstream AI-only shell. |
| `Components/MessageContentRenderer.cs` | `src/Components/AI/src/Components/BlockRenderer.cs` — the same "registers a fragment matching a type" pattern, generalised to `MessageContent` and keyed by content type rather than upstream's `ContentBlock`. |
| `Components/ChatViewContext.cs` | `src/Components/AI/src/Components/MessageListContext.cs` + `Components/AgentBoundary.cs` — the registration store, most-recent-wins resolution, and region-keyed cascade lifetime rule. |
| `Components/ChatView.razor` structural layout | `src/Components/AI/src/Components/ChatPage.cs` — header/body/footer three-region shell. |
| `Components/ChatMessagesView.razor` structural layout | `src/Components/AI/src/Components/MessageList.cs` — scroll container + streaming/error footer pattern. |
| `Components/Composer.razor` textarea behaviour | `src/Components/AI/src/Components/MessageInput.cs` — Enter to submit / Shift+Enter for newline, disable during streaming, submit clears text via the .NET model rather than DOM manipulation. |

## What is new here

- `ChatMessageRowModel` and `ChatRowProjection` mirror the semantic parity of
  `Microsoft.Maui.Chat.Controls.ChatContentItem` (grouping flags, outgoing detection)
  without inheriting `BindableObject`. There is no upstream equivalent — upstream models
  turns, not per-content rows.
- `IChatComposerContext` (and its `ChatComposerContext` implementation) is a Blazor
  analogue of the native `Microsoft.Maui.Chat.Controls.ChatInputContext`. Same
  send-state machine, but expressed as `EventCallback`s so a Razor consumer can bind
  action fragments to it. No upstream analogue.
- Multimodal support (attachments, audio, live speech) integrates with the existing
  `Microsoft.Maui.Chat.Controls` service interfaces (`IChatAttachmentPicker`,
  `IChatAudioRecorder`, `IChatSpeechRecognizer`, `IChatAudioTranscriber`) rather than
  the upstream AI-only feature set.
- Participant chrome (avatar, participant name, timestamps, grouping,
  incoming/outgoing alignment, delivery status) is provider-neutral. Upstream renders
  role-tagged `user` and `assistant` bubbles only.

## Third-party notices

`THIRD-PARTY-NOTICES.txt` at the repo root has an entry pointing here, plus a copy of
the MIT license under which the upstream is distributed.
