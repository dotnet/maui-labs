using System.Collections.ObjectModel;
using System.Globalization;

namespace Microsoft.Maui.Chat;

/// <summary>
/// Shared, renderer-neutral state and operation controller for a chat composer.
/// </summary>
/// <remarks>
/// <para>
/// The controller is UI-thread affine. It keeps a draft for each bound conversation and uses an
/// operation generation for every asynchronous continuation, so a completion from a replaced
/// conversation or a disposed controller cannot write into the current draft.
/// </para>
/// <para>
/// Renderers may bind directly to this class, or expose a compatibility adapter. Services are
/// intentionally optional: a host can offer text-only chat without bringing a file picker,
/// recorder, or speech recognizer into its dependency graph.
/// </para>
/// </remarks>
public sealed class ChatComposerController : INotifyPropertyChanged, IDisposable
{
    /// <summary>The generic, user-safe message shown when sending fails.</summary>
    public const string DefaultSendErrorMessage = "Your message could not be sent. Please try again.";

    /// <summary>The generic, user-safe message shown when adding an attachment fails.</summary>
    public const string DefaultAttachmentErrorMessage = "That attachment could not be added.";

    /// <summary>The generic, user-safe message shown when recording fails.</summary>
    public const string DefaultAudioCaptureErrorMessage = "The audio recording could not be completed.";

    /// <summary>The generic, user-safe message shown when speech recognition fails.</summary>
    public const string DefaultSpeechRecognitionErrorMessage = "Live voice could not continue.";

    private readonly ObservableCollection<ChatAttachment> _attachments = [];
    private readonly ReadOnlyObservableCollection<ChatAttachment> _readOnlyAttachments;
    private readonly Dictionary<ChatConversation, DraftState> _drafts = [];
    private IDisposable? _conversationSubscription;
    private CancellationTokenSource? _sendCts;
    private CancellationTokenSource? _pickCts;
    private CancellationTokenSource? _audioCts;
    private CancellationTokenSource? _speechCts;
    private CancellationTokenSource? _liveSpeechRestartCts;
    private EventHandler<ChatSpeechRecognitionEventArgs>? _speechHandler;
    private EventHandler<ChatSpeechRecognitionEventArgs>? _audioSpeechHandler;
    private ChatConversation? _conversation;
    private string _text = string.Empty;
    private string? _statusMessage;
    private string? _errorMessage;
    private int _generation;
    private int _audioOperationId;
    private int _audioSpeechOperationId;
    private int _speechOperationId;
    private Task? _audioCleanupTask;
    private Task? _speechCleanupTask;
    private Task? _audioSpeechCleanupTask;
    private IChatAudioRecorder? _activeRecorder;
    private IChatSpeechRecognizer? _activeRecognizer;
    private IChatSpeechRecognizer? _audioSpeechRecognizer;
    private bool _audioSpeechStarted;
    private bool _audioHadInterimTranscript;
    private bool _isSending;
    private bool _isRecordingAudio;
    private bool _isTranscribingAudio;
    private bool _isListening;
    private bool _isAudioStarting;
    private bool _isLiveSpeechEnabled;
    private bool _isSpeechStarting;
    private bool _isSpeechStopping;
    private bool _isComposingOverride;
    private bool _isDisposed;
    private int _liveSpeechRestartAttempt;
    private string _speechPrefix = string.Empty;
    private string _speechCommittedText = string.Empty;
    private string _audioDictationPrefix = string.Empty;
    private string _audioCommittedTranscript = string.Empty;

    /// <summary>Creates an empty controller.</summary>
    public ChatComposerController()
    {
        _readOnlyAttachments = new ReadOnlyObservableCollection<ChatAttachment>(_attachments);
    }

    /// <summary>Raised synchronously after observable composer state changes.</summary>
    public event Action? Changed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after an audio recording has been captured.</summary>
    public event EventHandler<ChatAudioRecordedEventArgs>? AudioRecorded;

    /// <summary>Raised after an audio recording has been transcribed.</summary>
    public event EventHandler<ChatAudioTranscribedEventArgs>? AudioTranscribed;

    /// <summary>Raised when the live speech recognizer reports a result.</summary>
    public event EventHandler<ChatSpeechRecognitionEventArgs>? SpeechRecognized;

    /// <summary>Gets or sets the conversation whose draft is currently bound.</summary>
    public ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_conversation, value))
                return;

            // A renderer may collect a draft before its conversation binding arrives.
            // Adopt that unbound draft on the first bind instead of silently discarding it.
            if (_conversation is null && value is not null && !_drafts.ContainsKey(value) &&
                (!string.IsNullOrEmpty(_text) || _attachments.Count > 0))
            {
                _drafts[value] = new DraftState(_text, [.. _attachments]);
            }
            SaveCurrentDraft();
            CancelOperations();
            _conversationSubscription?.Dispose();
            _conversationSubscription = null;
            _conversation = value;
            _generation++;
            _statusMessage = null;
            _errorMessage = null;
            RestoreDraft(value);
            if (value is not null)
                _conversationSubscription = value.Subscribe(_ => NotifyChanged());
            NotifyChanged();
        }
    }

    /// <summary>Gets or sets the current draft text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            ThrowIfDisposed();
            var next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal))
                return;
            _text = next;
            SaveCurrentDraft();
            NotifyChanged();
        }
    }

    /// <summary>Gets the attachments staged for the current conversation.</summary>
    public ReadOnlyObservableCollection<ChatAttachment> Attachments => _readOnlyAttachments;

    /// <summary>Gets or sets whether attachment picking is available.</summary>
    public bool AllowAttachments
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets whether audio capture is available.</summary>
    public bool AllowAudioCapture
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets whether live speech is available.</summary>
    public bool AllowLiveSpeech
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets the optional attachment picker.</summary>
    public IChatAttachmentPicker? AttachmentPicker
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets an optional renderer-defined filter passed to <see cref="AttachmentPicker"/>.</summary>
    public object? AttachmentFileTypes
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets the optional audio recorder.</summary>
    public IChatAudioRecorder? AudioRecorder
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;
            field = value;
            if (_isAudioStarting && _audioCts is null)
            {
                _audioOperationId++;
                _isAudioStarting = false;
            }
            _ = CancelAudioCaptureAsync();
            NotifyChanged();
        }
    }

    /// <summary>Gets or sets the optional audio transcriber.</summary>
    public IChatAudioTranscriber? AudioTranscriber
    {
        get;
        set => SetOption(ref field, value);
    }

    /// <summary>Gets or sets the optional live-speech recognizer.</summary>
    public IChatSpeechRecognizer? SpeechRecognizer
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;
            var previous = field;
            field = value;
            if (ReferenceEquals(_audioSpeechRecognizer, previous))
                _ = ObserveSpeechCleanupAsync(
                    StopAudioDictationAsync(CancellationToken.None));
            if (ReferenceEquals(_activeRecognizer, previous) ||
                _speechCts is not null ||
                _isSpeechStarting ||
                _isLiveSpeechEnabled)
            {
                _ = StopLiveSpeechAsync();
            }
            NotifyChanged();
        }
    }

    /// <summary>Gets or sets the accepted maximum attachment size. Defaults to 10 MB.</summary>
    public long MaximumAttachmentBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets the accepted maximum captured-audio size. Defaults to 10 MB.</summary>
    public long MaximumAudioBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets the maximum staged attachment count. Defaults to 10.</summary>
    public int MaximumAttachmentCount { get; set; } = 10;

    /// <summary>Gets or sets the maximum staged attachment bytes. Defaults to 50 MB.</summary>
    public long MaximumTotalAttachmentBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Gets or sets whether completed recordings are staged. Defaults to <see langword="true"/>.</summary>
    public bool AttachAudioRecording { get; set; } = true;

    /// <summary>Gets or sets whether a new recording replaces staged <c>audio/*</c> attachments.</summary>
    public bool ReplaceExistingAudio { get; set; } = true;

    /// <summary>Gets or sets whether partial speech is shown while audio is recording.</summary>
    public bool ShowInterimAudioTranscript { get; set; }

    /// <summary>Gets or sets whether partial live-speech text is shown in the draft.</summary>
    public bool ShowInterimSpeechText { get; set; } = true;

    /// <summary>Gets or sets whether finalized live speech sends immediately. Defaults to <see langword="true"/>.</summary>
    public bool LiveSpeechAutoSubmit { get; set; } = true;

    /// <summary>
    /// Gets or sets whether live speech remains armed across finalized utterances and transient failures.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Renderers that present live speech as a single pass set this to <see langword="false"/>.
    /// The controller still owns all microphone release and restart sequencing in either mode.
    /// </remarks>
    public bool ContinuousLiveSpeech { get; set; } = true;

    /// <summary>Gets or sets the culture supplied to the speech recognizer.</summary>
    public CultureInfo SpeechRecognitionCulture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Gets the current conversation status.</summary>
    public ChatConversationStatus Status => Conversation?.Status ?? ChatConversationStatus.Idle;

    /// <summary>Gets whether the conversation is busy or awaiting local input.</summary>
    public bool IsConversationBusy => Status is ChatConversationStatus.Busy or ChatConversationStatus.AwaitingInput;

    /// <summary>Gets whether a send is active.</summary>
    public bool IsSending => _isSending;

    /// <summary>Gets whether audio is being recorded.</summary>
    public bool IsRecordingAudio => _isRecordingAudio;

    /// <summary>Gets whether completed audio is being processed.</summary>
    public bool IsTranscribingAudio => _isTranscribingAudio;

    /// <summary>Gets whether audio capture startup is in progress.</summary>
    public bool IsAudioStarting => _isAudioStarting;

    /// <summary>Gets whether live speech has been enabled for the current recognition pass.</summary>
    public bool IsLiveSpeechEnabled => _isLiveSpeechEnabled;

    /// <summary>Gets whether live speech is listening.</summary>
    public bool IsListening => _isListening;

    /// <summary>Gets whether live-speech startup is in progress.</summary>
    public bool IsSpeechStarting => _isSpeechStarting;

    /// <summary>Gets whether live-speech shutdown is in progress.</summary>
    public bool IsSpeechStopping => _isSpeechStopping;

    /// <summary>Gets whether a local composing operation is active.</summary>
    public bool IsComposing =>
        _isRecordingAudio || _isTranscribingAudio || _isAudioStarting ||
        _isListening || _isSpeechStarting || _isSpeechStopping ||
        _isComposingOverride || _pickCts is not null;

    /// <summary>Gets whether the current draft can be submitted.</summary>
    public bool CanSubmit => !_isDisposed && !_isSending && !IsComposing && Conversation is { } conversation && conversation.CanSend(CreateDraft());

    /// <summary>Gets whether the current send can be stopped.</summary>
    public bool CanStop => !_isDisposed && !_isRecordingAudio && !_isTranscribingAudio && (_isSending || Conversation?.CanCancel == true);

    /// <summary>Gets whether attachments can be picked.</summary>
    public bool CanPickAttachments =>
        !_isDisposed && AllowAttachments && !IsConversationBusy && !IsComposing;

    /// <summary>Gets whether the audio button can be used.</summary>
    public bool CanToggleAudioCapture =>
        !_isDisposed && AllowAudioCapture && AudioRecorder is { IsSupported: true } &&
        !_isAudioStarting && !_isTranscribingAudio &&
        (_isRecordingAudio ||
            (!IsConversationBusy && !IsNonModalCompositionActive && !IsSpeechActive &&
                _audioCleanupTask is null && _speechCleanupTask is null &&
                _audioSpeechCleanupTask is null));

    /// <summary>Gets whether the speech button can be used.</summary>
    public bool CanToggleLiveSpeech =>
        !_isDisposed && AllowLiveSpeech && SpeechRecognizer is { IsSupported: true } &&
        !_isSpeechStarting && !_isSpeechStopping &&
        (_isLiveSpeechEnabled || _isListening ||
            (!IsConversationBusy && !IsNonModalCompositionActive && !IsAudioActive &&
                _audioCleanupTask is null && _speechCleanupTask is null &&
                _audioSpeechCleanupTask is null));

    /// <summary>Gets whether audio owns the microphone, including transient startup and processing states.</summary>
    public bool IsAudioActive => _isAudioStarting || _isRecordingAudio || _isTranscribingAudio;

    /// <summary>Gets whether live speech owns the microphone, including transient startup and stop states.</summary>
    public bool IsSpeechActive => _isSpeechStarting || _isSpeechStopping || _isLiveSpeechEnabled || _isListening;

    private bool IsNonModalCompositionActive =>
        _pickCts is not null || _isComposingOverride;

    /// <summary>Gets a user-safe status message.</summary>
    public string? StatusMessage => _statusMessage;

    /// <summary>Gets a user-safe error message.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>Creates the draft the controller would send now.</summary>
    public ChatDraft CreateDraft() => new(_text, _attachments);

    /// <summary>Stages an attachment in the current draft.</summary>
    public void AddAttachment(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ThrowIfDisposed();
        _attachments.Add(attachment);
        SaveCurrentDraft();
        NotifyChanged();
    }

    /// <summary>Removes an attachment from the current draft.</summary>
    public bool RemoveAttachment(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ThrowIfDisposed();
        var removed = _attachments.Remove(attachment);
        if (removed)
        {
            SaveCurrentDraft();
            NotifyChanged();
        }
        return removed;
    }

    /// <summary>Submits the current draft. Expected failures are exposed through <see cref="ErrorMessage"/>.</summary>
    public async Task SubmitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!CanSubmit || Conversation is not { } conversation)
            return;

        var draft = CreateDraft();
        var generation = _generation;
        var messageCount = conversation.Messages.Count;
        var source = Replace(ref _sendCts, cancellationToken);
        _isSending = true;
        _errorMessage = null;
        NotifyChanged();
        try
        {
            var accepted = await conversation.SendAsync(draft, source.Token).ConfigureAwait(true);
            if (IsCurrent(generation, conversation, source) && accepted)
                ClearAcceptedDraft(draft);
        }
        catch (OperationCanceledException)
        {
            // Some conversations (notably the AI bridge) accept and append the outgoing
            // message before their response stream observes cancellation. That accepted
            // draft must not be restored merely because the response was stopped.
            if (IsCurrent(generation, conversation, source) &&
                conversation.Messages.Count > messageCount)
            {
                ClearAcceptedDraft(draft);
            }
        }
        catch
        {
            if (IsCurrent(generation, conversation, source))
                _errorMessage = DefaultSendErrorMessage;
        }
        finally
        {
            if (IsCurrent(generation, conversation, source))
            {
                _isSending = false;
                _sendCts = null;
                NotifyChanged();
            }
            source.Dispose();
        }
    }

    /// <summary>Cancels the active send and asks the conversation to stop.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return;
        _sendCts?.Cancel();
        if (Conversation is { CanCancel: true } conversation)
        {
            try { await conversation.CancelAsync(cancellationToken).ConfigureAwait(true); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { if (!_isDisposed) _errorMessage = DefaultSendErrorMessage; }
        }
        if (!_isDisposed && !cancellationToken.IsCancellationRequested)
            _statusMessage = "Response stopped.";
        NotifyChanged();
    }

    /// <summary>Picks and stages attachments, applying the configured limits.</summary>
    public async Task PickAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // AllowAttachments controls whether a renderer offers the picker. Preserve the public
        // programmatic operation for callers that deliberately invoke it while the button is hidden.
        if (IsConversationBusy || IsComposing || AttachmentPicker is not { } picker)
            return;

        var conversation = Conversation;
        var generation = _generation;
        var source = Replace(ref _pickCts, cancellationToken);
        _errorMessage = null;
        NotifyChanged();
        try
        {
            var picked = await picker.PickAsync(AttachmentFileTypes, MaximumAttachmentBytes, source.Token).ConfigureAwait(true);
            if (!IsCurrent(generation, conversation, source) || picked is null)
                return;
            var additions = picked.Where(static attachment => attachment is not null).ToArray();
            if (_attachments.Count + additions.Length > MaximumAttachmentCount ||
                _attachments.Sum(static attachment => attachment.ByteCount) + additions.Sum(static attachment => attachment.ByteCount) > MaximumTotalAttachmentBytes)
            {
                _errorMessage = DefaultAttachmentErrorMessage;
                return;
            }
            foreach (var attachment in additions)
                _attachments.Add(attachment);
            SaveCurrentDraft();
        }
        catch (OperationCanceledException) { }
        catch { if (IsCurrent(generation, conversation, source)) _errorMessage = DefaultAttachmentErrorMessage; }
        finally
        {
            if (IsCurrent(generation, conversation, source))
            {
                _pickCts = null;
                NotifyChanged();
            }
            source.Dispose();
        }
    }

    /// <summary>Starts, stops, or cancels audio capture. Audio and speech never hold the microphone concurrently.</summary>
    public Task ToggleAudioCaptureAsync(CancellationToken cancellationToken = default) =>
        _isAudioStarting
            ? Task.CompletedTask
            : _isTranscribingAudio
            ? CancelAudioCaptureAsync(cancellationToken)
            : _audioCleanupTask is not null
                ? Task.CompletedTask
            : _isRecordingAudio
                ? StopAudioCaptureAsync(cancellationToken)
                : StartAudioCaptureAsync(cancellationToken);

    /// <summary>Starts audio capture.</summary>
    public async Task StartAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!AllowAudioCapture || AudioRecorder is not { IsSupported: true } recorder ||
            _isAudioStarting || _isRecordingAudio || _isTranscribingAudio ||
            IsConversationBusy || IsNonModalCompositionActive)
            return;

        await AwaitMicrophoneCleanupAsync().ConfigureAwait(true);
        ThrowIfDisposed();
        if (!AllowAudioCapture || !ReferenceEquals(AudioRecorder, recorder) ||
            _isAudioStarting || _isRecordingAudio || _isTranscribingAudio ||
            IsConversationBusy || IsNonModalCompositionActive)
        {
            return;
        }

        var conversation = Conversation;
        var generation = _generation;
        var operationId = ++_audioOperationId;
        _isAudioStarting = true; // Claim the microphone before awaiting speech cleanup.
        await EnsureSpeechStoppedAsync().ConfigureAwait(true);
        if (!IsAudioCurrent(generation, conversation, operationId) ||
            !ReferenceEquals(AudioRecorder, recorder))
            return;

        var source = Replace(ref _audioCts, cancellationToken);
        _activeRecorder = recorder;
        _errorMessage = null;
        _audioDictationPrefix = _text.Trim();
        _audioCommittedTranscript = string.Empty;
        _audioHadInterimTranscript = false;
        _statusMessage = "Recording audio.";
        NotifyChanged();
        try
        {
            await recorder.StartAsync(source.Token).ConfigureAwait(true);
            if (IsAudioCurrent(generation, conversation, operationId) &&
                ReferenceEquals(source, _audioCts))
            {
                _isRecordingAudio = true;
                _isAudioStarting = false;
                _statusMessage = "Recording audio.";
                if (ShowInterimAudioTranscript && SpeechRecognizer is { IsSupported: true })
                    await StartSpeechAsync(SpeechMode.AudioDictation, source.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch
        {
            if (IsAudioCurrent(generation, conversation, operationId))
                _errorMessage = DefaultAudioCaptureErrorMessage;
        }
        finally
        {
            if (IsAudioCurrent(generation, conversation, operationId))
            {
                _isAudioStarting = false;
                NotifyChanged();
            }
            if (!_isRecordingAudio && ReferenceEquals(_audioCts, source))
                Release(ref _audioCts, source);
        }
    }

    /// <summary>Stops audio capture and stages the completed recording.</summary>
    public async Task StopAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || !_isRecordingAudio || _activeRecorder is not { } recorder || _audioCts is not { } source)
            return;
        var conversation = Conversation;
        var generation = _generation;
        var operationId = _audioOperationId;
        _isRecordingAudio = false;
        _isTranscribingAudio = true;
        _statusMessage = "Processing audio…";
        NotifyChanged();
        var lifecycle = StopAudioCoreAsync();
        _audioCleanupTask = lifecycle;
        try { await lifecycle.ConfigureAwait(true); }
        finally
        {
            if (ReferenceEquals(_audioCleanupTask, lifecycle))
                _audioCleanupTask = null;
            if (ReferenceEquals(_activeRecorder, recorder))
                _activeRecorder = null;
        }

        async Task StopAudioCoreAsync()
        {
            try
            {
                if (_audioSpeechRecognizer is not null)
                {
                    try
                    {
                        await StopAudioDictationAsync(CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                    catch
                    {
                        if (IsAudioCurrent(generation, conversation, operationId))
                            _errorMessage = DefaultSpeechRecognitionErrorMessage;
                    }
                }
                if (_isListening)
                    await StopLiveSpeechAsync(CancellationToken.None).ConfigureAwait(true);
                var recording = await recorder.StopAsync(MaximumAudioBytes, source.Token).ConfigureAwait(true);
                if (!IsAudioCurrent(generation, conversation, operationId))
                    return;
                if (recording is null)
                {
                    _statusMessage = null;
                    _errorMessage =
                        "The device did not capture audio. Record for at least one second and check the microphone input level.";
                    return;
                }
                if (recording.ByteCount > MaximumAudioBytes)
                {
                    _statusMessage = null;
                    _errorMessage = $"Audio recordings must be {FormatMegabytes(MaximumAudioBytes)} MB or smaller.";
                    return;
                }
                if (AttachAudioRecording)
                {
                    if (ReplaceExistingAudio)
                        foreach (var attachment in _attachments.Where(static a => a.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)).ToArray())
                            _attachments.Remove(attachment);
                    _attachments.Add(recording);
                    SaveCurrentDraft();
                    AudioRecorded?.Invoke(this, new ChatAudioRecordedEventArgs(recording));
                }
                if (AudioTranscriber is { } transcriber)
                {
                    _statusMessage = "Transcribing audio.";
                    NotifyChanged();
                    var text = await transcriber.TranscribeAsync(recording, source.Token).ConfigureAwait(true);
                    if (!IsAudioCurrent(generation, conversation, operationId))
                        return;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _errorMessage = "No speech was recognized in the recording.";
                        return;
                    }
                    var normalized = text.Trim();
                    Text = _audioHadInterimTranscript
                        ? AppendText(_audioDictationPrefix, normalized)
                        : AppendText(_text, normalized);
                    AudioTranscribed?.Invoke(this, new ChatAudioTranscribedEventArgs(recording, normalized));
                    _statusMessage = "Voice transcription ready.";
                }
                else if (AttachAudioRecording)
                    _statusMessage = "Audio recording attached.";
                else
                    _errorMessage = "An audio transcriber is required when recordings are not attached.";
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            catch { if (IsAudioCurrent(generation, conversation, operationId)) _errorMessage = DefaultAudioCaptureErrorMessage; }
            finally
            {
                if (IsAudioCurrent(generation, conversation, operationId))
                {
                    _isTranscribingAudio = false;
                    Release(ref _audioCts, source);
                    NotifyChanged();
                }
            }
        }
    }

    /// <summary>Cancels the active audio recording or transcription without staging its result.</summary>
    public async Task CancelAudioCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || _audioCts is not { } source)
            return;
        ++_audioOperationId;
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var cleanup = _audioCleanupTask;
        source.Cancel();
        Exception? cleanupFailure = null;
        try
        {
            try
            {
                await StopAudioDictationAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            // A cleanup task can include a transcriber that deliberately ignores cancellation.
            // It no longer owns the microphone after the CTS is cancelled, so waiting for it
            // would make the composer impossible to cancel and would block a new draft.
            if (cleanup is null && recorder?.IsRecording == true)
                await recorder.CancelAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
        if (cleanupFailure is not null && !_isDisposed)
            _errorMessage = DefaultSpeechRecognitionErrorMessage;
        _isRecordingAudio = false;
        _isTranscribingAudio = false;
        _isAudioStarting = false;
        _statusMessage = "Audio transcription canceled.";
        if (ReferenceEquals(_audioCts, source))
            Release(ref _audioCts, source);
        NotifyChanged();
    }

    /// <summary>Starts or stops live speech recognition.</summary>
    public Task ToggleLiveSpeechAsync(CancellationToken cancellationToken = default) =>
        _isSpeechStarting || _isSpeechStopping
            ? Task.CompletedTask
            : (_isLiveSpeechEnabled || _isListening)
                ? StopLiveSpeechAsync(cancellationToken)
                : StartLiveSpeechAsync(cancellationToken);

    /// <summary>Starts a live-speech recognition pass.</summary>
    public async Task StartLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_isLiveSpeechEnabled || _isSpeechStarting || _isSpeechStopping ||
            SpeechRecognizer is not { IsSupported: true } recognizer ||
            !AllowLiveSpeech || IsConversationBusy || IsNonModalCompositionActive)
            return;

        await AwaitMicrophoneCleanupAsync().ConfigureAwait(true);
        ThrowIfDisposed();
        if (_isLiveSpeechEnabled || _isSpeechStarting || _isSpeechStopping ||
            !ReferenceEquals(SpeechRecognizer, recognizer) ||
            !AllowLiveSpeech || IsConversationBusy || IsNonModalCompositionActive)
        {
            return;
        }

        var conversation = Conversation;
        var generation = _generation;
        var operationId = ++_speechOperationId;
        var source = Replace(ref _speechCts, cancellationToken);
        _errorMessage = null;
        _statusMessage = "Listening.";
        _isSpeechStarting = true;
        _isLiveSpeechEnabled = true;
        _speechPrefix = _text.Trim();
        _speechCommittedText = string.Empty;
        NotifyChanged();
        try
        {
            var granted = await recognizer.RequestPermissionsAsync(source.Token).ConfigureAwait(true);
            if (!IsSpeechCurrent(generation, conversation, operationId) ||
                !ReferenceEquals(source, _speechCts))
                return;
            if (!granted)
            {
                _errorMessage = "Microphone access was denied.";
                _isLiveSpeechEnabled = false;
                return;
            }
            await EnsureAudioStoppedAsync().ConfigureAwait(true);
            if (!IsSpeechCurrent(generation, conversation, operationId) ||
                !ReferenceEquals(source, _speechCts))
                return;
            _activeRecognizer = recognizer;
            _speechHandler = (_, e) => OnRecognitionChanged(generation, conversation, source, operationId, e);
            recognizer.RecognitionChanged += _speechHandler;
            await recognizer.StartAsync(SpeechRecognitionCulture, reportPartialResults: true, source.Token).ConfigureAwait(true);
            if (IsSpeechCurrent(generation, conversation, operationId) &&
                ReferenceEquals(source, _speechCts) &&
                !source.IsCancellationRequested)
            {
                _isListening = true;
                _isSpeechStarting = false;
                _statusMessage = "Listening.";
                NotifyChanged();
            }
            else if (IsSpeechCurrent(generation, conversation, operationId) &&
                source.IsCancellationRequested)
            {
                DetachFailedSpeechStart(recognizer, source);
                _isLiveSpeechEnabled = false;
                _statusMessage = null;
                await StopCapturedRecognizerAsync(recognizer).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (IsSpeechCurrent(generation, conversation, operationId))
            {
                DetachFailedSpeechStart(recognizer, source);
                _isLiveSpeechEnabled = false;
                _statusMessage = null;
            }
        }
        catch
        {
            if (IsSpeechCurrent(generation, conversation, operationId))
            {
                DetachFailedSpeechStart(recognizer, source);
                if (ContinuousLiveSpeech)
                    ScheduleLiveSpeechRestart(generation, conversation, operationId);
                else
                {
                    _isLiveSpeechEnabled = false;
                    _errorMessage = DefaultSpeechRecognitionErrorMessage;
                }
            }
        }
        finally
        {
            if (IsSpeechCurrent(generation, conversation, operationId) &&
                ReferenceEquals(source, _speechCts))
            {
                _isSpeechStarting = false;
                if (!_isListening && !_isLiveSpeechEnabled)
                    Release(ref _speechCts, source);
            }
            NotifyChanged();
        }
    }

    /// <summary>Stops live speech recognition and releases microphone ownership.</summary>
    public async Task StopLiveSpeechAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || (!_isListening && !_isLiveSpeechEnabled && !_isSpeechStarting))
            return;
        ++_speechOperationId;
        _liveSpeechRestartCts?.Cancel();
        _liveSpeechRestartCts?.Dispose();
        _liveSpeechRestartCts = null;
        var source = _speechCts;
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        if (_speechHandler is not null && recognizer is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        _isListening = false;
        _isLiveSpeechEnabled = false;
        _isSpeechStopping = true;
        source?.Cancel();
        NotifyChanged();
        if (recognizer is null)
        {
            _isSpeechStarting = false;
            _isSpeechStopping = false;
            if (source is not null)
                Release(ref _speechCts, source);
            _statusMessage = "Live voice stopped.";
            NotifyChanged();
            return;
        }
        var cleanup = StopSpeechCoreAsync();
        _speechCleanupTask = cleanup;
        try { await cleanup.ConfigureAwait(true); }
        finally
        {
            if (ReferenceEquals(_speechCleanupTask, cleanup))
                _speechCleanupTask = null;
        }

        async Task StopSpeechCoreAsync()
        {
            try { await recognizer.StopAsync(cancellationToken).ConfigureAwait(true); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { if (!_isDisposed) _errorMessage = DefaultSpeechRecognitionErrorMessage; }
            finally
            {
                if (source is not null)
                    Release(ref _speechCts, source);
                if (!_isDisposed)
                {
                    _isSpeechStopping = false;
                    _statusMessage = "Live voice stopped.";
                    NotifyChanged();
                }
            }
        }
    }

    /// <summary>Sets a user-safe status message.</summary>
    public void SetStatusMessage(string? value) { _statusMessage = value; NotifyChanged(); }

    /// <summary>Sets a user-safe error message.</summary>
    public void SetErrorMessage(string? value) { _errorMessage = value; NotifyChanged(); }

    /// <summary>Sets whether a renderer-owned asynchronous composition operation is active.</summary>
    public void SetComposing(bool value) { _isComposingOverride = value; NotifyChanged(); }

    /// <summary>Forces renderer bindings to refresh after a conversation-owned state change.</summary>
    public void NotifyChanged() => NotifyChangedCore();

    /// <summary>Disposes subscriptions and invalidates all in-flight operations.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _generation++;
        CancelOperations();
        _conversationSubscription?.Dispose();
        _conversationSubscription = null;
        if (_speechHandler is not null && SpeechRecognizer is { } recognizer)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        Changed = null;
    }

    private void OnRecognitionChanged(
        int generation,
        ChatConversation? conversation,
        CancellationTokenSource source,
        int operationId,
        ChatSpeechRecognitionEventArgs e)
    {
        if (!IsSpeechCurrent(generation, conversation, operationId) ||
            !ReferenceEquals(source, _speechCts) || source.IsCancellationRequested)
            return;
        SpeechRecognized?.Invoke(this, e);
        if (!IsSpeechCurrent(generation, conversation, operationId) ||
            !ReferenceEquals(source, _speechCts) || source.IsCancellationRequested)
        {
            return;
        }

        if (e.ErrorKind is not ChatSpeechRecognitionErrorKind.None)
        {
            DetachCompletedSpeechPass(source);
            if (ContinuousLiveSpeech)
            {
                if (e.ErrorKind == ChatSpeechRecognitionErrorKind.Transient)
                    _statusMessage = "Live voice was interrupted. Reconnecting automatically.";
                ScheduleLiveSpeechRestart(
                    generation,
                    conversation,
                    operationId,
                    countsAgainstBudget: e.ErrorKind == ChatSpeechRecognitionErrorKind.Transient);
                NotifyChanged();
                return;
            }

            _isLiveSpeechEnabled = false;
            switch (e.ErrorKind)
            {
                case ChatSpeechRecognitionErrorKind.NoSpeech:
                case ChatSpeechRecognitionErrorKind.Aborted:
                    _statusMessage = null;
                    break;
                case ChatSpeechRecognitionErrorKind.Transient:
                    _statusMessage = "Live voice was interrupted.";
                    break;
                default:
                    _errorMessage = DefaultSpeechRecognitionErrorMessage;
                    break;
            }
            NotifyChanged();
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Text))
            _liveSpeechRestartAttempt = 0;

        if (!e.IsFinal)
        {
            if (!string.IsNullOrWhiteSpace(e.Text) && ShowInterimSpeechText)
                Text = AppendText(_speechPrefix, _speechCommittedText, e.Text);
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Text))
            _speechCommittedText = AppendText(_speechCommittedText, e.Text);
        Text = AppendText(_speechPrefix, _speechCommittedText, null);

        DetachCompletedSpeechPass(source);
        if (ContinuousLiveSpeech)
        {
            if (LiveSpeechAutoSubmit && !string.IsNullOrWhiteSpace(e.Text))
                _ = SubmitAndResumeSpeechAsync(generation, conversation, operationId);
            else
                ScheduleLiveSpeechRestart(generation, conversation, operationId, countsAgainstBudget: false);
            return;
        }

        _isLiveSpeechEnabled = false;
        _statusMessage = null;
        NotifyChanged();
        if (LiveSpeechAutoSubmit && !string.IsNullOrWhiteSpace(e.Text))
            _ = SubmitAsync();
    }

    /// <summary>Clears only the text and attachments represented by an accepted draft.</summary>
    /// <param name="draft">The accepted draft.</param>
    public void ClearAcceptedDraft(ChatDraft draft)
    {
        if (string.Equals(_text.Trim(), draft.Text, StringComparison.Ordinal))
            _text = string.Empty;
        foreach (var attachment in draft.Attachments)
            _attachments.Remove(attachment);
        SaveCurrentDraft();
    }

    private void SaveCurrentDraft()
    {
        if (_conversation is not null)
            _drafts[_conversation] = new DraftState(_text, [.. _attachments]);
    }

    private void RestoreDraft(ChatConversation? conversation)
    {
        _text = string.Empty;
        _attachments.Clear();
        if (conversation is not null && _drafts.TryGetValue(conversation, out var draft))
        {
            _text = draft.Text;
            foreach (var attachment in draft.Attachments)
                _attachments.Add(attachment);
        }
    }

    private bool IsCurrent(int generation, ChatConversation? conversation, CancellationTokenSource source) =>
        !_isDisposed && generation == _generation && ReferenceEquals(conversation, _conversation) &&
        (ReferenceEquals(source, _sendCts) || ReferenceEquals(source, _pickCts) || ReferenceEquals(source, _audioCts) || ReferenceEquals(source, _speechCts));

    private bool IsAudioCurrent(int generation, ChatConversation? conversation, int operationId) =>
        !_isDisposed && generation == _generation && ReferenceEquals(conversation, _conversation) &&
        operationId == _audioOperationId;

    private bool IsSpeechCurrent(int generation, ChatConversation? conversation, int operationId) =>
        !_isDisposed && generation == _generation && ReferenceEquals(conversation, _conversation) &&
        operationId == _speechOperationId;

    private async Task EnsureAudioStoppedAsync()
    {
        var cleanup = _audioCleanupTask;
        if (cleanup is not null)
        {
            try { await cleanup.ConfigureAwait(true); }
            catch { }
        }

        ++_audioOperationId;
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var source = _audioCts;
        _audioCts = null;
        source?.Cancel();
        if (_audioSpeechRecognizer is not null)
        {
            try
            {
                await StopAudioDictationAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // Recorder release below is mandatory even when dictation cleanup fails.
            }
        }

        try
        {
            if (recorder?.IsRecording == true)
                await recorder.CancelAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch { }
        finally
        {
            source?.Dispose();
            _isAudioStarting = _isRecordingAudio = _isTranscribingAudio = false;
            NotifyChanged();
        }
    }

    private async Task EnsureSpeechStoppedAsync()
    {
        var cleanup = _speechCleanupTask;
        if (cleanup is not null)
        {
            try { await cleanup.ConfigureAwait(true); }
            catch { }
        }

        ++_speechOperationId;
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        var source = _speechCts;
        _speechCts = null;
        if (_speechHandler is not null && recognizer is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        source?.Cancel();
        try
        {
            if (recognizer?.IsListening == true)
                await recognizer.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch { }
        finally
        {
            source?.Dispose();
            _isSpeechStarting = _isSpeechStopping = _isListening = _isLiveSpeechEnabled = false;
            NotifyChanged();
        }
    }

    private enum SpeechMode
    {
        AudioDictation,
        LiveSpeech,
    }

    private Task StartSpeechAsync(SpeechMode mode, CancellationToken cancellationToken) =>
        mode == SpeechMode.AudioDictation
            ? StartAudioDictationAsync(cancellationToken)
            : StartLiveSpeechAsync(cancellationToken);

    private async Task StartAudioDictationAsync(CancellationToken cancellationToken)
    {
        if (SpeechRecognizer is not { IsSupported: true } recognizer ||
            _audioCts is not { } source)
        {
            return;
        }

        await StopAudioDictationAsync(CancellationToken.None).ConfigureAwait(true);
        if (_audioCts is not { } currentSource ||
            !ReferenceEquals(source, currentSource) ||
            source.IsCancellationRequested)
        {
            return;
        }

        var generation = _generation;
        var conversation = _conversation;
        var operationId = _audioOperationId;
        var speechOperationId = ++_audioSpeechOperationId;
        EventHandler<ChatSpeechRecognitionEventArgs>? handler = null;
        handler = (_, e) =>
        {
            if (!IsAudioDictationCurrent(
                    generation,
                    conversation,
                    operationId,
                    speechOperationId,
                    source,
                    recognizer,
                    handler))
            {
                return;
            }

            SpeechRecognized?.Invoke(this, e);
            if (!IsAudioDictationCurrent(
                    generation,
                    conversation,
                    operationId,
                    speechOperationId,
                    source,
                    recognizer,
                    handler))
            {
                return;
            }

            if (e.ErrorKind != ChatSpeechRecognitionErrorKind.None)
            {
                CompleteAudioDictationPass(
                    recognizer,
                    handler!,
                    speechOperationId);
                if (_isRecordingAudio)
                    _statusMessage = "Recording audio. Live transcription is unavailable.";
                NotifyChanged();
                return;
            }

            if (!e.IsFinal)
            {
                _audioHadInterimTranscript = true;
                if (!string.IsNullOrWhiteSpace(e.Text))
                {
                    Text = AppendText(
                        _audioDictationPrefix,
                        _audioCommittedTranscript,
                        e.Text);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.Text))
            {
                _audioCommittedTranscript = AppendText(
                    _audioCommittedTranscript,
                    e.Text);
                _audioHadInterimTranscript = true;
            }
            Text = AppendText(
                _audioDictationPrefix,
                _audioCommittedTranscript,
                null);

            CompleteAudioDictationPass(
                recognizer,
                handler!,
                speechOperationId);
            if (_isRecordingAudio)
                ScheduleAudioDictationRestart(source, generation, conversation, operationId);
        };

        _audioSpeechRecognizer = recognizer;
        _audioSpeechHandler = handler;
        try
        {
            if (!await recognizer.RequestPermissionsAsync(source.Token).ConfigureAwait(true))
            {
                CompleteAudioDictationPass(
                    recognizer,
                    handler,
                    speechOperationId);
                if (_isRecordingAudio)
                    _statusMessage = "Recording audio. Live transcription is unavailable.";
                NotifyChanged();
                return;
            }

            if (!IsAudioDictationCurrent(
                    generation,
                    conversation,
                    operationId,
                    speechOperationId,
                    source,
                    recognizer,
                    handler))
            {
                CompleteAudioDictationPass(
                    recognizer,
                    handler,
                    speechOperationId);
                return;
            }

            recognizer.RecognitionChanged += handler;
            _audioSpeechStarted = true;
            await recognizer.StartAsync(
                SpeechRecognitionCulture,
                reportPartialResults: true,
                source.Token).ConfigureAwait(true);
            if (!IsAudioDictationCurrent(
                    generation,
                    conversation,
                    operationId,
                    speechOperationId,
                    source,
                    recognizer,
                    handler))
            {
                await StopCapturedRecognizerAsync(recognizer).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (
            source.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            CompleteAudioDictationPass(
                recognizer,
                handler,
                speechOperationId);
            await StopCapturedRecognizerAsync(recognizer).ConfigureAwait(true);
        }
        catch
        {
            CompleteAudioDictationPass(
                recognizer,
                handler,
                speechOperationId);
            await StopCapturedRecognizerAsync(recognizer).ConfigureAwait(true);
            if (IsAudioCurrent(generation, conversation, operationId) &&
                _isRecordingAudio)
            {
                _statusMessage = "Recording audio. Live transcription is unavailable.";
                NotifyChanged();
            }
        }
    }

    private Task StopAudioDictationAsync(CancellationToken cancellationToken)
    {
        _audioSpeechOperationId++;
        if (_audioSpeechCleanupTask is { } existing)
            return existing;

        var recognizer = _audioSpeechRecognizer;
        var handler = _audioSpeechHandler;
        var shouldStop = _audioSpeechStarted || recognizer?.IsListening == true;
        if (recognizer is null)
            return Task.CompletedTask;

        CompleteAudioDictationPass(recognizer, handler);
        if (!shouldStop)
            return Task.CompletedTask;

        Task? cleanup = null;
        cleanup = StopAsync();
        _audioSpeechCleanupTask = cleanup;
        if (cleanup.IsCompleted)
            _audioSpeechCleanupTask = null;
        return cleanup;

        async Task StopAsync()
        {
            try
            {
                await recognizer.StopAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_audioSpeechCleanupTask, cleanup))
                    _audioSpeechCleanupTask = null;
                if (!_isDisposed)
                    NotifyChanged();
            }
        }
    }

    private void CompleteAudioDictationPass(
        IChatSpeechRecognizer recognizer,
        EventHandler<ChatSpeechRecognitionEventArgs>? handler,
        int? operationId = null)
    {
        if (!ReferenceEquals(_audioSpeechRecognizer, recognizer) ||
            (operationId is not null &&
                operationId.Value != _audioSpeechOperationId))
            return;

        _audioSpeechOperationId++;
        if (handler is not null && _audioSpeechStarted)
            recognizer.RecognitionChanged -= handler;
        _audioSpeechRecognizer = null;
        _audioSpeechHandler = null;
        _audioSpeechStarted = false;
    }

    private bool IsAudioDictationCurrent(
        int generation,
        ChatConversation? conversation,
        int audioOperationId,
        int speechOperationId,
        CancellationTokenSource source,
        IChatSpeechRecognizer recognizer,
        EventHandler<ChatSpeechRecognitionEventArgs>? handler) =>
        IsAudioCurrent(generation, conversation, audioOperationId) &&
        speechOperationId == _audioSpeechOperationId &&
        ReferenceEquals(source, _audioCts) &&
        ReferenceEquals(recognizer, _audioSpeechRecognizer) &&
        ReferenceEquals(recognizer, SpeechRecognizer) &&
        ReferenceEquals(handler, _audioSpeechHandler) &&
        !source.IsCancellationRequested;

    private static async Task StopCapturedRecognizerAsync(
        IChatSpeechRecognizer recognizer)
    {
        try
        {
            if (recognizer.IsListening)
                await recognizer.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // The owning audio operation still has to release its recorder.
        }
    }

    private async Task ObserveSpeechCleanupAsync(Task cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(true);
        }
        catch
        {
            if (!_isDisposed)
            {
                _errorMessage = DefaultSpeechRecognitionErrorMessage;
                NotifyChanged();
            }
        }
    }

    private void ScheduleAudioDictationRestart(
        CancellationTokenSource source,
        int generation,
        ChatConversation? conversation,
        int operationId)
    {
        _ = RestartAsync();

        async Task RestartAsync()
        {
            try
            {
                await Task.Delay(250, source.Token).ConfigureAwait(true);
                if (ReferenceEquals(source, _audioCts) &&
                    IsAudioCurrent(generation, conversation, operationId) &&
                    _isRecordingAudio)
                {
                    await StartAudioDictationAsync(source.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task AwaitMicrophoneCleanupAsync()
    {
        var tasks = new[]
        {
            _audioCleanupTask,
            _speechCleanupTask,
            _audioSpeechCleanupTask,
        };

        foreach (var task in tasks)
        {
            if (task is null)
                continue;

            try
            {
                await task.ConfigureAwait(true);
            }
            catch
            {
                // Cleanup failures have already been surfaced by the operation that owned them.
            }
        }
    }

    private void DetachFailedSpeechStart(
        IChatSpeechRecognizer recognizer,
        CancellationTokenSource source)
    {
        if (_speechHandler is { } handler)
            recognizer.RecognitionChanged -= handler;
        _speechHandler = null;
        if (ReferenceEquals(_activeRecognizer, recognizer))
            _activeRecognizer = null;
        _isListening = false;
        _isSpeechStarting = false;
        if (ReferenceEquals(_speechCts, source))
            Release(ref _speechCts, source);
    }

    private static CancellationTokenSource Replace(ref CancellationTokenSource? field, CancellationToken cancellationToken)
    {
        field?.Cancel();
        field?.Dispose();
        field = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        return field;
    }

    private static void Release(ref CancellationTokenSource? field, CancellationTokenSource source)
    {
        if (ReferenceEquals(field, source))
            field = null;
        source.Dispose();
    }

    private void ReleaseSpeech(IChatSpeechRecognizer recognizer, CancellationTokenSource source)
    {
        if (_speechHandler is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        Release(ref _speechCts, source);
    }

    private void CancelOperations()
    {
        // Invalidate every modality before cancellation so a platform callback that races
        // its cancellation cannot write a recording or recognized text into another draft.
        _audioOperationId++;
        _speechOperationId++;
        Cancel(ref _sendCts);
        Cancel(ref _pickCts);
        Cancel(ref _liveSpeechRestartCts);

        var previousAudioCleanup = _audioCleanupTask;
        var audioSource = _audioCts;
        _audioCts = null;
        audioSource?.Cancel();
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var audioSpeechCleanup = StopAudioDictationAsync(CancellationToken.None);
        Task? audioBoundaryCleanup = null;
        audioBoundaryCleanup = CleanupAudioAsync();
        _audioCleanupTask = audioBoundaryCleanup;
        if (audioBoundaryCleanup.IsCompleted)
            _audioCleanupTask = null;

        var previousSpeechCleanup = _speechCleanupTask;
        var speechSource = _speechCts;
        _speechCts = null;
        speechSource?.Cancel();
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        if (_speechHandler is not null && recognizer is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        Task? speechBoundaryCleanup = null;
        speechBoundaryCleanup = CleanupSpeechAsync();
        _speechCleanupTask = speechBoundaryCleanup;
        if (speechBoundaryCleanup.IsCompleted)
            _speechCleanupTask = null;

        _isSending = _isRecordingAudio = _isTranscribingAudio = _isListening = false;
        _isAudioStarting = _isLiveSpeechEnabled = _isSpeechStarting = _isSpeechStopping = _isComposingOverride = false;
        _statusMessage = null;
        _errorMessage = null;

        async Task CleanupAudioAsync()
        {
            try
            {
                if (previousAudioCleanup is not null)
                    await previousAudioCleanup.ConfigureAwait(true);
            }
            catch
            {
            }

            try
            {
                await audioSpeechCleanup.ConfigureAwait(true);
            }
            catch
            {
            }

            try
            {
                if (recorder?.IsRecording == true)
                    await recorder.CancelAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // Boundary cleanup is observed here so replacement operations never race a
                // faulted, unobserved microphone-release task.
            }
            finally
            {
                audioSource?.Dispose();
                if (ReferenceEquals(_audioCleanupTask, audioBoundaryCleanup))
                    _audioCleanupTask = null;
                if (!_isDisposed)
                    NotifyChanged();
            }
        }

        async Task CleanupSpeechAsync()
        {
            try
            {
                if (previousSpeechCleanup is not null)
                    await previousSpeechCleanup.ConfigureAwait(true);
                if (recognizer?.IsListening == true)
                    await recognizer.StopAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // See CleanupAudioAsync.
            }
            finally
            {
                speechSource?.Dispose();
                if (ReferenceEquals(_speechCleanupTask, speechBoundaryCleanup))
                    _speechCleanupTask = null;
                if (!_isDisposed)
                    NotifyChanged();
            }
        }
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        var current = source;
        source = null;
        if (current is null)
            return;
        current.Cancel();
        current.Dispose();
    }

    private void NotifyChangedCore()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke();
    }

    private static string AppendText(string existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next.Trim() : existing.TrimEnd() + " " + next.Trim();

    private static string AppendText(string first, string second, string? third) =>
        string.Join(
            " ",
            new[] { first, second, third }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim()));

    private static string FormatMegabytes(long bytes) =>
        bytes % (1024L * 1024) == 0
            ? (bytes / (1024L * 1024)).ToString(CultureInfo.InvariantCulture)
            : (bytes / (double)(1024L * 1024)).ToString("0.#", CultureInfo.InvariantCulture);

    private void DetachCompletedSpeechPass(CancellationTokenSource source)
    {
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        if (_speechHandler is not null && recognizer is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
        _isListening = false;
        if (ReferenceEquals(_speechCts, source))
            Release(ref _speechCts, source);
        NotifyChanged();
    }

    private async Task SubmitAndResumeSpeechAsync(
        int generation,
        ChatConversation? conversation,
        int operationId)
    {
        // A completed recognition pass has already released its microphone. Keep the user's
        // live-speech intent while the ordinary send lifecycle runs, then resume only if this
        // is still the same controller/conversation/pass.
        await SubmitAsync().ConfigureAwait(true);
        if (IsSpeechCurrent(generation, conversation, operationId) && _isLiveSpeechEnabled)
            ScheduleLiveSpeechRestart(generation, conversation, operationId, countsAgainstBudget: false);
    }

    private void ScheduleLiveSpeechRestart(
        int generation,
        ChatConversation? conversation,
        int operationId,
        bool countsAgainstBudget = true)
    {
        if (_isDisposed || !_isLiveSpeechEnabled ||
            !IsSpeechCurrent(generation, conversation, operationId))
            return;

        if (countsAgainstBudget && ++_liveSpeechRestartAttempt > 3)
        {
            _isLiveSpeechEnabled = false;
            _errorMessage = DefaultSpeechRecognitionErrorMessage;
            NotifyChanged();
            return;
        }

        _liveSpeechRestartCts?.Cancel();
        _liveSpeechRestartCts?.Dispose();
        var restart = new CancellationTokenSource();
        _liveSpeechRestartCts = restart;
        var delay = countsAgainstBudget
            ? TimeSpan.FromMilliseconds(250 * Math.Pow(2, _liveSpeechRestartAttempt - 1))
            : TimeSpan.FromMilliseconds(250);
        _ = RestartAsync();

        async Task RestartAsync()
        {
            try
            {
                await Task.Delay(delay, restart.Token).ConfigureAwait(true);
                if (!ReferenceEquals(_liveSpeechRestartCts, restart) ||
                    !IsSpeechCurrent(generation, conversation, operationId) ||
                    !_isLiveSpeechEnabled)
                    return;

                _liveSpeechRestartCts = null;
                _isLiveSpeechEnabled = false;
                await StartLiveSpeechAsync(restart.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_liveSpeechRestartCts, restart))
                    _liveSpeechRestartCts = null;
                restart.Dispose();
            }
        }
    }

    private void SetOption<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        NotifyChanged();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed record DraftState(string Text, IReadOnlyList<ChatAttachment> Attachments);
}
