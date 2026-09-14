using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;
using BindingMaterialTheme = AndroidX.Compose.Material3.MaterialTheme;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// Read the active <see cref="ColorScheme"/> from the current
    /// <see cref="MaterialTheme"/> ancestor. Mirror of Kotlin's
    /// <c>MaterialTheme.colorScheme</c>. Must be called from inside a
    /// composable's <c>Render</c> body.
    /// </summary>
    public static ColorScheme ColorScheme(this IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return BindingMaterialTheme.Instance.GetColorScheme(composer, 0);
    }

    /// <summary>
    /// Read the active <see cref="Typography"/> from the current
    /// <see cref="MaterialTheme"/> ancestor. Mirror of Kotlin's
    /// <c>MaterialTheme.typography</c>. Must be called from inside a
    /// composable's <c>Render</c> body.
    /// </summary>
    public static Typography Typography(this IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return BindingMaterialTheme.Instance.GetTypography(composer, 0);
    }

    /// <summary>
    /// Read the active <see cref="Shapes"/> from the current
    /// <see cref="MaterialTheme"/> ancestor. Mirror of Kotlin's
    /// <c>MaterialTheme.shapes</c>. Must be called from inside a
    /// composable's <c>Render</c> body.
    /// </summary>
    public static Shapes Shapes(this IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return BindingMaterialTheme.Instance.GetShapes(composer, 0);
    }
}
