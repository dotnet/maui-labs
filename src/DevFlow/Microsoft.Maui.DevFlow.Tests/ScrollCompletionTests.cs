using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.DevFlow.Tests;

public class ScrollCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrollViewToAsync_UnchangedOffset_DoesNotRequestNativeCompletion(bool animated)
    {
        var scroll = new ScrollView();
        scroll.SetScrolledPosition(0, 40);
        var requests = 0;
        scroll.ScrollToRequested += (_, _) => requests++;

        var task = MauiDevFlowAgentService.ScrollViewToAsync(scroll, 0, 40, animated);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ScrollViewToAsync_AlreadyVisibleElement_CompletesWithoutNativeScroll()
    {
        var target = new Label { Frame = new Rect(0, 20, 100, 20) };
        var scroll = new ScrollView { Content = target, Frame = new Rect(0, 0, 100, 100) };
        var requests = 0;
        scroll.ScrollToRequested += (_, _) => requests++;
        var point = scroll.GetScrollPositionForElement(target, ScrollToPosition.MakeVisible);

        await MauiDevFlowAgentService.ScrollViewToAsync(scroll, point.X, point.Y, false);

        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ScrollViewToAsync_ChangedOffset_DispatchesAndAwaitsCompletion()
    {
        var scroll = new ScrollView();
        var requests = 0;
        scroll.ScrollToRequested += (_, request) =>
        {
            Assert.Equal(100, request.ScrollY);
            Assert.False(request.ShouldAnimate);
            requests++;
            scroll.SendScrollFinished();
        };

        await MauiDevFlowAgentService.ScrollViewToAsync(scroll, 0, 100, false);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ScrollViewToAsync_NativeRequestFails_PropagatesFailure()
    {
        var scroll = new ScrollView();
        scroll.ScrollToRequested += (_, _) => throw new InvalidOperationException("Native scroll failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MauiDevFlowAgentService.ScrollViewToAsync(scroll, 0, 100, false));

        Assert.Equal("Native scroll failed", exception.Message);
    }
}
