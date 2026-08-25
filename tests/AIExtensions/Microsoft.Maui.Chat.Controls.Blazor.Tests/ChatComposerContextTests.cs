// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>Verifies the composer state machine that guards send / stop / attachment flows.</summary>
public class ChatComposerContextTests
{
    private static ChatComposerContext CreateContext(ChatConversation? conversation = null)
    {
        var context = new ChatComposerContext(
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty);
        context.AttachConversation(conversation);
        return context;
    }

    [Fact]
    public void CreateDraft_Trims_Text()
    {
        var context = CreateContext();
        context.Text = "   hello world   ";

        var draft = context.CreateDraft();

        Assert.Equal("hello world", draft.Text);
    }

    [Fact]
    public void CanSubmit_False_WhenNoConversation()
    {
        var context = CreateContext();
        context.Text = "hello";

        Assert.False(context.CanSubmit);
    }

    [Fact]
    public void CanSubmit_False_WhileSending()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        context.Text = "hi";

        context.SetIsSending(true);

        Assert.False(context.CanSubmit);
    }

    [Fact]
    public void CanSubmit_True_WhenDraftAcceptable()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        context.Text = "hi";

        Assert.True(context.CanSubmit);
    }

    [Fact]
    public void ClearAcceptedDraft_ClearsMatchingText()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        context.Text = "hello";
        var draft = context.CreateDraft();

        context.ClearAcceptedDraft(draft);

        Assert.Equal(string.Empty, context.Text);
    }

    [Fact]
    public void ClearAcceptedDraft_LeavesTextIfChanged()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        context.Text = "hello";
        var draft = context.CreateDraft();
        context.Text = "in flight edit";

        context.ClearAcceptedDraft(draft);

        Assert.Equal("in flight edit", context.Text);
    }

    [Fact]
    public async Task Attachment_AddAndRemove_Roundtrip()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        var attachment = new ChatAttachment("hi.txt", "text/plain", new ReadOnlyMemory<byte>(new byte[] { 1 }));

        await context.AddAttachmentAsync(attachment);
        Assert.Single(context.Attachments);

        var removed = await context.RemoveAttachmentAsync(attachment);
        Assert.True(removed);
        Assert.Empty(context.Attachments);
    }

    [Fact]
    public void SetStatusMessage_And_SetErrorMessage_RaiseChanged()
    {
        var context = CreateContext();
        var raised = 0;
        context.Changed += () => raised++;

        context.SetStatusMessage("sending");
        context.SetErrorMessage("boom");

        Assert.Equal(2, raised);
        Assert.Equal("sending", context.StatusMessage);
        Assert.Equal("boom", context.ErrorMessage);
    }

    [Fact]
    public void SetComposing_TogglesFlagAndBlocksAttachments()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var context = CreateContext(conversation);
        context.AllowAttachments = true;

        Assert.True(context.CanPickAttachments);

        context.SetComposing(true);
        Assert.False(context.CanPickAttachments);

        context.SetComposing(false);
        Assert.True(context.CanPickAttachments);
    }
}
