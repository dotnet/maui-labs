using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

/// <summary>
/// The current <see cref="Android.Content.Res.Resources"/>, already
/// configuration-aware (re-read on configuration change). Equivalent
/// to Kotlin's <c>androidx.compose.ui.platform.LocalResources</c>.
/// </summary>
public static class LocalResources
{
    static readonly CompositionLocal<Android.Content.Res.Resources> s_instance =
        new(AndroidCompositionLocals_androidKt.LocalResources);

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalResources.current</c>.
    /// </summary>
    public static Android.Content.Res.Resources Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public static ProvidedValue Provides(Android.Content.Res.Resources value) =>
        s_instance.Provides(value);
}
