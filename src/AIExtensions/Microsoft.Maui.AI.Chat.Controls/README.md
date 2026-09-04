# Microsoft.Maui.AI.Chat.Controls

> **Experimental:** APIs may change between preview releases.

Native .NET MAUI controls for `Microsoft.Maui.AI.Chat`. `CopilotChatView` is a complete,
zero-configuration chat surface; `MessageListView` provides the virtualized message list without
input chrome. Both are fully customizable through XAML control/content templates and dynamic
resources.

## Install

```xml
<PackageReference Include="Microsoft.Maui.AI.Chat.Controls" Version="0.1.0-preview.*" />
```

Register the default resources:

```csharp
builder.UseMauiApp<App>()
       .UseChatControls();
```

## Quick start

```xml
<chat:CopilotChatView Session="{Binding Session}"
                      WelcomeMessage="How can I help?"
                      Placeholder="Type a message..." />
```

`Session` is an `AgentContext`. Built-in fallbacks render user/assistant text, approvals,
automatic UI actions, reasoning, media, thinking, and generic errors. Raw function calls and
unknown custom blocks stay hidden until explicitly templated.

`CopilotChatView` derives from the provider-neutral `Microsoft.Maui.Chat.Controls.ChatView`.
The AI package only adapts `AgentContext` blocks into neutral conversation messages and layers the
AI-specific template types on top, so the composer, attachments, suggestions, appearance, and
virtualized projection have one implementation.

## Architecture

| Layer | Responsibility | Dependencies |
|---|---|---|
| `Microsoft.Maui.AI.Chat` | Headless agent, turns, blocks, tools, approvals, state, persistence | No controls |
| `Microsoft.Maui.Chat.Controls` | Provider-neutral participants, `MessageContent`, conversations, projection, and native views | No AI |
| `Microsoft.Maui.AI.Chat.Controls` | Maps `ContentBlock` → neutral `ConversationMessage`/`MessageContent` and composes `AgentContext` with `ChatView` | Both layers |

The adapter maps common AI content to neutral content primitives. Blocks that need AI-specific
behavior are retained behind an internal `MessageContent` implementation so templates can inspect
tools, approvals, reasoning, and transient status without leaking controls types into the engine.
`ContentContext` is a specialized neutral `ChatContentItem`, not a second parallel row model.

### Canonical content pipeline

| Provider input | Headless block | Chat content | Default native body |
|---|---|---|---|
| `TextContent` / streamed text | `TextContentBlock` | `TextMessageContent` | `ChatTextContentView` |
| `DataContent` / generated image | `MediaContentBlock` | one `MediaMessageContent` per item | `ChatMediaContentView` or `ChatFileContentView` |
| provider rich snapshot | `RichContentBlock` | `StructuredTextMessageContent<IReadOnlyList<RichTextNode>>` | `RichTextView`, with neutral text fallback |
| tool, approval, reasoning, transient status | specialized AI block | internal block-backed `MessageContent` | AI-specific body inside neutral message chrome |

Blocks remain useful because they are processed, lifecycle-aware engine state. `MessageContent`
remains useful because it is the provider-neutral UI contract. The bridge converts once between
those boundaries; it does not define a second message shell, bubble, selector, or media/text view.

## Custom block views

```xml
<chat:CopilotChatView Session="{Binding Session}">
    <chat:CopilotChatView.ContentTemplates>
        <chat:GenericContentTemplate
            BlockType="{x:Type local:ProductResultsBlock}"
            ViewType="{x:Type local:ProductResultsView}" />
    </chat:CopilotChatView.ContentTemplates>
</chat:CopilotChatView>
```

Consumer templates always outrank built-in fallbacks. Set
`UseDefaultContentTemplates="False"` for strict allow-list rendering.

`ViewType` describes the **message body**, not a whole row. By default that body receives the same
provider-neutral avatar, participant name, direction, grouping, timestamp, status, and bubble/bare
presentation as every other message. Set `Presentation="Bubble"` or `"Bare"` to override the mapped
content, or `UseMessageChrome="False"` only when a view intentionally owns the complete row.

## Features

- Complete replaceable `ControlTemplate` with documented `PART_*` names.
- Priority-based block templates filtered by role, tool name, or block type.
- XAML views created through MAUI DI.
- Native `CollectionView` virtualization and 50 ms streaming refresh coalescing.
- Turn-aware `ContentContext` metadata (`Turn`, `TurnId`, request/first/last flags).
- Custom header, footer, and empty-state templates.
- Legacy string prompts and richer `ChatSuggestion` objects with icon/label/prompt/template.
- Optional native attachments with custom picker injection, MIME data, file names, and size limits.
- Attachment-only messages.
- Generic error UI with retry; diagnostic exceptions stay on `AgentContext.Error`.
- Collapsible visible reasoning and protected-reasoning disclosure.
- Avatars, display names, timestamps, bubble radius/stroke/width, and template-bound composer styles.
- Accessibility descriptions and automation IDs for core actions.

## Attachments

```xml
<chat:CopilotChatView Session="{Binding Session}"
                      AllowAttachments="True"
                      MaxAttachmentBytes="10485760" />
```

The default picker uses MAUI `FilePicker`. Set `AttachmentPicker` to an
`IChatAttachmentPicker` implementation to integrate your own native workflow. Selection errors
appear through `AttachmentError` rather than escaping the UI event handler.

## Suggestions

```csharp
chatView.Suggestions.Add(new ChatSuggestion(
    label: "Compare seeds",
    prompt: "Compare the tomato and pepper seeds")
{
    Icon = "🌱"
});
```

Use `SuggestionTemplate` for custom XAML. `SuggestionPrompts` remains available for simple
string-only compatibility.

## Customization

The outer shell is the same `MauiChat.ChatViewTemplate` used by the provider-neutral control.
Override `MauiChat.*` styles (or set `InputAreaStyle`, `InputEntryStyle`, `AttachButtonStyle`, and
`SendButtonStyle`) for shell chrome. Override `MauiAIChat.*` resources only for AI block views.
`CopilotChatView` swaps the message-list projection through `MessageListTemplate`; it does not carry
a duplicate full-control theme.

## Platform support

The package is MAUI-native and supports platforms where `Microsoft.Maui.Controls` and the chosen
application features are available. The Chat Controls and Garden samples cover the neutral and AI
layers across Android, iOS, Mac Catalyst, and Windows project targets; CI validates macOS and
Windows hosts.

## Related package

`Microsoft.Maui.AI.Chat` contains the headless engine and can be used without this controls
package.
