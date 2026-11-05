using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

/// <summary>
/// The current <see cref="Android.Content.Res.Configuration"/>, updated
/// when the system delivers a configuration change (locale, orientation,
/// dark mode, etc.). Equivalent to Kotlin's
/// <c>androidx.compose.ui.platform.LocalConfiguration</c>.
/// </summary>
public static class LocalConfiguration
{
    static readonly CompositionLocal<Android.Content.Res.Configuration> s_instance =
        new(AndroidCompositionLocals_androidKt.LocalConfiguration);

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalConfiguration.current</c>.
    /// </summary>
    public static Android.Content.Res.Configuration Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public static ProvidedValue Provides(Android.Content.Res.Configuration value) =>
        s_instance.Provides(value);
}
