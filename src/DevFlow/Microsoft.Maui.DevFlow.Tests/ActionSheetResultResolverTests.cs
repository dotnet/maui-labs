using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Tests for <see cref="ActionSheetResultResolver"/>, the pure helper extracted from
/// <c>AlertManagerSubscription.OnActionSheetRequested</c> so the button-index-to-result mapping
/// - including the case where <c>ActionSheetArguments.Buttons</c> is null, which is what a
/// null-buttons regression here guards against - can be unit tested without an AppKit runtime.
/// </summary>
public class ActionSheetResultResolverTests
{
    [Fact]
    public void Resolve_NullButtons_WithCancelOnly_ReturnsCancelForItsIndex()
    {
        // Regression test: ActionSheetArguments.Buttons is documented as nullable and is
        // genuinely null (not an empty sequence) when the caller passes a null buttons params
        // array. Resolve must not throw (e.g. via an unguarded .Where(...) on a null sequence)
        // and must still resolve the cancel button correctly.
        var result = ActionSheetResultResolver.Resolve(buttons: null, destruction: null, cancel: "Cancel", buttonIndex: 0);

        Assert.Equal("Cancel", result);
    }

    [Fact]
    public void Resolve_NullButtons_NoDestructionOrCancel_OutOfRangeIndex_ReturnsNull()
    {
        var result = ActionSheetResultResolver.Resolve(buttons: null, destruction: null, cancel: null, buttonIndex: 0);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_RegularButtons_ReturnsButtonAtIndex()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One", "Two", "Three" }, destruction: null, cancel: "Cancel", buttonIndex: 1);

        Assert.Equal("Two", result);
    }

    [Fact]
    public void Resolve_DestructionButton_IsOrderedAfterRegularButtons()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One", "Two" }, destruction: "Delete", cancel: "Cancel", buttonIndex: 2);

        Assert.Equal("Delete", result);
    }

    [Fact]
    public void Resolve_CancelButton_IsOrderedAfterDestructionButton()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One" }, destruction: "Delete", cancel: "Cancel", buttonIndex: 2);

        Assert.Equal("Cancel", result);
    }

    [Fact]
    public void Resolve_ButtonsContainsNullEntries_SkipsThem()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One", null!, "Three" }, destruction: null, cancel: null, buttonIndex: 1);

        Assert.Equal("Three", result);
    }

    [Fact]
    public void Resolve_IndexBeyondAllButtons_FallsBackToCancel()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One" }, destruction: null, cancel: "Cancel", buttonIndex: 99);

        Assert.Equal("Cancel", result);
    }

    [Fact]
    public void Resolve_NegativeIndex_FallsBackToCancel()
    {
        var result = ActionSheetResultResolver.Resolve(
            buttons: new[] { "One" }, destruction: null, cancel: "Cancel", buttonIndex: -1);

        Assert.Equal("Cancel", result);
    }
}
