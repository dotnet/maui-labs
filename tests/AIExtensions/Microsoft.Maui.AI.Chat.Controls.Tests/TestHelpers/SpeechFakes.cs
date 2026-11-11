using CommunityToolkit.Maui.Media;
using SpeechToTextOptions = CommunityToolkit.Maui.Media.SpeechToTextOptions;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>
/// The Community Toolkit speech-to-text service, raising updates the way CommunityToolkit.Maui.Core 14.0.1 does on
/// each platform (see its <c>SpeechToTextImplementation.*.cs</c>).
/// </summary>
internal sealed class FakeSpeechToText : ISpeechToText
{
    public event EventHandler<SpeechToTextRecognitionResultUpdatedEventArgs>? RecognitionResultUpdated;

    public event EventHandler<SpeechToTextRecognitionResultCompletedEventArgs>? RecognitionResultCompleted;

    public event EventHandler<SpeechToTextStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the state: while listening, <see cref="SpeechToTextState.Listening"/> (Windows also reports it while
    /// capturing in silence) unless the test said the platform reports silence.
    /// </summary>
    public SpeechToTextState CurrentState =>
        !IsListening ? SpeechToTextState.Stopped
        : _silence ? SpeechToTextState.Silence
        : SpeechToTextState.Listening;

    /// <summary>Gets whether the microphone is open.</summary>
    public bool IsListening { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public SpeechToTextOptions? LastOptions { get; private set; }

    /// <summary>Gets or sets a failure raised after the microphone opened, as the iOS service can.</summary>
    public Exception? StartError { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    /// <summary>Gets or sets work done while stopping, before the stop completes.</summary>
    public Action? Stopping { get; set; }

    public int HandlerCount =>
        (RecognitionResultUpdated?.GetInvocationList().Length ?? 0) +
        (RecognitionResultCompleted?.GetInvocationList().Length ?? 0) +
        (StateChanged?.GetInvocationList().Length ?? 0);

    private bool _silence;

    public async Task StartListenAsync(SpeechToTextOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        LastOptions = options;
        if (StartGate is not null)
            await StartGate.Task;

        IsListening = true;
        if (StartError is not null)
            throw StartError;
    }

    public async Task StopListenAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        Stopping?.Invoke();
        if (StopGate is not null)
            await StopGate.Task;
        IsListening = false;
    }

    public Task<bool> RequestPermissions(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>iOS and Mac Catalyst: each new segment (word) of the best transcription, once.</summary>
    public void SayOnApple(string words)
    {
        foreach (var word in words.Split(' '))
            Update(word);
    }

    /// <summary>Windows: sound starts, then once it ends, the recognized phrase, once.</summary>
    public void SayOnWindows(string phrase)
    {
        HearSound();
        HearSilence();
        Update(phrase);
    }

    /// <summary>Windows: the platform hears sound (a phrase it reports only once the sound ends).</summary>
    public void HearSound() => SetState(silence: false);

    /// <summary>Windows: the platform no longer hears sound.</summary>
    public void HearSilence() => SetState(silence: true);

    private void SetState(bool silence)
    {
        _silence = silence;
        StateChanged?.Invoke(this, new SpeechToTextStateChangedEventArgs(CurrentState));
    }

    /// <summary>Android: the whole text heard so far, growing word by word.</summary>
    public void SayOnAndroid(string words)
    {
        var split = words.Split(' ');
        for (var count = 1; count <= split.Length; count++)
            Update(string.Join(' ', split.Take(count)));
    }

    public void Update(string text) =>
        RecognitionResultUpdated?.Invoke(this, new SpeechToTextRecognitionResultUpdatedEventArgs(text));

    /// <summary>Ends the pass from the platform's side, with its final text or a failure.</summary>
    public void Complete(string? text, Exception? exception = null)
    {
        IsListening = false;
        RecognitionResultCompleted?.Invoke(
            this,
            new SpeechToTextRecognitionResultCompletedEventArgs(new SpeechToTextResult(text, exception)));
    }
}

/// <summary>A clock the test moves forward, so timers fire exactly when the test says.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan time)
    {
        _now += time;
        foreach (var timer in _timers.ToArray())
            timer.FireIfDue();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            owner._timers.Remove(this);
            if (_due is not null)
                owner._timers.Add(this);
            return true;
        }

        public void FireIfDue()
        {
            if (_due is not { } due || due > owner._now)
                return;

            // One-shot: Task.Delay never uses a period.
            Dispose();
            callback(state);
        }

        public void Dispose()
        {
            _due = null;
            owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A recognizer as an app sees it once the user allowed the microphone and speech recognition.</summary>
internal sealed class PermittedSpeechRecognizer(IChatSpeechRecognizer inner) : IChatSpeechRecognizer
{
    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged
    {
        add => inner.RecognitionChanged += value;
        remove => inner.RecognitionChanged -= value;
    }

    public bool IsSupported => true;

    public bool IsListening => inner.IsListening;

    public Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task StartAsync(System.Globalization.CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default) =>
        inner.StartAsync(culture, reportPartialResults, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
}
