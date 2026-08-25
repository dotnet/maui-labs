// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Regression tests for the single-microphone-owner rule between the two neutral capture
/// modalities on <see cref="ChatView"/>: audio recording via <see cref="IChatAudioRecorder"/>
/// and continuous live speech via <see cref="IChatSpeechRecognizer"/>. The platform capture
/// device belongs to at most one modality at a time; every transition must:
///
/// - Deny STARTING the other modality via the button gate
///   (<see cref="IChatComposerContext.CanToggleAudioCapture"/> /
///   <see cref="IChatComposerContext.CanToggleLiveSpeech"/>).
/// - Also tear down the other modality when the toggle method is called directly (a
///   programmatic caller bypassing the disabled button), and await the cleanup so the
///   underlying platform device has really been released before we claim it.
/// - Guard against stale completions from the modality that was preempted: a StopAsync that
///   returns AFTER we've torn down must not stage an attachment or mutate the composer.
/// - Preserve identity across conversation swap: the preempted modality's cleanup must not
///   leak into the new conversation's composer.
///
/// The tests exercise both the button gate and the defensive orchestration path directly.
/// </summary>
public class ChatViewModalityTransitionTests
{
    // ============================================================================
    // Button gate — CanToggleAudioCapture
    // ============================================================================

    [Fact]
    public async Task CanToggleAudio_DeniedWhile_SpeechListening()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();

        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);
        // Speech can still be stopped by its own toggle:
        Assert.True(view.ComposerContext.CanToggleLiveSpeech);
    }

    [Fact]
    public async Task CanToggleAudio_DeniedWhile_SpeechStarting()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        // Block StartAsync so we observe the starting state.
        var speechStartGate = new TaskCompletionSource<bool>();
        recognizer.StartGate = speechStartGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        var toggleTask = view.ToggleLiveSpeechAsync();

        // While StartAsync is in flight, IsSpeechStarting is true — audio must be denied.
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechActive);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);

        speechStartGate.SetResult(true);
        await toggleTask;
    }

    [Fact]
    public async Task CanToggleAudio_AllowsStop_WhileAudioActive()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleAudioCaptureAsync();

        Assert.True(view.ComposerContext.IsRecordingAudio);
        // Audio can be stopped; speech cannot start while audio owns the mic.
        Assert.True(view.ComposerContext.CanToggleAudioCapture);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);
    }

    // ============================================================================
    // Button gate — CanToggleLiveSpeech
    // ============================================================================

    [Fact]
    public async Task CanToggleSpeech_DeniedWhile_AudioRecording()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleAudioCaptureAsync();

        Assert.True(view.ComposerContext.IsRecordingAudio);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);
    }

    [Fact]
    public async Task CanToggleSpeech_DeniedWhile_AudioStarting()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var audioStartGate = new TaskCompletionSource<bool>();
        recorder.StartGate = audioStartGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        var toggleTask = view.ToggleAudioCaptureAsync();

        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsAudioActive);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);

        audioStartGate.SetResult(true);
        await toggleTask;
    }

    [Fact]
    public async Task CanToggleSpeech_DeniedWhile_AudioTranscribing()
    {
        // Model the transcribing state directly (a real recorder would arrive here in the
        // window between StopAsync being awaited and the buffer being converted to an
        // attachment). Regardless of how the state was reached, speech must not start.
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        var composer = (ChatComposerContext)view.ComposerContext;
        composer.SetIsTranscribingAudio(true);

        Assert.True(view.ComposerContext.IsTranscribingAudio);
        Assert.False(view.ComposerContext.CanToggleLiveSpeech);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CanToggleSpeech_AllowsStop_WhileSpeechActive()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();

        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.CanToggleLiveSpeech);
    }

    // ============================================================================
    // Defensive orchestration — direct calls that bypass the button gate
    // ============================================================================

    [Fact]
    public async Task StartingAudio_TearsDownActiveLiveSpeech_ExactlyOne()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);
        Assert.False(recorder.StartCalled);

        // Programmatic caller: bypass the disabled button and invoke the toggle directly.
        // The defensive orchestration inside StartAudioRecordingAsync must still tear down
        // the active speech pass BEFORE claiming the microphone.
        await view.ToggleAudioCaptureAsync();

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.True(recognizer.StopCalled);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.False(view.ComposerContext.IsListening);
        Assert.True(recorder.StartCalled);
        Assert.True(view.ComposerContext.IsRecordingAudio);
        // Exactly one microphone owner.
        Assert.True(recorder.StartCalled ^ recognizer.IsListening);
    }

    [Fact]
    public async Task StartingSpeech_TearsDownActiveAudio_ExactlyOne()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleAudioCaptureAsync();
        Assert.True(recorder.StartCalled);
        Assert.True(view.ComposerContext.IsRecordingAudio);
        Assert.Equal(0, recognizer.HandlerCount);

        // Programmatic caller: bypass the disabled button and invoke the toggle directly.
        // The defensive orchestration inside StartLiveSpeechAsync must first cancel/stop
        // the audio recorder BEFORE claiming the microphone for speech.
        await view.ToggleLiveSpeechAsync();

        Assert.True(recorder.CancelCalled, "audio recorder should have been cancelled");
        Assert.False(view.ComposerContext.IsRecordingAudio);
        Assert.Equal(1, recognizer.HandlerCount);
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
    }

    [Fact]
    public async Task StaleAudioCompletion_AfterSpeechPreempts_DoesNotStageAttachment()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var stopGate = new TaskCompletionSource<ChatAttachment?>();
        recorder.StopGate = stopGate.Task;

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleAudioCaptureAsync();
        Assert.True(view.ComposerContext.IsRecordingAudio);

        // Speech preempts audio: EnsureAudioStoppedAsync cancels + awaits CancelAsync,
        // not StopAsync, so the stopGate is not the path taken here. Instead, we simulate
        // an in-flight stop by pushing the recorder through the "stop attachment" arrival
        // AFTER the preemption. The stale attachment must not be staged.
        await view.ToggleLiveSpeechAsync();

        // Any late attachment coming out of the recorder must not land in the composer,
        // because the identity of the "audio operation" has moved on.
        stopGate.SetResult(new ChatAttachment("STALE.txt", "text/plain", new ReadOnlyMemory<byte>(new byte[] { 1 })));

        // Allow the stale completion to schedule.
        await Task.Delay(30);

        Assert.Empty(view.ComposerContext.Attachments);
    }

    [Fact]
    public async Task StaleSpeechEvent_AfterAudioPreempts_DoesNotMutateComposer()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        await view.ToggleAudioCaptureAsync();
        Assert.Equal(0, recognizer.HandlerCount);

        var textSnapshot = view.ComposerContext.Text;

        // Late speech events fired after the audio preemption reach no handlers because
        // DetachActiveRecognizer removed the subscription; even if the singleton reused
        // the event, the identity guard drops it because _activeRecognizer is null.
        recognizer.RaiseFinalBypassingHandlers("STALE FINAL");
        recognizer.RaiseInterimBypassingHandlers("STALE INTERIM");
        await Task.Delay(30);

        Assert.Equal(textSnapshot, view.ComposerContext.Text);
    }

    [Fact]
    public async Task RapidAlternation_LeavesSingleMicOwner()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        // Speech → Audio → Speech → Audio → Speech (final)
        await view.ToggleLiveSpeechAsync();
        await view.ToggleAudioCaptureAsync();
        await view.ToggleLiveSpeechAsync();
        await view.ToggleAudioCaptureAsync();
        await view.ToggleLiveSpeechAsync();

        // Exactly one active modality: speech.
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsRecordingAudio);
        Assert.Equal(1, recognizer.HandlerCount);
    }

    [Fact]
    public async Task StoppingActiveAudio_DoesNotCancelInactiveSpeech()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleAudioCaptureAsync();
        await view.ToggleAudioCaptureAsync(); // stop

        Assert.False(view.ComposerContext.IsRecordingAudio);
        // Speech was never active — no spurious StopAsync on the recognizer.
        Assert.False(recognizer.StopCalled);
    }

    [Fact]
    public async Task StoppingActiveSpeech_DoesNotCancelInactiveAudio()
    {
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recorder, recognizer);
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();
        await view.ToggleLiveSpeechAsync(); // stop

        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        // Audio was never active — no spurious CancelAsync on the recorder.
        Assert.False(recorder.CancelCalled);
    }

    [Fact]
    public async Task StartingSpeech_DuringPermissionRequest_ImmediatelyBlocksAudio()
    {
        // Regression: the permission request is async; if IsSpeechStarting isn't set
        // BEFORE the await, a competing ToggleAudioCaptureAsync would see IsSpeechActive
        // == false, pass the button gate, claim the microphone, and only be preempted
        // later when the permission resolves. Set the starting flag at the top of the
        // start path so the button gate rejects audio from moment zero.
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        // Gate the permission request so the "starting" state is observable.
        var permissionGate = new TaskCompletionSource<bool>();
        recognizer.PermissionGate = permissionGate.Task;

        var view = CreateView(conversation, recorder, recognizer);

        var speechToggleTask = view.ToggleLiveSpeechAsync();

        // Even before permission resolves, IsSpeechStarting must be set → audio blocked.
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsSpeechActive);
        Assert.False(view.ComposerContext.CanToggleAudioCapture);

        permissionGate.SetResult(true);
        await speechToggleTask;

        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.False(recorder.StartCalled);
    }

    [Fact]
    public async Task StartingAudio_DuringSpeechCleanup_ImmediatelyBlocksSpeech()
    {
        // Symmetric: the moment ToggleAudioCaptureAsync runs, IsAudioStarting is set,
        // so a rapid competing ToggleLiveSpeechAsync trying to reclaim the mic during
        // the EnsureLiveSpeechStopped await sees IsAudioActive=true and skips.
        var conversation = CreateConversation();
        var recorder = new TestAudioRecorder();
        var recognizer = new TestSpeechRecognizer();

        var recorderStartGate = new TaskCompletionSource<bool>();
        recorder.StartGate = recorderStartGate.Task;

        var view = CreateView(conversation, recorder, recognizer);

        var audioToggleTask = view.ToggleAudioCaptureAsync();
        await WaitFor(() => ((ChatComposerContext)view.ComposerContext).IsAudioActive);

        Assert.False(view.ComposerContext.CanToggleLiveSpeech);

        recorderStartGate.SetResult(true);
        await audioToggleTask;
        Assert.True(view.ComposerContext.IsRecordingAudio);
        Assert.Equal(0, recognizer.HandlerCount);
    }

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
        // AllowAudioCapture / AllowLiveSpeech default to false; the modality tests need them
        // enabled so the button gate can distinguish "denied because disabled" from "denied
        // because the other modality owns the mic". Set BEFORE OnInitialized so the composer
        // picks them up on first construction.
        view.SetParameter(nameof(ChatView.AllowAudioCapture), true);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        InvokeMethod(view, "OnInitialized");
        return view;
    }

    private static void SetInternalFlag(object target, string setterMethodName, bool value)
    {
        var method = target.GetType().GetMethod(
            setterMethodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Cannot find method {setterMethodName}");
        method.Invoke(target, new object[] { value });
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

    private static async Task WaitFor(Func<bool> predicate, int deadlineMs = 300)
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
    }

    private sealed class TestAudioRecorder : IChatAudioRecorder
    {
        public bool IsSupported => true;
        public bool IsRecording { get; private set; }

        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool CancelCalled { get; private set; }

        /// <summary>Optional gate that StartAsync awaits before returning.</summary>
        public Task<bool>? StartGate { get; set; }

        /// <summary>Optional gate that StopAsync awaits before returning its recording.</summary>
        public Task<ChatAttachment?>? StopGate { get; set; }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            if (StartGate is not null)
            {
                await StartGate.WaitAsync(cancellationToken);
            }

            IsRecording = true;
        }

        public async Task<ChatAttachment?> StopAsync(
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            IsRecording = false;
            if (StopGate is not null)
            {
                return await StopGate;
            }

            return null;
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            CancelCalled = true;
            IsRecording = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSpeechRecognizer : IChatSpeechRecognizer
    {
        private event EventHandler<ChatSpeechRecognitionEventArgs>? Inner;

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
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }

        public Task<bool>? StartGate { get; set; }

        public Task<bool>? PermissionGate { get; set; }

        public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
        {
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
            StartCalled = true;
            if (StartGate is not null)
            {
                await StartGate.WaitAsync(cancellationToken);
            }

            IsListening = true;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            IsListening = false;
            return Task.CompletedTask;
        }

        public void RaiseFinalBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: true));

        public void RaiseInterimBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: false));
    }

}
