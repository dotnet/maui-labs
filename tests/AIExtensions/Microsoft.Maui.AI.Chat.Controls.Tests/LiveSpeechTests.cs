using System.Collections.Specialized;
using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class LiveSpeechTests
{
    [Fact]
    public void DefaultRecognizer_OnlyRetriesKnownTransientFailures()
    {
        Assert.Equal(ChatSpeechRecognitionErrorKind.Transient, MauiChatSpeechRecognizer.Classify(new HttpRequestException()));
        Assert.Equal(ChatSpeechRecognitionErrorKind.Transient, MauiChatSpeechRecognizer.Classify(new TimeoutException()));
        Assert.Equal(ChatSpeechRecognitionErrorKind.Fatal, MauiChatSpeechRecognizer.Classify(new InvalidOperationException()));
        Assert.Equal(ChatSpeechRecognitionErrorKind.Fatal, MauiChatSpeechRecognizer.Classify(new ArgumentException()));
    }

    [Fact]
    public void FinalUtterance_IsSentAndListeningResumesAfterTheResponse() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var client = new ScriptedChatClient().Reply("Hi!");
        var view = CreateView(recognizer, client);

        await view.Composer.ToggleLiveSpeechAsync();
        Assert.True(view.IsListening);
        Assert.Equal("Listening.", view.Composer.LastAnnouncement);

        recognizer.Emit("hello wor", isFinal: false);
        Assert.Equal("hello wor", view.Text);

        recognizer.Emit("hello world", isFinal: true);
        await UIThread.Until(() => client.Requests.Count == 1 && !view.IsBusy);

        Assert.Equal("hello world", client.LastUserText);
        Assert.Equal(string.Empty, view.Text);
        Assert.True(view.IsListening);
        await UIThread.Until(() => recognizer.StartCount == 2);

        await view.Composer.ToggleLiveSpeechAsync();
        Assert.False(view.IsListening);
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.Equal("Live voice stopped.", view.Composer.LastAnnouncement);
    });

    [Fact]
    public void ResultsRaisedOnAnotherThread_AreHandledOnTheUIThread() => UIThread.Run(async () =>
    {
        using var dispatcher = UIThreadDispatcher.Install();
        var recognizer = new FakeSpeechRecognizer();
        var client = new ScriptedChatClient().Reply("Hi!");
        var view = CreateView(recognizer, client);
        var threads = new HashSet<int>();
        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AIChatView.Text) or nameof(AIChatView.IsBusy))
                threads.Add(Environment.CurrentManagedThreadId);
        };
        ((INotifyCollectionChanged)view.MessageList.ItemsSource).CollectionChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);

        // Platform recognizers (Windows, cloud SDKs) raise results on background threads.
        await view.Composer.StartLiveSpeechAsync();
        await Task.Run(() => recognizer.Emit("hello", isFinal: false));
        await UIThread.Until(() => view.Text == "hello");
        await Task.Run(() => recognizer.Emit("hello world", isFinal: true));
        await UIThread.Until(() => client.Requests.Count == 1 && !view.IsBusy && recognizer.StartCount == 2);

        Assert.Equal("hello world", client.LastUserText);
        Assert.Equal([dispatcher.ThreadId], threads);
        Assert.All(recognizer.StartThreadIds, id => Assert.Equal(dispatcher.ThreadId, id));
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void BuiltInRecognizer_SendsTheWholeUtteranceAfterAPause() => UIThread.Run(async () =>
    {
        // iOS and Mac Catalyst report one new word per update and never end a pass themselves.
        var toolkit = new FakeSpeechToText();
        var time = new ManualTimeProvider();
        var client = new ScriptedChatClient().Reply("Sunny.");
        var view = CreateView(BuiltInRecognizer(toolkit, time), client);

        await view.Composer.StartLiveSpeechAsync();
        toolkit.SayOnApple("what's the weather in Paris");
        Assert.Equal("what's the weather in Paris", view.Text);

        time.Advance(MauiChatSpeechRecognizer.DefaultEndOfSpeechTimeout);
        await UIThread.Until(() => client.Requests.Count == 1 && !view.IsBusy && toolkit.StartCount == 2);

        Assert.Equal("what's the weather in Paris", client.LastUserText);
        Assert.Equal(string.Empty, view.Text);
        Assert.True(view.IsListening);
        await view.Composer.StopLiveSpeechAsync();
        Assert.False(toolkit.IsListening);
    });

    [Fact]
    public void BuiltInRecognizer_StoppedMidUtterance_KeepsEveryWord() => UIThread.Run(async () =>
    {
        var toolkit = new FakeSpeechToText();
        var client = new ScriptedChatClient();
        var view = CreateView(BuiltInRecognizer(toolkit, new ManualTimeProvider()), client, v => v.ShowInterimSpeechText = false);

        await view.Composer.StartLiveSpeechAsync();
        toolkit.SayOnApple("what's the weather in Paris");
        Assert.Equal(string.Empty, view.Text);

        await view.Composer.StopLiveSpeechAsync();

        Assert.Equal("what's the weather in Paris", view.Text);
        Assert.False(toolkit.IsListening);
        Assert.Empty(client.Requests);
    });

    [Fact]
    public void Stopping_KeepsWhatWasHeardEvenWhenInterimTextIsHidden() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var client = new ScriptedChatClient();
        var view = CreateView(recognizer, client, v => v.ShowInterimSpeechText = false);
        view.Text = "Note:";

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("buy milk and", isFinal: false);
        Assert.Equal("Note:", view.Text);

        await view.Composer.StopLiveSpeechAsync();

        Assert.Equal("Note: buy milk and", view.Text);
        Assert.Empty(client.Requests);
    });

    [Fact]
    public void FailedPass_KeepsWhatWasHeardEvenWhenInterimTextIsHidden() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.ShowInterimSpeechText = false);

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("buy milk", isFinal: false);
        recognizer.Emit(string.Empty, isFinal: true, ChatSpeechRecognitionErrorKind.Transient);

        Assert.Equal("buy milk", view.Text);
        await view.Composer.StopLiveSpeechAsync();
        Assert.Equal("buy milk", view.Text);
    });

    [Fact]
    public void AutoSubmitOff_KeepsTheTranscriptInTheBox() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var client = new ScriptedChatClient();
        var view = CreateView(recognizer, client, v => v.LiveSpeechAutoSubmit = false);
        view.Text = "Note:";

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("buy milk", isFinal: true);
        await UIThread.Until(() => recognizer.StartCount == 2);
        recognizer.Emit("and eggs", isFinal: true);

        Assert.Equal("Note: buy milk and eggs", view.Text);
        Assert.Empty(client.Requests);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void InterimText_CanBeHidden() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v =>
        {
            v.ShowInterimSpeechText = false;
            v.LiveSpeechAutoSubmit = false;
        });

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("partial", isFinal: false);
        Assert.Equal(string.Empty, view.Text);

        recognizer.Emit("partial words", isFinal: true);
        Assert.Equal("partial words", view.Text);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void Recognition_UsesTheConfiguredCulture() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var view = CreateView(recognizer, configure: v => v.SpeechRecognitionCulture = culture);

        await view.Composer.StartLiveSpeechAsync();

        Assert.Same(culture, recognizer.LastCulture);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void LiveSpeechButton_ShowsWhatItWillDo() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer);
        var button = view.ByAutomationId<Button>("AIChatLiveSpeechButton");
        var editor = view.ByAutomationId<Editor>("AIChatInput");

        Assert.True(button.IsVisible);
        Assert.Equal(IconTests.Glyph(AIChatThemeKeys.LiveVoiceIcon), button.Text);
        Assert.Equal("Start live voice", SemanticProperties.GetDescription(button));

        button.Command.Execute(null);
        await UIThread.Until(() => view.IsListening && recognizer.IsListening);
        Assert.Equal(IconTests.Glyph(AIChatThemeKeys.StopIcon), button.Text);
        Assert.Equal("Stop live voice", SemanticProperties.GetDescription(button));
        Assert.True(editor.IsReadOnly);

        button.Command.Execute(null);
        await UIThread.Until(() => !view.IsListening);
        Assert.Equal(IconTests.Glyph(AIChatThemeKeys.LiveVoiceIcon), button.Text);
        Assert.Equal("Start live voice", SemanticProperties.GetDescription(button));
        Assert.False(editor.IsReadOnly);
    });

    [Fact]
    public void LiveSpeech_RequiresASupportedRecognizer()
    {
        var view = CreateView(new FakeSpeechRecognizer { IsSupported = false });

        Assert.False(view.CanUseLiveSpeech);
        Assert.False(view.ByAutomationId<Button>("AIChatLiveSpeechButton").IsVisible);
        Assert.False(view.ToggleLiveSpeechCommand.CanExecute(null));
    }

    [Fact]
    public void LiveSpeech_AndAudioCapture_NeverShareTheMicrophone() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recognizer, configure: v =>
        {
            v.AllowAudioCapture = true;
            v.AudioRecorder = recorder;
        });

        await view.Composer.StartLiveSpeechAsync();
        Assert.False(view.ToggleAudioCaptureCommand.CanExecute(null));

        await view.Composer.StartAudioCaptureAsync();

        Assert.False(view.IsListening);
        Assert.Equal(1, recognizer.StopCount);
        Assert.True(view.IsRecordingAudio);
        Assert.False(view.ToggleLiveSpeechCommand.CanExecute(null));
    });

    [Fact]
    public void PermissionDenied_ReportsASpecificError() => UIThread.Run(async () =>
    {
        var view = CreateView(new FakeSpeechRecognizer { PermissionGranted = false });

        await view.Composer.StartLiveSpeechAsync();

        Assert.False(view.IsListening);
        Assert.Equal(ChatComposer.MicrophoneDeniedMessage, view.ErrorMessage);
    });

    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.PermissionDenied, ChatComposer.MicrophoneDeniedMessage)]
    [InlineData(ChatSpeechRecognitionErrorKind.LanguageNotSupported, ChatComposer.LanguageNotSupportedMessage)]
    public void UnrecoverableRecognitionErrors_StopWithASpecificMessage(ChatSpeechRecognitionErrorKind kind, string message) => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer);

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit(string.Empty, isFinal: true, kind);
        await UIThread.Drain();

        Assert.False(view.IsListening);
        Assert.Equal(message, view.ErrorMessage);
        Assert.Equal(1, recognizer.StartCount);
        Assert.Equal(0, recognizer.HandlerCount);
    });

    [Fact]
    public void StopDuringPermission_PreventsALateStart() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { PermissionGate = Waiters.Signal<bool>() };
        var view = CreateView(recognizer);

        var start = view.Composer.StartLiveSpeechAsync();
        await recognizer.PermissionRequested.Task;
        await view.Composer.StopLiveSpeechAsync();
        recognizer.PermissionGate.SetResult(true);
        await start;

        Assert.Equal(0, recognizer.StartCount);
        Assert.False(view.IsListening);
    });

    [Fact]
    public void StopDuringStart_DetachesTheHandlerAndReleasesTheRecognizer() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { StartGate = Waiters.Signal(), ObserveStartCancellation = false };
        var view = CreateView(recognizer);

        var start = view.Composer.StartLiveSpeechAsync();
        await UIThread.Until(() => recognizer.HandlerCount == 1);
        await view.Composer.StopLiveSpeechAsync();
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.IsListening);

        recognizer.StartGate.SetResult();
        await start;

        Assert.False(recognizer.IsListening);
        Assert.False(view.IsListening);
        Assert.Equal(0, recognizer.HandlerCount);
    });

    [Fact]
    public void PartialDuringASlowStop_CannotChangeTheDraft() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { StopGate = Waiters.Signal() };
        var view = CreateView(recognizer);
        view.Text = "original";

        await view.Composer.StartLiveSpeechAsync();
        var stale = recognizer.CaptureEmitter();
        var stop = view.Composer.StopLiveSpeechAsync();
        await recognizer.StopStarted.Task;
        stale(new ChatSpeechRecognitionEventArgs("stale partial", isFinal: false));
        recognizer.Emit("stale partial", isFinal: false);
        recognizer.StopGate.SetResult();
        await stop;

        Assert.Equal("original", view.Text);
        Assert.False(view.IsListening);
    });

    [Fact]
    public void ReplacingTheRecognizerDuringPermission_InvalidatesTheOldStart() => UIThread.Run(async () =>
    {
        var original = new FakeSpeechRecognizer { PermissionGate = Waiters.Signal<bool>() };
        var view = CreateView(original);

        var start = view.Composer.StartLiveSpeechAsync();
        await original.PermissionRequested.Task;
        view.SpeechRecognizer = new FakeSpeechRecognizer();
        original.PermissionGate.SetResult(true);
        await start;
        await UIThread.Drain();

        Assert.Equal(0, original.StartCount);
        Assert.False(view.IsListening);
    });

    [Fact]
    public void EmptyFinal_DoesNotSendTypedText() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var client = new ScriptedChatClient();
        var view = CreateView(recognizer, client);
        view.Text = "typed text";

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit(string.Empty, isFinal: true);
        await UIThread.Drain();

        Assert.Empty(client.Requests);
        Assert.Equal("typed text", view.Text);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void EmptyFinal_RemovesUnconfirmedInterimText() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.LiveSpeechAutoSubmit = false);
        view.Text = "typed";

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("provisional", isFinal: false);
        Assert.Equal("typed provisional", view.Text);

        recognizer.Emit(string.Empty, isFinal: true);

        Assert.Equal("typed", view.Text);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void StalePass_CannotOverwriteANewerDraft() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.LiveSpeechAutoSubmit = false);

        await view.Composer.StartLiveSpeechAsync();
        var stale = recognizer.CaptureEmitter();
        await view.Composer.StopLiveSpeechAsync();
        view.Text = "newer";
        await view.Composer.StartLiveSpeechAsync();

        stale(new ChatSpeechRecognitionEventArgs("stale", isFinal: true));
        await UIThread.Drain();

        Assert.Equal("newer", view.Text);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void TransientFailure_RestartsAfterADelay() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.LiveSpeechAutoSubmit = false);

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit(string.Empty, isFinal: true, ChatSpeechRecognitionErrorKind.Transient);
        Assert.Equal("Live voice was interrupted. Reconnecting.", view.Composer.LastAnnouncement);
        Assert.True(view.IsListening);

        await UIThread.Until(() => recognizer.StartCount == 2);
        Assert.True(view.IsListening);
        Assert.False(view.HasError);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void PersistentTransientFailures_StopAfterTheRetryBudget() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.LiveSpeechAutoSubmit = false);

        await view.Composer.StartLiveSpeechAsync();
        for (var expectedStarts = 2; expectedStarts <= ChatComposer.MaximumSpeechRestarts + 1; expectedStarts++)
        {
            recognizer.Emit(string.Empty, isFinal: true, ChatSpeechRecognitionErrorKind.Transient);
            await UIThread.Until(() => recognizer.StartCount == expectedStarts);
        }

        recognizer.Emit(string.Empty, isFinal: true, ChatSpeechRecognitionErrorKind.Transient);
        await UIThread.Drain();

        Assert.Equal(ChatComposer.MaximumSpeechRestarts + 1, recognizer.StartCount);
        Assert.False(view.IsListening);
        Assert.Equal(ChatComposer.SpeechErrorMessage, view.ErrorMessage);
    });

    [Fact]
    public void SpeechAfterAFailure_ResetsTheRetryBudget() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer, configure: v => v.LiveSpeechAutoSubmit = false);

        await view.Composer.StartLiveSpeechAsync();
        for (var round = 0; round < 3; round++)
        {
            for (var failure = 0; failure < ChatComposer.MaximumSpeechRestarts; failure++)
            {
                var starts = recognizer.StartCount;
                recognizer.Emit(string.Empty, isFinal: true, ChatSpeechRecognitionErrorKind.Transient);
                await UIThread.Until(() => recognizer.StartCount == starts + 1);
            }

            var before = recognizer.StartCount;
            recognizer.Emit("still here", isFinal: true);
            await UIThread.Until(() => recognizer.StartCount == before + 1);
        }

        Assert.True(view.IsListening);
        Assert.False(view.HasError);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void NoSpeech_KeepsListeningWithoutSpendingTheRetryBudget() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer);

        await view.Composer.StartLiveSpeechAsync();
        for (var expectedStarts = 2; expectedStarts <= 7; expectedStarts++)
        {
            recognizer.Emit(string.Empty, isFinal: true, expectedStarts % 2 == 0 ? ChatSpeechRecognitionErrorKind.NoSpeech : ChatSpeechRecognitionErrorKind.Aborted);
            await UIThread.Until(() => recognizer.StartCount == expectedStarts);
        }

        Assert.True(view.IsListening);
        Assert.False(view.HasError);
        await view.Composer.StopLiveSpeechAsync();
    });

    [Fact]
    public void PersistentStartFailures_StopAfterTheRetryBudget() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { StartError = new InvalidOperationException("persistent configuration failure") };
        var view = CreateView(recognizer);

        await view.Composer.StartLiveSpeechAsync();
        Assert.Equal(0, recognizer.HandlerCount);
        await UIThread.Until(() => !view.IsListening);

        Assert.Equal(ChatComposer.MaximumSpeechRestarts + 1, recognizer.StartCount);
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.Equal(ChatComposer.SpeechErrorMessage, view.ErrorMessage);
    });

    [Fact]
    public void Unloading_StopsListening() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer);

        await view.Composer.StartLiveSpeechAsync();
        ChatHost.Unload(view);
        await UIThread.Until(() => recognizer.StopCount == 1);

        Assert.False(view.IsListening);
        Assert.Equal(0, recognizer.HandlerCount);
    });

    [Fact]
    public void DisallowingLiveSpeech_StopsListening() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(recognizer);

        await view.Composer.StartLiveSpeechAsync();
        view.AllowLiveSpeech = false;
        await UIThread.Until(() => recognizer.StopCount == 1);

        Assert.False(view.IsListening);
        Assert.False(view.CanUseLiveSpeech);
    });

    private static PermittedSpeechRecognizer BuiltInRecognizer(FakeSpeechToText toolkit, ManualTimeProvider time) =>
        new(new MauiChatSpeechRecognizer(toolkit, SpeechUpdateKind.Words, time));

    private static AIChatView CreateView(
        IChatSpeechRecognizer recognizer,
        ScriptedChatClient? client = null,
        Action<AIChatView>? configure = null)
    {
        var view = ChatHost.Create(ChatHost.Session(client ?? new ScriptedChatClient()), view =>
        {
            view.AllowLiveSpeech = true;
            view.SpeechRecognizer = recognizer;
            configure?.Invoke(view);
        });
        view.Composer.RestartDelay = TimeSpan.FromMilliseconds(1);
        return view;
    }
}
