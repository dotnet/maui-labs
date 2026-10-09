using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Tests for <see cref="ToolbarItemInvocationOutcome"/>, the decision logic
/// <c>VisualTreeWalker.TryNativeElementTap</c> (MACOS) uses to decide whether invoking a
/// registered <c>NSToolbarItem</c> should be reported as successful. Guards the fix for a
/// blocking-alert regression where a live, explicitly-targeted toolbar item's action was
/// genuinely dispatched but reported <c>success:false</c> because AppKit's
/// <c>NSApplication.SendAction</c> return value could not be observed until the action's
/// synchronous work (e.g. presenting a dialog) finished unwinding. The helper has no AppKit
/// dependency, so it is compiled directly into this test project (see the Compile Include in
/// the .csproj) and exercised without needing an AppKit/macOS runtime.
/// </summary>
public class ToolbarItemInvocationOutcomeTests
{
    [Fact]
    public void ExplicitTarget_ReportsSuccess_EvenWhenSendActionReturnsFalse()
    {
        // The dispatch is guaranteed by the explicit target/action pair our own ToolbarHandler
        // always wires up (see ToolbarHandler.CreateToolbarItem), so success must not depend
        // on SendAction's boolean, which can't be trusted while the dispatched action is still
        // synchronously unwinding.
        Assert.True(ToolbarItemInvocationOutcome.IsSuccessful(
            enabled: true,
            hasExplicitTarget: true,
            sendActionDispatched: false));
    }

    [Fact]
    public void ExplicitTarget_ReportsSuccess_WhenSendActionReturnsTrue()
    {
        Assert.True(ToolbarItemInvocationOutcome.IsSuccessful(
            enabled: true,
            hasExplicitTarget: true,
            sendActionDispatched: true));
    }

    [Fact]
    public void NoExplicitTarget_ReliesOnSendActionResult_WhenDispatched()
    {
        // Without an explicit target, AppKit resolves the responder via the chain, so
        // SendAction's return value is the only signal available.
        Assert.True(ToolbarItemInvocationOutcome.IsSuccessful(
            enabled: true,
            hasExplicitTarget: false,
            sendActionDispatched: true));
    }

    [Fact]
    public void NoExplicitTarget_ReportsFailure_WhenSendActionDidNotDispatch()
    {
        Assert.False(ToolbarItemInvocationOutcome.IsSuccessful(
            enabled: true,
            hasExplicitTarget: false,
            sendActionDispatched: false));
    }

    [Fact]
    public void DisabledItem_ReportsFailure_RegardlessOfTargetOrDispatchResult()
    {
        Assert.False(ToolbarItemInvocationOutcome.IsSuccessful(
            enabled: false,
            hasExplicitTarget: true,
            sendActionDispatched: true));
    }
}
