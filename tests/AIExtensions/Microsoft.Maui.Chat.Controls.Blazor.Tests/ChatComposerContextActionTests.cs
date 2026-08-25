// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies the composer-context action contract exposes plain <see cref="Task"/>-returning
/// methods (not <c>EventCallback</c>) and that they are wired through the attached delegates.
/// </summary>
public class ChatComposerContextActionTests
{
    [Fact]
    public async Task SubmitAsync_ForwardsToAttachedDelegate()
    {
        var context = new ChatComposerContext();
        var invoked = false;
        context.AttachActions(
            onSubmit: () => { invoked = true; return Task.CompletedTask; },
            onStop: () => Task.CompletedTask,
            onPickAttachments: () => Task.CompletedTask,
            onToggleAudioCapture: () => Task.CompletedTask,
            onToggleLiveSpeech: () => Task.CompletedTask);

        await ((IChatComposerContext)context).SubmitAsync();

        Assert.True(invoked);
    }

    [Fact]
    public async Task StopAsync_ForwardsToAttachedDelegate()
    {
        var context = new ChatComposerContext();
        var invoked = false;
        context.AttachActions(
            onSubmit: () => Task.CompletedTask,
            onStop: () => { invoked = true; return Task.CompletedTask; },
            onPickAttachments: () => Task.CompletedTask,
            onToggleAudioCapture: () => Task.CompletedTask,
            onToggleLiveSpeech: () => Task.CompletedTask);

        await ((IChatComposerContext)context).StopAsync();

        Assert.True(invoked);
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

        // A no-op action was installed by default so every call returns a completed Task.
        Assert.True(submitTask.IsCompletedSuccessfully);
        Assert.True(stopTask.IsCompletedSuccessfully);
        Assert.True(pickTask.IsCompletedSuccessfully);
        Assert.True(audioTask.IsCompletedSuccessfully);
        Assert.True(speechTask.IsCompletedSuccessfully);

        await Task.WhenAll(submitTask, stopTask, pickTask, audioTask, speechTask);
    }

    [Fact]
    public async Task ActionsAreReInvokable()
    {
        var context = new ChatComposerContext();
        var count = 0;
        context.AttachActions(
            onSubmit: () => { count++; return Task.CompletedTask; },
            onStop: () => Task.CompletedTask,
            onPickAttachments: () => Task.CompletedTask,
            onToggleAudioCapture: () => Task.CompletedTask,
            onToggleLiveSpeech: () => Task.CompletedTask);

        await ((IChatComposerContext)context).SubmitAsync();
        await ((IChatComposerContext)context).SubmitAsync();
        await ((IChatComposerContext)context).SubmitAsync();

        Assert.Equal(3, count);
    }
}
