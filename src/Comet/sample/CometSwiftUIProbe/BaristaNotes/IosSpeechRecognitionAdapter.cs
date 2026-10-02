#nullable enable
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using CometBaristaNotes.Services.Voice;
using CoreFoundation;
using Foundation;
using Speech;

namespace CometSwiftUIProbe.BaristaNotes;

sealed class IosSpeechRecognitionAdapter : IPlatformSpeechRecognizer
{
    readonly SFSpeechRecognizer _recognizer;
    AVAudioEngine? _audioEngine;
    SFSpeechAudioBufferRecognitionRequest? _request;
    SFSpeechRecognitionTask? _task;
    TaskCompletionSource<PlatformSpeechRecognitionResult>? _recognition;
    CancellationTokenRegistration _cancellationRegistration;
    string? _lastPartial;
    bool _cancelRequested;
    bool _tapInstalled;
    bool _disposed;
    int _generation;

    public IosSpeechRecognitionAdapter()
    {
        _recognizer = new SFSpeechRecognizer(
            NSLocale.FromLocaleIdentifier(CultureInfo.CurrentCulture.Name));
    }

    public event EventHandler<string>? PartialResultReceived;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!_disposed && _recognizer.Available);

    public Task<SpeechPermissionStatus> GetPermissionStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var speech = MapSpeechAuthorization(SFSpeechRecognizer.AuthorizationStatus);
        var microphone = MapMicrophoneAuthorization(
            AVAudioSession.SharedInstance().RecordPermission);
        return Task.FromResult(MergePermissions(speech, microphone));
    }

    public async Task<SpeechPermissionStatus> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        var speech = await RequestSpeechPermissionAsync(cancellationToken);
        if (speech != SpeechPermissionStatus.Granted)
            return speech;
        var microphone = await RequestMicrophonePermissionAsync(cancellationToken);
        return MergePermissions(speech, microphone);
    }

    public Task<PlatformSpeechRecognitionResult> StartListeningAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromResult(new PlatformSpeechRecognitionResult(
                false, null, 0, "Speech recognition has been disposed."));
        if (_recognition is not null)
            return Task.FromResult(new PlatformSpeechRecognitionResult(
                false, null, 0, "Already listening"));

        var completion = new TaskCompletionSource<PlatformSpeechRecognitionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _recognition = completion;
        var generation = Interlocked.Increment(ref _generation);
        _lastPartial = null;
        _cancelRequested = false;
        _cancellationRegistration = cancellationToken.Register(() =>
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (generation != _generation)
                    return;
                _cancelRequested = true;
                StopAudio(cancel: true);
                Complete(new(false, _lastPartial, 0, "Cancelled", Cancelled: true), generation);
            }));

        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (generation != _generation || _recognition != completion)
                return;
            try
            {
                var session = AVAudioSession.SharedInstance();
                session.SetCategory(
                    AVAudioSessionCategory.Record,
                    AVAudioSessionCategoryOptions.DuckOthers);
                session.SetMode(AVAudioSession.ModeMeasurement, out var modeError);
                if (modeError is not null)
                    throw new InvalidOperationException(modeError.LocalizedDescription);
                session.SetActive(true, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);

                _audioEngine = new AVAudioEngine();
                _request = new SFSpeechAudioBufferRecognitionRequest
                {
                    ShouldReportPartialResults = true,
                    TaskHint = SFSpeechRecognitionTaskHint.Dictation,
                };
                var input = _audioEngine.InputNode;
                var format = input.GetBusOutputFormat(0);
                input.InstallTapOnBus(0, 1024, format, (buffer, _) => _request?.Append(buffer));
                _tapInstalled = true;

                _task = _recognizer.GetRecognitionTask(_request, (result, error) =>
                    DispatchQueue.MainQueue.DispatchAsync(() =>
                        HandleRecognitionResult(result, error, generation)));

                _audioEngine.Prepare();
                if (!_audioEngine.StartAndReturnError(out var startError))
                    throw new InvalidOperationException(
                        startError?.LocalizedDescription ?? "Unable to start microphone capture.");
            }
            catch (Exception ex)
            {
                StopAudio(cancel: true);
                Complete(new(false, null, 0, ex.Message), generation);
            }
        });

        return completion.Task;
    }

    public Task StopListeningAsync(CancellationToken cancellationToken = default) =>
        OnMainQueueAsync(() => StopAudio(cancel: false));

    public Task CancelListeningAsync() => OnMainQueueAsync(() =>
    {
        _cancelRequested = true;
        StopAudio(cancel: true);
        Complete(
            new(false, _lastPartial, 0, "Cancelled", Cancelled: true),
            _generation);
    });

    static Task<SpeechPermissionStatus> RequestSpeechPermissionAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<SpeechPermissionStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        SFSpeechRecognizer.RequestAuthorization(status =>
            completion.TrySetResult(MapSpeechAuthorization(status)));
        return completion.Task;
    }

    static Task<SpeechPermissionStatus> RequestMicrophonePermissionAsync(
        CancellationToken cancellationToken)
    {
        var session = AVAudioSession.SharedInstance();
        var status = MapMicrophoneAuthorization(session.RecordPermission);
        if (status != SpeechPermissionStatus.Unknown)
            return Task.FromResult(status);

        var completion = new TaskCompletionSource<SpeechPermissionStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        session.RequestRecordPermission(granted =>
            completion.TrySetResult(
                granted ? SpeechPermissionStatus.Granted : SpeechPermissionStatus.Denied));
        return completion.Task;
    }

    static SpeechPermissionStatus MapSpeechAuthorization(
        SFSpeechRecognizerAuthorizationStatus status) => status switch
    {
        SFSpeechRecognizerAuthorizationStatus.Authorized => SpeechPermissionStatus.Granted,
        SFSpeechRecognizerAuthorizationStatus.Denied => SpeechPermissionStatus.Denied,
        SFSpeechRecognizerAuthorizationStatus.Restricted => SpeechPermissionStatus.Restricted,
        _ => SpeechPermissionStatus.Unknown,
    };

    static SpeechPermissionStatus MapMicrophoneAuthorization(
        AVAudioSessionRecordPermission status) => status switch
    {
        AVAudioSessionRecordPermission.Granted => SpeechPermissionStatus.Granted,
        AVAudioSessionRecordPermission.Denied => SpeechPermissionStatus.Denied,
        _ => SpeechPermissionStatus.Unknown,
    };

    static SpeechPermissionStatus MergePermissions(
        SpeechPermissionStatus speech,
        SpeechPermissionStatus microphone)
    {
        if (speech == SpeechPermissionStatus.Restricted ||
            microphone == SpeechPermissionStatus.Restricted)
            return SpeechPermissionStatus.Restricted;
        if (speech == SpeechPermissionStatus.Denied ||
            microphone == SpeechPermissionStatus.Denied)
            return SpeechPermissionStatus.Denied;
        if (speech == SpeechPermissionStatus.Granted &&
            microphone == SpeechPermissionStatus.Granted)
            return SpeechPermissionStatus.Granted;
        return SpeechPermissionStatus.Unknown;
    }

    void HandleRecognitionResult(
        SFSpeechRecognitionResult? result,
        NSError? error,
        int generation)
    {
        if (generation != _generation)
            return;
        if (result is not null)
        {
            var transcript = result.BestTranscription.FormattedString;
            _lastPartial = transcript;
            if (!string.IsNullOrWhiteSpace(transcript) && !result.Final)
                PartialResultReceived?.Invoke(this, transcript);
            if (result.Final)
                Complete(new(true, transcript, 1, null), generation);
        }
        else if (error is not null)
        {
            Complete(new(
                false,
                _lastPartial,
                0,
                _cancelRequested ? "Cancelled" : error.LocalizedDescription,
                _cancelRequested),
                generation);
        }
    }

    void StopAudio(bool cancel)
    {
        if (_audioEngine?.Running == true)
            _audioEngine.Stop();
        if (_tapInstalled)
        {
            _audioEngine?.InputNode.RemoveTapOnBus(0);
            _tapInstalled = false;
        }
        if (cancel)
            _task?.Cancel();
        else
            _request?.EndAudio();
    }

    void Complete(PlatformSpeechRecognitionResult result, int generation)
    {
        if (generation != _generation)
            return;
        var completion = Interlocked.Exchange(ref _recognition, null);
        if (completion is null)
            return;

        _cancellationRegistration.Dispose();
        StopAudio(cancel: false);
        completion.TrySetResult(result);
        _task?.Dispose();
        _request?.Dispose();
        _audioEngine?.Dispose();
        _task = null;
        _request = null;
        _audioEngine = null;
        AVAudioSession.SharedInstance().SetActive(
            false,
            AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
    }

    static Task OnMainQueueAsync(Action action)
    {
        if (NSThread.IsMain)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await OnMainQueueAsync(() =>
        {
            Interlocked.Increment(ref _generation);
            _cancelRequested = true;
            StopAudio(cancel: true);
            Interlocked.Exchange(ref _recognition, null)?.TrySetResult(
                new(false, _lastPartial, 0, "Cancelled", Cancelled: true));
            _cancellationRegistration.Dispose();
            _task?.Dispose();
            _request?.Dispose();
            _audioEngine?.Dispose();
            _task = null;
            _request = null;
            _audioEngine = null;
            AVAudioSession.SharedInstance().SetActive(
                false,
                AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
            _recognizer.Dispose();
        });
    }
}
