# Microsoft.Maui.AI.Chat.Controls

> **Experimental:** APIs may change between preview releases.

A drop-in .NET MAUI chat control for AI agents. `AIChatView` binds directly to a
[`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/) session
(`AgentContext`). It streams responses and renders tool calls, approvals, reasoning, and media. Its composer
handles attachments, audio notes, dictation, and live voice. You can change how any block renders with XAML
templates, restyle it with resource keys, or replace the whole layout.

## Features

- **Chat layout:** the user's messages sit in a right-aligned bubble and assistant text is full width. There
  are no avatars, names, or timestamps. A thinking row shows until content arrives, and the list
  auto-scrolls while you are at the bottom.
- **Agent content:** tool calls are compact cards with expandable arguments and results. Approvals have
  Approve and Reject buttons. There are also client UI actions, collapsible reasoning, inline images,
  file chips, and failed turns with Retry.
- **Composer:** Send and Stop, suggestion chips, attachments (picker, file types, size and count limits),
  audio notes, dictation, and continuous live voice. Errors appear in one place.
- **Customization:** `BlockTemplate` matches by block type (including `[ToolBlock]` types), tool name, role,
  priority, or a `When` override. There are header, empty, messages-header, and response-footer templates,
  input slots, resource keys, and a replaceable `ControlTemplate`.
- **Light and dark themes**, accessible names on every action, and services resolved from dependency
  injection.

## Platform support

| | Android | iOS | Mac Catalyst | Windows |
|---|:-:|:-:|:-:|:-:|
| Chat view, templates, themes | ✅ | ✅ | ✅ | ✅ |
| Attachments (platform file picker) | ✅ | ✅ | ✅ | ✅ |
| Audio notes (built-in WAV recorder) | ✅ | ✅ | ✅ | ✅ |
| Live voice (built-in recognizer) | ✅ | ✅ | ✅ | ✅ |

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

`Session` wins over `ChatClient`, and the view never disposes a session it did not create. Restore saved history
(`RestoreAsync`) before you assign the session.

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
- `Role="user"` templates can take over the user's own blocks, which otherwise render in the user bubble.
- Override `When(ContentBlock)` for anything else, and call the base method to keep the filters.
- Derive a view from `BlockView` to get `Block`, `Session`, and `OnBlockChanged()`. That method runs once per
  batch of streaming updates, not once per token.

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
| `ResponseFooterTemplate` | Content after each completed response. The binding context is the `ConversationTurn` |
| `InputLeadingContent`, `InputTrailingContent` | Extra views beside the message box |

App content in these slots inherits the view's `BindingContext`. Use `Placeholder`, `IsInputEnabled` (for
example, while a model loads), and `IsInputVisible` to shape the composer.

## Theme

The first `AIChatView` merges `AIChatTheme` into `Application.Resources` at index 0, so your own resources
win. Every visual uses `DynamicResource`, so changes apply at runtime. All keys are constants
on `AIChatThemeKeys`.

```xml
<Application.Resources>
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
are optional.

## Attachments, audio, and voice

| Property | Behavior |
|---|---|
| `AllowAttachments`, `AttachmentFileTypes` | Show the attach button and filter the picker |
| `MaximumAttachmentBytes`, `MaximumAttachmentCount` | Per-file (10 MB) and per-message (10) limits. Audio recordings use the same per-file limit |
| `AllowAudioCapture` | Record an audio note and attach it as WAV. With an `IChatAudioTranscriber`, the recording is dictated into the message box instead |
| `ShowInterimAudioTranscript` | Show words while recording, using the speech recognizer |
| `AllowLiveSpeech` | Continuous live voice. `LiveSpeechAutoSubmit` sends each finished utterance, and `ShowInterimSpeechText` and `SpeechRecognitionCulture` tune it |

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

## Related packages

| Package | Purpose |
|---|---|
| [`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/) | The headless agent engine: turns, blocks, tools, approvals, and persistence |
| [`Microsoft.Maui.AI.Attributes`](https://www.nuget.org/packages/Microsoft.Maui.AI.Attributes/) | AOT-friendly source-generated `AIFunction` tools |
