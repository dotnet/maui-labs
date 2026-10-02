#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Voice;

public sealed class PushToTalkVoiceService : IAsyncDisposable
{
    readonly ISpeechRecognitionService _speech;
    readonly IBaristaVoiceCommandService _commands;
    readonly IBaristaVoiceCallbacks _callbacks;
    readonly Action<Action> _dispatch;
    readonly SemaphoreSlim _gate = new(1, 1);

    CancellationTokenSource? _listenCts;
    Task<SpeechRecognitionResultDto>? _listenTask;
    Task? _observerTask;
    string _partialTranscript = "";
    bool _disposed;

    public PushToTalkVoiceService(
        ISpeechRecognitionService speech,
        IBaristaVoiceCommandService commands,
        IBaristaVoiceCallbacks callbacks,
        Action<Action>? dispatch = null)
    {
        _speech = speech;
        _commands = commands;
        _callbacks = callbacks;
        _dispatch = dispatch ?? (action => action());
        _speech.PartialResultReceived += OnPartialResultReceived;
        CurrentState = UnconfiguredState;
    }

    public VoiceOverlayState CurrentState { get; private set; }

    public event EventHandler<VoiceOverlayState>? StateChanged;

    public static VoiceOverlayState UnconfiguredState { get; } = new(
        VoiceSessionStatus.Unconfigured,
        SpeechRecognitionState.Idle,
        CommandStatus.Cancelled,
        "Voice unavailable",
        ErrorMessage: "The platform speech adapter has not been configured.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _speech.IsAvailableAsync())
        {
            Publish(new(
                VoiceSessionStatus.Unavailable,
                SpeechRecognitionState.Error,
                CommandStatus.Failed,
                "Voice unavailable",
                ErrorMessage: "Speech recognition is not available on this device."));
            return;
        }

        Publish(Ready());
    }

    public Task HandlePushToTalkAsync(
        PushToTalkPhase phase,
        CancellationToken cancellationToken = default) => phase switch
        {
            PushToTalkPhase.Started => StartAsync(cancellationToken),
            PushToTalkPhase.Completed => CompleteAsync(cancellationToken),
            PushToTalkPhase.Cancelled => CancelAsync(),
            _ => Task.CompletedTask,
        };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentState.Status is VoiceSessionStatus.Listening or VoiceSessionStatus.Processing)
                return;

            if (!await _speech.IsAvailableAsync())
            {
                Publish(new(
                    VoiceSessionStatus.Unavailable,
                    SpeechRecognitionState.Error,
                    CommandStatus.Failed,
                    "Voice unavailable",
                    ErrorMessage: "Speech recognition is not available on this device."));
                return;
            }

            Publish(new(
                VoiceSessionStatus.PermissionRequired,
                SpeechRecognitionState.Idle,
                CommandStatus.Listening,
                "Microphone permission required"));
            if (!await _speech.RequestPermissionsAsync())
            {
                Publish(new(
                    VoiceSessionStatus.PermissionDenied,
                    SpeechRecognitionState.Error,
                    CommandStatus.Failed,
                    "Permission required",
                    ErrorMessage:
                        "Microphone and speech recognition permission are required. Enable them in Settings."));
                return;
            }

            _listenCts?.Dispose();
            _listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _partialTranscript = "";
            Publish(new(
                VoiceSessionStatus.Listening,
                SpeechRecognitionState.Listening,
                CommandStatus.Listening,
                "Listening..."));
            _listenTask = _speech.StartListeningAsync(_listenCts.Token);
            _observerTask = ObserveRecognitionAsync(_listenTask, _listenCts);
        }
        catch (OperationCanceledException)
        {
            Publish(Cancelled());
        }
        catch (Exception ex)
        {
            CleanupSession(_listenCts);
            Publish(Error(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_listenTask is null)
                return;

            var listening = _listenTask;
            var sessionCts = _listenCts;
            await _speech.StopListeningAsync();
            SpeechRecognitionResultDto result;
            try
            {
                result = await listening.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                _listenCts?.Cancel();
                result = new(false, null, 0, "Speech recognition timed out.");
            }
            _listenTask = null;
            using var processCts = sessionCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    sessionCts.Token);
            await ProcessResultAsync(result, processCts.Token);
            CleanupSession(sessionCts);
        }
        catch (OperationCanceledException)
        {
            await CancelCoreAsync();
        }
        catch (Exception ex)
        {
            CleanupSession(_listenCts);
            Publish(Error(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        ThrowIfDisposed();
        _listenCts?.Cancel();
        await _gate.WaitAsync();
        try
        {
            await CancelCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeactivateAsync() =>
        CurrentState.Status is VoiceSessionStatus.Listening or VoiceSessionStatus.Processing ||
        _listenTask is not null
            ? CancelAsync()
            : Task.CompletedTask;

    public void Reset()
    {
        ThrowIfDisposed();
        if (CurrentState.Status is VoiceSessionStatus.Listening or VoiceSessionStatus.Processing)
            return;
        _partialTranscript = "";
        Publish(Ready());
    }

    async Task CancelCoreAsync()
    {
        var sessionCts = _listenCts;
        var listening = _listenTask;
        sessionCts?.Cancel();
        if (listening is not null)
        {
            await _speech.StopListeningAsync();
            try { await listening; }
            catch (OperationCanceledException) { }
            catch { }
        }
        _listenTask = null;
        CleanupSession(sessionCts);
        _partialTranscript = "";
        Publish(Cancelled());
    }

    async Task ObserveRecognitionAsync(
        Task<SpeechRecognitionResultDto> listening,
        CancellationTokenSource sessionCts)
    {
        SpeechRecognitionResultDto result;
        try
        {
            result = await listening;
        }
        catch (OperationCanceledException)
        {
            result = new(false, null, 0, "Cancelled");
        }
        catch (Exception ex)
        {
            result = new(false, null, 0, ex.Message);
        }

        try
        {
            await _gate.WaitAsync();
            try
            {
                if (_listenTask != listening)
                    return;
                _listenTask = null;
                await ProcessResultAsync(result, sessionCts.Token);
            }
            catch (OperationCanceledException)
            {
                Publish(Cancelled());
            }
            catch (Exception ex)
            {
                Publish(Error(ex.Message));
            }
            finally
            {
                CleanupSession(sessionCts);
                _gate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposal owns final cleanup and state.
        }
    }

    async Task ProcessResultAsync(
        SpeechRecognitionResultDto result,
        CancellationToken cancellationToken)
    {
        if (!result.Success &&
            string.Equals(result.ErrorMessage, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            Publish(Cancelled());
            return;
        }
        if (!result.Success &&
            result.ErrorMessage?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true)
        {
            Publish(Error(result.ErrorMessage));
            return;
        }

        var transcript = string.IsNullOrWhiteSpace(result.Transcript)
            ? _partialTranscript
            : result.Transcript.Trim();

        if (!result.Success && string.IsNullOrWhiteSpace(transcript))
        {
            Publish(Error(result.ErrorMessage ?? "No speech recognized."));
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            Publish(Error("No speech recognized. Hold the microphone and try again."));
            return;
        }

        Publish(new(
            VoiceSessionStatus.Processing,
            SpeechRecognitionState.Processing,
            CommandStatus.Processing,
            "Processing...",
            transcript));

        var commandResult = await _commands.ProcessAsync(transcript, cancellationToken);
        if (commandResult.Success)
        {
            Publish(new(
                VoiceSessionStatus.Completed,
                SpeechRecognitionState.Idle,
                CommandStatus.Completed,
                "Done",
                Response: commandResult.Message));
        }
        else if (string.Equals(commandResult.Message, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            Publish(Cancelled());
        }
        else
        {
            Publish(Error(commandResult.Message));
        }
    }

    void OnPartialResultReceived(object? sender, string transcript)
    {
        if (CurrentState.Status != VoiceSessionStatus.Listening)
            return;
        _partialTranscript = transcript?.Trim() ?? "";
        Publish(CurrentState with { Transcript = _partialTranscript });
    }

    void Publish(VoiceOverlayState state)
    {
        CurrentState = state;
        void Notify()
        {
            try { StateChanged?.Invoke(this, state); }
            catch { }
            try { _callbacks.OnVoiceStateChanged(state); }
            catch { }
        }

        try { _dispatch(Notify); }
        catch { Notify(); }
    }

    void CleanupSession(CancellationTokenSource? sessionCts)
    {
        if (sessionCts is null || !ReferenceEquals(_listenCts, sessionCts))
            return;
        _listenCts = null;
        sessionCts.Dispose();
    }

    static VoiceOverlayState Ready() => new(
        VoiceSessionStatus.Ready,
        SpeechRecognitionState.Idle,
        CommandStatus.Completed,
        "Ready");

    static VoiceOverlayState Cancelled() => new(
        VoiceSessionStatus.Cancelled,
        SpeechRecognitionState.Idle,
        CommandStatus.Cancelled,
        "Cancelled");

    static VoiceOverlayState Error(string message) => new(
        VoiceSessionStatus.Error,
        SpeechRecognitionState.Error,
        CommandStatus.Failed,
        "Error",
        ErrorMessage: message);

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _listenCts?.Cancel();
        await _gate.WaitAsync();
        try
        {
            if (_listenTask is not null || _listenCts is not null)
                await CancelCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
        if (_observerTask is not null)
        {
            try { await _observerTask; }
            catch { }
        }
        _speech.PartialResultReceived -= OnPartialResultReceived;
        if (_speech is IAsyncDisposable disposableSpeech)
            await disposableSpeech.DisposeAsync();
        _disposed = true;
        _gate.Dispose();
    }
}
