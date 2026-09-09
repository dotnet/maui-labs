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
    public const string DefaultAudioCaptureErrorMessage = "The audio recording could not be completed.";

    /// <summary>The generic message shown when speech recognition fails.</summary>
    public const string DefaultSpeechRecognitionErrorMessage = "Live voice could not continue.";

    private static readonly BindablePropertyKey InputContextPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(InputContext),
            typeof(ChatInputContext),
            typeof(ChatView),
            null);

    /// <summary>Backing property for <see cref="InputContext"/>.</summary>
    public static readonly BindableProperty InputContextProperty =
        InputContextPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="AllowAudioCapture"/>.</summary>
    public static readonly BindableProperty AllowAudioCaptureProperty =
        BindableProperty.Create(
            nameof(AllowAudioCapture),
            typeof(bool),
            typeof(ChatView),
            false,
            propertyChanged: static (bindable, _, _) =>
                ((ChatView)bindable).UpdateMultimodalState());

    /// <summary>Backing property for <see cref="AllowLiveSpeech"/>.</summary>
    public static readonly BindableProperty AllowLiveSpeechProperty =
        BindableProperty.Create(
            nameof(AllowLiveSpeech),
            typeof(bool),
            typeof(ChatView),
            false,
            propertyChanged: static (bindable, _, _) =>
                ((ChatView)bindable).UpdateMultimodalState());

    /// <summary>Backing property for <see cref="AudioRecorder"/>.</summary>
    public static readonly BindableProperty AudioRecorderProperty =
        BindableProperty.Create(
            nameof(AudioRecorder),
            typeof(IChatAudioRecorder),
            typeof(ChatView),
            defaultValueCreator: static _ => new MauiChatAudioRecorder(),
            propertyChanged: static (bindable, _, _) =>
                ((ChatView)bindable).OnAudioRecorderChanged());

    /// <summary>Backing property for <see cref="SpeechRecognizer"/>.</summary>
    public static readonly BindableProperty SpeechRecognizerProperty =
        BindableProperty.Create(
            nameof(SpeechRecognizer),
            typeof(IChatSpeechRecognizer),
            typeof(ChatView),
            defaultValueCreator: static _ => new MauiChatSpeechRecognizer(),
            propertyChanged: static (bindable, _, _) =>
                ((ChatView)bindable).OnSpeechRecognizerChanged());

    /// <summary>Backing property for <see cref="AudioTranscriber"/>.</summary>
    public static readonly BindableProperty AudioTranscriberProperty =
        BindableProperty.Create(
            nameof(AudioTranscriber),
            typeof(IChatAudioTranscriber),
            typeof(ChatView));

    /// <summary>Backing property for <see cref="MaximumAudioBytes"/>.</summary>
    public static readonly BindableProperty MaximumAudioBytesProperty =
        BindableProperty.Create(
            nameof(MaximumAudioBytes),
            typeof(long),
            typeof(ChatView),
            10L * 1024 * 1024,
            validateValue: static (_, value) => (long)value > 0);

    /// <summary>Backing property for <see cref="MaximumAttachmentCount"/>.</summary>
    public static readonly BindableProperty MaximumAttachmentCountProperty =
        BindableProperty.Create(
            nameof(MaximumAttachmentCount),
            typeof(int),
            typeof(ChatView),
            10,
            validateValue: static (_, value) => (int)value > 0);

    /// <summary>Backing property for <see cref="MaximumTotalAttachmentBytes"/>.</summary>
    public static readonly BindableProperty MaximumTotalAttachmentBytesProperty =
        BindableProperty.Create(
            nameof(MaximumTotalAttachmentBytes),
            typeof(long),
            typeof(ChatView),
            50L * 1024 * 1024,
            validateValue: static (_, value) => (long)value > 0);

    /// <summary>Backing property for <see cref="AttachAudioRecording"/>.</summary>
    public static readonly BindableProperty AttachAudioRecordingProperty =
        BindableProperty.Create(
            nameof(AttachAudioRecording),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="ReplaceExistingAudio"/>.</summary>
    public static readonly BindableProperty ReplaceExistingAudioProperty =
        BindableProperty.Create(
            nameof(ReplaceExistingAudio),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="ShowInterimAudioTranscript"/>.</summary>
    public static readonly BindableProperty ShowInterimAudioTranscriptProperty =
        BindableProperty.Create(
            nameof(ShowInterimAudioTranscript),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="LiveSpeechAutoSubmit"/>.</summary>
    public static readonly BindableProperty LiveSpeechAutoSubmitProperty =
        BindableProperty.Create(
            nameof(LiveSpeechAutoSubmit),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="ShowInterimSpeechText"/>.</summary>
    public static readonly BindableProperty ShowInterimSpeechTextProperty =
        BindableProperty.Create(
            nameof(ShowInterimSpeechText),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="SpeechRecognitionCulture"/>.</summary>
    public static readonly BindableProperty SpeechRecognitionCultureProperty =
        BindableProperty.Create(
            nameof(SpeechRecognitionCulture),
            typeof(CultureInfo),
            typeof(ChatView),
            defaultValueCreator: static _ => CultureInfo.CurrentCulture);

    /// <summary>Backing property for <see cref="StopButtonText"/>.</summary>
    public static readonly BindableProperty StopButtonTextProperty =
        BindableProperty.Create(
            nameof(StopButtonText),
            typeof(string),
            typeof(ChatView),
            "\u25A0");

    /// <summary>Backing property for <see cref="AudioStartButtonText"/>.</summary>
    public static readonly BindableProperty AudioStartButtonTextProperty =
        BindableProperty.Create(
            nameof(AudioStartButtonText),
            typeof(string),
            typeof(ChatView),
            "\U0001F3A4");

    /// <summary>Backing property for <see cref="AudioStopButtonText"/>.</summary>
    public static readonly BindableProperty AudioStopButtonTextProperty =
        BindableProperty.Create(
            nameof(AudioStopButtonText),
            typeof(string),
            typeof(ChatView),
            "\u25A0");

    /// <summary>Backing property for <see cref="LiveSpeechStartButtonText"/>.</summary>
    public static readonly BindableProperty LiveSpeechStartButtonTextProperty =
        BindableProperty.Create(
            nameof(LiveSpeechStartButtonText),
            typeof(string),
            typeof(ChatView),
            "\U0001F5E3");

    /// <summary>Backing property for <see cref="LiveSpeechStopButtonText"/>.</summary>
    public static readonly BindableProperty LiveSpeechStopButtonTextProperty =
        BindableProperty.Create(
            nameof(LiveSpeechStopButtonText),
            typeof(string),
            typeof(ChatView),
            "\u25A0");

    /// <summary>Backing property for <see cref="StopButtonStyle"/>.</summary>
    public static readonly BindableProperty StopButtonStyleProperty =
        BindableProperty.Create(
            nameof(StopButtonStyle),
            typeof(Style),
            typeof(ChatView));

    /// <summary>Backing property for <see cref="AudioButtonStyle"/>.</summary>
    public static readonly BindableProperty AudioButtonStyleProperty =
        BindableProperty.Create(
            nameof(AudioButtonStyle),
            typeof(Style),
            typeof(ChatView));

    /// <summary>Backing property for <see cref="LiveSpeechButtonStyle"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonStyleProperty =
        BindableProperty.Create(
            nameof(LiveSpeechButtonStyle),
            typeof(Style),
            typeof(ChatView));

    private static readonly BindablePropertyKey IsComposingPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsComposing),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="IsComposing"/>.</summary>
    public static readonly BindableProperty IsComposingProperty =
        IsComposingPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsInputEnabledPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsInputEnabled),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="IsInputEnabled"/>.</summary>
    public static readonly BindableProperty IsInputEnabledProperty =
        IsInputEnabledPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanStopPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(CanStop),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="CanStop"/>.</summary>
    public static readonly BindableProperty CanStopProperty =
        CanStopPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ShowSendButtonPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(ShowSendButton),
            typeof(bool),
            typeof(ChatView),
            true);

    /// <summary>Backing property for <see cref="ShowSendButton"/>.</summary>
    public static readonly BindableProperty ShowSendButtonProperty =
        ShowSendButtonPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsRecordingAudioPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsRecordingAudio),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="IsRecordingAudio"/>.</summary>
    public static readonly BindableProperty IsRecordingAudioProperty =
        IsRecordingAudioPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsTranscribingAudioPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsTranscribingAudio),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="IsTranscribingAudio"/>.</summary>
    public static readonly BindableProperty IsTranscribingAudioProperty =
        IsTranscribingAudioPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsLiveSpeechEnabledPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsLiveSpeechEnabled),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="IsLiveSpeechEnabled"/>.</summary>
    public static readonly BindableProperty IsLiveSpeechEnabledProperty =
        IsLiveSpeechEnabledPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsListeningPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(IsListening),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="IsListening"/>.</summary>
    public static readonly BindableProperty IsListeningProperty =
        IsListeningPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanToggleAudioCapturePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(CanToggleAudioCapture),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="CanToggleAudioCapture"/>.</summary>
    public static readonly BindableProperty CanToggleAudioCaptureProperty =
        CanToggleAudioCapturePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanToggleLiveSpeechPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(CanToggleLiveSpeech),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="CanToggleLiveSpeech"/>.</summary>
    public static readonly BindableProperty CanToggleLiveSpeechProperty =
        CanToggleLiveSpeechPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AudioButtonDisplayTextPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(AudioButtonDisplayText),
            typeof(string),
            typeof(ChatView),
            string.Empty);

    /// <summary>Backing property for <see cref="AudioButtonDisplayText"/>.</summary>
    public static readonly BindableProperty AudioButtonDisplayTextProperty =
        AudioButtonDisplayTextPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AudioButtonLabelPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(AudioButtonLabel),
            typeof(string),
            typeof(ChatView),
            "Record audio");

    /// <summary>Backing property for <see cref="AudioButtonLabel"/>.</summary>
    public static readonly BindableProperty AudioButtonLabelProperty =
        AudioButtonLabelPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey LiveSpeechButtonDisplayTextPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(LiveSpeechButtonDisplayText),
            typeof(string),
            typeof(ChatView),
            string.Empty);

    /// <summary>Backing property for <see cref="LiveSpeechButtonDisplayText"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonDisplayTextProperty =
        LiveSpeechButtonDisplayTextPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey LiveSpeechButtonLabelPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(LiveSpeechButtonLabel),
            typeof(string),
            typeof(ChatView),
            "Start live voice");

    /// <summary>Backing property for <see cref="LiveSpeechButtonLabel"/>.</summary>
    public static readonly BindableProperty LiveSpeechButtonLabelProperty =
        LiveSpeechButtonLabelPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey InputStatusMessagePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(InputStatusMessage),
            typeof(string),
            typeof(ChatView),
            null,
            propertyChanged: static (bindable, _, value) =>
                ((ChatView)bindable).SetValue(
                    HasInputStatusMessagePropertyKey,
                    value is string { Length: > 0 }));

    /// <summary>Backing property for <see cref="InputStatusMessage"/>.</summary>
    public static readonly BindableProperty InputStatusMessageProperty =
        InputStatusMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasInputStatusMessagePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(HasInputStatusMessage),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="HasInputStatusMessage"/>.</summary>
    public static readonly BindableProperty HasInputStatusMessageProperty =
        HasInputStatusMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey InputErrorMessagePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(InputErrorMessage),
            typeof(string),
            typeof(ChatView),
            null,
            propertyChanged: static (bindable, _, value) =>
                ((ChatView)bindable).SetValue(
                    HasInputErrorMessagePropertyKey,
                    value is string { Length: > 0 }));

    /// <summary>Backing property for <see cref="InputErrorMessage"/>.</summary>
    public static readonly BindableProperty InputErrorMessageProperty =
        InputErrorMessagePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasInputErrorMessagePropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(HasInputErrorMessage),
            typeof(bool),
            typeof(ChatView),
            false);

    /// <summary>Backing property for <see cref="HasInputErrorMessage"/>.</summary>
    public static readonly BindableProperty HasInputErrorMessageProperty =
        HasInputErrorMessagePropertyKey.BindableProperty;

    private CancellationTokenSource? _sendCts;
    private CancellationTokenSource? _stoppedSendCts;
    private CancellationTokenSource? _attachmentReadCts;
    private CancellationTokenSource? _audioOperationCts;
    private CancellationTokenSource? _speechPassCts;
    private CancellationTokenSource? _liveSpeechRestartCts;
    private Task? _speechStopTask;
    private IChatAudioRecorder? _activeAudioRecorder;
    private IChatSpeechRecognizer? _activeSpeechRecognizer;
    private EventHandler<ChatSpeechRecognitionEventArgs>? _activeSpeechHandler;
    private SpeechInputMode _speechMode;
    private Button? _stopButtonPart;
    private Button? _audioButtonPart;
    private Button? _liveSpeechButtonPart;
    private bool _externalIsComposing;
    private bool _isRecordingAudio;
    private bool _isTranscribingAudio;
    private bool _isLiveSpeechEnabled;
    private bool _isListening;
    private bool _speechPassStarted;
    private bool _speechStarting;
    private bool _speechFinalizing;
    private bool _speechPermissionsGranted;
    private int _liveSpeechRestartAttempt;
    private string _audioDictationPrefix = string.Empty;
    private string _audioCommittedTranscript = string.Empty;
    private bool _audioHadInterimTranscript;
    private string _liveSpeechPrefix = string.Empty;
    private string _liveSpeechCommittedTranscript = string.Empty;

    /// <summary>Raised after audio was captured successfully.</summary>
    public event EventHandler<ChatAudioRecordedEventArgs>? AudioRecorded;

    /// <summary>Raised after captured audio was transcribed successfully.</summary>
    public event EventHandler<ChatAudioTranscribedEventArgs>? AudioTranscribed;

    /// <summary>Raised for live-speech partials, final results, and failures.</summary>
    public event EventHandler<ChatSpeechRecognitionEventArgs>? SpeechRecognized;

    /// <summary>Gets the reusable state and action surface for the composer.</summary>
    public ChatInputContext InputContext =>
        (ChatInputContext)GetValue(InputContextProperty);

    /// <summary>Gets or sets whether the built-in audio-capture button is shown.</summary>
    public bool AllowAudioCapture
    {
        get => (bool)GetValue(AllowAudioCaptureProperty);
        set => SetValue(AllowAudioCaptureProperty, value);
    }

    /// <summary>Gets or sets whether the built-in live-speech button is shown.</summary>
    public bool AllowLiveSpeech
    {
        get => (bool)GetValue(AllowLiveSpeechProperty);
        set => SetValue(AllowLiveSpeechProperty, value);
    }

    /// <summary>Gets or sets the audio recorder. The default uses the current platform microphone.</summary>
    public IChatAudioRecorder? AudioRecorder
    {
        get => (IChatAudioRecorder?)GetValue(AudioRecorderProperty);
        set => SetValue(AudioRecorderProperty, value);
    }

    /// <summary>Gets or sets the speech recognizer. The default uses CommunityToolkit.Maui speech-to-text.</summary>
    public IChatSpeechRecognizer? SpeechRecognizer
    {
        get => (IChatSpeechRecognizer?)GetValue(SpeechRecognizerProperty);
        set => SetValue(SpeechRecognizerProperty, value);
    }

    /// <summary>Gets or sets the optional callback that transcribes a completed audio recording.</summary>
    public IChatAudioTranscriber? AudioTranscriber
    {
        get => (IChatAudioTranscriber?)GetValue(AudioTranscriberProperty);
        set => SetValue(AudioTranscriberProperty, value);
    }

    /// <summary>Gets or sets the largest accepted audio recording. Defaults to 10 MB.</summary>
    public long MaximumAudioBytes
    {
        get => (long)GetValue(MaximumAudioBytesProperty);
        set => SetValue(MaximumAudioBytesProperty, value);
    }

    /// <summary>Gets or sets the maximum number of staged attachments. Defaults to 10.</summary>
    public int MaximumAttachmentCount
    {
        get => (int)GetValue(MaximumAttachmentCountProperty);
        set => SetValue(MaximumAttachmentCountProperty, value);
    }

    /// <summary>Gets or sets the maximum total size of buffered attachments. Defaults to 50 MB.</summary>
    public long MaximumTotalAttachmentBytes
    {
        get => (long)GetValue(MaximumTotalAttachmentBytesProperty);
        set => SetValue(MaximumTotalAttachmentBytesProperty, value);
    }

    /// <summary>Gets or sets whether a captured recording is staged as an attachment.</summary>
    public bool AttachAudioRecording
    {
        get => (bool)GetValue(AttachAudioRecordingProperty);
        set => SetValue(AttachAudioRecordingProperty, value);
    }

    /// <summary>Gets or sets whether a new recording replaces staged <c>audio/*</c> attachments.</summary>
    public bool ReplaceExistingAudio
    {
        get => (bool)GetValue(ReplaceExistingAudioProperty);
        set => SetValue(ReplaceExistingAudioProperty, value);
    }

    /// <summary>Gets or sets whether speech recognition updates the composer while audio is recording.</summary>
    public bool ShowInterimAudioTranscript
    {
        get => (bool)GetValue(ShowInterimAudioTranscriptProperty);
        set => SetValue(ShowInterimAudioTranscriptProperty, value);
    }

    /// <summary>Gets or sets whether each finalized live-speech utterance is submitted automatically.</summary>
    public bool LiveSpeechAutoSubmit
    {
        get => (bool)GetValue(LiveSpeechAutoSubmitProperty);
        set => SetValue(LiveSpeechAutoSubmitProperty, value);
    }

    /// <summary>Gets or sets whether partial live-speech text is shown in the composer.</summary>
    public bool ShowInterimSpeechText
    {
        get => (bool)GetValue(ShowInterimSpeechTextProperty);
        set => SetValue(ShowInterimSpeechTextProperty, value);
    }

    /// <summary>Gets or sets the recognition language.</summary>
    public CultureInfo SpeechRecognitionCulture
    {
        get => (CultureInfo)GetValue(SpeechRecognitionCultureProperty);
        set => SetValue(SpeechRecognitionCultureProperty, value);
    }

    /// <summary>Gets or sets the stop button caption.</summary>
    public string StopButtonText
    {
        get => (string)GetValue(StopButtonTextProperty);
        set => SetValue(StopButtonTextProperty, value);
    }

    /// <summary>Gets or sets the idle audio-capture caption.</summary>
    public string AudioStartButtonText
    {
        get => (string)GetValue(AudioStartButtonTextProperty);
        set => SetValue(AudioStartButtonTextProperty, value);
    }

    /// <summary>Gets or sets the active audio-capture caption.</summary>
    public string AudioStopButtonText
    {
        get => (string)GetValue(AudioStopButtonTextProperty);
        set => SetValue(AudioStopButtonTextProperty, value);
    }

    /// <summary>Gets or sets the idle live-speech caption.</summary>
    public string LiveSpeechStartButtonText
    {
        get => (string)GetValue(LiveSpeechStartButtonTextProperty);
        set => SetValue(LiveSpeechStartButtonTextProperty, value);
    }

    /// <summary>Gets or sets the active live-speech caption.</summary>
    public string LiveSpeechStopButtonText
    {
        get => (string)GetValue(LiveSpeechStopButtonTextProperty);
        set => SetValue(LiveSpeechStopButtonTextProperty, value);
    }

    /// <summary>Gets or sets the style applied to the stop button.</summary>
    public Style? StopButtonStyle
    {
        get => (Style?)GetValue(StopButtonStyleProperty);
        set => SetValue(StopButtonStyleProperty, value);
    }

    /// <summary>Gets or sets the style applied to the audio-capture button.</summary>
    public Style? AudioButtonStyle
    {
        get => (Style?)GetValue(AudioButtonStyleProperty);
        set => SetValue(AudioButtonStyleProperty, value);
    }

    /// <summary>Gets or sets the style applied to the live-speech button.</summary>
    public Style? LiveSpeechButtonStyle
    {
        get => (Style?)GetValue(LiveSpeechButtonStyleProperty);
        set => SetValue(LiveSpeechButtonStyleProperty, value);
    }

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
    public string AudioButtonDisplayText =>
        (string)GetValue(AudioButtonDisplayTextProperty);

    /// <summary>Gets the current audio button accessibility label.</summary>
    public string AudioButtonLabel =>
        (string)GetValue(AudioButtonLabelProperty);

    /// <summary>Gets the current live-speech button caption.</summary>
    public string LiveSpeechButtonDisplayText =>
        (string)GetValue(LiveSpeechButtonDisplayTextProperty);

    /// <summary>Gets the current live-speech button accessibility label.</summary>
    public string LiveSpeechButtonLabel =>
        (string)GetValue(LiveSpeechButtonLabelProperty);

    /// <summary>Gets the current user-safe composer status.</summary>
    public string? InputStatusMessage =>
        (string?)GetValue(InputStatusMessageProperty);

    /// <summary>Gets whether <see cref="InputStatusMessage"/> has a value.</summary>
    public bool HasInputStatusMessage =>
        (bool)GetValue(HasInputStatusMessageProperty);

    /// <summary>Gets the current user-safe composer error.</summary>
    public string? InputErrorMessage =>
        (string?)GetValue(InputErrorMessageProperty);

    /// <summary>Gets whether <see cref="InputErrorMessage"/> has a value.</summary>
    public bool HasInputErrorMessage =>
        (bool)GetValue(HasInputErrorMessageProperty);

    /// <summary>Stops the active response. Expected failures are surfaced through <see cref="SendError"/>.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanStop)
            return;

        var sendCts = _sendCts;
        var conversation = Conversation;
        if (sendCts is not null)
            _stoppedSendCts = sendCts;
        SetInputStatusMessage("Response stopped.");

        try
        {
            if (conversation?.CanCancel == true)
            {
                await conversation.CancelAsync(CancellationToken.None)
                    .ConfigureAwait(true);
            }
            else
            {
                sendCts?.Cancel();
            }

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            SetValue(SendErrorPropertyKey, DefaultSendErrorMessage);
        }
        finally
        {
            await FocusInputAsync();
            UpdateState();
        }
    }

    /// <summary>Starts, stops, or cancels audio capture according to the current state.</summary>
    public Task ToggleAudioCaptureAsync()
    {
        if (_isTranscribingAudio)
            return CancelAudioOperationAsync("Audio transcription canceled.");

        return _isRecordingAudio
            ? StopAudioCaptureAsync()
            : StartAudioCaptureAsync();
    }

    /// <summary>Starts audio capture.</summary>
    public async Task StartAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_audioOperationCts is not null
            || _isRecordingAudio
            || _isTranscribingAudio
            || IsConversationOperationActive)
            return;

        if (_isLiveSpeechEnabled)
            await StopLiveSpeechAsync(cancellationToken).ConfigureAwait(true);

        var recorder = AudioRecorder;
        if (!AllowAudioCapture || recorder is null || !recorder.IsSupported)
        {
            SetInputErrorMessage("Audio recording is not supported on this device.");
            return;
        }

        var operationCts = ReplaceAudioOperation(cancellationToken);
        _activeAudioRecorder = recorder;
        _audioDictationPrefix = Text.Trim();
        _audioCommittedTranscript = string.Empty;
        _audioHadInterimTranscript = false;
        SetInputErrorMessage(null);
        SetInputStatusMessage("Recording audio.");
        UpdateMultimodalState();

        try
        {
            await recorder.StartAsync(operationCts.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_audioOperationCts, operationCts))
                return;

            _isRecordingAudio = true;
            UpdateMultimodalState();

            if (ShowInterimAudioTranscript && SpeechRecognizer is { IsSupported: true })
            {
                try
                {
                    await StartSpeechPassAsync(
                        SpeechInputMode.AudioDictation,
                        operationCts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (ReferenceEquals(_audioOperationCts, operationCts))
                        SetInputStatusMessage("Recording audio. Live transcription is unavailable.");
                }
            }
        }
        catch (MicrophonePermissionDeniedException)
        {
            if (ReferenceEquals(_audioOperationCts, operationCts))
                SetInputErrorMessage("Microphone access was not available. Check app permissions.");
            CompleteAudioOperation(operationCts);
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
            if (recorder.IsRecording)
                await recorder.CancelAsync(CancellationToken.None).ConfigureAwait(true);
            CompleteAudioOperation(operationCts);
        }
        catch (Exception)
        {
            if (ReferenceEquals(_audioOperationCts, operationCts))
                SetInputErrorMessage(DefaultAudioCaptureErrorMessage);
            CompleteAudioOperation(operationCts);
        }
    }

    /// <summary>Stops audio capture and optionally stages and transcribes the recording.</summary>
    public async Task StopAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        var operationCts = _audioOperationCts;
        var recorder = _activeAudioRecorder;
        if (!_isRecordingAudio || operationCts is null || recorder is null)
            return;

        _isRecordingAudio = false;
        _isTranscribingAudio = true;
        UpdateMultimodalState();

        try
        {
            if (_speechMode == SpeechInputMode.AudioDictation)
                await StopSpeechPassAsync(cancellationToken).ConfigureAwait(true);

            var recording = await recorder.StopAsync(
                    MaximumAudioBytes,
                    operationCts.Token)
                .ConfigureAwait(true);
            if (!ReferenceEquals(_audioOperationCts, operationCts))
                return;

            if (recording is null)
            {
                SetInputErrorMessage(
                    "The device did not capture audio. Record for at least one second and check the microphone input level.");
                return;
            }

            if (recording.ByteCount > MaximumAudioBytes)
            {
                SetInputErrorMessage(
                    $"Audio recordings must be {FormatMegabytes(MaximumAudioBytes)} MB or smaller.");
                return;
            }

            if (AttachAudioRecording)
            {
                if (ReplaceExistingAudio)
                {
                    foreach (var attachment in Attachments
                        .Where(static item => item.IsAudio)
                        .ToArray())
                    {
                        RemoveAttachment(attachment);
                    }
                }

                AddAttachment(recording);
                AudioRecorded?.Invoke(this, new ChatAudioRecordedEventArgs(recording));
            }

            if (AudioTranscriber is { } transcriber)
            {
                SetInputStatusMessage("Transcribing audio.");
                var transcript = await transcriber
                    .TranscribeAsync(recording, operationCts.Token)
                    .ConfigureAwait(true);
                if (!ReferenceEquals(_audioOperationCts, operationCts))
                    return;

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    SetInputErrorMessage("No speech was recognized in the recording.");
                    return;
                }

                var normalized = transcript.Trim();
                Text = _audioHadInterimTranscript
                    ? AppendText(_audioDictationPrefix, normalized)
                    : AppendText(Text, normalized);
                AudioTranscribed?.Invoke(
                    this,
                    new ChatAudioTranscribedEventArgs(recording, normalized));
                SetInputStatusMessage("Voice transcription ready.");
            }
            else if (AttachAudioRecording)
            {
                SetInputStatusMessage("Audio recording attached.");
            }
            else
            {
                SetInputErrorMessage(
                    "An audio transcriber is required when recordings are not attached.");
            }

            await FocusInputAsync();
        }
        catch (AudioRecordingTooLargeException)
        {
            if (ReferenceEquals(_audioOperationCts, operationCts))
            {
                SetInputErrorMessage(
                    $"Audio recordings must be {FormatMegabytes(MaximumAudioBytes)} MB or smaller.");
            }
        }
        catch (OperationCanceledException)
        {
            if (!operationCts.IsCancellationRequested)
                operationCts.Cancel();
            if (recorder.IsRecording)
                await recorder.CancelAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (ReferenceEquals(_audioOperationCts, operationCts))
                SetInputErrorMessage(DefaultAudioCaptureErrorMessage);
        }
        finally
        {
            CompleteAudioOperation(operationCts);
        }
    }

    /// <summary>Cancels and discards audio capture or transcription.</summary>
    public Task CancelAudioCaptureAsync(CancellationToken cancellationToken = default) =>
        CancelAudioOperationAsync("Audio capture canceled.", cancellationToken);

    private async Task CancelAudioOperationAsync(
        string statusMessage,
        CancellationToken cancellationToken = default)
    {
        var operationCts = _audioOperationCts;
        if (operationCts is null)
            return;
        var recorder = _activeAudioRecorder;

        _audioOperationCts = null;
        operationCts.Cancel();

        try
        {
            if (_speechMode == SpeechInputMode.AudioDictation)
                await StopSpeechPassAsync(CancellationToken.None).ConfigureAwait(true);
            if (recorder?.IsRecording == true)
            {
                await recorder.CancelAsync(CancellationToken.None)
                    .ConfigureAwait(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetInputErrorMessage(DefaultAudioCaptureErrorMessage);
        }
        finally
        {
            operationCts.Dispose();
            if (ReferenceEquals(_activeAudioRecorder, recorder))
                _activeAudioRecorder = null;
            _isRecordingAudio = false;
            _isTranscribingAudio = false;
            SetInputStatusMessage(statusMessage);
            UpdateMultimodalState();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Starts or stops continuous live speech.</summary>
    public Task ToggleLiveSpeechAsync() =>
        _isLiveSpeechEnabled
            ? StopLiveSpeechAsync()
            : StartLiveSpeechAsync();

    /// <summary>Enables continuous live speech.</summary>
    public async Task StartLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        if (_isLiveSpeechEnabled || IsConversationOperationActive)
            return;

        if (_isRecordingAudio || _isTranscribingAudio)
            await CancelAudioCaptureAsync(cancellationToken).ConfigureAwait(true);

        var recognizer = SpeechRecognizer;
        if (!AllowLiveSpeech || recognizer is null || !recognizer.IsSupported)
        {
            SetInputErrorMessage("Live speech recognition is not supported on this device.");
            return;
        }

        _isLiveSpeechEnabled = true;
        _liveSpeechRestartAttempt = 0;
        _liveSpeechPrefix = Text.Trim();
        _liveSpeechCommittedTranscript = string.Empty;
        SetInputErrorMessage(null);
        UpdateMultimodalState();
        await ResumeLiveSpeechAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Disables continuous live speech and preserves the composed transcript.</summary>
    public async Task StopLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        _isLiveSpeechEnabled = false;
        _liveSpeechRestartCts?.Cancel();
        _liveSpeechRestartCts?.Dispose();
        _liveSpeechRestartCts = null;

        try
        {
            if (_speechMode == SpeechInputMode.LiveSpeech)
                await StopSpeechPassAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception)
        {
            SetInputErrorMessage(DefaultSpeechRecognitionErrorMessage);
        }

        SetInputStatusMessage("Live voice stopped.");
        UpdateMultimodalState();
        await FocusInputAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal ValueTask FocusInputAsync()
    {
        _inputEntryPart?.Focus();
        return ValueTask.CompletedTask;
    }

    internal void SetInputComposing(bool value)
    {
        _externalIsComposing = value;
        UpdateMultimodalState();
    }

    internal void SetInputStatusMessage(string? value)
    {
        SetValue(InputStatusMessagePropertyKey, string.IsNullOrWhiteSpace(value) ? null : value);
        InputContext.Refresh();
    }

    internal void SetInputErrorMessage(string? value)
    {
        SetValue(InputErrorMessagePropertyKey, string.IsNullOrWhiteSpace(value) ? null : value);
        InputContext.Refresh();
    }

    private void RefreshInputContextIfAvailable()
    {
        if (GetValue(InputContextProperty) is ChatInputContext context)
            context.Refresh();
    }

    private void InitializeMultimodalInput()
    {
        SetValue(InputContextPropertyKey, new ChatInputContext(this));
        SetDynamicResource(StopButtonStyleProperty, Themes.ChatThemeKeys.StopButtonStyle);
        SetDynamicResource(AudioButtonStyleProperty, Themes.ChatThemeKeys.AudioButtonStyle);
        SetDynamicResource(
            LiveSpeechButtonStyleProperty,
            Themes.ChatThemeKeys.LiveSpeechButtonStyle);
        UpdateMultimodalState();
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

    private void OnAudioRecorderChanged()
    {
        if (_audioOperationCts is not null)
            _ = CancelAudioCaptureAsync();
        UpdateMultimodalState();
    }

    private void OnSpeechRecognizerChanged()
    {
        if (_isLiveSpeechEnabled)
            _ = StopLiveSpeechAsync();
        else if (_speechMode == SpeechInputMode.AudioDictation)
            _ = StopSpeechPassAsync();
        _speechPermissionsGranted = false;
        UpdateMultimodalState();
    }

    private void OnComposerTextChanged()
    {
        UpdateCanSend();
        InputContext.Refresh();
    }

    private void UpdateMultimodalState()
    {
        var isConversationBusy = IsConversationOperationActive;
        var isComposing =
            _externalIsComposing
            || _attachmentReadCts is not null
            || _audioOperationCts is not null
            || _isRecordingAudio
            || _isTranscribingAudio
            || _speechStarting
            || (_isLiveSpeechEnabled && !isConversationBusy && !_speechFinalizing);
        var canStop =
            (_isSending && _sendCts is not null)
            || Conversation?.CanCancel == true;
        var audioIsActive = _isRecordingAudio || _isTranscribingAudio;
        var canToggleAudio =
            AllowAudioCapture
            && (audioIsActive
                || (!isConversationBusy
                    && !isComposing
                    && AudioRecorder?.IsSupported == true));
        var canToggleSpeech =
            AllowLiveSpeech
            && (_isLiveSpeechEnabled
                || (!isConversationBusy
                    && !isComposing
                    && SpeechRecognizer?.IsSupported == true));

        SetValue(IsComposingPropertyKey, isComposing);
        SetValue(IsInputEnabledPropertyKey, !isConversationBusy && !isComposing);
        SetValue(CanStopPropertyKey, canStop);
        SetValue(ShowSendButtonPropertyKey, !canStop);
        SetValue(IsRecordingAudioPropertyKey, _isRecordingAudio);
        SetValue(IsTranscribingAudioPropertyKey, _isTranscribingAudio);
        SetValue(IsLiveSpeechEnabledPropertyKey, _isLiveSpeechEnabled);
        SetValue(IsListeningPropertyKey, _isListening);
        SetValue(CanToggleAudioCapturePropertyKey, canToggleAudio);
        SetValue(CanToggleLiveSpeechPropertyKey, canToggleSpeech);
        SetValue(
            AudioButtonDisplayTextPropertyKey,
            audioIsActive ? AudioStopButtonText : AudioStartButtonText);
        SetValue(
            AudioButtonLabelPropertyKey,
            _isTranscribingAudio
                ? "Cancel audio transcription"
                : _isRecordingAudio
                    ? "Stop audio recording"
                    : "Record audio");
        SetValue(
            LiveSpeechButtonDisplayTextPropertyKey,
            _isLiveSpeechEnabled ? LiveSpeechStopButtonText : LiveSpeechStartButtonText);
        SetValue(
            LiveSpeechButtonLabelPropertyKey,
            _isLiveSpeechEnabled ? "Stop live voice" : "Start live voice");

        UpdateCanSend();
        InputContext.Refresh();

        if (_isLiveSpeechEnabled)
        {
            if (isConversationBusy
                && (_isListening || _speechStarting)
                && _speechStopTask is null)
                _ = PauseLiveSpeechForConversationAsync();
            else if (!isConversationBusy
                     && !_isListening
                     && !_speechStarting
                     && !_speechFinalizing
                     && _speechStopTask is null
                     && _liveSpeechRestartCts is null)
                _ = ResumeLiveSpeechAsync();
        }
    }

    private CancellationTokenSource ReplaceAudioOperation(CancellationToken cancellationToken)
    {
        var previous = _audioOperationCts;
        _audioOperationCts = null;
        previous?.Cancel();

        var next = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _audioOperationCts = next;
        return next;
    }

    private void CompleteAudioOperation(CancellationTokenSource operationCts)
    {
        if (!ReferenceEquals(_audioOperationCts, operationCts))
        {
            operationCts.Dispose();
            return;
        }

        _audioOperationCts = null;
        _activeAudioRecorder = null;
        operationCts.Dispose();
        _isRecordingAudio = false;
        _isTranscribingAudio = false;
        UpdateMultimodalState();
    }

    private async Task ResumeLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        if (!_isLiveSpeechEnabled
            || IsConversationOperationActive
            || _isListening
            || _speechStarting
            || _speechFinalizing
            || _speechStopTask is not null)
        {
            return;
        }

        _speechStarting = true;
        SetInputStatusMessage("Listening.");
        UpdateMultimodalState();

        try
        {
            await StartSpeechPassAsync(
                SpeechInputMode.LiveSpeech,
                cancellationToken).ConfigureAwait(true);
            if (_isLiveSpeechEnabled && _isListening)
                SetInputStatusMessage("Listening.");
        }
        catch (MicrophonePermissionDeniedException)
        {
            _isLiveSpeechEnabled = false;
            SetInputErrorMessage(
                "Microphone access was denied. Allow microphone access to use live voice.");
        }
        catch (FeatureNotSupportedException)
        {
            _isLiveSpeechEnabled = false;
            SetInputErrorMessage("Live speech recognition is not supported on this device.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (_isLiveSpeechEnabled)
            {
                SetInputStatusMessage(
                    "Live voice was interrupted. Reconnecting automatically.");
                ScheduleLiveSpeechRestart(countsAgainstBudget: true);
            }
        }
        finally
        {
            _speechStarting = false;
            UpdateMultimodalState();
        }
    }

    private async Task PauseLiveSpeechForConversationAsync()
    {
        if (!_isLiveSpeechEnabled || _speechMode != SpeechInputMode.LiveSpeech)
            return;

        try
        {
            await StopSpeechPassAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            _isLiveSpeechEnabled = false;
            SetInputErrorMessage(DefaultSpeechRecognitionErrorMessage);
        }

        if (_isLiveSpeechEnabled)
        {
            SetInputStatusMessage(
                "Live voice is on and will resume after the current action.");
        }
        UpdateMultimodalState();
    }

    private async Task StartSpeechPassAsync(
        SpeechInputMode mode,
        CancellationToken cancellationToken)
    {
        var recognizer = SpeechRecognizer
            ?? throw new FeatureNotSupportedException("Speech recognition is not available.");
        if (!recognizer.IsSupported)
            throw new FeatureNotSupportedException("Speech recognition is not supported.");

        await StopSpeechPassAsync().ConfigureAwait(true);

        var passCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _speechPassCts = passCts;
        _activeSpeechRecognizer = recognizer;
        _speechMode = mode;

        try
        {
            if (!_speechPermissionsGranted)
            {
                var permissionGranted = await recognizer
                    .RequestPermissionsAsync(passCts.Token)
                    .ConfigureAwait(true);
                if (!ReferenceEquals(_speechPassCts, passCts))
                    return;

                _speechPermissionsGranted = permissionGranted;
                if (!permissionGranted)
                    throw new MicrophonePermissionDeniedException();
            }

            if (!ReferenceEquals(_speechPassCts, passCts))
                return;

            EventHandler<ChatSpeechRecognitionEventArgs> handler =
                (sender, args) => OnSpeechRecognitionChanged(
                    passCts,
                    sender,
                    args);
            _activeSpeechHandler = handler;
            recognizer.RecognitionChanged += handler;
            _speechPassStarted = true;
            await recognizer.StartAsync(
                SpeechRecognitionCulture,
                reportPartialResults: true,
                passCts.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_speechPassCts, passCts))
                return;

            _isListening = true;
            UpdateMultimodalState();
        }
        catch
        {
            CompleteSpeechPass(recognizer, passCts);
            throw;
        }
    }

    private Task StopSpeechPassAsync(CancellationToken cancellationToken = default)
    {
        if (_speechStopTask is { } existing)
            return existing;

        var recognizer = _activeSpeechRecognizer;
        var passCts = _speechPassCts;
        if (recognizer is null || passCts is null)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _speechStopTask = completion.Task;
        var shouldStopRecognizer = _speechPassStarted;
        InvalidateSpeechPass(recognizer, passCts);
        if (!shouldStopRecognizer)
        {
            passCts.Dispose();
            _speechStopTask = null;
            UpdateMultimodalState();
            completion.TrySetResult();
            return completion.Task;
        }

        _ = StopSpeechPassCoreAsync(
            recognizer,
            passCts,
            cancellationToken,
            completion);
        return completion.Task;
    }

    private async Task StopSpeechPassCoreAsync(
        IChatSpeechRecognizer recognizer,
        CancellationTokenSource passCts,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await recognizer.StopAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            passCts.Dispose();
            _speechStopTask = null;
            UpdateMultimodalState();
        }

        if (failure is null)
            completion.TrySetResult();
        else
            completion.TrySetException(failure);
    }

    private void OnSpeechRecognitionChanged(
        CancellationTokenSource expectedPassCts,
        object? sender,
        ChatSpeechRecognitionEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeSpeechRecognizer)
            || !ReferenceEquals(expectedPassCts, _speechPassCts)
            || expectedPassCts.IsCancellationRequested)
            return;

        var dispatcher =
            Application.Current?.Dispatcher
            ?? Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
        if (dispatcher is { IsDispatchRequired: true })
        {
            dispatcher.Dispatch(
                () => _ = HandleSpeechRecognitionAsync(
                    expectedPassCts,
                    sender,
                    e));
            return;
        }

        _ = HandleSpeechRecognitionAsync(expectedPassCts, sender, e);
    }

    private async Task HandleSpeechRecognitionAsync(
        CancellationTokenSource expectedPassCts,
        object? sender,
        ChatSpeechRecognitionEventArgs e)
    {
        var recognizer = _activeSpeechRecognizer;
        var passCts = _speechPassCts;
        var mode = _speechMode;
        if (!ReferenceEquals(sender, recognizer)
            || recognizer is null
            || passCts is null
            || !ReferenceEquals(expectedPassCts, passCts)
            || expectedPassCts.IsCancellationRequested)
        {
            return;
        }

        SpeechRecognized?.Invoke(this, e);

        if (!e.IsFinal
            && e.ErrorKind == ChatSpeechRecognitionErrorKind.None)
        {
            if (!string.IsNullOrWhiteSpace(e.Text))
                _liveSpeechRestartAttempt = 0;
            ApplyInterimSpeech(mode, e.Text);
            return;
        }

        _speechFinalizing = true;
        CompleteSpeechPass(recognizer, passCts);
        try
        {
            if (e.ErrorKind != ChatSpeechRecognitionErrorKind.None)
            {
                HandleSpeechError(mode, e.ErrorKind);
                return;
            }

            if (mode == SpeechInputMode.AudioDictation)
            {
                if (!string.IsNullOrWhiteSpace(e.Text))
                {
                    _audioCommittedTranscript = AppendText(
                        _audioCommittedTranscript,
                        e.Text);
                    _audioHadInterimTranscript = true;
                    Text = AppendText(
                        _audioDictationPrefix,
                        _audioCommittedTranscript);
                }

                if (_isRecordingAudio && _audioOperationCts is { } audioCts)
                    ScheduleAudioSpeechRestart(audioCts);
                return;
            }

            if (mode != SpeechInputMode.LiveSpeech || !_isLiveSpeechEnabled)
                return;

            var hasFinalText = !string.IsNullOrWhiteSpace(e.Text);
            if (hasFinalText)
            {
                _liveSpeechRestartAttempt = 0;
                _liveSpeechCommittedTranscript = AppendText(
                    _liveSpeechCommittedTranscript,
                    e.Text);
                Text = AppendText(
                    _liveSpeechPrefix,
                    _liveSpeechCommittedTranscript);
            }
            else
            {
                Text = AppendText(
                    _liveSpeechPrefix,
                    _liveSpeechCommittedTranscript);
            }

            if (hasFinalText && LiveSpeechAutoSubmit && CanSend)
            {
                _speechFinalizing = true;
                UpdateMultimodalState();
                await SendAsync().ConfigureAwait(true);
                _liveSpeechPrefix = Text.Trim();
                _liveSpeechCommittedTranscript = string.Empty;
            }
            else
            {
                ScheduleLiveSpeechRestart();
            }
        }
        finally
        {
            _speechFinalizing = false;
            UpdateMultimodalState();
        }
    }

    private void ApplyInterimSpeech(SpeechInputMode mode, string text)
    {
        if (mode == SpeechInputMode.AudioDictation)
        {
            _audioHadInterimTranscript = true;
            Text = AppendText(
                _audioDictationPrefix,
                _audioCommittedTranscript,
                text);
        }
        else if (mode == SpeechInputMode.LiveSpeech && ShowInterimSpeechText)
        {
            Text = AppendText(
                _liveSpeechPrefix,
                _liveSpeechCommittedTranscript,
                text);
        }
    }

    private void HandleSpeechError(
        SpeechInputMode mode,
        ChatSpeechRecognitionErrorKind errorKind)
    {
        if (mode == SpeechInputMode.AudioDictation)
        {
            if (_isRecordingAudio)
            {
                SetInputStatusMessage(
                    "Recording audio. Live transcription is unavailable.");
            }
            return;
        }

        if (!_isLiveSpeechEnabled)
            return;

        switch (errorKind)
        {
            case ChatSpeechRecognitionErrorKind.NoSpeech:
            case ChatSpeechRecognitionErrorKind.Aborted:
                ScheduleLiveSpeechRestart(countsAgainstBudget: false);
                break;
            case ChatSpeechRecognitionErrorKind.Transient:
                SetInputStatusMessage(
                    "Live voice was interrupted. Reconnecting automatically.");
                ScheduleLiveSpeechRestart(countsAgainstBudget: true);
                break;
            case ChatSpeechRecognitionErrorKind.PermissionDenied:
                _isLiveSpeechEnabled = false;
                SetInputErrorMessage(
                    "Microphone access was denied. Allow microphone access to use live voice.");
                break;
            case ChatSpeechRecognitionErrorKind.LanguageNotSupported:
                _isLiveSpeechEnabled = false;
                SetInputErrorMessage(
                    "The selected live voice language is not supported on this device.");
                break;
            default:
                _isLiveSpeechEnabled = false;
                SetInputErrorMessage(
                    "Live voice could not continue because speech recognition is not configured correctly.");
                break;
        }
    }

    private void ScheduleLiveSpeechRestart(bool countsAgainstBudget = false)
    {
        if (!_isLiveSpeechEnabled)
            return;

        if (countsAgainstBudget)
        {
            _liveSpeechRestartAttempt++;
            if (_liveSpeechRestartAttempt > 3)
            {
                _isLiveSpeechEnabled = false;
                SetInputErrorMessage(DefaultSpeechRecognitionErrorMessage);
                UpdateMultimodalState();
                return;
            }
        }

        _liveSpeechRestartCts?.Cancel();
        _liveSpeechRestartCts?.Dispose();
        var restartCts = new CancellationTokenSource();
        _liveSpeechRestartCts = restartCts;
        var delay = countsAgainstBudget
            ? TimeSpan.FromMilliseconds(
                250 * Math.Pow(2, _liveSpeechRestartAttempt - 1))
            : TimeSpan.FromMilliseconds(250);
        _ = RestartAsync(restartCts, delay);

        async Task RestartAsync(
            CancellationTokenSource expected,
            TimeSpan restartDelay)
        {
            try
            {
                await Task.Delay(restartDelay, expected.Token).ConfigureAwait(true);
                if (ReferenceEquals(_liveSpeechRestartCts, expected)
                    && _isLiveSpeechEnabled)
                {
                    _liveSpeechRestartCts = null;
                    await ResumeLiveSpeechAsync(expected.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                expected.Dispose();
            }
        }
    }

    private void ScheduleAudioSpeechRestart(CancellationTokenSource audioCts)
    {
        _ = RestartAsync();

        async Task RestartAsync()
        {
            try
            {
                await Task.Delay(250, audioCts.Token).ConfigureAwait(true);
                if (ReferenceEquals(_audioOperationCts, audioCts)
                    && _isRecordingAudio)
                {
                    await StartSpeechPassAsync(
                        SpeechInputMode.AudioDictation,
                        audioCts.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (ReferenceEquals(_audioOperationCts, audioCts)
                    && _isRecordingAudio)
                {
                    SetInputStatusMessage(
                        "Recording audio. Live transcription is unavailable.");
                }
            }
        }
    }

    private void CompleteSpeechPass(
        IChatSpeechRecognizer recognizer,
        CancellationTokenSource passCts)
    {
        if (!InvalidateSpeechPass(recognizer, passCts))
            return;

        passCts.Dispose();
    }

    private bool InvalidateSpeechPass(
        IChatSpeechRecognizer recognizer,
        CancellationTokenSource passCts)
    {
        if (!ReferenceEquals(_speechPassCts, passCts))
            return false;

        if (_activeSpeechHandler is { } handler)
            recognizer.RecognitionChanged -= handler;
        _speechPassCts = null;
        _activeSpeechRecognizer = null;
        _activeSpeechHandler = null;
        _speechMode = SpeechInputMode.None;
        _isListening = false;
        _speechPassStarted = false;
        passCts.Cancel();
        UpdateMultimodalState();
        return true;
    }

    private static string AppendText(params string?[] values) =>
        string.Join(
            " ",
            values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim()));

    private static string FormatMegabytes(long bytes) =>
        bytes % (1024L * 1024) == 0
            ? (bytes / (1024L * 1024)).ToString(CultureInfo.InvariantCulture)
            : (bytes / (double)(1024L * 1024)).ToString("0.#", CultureInfo.InvariantCulture);

    private bool IsConversationOperationActive =>
        _isSending
        || Conversation?.Status is ChatConversationStatus.Busy or ChatConversationStatus.AwaitingInput;

    private enum SpeechInputMode
    {
        None,
        AudioDictation,
        LiveSpeech,
    }
}
