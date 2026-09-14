using AndroidX.Compose.UI.Focus;

namespace AndroidX.Compose;

/// <summary>
/// Immutable snapshot of a Compose <c>FocusState</c>. Produced inside
/// the callback passed to
/// <see cref="Modifier.OnFocusChanged(Action{FocusState})"/>
/// — Compose reuses one <c>FocusState</c> JNI instance per node and
/// mutates it across recompositions, so we read all three booleans
/// once and hand back a value type the caller can keep without worrying
/// about peer lifetime.
/// </summary>
public readonly struct FocusState
{
    /// <summary>
    /// <c>true</c> if the node owning the modifier currently has focus.
    /// </summary>
    public bool IsFocused { get; }

    /// <summary>
    /// <c>true</c> if focus is held by this node or any of its
    /// descendants (mirrors Kotlin's <c>FocusState.hasFocus</c>).
    /// </summary>
    public bool HasFocus { get; }

    /// <summary>
    /// <c>true</c> if focus was captured via
    /// <c>FocusRequester.captureFocus()</c> — keystrokes are pinned
    /// to this node until <c>freeFocus()</c> is called.
    /// </summary>
    public bool IsCaptured { get; }

    /// <summary>
    /// Construct a snapshot directly. Public so test code can build
    /// fixtures without going through JNI.
    /// </summary>
    public FocusState(bool isFocused, bool hasFocus, bool isCaptured)
    {
        IsFocused = isFocused;
        HasFocus = hasFocus;
        IsCaptured = isCaptured;
    }

    internal static FocusState From(IFocusState state) =>
        new(state.IsFocused, state.HasFocus, state.IsCaptured);
}
