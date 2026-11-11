using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The message box behind <see cref="AIChatView"/>: staged attachments, sending, audio notes, dictation, and
/// live speech. The view's bindable properties are the single source of truth for the text and every option;
/// the composer only keeps the transient state of in-flight operations.
/// </summary>
/// <remarks>
/// Audio capture and live speech never hold the microphone at the same time. Every asynchronous step checks a
/// generation (bumped when the session changes or the view unloads) and a per-modality operation id, so a
/// late platform callback can never write into a newer draft.
/// </remarks>
internal sealed class ChatComposer
{
    internal const string SendErrorMessage = "Your message could not be sent. Please try again.";
    internal const string AttachmentErrorMessage = "That attachment could not be added.";
    internal const string AudioErrorMessage = "The audio recording could not be completed.";
    internal const string MicrophoneDeniedMessage = "Microphone access was denied.";
    internal const string NoAudioMessage = "The device did not capture audio. Record for at least one second and check the microphone input level.";
    internal const string NoSpeechMessage = "No speech was recognized in the recording.";
    internal const string SpeechErrorMessage = "Live voice could not continue.";
    internal const string LanguageNotSupportedMessage = "Live voice is not available in this language.";
    internal const long MaximumTotalAttachmentBytes = 50L * 1024 * 1024;
    internal const int MaximumSpeechRestarts = 3;

    private readonly AIChatView _view;
    private readonly List<DataContent> _attachments = [];
    private readonly CleanupSlot _audioCleanup = new();
    private readonly CleanupSlot _dictationCleanup = new();
    private readonly CleanupSlot _speechCleanup = new();
    private int _generation;
    private bool _isSending;
    private CancellationTokenSource? _pickCts;
    private string? _errorMessage;

    // Audio note / dictation recording.
    private int _audioOperation;
    private CancellationTokenSource? _audioCts;
    private IChatAudioRecorder? _activeRecorder;
    private bool _isAudioStarting;
    private bool _isRecording;
    private bool _isTranscribing;

    // Interim transcript shown while recording (ShowInterimAudioTranscript).
    private IChatSpeechRecognizer? _dictationRecognizer;
    private EventHandler<ChatSpeechRecognitionEventArgs>? _dictationHandler;
    private bool _dictationStarted;
    private int _dictationOperation;
    private string _dictationPrefix = string.Empty;
    private string _dictationCommitted = string.Empty;
    private bool _dictationHadInterim;

    // Live speech: continuous passes with bounded restarts.
    private int _speechOperation;
    private CancellationTokenSource? _speechCts;
    private IChatSpeechRecognizer? _activeRecognizer;
    private EventHandler<ChatSpeechRecognitionEventArgs>? _speechHandler;
    private bool _liveSpeechEnabled;
    private bool _isSpeechStarting;
    private bool _isSpeechStopping;
    private bool _isListening;
    private string _speechPrefix = string.Empty;
    private string _speechCommitted = string.Empty;
    private int _restartAttempt;
    private CancellationTokenSource? _restartCts;

    public ChatComposer(AIChatView view) => _view = view;

    /// <summary>Gets or sets the base delay before live speech or dictation restarts a recognition pass.</summary>
    internal TimeSpan RestartDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the last status message announced to screen readers.</summary>
    internal string? LastAnnouncement { get; private set; }

    public IReadOnlyList<DataContent> Attachments => _attachments;

    public string? ErrorMessage => _errorMessage;

    public bool IsSending => _isSending;

    public bool IsRecordingAudio => _isAudioStarting || _isRecording;

    public bool IsTranscribingAudio => _isTranscribing;

    /// <summary>Gets whether live voice is on, including the short gaps between recognition passes.</summary>
    public bool IsListening => _liveSpeechEnabled || _isListening || _isSpeechStarting;

    /// <summary>Gets whether a local operation (picking, recording, listening) owns the draft.</summary>
    public bool IsComposing =>
        _isAudioStarting || _isRecording || _isTranscribing ||
        _isListening || _isSpeechStarting || _isSpeechStopping ||
        _pickCts is not null;

    public bool CanSend =>
        !_isSending && !IsComposing && _view.IsInputEnabled &&
        _view.EffectiveSession is not null && !_view.IsSessionBusy &&
        (!string.IsNullOrWhiteSpace(_view.Text) || _attachments.Count > 0);

    public bool CanPickAttachments =>
        _view.AllowAttachments && _view.IsInputEnabled && !_view.IsSessionBusy && !IsComposing;

    public bool CanToggleAudioCapture =>
        _view.AllowAudioCapture && _view.GetAudioRecorder() is { IsSupported: true } && !_isAudioStarting &&
        (_isRecording || _isTranscribing ||
            (_view.IsInputEnabled && !_view.IsSessionBusy && _pickCts is null && !IsSpeechActive && !HasCleanup));

    public bool CanToggleLiveSpeech =>
        _view.AllowLiveSpeech && _view.GetSpeechRecognizer() is { IsSupported: true } &&
        !_isSpeechStarting && !_isSpeechStopping &&
        (_liveSpeechEnabled || _isListening ||
            (_view.IsInputEnabled && !_view.IsSessionBusy && _pickCts is null && !IsAudioActive && !HasCleanup));

    private bool IsAudioActive => _isAudioStarting || _isRecording || _isTranscribing;

    private bool IsSpeechActive => _isSpeechStarting || _isSpeechStopping || _liveSpeechEnabled || _isListening;

    private bool HasCleanup => _audioCleanup.Task is not null || _speechCleanup.Task is not null || _dictationCleanup.Task is not null;

    // ── Draft and sending ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Sends the draft. Failures surface through <see cref="ErrorMessage"/>; this never throws.</summary>
    public async Task SendAsync()
    {
        if (!CanSend || _view.EffectiveSession is not { } session)
            return;

        var text = _view.Text?.Trim() ?? string.Empty;
        var attachments = _attachments.ToArray();
        var message = CreateMessage(text, attachments);
        var generation = _generation;
        var turnCount = session.Turns.Count;

        // Clear optimistically: the message is on its way, so the box must not keep showing it while the
        // response streams. The draft comes back only if the session never accepted it.
        _isSending = true;
        _errorMessage = null;
        _attachments.Clear();
        _view.Text = string.Empty;
        _view.OnSendStarted();
        Notify();

        try
        {
            await session.SendMessageAsync(message);
        }
        catch (OperationCanceledException)
        {
            // Stopped: the turn (and the user's message in it) stays.
        }
        catch (Exception)
        {
            if (generation == _generation && ReferenceEquals(session, _view.EffectiveSession))
            {
                if (session.Turns.Count == turnCount)
                    RestoreDraft(text, attachments);
                _errorMessage = SendErrorMessage;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _isSending = false;
                Notify();
            }
        }
    }

    /// <summary>Sends <paramref name="text"/> as if the user typed it, keeping staged attachments.</summary>
    public Task SendTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        _view.Text = text;
        return SendAsync();
    }

    /// <summary>Stops the response in progress.</summary>
    public async Task StopAsync()
    {
        if (_view.EffectiveSession is not { } session)
            return;

        try
        {
            await session.CancelAsync();
            Announce("Response stopped.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public bool AddAttachments(IReadOnlyList<DataContent> additions)
    {
        if (additions.Count == 0)
            return true;

        var maxBytes = _view.MaximumAttachmentBytes;
        if (additions.Any(addition => addition.Data.Length > maxBytes))
        {
            SetError(TooLargeMessage(maxBytes));
            return false;
        }

        var maxCount = _view.MaximumAttachmentCount;
        var total = _attachments.Sum(static a => (long)a.Data.Length) + additions.Sum(static a => (long)a.Data.Length);
        if (_attachments.Count + additions.Count > maxCount || total > MaximumTotalAttachmentBytes)
        {
            SetError($"You can attach up to {maxCount} files and {FormatMegabytes(MaximumTotalAttachmentBytes)} MB in total.");
            return false;
        }

        _attachments.AddRange(additions);
        _errorMessage = null;
        Notify();
        return true;
    }

    public bool RemoveAttachment(DataContent attachment)
    {
        if (!_attachments.Remove(attachment))
            return false;

        Notify();
        return true;
    }

    /// <summary>Shows the picker and stages what the user chose.</summary>
    public async Task PickAttachmentsAsync()
    {
        if (_view.IsSessionBusy || IsComposing || !_view.IsInputEnabled)
            return;

        var picker = _view.GetAttachmentPicker();
        var generation = _generation;
        var source = new CancellationTokenSource();
        _pickCts = source;
        _errorMessage = null;
        Notify();

        try
        {
            var picked = await picker.PickAsync(_view.AttachmentFileTypes, _view.MaximumAttachmentBytes, source.Token);
            if (generation == _generation && ReferenceEquals(source, _pickCts) && picked is not null)
                AddAttachments(picked.Where(static attachment => attachment is not null).ToArray());
        }
        catch (OperationCanceledException)
        {
        }
        catch (AttachmentTooLargeException)
        {
            if (generation == _generation)
                _errorMessage = TooLargeMessage(_view.MaximumAttachmentBytes);
        }
        catch (Exception)
        {
            if (generation == _generation)
                _errorMessage = AttachmentErrorMessage;
        }
        finally
        {
            if (ReferenceEquals(_pickCts, source))
                _pickCts = null;
            Notify();
        }
    }

    /// <summary>Cancels every operation; used when the session changes or the view unloads.</summary>
    public void Reset(bool clearDraft)
    {
        CancelOperations();
        if (clearDraft)
        {
            _attachments.Clear();
            _view.Text = string.Empty;
        }

        Notify();
    }

    /// <summary>Stops audio capture and live speech; used when a capture service is replaced.</summary>
    public void CancelCapture()
    {
        CancelAudio();
        CancelSpeech();
        Notify();
    }

    // ── Audio notes and dictation ───────────────────────────────────────────────────────────────────────

    /// <summary>Starts recording, stops and stages it, or cancels a transcription in progress.</summary>
    public Task ToggleAudioCaptureAsync() =>
        _isAudioStarting ? Task.CompletedTask
        : _isTranscribing ? CancelAudioCaptureAsync()
        : _audioCleanup.Task is not null ? Task.CompletedTask
        : _isRecording ? StopAudioCaptureAsync()
        : StartAudioCaptureAsync();

    internal async Task StartAudioCaptureAsync()
    {
        if (!CanStartAudio(out var recorder))
            return;

        await AwaitMicrophoneCleanupAsync();
        if (!CanStartAudio(out var current) || !ReferenceEquals(current, recorder))
            return;

        var generation = _generation;
        var operation = ++_audioOperation;

        // Claim the microphone before awaiting live speech shutdown.
        _isAudioStarting = true;
        Notify();
        await EnsureSpeechStoppedAsync();
        if (!IsAudioCurrent(generation, operation) || !ReferenceEquals(_view.GetAudioRecorder(), recorder))
        {
            if (IsAudioCurrent(generation, operation))
            {
                _isAudioStarting = false;
                Notify();
            }

            return;
        }

        var source = new CancellationTokenSource();
        _audioCts = source;
        _activeRecorder = recorder;
        _errorMessage = null;
        _dictationPrefix = _view.Text?.Trim() ?? string.Empty;
        _dictationCommitted = string.Empty;
        _dictationHadInterim = false;
        Announce("Recording audio.");

        try
        {
            await recorder.StartAsync(source.Token);
            if (IsAudioCurrent(generation, operation) && ReferenceEquals(source, _audioCts))
            {
                _isRecording = true;
                _isAudioStarting = false;
                Notify();
                if (_view.ShowInterimAudioTranscript && _view.GetSpeechRecognizer() is { IsSupported: true })
                    await StartDictationAsync(generation, operation, source);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsAudioCurrent(generation, operation))
                _errorMessage = ex is PermissionException or UnauthorizedAccessException ? MicrophoneDeniedMessage : AudioErrorMessage;
        }
        finally
        {
            if (IsAudioCurrent(generation, operation))
            {
                _isAudioStarting = false;
                if (!_isRecording && ReferenceEquals(_audioCts, source))
                {
                    _audioCts = null;
                    _activeRecorder = null;
                }

                Notify();
            }
        }
    }

    internal async Task StopAudioCaptureAsync()
    {
        if (!_isRecording || _activeRecorder is not { } recorder || _audioCts is not { } source)
            return;

        var generation = _generation;
        var operation = _audioOperation;
        _isRecording = false;
        _isTranscribing = true;
        Notify();
        Announce("Processing audio.");

        var lifecycle = StopCoreAsync();
        _audioCleanup.Track(lifecycle, Notify);
        try
        {
            await lifecycle;
        }
        finally
        {
            if (ReferenceEquals(_activeRecorder, recorder))
                _activeRecorder = null;
            Notify();
        }

        async Task StopCoreAsync()
        {
            try
            {
                if (_dictationRecognizer is not null)
                {
                    try
                    {
                        await StopDictationAsync();
                    }
                    catch (Exception)
                    {
                        if (IsAudioCurrent(generation, operation))
                            _errorMessage = SpeechErrorMessage;
                    }
                }

                var maxBytes = _view.MaximumAttachmentBytes;
                var recording = await recorder.StopAsync(maxBytes, source.Token);
                if (!IsAudioCurrent(generation, operation))
                    return;

                if (recording is null || recording.Data.IsEmpty)
                {
                    _errorMessage = NoAudioMessage;
                    return;
                }

                if (recording.Data.Length > maxBytes)
                {
                    _errorMessage = TooLargeMessage(maxBytes);
                    return;
                }

                if (_view.GetAudioTranscriber() is { } transcriber)
                {
                    // Dictation: the recording becomes text in the message box.
                    Announce("Transcribing audio.");
                    var text = await transcriber.TranscribeAsync(recording, source.Token);
                    if (!IsAudioCurrent(generation, operation))
                        return;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _errorMessage = NoSpeechMessage;
                        return;
                    }

                    _view.Text = _dictationHadInterim
                        ? Join(_dictationPrefix, text)
                        : Join(_view.Text, text);
                    Announce("Voice transcription ready.");
                }
                else
                {
                    // Audio note: the recording is attached, replacing an earlier one.
                    _attachments.RemoveAll(static a => a.HasTopLevelMediaType("audio"));
                    if (AddAttachments([recording]))
                        Announce("Audio recording attached.");
                }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (IsAudioCurrent(generation, operation))
                    _errorMessage = ex is AudioRecordingTooLargeException ? TooLargeMessage(_view.MaximumAttachmentBytes) : AudioErrorMessage;
            }
            finally
            {
                if (IsAudioCurrent(generation, operation))
                {
                    _isTranscribing = false;
                    if (ReferenceEquals(_audioCts, source))
                        _audioCts = null;
                }
            }
        }
    }

    /// <summary>Cancels the recording or transcription in progress without staging anything.</summary>
    internal async Task CancelAudioCaptureAsync()
    {
        if (_audioCts is not { } source)
            return;

        ++_audioOperation;
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var cleanup = _audioCleanup.Task;
        source.Cancel();

        Exception? dictationFailure = null;
        try
        {
            await StopDictationAsync();
        }
        catch (Exception ex)
        {
            dictationFailure = ex;
        }

        try
        {
            // A transcriber may ignore cancellation; it no longer owns the microphone, so don't wait for it.
            if (cleanup is null && recorder?.IsRecording == true)
                await recorder.CancelAsync();
        }
        catch (Exception)
        {
        }

        if (dictationFailure is not null)
            _errorMessage = SpeechErrorMessage;
        _isRecording = _isTranscribing = _isAudioStarting = false;
        if (ReferenceEquals(_audioCts, source))
            _audioCts = null;
        Announce("Audio canceled.");
        Notify();
    }

    private bool CanStartAudio(out IChatAudioRecorder recorder)
    {
        recorder = _view.GetAudioRecorder()!;
        return _view.AllowAudioCapture && recorder is { IsSupported: true } &&
            !_isAudioStarting && !_isRecording && !_isTranscribing &&
            _view.IsInputEnabled && !_view.IsSessionBusy && _pickCts is null;
    }

    private async Task StartDictationAsync(int generation, int audioOperation, CancellationTokenSource source)
    {
        if (_view.GetSpeechRecognizer() is not { IsSupported: true } recognizer)
            return;

        await StopDictationAsync();
        if (!ReferenceEquals(source, _audioCts) || source.IsCancellationRequested || !IsAudioCurrent(generation, audioOperation))
            return;

        var dictationOperation = ++_dictationOperation;
        EventHandler<ChatSpeechRecognitionEventArgs>? handler = null;
        handler = (_, e) =>
        {
            if (!IsDictationCurrent(generation, audioOperation, dictationOperation, source, recognizer, handler))
                return;

            if (e.ErrorKind != ChatSpeechRecognitionErrorKind.None)
            {
                CompleteDictationPass(recognizer, handler, dictationOperation);
                if (_isRecording)
                    Announce("Recording audio. Live transcription is unavailable.");
                Notify();
                return;
            }

            if (!e.IsFinal)
            {
                _dictationHadInterim = true;
                if (!string.IsNullOrWhiteSpace(e.Text))
                    _view.Text = Join(_dictationPrefix, _dictationCommitted, e.Text);
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.Text))
            {
                _dictationCommitted = Join(_dictationCommitted, e.Text);
                _dictationHadInterim = true;
            }

            _view.Text = Join(_dictationPrefix, _dictationCommitted);
            CompleteDictationPass(recognizer, handler, dictationOperation);
            if (_isRecording)
                ScheduleDictationRestart(generation, audioOperation, source);
        };

        _dictationRecognizer = recognizer;
        _dictationHandler = handler;
        try
        {
            if (!await recognizer.RequestPermissionsAsync(source.Token))
            {
                CompleteDictationPass(recognizer, handler, dictationOperation);
                if (_isRecording)
                    Announce("Recording audio. Live transcription is unavailable.");
                Notify();
                return;
            }

            if (!IsDictationCurrent(generation, audioOperation, dictationOperation, source, recognizer, handler))
            {
                CompleteDictationPass(recognizer, handler, dictationOperation);
                return;
            }

            recognizer.RecognitionChanged += handler;
            _dictationStarted = true;
            await recognizer.StartAsync(_view.SpeechRecognitionCulture, reportPartialResults: true, source.Token);
            if (!IsDictationCurrent(generation, audioOperation, dictationOperation, source, recognizer, handler))
                await StopQuietlyAsync(recognizer);
        }
        catch (Exception)
        {
            CompleteDictationPass(recognizer, handler, dictationOperation);
            await StopQuietlyAsync(recognizer);
            if (IsAudioCurrent(generation, audioOperation) && _isRecording && !source.IsCancellationRequested)
            {
                Announce("Recording audio. Live transcription is unavailable.");
                Notify();
            }
        }
    }

    private Task StopDictationAsync()
    {
        _dictationOperation++;
        if (_dictationCleanup.Task is { } existing)
            return existing;

        var recognizer = _dictationRecognizer;
        if (recognizer is null)
            return Task.CompletedTask;

        var shouldStop = _dictationStarted || recognizer.IsListening;
        CompleteDictationPass(recognizer, _dictationHandler);
        if (!shouldStop)
            return Task.CompletedTask;

        var cleanup = StopRecognizerAsync(recognizer);
        _dictationCleanup.Track(cleanup, Notify);
        return cleanup;

        // Async so a recognizer that throws synchronously still produces a faulted task.
        static async Task StopRecognizerAsync(IChatSpeechRecognizer recognizer) => await recognizer.StopAsync();
    }

    private void CompleteDictationPass(
        IChatSpeechRecognizer recognizer,
        EventHandler<ChatSpeechRecognitionEventArgs>? handler,
        int? operation = null)
    {
        if (!ReferenceEquals(_dictationRecognizer, recognizer) || (operation is { } id && id != _dictationOperation))
            return;

        _dictationOperation++;
        if (handler is not null && _dictationStarted)
            recognizer.RecognitionChanged -= handler;
        _dictationRecognizer = null;
        _dictationHandler = null;
        _dictationStarted = false;
    }

    private bool IsDictationCurrent(
        int generation,
        int audioOperation,
        int dictationOperation,
        CancellationTokenSource source,
        IChatSpeechRecognizer recognizer,
        EventHandler<ChatSpeechRecognitionEventArgs>? handler) =>
        IsAudioCurrent(generation, audioOperation) &&
        dictationOperation == _dictationOperation &&
        ReferenceEquals(source, _audioCts) &&
        ReferenceEquals(recognizer, _dictationRecognizer) &&
        ReferenceEquals(recognizer, _view.GetSpeechRecognizer()) &&
        ReferenceEquals(handler, _dictationHandler) &&
        !source.IsCancellationRequested;

    private void ScheduleDictationRestart(int generation, int audioOperation, CancellationTokenSource source)
    {
        _ = RestartAsync();

        async Task RestartAsync()
        {
            try
            {
                await Task.Delay(RestartDelay, source.Token);
                if (ReferenceEquals(source, _audioCts) && IsAudioCurrent(generation, audioOperation) && _isRecording)
                    await StartDictationAsync(generation, audioOperation, source);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    // ── Live speech ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Turns live voice on or off.</summary>
    public Task ToggleLiveSpeechAsync()
    {
        if (_isSpeechStarting || _isSpeechStopping)
            return Task.CompletedTask;

        if (_liveSpeechEnabled || _isListening)
            return StopLiveSpeechAsync();

        _restartAttempt = 0;
        return StartLiveSpeechAsync();
    }

    internal async Task StartLiveSpeechAsync()
    {
        if (!CanStartSpeech(out var recognizer))
            return;

        await AwaitMicrophoneCleanupAsync();
        if (!CanStartSpeech(out var current) || !ReferenceEquals(current, recognizer))
            return;

        var generation = _generation;
        var operation = ++_speechOperation;
        var source = new CancellationTokenSource();
        _speechCts = source;
        _errorMessage = null;
        _isSpeechStarting = true;
        _liveSpeechEnabled = true;
        _speechPrefix = _view.Text?.Trim() ?? string.Empty;
        _speechCommitted = string.Empty;
        Notify();
        Announce("Listening.");

        try
        {
            var granted = await recognizer.RequestPermissionsAsync(source.Token);
            if (!IsSpeechCurrent(generation, operation, source))
                return;

            if (!granted)
            {
                _errorMessage = MicrophoneDeniedMessage;
                _liveSpeechEnabled = false;
                return;
            }

            await EnsureAudioStoppedAsync();
            if (!IsSpeechCurrent(generation, operation, source))
                return;

            _activeRecognizer = recognizer;
            _speechHandler = (_, e) => OnSpeechRecognized(generation, operation, source, e);
            recognizer.RecognitionChanged += _speechHandler;
            await recognizer.StartAsync(_view.SpeechRecognitionCulture, reportPartialResults: true, source.Token);

            if (IsSpeechCurrent(generation, operation, source) && !source.IsCancellationRequested)
            {
                _isListening = true;
                _isSpeechStarting = false;
            }
            else if (IsSpeechCurrent(generation, operation, null) && source.IsCancellationRequested)
            {
                DetachFailedSpeechStart(recognizer, source);
                _liveSpeechEnabled = false;
                await StopQuietlyAsync(recognizer);
            }
            else if (!IsSpeechCurrent(generation, operation, null) && !ReferenceEquals(_activeRecognizer, recognizer))
            {
                // Stopped or replaced while starting, and the recognizer ignored the cancellation: release it.
                await StopQuietlyAsync(recognizer);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (IsSpeechCurrent(generation, operation, null))
            {
                DetachFailedSpeechStart(recognizer, source);
                _liveSpeechEnabled = false;
            }
        }
        catch (Exception ex)
        {
            if (IsSpeechCurrent(generation, operation, null))
            {
                DetachFailedSpeechStart(recognizer, source);
                if (ex is PermissionException or UnauthorizedAccessException)
                {
                    _liveSpeechEnabled = false;
                    _errorMessage = MicrophoneDeniedMessage;
                }
                else
                {
                    ScheduleSpeechRestart(generation, operation, countsAgainstBudget: true);
                }
            }
        }
        finally
        {
            if (IsSpeechCurrent(generation, operation, source))
            {
                _isSpeechStarting = false;
                if (!_isListening && !_liveSpeechEnabled)
                    _speechCts = null;
            }

            Notify();
        }
    }

    internal async Task StopLiveSpeechAsync()
    {
        if (!_isListening && !_liveSpeechEnabled && !_isSpeechStarting)
            return;

        ++_speechOperation;
        Cancel(ref _restartCts);
        var source = _speechCts;
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        DetachSpeechHandler(recognizer);
        _isListening = false;
        _liveSpeechEnabled = false;
        _isSpeechStopping = true;
        source?.Cancel();
        Notify();

        if (recognizer is null)
        {
            FinishStop();
            return;
        }

        var cleanup = StopCoreAsync();
        _speechCleanup.Track(cleanup, Notify);
        await cleanup;

        async Task StopCoreAsync()
        {
            try
            {
                await recognizer.StopAsync();
            }
            catch (Exception)
            {
                _errorMessage = SpeechErrorMessage;
            }
            finally
            {
                FinishStop();
            }
        }

        void FinishStop()
        {
            if (ReferenceEquals(_speechCts, source))
                _speechCts = null;
            _isSpeechStarting = false;
            _isSpeechStopping = false;
            Announce("Live voice stopped.");
            Notify();
        }
    }

    private bool CanStartSpeech(out IChatSpeechRecognizer recognizer)
    {
        recognizer = _view.GetSpeechRecognizer()!;
        return _view.AllowLiveSpeech && recognizer is { IsSupported: true } &&
            !_liveSpeechEnabled && !_isSpeechStarting && !_isSpeechStopping &&
            _view.IsInputEnabled && !_view.IsSessionBusy && _pickCts is null;
    }

    private void OnSpeechRecognized(int generation, int operation, CancellationTokenSource source, ChatSpeechRecognitionEventArgs e)
    {
        if (!IsSpeechCurrent(generation, operation, source) || source.IsCancellationRequested)
            return;

        if (e.ErrorKind != ChatSpeechRecognitionErrorKind.None)
        {
            DetachCompletedSpeechPass(source);
            switch (e.ErrorKind)
            {
                case ChatSpeechRecognitionErrorKind.PermissionDenied:
                    _liveSpeechEnabled = false;
                    _errorMessage = MicrophoneDeniedMessage;
                    break;
                case ChatSpeechRecognitionErrorKind.LanguageNotSupported:
                    _liveSpeechEnabled = false;
                    _errorMessage = LanguageNotSupportedMessage;
                    break;
                case ChatSpeechRecognitionErrorKind.Transient:
                    Announce("Live voice was interrupted. Reconnecting.");
                    ScheduleSpeechRestart(generation, operation, countsAgainstBudget: true);
                    break;
                case ChatSpeechRecognitionErrorKind.Fatal:
                    ScheduleSpeechRestart(generation, operation, countsAgainstBudget: true);
                    break;
                default:
                    // No speech or an aborted pass: keep listening.
                    ScheduleSpeechRestart(generation, operation, countsAgainstBudget: false);
                    break;
            }

            Notify();
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Text))
            _restartAttempt = 0;

        if (!e.IsFinal)
        {
            if (!string.IsNullOrWhiteSpace(e.Text) && _view.ShowInterimSpeechText)
                _view.Text = Join(_speechPrefix, _speechCommitted, e.Text);
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Text))
            _speechCommitted = Join(_speechCommitted, e.Text);

        // Replaces any unconfirmed interim text with what the pass actually committed.
        _view.Text = Join(_speechPrefix, _speechCommitted);
        DetachCompletedSpeechPass(source);

        if (_view.LiveSpeechAutoSubmit && !string.IsNullOrWhiteSpace(e.Text))
            _ = SubmitAndResumeSpeechAsync(generation, operation);
        else
            ScheduleSpeechRestart(generation, operation, countsAgainstBudget: false);

        Notify();
    }

    private async Task SubmitAndResumeSpeechAsync(int generation, int operation)
    {
        // The finished pass already released the microphone. Keep the live voice intent while the turn runs,
        // then listen again if nothing else happened in the meantime.
        await SendAsync();
        if (_liveSpeechEnabled && IsSpeechCurrent(generation, operation, null))
            ScheduleSpeechRestart(generation, operation, countsAgainstBudget: false);
    }

    private void ScheduleSpeechRestart(int generation, int operation, bool countsAgainstBudget)
    {
        if (!_liveSpeechEnabled || !IsSpeechCurrent(generation, operation, null))
            return;

        if (countsAgainstBudget && ++_restartAttempt > MaximumSpeechRestarts)
        {
            _liveSpeechEnabled = false;
            _errorMessage = SpeechErrorMessage;
            Notify();
            return;
        }

        Cancel(ref _restartCts);
        var restart = new CancellationTokenSource();
        _restartCts = restart;
        var delay = countsAgainstBudget
            ? RestartDelay * Math.Pow(2, _restartAttempt - 1)
            : RestartDelay;
        _ = RestartAsync();

        async Task RestartAsync()
        {
            try
            {
                await Task.Delay(delay, restart.Token);
                if (!ReferenceEquals(_restartCts, restart) || !IsSpeechCurrent(generation, operation, null) || !_liveSpeechEnabled)
                    return;

                _restartCts = null;
                _liveSpeechEnabled = false;
                await StartLiveSpeechAsync();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_restartCts, restart))
                    _restartCts = null;
            }
        }
    }

    private void DetachCompletedSpeechPass(CancellationTokenSource source)
    {
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        DetachSpeechHandler(recognizer);
        _isListening = false;
        if (ReferenceEquals(_speechCts, source))
            _speechCts = null;
    }

    private void DetachFailedSpeechStart(IChatSpeechRecognizer recognizer, CancellationTokenSource source)
    {
        if (_speechHandler is { } handler)
            recognizer.RecognitionChanged -= handler;
        _speechHandler = null;
        if (ReferenceEquals(_activeRecognizer, recognizer))
            _activeRecognizer = null;
        _isListening = false;
        _isSpeechStarting = false;
        if (ReferenceEquals(_speechCts, source))
            _speechCts = null;
    }

    private void DetachSpeechHandler(IChatSpeechRecognizer? recognizer)
    {
        if (_speechHandler is not null && recognizer is not null)
            recognizer.RecognitionChanged -= _speechHandler;
        _speechHandler = null;
    }

    // ── Microphone ownership ────────────────────────────────────────────────────────────────────────────

    private async Task AwaitMicrophoneCleanupAsync()
    {
        await _audioCleanup.AwaitAsync();
        await _speechCleanup.AwaitAsync();
        await _dictationCleanup.AwaitAsync();
    }

    private async Task EnsureAudioStoppedAsync()
    {
        await _audioCleanup.AwaitAsync();

        ++_audioOperation;
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var source = _audioCts;
        _audioCts = null;
        source?.Cancel();

        if (_dictationRecognizer is not null)
        {
            try
            {
                await StopDictationAsync();
            }
            catch (Exception)
            {
                // Releasing the recorder below is mandatory even when dictation cleanup fails.
            }
        }

        try
        {
            if (recorder?.IsRecording == true)
                await recorder.CancelAsync();
        }
        catch (Exception)
        {
        }
        finally
        {
            _isAudioStarting = _isRecording = _isTranscribing = false;
            Notify();
        }
    }

    private async Task EnsureSpeechStoppedAsync()
    {
        await _speechCleanup.AwaitAsync();

        ++_speechOperation;
        Cancel(ref _restartCts);
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        var source = _speechCts;
        _speechCts = null;
        DetachSpeechHandler(recognizer);
        source?.Cancel();

        try
        {
            if (recognizer?.IsListening == true)
                await recognizer.StopAsync();
        }
        catch (Exception)
        {
        }
        finally
        {
            _isSpeechStarting = _isSpeechStopping = _isListening = _liveSpeechEnabled = false;
            Notify();
        }
    }

    private void CancelOperations()
    {
        // Invalidate every modality before cancelling, so a platform callback racing the cancellation can't
        // write a recording or recognized text into the next draft.
        _generation++;
        Cancel(ref _pickCts);
        _isSending = false;
        _errorMessage = null;
        CancelAudio();
        CancelSpeech();
    }

    private void CancelAudio()
    {
        _audioOperation++;
        var previous = _audioCleanup.Task;
        var source = _audioCts;
        _audioCts = null;
        source?.Cancel();
        var recorder = _activeRecorder;
        _activeRecorder = null;
        var dictation = StopDictationAsync();
        _audioCleanup.Track(CleanupAsync(), Notify);
        _isAudioStarting = _isRecording = _isTranscribing = false;

        async Task CleanupAsync()
        {
            await Observe(previous);
            await Observe(dictation);
            try
            {
                if (recorder?.IsRecording == true)
                    await recorder.CancelAsync();
            }
            catch (Exception)
            {
                // Observed here so a replacement operation never races a faulted microphone release.
            }
        }
    }

    private void CancelSpeech()
    {
        _speechOperation++;
        Cancel(ref _restartCts);
        var previous = _speechCleanup.Task;
        var source = _speechCts;
        _speechCts = null;
        source?.Cancel();
        var recognizer = _activeRecognizer;
        _activeRecognizer = null;
        DetachSpeechHandler(recognizer);
        _speechCleanup.Track(CleanupAsync(), Notify);
        _isListening = _liveSpeechEnabled = _isSpeechStarting = _isSpeechStopping = false;

        async Task CleanupAsync()
        {
            await Observe(previous);
            try
            {
                if (recognizer?.IsListening == true)
                    await recognizer.StopAsync();
            }
            catch (Exception)
            {
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    internal static ChatMessage CreateMessage(string text, IReadOnlyList<DataContent> attachments)
    {
        var contents = new List<AIContent>(attachments.Count + 1);
        if (text.Length > 0)
            contents.Add(new TextContent(text));
        contents.AddRange(attachments);
        return new ChatMessage(ChatRole.User, contents);
    }

    private void RestoreDraft(string text, IReadOnlyList<DataContent> attachments)
    {
        // Only restore into an empty box: the user may already have started a new message.
        if (string.IsNullOrWhiteSpace(_view.Text))
            _view.Text = text;

        foreach (var attachment in attachments)
        {
            if (!_attachments.Contains(attachment))
                _attachments.Add(attachment);
        }
    }

    private bool IsAudioCurrent(int generation, int operation) =>
        generation == _generation && operation == _audioOperation;

    private bool IsSpeechCurrent(int generation, int operation, CancellationTokenSource? source) =>
        generation == _generation && operation == _speechOperation &&
        (source is null || ReferenceEquals(source, _speechCts));

    private void SetError(string message)
    {
        _errorMessage = message;
        Notify();
    }

    private void Notify() => _view.OnComposerChanged();

    private void Announce(string message)
    {
        LastAnnouncement = message;
        try
        {
            SemanticScreenReader.Default.Announce(message);
        }
        catch (Exception)
        {
            // No screen reader service on this platform (or in unit tests).
        }
    }

    private static async Task StopQuietlyAsync(IChatSpeechRecognizer recognizer)
    {
        try
        {
            if (recognizer.IsListening)
                await recognizer.StopAsync();
        }
        catch (Exception)
        {
            // The owning operation still has to release the microphone.
        }
    }

    private static async Task Observe(Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task;
        }
        catch (Exception)
        {
        }
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        var current = source;
        source = null;
        current?.Cancel();
    }

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!.Trim()));

    internal static string TooLargeMessage(long maxBytes) =>
        $"Attachments must be {FormatMegabytes(maxBytes)} MB or smaller.";

    private static string FormatMegabytes(long bytes) =>
        bytes % (1024L * 1024) == 0
            ? (bytes / (1024L * 1024)).ToString(CultureInfo.InvariantCulture)
            : (bytes / (double)(1024L * 1024)).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Tracks a microphone cleanup task so the next capture can wait for the microphone.</summary>
    private sealed class CleanupSlot
    {
        public Task? Task { get; private set; }

        public void Track(Task cleanup, Action onCompleted)
        {
            if (cleanup.IsCompleted)
            {
                if (cleanup.IsFaulted)
                    _ = cleanup.Exception;
                Task = null;
                return;
            }

            Task = cleanup;
            _ = WaitAsync(cleanup, onCompleted);
        }

        public Task AwaitAsync() => Observe(Task);

        private async Task WaitAsync(Task cleanup, Action onCompleted)
        {
            await Observe(cleanup);
            if (ReferenceEquals(Task, cleanup))
            {
                Task = null;
                onCompleted();
            }
        }
    }
}
