namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Decides whether a DevFlow-invoked <c>NSToolbarItem</c> tap should be reported as
/// successful, used by <c>VisualTreeWalker.TryNativeElementTap</c>. AppKit's
/// <c>NSApplication.SendAction</c> reports whether a responder was *found* for a target/action
/// pair; when the toolbar item carries an explicit target (this project's
/// <c>ToolbarHandler</c> always wires one - see <c>ToolbarHandler.CreateToolbarItem</c>), that
/// target is guaranteed (by the compile-time <c>[Export]</c> contract) to implement the
/// action selector, so the action is genuinely dispatched as soon as <c>SendAction</c> is
/// called, regardless of the boolean it eventually returns. That boolean cannot be trusted as
/// a completion signal while the invoked action is still synchronously unwinding (for example,
/// while it is presenting a dialog), so success is derived from "was there a live, enabled
/// target/action pair to dispatch to" rather than solely from the raw return value. Genuinely
/// missing or disabled targets - no explicit target *and* no responder found, or a disabled
/// item - are still reported as failures. Contains no AppKit dependency so it can be exercised
/// by plain unit tests.
/// </summary>
internal static class ToolbarItemInvocationOutcome
{
    /// <summary>
    /// Returns true when a tap on a toolbar item with a non-null action should be reported as
    /// successfully dispatched.
    /// </summary>
    /// <param name="enabled">The toolbar item's current enabled state.</param>
    /// <param name="hasExplicitTarget">Whether the toolbar item has a non-null <c>Target</c>.</param>
    /// <param name="sendActionDispatched">The boolean returned by <c>NSApplication.SendAction</c>.</param>
    public static bool IsSuccessful(bool enabled, bool hasExplicitTarget, bool sendActionDispatched)
    {
        if (!enabled)
            return false;

        return sendActionDispatched || hasExplicitTarget;
    }
}
