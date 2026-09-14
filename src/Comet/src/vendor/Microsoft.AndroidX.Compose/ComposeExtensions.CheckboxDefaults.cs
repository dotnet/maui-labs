using System.Runtime.CompilerServices;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Static factories mirroring Kotlin's
/// <c>androidx.compose.material3.CheckboxDefaults</c> singleton —
/// returns a <see cref="CheckboxColors"/> configured for a particular
/// override pattern. Surfaced as <see cref="IComposer"/> extensions so
/// call sites read <c>composer.CheckboxColors(checkedColor: ...)</c>.
/// </summary>
public static partial class ComposeExtensions
{
    /// <summary>
    /// Mirrors Kotlin's
    /// <c>CheckboxDefaults.colors(checkedColor = …, uncheckedColor = …,
    /// checkmarkColor = …)</c> for the three slots most consumers want
    /// to override.
    ///
    /// <para>The Kotlin <c>colors(...)</c> factory takes a
    /// <c>$default</c> bitmask, but the C# binder strips it because
    /// every parameter is the <c>@JvmInline value class Color</c>. We
    /// therefore call the parameterless <c>colors(composer, _changed)</c>
    /// to obtain the theme defaults and build the result via the bound
    /// <c>CheckboxColors.copy(...)</c> overload — which is the only
    /// twelve-<c>long</c> entry point that survives the binder strip.</para>
    ///
    /// <para>Each non-<c>null</c> argument substitutes for the
    /// corresponding default; <c>null</c> falls back to the value from
    /// the current <c>MaterialTheme</c>. <paramref name="checkedColor"/>
    /// applies to both the box fill and the box border so the checked
    /// chrome stays internally consistent.</para>
    /// </summary>
    public static CheckboxColors CheckboxColors(
        this IComposer composer,
        long? checkedColor = null,
        long? uncheckedColor = null,
        long? checkmarkColor = null,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            var d = AndroidX.Compose.Material3.CheckboxDefaults.Instance.Colors(composer, 0);
            if (checkedColor is null && uncheckedColor is null && checkmarkColor is null)
                return d;

            return d.Copy(
                checkedCheckmarkColor:            checkmarkColor ?? d.CheckedCheckmarkColor,
                uncheckedCheckmarkColor:          d.UncheckedCheckmarkColor,
                checkedBoxColor:                  checkedColor   ?? d.CheckedBoxColor,
                uncheckedBoxColor:                uncheckedColor ?? d.UncheckedBoxColor,
                disabledCheckedBoxColor:          d.DisabledCheckedBoxColor,
                disabledUncheckedBoxColor:        d.DisabledUncheckedBoxColor,
                disabledIndeterminateBoxColor:    d.DisabledIndeterminateBoxColor,
                checkedBorderColor:               checkedColor   ?? d.CheckedBorderColor,
                uncheckedBorderColor:             uncheckedColor ?? d.UncheckedBorderColor,
                disabledBorderColor:              d.DisabledBorderColor,
                disabledUncheckedBorderColor:     d.DisabledUncheckedBorderColor,
                disabledIndeterminateBorderColor: d.DisabledIndeterminateBorderColor);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
