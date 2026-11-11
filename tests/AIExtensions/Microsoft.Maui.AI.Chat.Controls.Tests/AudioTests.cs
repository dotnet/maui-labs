namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class AudioTests
{
    [Fact]
    public void AudioNote_IsStagedAndReplacesAnEarlierRecording() => UIThread.Run(async () =>
    {
        var recording = FakeAudioRecorder.Wav(name: "new.wav");
        var recorder = new FakeAudioRecorder(recording);
        var view = CreateView(recorder);
        var document = Blocks.Pdf();
        view.AddAttachment(FakeAudioRecorder.Wav(name: "old.wav"));
        view.AddAttachment(document);

        await view.Composer.ToggleAudioCaptureAsync();

        Assert.True(view.IsRecordingAudio);
        Assert.Equal("Recording audio.", view.Composer.LastAnnouncement);
        Assert.False(view.CanSend);

        await view.Composer.ToggleAudioCaptureAsync();

        Assert.False(view.IsRecordingAudio);
        Assert.False(view.IsTranscribingAudio);
        Assert.Equal([document, recording], view.Attachments);
        Assert.Equal("Audio recording attached.", view.Composer.LastAnnouncement);
        Assert.Equal(1, recorder.StartCount);
        Assert.Equal(1, recorder.StopCount);
        Assert.Equal(view.MaximumAttachmentBytes, recorder.SeenMaximumBytes);
        Assert.False(view.HasError);
    });

    [Fact]
    public void AudioButton_ShowsWhatItWillDo() => UIThread.Run(async () =>
    {
        var transcriber = new FakeTranscriber();
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v => v.AudioTranscriber = transcriber);
        var button = view.ByAutomationId<Button>("AIChatAudioButton");

        Assert.True(button.IsVisible);
        Assert.Equal("\U0001F3A4", button.Text);
        Assert.Equal("Record audio", SemanticProperties.GetDescription(button));

        button.Command.Execute(null);
        await UIThread.Until(() => view.IsRecordingAudio);
        Assert.Equal("\u25A0", button.Text);
        Assert.Equal("Stop recording", SemanticProperties.GetDescription(button));

        button.Command.Execute(null);
        await transcriber.Started.Task;
        Assert.True(view.IsTranscribingAudio);
        Assert.Equal("\u2026", button.Text);
        Assert.Equal("Cancel transcription", SemanticProperties.GetDescription(button));
        Assert.True(view.ByAutomationId<Editor>("AIChatInput").IsReadOnly);

        transcriber.Deferred!.SetResult("hello");
        await UIThread.Until(() => !view.IsTranscribingAudio);
        Assert.Equal("\U0001F3A4", button.Text);
        Assert.Equal("Record audio", SemanticProperties.GetDescription(button));
        Assert.False(view.ByAutomationId<Editor>("AIChatInput").IsReadOnly);
    });

    [Fact]
    public void AudioButton_IsHiddenWithoutSupport()
    {
        var view = CreateView(new FakeAudioRecorder { IsSupported = false });

        Assert.False(view.CanCaptureAudio);
        Assert.False(view.ByAutomationId<Button>("AIChatAudioButton").IsVisible);
        Assert.False(view.ToggleAudioCaptureCommand.CanExecute(null));

        var disallowed = CreateView(new FakeAudioRecorder(), v => v.AllowAudioCapture = false);
        Assert.False(disallowed.CanCaptureAudio);
        Assert.False(disallowed.ToggleAudioCaptureCommand.CanExecute(null));
    }

    [Fact]
    public void AudioNote_WithoutAudio_ExplainsWhatHappened() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder(recording: null);
        var view = CreateView(recorder);

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal(ChatComposer.NoAudioMessage, view.ErrorMessage);
        Assert.Empty(view.Attachments);

        recorder.Recording = new DataContent(Array.Empty<byte>(), "audio/wav");
        await view.Composer.StartAudioCaptureAsync();
        Assert.False(view.HasError);
        await view.Composer.StopAudioCaptureAsync();
        Assert.Equal(ChatComposer.NoAudioMessage, view.ErrorMessage);
    });

    [Fact]
    public void AudioNote_OverTheLimit_IsRejected() => UIThread.Run(async () =>
    {
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav(bytes: 2048)), v => v.MaximumAttachmentBytes = 1024);

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Empty(view.Attachments);
        Assert.Contains("smaller", view.ErrorMessage);
    });

    [Fact]
    public void AudioNote_RecorderReportsTooLarge_NamesTheLimit() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder { StopError = new AudioRecordingTooLargeException() };
        var view = CreateView(recorder, v => v.MaximumAttachmentBytes = 3 * 1024 * 1024);

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal("Attachments must be 3 MB or smaller.", view.ErrorMessage);
    });

    [Fact]
    public void Microphone_Denied_ReportsASpecificError() => UIThread.Run(async () =>
    {
        var view = CreateView(new FakeAudioRecorder { StartError = new PermissionException("denied") });

        await view.Composer.StartAudioCaptureAsync();

        Assert.Equal(ChatComposer.MicrophoneDeniedMessage, view.ErrorMessage);
        Assert.False(view.IsRecordingAudio);
    });

    [Fact]
    public void Recorder_Failure_ReportsAnAudioError() => UIThread.Run(async () =>
    {
        var view = CreateView(new FakeAudioRecorder { StartError = new InvalidOperationException("no device") });

        await view.Composer.StartAudioCaptureAsync();

        Assert.Equal(ChatComposer.AudioErrorMessage, view.ErrorMessage);
        Assert.False(view.IsRecordingAudio);
        Assert.True(view.ToggleAudioCaptureCommand.CanExecute(null));
    });

    [Fact]
    public void Dictation_AppendsTheTranscriptToTheDraft() => UIThread.Run(async () =>
    {
        var recording = FakeAudioRecorder.Wav();
        var transcriber = new FakeTranscriber("garden update");
        var view = CreateView(new FakeAudioRecorder(recording), v => v.AudioTranscriber = transcriber);
        view.Text = "Existing";

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal("Existing garden update", view.Text);
        Assert.Same(recording, transcriber.SeenRecording);
        Assert.Empty(view.Attachments);
        Assert.Equal("Voice transcription ready.", view.Composer.LastAnnouncement);
    });

    [Fact]
    public void Dictation_WithoutSpeech_ExplainsWhatHappened() => UIThread.Run(async () =>
    {
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v => v.AudioTranscriber = new FakeTranscriber("  "));

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal(ChatComposer.NoSpeechMessage, view.ErrorMessage);
        Assert.Equal(string.Empty, view.Text);
    });

    [Fact]
    public void Dictation_CanceledTranscriptionCannotOverwriteTheDraft() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var transcriber = new FakeTranscriber { IgnoreCancellation = true };
        var view = CreateView(recorder, v => v.AudioTranscriber = transcriber);

        await view.Composer.StartAudioCaptureAsync();
        var stop = view.Composer.StopAudioCaptureAsync();
        await transcriber.Started.Task;
        Assert.True(view.IsTranscribingAudio);

        await view.Composer.ToggleAudioCaptureAsync();
        Assert.False(view.IsTranscribingAudio);
        view.Text = "newer text";
        transcriber.Deferred!.SetResult("stale transcript");
        await stop;

        Assert.Equal("newer text", view.Text);
        Assert.Equal("Audio canceled.", view.Composer.LastAnnouncement);
        Assert.Equal(1, recorder.StopCount);
        Assert.False(view.HasError);
    });

    [Fact]
    public void Dictation_CancelingACooperativeTranscriberCompletesCleanly() => UIThread.Run(async () =>
    {
        var transcriber = new FakeTranscriber();
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v => v.AudioTranscriber = transcriber);

        await view.Composer.StartAudioCaptureAsync();
        var stop = view.Composer.StopAudioCaptureAsync();
        await transcriber.Started.Task;

        await view.Composer.ToggleAudioCaptureAsync();
        await stop;

        Assert.False(view.IsTranscribingAudio);
        Assert.False(view.HasError);
        Assert.True(view.ToggleAudioCaptureCommand.CanExecute(null));
    });

    [Fact]
    public void ReplacingTheRecorder_CancelsTheRecordingInProgress() => UIThread.Run(async () =>
    {
        var original = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(original);

        await view.Composer.StartAudioCaptureAsync();
        view.AudioRecorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        await UIThread.Until(() => original.CancelCount == 1);

        Assert.False(original.IsRecording);
        Assert.False(view.IsRecordingAudio);
        Assert.Empty(view.Attachments);
    });

    [Fact]
    public void DisallowingAudio_CancelsTheRecordingInProgress() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recorder);

        await view.Composer.StartAudioCaptureAsync();
        view.AllowAudioCapture = false;
        await UIThread.Until(() => recorder.CancelCount == 1);

        Assert.False(view.IsRecordingAudio);
        Assert.False(view.CanCaptureAudio);
    });

    [Fact]
    public void Unloading_CancelsTheRecordingInProgress() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recorder);

        await view.Composer.StartAudioCaptureAsync();
        ChatHost.Unload(view);
        await UIThread.Until(() => recorder.CancelCount == 1);

        Assert.False(view.IsRecordingAudio);
    });

    [Fact]
    public void SwappingSessionsDuringStop_DoesNotStageAStaleRecording() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder { StopGate = Waiters.Signal<DataContent?>() };
        var view = CreateView(recorder);

        await view.Composer.StartAudioCaptureAsync();
        var stop = view.Composer.StopAudioCaptureAsync();
        view.Session = ChatHost.Session(new ScriptedChatClient());
        recorder.StopGate.SetResult(FakeAudioRecorder.Wav());
        await stop;

        Assert.Empty(view.Attachments);
        Assert.False(view.IsRecordingAudio);
        Assert.False(view.IsTranscribingAudio);
    });

    [Fact]
    public void InterimTranscript_ReplacesInterimTextAndRestartsUntilStopped() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v =>
        {
            v.SpeechRecognizer = recognizer;
            v.ShowInterimAudioTranscript = true;
            v.AudioTranscriber = new FakeTranscriber("hello there");
        });
        view.Composer.RestartDelay = TimeSpan.FromMilliseconds(1);
        view.Text = "prefix";

        await view.Composer.StartAudioCaptureAsync();
        Assert.Equal(1, recognizer.StartCount);
        Assert.Equal(1, recognizer.HandlerCount);

        recognizer.Emit("hel", isFinal: false);
        Assert.Equal("prefix hel", view.Text);
        recognizer.Emit("hello", isFinal: true);
        Assert.Equal("prefix hello", view.Text);
        Assert.Equal(0, recognizer.HandlerCount);

        await UIThread.Until(() => recognizer.StartCount == 2);
        Assert.Equal(1, recognizer.HandlerCount);

        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.True(recognizer.StopCount > 0);
        Assert.Equal("prefix hello there", view.Text);
    });

    [Fact]
    public void InterimTranscript_RaisedOnAnotherThread_IsShownOnTheUIThread() => UIThread.Run(async () =>
    {
        using var dispatcher = UIThreadDispatcher.Install();
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v =>
        {
            v.SpeechRecognizer = recognizer;
            v.ShowInterimAudioTranscript = true;
        });
        var threads = new HashSet<int>();
        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AIChatView.Text))
                threads.Add(Environment.CurrentManagedThreadId);
        };

        await view.Composer.StartAudioCaptureAsync();
        await Task.Run(() => recognizer.Emit("hello", isFinal: false));
        await UIThread.Until(() => view.Text == "hello");

        Assert.Equal([dispatcher.ThreadId], threads);
        await view.Composer.StopAudioCaptureAsync();
    });

    [Fact]
    public void InterimTranscript_SessionSwapReleasesTheRecognizer() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v =>
        {
            v.SpeechRecognizer = recognizer;
            v.ShowInterimAudioTranscript = true;
        });

        await view.Composer.StartAudioCaptureAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        view.Session = ChatHost.Session(new ScriptedChatClient());
        await UIThread.Until(() => recognizer.StopCount > 0);

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.IsRecordingAudio);
    });

    [Fact]
    public void InterimTranscript_StopFailureStillReleasesTheRecorder() => UIThread.Run(async () =>
    {
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recorder, v =>
        {
            v.SpeechRecognizer = new FakeSpeechRecognizer { StopError = new InvalidOperationException("speech stop failed") };
            v.ShowInterimAudioTranscript = true;
        });

        await view.Composer.StartAudioCaptureAsync();
        await view.Composer.StopAudioCaptureAsync();

        Assert.Equal(1, recorder.StopCount);
        Assert.False(recorder.IsRecording);
        Assert.False(view.IsRecordingAudio);
    });

    [Fact]
    public void InterimTranscript_RecognizerReplacedDuringPermission_DoesNotStartTheOldOne() => UIThread.Run(async () =>
    {
        var original = new FakeSpeechRecognizer { PermissionGate = Waiters.Signal<bool>() };
        var view = CreateView(new FakeAudioRecorder(FakeAudioRecorder.Wav()), v =>
        {
            v.SpeechRecognizer = original;
            v.ShowInterimAudioTranscript = true;
        });

        var start = view.Composer.StartAudioCaptureAsync();
        await original.PermissionRequested.Task;
        view.SpeechRecognizer = new FakeSpeechRecognizer();
        original.PermissionGate.SetResult(true);
        await start;

        Assert.Equal(0, original.StartCount);
        Assert.Equal(0, original.HandlerCount);
    });

    [Fact]
    public void SessionSwap_WaitsForTheOldSpeechCleanupBeforeRecording() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { StopGate = Waiters.Signal() };
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recorder, v =>
        {
            v.SpeechRecognizer = recognizer;
            v.AllowLiveSpeech = true;
        });

        await view.Composer.StartLiveSpeechAsync();
        view.Session = ChatHost.Session(new ScriptedChatClient());

        var startAudio = view.Composer.StartAudioCaptureAsync();
        await UIThread.Drain();
        Assert.Equal(0, recorder.StartCount);
        Assert.False(view.ToggleAudioCaptureCommand.CanExecute(null));

        recognizer.StopGate.SetResult();
        await startAudio;
        Assert.Equal(1, recorder.StartCount);
        Assert.True(view.IsRecordingAudio);
    });

    [Fact]
    public void StartingARecording_KeepsWhatLiveVoiceHeard() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer();
        var recorder = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(recorder, v =>
        {
            v.SpeechRecognizer = recognizer;
            v.AllowLiveSpeech = true;
            v.ShowInterimSpeechText = false;
        });

        await view.Composer.StartLiveSpeechAsync();
        recognizer.Emit("buy milk", isFinal: false);
        Assert.Equal(string.Empty, view.Text);

        await view.Composer.StartAudioCaptureAsync();

        Assert.Equal("buy milk", view.Text);
        Assert.True(view.IsRecordingAudio);
    });

    [Fact]
    public void ReplacingTheRecorderDuringSpeechCleanup_StartsNeitherRecorder() => UIThread.Run(async () =>
    {
        var recognizer = new FakeSpeechRecognizer { StopGate = Waiters.Signal() };
        var original = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var replacement = new FakeAudioRecorder(FakeAudioRecorder.Wav());
        var view = CreateView(original, v =>
        {
            v.SpeechRecognizer = recognizer;
            v.AllowLiveSpeech = true;
        });

        await view.Composer.StartLiveSpeechAsync();
        var startAudio = view.Composer.StartAudioCaptureAsync();
        await UIThread.Until(() => recognizer.StopCount == 1);

        view.AudioRecorder = replacement;
        recognizer.StopGate.SetResult();
        await startAudio;

        Assert.Equal(0, original.StartCount);
        Assert.Equal(0, replacement.StartCount);
        Assert.False(view.IsRecordingAudio);
    });

    private static AIChatView CreateView(FakeAudioRecorder recorder, Action<AIChatView>? configure = null) =>
        ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), view =>
        {
            view.AllowAudioCapture = true;
            view.AudioRecorder = recorder;
            configure?.Invoke(view);
        });
}
