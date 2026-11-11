using System.Globalization;
using System.Windows.Input;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A drop-in chat view for an AI agent session: streaming responses, tool calls and approvals, reasoning,
/// media, attachments, audio notes, dictation, and live voice.
/// </summary>
/// <remarks>
/// <para>
/// Give the view a <see cref="ChatClient"/> (it creates and owns the session) or an existing
/// <see cref="Session"/>. With neither, it resolves <see cref="IChatClient"/> from the app's services.
/// </para>
/// <para>
/// Customize how blocks render with <see cref="BlockTemplate"/>s in <see cref="ContentTemplates"/> (the content
/// property, so templates can be written directly inside the element), restyle it through the resource keys in
/// <see cref="AIChatThemeKeys"/>, or replace the whole layout with a <see cref="ControlTemplate"/>.
/// </para>
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public partial class AIChatView : TemplatedView
{
    /// <summary>The XAML namespace for the chat controls and the engine's block types.</summary>
    public const string XmlNamespace = "http://schemas.microsoft.com/dotnet/2026/maui/ai/chat";

    internal const string HeaderPartName = "PART_Header";
    internal const string MessagesPartName = "PART_Messages";
    internal const string EmptyViewPartName = "PART_EmptyView";
    internal const string SuggestionsPartName = "PART_Suggestions";
    internal const string AttachmentsPartName = "PART_Attachments";
    internal const string InputPartName = "PART_Input";

    // ── Session ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateEffectiveSession(recreateOwned: false));

    /// <summary>Backing property for <see cref="ChatClient"/>.</summary>
    public static readonly BindableProperty ChatClientProperty =
        BindableProperty.Create(
            nameof(ChatClient),
            typeof(IChatClient),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateEffectiveSession(recreateOwned: true));

    /// <summary>Backing property for <see cref="Instructions"/>.</summary>
    public static readonly BindableProperty InstructionsProperty =
        BindableProperty.Create(
            nameof(Instructions),
            typeof(string),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateOwnedOptions());

    /// <summary>Backing property for <see cref="Tools"/>.</summary>
    public static readonly BindableProperty ToolsProperty =
        BindableProperty.Create(
            nameof(Tools),
            typeof(IList<AITool>),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateOwnedOptions());

    // ── Content ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Backing property for <see cref="ContentTemplates"/>.</summary>
    public static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<BlockTemplate>),
            typeof(AIChatView),
            defaultValueCreator: static _ => new System.Collections.ObjectModel.ObservableCollection<BlockTemplate>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((AIChatView)bindable).OnContentTemplatesChanged(oldValue as IList<BlockTemplate>, newValue as IList<BlockTemplate>));

    /// <summary>Backing property for <see cref="EmptyViewTemplate"/>.</summary>
    public static readonly BindableProperty EmptyViewTemplateProperty =
        BindableProperty.Create(
            nameof(EmptyViewTemplate),
            typeof(DataTemplate),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).ApplyEmptyView());

    /// <summary>Backing property for <see cref="HeaderTemplate"/>.</summary>
    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(
            nameof(HeaderTemplate),
            typeof(DataTemplate),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).ApplyHeader());

    /// <summary>Backing property for <see cref="MessagesHeaderTemplate"/>.</summary>
    public static readonly BindableProperty MessagesHeaderTemplateProperty =
        BindableProperty.Create(
            nameof(MessagesHeaderTemplate),
            typeof(DataTemplate),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).ApplyMessagesHeader());

    /// <summary>Backing property for <see cref="ResponseFooterTemplate"/>.</summary>
    public static readonly BindableProperty ResponseFooterTemplateProperty =
        BindableProperty.Create(
            nameof(ResponseFooterTemplate),
            typeof(DataTemplate),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable)._messageList.InvalidateTemplates());

    /// <summary>Backing property for <see cref="Suggestions"/>.</summary>
    public static readonly BindableProperty SuggestionsProperty =
        BindableProperty.Create(
            nameof(Suggestions),
            typeof(IList<string>),
            typeof(AIChatView),
            defaultValueCreator: static _ => new System.Collections.ObjectModel.ObservableCollection<string>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((AIChatView)bindable).OnSuggestionsChanged(oldValue as IList<string>, newValue as IList<string>));

    /// <summary>Backing property for <see cref="WelcomeTitle"/>.</summary>
    public static readonly BindableProperty WelcomeTitleProperty =
        BindableProperty.Create(nameof(WelcomeTitle), typeof(string), typeof(AIChatView), "How can I help you today?");

    /// <summary>Backing property for <see cref="WelcomeMessage"/>.</summary>
    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(AIChatView));

    // ── Input ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Backing property for <see cref="Text"/>.</summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(AIChatView),
            string.Empty,
            BindingMode.TwoWay,
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateState());

    /// <summary>Backing property for <see cref="Placeholder"/>.</summary>
    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(AIChatView), "Message");

    /// <summary>Backing property for <see cref="IsInputEnabled"/>.</summary>
    public static readonly BindableProperty IsInputEnabledProperty =
        BindableProperty.Create(
            nameof(IsInputEnabled),
            typeof(bool),
            typeof(AIChatView),
            true,
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateState());

    /// <summary>Backing property for <see cref="IsInputVisible"/>.</summary>
    public static readonly BindableProperty IsInputVisibleProperty =
        BindableProperty.Create(nameof(IsInputVisible), typeof(bool), typeof(AIChatView), true);

    /// <summary>Backing property for <see cref="InputHeaderContent"/>.</summary>
    public static readonly BindableProperty InputHeaderContentProperty =
        BindableProperty.Create(nameof(InputHeaderContent), typeof(View), typeof(AIChatView));

    /// <summary>Backing property for <see cref="InputLeadingContent"/>.</summary>
    public static readonly BindableProperty InputLeadingContentProperty =
        BindableProperty.Create(nameof(InputLeadingContent), typeof(View), typeof(AIChatView));

    /// <summary>Backing property for <see cref="InputTrailingContent"/>.</summary>
    public static readonly BindableProperty InputTrailingContentProperty =
        BindableProperty.Create(nameof(InputTrailingContent), typeof(View), typeof(AIChatView));

    /// <summary>Backing property for <see cref="AllowAttachments"/>.</summary>
    public static readonly BindableProperty AllowAttachmentsProperty =
        BindableProperty.Create(
            nameof(AllowAttachments),
            typeof(bool),
            typeof(AIChatView),
            false,
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).UpdateState());

    /// <summary>Backing property for <see cref="AttachmentPicker"/>.</summary>
    public static readonly BindableProperty AttachmentPickerProperty =
        BindableProperty.Create(nameof(AttachmentPicker), typeof(IChatAttachmentPicker), typeof(AIChatView));

    /// <summary>Backing property for <see cref="AttachmentFileTypes"/>.</summary>
    public static readonly BindableProperty AttachmentFileTypesProperty =
        BindableProperty.Create(nameof(AttachmentFileTypes), typeof(FilePickerFileType), typeof(AIChatView));

    /// <summary>Backing property for <see cref="MaximumAttachmentBytes"/>.</summary>
    public static readonly BindableProperty MaximumAttachmentBytesProperty =
        BindableProperty.Create(
            nameof(MaximumAttachmentBytes),
            typeof(long),
            typeof(AIChatView),
            10L * 1024 * 1024,
            validateValue: static (_, value) => (long)value > 0);

    /// <summary>Backing property for <see cref="MaximumAttachmentCount"/>.</summary>
    public static readonly BindableProperty MaximumAttachmentCountProperty =
        BindableProperty.Create(
            nameof(MaximumAttachmentCount),
            typeof(int),
            typeof(AIChatView),
            10,
            validateValue: static (_, value) => (int)value > 0);

    /// <summary>Backing property for <see cref="AllowAudioCapture"/>.</summary>
    public static readonly BindableProperty AllowAudioCaptureProperty =
        BindableProperty.Create(
            nameof(AllowAudioCapture),
            typeof(bool),
            typeof(AIChatView),
            false,
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).OnCaptureOptionChanged());

    /// <summary>Backing property for <see cref="AudioRecorder"/>.</summary>
    public static readonly BindableProperty AudioRecorderProperty =
        BindableProperty.Create(
            nameof(AudioRecorder),
            typeof(IChatAudioRecorder),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).OnCaptureServiceChanged());

    /// <summary>Backing property for <see cref="AudioTranscriber"/>.</summary>
    public static readonly BindableProperty AudioTranscriberProperty =
        BindableProperty.Create(
            nameof(AudioTranscriber),
            typeof(IChatAudioTranscriber),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).OnCaptureServiceChanged());

    /// <summary>Backing property for <see cref="AllowLiveSpeech"/>.</summary>
    public static readonly BindableProperty AllowLiveSpeechProperty =
        BindableProperty.Create(
            nameof(AllowLiveSpeech),
            typeof(bool),
            typeof(AIChatView),
            false,
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).OnCaptureOptionChanged());

    /// <summary>Backing property for <see cref="SpeechRecognizer"/>.</summary>
    public static readonly BindableProperty SpeechRecognizerProperty =
        BindableProperty.Create(
            nameof(SpeechRecognizer),
            typeof(IChatSpeechRecognizer),
            typeof(AIChatView),
            propertyChanged: static (bindable, _, _) => ((AIChatView)bindable).OnCaptureServiceChanged());

    /// <summary>Backing property for <see cref="SpeechRecognitionCulture"/>.</summary>
    public static readonly BindableProperty SpeechRecognitionCultureProperty =
        BindableProperty.Create(
            nameof(SpeechRecognitionCulture),
            typeof(CultureInfo),
            typeof(AIChatView),
            defaultValueCreator: static _ => CultureInfo.CurrentCulture);

    /// <summary>Backing property for <see cref="LiveSpeechAutoSubmit"/>.</summary>
    public static readonly BindableProperty LiveSpeechAutoSubmitProperty =
        BindableProperty.Create(nameof(LiveSpeechAutoSubmit), typeof(bool), typeof(AIChatView), true);

    /// <summary>Backing property for <see cref="ShowInterimSpeechText"/>.</summary>
    public static readonly BindableProperty ShowInterimSpeechTextProperty =
        BindableProperty.Create(nameof(ShowInterimSpeechText), typeof(bool), typeof(AIChatView), true);

    /// <summary>Backing property for <see cref="ShowInterimAudioTranscript"/>.</summary>
    public static readonly BindableProperty ShowInterimAudioTranscriptProperty =
        BindableProperty.Create(nameof(ShowInterimAudioTranscript), typeof(bool), typeof(AIChatView), false);

    // ── Read-only state ─────────────────────────────────────────────────────────────────────────────────

    private static readonly BindablePropertyKey IsBusyPropertyKey = CreateReadOnly(nameof(IsBusy), false);
    private static readonly BindablePropertyKey IsEmptyPropertyKey = CreateReadOnly(nameof(IsEmpty), true);
    private static readonly BindablePropertyKey CanSendPropertyKey = CreateReadOnly(nameof(CanSend), false);
    private static readonly BindablePropertyKey CanStopPropertyKey = CreateReadOnly(nameof(CanStop), false);
    private static readonly BindablePropertyKey HasErrorPropertyKey = CreateReadOnly(nameof(HasError), false);
    private static readonly BindablePropertyKey HasAttachmentsPropertyKey = CreateReadOnly(nameof(HasAttachments), false);
    private static readonly BindablePropertyKey ShowSuggestionsPropertyKey = CreateReadOnly(nameof(ShowSuggestions), false);
    private static readonly BindablePropertyKey IsRecordingAudioPropertyKey = CreateReadOnly(nameof(IsRecordingAudio), false);
    private static readonly BindablePropertyKey IsTranscribingAudioPropertyKey = CreateReadOnly(nameof(IsTranscribingAudio), false);
    private static readonly BindablePropertyKey IsListeningPropertyKey = CreateReadOnly(nameof(IsListening), false);
    private static readonly BindablePropertyKey CanCaptureAudioPropertyKey = CreateReadOnly(nameof(CanCaptureAudio), false);
    private static readonly BindablePropertyKey CanUseLiveSpeechPropertyKey = CreateReadOnly(nameof(CanUseLiveSpeech), false);

    private static readonly BindablePropertyKey ErrorMessagePropertyKey =
        BindableProperty.CreateReadOnly(nameof(ErrorMessage), typeof(string), typeof(AIChatView), null);

    private static readonly BindablePropertyKey AttachmentsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Attachments),
            typeof(IReadOnlyList<DataContent>),
            typeof(AIChatView),
            null,
            defaultValueCreator: static _ => Array.Empty<DataContent>());

    /// <summary>Backing property for <see cref="IsBusy"/>.</summary>
    public static readonly BindableProperty IsBusyProperty = IsBusyPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="IsEmpty"/>.</summary>
    public static readonly BindableProperty IsEmptyProperty = IsEmptyPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="CanSend"/>.</summary>
    public static readonly BindableProperty CanSendProperty = CanSendPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="CanStop"/>.</summary>
    public static readonly BindableProperty CanStopProperty = CanStopPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="ErrorMessage"/>.</summary>
    public static readonly BindableProperty ErrorMessageProperty = ErrorMessagePropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="HasError"/>.</summary>
    public static readonly BindableProperty HasErrorProperty = HasErrorPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="Attachments"/>.</summary>
    public static readonly BindableProperty AttachmentsProperty = AttachmentsPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="HasAttachments"/>.</summary>
    public static readonly BindableProperty HasAttachmentsProperty = HasAttachmentsPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="ShowSuggestions"/>.</summary>
    public static readonly BindableProperty ShowSuggestionsProperty = ShowSuggestionsPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="IsRecordingAudio"/>.</summary>
    public static readonly BindableProperty IsRecordingAudioProperty = IsRecordingAudioPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="IsTranscribingAudio"/>.</summary>
    public static readonly BindableProperty IsTranscribingAudioProperty = IsTranscribingAudioPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="IsListening"/>.</summary>
    public static readonly BindableProperty IsListeningProperty = IsListeningPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="CanCaptureAudio"/>.</summary>
    public static readonly BindableProperty CanCaptureAudioProperty = CanCaptureAudioPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="CanUseLiveSpeech"/>.</summary>
    public static readonly BindableProperty CanUseLiveSpeechProperty = CanUseLiveSpeechPropertyKey.BindableProperty;

    /// <summary>
    /// Gets or sets the agent session to show. When set, it takes precedence over <see cref="ChatClient"/>.
    /// The view never disposes a session it did not create.
    /// </summary>
    /// <remarks>
    /// Restore saved history before assigning the session — from its thread with
    /// <see cref="AgentContext.RestoreAsync(CancellationToken)"/>, or from recorded messages with
    /// <see cref="AgentContext.RestoreAsync(IEnumerable{ChatMessage}, CancellationToken)"/>. The engine does not
    /// announce restored turns, so turns restored into a session that is already shown appear only with its next
    /// change.
    /// </remarks>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets or sets a chat client for which the view creates and owns a session when <see cref="Session"/> is
    /// <see langword="null"/>. The client is wrapped with <c>UseFunctionInvocation()</c> unless it already
    /// invokes functions, so <see cref="Tools"/> run automatically.
    /// </summary>
    public IChatClient? ChatClient
    {
        get => (IChatClient?)GetValue(ChatClientProperty);
        set => SetValue(ChatClientProperty, value);
    }

    /// <summary>Gets or sets system instructions for the session the view owns.</summary>
    public string? Instructions
    {
        get => (string?)GetValue(InstructionsProperty);
        set => SetValue(InstructionsProperty, value);
    }

    /// <summary>Gets or sets the tools offered to the model by the session the view owns.</summary>
    public IList<AITool>? Tools
    {
        get => (IList<AITool>?)GetValue(ToolsProperty);
        set => SetValue(ToolsProperty, value);
    }

    /// <summary>Gets or sets the templates that render content blocks. App templates win over the built-ins.</summary>
    public IList<BlockTemplate> ContentTemplates
    {
        get => (IList<BlockTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets what is shown before the first message, replacing the default welcome.</summary>
    public DataTemplate? EmptyViewTemplate
    {
        get => (DataTemplate?)GetValue(EmptyViewTemplateProperty);
        set => SetValue(EmptyViewTemplateProperty, value);
    }

    /// <summary>Gets or sets a header above the messages, for example a model picker.</summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets content shown at the top of the scrolling message list, such as a disclaimer or the
    /// system instructions.
    /// </summary>
    public DataTemplate? MessagesHeaderTemplate
    {
        get => (DataTemplate?)GetValue(MessagesHeaderTemplateProperty);
        set => SetValue(MessagesHeaderTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets content shown after each completed response, with the <see cref="ConversationTurn"/> as its
    /// binding context — for example the model id, token usage, or copy and feedback actions.
    /// </summary>
    public DataTemplate? ResponseFooterTemplate
    {
        get => (DataTemplate?)GetValue(ResponseFooterTemplateProperty);
        set => SetValue(ResponseFooterTemplateProperty, value);
    }

    /// <summary>Gets or sets prompts offered as chips before the first message. Tapping one sends it.</summary>
    public IList<string> Suggestions
    {
        get => (IList<string>)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Gets or sets the title of the default welcome shown before the first message.</summary>
    public string? WelcomeTitle
    {
        get => (string?)GetValue(WelcomeTitleProperty);
        set => SetValue(WelcomeTitleProperty, value);
    }

    /// <summary>Gets or sets the message under <see cref="WelcomeTitle"/>.</summary>
    public string? WelcomeMessage
    {
        get => (string?)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    /// <summary>Gets or sets the text in the message box. Binds two-way by default.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the message box placeholder.</summary>
    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Gets or sets whether the user can type and send, for example while a model is still loading.</summary>
    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>Gets or sets whether the message box is shown.</summary>
    public bool IsInputVisible
    {
        get => (bool)GetValue(IsInputVisibleProperty);
        set => SetValue(IsInputVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets content shown above the message box, such as a status banner. It is hidden with the
    /// message box (<see cref="IsInputVisible"/>) and takes no space while it is <see langword="null"/> or hidden.
    /// </summary>
    public View? InputHeaderContent
    {
        get => (View?)GetValue(InputHeaderContentProperty);
        set => SetValue(InputHeaderContentProperty, value);
    }

    /// <summary>Gets or sets extra content before the message box, such as more buttons.</summary>
    public View? InputLeadingContent
    {
        get => (View?)GetValue(InputLeadingContentProperty);
        set => SetValue(InputLeadingContentProperty, value);
    }

    /// <summary>Gets or sets extra content after the message box, before the send button.</summary>
    public View? InputTrailingContent
    {
        get => (View?)GetValue(InputTrailingContentProperty);
        set => SetValue(InputTrailingContentProperty, value);
    }

    /// <summary>Gets or sets whether the attach button is shown.</summary>
    public bool AllowAttachments
    {
        get => (bool)GetValue(AllowAttachmentsProperty);
        set => SetValue(AllowAttachmentsProperty, value);
    }

    /// <summary>
    /// Gets or sets the attachment picker. When <see langword="null"/>, the registered
    /// <see cref="IChatAttachmentPicker"/> or the platform file picker is used.
    /// </summary>
    public IChatAttachmentPicker? AttachmentPicker
    {
        get => (IChatAttachmentPicker?)GetValue(AttachmentPickerProperty);
        set => SetValue(AttachmentPickerProperty, value);
    }

    /// <summary>Gets or sets the file types offered by the picker, or <see langword="null"/> for any file.</summary>
    public FilePickerFileType? AttachmentFileTypes
    {
        get => (FilePickerFileType?)GetValue(AttachmentFileTypesProperty);
        set => SetValue(AttachmentFileTypesProperty, value);
    }

    /// <summary>
    /// Gets or sets the largest attachment or audio recording, in bytes. Defaults to 10 MB; values below 1 are ignored.
    /// </summary>
    public long MaximumAttachmentBytes
    {
        get => (long)GetValue(MaximumAttachmentBytesProperty);
        set => SetValue(MaximumAttachmentBytesProperty, value);
    }

    /// <summary>Gets or sets how many attachments one message can carry. Defaults to 10; values below 1 are ignored.</summary>
    public int MaximumAttachmentCount
    {
        get => (int)GetValue(MaximumAttachmentCountProperty);
        set => SetValue(MaximumAttachmentCountProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the audio button is shown. It records an audio note, or dictates into the message
    /// box when <see cref="AudioTranscriber"/> is set.
    /// </summary>
    public bool AllowAudioCapture
    {
        get => (bool)GetValue(AllowAudioCaptureProperty);
        set => SetValue(AllowAudioCaptureProperty, value);
    }

    /// <summary>
    /// Gets or sets the audio recorder. When <see langword="null"/>, the registered
    /// <see cref="IChatAudioRecorder"/> or the built-in WAV recorder is used.
    /// </summary>
    public IChatAudioRecorder? AudioRecorder
    {
        get => (IChatAudioRecorder?)GetValue(AudioRecorderProperty);
        set => SetValue(AudioRecorderProperty, value);
    }

    /// <summary>
    /// Gets or sets a transcriber that turns recordings into text (dictation). When <see langword="null"/>, the
    /// registered <see cref="IChatAudioTranscriber"/> is used; without one, recordings are attached as audio.
    /// </summary>
    public IChatAudioTranscriber? AudioTranscriber
    {
        get => (IChatAudioTranscriber?)GetValue(AudioTranscriberProperty);
        set => SetValue(AudioTranscriberProperty, value);
    }

    /// <summary>Gets or sets whether the live voice button is shown.</summary>
    public bool AllowLiveSpeech
    {
        get => (bool)GetValue(AllowLiveSpeechProperty);
        set => SetValue(AllowLiveSpeechProperty, value);
    }

    /// <summary>
    /// Gets or sets the speech recognizer for live voice. When <see langword="null"/>, the registered
    /// <see cref="IChatSpeechRecognizer"/> or the built-in recognizer is used.
    /// </summary>
    public IChatSpeechRecognizer? SpeechRecognizer
    {
        get => (IChatSpeechRecognizer?)GetValue(SpeechRecognizerProperty);
        set => SetValue(SpeechRecognizerProperty, value);
    }

    /// <summary>Gets or sets the language recognized by live voice. Defaults to the current culture.</summary>
    public CultureInfo SpeechRecognitionCulture
    {
        get => (CultureInfo)GetValue(SpeechRecognitionCultureProperty);
        set => SetValue(SpeechRecognitionCultureProperty, value);
    }

    /// <summary>Gets or sets whether live voice sends each finished utterance. Defaults to <see langword="true"/>.</summary>
    public bool LiveSpeechAutoSubmit
    {
        get => (bool)GetValue(LiveSpeechAutoSubmitProperty);
        set => SetValue(LiveSpeechAutoSubmitProperty, value);
    }

    /// <summary>Gets or sets whether live voice shows words as they are recognized. Defaults to <see langword="true"/>.</summary>
    public bool ShowInterimSpeechText
    {
        get => (bool)GetValue(ShowInterimSpeechTextProperty);
        set => SetValue(ShowInterimSpeechTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether recording also shows a live transcript from <see cref="SpeechRecognizer"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ShowInterimAudioTranscript
    {
        get => (bool)GetValue(ShowInterimAudioTranscriptProperty);
        set => SetValue(ShowInterimAudioTranscriptProperty, value);
    }

    /// <summary>Gets whether the assistant is responding or waiting for an approval.</summary>
    public bool IsBusy => (bool)GetValue(IsBusyProperty);

    /// <summary>Gets whether the conversation has no messages yet.</summary>
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

    /// <summary>Gets whether the current draft can be sent.</summary>
    public bool CanSend => (bool)GetValue(CanSendProperty);

    /// <summary>Gets whether a response can be stopped.</summary>
    public bool CanStop => (bool)GetValue(CanStopProperty);

    /// <summary>Gets the message box error (a failed send, attachment, or recording), if any.</summary>
    public string? ErrorMessage => (string?)GetValue(ErrorMessageProperty);

    /// <summary>Gets whether <see cref="ErrorMessage"/> is set.</summary>
    public bool HasError => (bool)GetValue(HasErrorProperty);

    /// <summary>Gets the attachments staged for the next message.</summary>
    public IReadOnlyList<DataContent> Attachments => (IReadOnlyList<DataContent>)GetValue(AttachmentsProperty);

    /// <summary>Gets whether any attachments are staged.</summary>
    public bool HasAttachments => (bool)GetValue(HasAttachmentsProperty);

    /// <summary>Gets whether the suggestion chips are shown (before the first message).</summary>
    public bool ShowSuggestions => (bool)GetValue(ShowSuggestionsProperty);

    /// <summary>Gets whether an audio recording is in progress.</summary>
    public bool IsRecordingAudio => (bool)GetValue(IsRecordingAudioProperty);

    /// <summary>Gets whether a recording is being processed or transcribed.</summary>
    public bool IsTranscribingAudio => (bool)GetValue(IsTranscribingAudioProperty);

    /// <summary>Gets whether live voice is on.</summary>
    public bool IsListening => (bool)GetValue(IsListeningProperty);

    /// <summary>Gets whether the audio button is available (allowed and supported on this device).</summary>
    public bool CanCaptureAudio => (bool)GetValue(CanCaptureAudioProperty);

    /// <summary>Gets whether the live voice button is available (allowed and supported on this device).</summary>
    public bool CanUseLiveSpeech => (bool)GetValue(CanUseLiveSpeechProperty);

    /// <summary>Gets a command that sends the draft.</summary>
    public ICommand SendCommand { get; }

    /// <summary>Gets a command that stops the response in progress.</summary>
    public ICommand StopCommand { get; }

    /// <summary>Gets a command that shows the attachment picker.</summary>
    public ICommand PickAttachmentsCommand { get; }

    /// <summary>Gets a command that starts or stops audio capture, or cancels a transcription.</summary>
    public ICommand ToggleAudioCaptureCommand { get; }

    /// <summary>Gets a command that turns live voice on or off.</summary>
    public ICommand ToggleLiveSpeechCommand { get; }

    /// <summary>
    /// Sends the draft. A message the session could not accept is reported through <see cref="ErrorMessage"/>;
    /// a response that fails shows an error with Retry in the message list.
    /// </summary>
    /// <returns>A task that completes when the response has finished, stopped, or failed.</returns>
    public Task SendAsync() => _composer.SendAsync();

    /// <summary>
    /// Stops the response in progress. The turn keeps the user's message and the partial response, and is marked
    /// as stopped.
    /// </summary>
    /// <returns>A task that completes when the response has stopped.</returns>
    public Task StopAsync() => _composer.StopAsync();

    /// <summary>
    /// Stops and discards any response in progress, then clears the conversation, keeping the session and its tools.
    /// </summary>
    /// <returns>A task that completes when the conversation is cleared.</returns>
    public Task ClearAsync() => ClearSessionAsync();

    /// <summary>Stages an attachment for the next message, within the attachment limits.</summary>
    /// <param name="attachment">The attachment; set <see cref="DataContent.Name"/> to show a file name.</param>
    /// <returns><see langword="true"/> when it was staged; otherwise <see cref="ErrorMessage"/> says why not.</returns>
    public bool AddAttachment(DataContent attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return _composer.AddAttachments([attachment]);
    }

    /// <summary>Removes a staged attachment.</summary>
    /// <param name="attachment">The attachment to remove.</param>
    /// <returns><see langword="true"/> when it was staged.</returns>
    public bool RemoveAttachment(DataContent attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return _composer.RemoveAttachment(attachment);
    }

    /// <summary>Shows the attachment picker and stages what the user picks.</summary>
    /// <returns>A task that completes when the picker closes.</returns>
    public Task PickAttachmentsAsync() => _composer.PickAttachmentsAsync();

    /// <summary>Moves keyboard focus to the message box.</summary>
    /// <returns><see langword="true"/> when the message box took focus.</returns>
    public bool FocusInput() => _inputPart?.Focus() ?? false;

    private static BindablePropertyKey CreateReadOnly(string name, bool defaultValue) =>
        BindableProperty.CreateReadOnly(name, typeof(bool), typeof(AIChatView), defaultValue);
}
