using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

/// <summary>
/// The Android <see cref="Android.Content.Context"/> attached to the host
/// <c>ComposeView</c>. Equivalent to Kotlin's
/// <c>androidx.compose.ui.platform.LocalContext</c>.
///
/// <para>Read inside a <c>Render</c> method via
/// <see cref="Current(IComposer)"/>; install a different value for a
/// subtree by passing <see cref="Provides(Android.Content.Context)"/>
/// to a <see cref="CompositionLocalProvider"/>.</para>
/// </summary>
public static class LocalContext
{
    static readonly CompositionLocal<Android.Content.Context> s_instance =
        new(AndroidCompositionLocals_androidKt.LocalContext);

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalContext.current</c>. Pass the <see cref="IComposer"/>
    /// handed to the enclosing <c>Render</c> method.
    /// </summary>
    public static Android.Content.Context Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>. Equivalent to
    /// Kotlin's infix <c>LocalContext provides value</c> syntax.
    /// </summary>
    public static ProvidedValue Provides(Android.Content.Context value) =>
        s_instance.Provides(value);
}
