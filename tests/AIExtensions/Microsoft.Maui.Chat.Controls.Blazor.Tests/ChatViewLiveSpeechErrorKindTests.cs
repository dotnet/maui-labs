// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Regression tests for the ChatView terminal-final path across every
/// <see cref="ChatSpeechRecognitionErrorKind"/>. The one-pass recognizer model treats
/// <c>e.IsFinal</c> as terminal regardless of error kind:
///
/// - Success (<see cref="ChatSpeechRecognitionErrorKind.None"/>): preserve text; auto-submit
///   iff text is non-empty and AutoSubmitLiveSpeech is enabled.
/// - Recoverable (<see cref="ChatSpeechRecognitionErrorKind.NoSpeech"/> /
///   <see cref="ChatSpeechRecognitionErrorKind.Aborted"/>): silent teardown; composer usable.
/// - Transient (<see cref="ChatSpeechRecognitionErrorKind.Transient"/>): user-safe status
///   message; teardown; composer usable.
/// - Fatal (<see cref="ChatSpeechRecognitionErrorKind.PermissionDenied"/> /
///   <see cref="ChatSpeechRecognitionErrorKind.Fatal"/> /
///   <see cref="ChatSpeechRecognitionErrorKind.LanguageNotSupported"/>): user-safe error
///   banner; teardown; composer usable.
///
/// Every path must:
/// - Detach the recognizer exactly once (subscription released).
/// - Clear <c>IsListening</c> / <c>IsLiveSpeechEnabled</c> so <c>CanSubmit</c> is not
///   permanently blocked and the user can toggle live speech again.
/// - Not auto-submit for any non-None error, nor for empty text.
/// - Ignore stale/duplicate finals fired after teardown.
/// </summary>
public class ChatViewLiveSpeechErrorKindTests
{
    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.NoSpeech)]
    [InlineData(ChatSpeechRecognitionErrorKind.Aborted)]
    public async Task FinalRecoverableError_Detaches_And_LeavesComposerUsable_WithoutError(
        ChatSpeechRecognitionErrorKind errorKind)
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();
        Assert.Equal(1, recognizer.HandlerCount);

        recognizer.RaiseError(errorKind, isFinal: true);

        await WaitFor(() => recognizer.HandlerCount == 0);

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        // Recoverable: no scary error banner, no status message.
        Assert.Null(view.ComposerContext.ErrorMessage);
        Assert.Null(view.ComposerContext.StatusMessage);
        // No auto-submit for an error final.
        Assert.Equal(0, sendCounter.Value);
    }

    [Fact]
    public async Task FinalTransientError_Detaches_And_ShowsUserSafeStatus()
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseError(ChatSpeechRecognitionErrorKind.Transient, isFinal: true);

        await WaitFor(() => recognizer.HandlerCount == 0);

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        // Transient shows a user-safe status message (not an error banner).
        Assert.Equal("Live voice was interrupted.", view.ComposerContext.StatusMessage);
        Assert.Null(view.ComposerContext.ErrorMessage);
        Assert.Equal(0, sendCounter.Value);
    }

    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.PermissionDenied)]
    [InlineData(ChatSpeechRecognitionErrorKind.Fatal)]
    [InlineData(ChatSpeechRecognitionErrorKind.LanguageNotSupported)]
    public async Task FinalFatalError_Detaches_And_ShowsUserSafeError(
        ChatSpeechRecognitionErrorKind errorKind)
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseError(errorKind, isFinal: true);

        await WaitFor(() => recognizer.HandlerCount == 0);

        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.ComposerContext.IsListening);
        Assert.False(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.Equal("Live voice could not continue.", view.ComposerContext.ErrorMessage);
        Assert.Equal(0, sendCounter.Value);
    }

    [Fact]
    public async Task Final_None_EmptyText_DoesNotAutoSubmit()
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        view.SetParameter(nameof(ChatView.AutoSubmitLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseFinal(string.Empty);

        await WaitFor(() => recognizer.HandlerCount == 0);

        // Empty final: detached, no error, no submit. CanSubmit would reject an empty draft
        // anyway, but we short-circuit to make the intent explicit and avoid a wasted round
        // through SendAsync's rejection path.
        Assert.Equal(0, recognizer.HandlerCount);
        Assert.False(view.ComposerContext.IsListening);
        Assert.Equal(0, sendCounter.Value);
    }

    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.NoSpeech)]
    [InlineData(ChatSpeechRecognitionErrorKind.Aborted)]
    [InlineData(ChatSpeechRecognitionErrorKind.Transient)]
    [InlineData(ChatSpeechRecognitionErrorKind.PermissionDenied)]
    [InlineData(ChatSpeechRecognitionErrorKind.Fatal)]
    [InlineData(ChatSpeechRecognitionErrorKind.LanguageNotSupported)]
    public async Task FinalError_AllowsSubsequentSubmit(ChatSpeechRecognitionErrorKind errorKind)
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseError(errorKind, isFinal: true);
        await WaitFor(() => recognizer.HandlerCount == 0);

        // The user recovers by typing and submitting through the ordinary path. CanSubmit
        // must accept because IsListening/IsLiveSpeechEnabled were cleared during teardown.
        view.ComposerContext.Text = "user recovery";
        Assert.True(view.ComposerContext.CanSubmit);

        await ((IChatComposerContext)view.ComposerContext).SubmitAsync();
        await WaitFor(() => sendCounter.Value == 1);
        Assert.Equal(1, sendCounter.Value);
    }

    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.NoSpeech)]
    [InlineData(ChatSpeechRecognitionErrorKind.Aborted)]
    [InlineData(ChatSpeechRecognitionErrorKind.Transient)]
    [InlineData(ChatSpeechRecognitionErrorKind.PermissionDenied)]
    [InlineData(ChatSpeechRecognitionErrorKind.Fatal)]
    [InlineData(ChatSpeechRecognitionErrorKind.LanguageNotSupported)]
    public async Task FinalError_AllowsSubsequentToggle(ChatSpeechRecognitionErrorKind errorKind)
    {
        var conversation = CreateConversation();
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseError(errorKind, isFinal: true);
        await WaitFor(() => recognizer.HandlerCount == 0);

        // The user recovers by toggling live speech back on. This is the intended "single
        // pass ends, user re-arms" flow, and it must succeed: IsLiveSpeechEnabled was
        // cleared, so ToggleLiveSpeechAsync takes the "enable" branch and re-subscribes.
        await view.ToggleLiveSpeechAsync();

        Assert.Equal(1, recognizer.HandlerCount);
        Assert.True(view.ComposerContext.IsLiveSpeechEnabled);
        Assert.True(view.ComposerContext.IsListening);
    }

    [Theory]
    [InlineData(ChatSpeechRecognitionErrorKind.NoSpeech)]
    [InlineData(ChatSpeechRecognitionErrorKind.Aborted)]
    [InlineData(ChatSpeechRecognitionErrorKind.Transient)]
    [InlineData(ChatSpeechRecognitionErrorKind.Fatal)]
    public async Task StaleDuplicate_FinalAfterTeardown_DoesNotMutateComposer(
        ChatSpeechRecognitionErrorKind errorKind)
    {
        var conversation = CreateConversationWithSendCounter(out var sendCounter);
        var recognizer = new TestSpeechRecognizer();

        var view = CreateView(conversation, recognizer);
        view.SetParameter(nameof(ChatView.AllowLiveSpeech), true);
        await view.ToggleLiveSpeechAsync();

        recognizer.RaiseError(errorKind, isFinal: true);
        await WaitFor(() => recognizer.HandlerCount == 0);

        var errorSnapshot = view.ComposerContext.ErrorMessage;
        var statusSnapshot = view.ComposerContext.StatusMessage;
        var textSnapshot = view.ComposerContext.Text;

        // Simulate the singleton recognizer somehow racing a stale event after the pass was
        // torn down. Because DetachActiveRecognizer removed the subscription, the invoke
        // reaches no handlers. Even if it did, the identity guard would drop it because
        // _activeRecognizer is null.
        recognizer.RaiseFinalBypassingHandlers("STALE - should never appear");
        recognizer.RaiseInterimBypassingHandlers("STALE INTERIM - should never appear");
        await Task.Delay(30);

        Assert.Equal(errorSnapshot, view.ComposerContext.ErrorMessage);
        Assert.Equal(statusSnapshot, view.ComposerContext.StatusMessage);
        Assert.Equal(textSnapshot, view.ComposerContext.Text);
        Assert.Equal(0, sendCounter.Value);
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

        public void RaiseFinal(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: true));

        public void RaiseError(ChatSpeechRecognitionErrorKind kind, bool isFinal) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(
                string.Empty, isFinal, kind, exception: null));

        public void RaiseFinalBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: true));

        public void RaiseInterimBypassingHandlers(string text) =>
            Inner?.Invoke(this, new ChatSpeechRecognitionEventArgs(text, isFinal: false));
    }
}
