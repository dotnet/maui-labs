namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="SnackbarHost"/>. Wraps
/// the bound <c>androidx.compose.material3.SnackbarHostState</c> — the
/// Kotlin class has a public no-arg constructor and is plain enough
/// for the binder to expose, so no JNI bridge is needed.
/// </summary>
/// <remarks>
/// The Kotlin <c>showSnackbar</c> trigger is a suspending function
/// (takes a <c>Continuation</c>) and isn't directly callable from C#
/// without a coroutine bridge. For now, prefer toggling a
/// <see cref="MutableState{T}"/> + rendering a <see cref="Snackbar"/>
/// conditionally inside the <see cref="Scaffold.SnackbarHost"/> slot.
/// This wrapper exists so the bound state can still be passed to a
/// <see cref="SnackbarHost"/> when one is needed.
/// </remarks>
public sealed class SnackbarHostState
{
    internal AndroidX.Compose.Material3.SnackbarHostState Jvm { get; } = new();
}
