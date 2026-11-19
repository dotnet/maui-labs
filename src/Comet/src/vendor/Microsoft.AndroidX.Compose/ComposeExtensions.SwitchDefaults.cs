using System.Runtime.CompilerServices;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Static factory mirroring Kotlin's
/// <c>androidx.compose.material3.SwitchDefaults.colors(...)</c>. Returns
/// a <see cref="SwitchColors"/> overriding only the slots the caller
/// supplies; the rest fall back to the current <c>MaterialTheme</c>.
/// </summary>
public static partial class ComposeExtensions
{
    /// <summary>
    /// Build a <see cref="SwitchColors"/> with the four "common"
    /// override slots — thumb / track for both the checked and
    /// unchecked states — wired to the bound parameterised
    /// <c>SwitchDefaults.colors(c1..c16, composer, $default, $changed, $changed1)</c>
    /// overload.
    ///
    /// <para>Every slot left <c>null</c> stays "use Kotlin's default";
    /// the corresponding bit in the <c>$default</c> bitmask stays set
    /// and Kotlin substitutes the theme's <c>colorScheme</c> entry. The
    /// 16 slot order matches the bytecode lowering of the
    /// <c>@JvmInline value class Color</c> parameters: thumb / track /
    /// border / icon — checked, unchecked, disabled-checked,
    /// disabled-unchecked.</para>
    /// </summary>
    public static SwitchColors SwitchColors(
        this IComposer composer,
        long? checkedThumbColor   = null,
        long? checkedTrackColor   = null,
        long? uncheckedThumbColor = null,
        long? uncheckedTrackColor = null,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);

        // Default mask: every bit set = "use Kotlin's default for that
        // slot". Clear a bit when the caller passes a value. The enum
        // is generated from the declarative `[assembly: ComposeDefaults]`
        // entry; member ordering matches the slot order on the bound
        // `colors(...)` overload.
        var defaults = SwitchColorsDefault.All;
        if (checkedThumbColor   is not null) defaults &= ~SwitchColorsDefault.CheckedThumbColor;
        if (checkedTrackColor   is not null) defaults &= ~SwitchColorsDefault.CheckedTrackColor;
        if (uncheckedThumbColor is not null) defaults &= ~SwitchColorsDefault.UncheckedThumbColor;
        if (uncheckedTrackColor is not null) defaults &= ~SwitchColorsDefault.UncheckedTrackColor;

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            return AndroidX.Compose.Material3.SwitchDefaults.Instance.Colors(
                checkedThumbColor:            checkedThumbColor   ?? 0L,
                checkedTrackColor:            checkedTrackColor   ?? 0L,
                checkedBorderColor:           0L,
                checkedIconColor:             0L,
                uncheckedThumbColor:          uncheckedThumbColor ?? 0L,
                uncheckedTrackColor:          uncheckedTrackColor ?? 0L,
                uncheckedBorderColor:         0L,
                uncheckedIconColor:           0L,
                disabledCheckedThumbColor:    0L,
                disabledCheckedTrackColor:    0L,
                disabledCheckedBorderColor:   0L,
                disabledCheckedIconColor:     0L,
                disabledUncheckedThumbColor:  0L,
                disabledUncheckedTrackColor:  0L,
                disabledUncheckedBorderColor: 0L,
                disabledUncheckedIconColor:   0L,
                _composer: composer,
                p17:       (int)defaults,
                _changed:  0,
                _changed1: 0);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
