using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// The Material 3 <see cref="ColorScheme"/> installed by the nearest
/// <see cref="MaterialTheme"/>. Equivalent to Kotlin's
/// <c>androidx.compose.material3.LocalColorScheme</c>.
///
/// <para>Reading this directly is rarely necessary — most consumers go
/// through <see cref="MaterialTheme"/>'s own accessors — but it's
/// exposed for parity with the Kotlin API and for callers that want to
/// override the scheme inside a subtree without redefining the whole
/// theme.</para>
/// </summary>
public static class LocalColorScheme
{
    static readonly CompositionLocal<ColorScheme> s_instance =
        new(ColorSchemeKt.LocalColorScheme);

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalColorScheme.current</c>.
    /// </summary>
    public static ColorScheme Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public static ProvidedValue Provides(ColorScheme value) =>
        s_instance.Provides(value);
}
