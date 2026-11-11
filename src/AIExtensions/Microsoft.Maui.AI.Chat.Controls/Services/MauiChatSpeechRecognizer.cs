using System.Globalization;
using CommunityToolkit.Maui.Media;
using SpeechToTextOptions = CommunityToolkit.Maui.Media.SpeechToTextOptions;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>How a platform speech recognizer reports what it heard during a recognition pass.</summary>
internal enum SpeechUpdateKind
{
    /// <summary>Each update is everything heard so far, and the platform ends a pass when the speaker pauses.</summary>
    Cumulative,

    /// <summary>Each update is one new word as soon as it is heard, and the platform never ends a pass itself.</summary>
    Words,

    /// <summary>Each update is one phrase once the speaker finishes it, and the platform never ends a pass itself.</summary>
    Phrases,
}

/// <summary>
/// The default <see cref="IChatSpeechRecognizer"/>: the Community Toolkit speech-to-text service, shaped into the
/// cumulative interim results and the single final result per pass that the chat view expects.
/// </summary>
/// <remarks>
/// <para>
/// The toolkit (14.0.1) reports a pass differently on each platform. Android raises the whole text so far and ends
/// the pass when the speaker pauses. iOS and Mac Catalyst raise each new word once, and Windows each finished
/// phrase once; neither ends a pass until it is stopped, and stopping cancels it on iOS and Mac Catalyst, so its
/// final result never arrives. For those platforms this recognizer joins the updates and ends the pass itself once
/// nothing new arrives for <see cref="EndOfSpeechTimeout"/>; on Windows, which reports a phrase only after it ends,
/// also only while the platform reports silence.
/// </para>
/// <para>
/// Toolkit calls run one at a time: a pass starts only once the previous one has released the microphone, and a
/// stop requested while a pass is starting releases it once the start completes. The toolkit raises events on the
/// main thread (Android, iOS, Mac Catalyst) or a background thread (Windows); each is moved to the thread that
/// started the pass, so a pass's state is only touched there.
/// </para>
/// </remarks>
internal sealed class MauiChatSpeechRecognizer : IChatSpeechRecognizer
{
    /// <summary>The pause after which a pass ends on platforms that never end one themselves.</summary>
    internal static readonly TimeSpan DefaultEndOfSpeechTimeout = TimeSpan.FromSeconds(2);

    private readonly ISpeechToText? _speechToText;
    private readonly TimeProvider _timeProvider;
    private SpeechUpdateKind? _updateKind;
    private Pass? _pass;

    // Completes once every toolkit start and stop requested so far has finished. Never faults.
    private Task _toolkitIdle = Task.CompletedTask;
    private bool? _isSupported;

    public MauiChatSpeechRecognizer()
        : this(speechToText: null, updateKind: null, TimeProvider.System)
    {
    }

    /// <summary>Creates a recognizer over <paramref name="speechToText"/> (by default the toolkit's).</summary>
    internal MauiChatSpeechRecognizer(ISpeechToText? speechToText, SpeechUpdateKind? updateKind, TimeProvider timeProvider)
    {
        _speechToText = speechToText;
        _updateKind = updateKind;
        _timeProvider = timeProvider;
    }

    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    /// <summary>Gets or sets how long a pause ends a pass on iOS, Mac Catalyst, and Windows.</summary>
    internal TimeSpan EndOfSpeechTimeout { get; set; } = DefaultEndOfSpeechTimeout;

    public bool IsSupported => _isSupported ??= GetIsSupported();

    public bool IsListening => _pass is not null;

    private ISpeechToText SpeechToTextService => _speechToText ?? SpeechToText.Default;

    private static bool GetIsSupported()
    {
        try
        {
            var platform = DeviceInfo.Platform;
            return platform == DevicePlatform.Android
                || platform == DevicePlatform.iOS
                || platform == DevicePlatform.MacCatalyst
                || platform == DevicePlatform.WinUI
                || platform == DevicePlatform.Tizen;
        }
        catch
        {
            // No platform (unit tests, unsupported hosts).
            return false;
        }
    }

    private static SpeechUpdateKind GetUpdateKind()
    {
        try
        {
            var platform = DeviceInfo.Platform;
            return platform == DevicePlatform.WinUI ? SpeechUpdateKind.Phrases
                : platform == DevicePlatform.iOS || platform == DevicePlatform.MacCatalyst ? SpeechUpdateKind.Words
                : SpeechUpdateKind.Cumulative;
        }
        catch
        {
            return SpeechUpdateKind.Cumulative;
        }
    }

    public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();
        if (microphone != PermissionStatus.Granted)
            return false;

        return await SpeechToTextService.RequestPermissions(cancellationToken);
    }

    public async Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        cancellationToken.ThrowIfCancellationRequested();

        if (_pass is not null)
            throw new InvalidOperationException("Speech recognition is already active.");

        var pass = new Pass(this, SpeechToTextService, _updateKind ??= GetUpdateKind(), reportPartialResults, SynchronizationContext.Current);
        _pass = pass;

        // A stop requested while the toolkit starts runs once the start has settled.
        var previous = _toolkitIdle;
        var started = new TaskCompletionSource();
        _toolkitIdle = WhenIdleAsync(previous, started.Task);
        try
        {
            // The previous pass may still be releasing the microphone.
            await previous.WaitAsync(cancellationToken);
            pass.Attach();

            // Partial results are the only way to hear a pass on iOS, Mac Catalyst, and Windows.
            await pass.SpeechToText.StartListenAsync(
                new SpeechToTextOptions { Culture = culture, ShouldReportPartialResults = true },
                cancellationToken);
        }
        catch
        {
            started.SetResult();
            if (ReferenceEquals(_pass, pass))
            {
                _pass = null;

                // The toolkit can fail after it opened the microphone (on iOS it checks for cancellation after the
                // audio engine has started), so release it either way.
                if (pass.IsAttached)
                    await ObserveAsync(EndAsync(pass, raiseFinal: false));
            }

            throw;
        }

        started.SetResult();
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_pass is not { } pass)
            return _toolkitIdle;

        _pass = null;
        return EndAsync(pass, raiseFinal: false);
    }

    internal static ChatSpeechRecognitionErrorKind Classify(Exception exception) =>
        exception switch
        {
            PermissionException or UnauthorizedAccessException => ChatSpeechRecognitionErrorKind.PermissionDenied,
            CultureNotFoundException => ChatSpeechRecognitionErrorKind.LanguageNotSupported,
            OperationCanceledException => ChatSpeechRecognitionErrorKind.Aborted,
            HttpRequestException or TimeoutException => ChatSpeechRecognitionErrorKind.Transient,
            _ => ChatSpeechRecognitionErrorKind.Fatal,
        };

    private void OnUpdated(Pass pass, string update)
    {
        if (!pass.IsAttached || !pass.Add(update))
            return;

        RestartEndOfSpeechTimer(pass);
        if (pass.ReportPartialResults)
            Raise(new ChatSpeechRecognitionEventArgs(pass.Text, isFinal: false));
    }

    private void OnStateChanged(Pass pass)
    {
        // Windows reports sound starting and ending around each phrase, before the phrase itself.
        if (pass.Kind == SpeechUpdateKind.Phrases && pass.Text.Length > 0)
            RestartEndOfSpeechTimer(pass);
    }

    private void OnCompleted(Pass pass, SpeechToTextResult result)
    {
        // A pass this recognizer is ending reports its own final result once the toolkit has stopped.
        if (!ReferenceEquals(_pass, pass))
            return;

        _pass = null;
        pass.Detach();
        var final = result.Exception is { } exception
            ? new ChatSpeechRecognitionEventArgs(pass.Text, isFinal: true, Classify(exception), exception)
            : new ChatSpeechRecognitionEventArgs(string.IsNullOrWhiteSpace(result.Text) ? pass.Text : result.Text, isFinal: true);

        // The platform ended the pass. Stopping the toolkit as well releases the recognizer it keeps per pass.
        _ = EndAsync(pass, raiseFinal: false);
        Raise(final);
    }

    private void OnEndOfSpeech(Pass pass)
    {
        if (!ReferenceEquals(_pass, pass))
            return;

        // Windows reports a phrase only once the speaker finishes it: wait while the platform still hears sound.
        if (pass.Kind == SpeechUpdateKind.Phrases && pass.HearsSound())
        {
            RestartEndOfSpeechTimer(pass);
            return;
        }

        _pass = null;
        _ = EndAsync(pass, raiseFinal: true);
    }

    private void RestartEndOfSpeechTimer(Pass pass)
    {
        if (ReferenceEquals(_pass, pass) && pass.Kind != SpeechUpdateKind.Cumulative)
            pass.RestartEndOfSpeechTimer(EndOfSpeechTimeout, _timeProvider);
    }

    /// <summary>Stops the toolkit and detaches from it; with <paramref name="raiseFinal"/>, then reports the pass.</summary>
    private Task EndAsync(Pass pass, bool raiseFinal)
    {
        pass.StopEndOfSpeechTimer();
        var previous = _toolkitIdle;
        var ended = EndCoreAsync();
        _toolkitIdle = WhenIdleAsync(previous, ended);
        return ended;

        async Task EndCoreAsync()
        {
            // A start still in progress completes first, so this releases whatever it opened.
            await previous;
            try
            {
                // Updates the toolkit raises while it stops (Windows passes on the phrase it was still
                // recognizing) join the pass.
                await pass.SpeechToText.StopListenAsync(CancellationToken.None);
            }
            finally
            {
                pass.Detach();
                if (raiseFinal)
                    Raise(new ChatSpeechRecognitionEventArgs(pass.Text, isFinal: true));
            }
        }
    }

    private void Raise(ChatSpeechRecognitionEventArgs e) => RecognitionChanged?.Invoke(this, e);

    private static async Task WhenIdleAsync(Task previous, Task operation)
    {
        await previous;
        await ObserveAsync(operation);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // The caller of the operation sees its failure; the next toolkit call goes ahead anyway.
        }
    }

    /// <summary>One recognition pass: what was heard so far and the toolkit subscription that feeds it.</summary>
    private sealed class Pass(
        MauiChatSpeechRecognizer owner,
        ISpeechToText speechToText,
        SpeechUpdateKind kind,
        bool reportPartialResults,
        SynchronizationContext? context)
    {
        private CancellationTokenSource? _endOfSpeech;

        public ISpeechToText SpeechToText { get; } = speechToText;

        public SpeechUpdateKind Kind { get; } = kind;

        public bool ReportPartialResults { get; } = reportPartialResults;

        public bool IsAttached { get; private set; }

        public string Text { get; private set; } = string.Empty;

        public void Attach()
        {
            IsAttached = true;
            SpeechToText.RecognitionResultUpdated += OnRecognitionResultUpdated;
            SpeechToText.RecognitionResultCompleted += OnRecognitionResultCompleted;
            SpeechToText.StateChanged += OnStateChanged;
        }

        public void Detach()
        {
            IsAttached = false;
            SpeechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
            SpeechToText.RecognitionResultCompleted -= OnRecognitionResultCompleted;
            SpeechToText.StateChanged -= OnStateChanged;
        }

        /// <summary>Adds an update and reports whether the text heard so far changed.</summary>
        public bool Add(string update)
        {
            if (string.IsNullOrWhiteSpace(update))
                return false;

            var text = update.Trim();
            var next = Kind == SpeechUpdateKind.Cumulative || Text.Length == 0 ? text : $"{Text} {text}";
            if (next == Text)
                return false;

            Text = next;
            return true;
        }

        public bool HearsSound()
        {
            try
            {
                return SpeechToText.CurrentState == SpeechToTextState.Listening;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void RestartEndOfSpeechTimer(TimeSpan timeout, TimeProvider timeProvider)
        {
            StopEndOfSpeechTimer();
            var source = new CancellationTokenSource();
            _endOfSpeech = source;
            _ = WaitAsync();

            async Task WaitAsync()
            {
                try
                {
                    await Task.Delay(timeout, timeProvider, source.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Post(() =>
                {
                    // Ignore a timer that fired just as a newer word restarted it.
                    if (ReferenceEquals(_endOfSpeech, source))
                        owner.OnEndOfSpeech(this);
                });
            }
        }

        public void StopEndOfSpeechTimer()
        {
            var source = _endOfSpeech;
            _endOfSpeech = null;
            source?.Cancel();
            source?.Dispose();
        }

        private void OnRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e)
        {
            var update = e.RecognitionResult;
            Post(() => owner.OnUpdated(this, update));
        }

        private void OnRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
        {
            var result = e.RecognitionResult;
            Post(() => owner.OnCompleted(this, result));
        }

        private void OnStateChanged(object? sender, SpeechToTextStateChangedEventArgs e) =>
            Post(() => owner.OnStateChanged(this));

        private void Post(Action action)
        {
            if (context is null || SynchronizationContext.Current == context)
                action();
            else
                context.Post(static state => ((Action)state!)(), action);
        }
    }
}
