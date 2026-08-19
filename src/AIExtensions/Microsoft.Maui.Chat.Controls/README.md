# Microsoft.Maui.Chat.Controls

Provider-neutral chat controls for .NET MAUI: a conversation model, a flat virtualized message list, a
content template system, and a drop-in chat surface with a composer.

> [!WARNING]
> **Experimental.** This package is part of [dotnet/maui-labs](https://github.com/dotnet/maui-labs) and
> ships as `0.1.0-preview`. APIs can change in any release.

There is no AI, transport, or provider dependency anywhere in this package. A conversation is just a
model you drive: from a websocket, a local database, an AI client, or a unit test.

## Features

- **Conversation model** — participants, messages, ordered per-message content, delivery status, and a
  single ordered change stream (`Subscribe`) that every mutation publishes automatically.
- **Streaming friendly** — content is mutated in place, so a growing response updates the existing row
  instead of replacing it.
- **Flat virtualized list** — one row per content item, never a nested list inside a cell.
- **Template system** — match content by type, participant kind, or direction; consumer templates always
  outrank the built-in ones, and unmatched content stays hidden rather than leaking a placeholder.
- **Replaceable templates** — the whole visual tree of both controls is a `ControlTemplate` with
  documented `PART_*` names, plus `MauiChat.*` resource keys for restyling.
- **Composer** — text, attachments, suggestions, reentrancy-guarded sending, and generic user-safe error
  strings.
- **Accessible defaults** — semantic descriptions on every bubble, automation IDs on every interactive
  part, and light and dark styling out of the box.

## Platform support

Targets `net10.0` with `UseMaui`, so it runs on every platform .NET MAUI supports.

| Platform | Supported |
| --- | --- |
| Android | ✅ |
| iOS / Mac Catalyst | ✅ |
| Windows (WinUI) | ✅ |
| Other MAUI backends | ✅ (cross-platform controls only) |

## Install

```shell
dotnet add package Microsoft.Maui.Chat.Controls
```

Optionally load the theme at startup — the controls also load it themselves when they enter a visual
tree, so this is only needed when your own resources build on the `MauiChat.*` keys:

```csharp
builder.UseChatControls();
```

## Quick start

Build a conversation, then bind it:

```csharp
var me = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
var assistant = new ChatParticipant("bot", "Assistant", ChatParticipantKind.Agent);

var conversation = new ObservableChatConversation(me);
conversation.Participants.Add(assistant);

// Route what the composer sends wherever you like.
conversation.SendHandler = async (chat, draft, cancellationToken) =>
{
    var outgoing = new ConversationMessage(me) { Status = ConversationMessageStatus.Sending };
    foreach (var content in draft.CreateContents())
        outgoing.Contents.Add(content);
    chat.AddMessage(outgoing);
    outgoing.Status = ConversationMessageStatus.Sent;

    // Stream a reply into a single content instance: the row updates in place.
    chat.SetStatus(ChatConversationStatus.Busy);
    var reply = new TextMessageContent();
    chat.AddMessage(new ConversationMessage(assistant)).Contents.Add(reply);

    await foreach (var chunk in MyBackend.StreamAsync(draft.Text, cancellationToken))
        reply.Append(chunk);

    chat.SetStatus(ChatConversationStatus.Idle);
    return true;
};
```

```xml
<ContentPage xmlns:chat="clr-namespace:Microsoft.Maui.Chat.Controls;assembly=Microsoft.Maui.Chat.Controls">
    <chat:ChatView Conversation="{Binding Conversation}"
                   AllowAttachments="True"
                   WelcomeMessage="Ask me anything" />
</ContentPage>
```

Only the message list, without header, composer, or welcome panel:

```xml
<chat:ChatMessagesView Conversation="{Binding Conversation}" />
```

The [Chat Controls sample](https://github.com/dotnet/maui-labs/tree/main/samples/ChatControls.Sample)
is a complete three-person example with text, media, delivery states, typing, attachments, and a
custom task-card content type. Its page and view model reference no AI APIs.

## Rendering custom content

Add a content type, a view for it, and a template that matches it:

```csharp
public sealed class PollContent : MessageContent
{
    public PollContent() => Presentation = ChatContentPresentation.Bare;

    public required string Question { get; init; }
}

public sealed class PollView : ChatContentView
{
    private readonly Label _label = new();

    public PollView() => Content = _label;

    protected override void RefreshContent()
    {
        _label.Text = (Item?.Content as PollContent)?.Question;
        SemanticProperties.SetDescription(this, _label.Text ?? string.Empty);
    }
}
```

`GenericChatContentTemplate` wraps custom body views in the standard message chrome by default, so
the poll keeps the participant avatar, name, direction, grouping, timestamp, and delivery status.
`MessageContent.Presentation` decides whether the body is inside the themed `Bubble` or rendered
`Bare` in that chrome (for cards, stickers, reactions, and similar content). A
`GenericChatContentTemplate.Presentation` value can override the content for one surface. Set
`UseMessageChrome="False"` only when a custom view intentionally owns the entire row. A custom view
that derives from `ChatBubbleView` already owns that chrome and is never wrapped twice.

```xml
<chat:ChatView Conversation="{Binding Conversation}">
    <chat:GenericChatContentTemplate ContentType="{x:Type local:PollContent}"
                                     ViewType="{x:Type local:PollView}" />
</chat:ChatView>
```

Content with no matching template renders as a hidden, zero-height row, so an unknown content type can
never break a screen. Set `UseDefaultContentTemplates="False"` to render *only* what you templated.

`StructuredTextMessageContent<TDocument>` carries a readable text fallback plus a provider-owned
structured document. A specialized package can render `TDocument`; an app that does not register that
renderer still gets the text through the built-in `ChatTextContentView`.

## Customising the look

Three levels, from cheapest to deepest:

1. **`Appearance`** — one `ChatAppearance` object drives avatars, participant names, timestamps, status,
   bubble radius, stroke, maximum width, and spacing. Its colour properties are `null` by default, which
   means "use the theme"; set one to override just that colour.
2. **Styles** — redefine any `MauiChat.*` style (see `ChatThemeKeys`) in application resources, or set
   `InputAreaStyle`, `InputEntryStyle`, `AttachButtonStyle`, and `SendButtonStyle` on one `ChatView`.
   The shared control template binds those style properties directly.
3. **Message-list template** — set `MessageListTemplate` to swap the `ChatMessagesView` subclass while
   keeping the complete shell and its styles.
4. **Control templates** — replace `MauiChat.ChatViewTemplate` or `MauiChat.ChatMessagesViewTemplate`
   wholesale. Keep the `PART_*` names for the sections you want to keep working.

`ChatView` parts: `PART_Header`, `PART_MessageList`, `PART_WelcomePanel`, `PART_WelcomeIcon`,
`PART_WelcomeMessage`, `PART_EmptyView`, `PART_BusyIndicator`, `PART_Suggestions`, `PART_Footer`,
`PART_TypingIndicator`, `PART_InputArea`, `PART_Attachments`, `PART_AttachButton`, `PART_InputEntry`,
`PART_SendButton`.
`ChatMessagesView` part: `PART_Messages`.

## Threading contract

Everything in this package — models, projections, and controls — is **single-thread affine and not
thread-safe**. Create and mutate a conversation on the UI thread. Subscribers are invoked synchronously,
in subscription order, on the mutating thread: there is no marshalling, no queueing, and no locking.

If work arrives on a background thread, marshal it yourself before touching the model:

```csharp
await MainThread.InvokeOnMainThreadAsync(() => reply.Append(chunk));
```

The one piece of batching is in `ChatMessagesView`: a 50 ms coalescer collapses a burst of streaming
updates into a single refresh and at most one pending scroll request. Structural changes are applied
immediately.

## Packages

| Package | Description |
| --- | --- |
| `Microsoft.Maui.Chat.Controls` | Conversation models, content templates, `ChatMessagesView`, and `ChatView` |

## Requirements

- .NET 10 SDK
- .NET MAUI workload

## Contributing

Build and test from the repository root:

```shell
dotnet build src/AIExtensions/Microsoft.Maui.Chat.Controls/Microsoft.Maui.Chat.Controls.csproj
dotnet test tests/AIExtensions/Microsoft.Maui.Chat.Controls.Tests/Microsoft.Maui.Chat.Controls.Tests.csproj
```

Layout:

```text
Microsoft.Maui.Chat.Controls/
├── Models/       # participants, messages, content, drafts, conversations, appearance
├── Templates/    # ChatContentItem projection, templates, and the selector
├── Views/        # ChatContentView base, the default bubble, and the built-in views
├── Controls/     # ChatMessagesView, ChatView, attachment picking
└── Themes/       # resource dictionaries, styles, and control templates
```

Public API changes must be reflected in `PublicAPI.Unshipped.txt`.
