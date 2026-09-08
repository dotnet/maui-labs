using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

/// <summary>
/// The hosting <see cref="Android.Views.View"/> — usually the
/// <c>AbstractComposeView</c> embedding this composition. Equivalent
/// to Kotlin's <c>androidx.compose.ui.platform.LocalView</c>.
/// </summary>
public static class LocalView
{
    static readonly CompositionLocal<Android.Views.View> s_instance =
        new(AndroidCompositionLocals_androidKt.LocalView);

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalView.current</c>.
    /// </summary>
    public static Android.Views.View Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public static ProvidedValue Provides(Android.Views.View value) =>
        s_instance.Provides(value);
}
