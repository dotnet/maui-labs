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
- Avatars, display names, timestamps, bubble radius/stroke/width, and input colors.
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

Override `MauiAIChat.*` resources for colors and sizing, set the appearance bindable properties,
or replace the complete control template. This native model is the MAUI equivalent of separate
page/drawer/bubble shells: host the same control in the layout appropriate to your app instead of
adding web-specific shell abstractions.

## Platform support

The package is MAUI-native and supports platforms where `Microsoft.Maui.Controls` and the chosen
application features are available. The included Garden sample validates Android, iOS,
Mac Catalyst, and Windows project targets; CI validates macOS and Windows hosts.

## Related package

`Microsoft.Maui.AI.Chat` contains the headless engine and can be used without this controls
package.

