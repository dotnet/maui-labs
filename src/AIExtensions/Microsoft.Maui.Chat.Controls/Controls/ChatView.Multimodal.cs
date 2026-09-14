using System.Globalization;

namespace Microsoft.Maui.Chat.Controls;

public partial class ChatView
{
    /// <summary>The name of the stop button part.</summary>
    public const string StopButtonPartName = "PART_StopButton";

    /// <summary>The name of the audio-capture button part.</summary>
    public const string AudioButtonPartName = "PART_AudioButton";

    /// <summary>The name of the live-speech button part.</summary>
    public const string LiveSpeechButtonPartName = "PART_LiveSpeechButton";

    /// <summary>The generic message shown when audio capture fails.</summary>
    public const string DefaultAudioCaptureErrorMessage = ChatComposerController.DefaultAudioCaptureErrorMessage;

    /// <summary>The generic message shown when speech recognition fails.</summary>
    public const string DefaultSpeechRecognitionErrorMessage = ChatComposerController.DefaultSpeechRecognitionErrorMessage;

    private static readonly BindablePropertyKey InputContextPropertyKey =
        BindableProperty.CreateReadOnly(nameof(InputContext), typeof(ChatInputContext), typeof(ChatView), null);

    /// <summary>Backing property for <see cref="InputContext"/>.</summary>
    public static readonly BindableProperty InputContextProperty = InputContextPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="AllowAudioCapture"/>.</summary>
    public static readonly BindableProperty AllowAudioCaptureProperty =
        BindableProperty.Create(nameof(AllowAudioCapture), typeof(bool), typeof(ChatView), false,
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).UpdateMultimodalState());

    /// <summary>Backing property for <see cref="AllowLiveSpeech"/>.</summary>
    public static readonly BindableProperty AllowLiveSpeechProperty =
        BindableProperty.Create(nameof(AllowLiveSpeech), typeof(bool), typeof(ChatView), false,
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).UpdateMultimodalState());

    /// <summary>Backing property for <see cref="AudioRecorder"/>.</summary>
    public static readonly BindableProperty AudioRecorderProperty =
        BindableProperty.Create(nameof(AudioRecorder), typeof(IChatAudioRecorder), typeof(ChatView),
            defaultValueCreator: static _ => new MauiChatAudioRecorder(),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).OnAudioRecorderChanged());

    /// <summary>Backing property for <see cref="SpeechRecognizer"/>.</summary>
    public static readonly BindableProperty SpeechRecognizerProperty =
        BindableProperty.Create(nameof(SpeechRecognizer), typeof(IChatSpeechRecognizer), typeof(ChatView),
            defaultValueCreator: static _ => new MauiChatSpeechRecognizer(),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).OnSpeechRecognizerChanged());

    /// <summary>Backing property for <see cref="AudioTranscriber"/>.</summary>
    public static readonly BindableProperty AudioTranscriberProperty =
        BindableProperty.Create(nameof(AudioTranscriber), typeof(IChatAudioTranscriber), typeof(ChatView));

    /// <summary>Backing property for <see cref="MaximumAudioBytes"/>.</summary>
    public static readonly BindableProperty MaximumAudioBytesProperty =
        BindableProperty.Create(nameof(MaximumAudioBytes), typeof(long), typeof(ChatView), 10L * 1024 * 1024,
            validateValue: static (_, value) => (long)value > 0);

    /// <summary>Backing property for <see cref="MaximumAttachmentCount"/>.</summary>
    public static readonly BindableProperty MaximumAttachmentCountProperty =
        BindableProperty.Create(nameof(MaximumAttachmentCount), typeof(int), typeof(ChatView), 10,
            validateValue: static (_, value) => (int)value > 0);

    /// <summary>Backing property for <see cref="MaximumTotalAttachmentBytes"/>.</summary>
    public static readonly BindableProperty MaximumTotalAttachmentBytesProperty =
        BindableProperty.Create(nameof(MaximumTotalAttachmentBytes), typeof(long), typeof(ChatView), 50L * 1024 * 1024,
            validateValue: static (_, value) => (long)value > 0);

    /// <summary>Backing property for <see cref="AttachAudioRecording"/>.</summary>
    public static readonly BindableProperty AttachAudioRecordingProperty =
        BindableProperty.Create(nameof(AttachAudioRecording), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="ReplaceExistingAudio"/>.</summary>
    public static readonly BindableProperty ReplaceExistingAudioProperty =
        BindableProperty.Create(nameof(ReplaceExistingAudio), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="ShowInterimAudioTranscript"/>.</summary>
    public static readonly BindableProperty ShowInterimAudioTranscriptProperty =
        BindableProperty.Create(nameof(ShowInterimAudioTranscript), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="LiveSpeechAutoSubmit"/>.</summary>
    public static readonly BindableProperty LiveSpeechAutoSubmitProperty =
        BindableProperty.Create(nameof(LiveSpeechAutoSubmit), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="ShowInterimSpeechText"/>.</summary>
    public static readonly BindableProperty ShowInterimSpeechTextProperty =
        BindableProperty.Create(nameof(ShowInterimSpeechText), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="SpeechRecognitionCulture"/>.</summary>
    public static readonly BindableProperty SpeechRecognitionCultureProperty =
        BindableProperty.Create(nameof(SpeechRecognitionCulture), typeof(CultureInfo), typeof(ChatView),
            defaultValueCreator: static _ => CultureInfo.CurrentCulture);

    /// <summary>Backing property for <see cref="StopButtonText"/>.</summary>
    public static readonly BindableProperty StopButtonTextProperty =
        BindableProperty.Create(nameof(StopButtonText), typeof(string), typeof(ChatView), "\u25A0");

    /// <summary>Backing property for <see cref="AudioStartButtonText"/>.</summary>
    public static readonly BindableProperty AudioStartButtonTextProperty =
        BindableProperty.Create(nameof(AudioStartButtonText), typeof(string), typeof(ChatView), "\U0001F3A4");

    /// <summary>Backing property for <see cref="AudioStopButtonText"/>.</summary>
    public static readonly BindableProperty AudioStopButtonTextProperty =
        BindableProperty.Create(nameof(AudioStopButtonText), typeof(string), typeof(ChatView), "\u25A0");

    /// <summary>Backing property for <see cref="LiveSpeechStartButtonText"/>.</summary>
    public static readonly BindableProperty LiveSpeechStartButtonTextProperty =
        BindableProperty.Create(nameof(LiveSpeechStartButtonText), typeof(string), typeof(ChatView), "\U0001F5E3");

    /// <summary>Backing property for <see cref="LiveSpeechStopButtonText"/>.</summary>
    public static readonly BindableProperty LiveSpeechStopButtonTextProperty =
        BindableProperty.Create(nameof(LiveSpeechStopButtonText), typeof(string), typeof(ChatView), "\u25A0");

    /// <summary>Backing property for <see cref="StopButtonStyle"/>.</summary>
    public static readonly BindableProperty StopButtonStyleProperty =
        BindableProperty.Create(nameof(StopButtonStyle), typeof(Style), typeof(ChatView));

    /// <summary>Backing property for <see cref="AudioButtonStyle"/>.</summary>
    public static readonly BindableProperty AudioButtonStyleProperty =
        BindableProperty.Create(nameof(AudioButtonStyle), typeof(Style), typeof(ChatView));

    /// <summary>Backing property for <see cref="LiveSpeechButtonStyle"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonStyleProperty =
        BindableProperty.Create(nameof(LiveSpeechButtonStyle), typeof(Style), typeof(ChatView));

    private static readonly BindablePropertyKey IsComposingPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsComposing), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="IsComposing"/>.</summary>
    public static readonly BindableProperty IsComposingProperty = IsComposingPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsInputEnabledPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsInputEnabled), typeof(bool), typeof(ChatView), true);
    /// <summary>Backing property for <see cref="IsInputEnabled"/>.</summary>
    public static readonly BindableProperty IsInputEnabledProperty = IsInputEnabledPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanStopPropertyKey =
        BindableProperty.CreateReadOnly(nameof(CanStop), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="CanStop"/>.</summary>
    public static readonly BindableProperty CanStopProperty = CanStopPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ShowSendButtonPropertyKey =
        BindableProperty.CreateReadOnly(nameof(ShowSendButton), typeof(bool), typeof(ChatView), true);
    /// <summary>Backing property for <see cref="ShowSendButton"/>.</summary>
    public static readonly BindableProperty ShowSendButtonProperty = ShowSendButtonPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsRecordingAudioPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsRecordingAudio), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="IsRecordingAudio"/>.</summary>
    public static readonly BindableProperty IsRecordingAudioProperty = IsRecordingAudioPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsTranscribingAudioPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsTranscribingAudio), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="IsTranscribingAudio"/>.</summary>
    public static readonly BindableProperty IsTranscribingAudioProperty = IsTranscribingAudioPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsLiveSpeechEnabledPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsLiveSpeechEnabled), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="IsLiveSpeechEnabled"/>.</summary>
    public static readonly BindableProperty IsLiveSpeechEnabledProperty = IsLiveSpeechEnabledPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsListeningPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsListening), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="IsListening"/>.</summary>
    public static readonly BindableProperty IsListeningProperty = IsListeningPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanToggleAudioCapturePropertyKey =
        BindableProperty.CreateReadOnly(nameof(CanToggleAudioCapture), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="CanToggleAudioCapture"/>.</summary>
    public static readonly BindableProperty CanToggleAudioCaptureProperty = CanToggleAudioCapturePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanToggleLiveSpeechPropertyKey =
        BindableProperty.CreateReadOnly(nameof(CanToggleLiveSpeech), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="CanToggleLiveSpeech"/>.</summary>
    public static readonly BindableProperty CanToggleLiveSpeechProperty = CanToggleLiveSpeechPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AudioButtonDisplayTextPropertyKey =
        BindableProperty.CreateReadOnly(nameof(AudioButtonDisplayText), typeof(string), typeof(ChatView), string.Empty);
    /// <summary>Backing property for <see cref="AudioButtonDisplayText"/>.</summary>
    public static readonly BindableProperty AudioButtonDisplayTextProperty = AudioButtonDisplayTextPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AudioButtonLabelPropertyKey =
        BindableProperty.CreateReadOnly(nameof(AudioButtonLabel), typeof(string), typeof(ChatView), "Record audio");
    /// <summary>Backing property for <see cref="AudioButtonLabel"/>.</summary>
    public static readonly BindableProperty AudioButtonLabelProperty = AudioButtonLabelPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey LiveSpeechButtonDisplayTextPropertyKey =
        BindableProperty.CreateReadOnly(nameof(LiveSpeechButtonDisplayText), typeof(string), typeof(ChatView), string.Empty);
    /// <summary>Backing property for <see cref="LiveSpeechButtonDisplayText"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonDisplayTextProperty = LiveSpeechButtonDisplayTextPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey LiveSpeechButtonLabelPropertyKey =
        BindableProperty.CreateReadOnly(nameof(LiveSpeechButtonLabel), typeof(string), typeof(ChatView), "Start live voice");
    /// <summary>Backing property for <see cref="LiveSpeechButtonLabel"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonLabelProperty = LiveSpeechButtonLabelPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey InputStatusMessagePropertyKey =
        BindableProperty.CreateReadOnly(nameof(InputStatusMessage), typeof(string), typeof(ChatView), null,
            propertyChanged: static (bindable, _, value) => ((ChatView)bindable).SetValue(HasInputStatusMessagePropertyKey, value is string { Length: > 0 }));
    /// <summary>Backing property for <see cref="InputStatusMessage"/>.</summary>
    public static readonly BindableProperty InputStatusMessageProperty = InputStatusMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasInputStatusMessagePropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasInputStatusMessage), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="HasInputStatusMessage"/>.</summary>
    public static readonly BindableProperty HasInputStatusMessageProperty = HasInputStatusMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey InputErrorMessagePropertyKey =
        BindableProperty.CreateReadOnly(nameof(InputErrorMessage), typeof(string), typeof(ChatView), null,
            propertyChanged: static (bindable, _, value) => ((ChatView)bindable).SetValue(HasInputErrorMessagePropertyKey, value is string { Length: > 0 }));
    /// <summary>Backing property for <see cref="InputErrorMessage"/>.</summary>
    public static readonly BindableProperty InputErrorMessageProperty = InputErrorMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasInputErrorMessagePropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasInputErrorMessage), typeof(bool), typeof(ChatView), false);
    /// <summary>Backing property for <see cref="HasInputErrorMessage"/>.</summary>
    public static readonly BindableProperty HasInputErrorMessageProperty = HasInputErrorMessagePropertyKey.BindableProperty;

    private Button? _stopButtonPart;
    private Button? _audioButtonPart;
    private Button? _liveSpeechButtonPart;

    /// <summary>Raised after audio was captured successfully.</summary>
    public event EventHandler<ChatAudioRecordedEventArgs>? AudioRecorded;

    /// <summary>Raised after captured audio was transcribed successfully.</summary>
    public event EventHandler<ChatAudioTranscribedEventArgs>? AudioTranscribed;

    /// <summary>Raised for live-speech partials, final results, and failures.</summary>
    public event EventHandler<ChatSpeechRecognitionEventArgs>? SpeechRecognized;

    /// <summary>Gets the reusable state and action surface for the composer.</summary>
    public ChatInputContext InputContext => (ChatInputContext)GetValue(InputContextProperty);

    /// <summary>Gets or sets whether the built-in audio-capture button is shown.</summary>
    public bool AllowAudioCapture { get => (bool)GetValue(AllowAudioCaptureProperty); set => SetValue(AllowAudioCaptureProperty, value); }

    /// <summary>Gets or sets whether the built-in live-speech button is shown.</summary>
    public bool AllowLiveSpeech { get => (bool)GetValue(AllowLiveSpeechProperty); set => SetValue(AllowLiveSpeechProperty, value); }

    /// <summary>Gets or sets the audio recorder. The default uses the current platform microphone.</summary>
    public IChatAudioRecorder? AudioRecorder { get => (IChatAudioRecorder?)GetValue(AudioRecorderProperty); set => SetValue(AudioRecorderProperty, value); }

    /// <summary>Gets or sets the speech recognizer. The default uses CommunityToolkit.Maui speech-to-text.</summary>
    public IChatSpeechRecognizer? SpeechRecognizer { get => (IChatSpeechRecognizer?)GetValue(SpeechRecognizerProperty); set => SetValue(SpeechRecognizerProperty, value); }

    /// <summary>Gets or sets the optional callback that transcribes a completed audio recording.</summary>
    public IChatAudioTranscriber? AudioTranscriber { get => (IChatAudioTranscriber?)GetValue(AudioTranscriberProperty); set => SetValue(AudioTranscriberProperty, value); }

    /// <summary>Gets or sets the largest accepted audio recording. Defaults to 10 MB.</summary>
    public long MaximumAudioBytes { get => (long)GetValue(MaximumAudioBytesProperty); set => SetValue(MaximumAudioBytesProperty, value); }

    /// <summary>Gets or sets the maximum number of staged attachments. Defaults to 10.</summary>
    public int MaximumAttachmentCount { get => (int)GetValue(MaximumAttachmentCountProperty); set => SetValue(MaximumAttachmentCountProperty, value); }

    /// <summary>Gets or sets the maximum total size of buffered attachments. Defaults to 50 MB.</summary>
    public long MaximumTotalAttachmentBytes { get => (long)GetValue(MaximumTotalAttachmentBytesProperty); set => SetValue(MaximumTotalAttachmentBytesProperty, value); }

    /// <summary>Gets or sets whether a captured recording is staged as an attachment.</summary>
    public bool AttachAudioRecording { get => (bool)GetValue(AttachAudioRecordingProperty); set => SetValue(AttachAudioRecordingProperty, value); }

    /// <summary>Gets or sets whether a new recording replaces staged <c>audio/*</c> attachments.</summary>
    public bool ReplaceExistingAudio { get => (bool)GetValue(ReplaceExistingAudioProperty); set => SetValue(ReplaceExistingAudioProperty, value); }

    /// <summary>Gets or sets whether speech recognition updates the composer while audio is recording.</summary>
    public bool ShowInterimAudioTranscript { get => (bool)GetValue(ShowInterimAudioTranscriptProperty); set => SetValue(ShowInterimAudioTranscriptProperty, value); }

    /// <summary>Gets or sets whether each finalized live-speech utterance is submitted automatically.</summary>
    public bool LiveSpeechAutoSubmit { get => (bool)GetValue(LiveSpeechAutoSubmitProperty); set => SetValue(LiveSpeechAutoSubmitProperty, value); }

    /// <summary>Gets or sets whether partial live-speech text is shown in the composer.</summary>
    public bool ShowInterimSpeechText { get => (bool)GetValue(ShowInterimSpeechTextProperty); set => SetValue(ShowInterimSpeechTextProperty, value); }

    /// <summary>Gets or sets the recognition language.</summary>
    public CultureInfo SpeechRecognitionCulture { get => (CultureInfo)GetValue(SpeechRecognitionCultureProperty); set => SetValue(SpeechRecognitionCultureProperty, value); }

    /// <summary>Gets or sets the stop button caption.</summary>
    public string StopButtonText { get => (string)GetValue(StopButtonTextProperty); set => SetValue(StopButtonTextProperty, value); }

    /// <summary>Gets or sets the idle audio-capture caption.</summary>
    public string AudioStartButtonText { get => (string)GetValue(AudioStartButtonTextProperty); set => SetValue(AudioStartButtonTextProperty, value); }

    /// <summary>Gets or sets the active audio-capture caption.</summary>
    public string AudioStopButtonText { get => (string)GetValue(AudioStopButtonTextProperty); set => SetValue(AudioStopButtonTextProperty, value); }

    /// <summary>Gets or sets the idle live-speech caption.</summary>
    public string LiveSpeechStartButtonText { get => (string)GetValue(LiveSpeechStartButtonTextProperty); set => SetValue(LiveSpeechStartButtonTextProperty, value); }

    /// <summary>Gets or sets the active live-speech caption.</summary>
    public string LiveSpeechStopButtonText { get => (string)GetValue(LiveSpeechStopButtonTextProperty); set => SetValue(LiveSpeechStopButtonTextProperty, value); }

    /// <summary>Gets or sets the style applied to the stop button.</summary>
    public Style? StopButtonStyle { get => (Style?)GetValue(StopButtonStyleProperty); set => SetValue(StopButtonStyleProperty, value); }

    /// <summary>Gets or sets the style applied to the audio-capture button.</summary>
    public Style? AudioButtonStyle { get => (Style?)GetValue(AudioButtonStyleProperty); set => SetValue(AudioButtonStyleProperty, value); }

    /// <summary>Gets or sets the style applied to the live-speech button.</summary>
    public Style? LiveSpeechButtonStyle { get => (Style?)GetValue(LiveSpeechButtonStyleProperty); set => SetValue(LiveSpeechButtonStyleProperty, value); }

    /// <summary>Gets whether an asynchronous composer operation is active.</summary>
    public bool IsComposing => (bool)GetValue(IsComposingProperty);
    /// <summary>Gets whether the text and attachment inputs should be enabled.</summary>
    public bool IsInputEnabled => (bool)GetValue(IsInputEnabledProperty);
    /// <summary>Gets whether the active response can be stopped.</summary>
    public bool CanStop => (bool)GetValue(CanStopProperty);
    /// <summary>Gets whether the send button should be shown instead of the stop button.</summary>
    public bool ShowSendButton => (bool)GetValue(ShowSendButtonProperty);
    /// <summary>Gets whether audio is currently being recorded.</summary>
    public bool IsRecordingAudio => (bool)GetValue(IsRecordingAudioProperty);
    /// <summary>Gets whether captured audio is being transcribed.</summary>
    public bool IsTranscribingAudio => (bool)GetValue(IsTranscribingAudioProperty);
    /// <summary>Gets whether continuous live speech is enabled.</summary>
    public bool IsLiveSpeechEnabled => (bool)GetValue(IsLiveSpeechEnabledProperty);
    /// <summary>Gets whether the speech recognizer is actively listening.</summary>
    public bool IsListening => (bool)GetValue(IsListeningProperty);
    /// <summary>Gets whether audio capture can be toggled.</summary>
    public bool CanToggleAudioCapture => (bool)GetValue(CanToggleAudioCaptureProperty);
    /// <summary>Gets whether live speech can be toggled.</summary>
    public bool CanToggleLiveSpeech => (bool)GetValue(CanToggleLiveSpeechProperty);
    /// <summary>Gets the current audio button caption.</summary>
    public string AudioButtonDisplayText => (string)GetValue(AudioButtonDisplayTextProperty);
    /// <summary>Gets the current audio button accessibility label.</summary>
    public string AudioButtonLabel => (string)GetValue(AudioButtonLabelProperty);
    /// <summary>Gets the current live-speech button caption.</summary>
    public string LiveSpeechButtonDisplayText => (string)GetValue(LiveSpeechButtonDisplayTextProperty);
    /// <summary>Gets the current live-speech button accessibility label.</summary>
    public string LiveSpeechButtonLabel => (string)GetValue(LiveSpeechButtonLabelProperty);
    /// <summary>Gets the current user-safe composer status.</summary>
    public string? InputStatusMessage => (string?)GetValue(InputStatusMessageProperty);
    /// <summary>Gets whether <see cref="InputStatusMessage"/> has a value.</summary>
    public bool HasInputStatusMessage => (bool)GetValue(HasInputStatusMessageProperty);
    /// <summary>Gets the current user-safe composer error.</summary>
    public string? InputErrorMessage => (string?)GetValue(InputErrorMessageProperty);
    /// <summary>Gets whether <see cref="InputErrorMessage"/> has a value.</summary>
    public bool HasInputErrorMessage => (bool)GetValue(HasInputErrorMessageProperty);

    /// <summary>Stops the active response.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigureComposerController();
        await EffectiveComposerController.StopAsync(cancellationToken).ConfigureAwait(true);
        await FocusInputAsync();
    }

    /// <summary>Starts, stops, or cancels audio capture according to the current state.</summary>
    public Task ToggleAudioCaptureAsync() => ConfigureAndInvoke(static controller => controller.ToggleAudioCaptureAsync());

    /// <summary>Starts audio capture.</summary>
    public Task StartAudioCaptureAsync(CancellationToken cancellationToken = default) =>
        ConfigureAndInvoke(controller => controller.StartAudioCaptureAsync(cancellationToken));

    /// <summary>Stops audio capture and optionally stages and transcribes the recording.</summary>
    public async Task StopAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        ConfigureComposerController();
        await EffectiveComposerController.StopAudioCaptureAsync(cancellationToken).ConfigureAwait(true);
        await FocusInputAsync();
    }

    /// <summary>Cancels and discards audio capture or transcription.</summary>
    public Task CancelAudioCaptureAsync(CancellationToken cancellationToken = default) =>
        ConfigureAndInvoke(controller => controller.CancelAudioCaptureAsync(cancellationToken));

    /// <summary>Starts or stops continuous live speech.</summary>
    public Task ToggleLiveSpeechAsync() => ConfigureAndInvoke(static controller => controller.ToggleLiveSpeechAsync());

    /// <summary>Enables continuous live speech.</summary>
    public Task StartLiveSpeechAsync(CancellationToken cancellationToken = default) =>
        ConfigureAndInvoke(controller => controller.StartLiveSpeechAsync(cancellationToken));

    /// <summary>Disables continuous live speech and preserves the composed transcript.</summary>
    public async Task StopLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        ConfigureComposerController();
        await EffectiveComposerController.StopLiveSpeechAsync(cancellationToken).ConfigureAwait(true);
        await FocusInputAsync();
    }

    internal ValueTask FocusInputAsync()
    {
        _inputEntryPart?.Focus();
        return ValueTask.CompletedTask;
    }

    internal void SetInputComposing(bool value) => EffectiveComposerController.SetComposing(value);

    internal void SetInputStatusMessage(string? value) => EffectiveComposerController.SetStatusMessage(value);

    internal void SetInputErrorMessage(string? value) => EffectiveComposerController.SetErrorMessage(value);

    private void RefreshInputContextIfAvailable()
    {
        if (GetValue(InputContextProperty) is ChatInputContext context)
            context.Refresh();
    }

    private async Task ConfigureAndInvoke(Func<ChatComposerController, Task> operation)
    {
        ConfigureComposerController();
        await operation(EffectiveComposerController).ConfigureAwait(true);
    }

    private void InitializeMultimodalInput()
    {
        SetValue(InputContextPropertyKey, new ChatInputContext(EffectiveComposerController, FocusInputAsync));
        SetDynamicResource(StopButtonStyleProperty, Themes.ChatThemeKeys.StopButtonStyle);
        SetDynamicResource(AudioButtonStyleProperty, Themes.ChatThemeKeys.AudioButtonStyle);
        SetDynamicResource(LiveSpeechButtonStyleProperty, Themes.ChatThemeKeys.LiveSpeechButtonStyle);
        SynchronizeComposerState();
    }

    private void AttachMultimodalParts()
    {
        _stopButtonPart = FindPart<Button>(StopButtonPartName);
        _audioButtonPart = FindPart<Button>(AudioButtonPartName);
        _liveSpeechButtonPart = FindPart<Button>(LiveSpeechButtonPartName);
        if (_stopButtonPart is not null)
            _stopButtonPart.Clicked += OnStopClicked;
        if (_audioButtonPart is not null)
            _audioButtonPart.Clicked += OnAudioClicked;
        if (_liveSpeechButtonPart is not null)
            _liveSpeechButtonPart.Clicked += OnLiveSpeechClicked;
    }

    private void DetachMultimodalParts()
    {
        if (_stopButtonPart is not null)
            _stopButtonPart.Clicked -= OnStopClicked;
        if (_audioButtonPart is not null)
            _audioButtonPart.Clicked -= OnAudioClicked;
        if (_liveSpeechButtonPart is not null)
            _liveSpeechButtonPart.Clicked -= OnLiveSpeechClicked;
        _stopButtonPart = null;
        _audioButtonPart = null;
        _liveSpeechButtonPart = null;
    }

    private void OnStopClicked(object? sender, EventArgs e) => _ = StopAsync();
    private void OnAudioClicked(object? sender, EventArgs e) => _ = ToggleAudioCaptureAsync();
    private void OnLiveSpeechClicked(object? sender, EventArgs e) => _ = ToggleLiveSpeechAsync();

    private void OnAudioRecorderChanged() => ConfigureComposerController();
    private void OnSpeechRecognizerChanged() => ConfigureComposerController();

    private void OnComposerTextChanged()
    {
        if (!_updatingController)
            EffectiveComposerController.Text = (string)GetValue(TextProperty);
    }

    private void UpdateMultimodalState()
    {
        if (_effectiveComposerController is null)
            return;
        ConfigureComposerController();
        SynchronizeComposerState();
    }
}
