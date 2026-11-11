using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

internal static class Waiters
{
    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class FakeAttachmentPicker : IChatAttachmentPicker
{
    public List<DataContent> Result { get; } = [];

    public Exception? Error { get; set; }

    public TaskCompletionSource<IReadOnlyList<DataContent>>? Deferred { get; set; }

    public FilePickerFileType? SeenFileTypes { get; private set; }

    public long SeenLimit { get; private set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<DataContent>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        SeenFileTypes = fileTypes;
        SeenLimit = maxBytesPerFile;
        if (Error is not null)
            return Task.FromException<IReadOnlyList<DataContent>>(Error);
        if (Deferred is not null)
            return Deferred.Task;
        return Task.FromResult<IReadOnlyList<DataContent>>(Result.ToArray());
    }
}

internal sealed class FakeAudioRecorder(DataContent? recording = null) : IChatAudioRecorder
{
    public bool IsSupported { get; set; } = true;

    public bool IsRecording { get; private set; }

    public DataContent? Recording { get; set; } = recording;

    public Exception? StartError { get; set; }

    public Exception? StopError { get; set; }

    public TaskCompletionSource<DataContent?>? StopGate { get; set; }

    /// <summary>Gets or sets a gate the start waits for; the start then completes even if it was cancelled.</summary>
    public TaskCompletionSource? StartGate { get; set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int CancelCount { get; private set; }

    public long SeenMaximumBytes { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        if (StartError is not null)
            return Task.FromException(StartError);
        if (StartGate is { } gate)
            return StartLateAsync(gate);
        IsRecording = true;
        return Task.CompletedTask;

        async Task StartLateAsync(TaskCompletionSource gate)
        {
            await gate.Task;
            IsRecording = true;
        }
    }

    public Task<DataContent?> StopAsync(long maximumBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SeenMaximumBytes = maximumBytes;
        StopCount++;
        IsRecording = false;
        if (StopError is not null)
            return Task.FromException<DataContent?>(StopError);
        if (StopGate is not null)
            return StopGate.Task;
        return Task.FromResult(Recording);
    }

    public Task CancelAsync(CancellationToken cancellationToken = default)
    {
        CancelCount++;
        IsRecording = false;
        return Task.CompletedTask;
    }

    public static DataContent Wav(int bytes = 64, string name = "note.wav") =>
        new(new byte[bytes], "audio/wav") { Name = name };
}

internal sealed class FakeTranscriber : IChatAudioTranscriber
{
    private readonly string? _text;

    public FakeTranscriber(string? text) => _text = text;

    public FakeTranscriber()
    {
        Deferred = Waiters.Signal<string?>();
    }

    public TaskCompletionSource<string?>? Deferred { get; }

    public bool IgnoreCancellation { get; set; }

    public TaskCompletionSource Started { get; } = Waiters.Signal();

    public DataContent? SeenRecording { get; private set; }

    public async ValueTask<string?> TranscribeAsync(DataContent recording, CancellationToken cancellationToken = default)
    {
        SeenRecording = recording;
        Started.TrySetResult();
        if (Deferred is null)
            return _text;

        return IgnoreCancellation
            ? await Deferred.Task
            : await Deferred.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class FakeSpeechRecognizer : IChatSpeechRecognizer
{
    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    public bool IsSupported { get; set; } = true;

    public bool IsListening { get; private set; }

    public bool PermissionGranted { get; set; } = true;

    public TaskCompletionSource<bool>? PermissionGate { get; set; }

    public TaskCompletionSource PermissionRequested { get; } = Waiters.Signal();

    public Exception? StartError { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public bool ObserveStartCancellation { get; set; } = true;

    public Exception? StopError { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    public TaskCompletionSource StopStarted { get; } = Waiters.Signal();

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public List<int> StartThreadIds { get; } = [];

    public CultureInfo? LastCulture { get; private set; }

    public int HandlerCount => RecognitionChanged?.GetInvocationList().Length ?? 0;

    public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
    {
        PermissionRequested.TrySetResult();
        if (PermissionGate is not null)
            return await PermissionGate.Task;
        return PermissionGranted;
    }

    public async Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        StartThreadIds.Add(Environment.CurrentManagedThreadId);
        LastCulture = culture;
        if (StartError is not null)
            throw StartError;
        if (StartGate is not null)
        {
            if (ObserveStartCancellation)
                await StartGate.Task.WaitAsync(cancellationToken);
            else
                await StartGate.Task;
        }

        IsListening = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        StopStarted.TrySetResult();
        if (StopGate is not null)
            await StopGate.Task;
        IsListening = false;
        if (StopError is not null)
            throw StopError;
    }

    public void Emit(string text, bool isFinal, ChatSpeechRecognitionErrorKind errorKind = ChatSpeechRecognitionErrorKind.None)
    {
        if (isFinal)
            IsListening = false;
        RecognitionChanged?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal, errorKind, exception: null));
    }

    /// <summary>Captures the current handlers so a test can raise a late event after they detach.</summary>
    public Action<ChatSpeechRecognitionEventArgs> CaptureEmitter()
    {
        var handlers = RecognitionChanged;
        return args => handlers?.Invoke(this, args);
    }
}
