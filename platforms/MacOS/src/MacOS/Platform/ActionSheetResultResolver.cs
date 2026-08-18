namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Resolves which action sheet button string corresponds to the NSAlert button index the user
/// picked, matching the order buttons were added to the alert in
/// <c>AlertManagerSubscription.OnActionSheetRequested</c> (regular buttons, then the
/// destructive button if any, then the cancel button if any). Extracted as a small, AppKit-free
/// helper so this mapping - including the "no destructive/cancel button, unexpected index, or a
/// null <c>buttons</c> sequence" edge cases - can be unit tested directly, without requiring an
/// AppKit runtime. <c>Controls.Internals.ActionSheetArguments.Buttons</c> is documented as
/// nullable (a null <c>params</c> array from the caller flows straight through as a null
/// sequence rather than an empty one), so <paramref name="buttons"/> must be treated as
/// optional here.
/// </summary>
internal static class ActionSheetResultResolver
{
    public static string? Resolve(IEnumerable<string>? buttons, string? destruction, string? cancel, int buttonIndex)
    {
        var allButtons = (buttons ?? Enumerable.Empty<string>())
            .Where(button => button != null)
            .ToList();

        if (destruction != null)
            allButtons.Add(destruction);

        if (cancel != null)
            allButtons.Add(cancel);

        return buttonIndex >= 0 && buttonIndex < allButtons.Count
            ? allButtons[buttonIndex]
            : cancel;
    }
}
