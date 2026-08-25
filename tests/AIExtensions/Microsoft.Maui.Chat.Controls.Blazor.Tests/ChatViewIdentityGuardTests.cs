// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Regression tests for the ChatView operation-identity guards. Independent code review found
/// that an operation started for conversation A could clear or mutate conversation B's composer
/// after await. These tests drive the ChatView directly (bypassing the Blazor renderer) and
/// prove that:
///
/// - A slow send whose await completes AFTER a conversation swap does not mutate the new
///   composer (no text clear, no "Message sent." status).
/// - A slow picker whose await completes AFTER a conversation swap does not stage the picked
///   files into the new composer.
/// - A stale live-speech event fired AFTER Detach does not mutate the composer.
/// - Dispose cancels every in-flight op and detaches the recognizer, so a subsequent stale
///   event or send-completion cannot leak into a torn-down component.
/// </summary>
public class ChatViewIdentityGuardTests
{
    // ============================================================================
    // #1 - Conversation swap invalidates in-flight send
    // ============================================================================

    [Fact]
    public async Task SlowSend_Swap_DoesNotClearNewConversationDraft()
    {
        var a = CreateConversation("a");
        var b = CreateConversation("b");
        var services = BuildServices();

        // Slow send: hold the send until we signal it (simulating a network-bound send).
        var sendGate = new TaskCompletionSource<bool>();
        a.SendHandler = async (_, _, _) =>
        {
            await sendGate.Task;
            return true;
        };

        var view = CreateView(services, a);
        view.ComposerContext.Text = "draft-for-A";
        var sendTask = view.SendAsync();
        // Give the async method a chance to enter its await.
        await Task.Yield();

        // Swap to conversation B and let the user type a new draft into the new composer.
        SetConversation(view, b);
        view.ComposerContext.Text = "draft-for-B";

        // Now let A's send complete "successfully" (accepted=true).
        sendGate.SetResult(true);
        await sendTask;

        // B's draft must survive. The stale completion from A must not clear it or advertise a
        // "Message sent." status against B.
        Assert.Equal("draft-for-B", view.ComposerContext.Text);
        Assert.Null(view.ComposerContext.StatusMessage);
    }

    [Fact]
    public async Task SlowSend_Swap_DoesNotSurfaceFailureOnNewConversation()
    {
        var a = CreateConversation("a");
        var b = CreateConversation("b");
        var services = BuildServices();

        var sendGate = new TaskCompletionSource<bool>();
        a.SendHandler = async (_, _, _) =>
        {
            await sendGate.Task;
            throw new InvalidOperationException("simulated");
        };

        var view = CreateView(services, a);
        view.ComposerContext.Text = "hi";
        var sendTask = view.SendAsync();
        await Task.Yield();

        SetConversation(view, b);

        // Fail A's send AFTER the swap. The failure belongs to A, not B, so the error banner
        // must not appear on the new conversation's composer.
        sendGate.SetException(new InvalidOperationException("simulated"));
        try { await sendTask; } catch { /* swallowed by SendAsync */ }

        Assert.Null(view.ComposerContext.ErrorMessage);
    }

    // ============================================================================
    // #1 - Conversation swap invalidates in-flight picker
    // ============================================================================

    [Fact]
    public async Task SlowPicker_Swap_DoesNotStageAttachmentsOnNewConversation()
    {
        var a = CreateConversation("a");
        var b = CreateConversation("b");

        var pickerGate = new TaskCompletionSource<IReadOnlyList<ChatAttachment>>();
        var picker = new GatePicker(pickerGate.Task);

        var services = BuildServices(collection =>
            collection.AddSingleton<IChatAttachmentPicker>(picker));

        var view = CreateView(services, a);
        view.SetParameter(nameof(ChatView.AllowAttachments), true);
        var pickTask = view.PickAttachmentsAsync();
        await Task.Yield();

        // Swap to B while the picker is open.
        SetConversation(view, b);

        // Now the picker "completes" with a picked file. Since we swapped, the file belongs to
        // A's intent and must NOT stage on B's composer.
        pickerGate.SetResult(new[]
        {
            new ChatAttachment("hi.txt", "text/plain", new ReadOnlyMemory<byte>(new byte[] { 1 })),
        });
        await pickTask;

        Assert.Empty(view.ComposerContext.Attachments);
    }

    // ============================================================================
    // #2 - Live-speech subscription lifetime
    // ============================================================================

    [Fact]
    public async Task LiveSpeech_TerminalError_UnsubscribesRecognizer()
    {
        var a = CreateConversation("a");
        var recognizer = new TestSpeechRecognizer();

        var services = BuildServices(collection =>
            collection.AddSingleton<IChatSpeechRecognizer>(recognizer));

        var view = CreateView(services, a);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.Equal(1, recognizer.HandlerCount);

        // Fatal error: shell must unsubscribe so the singleton recognizer stops holding this
        // ChatView.
        recognizer.RaiseFatal();

        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.False(view.ComposerContext.IsListening);
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.Equal("Live voice could not continue.", view.ComposerContext.ErrorMessage);
    }

    [Fact]
    public async Task LiveSpeech_ConversationSwap_UnsubscribesRecognizer()
    {
        var a = CreateConversation("a");
        var b = CreateConversation("b");
        var recognizer = new TestSpeechRecognizer();

        var services = BuildServices(collection =>
            collection.AddSingleton<IChatSpeechRecognizer>(recognizer));

        var view = CreateView(services, a);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);

        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        // Swap conversations: the shell must detach the recognizer so events from the old
        // conversation's context cannot mutate the new composer.
        SetConversation(view, b);

        Assert.Equal(0, recognizer.HandlerCount);
    }

    [Fact]
    public async Task LiveSpeech_StaleEventAfterDetach_DoesNotMutateComposer()
    {
        var a = CreateConversation("a");
        var recognizer = new TestSpeechRecognizer();

        var services = BuildServices(collection =>
            collection.AddSingleton<IChatSpeechRecognizer>(recognizer));

        var view = CreateView(services, a);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();
        await view.ToggleLiveSpeechAsync(); // toggle off - detach

        // Simulate a stale event fired by the singleton recognizer AFTER we detached. The
        // handler was already removed, so no callback should be invoked. This asserts the
        // detach path fully unsubscribed.
        var beforeText = view.ComposerContext.Text;
        recognizer.RaiseInterim("STALE - should never appear");

        Assert.Equal(beforeText, view.ComposerContext.Text);
    }

    // ============================================================================
    // Dispose - cancels in-flight, detaches recognizer
    // ============================================================================

    [Fact]
    public async Task Dispose_CancelsInFlightSend_AndDetachesRecognizer()
    {
        var a = CreateConversation("a");
        var recognizer = new TestSpeechRecognizer();

        var services = BuildServices(collection =>
            collection.AddSingleton<IChatSpeechRecognizer>(recognizer));

        var view = CreateView(services, a);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        // Slow send in flight...
        var sendGate = new TaskCompletionSource<bool>();
        a.SendHandler = async (_, _, _) =>
        {
            await sendGate.Task;
            return true;
        };
        view.ComposerContext.Text = "hi";
        var sendTask = view.SendAsync();
        await Task.Yield();

        // Dispose the component.
        view.Dispose();

        // The recognizer must be unhooked so it stops holding this view.
        Assert.Equal(0, recognizer.HandlerCount);

        // The send CTS must have been cancelled: unblock the handler and confirm the awaited
        // send completes without leaking mutations onto the (now-disposed) composer.
        sendGate.SetResult(true);
        await sendTask;
        Assert.Null(view.ComposerContext.StatusMessage);
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static ObservableChatConversation CreateConversation(string id)
    {
        var local = new ChatParticipant(id + "-me", "Me", ChatParticipantKind.Local);
        return new ObservableChatConversation(local);
    }

    private static IServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ChatView CreateView(IServiceProvider services, ChatConversation conversation)
    {
        var view = new ChatView();
        SetPrivateProperty(view, "Services", services);
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

    /// <summary>Test speech recognizer that counts handler subscriptions so the unsubscribe path can be asserted.</summary>
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

        public void RaiseFatal() =>
            Inner?.Invoke(this,
                new ChatSpeechRecognitionEventArgs(
                    string.Empty,
                    isFinal: false,
                    ChatSpeechRecognitionErrorKind.Fatal,
                    exception: null));
    }

    /// <summary>Attachment picker that returns whatever the awaiting caller signals through the gate task.</summary>
    private sealed class GatePicker : IChatAttachmentPicker
    {
        private readonly Task<IReadOnlyList<ChatAttachment>> _gate;

        public GatePicker(Task<IReadOnlyList<ChatAttachment>> gate) => _gate = gate;

        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            FilePickerFileType? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default) => _gate;
    }
}

internal static class ChatViewTestExtensions
{
    /// <summary>Sets a public property on a ChatView instance for tests that bypass the renderer.</summary>
    public static void SetParameter(this ChatView view, string name, object? value)
    {
        var prop = typeof(ChatView).GetProperty(name)
            ?? throw new InvalidOperationException($"Cannot find parameter {name}");
        prop.SetValue(view, value);
    }
}
