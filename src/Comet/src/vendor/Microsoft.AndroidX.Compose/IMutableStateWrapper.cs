using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Non-generic view onto a <see cref="MutableState{T}"/> /
/// <see cref="MutableNumberState{T}"/> wrapper. Exists so
/// <see cref="ComposeExtensions.RememberSaveable{T}(Func{T}, int, string)"/>
/// can detect "<c>T</c> is a state-holder wrapper" with a cheap
/// <c>is</c>-check (no reflection, trim-safe), and so the dispatcher
/// can swap the wrapper's underlying <see cref="IMutableState"/>
/// after Compose's <c>rememberSaveable</c> hands back a (possibly
/// restored) state on first composition.
/// </summary>
internal interface IMutableStateWrapper
{
    IMutableState State { get; set; }
}
