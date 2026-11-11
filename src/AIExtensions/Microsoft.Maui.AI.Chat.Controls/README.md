# Microsoft.Maui.AI.Chat.Controls

> **Experimental:** APIs may change between preview releases.

A drop-in .NET MAUI chat control for AI agents. `AIChatView` binds directly to a
[`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/) session
(`AgentContext`). It streams Markdown answers and renders tool calls, approvals, reasoning, and media. Its composer
handles attachments, audio notes, dictation, and live voice. You can change how any block renders with XAML
templates, restyle it with resource keys, or replace the whole layout.

## Features

- **Chat layout:** the user's messages sit in a right-aligned bubble sized to the text, with attached images and
  files as right-aligned thumbnails and chips above it, and assistant text is full width. There are no avatars,
  names, or timestamps. A thinking row shows until content arrives. The list follows new content while you are
  at the bottom, and again whenever a new turn starts.
- **Markdown answers:** headings, emphasis, lists and task lists, tables, block quotes, code, links, and images
  render as native views while the answer streams. Images from the web show as chips until you allow them
  (`AllowRemoteImages`). Your own messages stay plain text.
- **Agent content:** tool calls are compact cards with expandable arguments and results. An approval is one
  card: Approve and Reject, then the decision, with the arguments and result folded away like a tool call's.
  There are also client UI actions, collapsible reasoning (encrypted reasoning without text is not shown),
  inline images (data or links, with a link chip when an image cannot load), and file and link chips.
- **Turn states:** a stopped response keeps its partial output under a "Stopped" marker. A failed response
  keeps its partial output and shows the error with Retry; if another message is sent instead, the failed one
  is marked "Not sent". A restored conversation renders without sending.
- **Composer:** Send and Stop, suggestion chips, attachments (picker, file types, size and count limits),
  audio notes, dictation, and continuous live voice. Errors appear in one place.
- **Customization:** `BlockTemplate` matches by block type (including `[ToolBlock]` types), tool name, role,
  priority, or a `When` override. There are header, empty, messages-header, and response-footer templates,
  input slots, resource keys, and a replaceable `ControlTemplate`.
- **Light and dark themes** that your app's implicit styles don't change, accessible names on every action,
  and services resolved from dependency injection.

## Platform support

| | Android | iOS | Mac Catalyst | Windows |
|---|:-:|:-:|:-:|:-:|
| Chat view, templates, themes | ✅ | ✅ | ✅ | ✅ |
| Attachments (platform file picker) | ✅ | ✅ | ✅ | ✅ |
| Audio notes (built-in WAV recorder) | ✅ | ✅ | ✅ | ✅ |
| Live voice (built-in recognizer) | ✅¹ | ✅¹ | ✅¹ | ✅¹ |

¹ Implemented over the .NET MAUI Community Toolkit speech-to-text service; not yet verified on devices.

The package targets `net10.0` and works with any .NET MAUI backend. On other backends, register your own
`IChatAudioRecorder` and `IChatSpeechRecognizer`.

## Install

```xml
<PackageReference Include="Microsoft.Maui.AI.Chat.Controls" Version="0.1.0-preview.*" />
```

```csharp
builder
    .UseMauiApp<App>()
    .UseAIChat();

// Optional: a chat view without a Session or ChatClient uses the registered client.
builder.Services.AddChatClient(services => /* any Microsoft.Extensions.AI IChatClient */);
```

## Quick start

```xml
<ContentPage xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat">
    <chat:AIChatView ChatClient="{Binding ChatClient}"
                     Instructions="You are a helpful assistant."
                     WelcomeMessage="Ask me anything."
                     AllowAttachments="True" />
</ContentPage>
```

The one `chat` namespace covers the controls and the engine's block types (`{x:Type chat:ReasoningContentBlock}`).

From code:

```csharp
var chat = new AIChatView
{
    ChatClient = client,
    Instructions = "You are a helpful assistant.",
    Tools = [AIFunctionFactory.Create(GetWeather)],
};
```

When you set `ChatClient`, the view creates and owns the session. If the client has no
`FunctionInvokingChatClient`, the view adds one so `Tools` run. Changes to `Instructions` and `Tools` apply to
the next message. Replacing `ChatClient` starts a new conversation.

### Bring your own session

Set `Session` to use an `AgentContext` you configure, for example with generated tool blocks or persistence:

```csharp
var agent = new UIAgent(client.AsBuilder().UseFunctionInvocation().Build(), options =>
{
    options.ChatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather)] };
    options.AddGeneratedToolBlocks();
});
chat.Session = new AgentContext(agent);
```

`Session` wins over `ChatClient`, and the view never disposes a session it did not create. The session it
creates for `ChatClient` lasts until the view's handler is disconnected (for example when its page is popped),
which stops a response in progress.

To continue a saved conversation, restore it into the session, before or after you assign it: `RestoreAsync()`
loads the turns of the agent's conversation thread, and `RestoreAsync(messages)` loads recorded `ChatMessage`s.
Restored turns render right away, recorded tool calls show their recorded results and never run again, and the
next message continues the conversation:

```csharp
var session = new AgentContext(agent);
await session.RestoreAsync(savedMessages);
chat.Session = session;
```

`ClearAsync()` stops any response in progress and clears the conversation. `StopAsync()` completes once the
response has stopped.

## Customize blocks

Add `BlockTemplate`s as the view's content. A template matches when every filter you set matches:

```xml
<chat:AIChatView Session="{Binding Session}">
    <!-- A view for one tool, created through dependency injection when available. -->
    <chat:BlockTemplate ToolName="get_weather" ViewType="{x:Type views:WeatherCard}" />

    <!-- An inline template. The binding context is the block, refreshed as it streams. -->
    <chat:BlockTemplate BlockType="{x:Type chat:FunctionInvocationContentBlock}">
        <DataTemplate x:DataType="chat:FunctionInvocationContentBlock">
            <Label Text="{Binding ToolName, StringFormat='Used {0}'}" />
        </DataTemplate>
    </chat:BlockTemplate>

    <!-- A template without a view hides the blocks it matches. -->
    <chat:BlockTemplate BlockType="{x:Type chat:ReasoningContentBlock}" />
</chat:AIChatView>
```

- Your templates always win over the built-in ones. Among yours, the highest `Priority` wins, then the most
  specific: a tool name beats a block type, a derived type beats its base type, and a type beats a role. If
  they are still tied, the last one declared wins.
- `Role="user"` templates can take over the user's own blocks, which otherwise render as the built-in user message.
- Override `When(ContentBlock)` for anything else, and call the base method to keep the filters.
- Derive a view from `BlockView` to get `Block`, `Session`, and `OnBlockChanged()`. That method runs once per
  batch of streaming updates, not once per token.
- In a template for a `ToolApprovalBlock` or `UIActionBlock`, bind the buttons to `IsAwaitingInput`
  (`IsVisible="{Binding IsAwaitingInput}"`): it turns false once the block is answered, while the action runs,
  and when the turn stops, fails or is restored, after which answers are ignored.

Typed tool blocks from the engine's source generator work the same way:

```csharp
[ToolBlock("get_weather")]
public sealed partial class WeatherBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "city")]
    public string City { get; set; } = "";

    [ToolResult(Name = "temperature")]
    public double Temperature { get; set; }
}

public sealed class WeatherCard : BlockView
{
    private readonly Label _label = new();

    public WeatherCard() => Content = _label;

    protected override void OnBlockChanged() =>
        _label.Text = Block is WeatherBlock { HasResult: true } weather
            ? $"{weather.City}: {weather.Temperature}°"
            : "Checking the weather…";
}
```

```xml
<chat:BlockTemplate BlockType="{x:Type local:WeatherBlock}" ViewType="{x:Type local:WeatherCard}" />
```

Generated blocks need `options.AddGeneratedToolBlocks()` on the agent, so assign a `Session` you build yourself.

## Layout slots

| Property | Shows |
|---|---|
| `HeaderTemplate` | Content above the messages, such as a model picker |
| `EmptyViewTemplate` | Content before the first message. The default shows `WelcomeTitle` and `WelcomeMessage` |
| `Suggestions` | Prompt chips shown while the conversation is empty. Tapping one sends it |
| `MessagesHeaderTemplate` | Content at the top of the scrolling list, such as a disclaimer |
| `ResponseFooterTemplate` | Content after each completed response (not after a failed one). The binding context is the `ConversationTurn`, with its final `ModelId`, `Usage`, and `IsStopped` |
| `InputHeaderContent` | A view above the message box, such as a status banner. It hides with the message box |
| `InputLeadingContent`, `InputTrailingContent` | Extra views beside the message box |

App content in these slots inherits the view's `BindingContext`. Use `Placeholder`, `IsInputEnabled` (for
example, while a model loads), and `IsInputVisible` to shape the composer.

## Theme

The first `AIChatView` merges `AIChatTheme` into `Application.Resources` at index 0, so your own resources
win. Every visual uses `DynamicResource`, so changes apply at runtime. All keys are constants
on `AIChatThemeKeys`.

The view's own parts ignore your app's implicit styles (such as the `Button`, `Label`, and `Border` styles of the
default MAUI template), so the chat looks the same in any app: restyle them with these keys. Content you supply
(slots, block templates, a replaced `ControlTemplate`) takes your implicit styles as usual. Text uses the platform
font unless you set `AIChat.FontFamily`.

```xml
<Application.Resources>
    <!-- Your app's font for every text but code. -->
    <x:String x:Key="AIChat.FontFamily">OpenSansRegular</x:String>
    <!-- One color in both themes. -->
    <Color x:Key="AIChat.Color.Accent">#7C3AED</Color>
    <!-- Or per theme: AIChat.Light.* and AIChat.Dark.* switch with the app theme. -->
    <Color x:Key="AIChat.Dark.UserBubble">#3B2F5C</Color>
    <x:Double x:Key="AIChat.FontSize.Body">16</x:Double>
    <Style x:Key="AIChat.Style.UserBubble" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{DynamicResource AIChat.Color.UserBubble}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 8" />
        <Setter Property="Padding" Value="12,8" />
        <Setter Property="HorizontalOptions" Value="End" />
    </Style>
</Application.Resources>
```

To change the whole layout, set `ControlTemplate` (or define the `AIChat.Template.ChatView` resource). Bind
buttons to the view's commands (`SendCommand`, `StopCommand`, `PickAttachmentsCommand`,
`ToggleAudioCaptureCommand`, `ToggleLiveSpeechCommand`) and its state (`CanSend`, `CanStop`, `IsBusy`,
`ErrorMessage`, `HasError`, `Attachments`, `IsRecordingAudio`, `IsListening`, and others). The named parts
`PART_Header`, `PART_Messages`, `PART_EmptyView`, `PART_Suggestions`, `PART_Attachments`, and `PART_Input`
are optional. The default template is built from internal element types; when you start from a copy, use the
standard controls instead.

### Markdown

Assistant text renders as Markdown, parsed with [Markdig](https://github.com/xoofx/markdig). Rich text from a
provider (another `RichContentBlock`) is not parsed: the same renderer draws the provider's own nodes, so it looks
like an answer, and a block without nodes shows its text as written. While an answer streams, the views of what
changed update in place (the growing paragraph, list item, table row, or code block), so the text above it does not
flicker or jump. Raw HTML shows as text and is never rendered. Input far beyond any real answer is shown more
simply, so it cannot stall the chat: a paragraph or heading with more than 1,000 emphasis or link markers shows its
text unformatted, and a table with more than 64 columns or 2,000 cells shows as text, one row per line.

Markdown uses the chat's keys: `AIChat.Style.AssistantText` for paragraphs and list items,
`AIChat.Style.LinkSpan` for links, `AIChat.Style.CodeBlock` and `AIChat.Style.CodeText` for code blocks (the same
as in tool cards), and the `AIChat.Color.*` and `AIChat.FontFamily.Code` resources for the rest. Recoloring the
chat recolors Markdown in both themes. Text sizes come from `AIChat.FontSize.Body` and `AIChat.FontSize.Code`
(headings scale from the body size), not from the styles: a `FontSize` setter in `AIChat.Style.AssistantText`
doesn't resize an answer, so set those keys instead. These keys style what only Markdown has:

| Key | Type | Styles |
|---|---|---|
| `AIChat.Markdown.BlockSpacing` | `x:Double` | Space between blocks |
| `AIChat.Markdown.HeadingStyle` | `Label` style | Headings |
| `AIChat.Markdown.InlineCodeStyle` | `Span` style | Inline code |
| `AIChat.Markdown.CodeBlockLanguageStyle` | `Label` style | The language caption above a fenced code block |
| `AIChat.Markdown.BlockQuoteBarStyle` | `BoxView` style | The bar beside a block quote |
| `AIChat.Markdown.BlockQuoteTextStyle` | `Label` style | Block quote text |
| `AIChat.Markdown.ListMarkerStyle` | `Label` style | Bullets, numbers, and task boxes |
| `AIChat.Markdown.TableBorderStyle` | `Border` style | The table outline |
| `AIChat.Markdown.TableLineStyle` | `BoxView` style | Lines between rows and columns |
| `AIChat.Markdown.TableHeaderCellStyle` | `Label` style | Header cells |
| `AIChat.Markdown.TableCellStyle` | `Label` style | Body cells |
| `AIChat.Markdown.ThematicBreakStyle` | `BoxView` style | Horizontal rules |

Only `http`, `https`, and `mailto` links open; other links read as plain text.

Images from the web don't load unless you set `AllowRemoteImages="True"`. An answer is untrusted text: a web
page, document, or tool result that the model read can make it write an image link whose address carries parts of
the conversation, and loading that image would send them to the site. While `AllowRemoteImages` is off (the
default), an `http` or `https` image shows as a chip with its description and the site's host, and tapping the
chip opens the image in the browser. When it is on, images load inline, up to 480 × 320, within limits: a download
stops at 10 MB or after 30 seconds, and an image larger than 16 megapixels, or one whose size can't be read from
its header (PNG, JPEG, GIF, BMP, and WebP can), shows as the chip instead of being decoded. Changing the setting
updates the answers already shown. An image from any other source (such as an `attachment://` reference), or one
that fails to load, also shows as a chip. A link around an image, such as a badge, shows the image (or its chip,
naming the link's site) and follows the link when tapped. An image inside other text, such as in a heading or a
table cell, reads as its alt text, title, or address. `AllowRemoteImages` covers images in Markdown and in a provider's rich
text; images that tools and providers return as media (`MediaContentBlock`) are not affected.

## Attachments, audio, and voice

| Property | Behavior |
|---|---|
| `AllowAttachments`, `AttachmentFileTypes` | Show the attach button and filter the picker |
| `MaximumAttachmentBytes`, `MaximumAttachmentCount` | Per-file (10 MB) and per-message (10) limits. Audio recordings use the same per-file limit. All attachments of a message together can be up to 50 MB, or up to `MaximumAttachmentBytes` when that is larger |
| `AllowAudioCapture` | Record an audio note and attach it as WAV; a new recording replaces the previous one. With an `IChatAudioTranscriber`, the recording is dictated into the message box instead |
| `ShowInterimAudioTranscript` | Show words while recording, using the speech recognizer |
| `AllowLiveSpeech` | Continuous live voice. `LiveSpeechAutoSubmit` sends each finished utterance, and `ShowInterimSpeechText` and `SpeechRecognitionCulture` tune it |

Live voice ends an utterance when the speaker pauses. Android ends it itself; on iOS, Mac Catalyst, and Windows,
whose recognizers never end on their own, the built-in recognizer ends it after about two seconds without new
words (on Windows, only while the platform reports silence). Turning live voice off keeps what it heard so far
in the message box.

Attachments and recordings are sent as `DataContent` next to the message text. Recording and live voice never
share the microphone. Declare microphone access in your app:

- Android: `RECORD_AUDIO`
- iOS and Mac Catalyst: `NSMicrophoneUsageDescription` and `NSSpeechRecognitionUsageDescription`
- Windows: the `microphone` capability

## Services

| Interface | Default (`UseAIChat()`) | Used for |
|---|---|---|
| `IChatClient` | none | A view without `Session` or `ChatClient` |
| `IChatAttachmentPicker` | Platform file picker, read into memory | The attach button |
| `IChatAudioRecorder` | Mono WAV recorder (Plugin.Maui.Audio) | Audio notes and dictation |
| `IChatAudioTranscriber` | none | Dictation |
| `IChatSpeechRecognizer` | .NET MAUI Community Toolkit speech-to-text | Live voice and interim transcripts |

The view uses its matching property (`AttachmentPicker`, `AudioRecorder`, `AudioTranscriber`, or
`SpeechRecognizer`) first, then the registered service, then the built-in default. The default picker works
even without `UseAIChat()`.

## Requirements

- .NET 10 and .NET MAUI 10
- A `Microsoft.Extensions.AI` `IChatClient`
- [Markdig](https://www.nuget.org/packages/Markdig/), restored with the package

## Related packages

| Package | Purpose |
|---|---|
| [`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/) | The headless agent engine: turns, blocks, tools, approvals, and persistence |
| [`Microsoft.Maui.AI.Attributes`](https://www.nuget.org/packages/Microsoft.Maui.AI.Attributes/) | AOT-friendly source-generated `AIFunction` tools |
