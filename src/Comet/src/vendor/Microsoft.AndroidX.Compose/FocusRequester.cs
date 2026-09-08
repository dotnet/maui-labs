using JFocusRequester = AndroidX.Compose.UI.Focus.FocusRequester;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.focus.FocusRequester</c>.
/// Pair it with <see cref="Modifier.FocusRequester(FocusRequester)"/>
/// to install the requester on a focusable node, then call
/// <see cref="RequestFocus"/> (e.g. from a button click) to programmatically
/// move focus there.
///
/// <para>
/// Holds a Compose <c>FocusRequester</c> instance. The instance must
/// outlive the composition that wired it — store one
/// <see cref="FocusRequester"/> per logical focus target on the activity
/// (or on a state holder) and reuse the same instance across renders.
/// Constructing a new instance per recomposition would silently drop
/// any pending request.
/// </para>
/// </summary>
public sealed class FocusRequester
{
    internal JFocusRequester Java { get; }

    /// <summary>
    /// Construct a new focus requester. Allocates a fresh Compose
    /// <c>FocusRequester</c> instance.
    /// </summary>
    public FocusRequester() => Java = new JFocusRequester();

    /// <summary>
    /// Equivalent to Kotlin's no-arg <c>focusRequester.requestFocus()</c> —
    /// asks Compose to move focus to the node currently wearing this
    /// requester. Safe to call from event handlers (button clicks,
    /// LaunchedEffect bodies, etc.). The .NET binding does not surface
    /// the no-arg overload, so we call it directly via JNI.
    /// </summary>
    public void RequestFocus() =>
        ComposeBridges.FocusRequesterRequestFocus(((Java.Lang.Object)Java).Handle);

    /// <summary>
    /// Captures focus on the node — input is pinned here until
    /// <see cref="FreeFocus"/> is called. Returns <c>true</c> if the
    /// capture succeeded.
    /// </summary>
    public bool CaptureFocus() => Java.CaptureFocus();

    /// <summary>
    /// Releases a previously-<see cref="CaptureFocus">captured</see>
    /// focus. Returns <c>true</c> if focus was released.
    /// </summary>
    public bool FreeFocus() => Java.FreeFocus();
}
