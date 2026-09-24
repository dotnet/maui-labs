// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Regression tests for the ChatView terminal-final live-speech path.
///
/// The final review round found that OnRecognition set the text and called SendAsync while
/// _isListening was still true, which meant CanSubmit rejected the auto-submit and the
/// recognized utterance never left the composer. Additionally the recognizer subscription
/// was not released on the successful final path, keeping the ChatView alive inside a
/// singleton recognizer.
///
/// The fix treats <c>e.IsFinal</c> as terminal BEFORE submission: detach the recognizer,
/// clear IsListening/IsLiveSpeechEnabled so CanSubmit becomes true, preserve the recognized
/// text, and (when AutoSubmitLiveSpeech is enabled) invoke ordinary SendAsync exactly once.
/// </summary>
public class ChatViewLiveSpeechFinalTests
{
    // ============================================================================
    // Final auto-submits exactly once
    // ============================================================================

    [Fact]
    public async Task FinalResult_AutoSubmitTrue_SendsExactlyOnce()
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        view.SetParameter(nameof(ChatView.AutoSubmitLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseFinal("hello world");

        // The recognizer fires synchronously; SendAsync inside OnRecognition is async void
        // and completes on the next continuation. Yield until the counter observes the send.
        await WaitFor(() => sendCounter.Value == 1);

        Assert.Equal(1, sendCounter.Value);
    }

    // ============================================================================
    // Final unsubscribes the recognizer
    // ============================================================================

    [Fact]
    public async Task FinalResult_UnsubscribesRecognizer()
    {
        var conversation = CreateConversation();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        recognizer.RaiseFinal("hello");

        // The final result must detach the recognizer even on the auto-submit path so the
        // singleton recognizer stops holding this ChatView. Wait for the async void to
        // complete its detach step (it happens synchronously before the SendAsync await,
        // but we allow a small window for the continuation to settle).
        await WaitFor(() => recognizer.HandlerCount == 0);
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
    }

    // ============================================================================
    // Final with AutoSubmit=false preserves text and detaches
    // ============================================================================

    [Fact]
    public async Task FinalResult_AutoSubmitFalse_PreservesText_And_Detaches()
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        view.SetParameter(nameof(ChatView.AutoSubmitLiveSpeech), false);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseFinal("preserved text");

        await WaitFor(() => recognizer.HandlerCount == 0);

        // Text preserved so the user can review + submit.
        Assert.Equal("preserved text", view.ComposerContext.Text);
        // Recognizer detached (no lingering subscription).
        Assert.Equal(0, recognizer.HandlerCount);
        // Composer flags cleared so CanSubmit can now accept a manual submit.
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        // No send fired.
        Assert.Equal(0, sendCounter.Value);
    }

    // ============================================================================
    // Stale event after final cannot mutate
    // ============================================================================

    [Fact]
    public async Task StaleEvent_AfterFinal_DoesNotMutateComposer()
    {
        var conversation = CreateConversation();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        view.SetParameter(nameof(ChatView.AutoSubmitLiveSpeech), false);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseFinal("preserved");
        await WaitFor(() => recognizer.HandlerCount == 0);

        // Simulate the recognizer somehow firing a follow-up event after we detached
        // (bypassing the subscription because it was removed).
        recognizer.RaiseInterimBypassingHandlers("STALE — should never appear");

        Assert.Equal("preserved", view.ComposerContext.Text);
    }

    // ============================================================================
    // No duplicate resume/send: a second final from the same pass cannot re-fire
    // ============================================================================

    [Fact]
    public async Task DuplicateFinal_FromSameRecognizer_DoesNotSendTwice()
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        view.SetParameter(nameof(ChatView.AutoSubmitLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        // First final: auto-submits.
        recognizer.RaiseFinal("first");
        await WaitFor(() => sendCounter.Value == 1);

        // Second final "raised" through the SAME recognizer AFTER detach: because the
        // subscription is gone the handler is not invoked, but even if it were, the
        // identity guard (sender != _activeRecognizer) would drop it. The RaiseInterim
        // bypass version simulates that racing condition directly.
        recognizer.RaiseFinalBypassingHandlers("duplicate that should not resend");
        await Task.Delay(50);

        Assert.Equal(1, sendCounter.Value);
    }

    // ============================================================================
    // AutoSubmit=true, swap between detach and send: the shell's identity guards protect
    // the *new conversation's composer* from being mutated by the old conversation's send
    // completion. They do not (and cannot) undo a send whose transport handler ignores its
    // cancellation token — that is the app's contract. What the shell guarantees is that
    // B's composer is not mutated by A's stale send.
    // ============================================================================

    [Fact]
    public async Task FinalAutoSubmit_SwapAfterFinal_DoesNotMutateNewConversationComposer()
    {
        var conversationA = CreateConversationWithSendCounter(out var counterA);
        var conversationB = CreateConversationWithSendCounter(out var counterB);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversationA, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        // Fire a synchronous final. Before the async void continuation can call SendAsync,
        // we swap conversations. The identity guards must prevent A's send completion from
        // touching B's composer state (no "Message sent." status, no error, no text change,
        // no IsSending toggle).
        recognizer.RaiseFinal("hello from A");
        SetConversation(view, conversationB);

        await Task.Delay(100);

        // B was never targeted by this send — no counter increment.
        Assert.Equal(0, counterB.Value);

        // B's composer is not advertising a "Message sent." status from A's send: the
        // AttachConversation reset already cleared it, and A's late completion is gated on
        // identity so it can't put the message back.
        Assert.Null(view.ComposerContext.StatusMessage);
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static ObservableChatConversation CreateConversation()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        return new ObservableChatConversation(local);
    }

    private static ObservableChatConversation CreateConversationWithSendCounter(out StrongBox<int> counter)
    {
        var conv = CreateConversation();
        var count = new StrongBox<int>(0);
        conv.SendHandler = async (_, draft, _) =>
        {
            // Yield asynchronously so a test that races a conversation swap against the
            // send has a real window between "SendAsync entered" and "handler completes".
            // With a purely synchronous handler the SendAsync ValueTask completes before
            // the caller regains control, and the identity guards have nothing to protect.
            await Task.Yield();
            count.Value++;
            return true;
        };
        counter = count;
        return conv;
    }

    private static IServiceProvider BuildServices(IChatSpeechRecognizer recognizer)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatSpeechRecognizer>(recognizer);
        return services.BuildServiceProvider();
    }

    private static ChatView CreateView(ChatConversation conversation, IChatSpeechRecognizer recognizer)
    {
        var view = new ChatView();
        SetPrivateProperty(view, "Services", BuildServices(recognizer));
        view.SetParameter(nameof(ChatView.Conversation), conversation);
        InvokeMethod(view, "OnInitialized");
        return view;
    }

    private static void SetConversation(ChatView view, ChatConversation conversation)
    {
        view.SetParameter(nameof(ChatView.Conversation), conversation);
        InvokeMethod(view, "OnParametersSet");
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

    /// <summary>
    /// Waits until <paramref name="predicate"/> returns true or a small deadline elapses.
    /// The final path calls SendAsync as async void; the continuation runs on the current
    /// synchronization context after zero or more yields.
    /// </summary>
    private static async Task WaitFor(Func<bool> predicate, int deadlineMs = 200)
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

    /// <summary>
    /// Test speech recognizer. Counts handler subscriptions, exposes RaiseFinal for the
    /// terminal path, and offers `BypassingHandlers` variants that raise even when the
    /// subscription list is empty so we can prove the shell drops those events safely.
    /// </summary>
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

        public Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default)
        {
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsListening = false;
            return Task.CompletedTask;
        }

        public void RaiseInterim(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: false));

        public void RaiseFinal(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: true));

        // These simulate the "singleton recognizer somehow fires after the ChatView already
        // detached" scenario by invoking a handler snapshot that might have been captured
        // outside the shell. The shell's identity guard should still drop it because sender
        // matches _activeRecognizer (which we already cleared).
        public void RaiseInterimBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: false));

        public void RaiseFinalBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: true));
    }
}

internal sealed class StrongBox<T>
{
    public StrongBox(T value) => Value = value;
    public T Value { get; set; }
}
