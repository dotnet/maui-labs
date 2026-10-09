#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using CometBaristaNotes.Services.Voice;

namespace CometComposeProbe.BaristaNotes;

sealed class AndroidSpeechRecognitionAdapter : Java.Lang.Object, IPlatformSpeechRecognizer, IRecognitionListener
{
    public const int AudioPermissionRequestCode = 7104;

    readonly Activity _activity;
    SpeechRecognizer? _recognizer;
    TaskCompletionSource<PlatformSpeechRecognitionResult>? _recognition;
    TaskCompletionSource<SpeechPermissionStatus>? _permission;
    CancellationTokenRegistration _cancellationRegistration;
    string? _lastPartial;
    bool _disposed;

    public AndroidSpeechRecognitionAdapter(Activity activity) => _activity = activity;

    public event EventHandler<string>? PartialResultReceived;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!_disposed && SpeechRecognizer.IsRecognitionAvailable(_activity));

    public Task<SpeechPermissionStatus> GetPermissionStatusAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _activity.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted
                ? SpeechPermissionStatus.Granted
                : SpeechPermissionStatus.Unknown);

    public Task<SpeechPermissionStatus> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_activity.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted)
            return Task.FromResult(SpeechPermissionStatus.Granted);

        var completion = new TaskCompletionSource<SpeechPermissionStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _permission = completion;
        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _activity.RunOnUiThread(() =>
            _activity.RequestPermissions(
                new[] { Manifest.Permission.RecordAudio },
                AudioPermissionRequestCode));
        return completion.Task;
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
        _lastPartial = null;
        _cancellationRegistration = cancellationToken.Register(() =>
        {
            _activity.RunOnUiThread(() => _recognizer?.Cancel());
            Complete(new(false, _lastPartial, 0, "Cancelled", Cancelled: true));
        });

        _activity.RunOnUiThread(() =>
        {
            try
            {
                _recognizer?.Destroy();
                _recognizer = SpeechRecognizer.CreateSpeechRecognizer(_activity);
                _recognizer.SetRecognitionListener(this);

                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(
                    RecognizerIntent.ExtraLanguageModel,
                    RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraLanguage, CultureInfo.CurrentCulture.Name);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                intent.PutExtra(RecognizerIntent.ExtraMaxResults, 3);
                _recognizer.StartListening(intent);
            }
            catch (Exception ex)
            {
                Complete(new(false, null, 0, ex.Message));
            }
        });
        return completion.Task;
    }

    public Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        _activity.RunOnUiThread(() => _recognizer?.StopListening());
        return Task.CompletedTask;
    }

    public Task CancelListeningAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        _activity.RunOnUiThread(() => _recognizer?.Cancel());
        Complete(new(false, _lastPartial, 0, "Cancelled", Cancelled: true));
        return Task.CompletedTask;
    }

    public void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        if (requestCode != AudioPermissionRequestCode)
            return;
        var status = grantResults.Length > 0 && grantResults[0] == Permission.Granted
            ? SpeechPermissionStatus.Granted
            : SpeechPermissionStatus.Denied;
        _permission?.TrySetResult(status);
        _permission = null;
    }

    public void OnPartialResults(Bundle? partialResults)
    {
        var transcript = FirstTranscript(partialResults);
        if (string.IsNullOrWhiteSpace(transcript))
            return;
        _lastPartial = transcript;
        PartialResultReceived?.Invoke(this, transcript);
    }

    public void OnResults(Bundle? results)
    {
        var transcript = FirstTranscript(results) ?? _lastPartial;
        var confidence = FirstConfidence(results);
        Complete(string.IsNullOrWhiteSpace(transcript)
            ? new(false, null, 0, "No speech recognized")
            : new(true, transcript, confidence, null));
    }

    public void OnError(SpeechRecognizerError error)
    {
        var cancelled = error == SpeechRecognizerError.Client &&
            _recognition?.Task.IsCompleted != false;
        Complete(new(
            false,
            _lastPartial,
            0,
            cancelled ? "Cancelled" : ErrorMessage(error),
            cancelled));
    }

    public void OnBeginningOfSpeech() { }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? @params) { }
    public void OnReadyForSpeech(Bundle? @params) { }
    public void OnRmsChanged(float rmsdB) { }

    static string? FirstTranscript(Bundle? bundle) =>
        bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition) is IList<string> values &&
        values.Count > 0
            ? values[0]
            : null;

    static double FirstConfidence(Bundle? bundle)
    {
        var values = bundle?.GetFloatArray(SpeechRecognizer.ConfidenceScores);
        return values is { Length: > 0 } && values[0] >= 0 ? values[0] : 1;
    }

    static string ErrorMessage(SpeechRecognizerError error) => error switch
    {
        SpeechRecognizerError.Audio => "The microphone could not capture audio.",
        SpeechRecognizerError.InsufficientPermissions =>
            "Microphone permission is required. Enable it in Settings.",
        SpeechRecognizerError.Network or SpeechRecognizerError.NetworkTimeout =>
            "Speech recognition needs a network connection.",
        SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout =>
            "No speech recognized. Hold the microphone and try again.",
        SpeechRecognizerError.RecognizerBusy => "Speech recognition is busy. Please try again.",
        SpeechRecognizerError.Server => "The speech recognition service returned an error.",
        _ => $"Speech recognition failed ({error}).",
    };

    void Complete(PlatformSpeechRecognitionResult result)
    {
        var completion = Interlocked.Exchange(ref _recognition, null);
        if (completion is null)
            return;
        _cancellationRegistration.Dispose();
        completion.TrySetResult(result);
        _activity.RunOnUiThread(() =>
        {
            _recognizer?.Destroy();
            _recognizer?.Dispose();
            _recognizer = null;
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellationRegistration.Dispose();
        Interlocked.Exchange(ref _recognition, null)?.TrySetResult(
            new(false, _lastPartial, 0, "Cancelled", Cancelled: true));
        _permission?.TrySetResult(SpeechPermissionStatus.Denied);
        _permission = null;
        await RunOnUiThreadAsync(() =>
        {
            _recognizer?.Cancel();
            _recognizer?.Destroy();
            _recognizer?.Dispose();
            _recognizer = null;
        });
    }

    Task RunOnUiThreadAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _activity.RunOnUiThread(() =>
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
}
