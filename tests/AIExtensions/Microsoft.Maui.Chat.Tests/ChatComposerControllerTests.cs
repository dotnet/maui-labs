namespace Microsoft.Maui.Chat.Tests;

public class ChatComposerControllerTests
{
    [Fact]
    public void TextContent_ChangesSynchronouslyNotifyItsConversation()
    {
        var participant = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(participant);
        var content = new TextMessageContent("one");
        var message = new ConversationMessage(participant);
        message.AddContent(content);
        conversation.AddMessage(message);
        ChatConversationChange? received = null;
        using var subscription = conversation.Subscribe(change => received = change);

        content.Append(" two");

        Assert.Equal("one two", content.Text);
        Assert.NotNull(received);
        Assert.Equal(ChatConversationChangeKind.ContentChanged, received.Value.Kind);
        Assert.Same(message, received.Value.Message);
        Assert.Same(content, received.Value.Content);
    }

    [Fact]
    public void ConversationSwap_RestoresEachConversationDraft()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var first = new ObservableChatConversation(local);
        var second = new ObservableChatConversation(local);
        using var controller = new ChatComposerController { Conversation = first };

        controller.Text = "first draft";
        controller.AddAttachment(new ChatAttachment("first.txt", "text/plain", new byte[] { 1 }));

        controller.Conversation = second;
        controller.Text = "second draft";

        controller.Conversation = first;

        Assert.Equal("first draft", controller.Text);
        Assert.Single(controller.Attachments);
    }

    [Fact]
    public async Task SubmitAsync_ConversationSwappedBeforeCompletion_DoesNotClearNewDraft()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ObservableChatConversation(local)
        {
            SendHandler = (_, _, _) => completion.Task,
        };
        var second = new ObservableChatConversation(local);
        using var controller = new ChatComposerController { Conversation = first, Text = "first draft" };

        var send = controller.SubmitAsync();
        controller.Conversation = second;
        controller.Text = "second draft";
        completion.SetResult(true);
        await send;

        Assert.Equal("second draft", controller.Text);
        Assert.Empty(controller.Attachments);
    }

    [Fact]
    public async Task StopAudioCaptureAsync_ConversationSwappedDuringStop_DoesNotStageStaleRecording()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var completion = new TaskCompletionSource<ChatAttachment?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new ControlledRecorder(completion);
        var first = new ObservableChatConversation(local);
        var second = new ObservableChatConversation(local);
        using var controller = new ChatComposerController
        {
            Conversation = first,
            AllowAudioCapture = true,
            AudioRecorder = recorder,
        };

        await controller.StartAudioCaptureAsync();
        var stop = controller.StopAudioCaptureAsync();
        controller.Conversation = second;
        completion.SetResult(new ChatAttachment("recording.wav", "audio/wav", new byte[] { 1, 2, 3 }));
        await stop;

        Assert.Empty(controller.Attachments);
        Assert.False(controller.IsRecordingAudio);
        Assert.False(controller.IsTranscribingAudio);
    }

    [Fact]
    public async Task StopAsync_CancelsControllerSendToken()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local)
        {
            SendHandler = async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            },
        };
        using var controller = new ChatComposerController
        {
            Conversation = conversation,
            Text = "cancel me",
        };

        var send = controller.SubmitAsync();
        await Task.Yield();
        await controller.StopAsync();
        await send;

        Assert.False(controller.IsSending);
    }

    [Fact]
    public void ComposerCapabilities_RespectAttachmentAndCustomCompositionGates()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            AttachmentPicker = new StubPicker(),
            AudioRecorder = new TrackingRecorder(),
            SpeechRecognizer = new TrackingRecognizer(),
            AllowAudioCapture = true,
            AllowLiveSpeech = true,
        };

        Assert.False(controller.CanPickAttachments);

        controller.AllowAttachments = true;
        Assert.True(controller.CanPickAttachments);

        controller.SetComposing(true);
        Assert.False(controller.CanPickAttachments);
        Assert.False(controller.CanToggleAudioCapture);
        Assert.False(controller.CanToggleLiveSpeech);
    }

    [Fact]
    public async Task StopAudioCapture_NullRecording_ReportsCaptureFailure()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var recorder = new TrackingRecorder { StopResult = null };
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            AudioRecorder = recorder,
            AllowAudioCapture = true,
        };

        await controller.StartAudioCaptureAsync();
        await controller.StopAudioCaptureAsync();

        Assert.False(controller.IsRecordingAudio);
        Assert.False(controller.IsTranscribingAudio);
        Assert.Null(controller.StatusMessage);
        Assert.Contains("did not capture audio", controller.ErrorMessage);
    }

    [Fact]
    public async Task AudioDictation_ReplacesInterimText_RestartsAndCleansUpOnConversationSwap()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var first = new ObservableChatConversation(local);
        var second = new ObservableChatConversation(local);
        var recorder = new TrackingRecorder();
        var recognizer = new TrackingRecognizer();
        using var controller = new ChatComposerController
        {
            Conversation = first,
            Text = "prefix",
            AudioRecorder = recorder,
            SpeechRecognizer = recognizer,
            AllowAudioCapture = true,
            ShowInterimAudioTranscript = true,
        };

        await controller.StartAudioCaptureAsync();
        Assert.Equal(1, recognizer.StartCount);
        Assert.Equal(1, recognizer.HandlerCount);

        recognizer.Raise("hel", isFinal: false);
        Assert.Equal("prefix hel", controller.Text);
        recognizer.Raise("hello", isFinal: true);
        Assert.Equal("prefix hello", controller.Text);
        Assert.Equal(0, recognizer.HandlerCount);

        await WaitUntilAsync(() => recognizer.StartCount == 2);
        Assert.Equal(1, recognizer.HandlerCount);

        controller.Conversation = second;
        await WaitUntilAsync(() => recognizer.StopCount > 0);
        Assert.Equal(0, recognizer.HandlerCount);
    }

    [Fact]
    public async Task LiveSpeech_FailedStart_DetachesHandlerBeforeRetry()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var recognizer = new TrackingRecognizer
        {
            StartException = new InvalidOperationException("start failed"),
        };
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            SpeechRecognizer = recognizer,
            AllowLiveSpeech = true,
            ContinuousLiveSpeech = true,
        };

        await controller.StartLiveSpeechAsync();

        Assert.Equal(0, recognizer.HandlerCount);
        await controller.StopLiveSpeechAsync();
    }

    [Fact]
    public async Task ConversationSwap_AwaitsOldSpeechCleanupBeforeStartingAudio()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var first = new ObservableChatConversation(local);
        var second = new ObservableChatConversation(local);
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new TrackingRecognizer { StopGate = stopGate.Task };
        var recorder = new TrackingRecorder();
        using var controller = new ChatComposerController
        {
            Conversation = first,
            SpeechRecognizer = recognizer,
            AudioRecorder = recorder,
            AllowLiveSpeech = true,
            AllowAudioCapture = true,
        };

        await controller.StartLiveSpeechAsync();
        controller.Conversation = second;

        var startAudio = controller.StartAudioCaptureAsync();
        await Task.Yield();
        Assert.Equal(0, recorder.StartCount);

        stopGate.SetResult();
        await startAudio;
        Assert.Equal(1, recorder.StartCount);
    }

    [Fact]
    public async Task AudioRecorderReplacement_DuringSpeechCleanup_DoesNotStartOldRecorder()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new TrackingRecognizer { StopGate = stopGate.Task };
        var original = new TrackingRecorder();
        var replacement = new TrackingRecorder();
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            SpeechRecognizer = recognizer,
            AudioRecorder = original,
            AllowLiveSpeech = true,
            AllowAudioCapture = true,
        };

        await controller.StartLiveSpeechAsync();
        var startAudio = controller.StartAudioCaptureAsync();
        await WaitUntilAsync(() => recognizer.StopCount == 1);

        controller.AudioRecorder = replacement;
        stopGate.SetResult();
        await startAudio;

        Assert.Equal(0, original.StartCount);
        Assert.Equal(0, replacement.StartCount);
    }

    [Fact]
    public async Task AudioDictation_RecognizerReplacedDuringPermission_DoesNotStartOldRecognizer()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var permission = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new TrackingRecognizer { PermissionGate = permission.Task };
        var replacement = new TrackingRecognizer();
        var recorder = new TrackingRecorder();
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            AudioRecorder = recorder,
            SpeechRecognizer = original,
            AllowAudioCapture = true,
            ShowInterimAudioTranscript = true,
        };

        var start = controller.StartAudioCaptureAsync();
        await original.PermissionRequested.Task;
        controller.SpeechRecognizer = replacement;
        permission.SetResult(true);
        await start;

        Assert.Equal(0, original.StartCount);
        Assert.Equal(0, original.HandlerCount);
        await controller.CancelAudioCaptureAsync();
    }

    [Fact]
    public async Task LiveSpeech_CallerCanceledStartup_DetachesHandlerAndClearsIntent()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new TrackingRecognizer { StartGate = startGate.Task };
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            SpeechRecognizer = recognizer,
            AllowLiveSpeech = true,
        };
        using var cancellation = new CancellationTokenSource();

        var start = controller.StartLiveSpeechAsync(cancellation.Token);
        await WaitUntilAsync(() => recognizer.HandlerCount == 1);
        cancellation.Cancel();
        await start;

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(controller.IsLiveSpeechEnabled);
        Assert.False(controller.IsSpeechStarting);
    }

    [Fact]
    public async Task LiveSpeech_CancellationAfterSuccessfulStart_StopsRecognizer()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new TrackingRecognizer
        {
            StartGate = startGate.Task,
            ObserveStartCancellation = false,
        };
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            SpeechRecognizer = recognizer,
            AllowLiveSpeech = true,
        };
        using var cancellation = new CancellationTokenSource();

        var start = controller.StartLiveSpeechAsync(cancellation.Token);
        await WaitUntilAsync(() => recognizer.HandlerCount == 1);
        startGate.SetResult();
        cancellation.Cancel();
        await start;

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(controller.IsLiveSpeechEnabled);
        Assert.False(controller.IsListening);
        Assert.Equal(1, recognizer.StopCount);
    }

    [Fact]
    public async Task AudioDictation_StopFailure_StillReleasesRecorder()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var recorder = new TrackingRecorder();
        var recognizer = new TrackingRecognizer
        {
            StopException = new InvalidOperationException("speech stop failed"),
        };
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            AudioRecorder = recorder,
            SpeechRecognizer = recognizer,
            AllowAudioCapture = true,
            ShowInterimAudioTranscript = true,
        };

        await controller.StartAudioCaptureAsync();
        await controller.StopAudioCaptureAsync();

        Assert.Equal(1, recorder.StopCount);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task SpeechRecognized_ReentrantConversationSwap_CannotWriteStaleTranscript()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var first = new ObservableChatConversation(local);
        var second = new ObservableChatConversation(local);
        var recognizer = new TrackingRecognizer();
        using var controller = new ChatComposerController
        {
            Conversation = first,
            SpeechRecognizer = recognizer,
            AllowLiveSpeech = true,
            LiveSpeechAutoSubmit = false,
        };
        controller.SpeechRecognized += (_, _) => controller.Conversation = second;

        await controller.StartLiveSpeechAsync();
        recognizer.Raise("stale", isFinal: false);

        Assert.Same(second, controller.Conversation);
        Assert.Equal(string.Empty, controller.Text);
    }

    [Fact]
    public async Task LiveSpeech_EmptyFinal_RemovesUnconfirmedInterimText()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var recognizer = new TrackingRecognizer();
        using var controller = new ChatComposerController
        {
            Conversation = new ObservableChatConversation(local),
            Text = "typed",
            SpeechRecognizer = recognizer,
            AllowLiveSpeech = true,
            LiveSpeechAutoSubmit = false,
            ContinuousLiveSpeech = false,
        };

        await controller.StartLiveSpeechAsync();
        recognizer.Raise("provisional", isFinal: false);
        Assert.Equal("typed provisional", controller.Text);

        recognizer.Raise(string.Empty, isFinal: true);

        Assert.Equal("typed", controller.Text);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledRecorder(TaskCompletionSource<ChatAttachment?> stopCompletion) : IChatAudioRecorder
    {
        public bool IsSupported => true;

        public bool IsRecording { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRecording = true;
            return Task.CompletedTask;
        }

        public async Task<ChatAttachment?> StopAsync(
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            var result = await stopCompletion.Task.WaitAsync(cancellationToken);
            IsRecording = false;
            return result;
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            IsRecording = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPicker : IChatAttachmentPicker
    {
        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            object? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatAttachment>>([]);
    }

    private sealed class TrackingRecorder : IChatAudioRecorder
    {
        public bool IsSupported => true;

        public bool IsRecording { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int CancelCount { get; private set; }

        public ChatAttachment? StopResult { get; set; } =
            new("recording.wav", "audio/wav", new byte[] { 1, 2, 3 });

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
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            IsRecording = false;
            return Task.FromResult(StopResult);
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCount++;
            IsRecording = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingRecognizer : IChatSpeechRecognizer
    {
        private EventHandler<ChatSpeechRecognitionEventArgs>? _recognitionChanged;

        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged
        {
            add
            {
                _recognitionChanged += value;
                HandlerCount++;
            }
            remove
            {
                _recognitionChanged -= value;
                HandlerCount--;
            }
        }

        public bool IsSupported => true;

        public bool IsListening { get; private set; }

        public int HandlerCount { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Exception? StartException { get; set; }

        public Exception? StopException { get; set; }

        public Task<bool>? PermissionGate { get; set; }

        public Task? StartGate { get; set; }

        public Task? StopGate { get; set; }

        public bool ObserveStartCancellation { get; set; } = true;

        public TaskCompletionSource PermissionRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PermissionRequested.TrySetResult();
            return PermissionGate ?? Task.FromResult(true);
        }

        public async Task StartAsync(
            System.Globalization.CultureInfo culture,
            bool reportPartialResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartException is not null)
                throw StartException;
            if (StartGate is not null)
            {
                if (ObserveStartCancellation)
                    await StartGate.WaitAsync(cancellationToken);
                else
                    await StartGate;
            }
            IsListening = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            if (StopException is not null)
                throw StopException;
            if (StopGate is not null)
                await StopGate.WaitAsync(cancellationToken);
            IsListening = false;
        }

        public void Raise(string text, bool isFinal)
        {
            if (isFinal)
                IsListening = false;
            _recognitionChanged?.Invoke(
                this,
                new ChatSpeechRecognitionEventArgs(text, isFinal));
        }
    }
}
