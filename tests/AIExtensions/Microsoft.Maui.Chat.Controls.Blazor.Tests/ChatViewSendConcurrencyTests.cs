// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies that ChatComposerContext.CanSubmit collapses the send-in-flight guard with
/// the conversation's own CanSend, so a rapid double-submit is rejected without touching
/// the transport twice.
/// </summary>
public class ChatViewSendConcurrencyTests
{
    [Fact]
    public void CanSubmit_Returns_False_WhileSending()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = new ChatComposerContext();
        context.AttachConversation(conversation);
        context.Text = "hi";

        Assert.True(context.CanSubmit);

        context.SetIsSending(true);

        // The neutral guard is what the shell relies on to reject a second submit while the
        // first is in flight; without it a fast tap + Enter would race past CanSend.
        Assert.False(context.CanSubmit);

        context.SetIsSending(false);

        Assert.True(context.CanSubmit);
    }

    [Fact]
    public void CanSubmit_Returns_False_WhileComposing()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = new ChatComposerContext();
        context.AttachConversation(conversation);
        context.Text = "hi";

        context.SetComposing(true);

        // While a picker/recorder/live-speech operation is composing the draft, the shell
        // must refuse to submit the (potentially partial) draft.
        Assert.False(context.CanSubmit);

        context.SetComposing(false);

        Assert.True(context.CanSubmit);
    }

    [Fact]
    public void CanSubmit_Returns_False_WhileConversationBusy()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = new ChatComposerContext();
        context.AttachConversation(conversation);
        context.Text = "hi";

        conversation.SetStatus(ChatConversationStatus.Busy);

        Assert.False(context.CanSubmit);
    }
}
