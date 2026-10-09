using System.Runtime.CompilerServices;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Static factories mirroring Kotlin's
/// <c>androidx.compose.material3.SliderDefaults</c> singleton — builds
/// a <see cref="SliderColors"/> with one or more slot overrides. Surfaced
/// as an <see cref="IComposer"/> extension so call sites read
/// <c>composer.SliderColors(thumbColor: ...)</c>.
///
/// Useful for overriding a subset of the ten color slots (<c>thumbColor</c>,
/// <c>activeTrackColor</c>, <c>activeTickColor</c>, <c>inactiveTrackColor</c>,
/// <c>inactiveTickColor</c>, plus the four disabled siblings) without
/// hand-rolling a full <see cref="SliderColors"/>. Any slot left
/// <c>null</c> falls back to the value
/// <c>SliderDefaults.colors()</c> would return at the current
/// <c>MaterialTheme</c>.
/// </summary>
public static partial class ComposeExtensions
{
    /// <summary>
    /// Mirrors Kotlin's
    /// <c>SliderDefaults.colors(thumbColor = …, activeTrackColor = …, …)</c>.
    ///
    /// The Compose lowering takes a packed <c>Color</c> (a <c>long</c>)
    /// per slot plus a <c>$default</c> bitmask flagging which slots the
    /// caller actually supplied. Each non-<c>null</c> argument here
    /// clears the corresponding bit so Compose adopts your color;
    /// <c>null</c> leaves the bit set so Kotlin's default (the theme's
    /// color-scheme slot) wins.
    /// </summary>
    public static SliderColors SliderColors(
        this IComposer composer,
        long? thumbColor = null,
        long? activeTrackColor = null,
        long? activeTickColor = null,
        long? inactiveTrackColor = null,
        long? inactiveTickColor = null,
        long? disabledThumbColor = null,
        long? disabledActiveTrackColor = null,
        long? disabledActiveTickColor = null,
        long? disabledInactiveTrackColor = null,
        long? disabledInactiveTickColor = null,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);

        // Default mask: every bit set = "use Kotlin's default for that
        // slot". Clear a bit when the caller passes a value. The enum
        // is generated from the declarative `[assembly: ComposeDefaults]`
        // entry; member ordering matches the slot order on the bound
        // `SliderDefaults.colors(...)` overload.
        var defaults = SliderColorsDefault.All;
        if (thumbColor                  is not null) defaults &= ~SliderColorsDefault.ThumbColor;
        if (activeTrackColor            is not null) defaults &= ~SliderColorsDefault.ActiveTrackColor;
        if (activeTickColor             is not null) defaults &= ~SliderColorsDefault.ActiveTickColor;
        if (inactiveTrackColor          is not null) defaults &= ~SliderColorsDefault.InactiveTrackColor;
        if (inactiveTickColor           is not null) defaults &= ~SliderColorsDefault.InactiveTickColor;
        if (disabledThumbColor          is not null) defaults &= ~SliderColorsDefault.DisabledThumbColor;
        if (disabledActiveTrackColor    is not null) defaults &= ~SliderColorsDefault.DisabledActiveTrackColor;
        if (disabledActiveTickColor     is not null) defaults &= ~SliderColorsDefault.DisabledActiveTickColor;
        if (disabledInactiveTrackColor  is not null) defaults &= ~SliderColorsDefault.DisabledInactiveTrackColor;
        if (disabledInactiveTickColor   is not null) defaults &= ~SliderColorsDefault.DisabledInactiveTickColor;

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            // Kotlin signature: colors-q0g_0yA(JJJJJJJJJJ;Composer;III) →
            // ten longs, $changed, $changed1, $default. Last trailing
            // int (`_changed1`) is $default per the SliderKt.Slider /
            // ProgressIndicatorKt convention. (Even if the mask is
            // wrong, Color.Unspecified packs to 0L and Compose's color
            // resolver falls back to theme — Kotlin's belt-and-braces
            // default semantics — so unset slots still pick up the
            // current MaterialTheme color.)
            return AndroidX.Compose.Material3.SliderDefaults.Instance.Colors(
                thumbColor                 ?? 0L,
                activeTrackColor           ?? 0L,
                activeTickColor            ?? 0L,
                inactiveTrackColor         ?? 0L,
                inactiveTickColor          ?? 0L,
                disabledThumbColor         ?? 0L,
                disabledActiveTrackColor   ?? 0L,
                disabledActiveTickColor    ?? 0L,
                disabledInactiveTrackColor ?? 0L,
                disabledInactiveTickColor  ?? 0L,
                composer,
                p11:       0,
                _changed:  0,
                _changed1: (int)defaults);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
