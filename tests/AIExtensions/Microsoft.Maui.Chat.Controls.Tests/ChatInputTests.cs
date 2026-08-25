using System.Globalization;
using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

public class ChatInputTests
{
    [Fact]
    public void InputContext_ReflectsComposerState()
    {
        var view = new ChatView
        {
            Conversation = ChatFactory.Conversation(),
            Text = "hello",
        };

        Assert.Same(view.InputContext, view.InputContext);
        Assert.Equal("hello", view.InputContext.Text);
        Assert.True(view.InputContext.CanSubmit);
        Assert.False(view.InputContext.CanCancel);
        Assert.False(view.InputContext.IsComposing);

        view.InputContext.SetComposing(true);

        Assert.True(view.IsComposing);
        Assert.False(view.CanSend);
        Assert.False(view.InputContext.CanSubmit);
    }

    [Fact]
    public void InputContext_CallbackCanDisposeDuringNotification()
    {
        var view = new ChatView();
        var firstCalls = 0;
        var secondCalls = 0;
        IDisposable? first = null;
        first = view.InputContext.RegisterOnChanged(() =>
        {
            firstCalls++;
            first!.Dispose();
        });
        view.InputContext.RegisterOnChanged(() => secondCalls++);

        view.Text = "one";
        view.Text = "two";

        Assert.Equal(1, firstCalls);
        Assert.Equal(2, secondCalls);
    }

    [Fact]
    public void DefaultSpeechRecognizer_OnlyRetriesKnownTransientFailures()
    {
        Assert.Equal(
            ChatSpeechRecognitionErrorKind.Transient,
            MauiChatSpeechRecognizer.Classify(new HttpRequestException()));
        Assert.Equal(
            ChatSpeechRecognitionErrorKind.Transient,
            MauiChatSpeechRecognizer.Classify(new TimeoutException()));
        Assert.Equal(
            ChatSpeechRecognitionErrorKind.Fatal,
            MauiChatSpeechRecognizer.Classify(new InvalidOperationException()));
        Assert.Equal(
            ChatSpeechRecognitionErrorKind.Fatal,
            MauiChatSpeechRecognizer.Classify(new ArgumentException()));
    }

    [Fact]
    public async Task StopAsync_CancelsActiveSendAndKeepsDraft()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = async (chat, draft, cancellationToken) =>
        {
            chat.SetStatus(ChatConversationStatus.Busy);
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                chat.SetStatus(ChatConversationStatus.Idle);
            }
        };
        var view = new ChatView
        {
            Conversation = conversation,
            Text = "keep me",
        };

        var send = view.SendAsync();
        await started.Task;

        Assert.True(view.CanStop);
        Assert.False(view.ShowSendButton);
        await view.StopAsync();
        await send;

        Assert.False(view.CanStop);
        Assert.True(view.ShowSendButton);
        Assert.Equal("keep me", view.Text);
        Assert.Equal("Response stopped.", view.InputStatusMessage);
        Assert.Null(view.SendError);
    }

    [Fact]
    public async Task StopAsync_CancelsExternallyStartedAwaitingInput()
    {
        var cancelCalls = 0;
        var conversation = ChatFactory.Conversation();
        conversation.CancelHandler = (chat, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancelCalls++;
            chat.SetStatus(ChatConversationStatus.Idle);
            return Task.CompletedTask;
        };
        conversation.SetStatus(ChatConversationStatus.AwaitingInput);
        var view = new ChatView { Conversation = conversation };

        Assert.True(view.CanStop);
        await view.StopAsync();

        Assert.Equal(1, cancelCalls);
        Assert.Equal(ChatConversationStatus.Idle, conversation.Status);
        Assert.False(view.CanStop);
    }

    [Fact]
    public async Task StopAsync_CanceledRequestDoesNotClaimTheResponseStopped()
    {
        var cancelCalls = 0;
        var conversation = ChatFactory.Conversation();
        conversation.CancelHandler = (chat, cancellationToken) =>
        {
            cancelCalls++;
            return Task.CompletedTask;
        };
        conversation.SetStatus(ChatConversationStatus.AwaitingInput);
        var view = new ChatView { Conversation = conversation };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => view.StopAsync(cancellation.Token));

        Assert.Equal(0, cancelCalls);
        Assert.Null(view.InputStatusMessage);
        Assert.True(view.CanStop);
    }

    [Fact]
    public async Task AudioCapture_StagesRecordingAndReplacesExistingAudio()
    {
        var oldRecording = ChatFactory.Attachment("old.wav", "audio/wav");
        var newRecording = ChatFactory.Attachment("new.wav", "audio/wav");
        var recorder = new FakeAudioRecorder(newRecording);
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = recorder,
        };
        view.AddAttachment(oldRecording);
        ChatAttachment? recorded = null;
        view.AudioRecorded += (_, args) => recorded = args.Recording;

        await view.StartAudioCaptureAsync();

        Assert.True(view.IsRecordingAudio);
        Assert.True(view.IsComposing);
        Assert.Equal("Recording audio.", view.InputStatusMessage);

        await view.StopAudioCaptureAsync();

        Assert.False(view.IsRecordingAudio);
        Assert.False(view.IsTranscribingAudio);
        Assert.False(view.IsComposing);
        Assert.Same(newRecording, Assert.Single(view.Attachments));
        Assert.Same(newRecording, recorded);
        Assert.Equal("Audio recording attached.", view.InputStatusMessage);
        Assert.Equal(1, recorder.StartCount);
        Assert.Equal(1, recorder.StopCount);
        Assert.Equal(view.MaximumAudioBytes, recorder.SeenMaximumBytes);
    }

    [Fact]
    public async Task AudioTranscription_CanceledOperationCannotOverwriteComposer()
    {
        var recorder = new FakeAudioRecorder(
            ChatFactory.Attachment("voice.wav", "audio/wav"));
        var transcriber = new DeferredTranscriber();
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = recorder,
            AudioTranscriber = transcriber,
            Text = "newer text",
        };

        await view.StartAudioCaptureAsync();
        var stop = view.StopAudioCaptureAsync();
        await transcriber.Started.Task;
        Assert.True(view.IsTranscribingAudio);

        await view.ToggleAudioCaptureAsync();
        view.Text = "newer text";
        transcriber.Complete("stale transcript");
        await stop;

        Assert.Equal("newer text", view.Text);
        Assert.Equal("Audio transcription canceled.", view.InputStatusMessage);
        Assert.Equal(1, recorder.StopCount);
    }

    [Fact]
    public async Task AudioTranscription_CancelingCooperativeTranscriberCompletesCleanly()
    {
        var transcriber = new CancelableTranscriber();
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = new FakeAudioRecorder(
                ChatFactory.Attachment("voice.wav", "audio/wav")),
            AudioTranscriber = transcriber,
        };

        await view.StartAudioCaptureAsync();
        var stop = view.StopAudioCaptureAsync();
        await transcriber.Started.Task;

        await view.ToggleAudioCaptureAsync();
        await stop;

        Assert.False(view.IsTranscribingAudio);
        Assert.Equal("Audio transcription canceled.", view.InputStatusMessage);
    }

    [Fact]
    public async Task AudioTranscription_AppendsTextAndRaisesEvent()
    {
        var recording = ChatFactory.Attachment("voice.wav", "audio/wav");
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = new FakeAudioRecorder(recording),
            AudioTranscriber = new ImmediateTranscriber("garden update"),
            Text = "Existing",
        };
        ChatAudioTranscribedEventArgs? completed = null;
        view.AudioTranscribed += (_, args) => completed = args;

        await view.StartAudioCaptureAsync();
        await view.StopAudioCaptureAsync();

        Assert.Equal("Existing garden update", view.Text);
        Assert.NotNull(completed);
        Assert.Same(recording, completed.Recording);
        Assert.Equal("garden update", completed.Text);
        Assert.Equal("Voice transcription ready.", view.InputStatusMessage);
    }

    [Fact]
    public async Task AudioCapture_RejectsRecordingOverLimit()
    {
        var recording = new ChatAttachment(
            "large.wav",
            "audio/wav",
            new byte[16]);
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = new FakeAudioRecorder(recording),
            MaximumAudioBytes = 8,
        };

        await view.StartAudioCaptureAsync();
        await view.StopAudioCaptureAsync();

        Assert.Empty(view.Attachments);
        Assert.Contains("smaller", view.InputErrorMessage);
    }

    [Fact]
    public async Task AudioCapture_ReplacingRecorderCancelsOriginalRecorder()
    {
        var original = new FakeAudioRecorder(
            ChatFactory.Attachment("old.wav", "audio/wav"));
        var replacement = new FakeAudioRecorder(
            ChatFactory.Attachment("new.wav", "audio/wav"));
        var view = new ChatView
        {
            AllowAudioCapture = true,
            AudioRecorder = original,
        };

        await view.StartAudioCaptureAsync();
        view.AudioRecorder = replacement;
        await original.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, original.CancelCount);
        Assert.False(original.IsRecording);
        Assert.False(view.IsRecordingAudio);
    }

    [Fact]
    public async Task LiveSpeech_FinalUtteranceAutoSubmitsAndKeepsListeningIntent()
    {
        var recognizer = new FakeSpeechRecognizer();
        var submitted = new TaskCompletionSource<ChatDraft>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = (chat, draft, cancellationToken) =>
        {
            submitted.TrySetResult(draft);
            return Task.FromResult(true);
        };
        var view = new ChatView
        {
            Conversation = conversation,
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();
        recognizer.Emit("hello wor", isFinal: false);
        Assert.Equal("hello wor", view.Text);

        recognizer.Emit("hello world", isFinal: true);
        var draft = await submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("hello world", draft.Text);
        Assert.True(view.IsLiveSpeechEnabled);
        Assert.Equal(string.Empty, view.Text);

        await view.StopLiveSpeechAsync();
        Assert.False(view.IsLiveSpeechEnabled);
    }

    [Fact]
    public async Task LiveSpeech_PermissionDeniedSurfacesSpecificError()
    {
        var recognizer = new FakeSpeechRecognizer
        {
            PermissionGranted = false,
        };
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();

        Assert.False(view.IsLiveSpeechEnabled);
        Assert.Contains("Microphone access was denied", view.InputErrorMessage);
    }

    [Fact]
    public async Task LiveSpeech_StopDuringPermissionPreventsLateStart()
    {
        var recognizer = new DelayedSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
        };

        var start = view.StartLiveSpeechAsync();
        await recognizer.PermissionRequested.Task;
        await view.StopLiveSpeechAsync();
        recognizer.PermissionResult.TrySetResult(true);
        await start;

        Assert.Equal(0, recognizer.StartCount);
        Assert.False(view.IsLiveSpeechEnabled);
        Assert.False(view.IsListening);
    }

    [Fact]
    public async Task LiveSpeech_PartialDuringSlowStopCannotMutateComposer()
    {
        var recognizer = new DelayedSpeechRecognizer
        {
            DelayStop = true,
        };
        recognizer.PermissionResult.TrySetResult(true);
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
            Text = "original",
        };

        await view.StartLiveSpeechAsync();
        var stop = view.StopLiveSpeechAsync();
        await recognizer.StopStarted.Task;
        recognizer.Emit("stale partial", isFinal: false);
        recognizer.StopCompletion.TrySetResult();
        await stop;

        Assert.Equal("original", view.Text);
        Assert.False(view.IsLiveSpeechEnabled);
    }

    [Fact]
    public async Task LiveSpeech_ReplacingRecognizerDuringPermissionInvalidatesOldStart()
    {
        var original = new DelayedSpeechRecognizer();
        var replacement = new FakeSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            SpeechRecognizer = original,
        };

        var start = view.StartLiveSpeechAsync();
        await original.PermissionRequested.Task;
        view.SpeechRecognizer = replacement;
        original.PermissionResult.TrySetResult(true);
        await start;
        await Task.Yield();

        Assert.Equal(0, original.StartCount);
        Assert.False(view.IsLiveSpeechEnabled);
    }

    [Fact]
    public async Task LiveSpeech_EmptyFinalDoesNotSubmitExistingText()
    {
        var recognizer = new FakeSpeechRecognizer();
        var sendCount = 0;
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = (chat, draft, cancellationToken) =>
        {
            sendCount++;
            return Task.FromResult(true);
        };
        var view = new ChatView
        {
            Conversation = conversation,
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
            Text = "typed text",
        };

        await view.StartLiveSpeechAsync();
        recognizer.Emit(string.Empty, isFinal: true);
        await Task.Yield();

        Assert.Equal(0, sendCount);
        Assert.Equal("typed text", view.Text);
        await view.StopLiveSpeechAsync();
    }

    [Fact]
    public async Task LiveSpeech_StalePassCannotOverwriteNewerComposer()
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            LiveSpeechAutoSubmit = false,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();
        var staleEmitter = recognizer.CaptureEmitter();
        await view.StopLiveSpeechAsync();
        view.Text = "newer";
        await view.StartLiveSpeechAsync();

        staleEmitter(new ChatSpeechRecognitionEventArgs("stale", isFinal: true));
        await Task.Yield();

        Assert.Equal("newer", view.Text);
        await view.StopLiveSpeechAsync();
    }

    [Fact]
    public async Task LiveSpeech_TransientFailureRestartsAfterDelay()
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            LiveSpeechAutoSubmit = false,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();
        recognizer.Emit(
            string.Empty,
            isFinal: true,
            ChatSpeechRecognitionErrorKind.Transient);

        await recognizer.SecondStart.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, recognizer.StartCount);
        Assert.Equal("Listening.", view.InputStatusMessage);
        Assert.True(view.IsLiveSpeechEnabled);
        await view.StopLiveSpeechAsync();
    }

    [Fact]
    public async Task LiveSpeech_PersistentTransientFailureHasBoundedRetries()
    {
        var recognizer = new FakeSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            LiveSpeechAutoSubmit = false,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();
        for (var expectedStarts = 2; expectedStarts <= 4; expectedStarts++)
        {
            recognizer.Emit(
                string.Empty,
                isFinal: true,
                ChatSpeechRecognitionErrorKind.Transient);
            await WaitForAsync(
                () => recognizer.StartCount == expectedStarts,
                TimeSpan.FromSeconds(3));
        }

        recognizer.Emit(
            string.Empty,
            isFinal: true,
            ChatSpeechRecognitionErrorKind.Transient);
        await Task.Yield();

        Assert.Equal(4, recognizer.StartCount);
        Assert.False(view.IsLiveSpeechEnabled);
        Assert.Equal(
            ChatView.DefaultSpeechRecognitionErrorMessage,
            view.InputErrorMessage);
    }

    [Fact]
    public async Task LiveSpeech_PersistentStartFailureHasBoundedRetries()
    {
        var recognizer = new ThrowingSpeechRecognizer();
        var view = new ChatView
        {
            AllowLiveSpeech = true,
            SpeechRecognizer = recognizer,
        };

        await view.StartLiveSpeechAsync();
        await WaitForAsync(
            () => !view.IsLiveSpeechEnabled,
            TimeSpan.FromSeconds(3));

        Assert.Equal(4, recognizer.StartCount);
        Assert.Equal(
            ChatView.DefaultSpeechRecognitionErrorMessage,
            view.InputErrorMessage);
    }

    private sealed class FakeAudioRecorder(ChatAttachment recording) : IChatAudioRecorder
    {
        public bool IsSupported { get; set; } = true;

        public bool IsRecording { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int CancelCount { get; private set; }

        public long SeenMaximumBytes { get; private set; }

        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task<ChatAttachment?> StopAsync(
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            SeenMaximumBytes = maximumBytes;
            StopCount++;
            IsRecording = false;
            return Task.FromResult<ChatAttachment?>(recording);
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCount++;
            IsRecording = false;
            Canceled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class DeferredTranscriber : IChatAudioTranscriber
    {
        private readonly TaskCompletionSource<string?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string?> TranscribeAsync(
            ChatAttachment recording,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new ValueTask<string?>(_completion.Task);
        }

        public void Complete(string text) => _completion.TrySetResult(text);
    }

    private sealed class ImmediateTranscriber(string text) : IChatAudioTranscriber
    {
        public ValueTask<string?> TranscribeAsync(
            ChatAttachment recording,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(text);
        }
    }

    private sealed class CancelableTranscriber : IChatAudioTranscriber
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<string?> TranscribeAsync(
            ChatAttachment recording,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private sealed class FakeSpeechRecognizer : IChatSpeechRecognizer
    {
        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

        public bool IsSupported { get; set; } = true;

        public bool IsListening { get; private set; }

        public bool PermissionGranted { get; set; } = true;

        public int StartCount { get; private set; }

        public TaskCompletionSource SecondStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> RequestPermissionsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PermissionGranted);
        }

        public Task StartAsync(
            CultureInfo culture,
            bool reportPartialResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartCount == 2)
                SecondStart.TrySetResult();
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsListening = false;
            return Task.CompletedTask;
        }

        public void Emit(
            string text,
            bool isFinal,
            ChatSpeechRecognitionErrorKind errorKind =
                ChatSpeechRecognitionErrorKind.None)
        {
            if (isFinal)
                IsListening = false;
            RecognitionChanged?.Invoke(
                this,
                new ChatSpeechRecognitionEventArgs(
                    text,
                    isFinal,
                    errorKind,
                    exception: null));
        }

        public Action<ChatSpeechRecognitionEventArgs> CaptureEmitter()
        {
            var handlers = RecognitionChanged;
            return args => handlers?.Invoke(this, args);
        }
    }

    private sealed class ThrowingSpeechRecognizer : IChatSpeechRecognizer
    {
        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged
        {
            add { }
            remove { }
        }

        public bool IsSupported => true;

        public bool IsListening => false;

        public int StartCount { get; private set; }

        public Task<bool> RequestPermissionsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task StartAsync(
            CultureInfo culture,
            bool reportPartialResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            throw new InvalidOperationException("persistent configuration failure");
        }

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class DelayedSpeechRecognizer : IChatSpeechRecognizer
    {
        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

        public bool IsSupported => true;

        public bool IsListening { get; private set; }

        public bool DelayStop { get; set; }

        public int StartCount { get; private set; }

        public TaskCompletionSource PermissionRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> PermissionResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> RequestPermissionsAsync(
            CancellationToken cancellationToken = default)
        {
            PermissionRequested.TrySetResult();
            return await PermissionResult.Task;
        }

        public Task StartAsync(
            CultureInfo culture,
            bool reportPartialResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            IsListening = true;
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopStarted.TrySetResult();
            if (DelayStop)
                await StopCompletion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            IsListening = false;
        }

        public void Emit(string text, bool isFinal)
        {
            RecognitionChanged?.Invoke(
                this,
                new ChatSpeechRecognitionEventArgs(text, isFinal));
        }
    }

    private static async Task WaitForAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }
}
