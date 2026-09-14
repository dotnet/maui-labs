// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies the composer-context action contract exposes plain <see cref="Task"/>-returning
/// methods (not <c>EventCallback</c>) and delegate directly to the shared controller.
/// </summary>
public class ChatComposerContextActionTests
{
    [Fact]
    public async Task SubmitAsync_DelegatesToController()
    {
        var local = new ChatParticipant("local", kind: ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        using var controller = new ChatComposerController
        {
            Conversation = conversation,
            Text = "hello",
        };
        using var context = new ChatComposerContext(controller);

        await ((IChatComposerContext)context).SubmitAsync();

        Assert.Single(conversation.Messages);
    }

    [Fact]
    public async Task StopAsync_DelegatesToController()
    {
        using var controller = new ChatComposerController();
        using var context = new ChatComposerContext(controller);

        await ((IChatComposerContext)context).StopAsync();

        Assert.False(controller.CanStop);
    }

    [Fact]
    public async Task Interface_ExposesTaskReturningMethods_NotEventCallback()
    {
        var context = (IChatComposerContext)new ChatComposerContext();
        var submitTask = context.SubmitAsync();
        var stopTask = context.StopAsync();
        var pickTask = context.PickAttachmentsAsync();
        var audioTask = context.ToggleAudioCaptureAsync();
        var speechTask = context.ToggleLiveSpeechAsync();

        // An unconfigured controller treats all actions as safe no-ops.
        Assert.True(submitTask.IsCompletedSuccessfully);
        Assert.True(stopTask.IsCompletedSuccessfully);
        Assert.True(pickTask.IsCompletedSuccessfully);
        Assert.True(audioTask.IsCompletedSuccessfully);
        Assert.True(speechTask.IsCompletedSuccessfully);

        await Task.WhenAll(submitTask, stopTask, pickTask, audioTask, speechTask);
    }

    [Fact]
    public async Task ContextDisposal_DoesNotDisposeExternallySuppliedController()
    {
        var controller = new ChatComposerController();
        using (var context = new ChatComposerContext(controller))
        {
        }

        controller.Text = "still alive";
        Assert.Equal("still alive", controller.Text);
        controller.Dispose();
    }
}
