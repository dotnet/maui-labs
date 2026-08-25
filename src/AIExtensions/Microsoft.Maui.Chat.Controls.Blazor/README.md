# Microsoft.Maui.Chat.Controls.Blazor

Provider-neutral **Blazor Hybrid** chat controls for .NET MAUI: a virtualisation-friendly
message list, a multimodal composer, streaming, typing, empty / header / footer slots,
participant chrome, and a `MessageContentRenderer<T>` seam for custom message bodies.

> [!WARNING]
> **Experimental.** This package is part of [dotnet/maui-labs](https://github.com/dotnet/maui-labs)
> and ships as `0.1.0-preview`. APIs can change in any release.

There is no AI, transport, or provider dependency anywhere in this package. It shares the
same neutral conversation model as the native XAML sibling package
[`Microsoft.Maui.Chat.Controls`](../Microsoft.Maui.Chat.Controls/): everything below is
driven by a `ChatConversation` you own — from an in-memory `ObservableChatConversation`, a
websocket, a database, or an AI client.

## Features

- **Drop-in `<ChatView>` component** — shell, message list, composer, empty state,
  streaming indicator, error / retry banner, typing indicator.
- **Neutral conversation model** — reuses `ConversationMessage`, `MessageContent`,
  `TextMessageContent`, `MediaMessageContent`, `StructuredTextMessageContent<T>`,
  `ChatDraft`, `ChatAttachment`, `ChatParticipant`, and `ObservableChatConversation` from
  `Microsoft.Maui.Chat.Controls`. No second model to keep in sync.
- **`MessageContentRenderer<TContent>` seam** — consumer-supplied Razor fragments render
  custom `MessageContent` subclasses. Most-recently-registered renderer wins, so an AI
  bridge shipped later can slot in AI-only message bodies without changing the shell.
- **Reusable `IChatComposerContext`** — leading and trailing composer actions receive it
  as a `RenderFragment<IChatComposerContext>`. Text, attachments, send / stop, audio and
  live speech are all guarded by a single reentrancy-safe state machine.
- **Injectable services** — the composer looks up `IChatAttachmentPicker`,
  `IChatAudioRecorder`, `IChatSpeechRecognizer` (and `IChatAudioTranscriber`) from DI. When
  a service is missing the corresponding button is disabled instead of throwing.
- **Streaming-friendly** — content is mutated in place, so a growing response updates the
  existing DOM node instead of replacing it.
- **Grouped rows and participant chrome** — avatars, participant names, timestamps, and
  delivery status glyphs appear at the boundaries of a run from the same participant.
- **Accessible defaults** — `role="log"` on the message list, `role="status"` on the
  typing indicator, `role="alert"` on the error banner, `aria-label` on every button, and
  `aria-describedby` linking bubbles to their delivery-status footer.
- **Themable through CSS variables** — every colour, spacing, and radius flows through
  `--mchat-*` custom properties under a single `.mchat-root` scope. Automatic dark mode
  via `prefers-color-scheme` and an explicit `data-theme` opt-in.

## Install

```shell
dotnet add package Microsoft.Maui.Chat.Controls.Blazor
```

Register the initialiser in `MauiProgram.cs` and enable Blazor Hybrid the usual way:

```csharp
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.Chat.Controls.Blazor;

var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseChatControls()          // From Microsoft.Maui.Chat.Controls
    .AddChatControlsBlazor();   // From this package

builder.Services.AddMauiBlazorWebView();
```

Add the static assets to your `wwwroot/index.html`:

```html
<link rel="stylesheet" href="_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.css" />
```

## Quick start

Build a conversation and bind it to `<ChatView>`:

```csharp
var me = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
var assistant = new ChatParticipant("bot", "Assistant", ChatParticipantKind.Agent);

var conversation = new ObservableChatConversation(me);
conversation.Participants.Add(assistant);
conversation.SendHandler = async (chat, draft, cancellationToken) =>
{
    var outgoing = new ConversationMessage(me) { Status = ConversationMessageStatus.Sending };
    foreach (var content in draft.CreateContents())
        outgoing.Contents.Add(content);
    chat.AddMessage(outgoing);
    outgoing.Status = ConversationMessageStatus.Sent;

    chat.SetStatus(ChatConversationStatus.Busy);
    var reply = new TextMessageContent();
    chat.AddMessage(new ConversationMessage(assistant)).Contents.Add(reply);
    await foreach (var chunk in MyBackend.StreamAsync(draft.Text, cancellationToken))
        reply.Append(chunk);
    chat.SetStatus(ChatConversationStatus.Idle);
    return true;
};
```

```razor
@using Microsoft.Maui.Chat.Controls.Blazor

<ChatView Conversation="conversation"
          Placeholder="Ask me anything"
          WelcomeMessage="Say hi to the assistant" />
```

## Rendering custom content

Register a `MessageContentRenderer<T>` inside your `<ChatView>`:

```razor
<ChatView Conversation="conversation">
    <MessageContentRenderer TContent="PollContent" Context="poll">
        <div class="my-poll">@poll.Question</div>
    </MessageContentRenderer>
</ChatView>
```

Content whose `Presentation` is `Bubble` (default) renders inside the standard bubble;
`Bare` content drops the bubble but keeps the participant chrome — perfect for task cards,
stickers, and reactions. Content with no matching renderer produces a zero-height row so
an unrecognised content type never breaks a screen.

## Customising composer actions

The composer exposes `InputLeadingActions` and `InputTrailingActions` as
`RenderFragment<IChatComposerContext>` slots so a consumer (or the future AI bridge)
can add or replace actions without forking the shell:

```razor
<ChatView Conversation="conversation">
    <InputTrailingActions Context="composer">
        <button type="button"
                class="mchat-icon-btn mchat-icon-btn--primary"
                disabled="@(!composer.CanSubmit)"
                @onclick="composer.SubmitCallback">
            @if (composer.CanStop) { <span>■</span> }
            else                    { <span>➤</span> }
        </button>
    </InputTrailingActions>
</ChatView>
```

## Layer 2: the AI bridge

A separate `Microsoft.Maui.AI.Chat.Controls.Blazor` package will layer on top of this one:
it will translate an `AgentContext` from `Microsoft.Maui.AI.Chat` into an
`ObservableChatConversation`, register `MessageContentRenderer<>` fragments for AI-only
content types (reasoning, tool calls, tool approvals, image generation), and expose a
drop-in `<CopilotChatView>` that wraps this shell. Nothing in the shell changes.

## Requirements

- .NET 10 SDK
- .NET MAUI workload
- Microsoft.AspNetCore.Components.WebView.Maui in the host app

## Contributing

Build and test from the repository root:

```shell
dotnet build src/AIExtensions/Microsoft.Maui.Chat.Controls.Blazor/Microsoft.Maui.Chat.Controls.Blazor.csproj
dotnet test  tests/AIExtensions/Microsoft.Maui.Chat.Controls.Blazor.Tests/Microsoft.Maui.Chat.Controls.Blazor.Tests.csproj
```

Component and CSS provenance is documented in
[`Components/UPSTREAM-NOTES.md`](Components/UPSTREAM-NOTES.md).
