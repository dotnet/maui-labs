#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Voice;

public sealed class NativeSpeechRecognitionService : ISpeechRecognitionService, IAsyncDisposable
{
    readonly IPlatformSpeechRecognizer _platform;
    readonly TimeSpan _maximumListeningDuration;
    readonly bool _ownsPlatform;
    SpeechRecognitionState _state = SpeechRecognitionState.Idle;
    CancellationTokenSource? _currentCts;
    bool _disposed;

    public NativeSpeechRecognitionService(
        IPlatformSpeechRecognizer platform,
        TimeSpan? maximumListeningDuration = null,
        bool ownsPlatform = true)
    {
        _platform = platform;
        _maximumListeningDuration = maximumListeningDuration ?? TimeSpan.FromSeconds(60);
        _ownsPlatform = ownsPlatform;
        _platform.PartialResultReceived += OnPartialResultReceived;
    }

    public SpeechRecognitionState State => _state;

    public event EventHandler<SpeechRecognitionState>? StateChanged;
    public event EventHandler<string>? PartialResultReceived;

    public Task<bool> IsAvailableAsync()
    {
        ThrowIfDisposed();
        return _platform.IsAvailableAsync();
    }

    public async Task<bool> RequestPermissionsAsync()
    {
        ThrowIfDisposed();
        var status = await _platform.GetPermissionStatusAsync();
        if (status == SpeechPermissionStatus.Unknown)
            status = await _platform.RequestPermissionAsync();
        return status == SpeechPermissionStatus.Granted;
    }

    public async Task<SpeechRecognitionResultDto> StartListeningAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_state == SpeechRecognitionState.Listening)
            return new(false, null, 0, "Already listening");

        using var timeoutCts = new CancellationTokenSource(_maximumListeningDuration);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        _currentCts = linkedCts;
        SetState(SpeechRecognitionState.Listening);
        try
        {
            var result = await _platform.StartListeningAsync(linkedCts.Token);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                SetState(SpeechRecognitionState.Error);
                return new(false, result.Transcript, result.Confidence,
                    "Listening timed out. Please try again.");
            }
            if (result.Cancelled)
            {
                SetState(SpeechRecognitionState.Idle);
                return new(false, null, 0, "Cancelled");
            }

            SetState(SpeechRecognitionState.Processing);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Transcript))
            {
                SetState(SpeechRecognitionState.Error);
                return new(
                    false,
                    result.Transcript,
                    result.Confidence,
                    result.ErrorMessage ?? "No speech recognized");
            }

            SetState(SpeechRecognitionState.Idle);
            return new(true, result.Transcript, result.Confidence, null);
        }
        catch (OperationCanceledException)
        {
            await _platform.CancelListeningAsync();
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                SetState(SpeechRecognitionState.Error);
                return new(false, null, 0, "Listening timed out. Please try again.");
            }
            SetState(SpeechRecognitionState.Idle);
            return new(false, null, 0, "Cancelled");
        }
        catch (Exception ex)
        {
            SetState(SpeechRecognitionState.Error);
            return new(false, null, 0, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_currentCts, linkedCts))
                _currentCts = null;
        }
    }

    public Task StopListeningAsync()
    {
        ThrowIfDisposed();
        return _platform.StopListeningAsync();
    }

    void OnPartialResultReceived(object? sender, string transcript) =>
        PartialResultReceived?.Invoke(this, transcript);

    void SetState(SpeechRecognitionState state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _currentCts?.Cancel();
        _platform.PartialResultReceived -= OnPartialResultReceived;
        try
        {
            await _platform.CancelListeningAsync();
        }
        finally
        {
            if (_ownsPlatform)
                await _platform.DisposeAsync();
            _currentCts = null;
            SetState(SpeechRecognitionState.Idle);
        }
    }
}
