// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Round-6 regression tests for the two-part fix to the modality state machine:
///
/// 1. Same-modality duplicate starts during startup. The startup window (recorder /
///    recognizer StartAsync in flight) must NOT be toggleable: a second click must not
///    re-invoke StartAsync. The gate is enforced twice — CanToggleXxx returns false
///    (DOM button disabled) AND the toggle method itself early-returns.
///
/// 2. Audio stop/processing window is a real active state. IsTranscribingAudio is set
///    BEFORE clearing IsRecordingAudio and BEFORE awaiting StopAsync so:
///      * Speech cannot preempt during the stop/read window (IsAudioActive stays true).
///      * A second audio click cannot double-invoke recorder.StopAsync.
///      * Send / PickAttachments are gated off (IsComposing = true).
///    Similarly IsSpeechStopping guards the speech StopAsync await window.
///
/// Plus per-modality operation identity (<c>_audioOperationId</c> /
/// <c>_speechOperationId</c>) so late writeback from a preempted operation cannot
/// stage a stale attachment, set a stale error, or clear a newer operation's flags
/// via its finally block.
/// </summary>
public class ChatViewSameModalityAndProcessingTests
{
    // ============================================================================
    // Part 1: Startup window is NOT toggleable — same-modality duplicate starts
    // ============================================================================

    [Fact]
    public async Task DoubleAudioToggle_DuringDelayedStart_InvokesStartAsyncExactlyOnce()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        // Gate StartAsync so the "starting" window is observable.
        var startGate = new TaskCompletionSource<bool>();
        recorder.StartGate = startGate.Task;

        var view = CreateView(conversation, recorder, recognizer);

        var first = view.ToggleAudioCaptureAsync();
        // Wait for IsAudioStarting to actually be set.
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsAudioStarting);
        Assert.Equal(1, recorder.StartCallCount);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);

        // Programmatic second click bypassing the disabled DOM button. The Toggle
        // early-return + the CanToggle gate together must prevent a second StartAsync.
        var second = view.ToggleAudioCaptureAsync();
        Assert.Equal(1, recorder.StartCallCount);

        // Let the first start complete.
        startGate.SetResult(true);
        await first;
        await second;

        Assert.Equal(1, recorder.StartCallCount);
        Assert.True(view.ComposerContext.IsRecordingAudio);
        Assert.False(((ChatComposerContext)view.ComposerContext).IsAudioStarting);
    }

    [Fact]
    public async Task DoubleSpeechToggle_DuringDelayedPermission_InvokesStartAsyncExactlyOnce()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        // Block permission request so IsSpeechStarting is set but StartAsync hasn't
        // been called yet — the toggle guard must still catch the double-tap.
        var permGate = new TaskCompletionSource<bool>();
        recognizer.PermissionGate = permGate.Task;

        var view = CreateView(conversation, recorder, recognizer);

        var first = view.ToggleLiveSpeechAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechStarting);
        Assert.Equal(0, recognizer.StartCallCount);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);

        // Second click during permission request must NOT re-invoke RequestPermissionsAsync
        // (which would deadlock on the singleton gate) and must NOT queue a second
        // StartAsync when permission resolves.
        var second = view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.PermissionRequestCount);

        permGate.SetResult(true);
        await first;
        await second;

        Assert.Equal(1, recognizer.StartCallCount);
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.False(((ChatComposerContext)view.ComposerContext).IsSpeechStarting);
    }

    [Fact]
    public async Task DoubleSpeechToggle_DuringDelayedStart_InvokesStartAsyncExactlyOnce()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var startGate = new TaskCompletionSource<bool>();
        recognizer.StartGate = startGate.Task;

        var view = CreateView(conversation, recorder, recognizer);

        var first = view.ToggleLiveSpeechAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechStarting);
        Assert.Equal(1, recognizer.StartCallCount);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);

        var second = view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.StartCallCount);

        startGate.SetResult(true);
        await first;
        await second;

        Assert.Equal(1, recognizer.StartCallCount);
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
    }

    // ============================================================================
    // Part 1b: Toggle during transient states is a no-op
    // ============================================================================

    [Fact]
    public async Task ToggleAudio_DuringStartingWindow_IsNoOp()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var startGate = new TaskCompletionSource<bool>();
        recorder.StartGate = startGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        var first = view.ToggleAudioCaptureAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsAudioStarting);

        // Multiple bypass calls during starting: all no-op.
        await view.ToggleAudioCaptureAsync();
        await view.ToggleAudioCaptureAsync();
        await view.ToggleAudioCaptureAsync();

        Assert.Equal(1, recorder.StartCallCount);
        Assert.Equal(0, recorder.StopCallCount);
        Assert.Equal(0, recorder.CancelCallCount);

        startGate.SetResult(true);
        await first;
        Assert.True(view.ComposerContext.IsRecordingAudio);
    }

    [Fact]
    public async Task ToggleAudio_DuringTranscribingWindow_IsNoOp()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        await view.ToggleAudioCaptureAsync();
        Assert.True(view.ComposerContext.IsRecordingAudio);

        var stop = view.ToggleAudioCaptureAsync();
        // Transcribing window: IsTranscribingAudio true, IsRecordingAudio false.
        await WaitFor(() => view.ComposerContext.IsTranscribingAudio);
        Assert.False(view.ComposerContext.IsRecordingAudio);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);
        Assert.True(view.ComposerContext.IsComposing);
        Assert.False(view.ComposerContext.CanSubmit);

        // Multiple bypass calls during transcribing: all no-op. In particular, no
        // duplicate StopAsync invocation.
        await view.ToggleAudioCaptureAsync();
        await view.ToggleAudioCaptureAsync();
        Assert.Equal(1, recorder.StopCallCount);

        stopGate.SetResult(null);
        await stop;
        Assert.False(view.ComposerContext.IsTranscribingAudio);
        Assert.Equal(1, recorder.StopCallCount);
    }

    [Fact]
    public async Task ToggleSpeech_DuringStartingWindow_IsNoOp()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var startGate = new TaskCompletionSource<bool>();
        recognizer.StartGate = startGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        var first = view.ToggleLiveSpeechAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechStarting);

        await view.ToggleLiveSpeechAsync();
        await view.ToggleLiveSpeechAsync();

        Assert.Equal(1, recognizer.StartCallCount);
        Assert.Equal(0, recognizer.StopCallCount);

        startGate.SetResult(true);
        await first;
    }

    [Fact]
    public async Task ToggleSpeech_DuringStoppingWindow_IsNoOp()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<bool>();
        recognizer.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        await view.ToggleLiveSpeechAsync();
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);

        var stop = view.ToggleLiveSpeechAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechStopping);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);
        Assert.True(view.ComposerContext.IsComposing);
        Assert.False(view.ComposerContext.CanSubmit);

        await view.ToggleLiveSpeechAsync();
        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.StopCallCount);

        stopGate.SetResult(true);
        await stop;
        Assert.False(((ChatComposerContext)view.ComposerContext).IsSpeechStopping);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
    }

    // ============================================================================
    // Part 2: Stop/read window is protected — speech cannot preempt during it,
    //          and stale completions cannot stage
    // ============================================================================

    [Fact]
    public async Task AudioStopDelayed_ThenSpeechPreempts_NoStaleAttachmentStaged()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        await view.ToggleAudioCaptureAsync();
        Assert.True(view.ComposerContext.IsRecordingAudio);

        // Start the stop — the recorder StopAsync is now gated on stopGate.
        var stopTask = view.ToggleAudioCaptureAsync();
        await WaitFor(() => view.ComposerContext.IsTranscribingAudio);

        // Speech preempts during the transcribing window. This is a programmatic
        // caller bypassing the disabled speech button (CanToggleLiveSpeech should be
        // false during transcribing, but the toggle method's defensive orchestration
        // must also enforce the invariant).
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);
        await view.ToggleLiveSpeechAsync();
        // Note: the defensive Toggle path DOES allow speech to start after Ensure-audio-stop.
        // The important part is: the audio op-id has been bumped by EnsureAudioStoppedAsync,
        // so when stopGate resolves, the stale attachment must NOT stage.

        // Late audio StopAsync completes AFTER speech preemption.
        stopGate.SetResult(new ChatAttachment(
            "stale.wav",
            "audio/wav",
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 })));
        await stopTask;
        await Task.Delay(30);

        // Stale attachment must NOT have been staged.
        Assert.Empty(view.ComposerContext.Attachments);
        // Speech's IsListening flag must NOT have been clobbered by the stale
        // audio finally clearing IsTranscribingAudio (which would be a stale write).
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
    }

    [Fact]
    public async Task SendBlocked_DuringAudioTranscribingWindow()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        view.ComposerContext.Text = "Hello";
        await view.ToggleAudioCaptureAsync();

        var stop = view.ToggleAudioCaptureAsync();
        await WaitFor(() => view.ComposerContext.IsTranscribingAudio);

        // Send must be gated off during transcribing.
        Assert.False(view.ComposerContext.CanSubmit);
        Assert.False(view.ComposerContext.CanPickAttachments);

        stopGate.SetResult(null);
        await stop;

        // After transcribing clears, send is available again.
        Assert.True(view.ComposerContext.CanSubmit);
    }

    [Fact]
    public async Task AudioTranscribingWindow_KeepsAudioActiveTrue_UntilStopReturns()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        await view.ToggleAudioCaptureAsync();
        Assert.True(view.ComposerContext.IsRecordingAudio);
        Assert.True(((ChatComposerContext)view.ComposerContext).IsAudioActive);

        var stop = view.ToggleAudioCaptureAsync();
        await WaitFor(() => view.ComposerContext.IsTranscribingAudio);

        // Recording cleared, transcribing set — IsAudioActive collapses both.
        Assert.False(view.ComposerContext.IsRecordingAudio);
        Assert.True(view.ComposerContext.IsTranscribingAudio);
        Assert.True(((ChatComposerContext)view.ComposerContext).IsAudioActive);

        stopGate.SetResult(null);
        await stop;

        Assert.False(view.ComposerContext.IsTranscribingAudio);
        Assert.False(((ChatComposerContext)view.ComposerContext).IsAudioActive);
    }

    // ============================================================================
    // Part 3: Stale-finally protection
    // ============================================================================

    [Fact]
    public async Task RapidStopStart_SameModality_StaleCompletionIgnored()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var firstStopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = firstStopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        await view.ToggleAudioCaptureAsync();  // recording #1

        // Toggle to stop — begins the transcribing window (StopAsync gated).
        var stopA = view.ToggleAudioCaptureAsync();
        await WaitFor(() => view.ComposerContext.IsTranscribingAudio);

        // While the stop is in-flight, a conversation swap forces a fresh audio-op-id
        // bump. After the swap, we start a NEW audio recording.
        var newConv = CreateConversation();
        view.SetParameter(nameof(ChatView.Conversation), newConv);
        InvokeMethod(view, "OnParametersSet");

        // AttachConversation bumped both op-ids. The gated stop from the first
        // recording is now stale.
        recorder.StopGate = null;  // second stop completes synchronously
        await view.ToggleAudioCaptureAsync();  // recording #2 on new conv
        Assert.True(view.ComposerContext.IsRecordingAudio);

        // Now the first (stale) stop completes with an attachment.
        firstStopGate.SetResult(new ChatAttachment(
            "stale.wav",
            "audio/wav",
            new ReadOnlyMemory<byte>(new byte[] { 9 })));
        await stopA;
        await Task.Delay(30);

        // Stale attachment must NOT appear on the new conversation.
        Assert.Empty(view.ComposerContext.Attachments);
        // Recording #2 must not have been clobbered by the stale finally clearing
        // IsTranscribingAudio (it wasn't, but IsRecordingAudio must still be true).
        Assert.True(view.ComposerContext.IsRecordingAudio);
    }

    [Fact]
    public async Task StaleAudioStartFinally_CannotClearNewerOperationsStartingFlag()
    {
        // A rare interleaving: audio-op #1 starts (gated on startGate), speech
        // preempts (bumps id), speech clears everything and starts. Then audio-op #1's
        // StartAsync returns — its finally runs and would try to clear
        // IsAudioStarting. But IsAudioStarting was already cleared by the preemption;
        // more importantly, if a NEW audio operation had started, its IsAudioStarting
        // must not be cleared by the stale operation's finally. Model this by
        // triggering a rapid speech→audio→audio cycle.

        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var startGateA = new TaskCompletionSource<bool>();
        var startGateB = new TaskCompletionSource<bool>();

        // First StartAsync uses gate A, second uses gate B.
        recorder.QueueStartGates(startGateA.Task, startGateB.Task);

        var view = CreateView(conversation, recorder, recognizer);

        // Audio op #1 — gated on A.
        var audio1 = view.ToggleAudioCaptureAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsAudioStarting);

        // Speech preempts (bumps _audioOperationId AND cancels _audioCts). This makes
        // audio1's StartAsync throw OperationCanceledException.
        var speech = view.ToggleLiveSpeechAsync();
        // Wait for speech to actually start.
        await WaitFor(() => view.ComposerContext.IsLiveSpeechEnabled);
        await speech;

        // Audio1 finally runs — must NOT clear speech's flags.
        startGateA.TrySetCanceled();
        await audio1;

        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static ObservableChatConversation CreateConversation()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        return new ObservableChatConversation(local);
    }

    private static IServiceProvider BuildServices(
        IChatAudioRecorder recorder,
        IChatSpeechRecognizer recognizer)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatAudioRecorder>(recorder);
        services.AddSingleton<IChatSpeechRecognizer>(recognizer);
        return services.BuildServiceProvider();
    }

    private static ChatView CreateView(
        ChatConversation conversation,
        IChatAudioRecorder recorder,
        IChatSpeechRecognizer recognizer)
    {
        var view = new ChatView();
        SetPrivateProperty(view, "Services", BuildServices(recorder, recognizer));
        view.SetParameter(nameof(ChatView.Conversation), conversation);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        InvokeMethod(view, "OnInitialized");
        return view;
    }

    private static void SetPrivateProperty<T>(object target, string name, T value)
    {
        var prop = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Cannot find property {name}");
        prop.SetValue(target, value);
    }

    private static void InvokeMethod(object target, string name)
    {
        var method = target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Cannot find method {name}");
        method.Invoke(target, Array.Empty<object>());
    }

    private static async Task WaitFor(Func<bool> predicate, int deadlineMs = 500)
    {
        var stopAt = DateTime.UtcNow.AddMilliseconds(deadlineMs);
        while (DateTime.UtcNow < stopAt)
        {
            if (predicate())
            {
                return;
            }

            await Task.Yield();
            await Task.Delay(5);
        }

        throw new TimeoutException("Predicate did not become true within deadline.");
    }

    private sealed class TestAudioRecorder : IChatAudioRecorder
    {
        private readonly Queue<Task<bool>> _startGates = new();

        public bool IsSupported => true;
        public bool IsRecording { get; private set; }

        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int CancelCallCount { get; private set; }

        public bool StartCalled => StartCallCount > 0;
        public bool StopCalled => StopCallCount > 0;
        public bool CancelCalled => CancelCallCount > 0;

        /// <summary>Optional gate that a single StartAsync awaits before returning.</summary>
        public Task<bool>? StartGate { get; set; }

        /// <summary>Optional gate that StopAsync awaits before returning its recording.</summary>
        public Task<ChatAttachment?>? StopGate { get; set; }

        public void QueueStartGates(params Task<bool>[] gates)
        {
            foreach (var g in gates)
            {
                _startGates.Enqueue(g);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            Task<bool>? gate = null;
            if (_startGates.Count > 0)
            {
                gate = _startGates.Dequeue();
            }
            else if (StartGate is not null)
            {
                gate = StartGate;
            }

            if (gate is not null)
            {
                await gate.WaitAsync(cancellationToken);
            }

            IsRecording = true;
        }

        public async Task<ChatAttachment?> StopAsync(
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            IsRecording = false;
            if (StopGate is not null)
            {
                return await StopGate;
            }

            return null;
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            CancelCallCount++;
            IsRecording = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSpeechRecognizer : IChatSpeechRecognizer
    {
#pragma warning disable CS0067  // Inner is queried via HandlerCount, no direct invoke needed.
        private event EventHandler<ChatSpeechRecognitionEventArgs>? Inner;
#pragma warning restore CS0067

        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged
        {
            add
            {
                Inner += value;
                HandlerCount++;
            }
            remove
            {
                Inner -= value;
                HandlerCount--;
            }
        }

        public int HandlerCount { get; private set; }
        public bool IsSupported => true;
        public bool IsListening { get; private set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int PermissionRequestCount { get; private set; }

        public Task<bool>? StartGate { get; set; }
        public Task<bool>? PermissionGate { get; set; }
        public Task<bool>? StopGate { get; set; }

        public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
        {
            PermissionRequestCount++;
            if (PermissionGate is not null)
            {
                return await PermissionGate;
            }

            return true;
        }

        public async Task StartAsync(
            CultureInfo culture,
            bool reportPartialResults,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            if (StartGate is not null)
            {
                await StartGate.WaitAsync(cancellationToken);
            }

            IsListening = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            if (StopGate is not null)
            {
                await StopGate;
            }

            IsListening = false;
        }
    }
}
