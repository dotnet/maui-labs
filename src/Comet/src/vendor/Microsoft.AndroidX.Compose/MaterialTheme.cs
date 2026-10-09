using Android.OS;
using AndroidX.Compose.Foundation;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>MaterialTheme</c> root. Supplies the active
/// <see cref="ColorScheme"/>, <see cref="Typography"/>, and
/// <see cref="Shapes"/> to every child composable.
/// </summary>
/// <remarks>
/// <para>
/// With no properties set, <c>MaterialTheme</c> picks an Android 12+
/// dynamic (Material You) light or dark color scheme depending on
/// <see cref="IsSystemInDarkTheme(IComposer)"/>, falling back to the
/// static M3 baseline palette on API levels &lt; 31.
/// </para>
/// <para>
/// To override a slice of the theme, set <see cref="ColorScheme"/>,
/// <see cref="Typography"/>, or <see cref="Shapes"/> directly — the
/// matching static factory methods build the scheme:
/// </para>
/// <code>
/// new MaterialTheme {
///     ColorScheme     = MaterialTheme.DarkColorScheme(),
///     UseDynamicColor = false,
///     new Text("Hello"),
/// }
/// </code>
/// <para>
/// Child composables can read the active theme via
/// <see cref="ComposeExtensions.ColorScheme(IComposer)"/> /
/// <see cref="ComposeExtensions.Typography(IComposer)"/> /
/// <see cref="ComposeExtensions.Shapes(IComposer)"/>.
/// </para>
/// </remarks>
public sealed class MaterialTheme : ComposableContainer
{
    /// <summary>
    /// Optional explicit color scheme. When <see langword="null"/> (the
    /// default), the theme picks one based on <see cref="UseDynamicColor"/>
    /// and <see cref="Dark"/>.
    /// </summary>
    public ColorScheme? ColorScheme { get; set; }

    /// <summary>
    /// Optional explicit typography. When <see langword="null"/> (the
    /// default), the Compose-provided M3 baseline typography is used.
    /// </summary>
    public Typography? Typography { get; set; }

    /// <summary>
    /// Optional explicit shape set. When <see langword="null"/> (the
    /// default), the Compose-provided M3 baseline shapes are used.
    /// </summary>
    public Shapes? Shapes { get; set; }

    /// <summary>
    /// Force the theme into dark or light mode. When <see langword="null"/>
    /// (the default), the theme follows
    /// <see cref="IsSystemInDarkTheme(IComposer)"/>.
    /// </summary>
    public bool? Dark { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default) and running on Android
    /// 12 or later, use Material You dynamic color (derived from the
    /// system wallpaper). Has no effect when <see cref="ColorScheme"/>
    /// is set, or when running on API levels &lt; 31.
    /// </summary>
    public bool UseDynamicColor { get; set; } = true;

    public override void Render(IComposer composer)
    {
        bool dark = Dark ?? DarkThemeKt.IsSystemInDarkTheme(composer, 0);

        ColorScheme scheme = ColorScheme ?? ResolveDefaultScheme(composer, dark);

        // MaterialThemeDefault is generated from `[ComposeDefaults<MaterialThemeKt>]`
        // and contains a bit per optional Kotlin parameter (ColorScheme,
        // Shapes, Typography). Content is always provided. Start with
        // every bit set, then clear the bits for slots we actually
        // supply.
        var defaults = MaterialThemeDefault.All & ~MaterialThemeDefault.ColorScheme;
        if (Shapes is not null)     defaults &= ~MaterialThemeDefault.Shapes;
        if (Typography is not null) defaults &= ~MaterialThemeDefault.Typography;

        var content = ComposableLambdas.Wrap2(composer, c => RenderChildren(c));
        MaterialThemeKt.MaterialTheme(
            colorScheme: scheme,
            shapes:      Shapes,
            typography:  Typography,
            content:     content,
            _composer:   composer,
            p5:          0,
            _changed:    (int)defaults);
    }

    ColorScheme ResolveDefaultScheme(IComposer composer, bool dark)
    {
        if (UseDynamicColor && Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            // Read the composition-scoped Android context rather than
            // Android.App.Application.Context, so the theme reflects any
            // override installed by an enclosing CompositionLocalProvider
            // (e.g. a contextual content-wrapper) and updates correctly on
            // activity recreations.
            var ctx = LocalContext.Current(composer);
            return dark
                ? DynamicTonalPaletteKt.DynamicDarkColorScheme(ctx)
                : DynamicTonalPaletteKt.DynamicLightColorScheme(ctx);
        }
        return dark
            ? ComposeBridges.DefaultDarkColorScheme()
            : ComposeBridges.DefaultLightColorScheme();
    }

    /// <summary>
    /// Build the Material 3 baseline light <see cref="ColorScheme"/> —
    /// the same palette Kotlin's <c>lightColorScheme()</c> returns when
    /// every parameter is left at its default.
    /// </summary>
    public static ColorScheme LightColorScheme() => ComposeBridges.DefaultLightColorScheme();

    /// <summary>
    /// Build the Material 3 baseline dark <see cref="ColorScheme"/> —
    /// the same palette Kotlin's <c>darkColorScheme()</c> returns when
    /// every parameter is left at its default.
    /// </summary>
    public static ColorScheme DarkColorScheme() => ComposeBridges.DefaultDarkColorScheme();

    /// <summary>
    /// Build a custom Material 3 light <see cref="ColorScheme"/> with
    /// the supplied per-slot color overrides. Any parameter left at
    /// <see langword="null"/> falls back to the corresponding M3 tonal
    /// palette token default.
    /// </summary>
    /// <remarks>
    /// Mirror of Kotlin's
    /// <c>lightColorScheme(primary = …, onPrimary = …, …)</c> builder.
    /// </remarks>
    public static ColorScheme LightColorScheme(
        Color? primary = null, Color? onPrimary = null,
        Color? primaryContainer = null, Color? onPrimaryContainer = null,
        Color? inversePrimary = null,
        Color? secondary = null, Color? onSecondary = null,
        Color? secondaryContainer = null, Color? onSecondaryContainer = null,
        Color? tertiary = null, Color? onTertiary = null,
        Color? tertiaryContainer = null, Color? onTertiaryContainer = null,
        Color? background = null, Color? onBackground = null,
        Color? surface = null, Color? onSurface = null,
        Color? surfaceVariant = null, Color? onSurfaceVariant = null,
        Color? surfaceTint = null,
        Color? inverseSurface = null, Color? inverseOnSurface = null,
        Color? error = null, Color? onError = null,
        Color? errorContainer = null, Color? onErrorContainer = null,
        Color? outline = null, Color? outlineVariant = null,
        Color? scrim = null,
        Color? surfaceBright = null,
        Color? surfaceContainer = null, Color? surfaceContainerHigh = null,
        Color? surfaceContainerHighest = null, Color? surfaceContainerLow = null,
        Color? surfaceContainerLowest = null,
        Color? surfaceDim = null,
        Color? primaryFixed = null, Color? primaryFixedDim = null,
        Color? onPrimaryFixed = null, Color? onPrimaryFixedVariant = null,
        Color? secondaryFixed = null, Color? secondaryFixedDim = null,
        Color? onSecondaryFixed = null, Color? onSecondaryFixedVariant = null,
        Color? tertiaryFixed = null, Color? tertiaryFixedDim = null,
        Color? onTertiaryFixed = null, Color? onTertiaryFixedVariant = null) =>
        BuildColorScheme(light: true,
            primary, onPrimary, primaryContainer, onPrimaryContainer, inversePrimary,
            secondary, onSecondary, secondaryContainer, onSecondaryContainer,
            tertiary, onTertiary, tertiaryContainer, onTertiaryContainer,
            background, onBackground, surface, onSurface, surfaceVariant, onSurfaceVariant,
            surfaceTint, inverseSurface, inverseOnSurface,
            error, onError, errorContainer, onErrorContainer,
            outline, outlineVariant, scrim,
            surfaceBright, surfaceContainer, surfaceContainerHigh, surfaceContainerHighest,
            surfaceContainerLow, surfaceContainerLowest, surfaceDim,
            primaryFixed, primaryFixedDim, onPrimaryFixed, onPrimaryFixedVariant,
            secondaryFixed, secondaryFixedDim, onSecondaryFixed, onSecondaryFixedVariant,
            tertiaryFixed, tertiaryFixedDim, onTertiaryFixed, onTertiaryFixedVariant);

    /// <summary>
    /// Build a custom Material 3 dark <see cref="ColorScheme"/> with
    /// the supplied per-slot color overrides. Any parameter left at
    /// <see langword="null"/> falls back to the corresponding M3 tonal
    /// palette token default.
    /// </summary>
    /// <remarks>
    /// Mirror of Kotlin's
    /// <c>darkColorScheme(primary = …, onPrimary = …, …)</c> builder.
    /// </remarks>
    public static ColorScheme DarkColorScheme(
        Color? primary = null, Color? onPrimary = null,
        Color? primaryContainer = null, Color? onPrimaryContainer = null,
        Color? inversePrimary = null,
        Color? secondary = null, Color? onSecondary = null,
        Color? secondaryContainer = null, Color? onSecondaryContainer = null,
        Color? tertiary = null, Color? onTertiary = null,
        Color? tertiaryContainer = null, Color? onTertiaryContainer = null,
        Color? background = null, Color? onBackground = null,
        Color? surface = null, Color? onSurface = null,
        Color? surfaceVariant = null, Color? onSurfaceVariant = null,
        Color? surfaceTint = null,
        Color? inverseSurface = null, Color? inverseOnSurface = null,
        Color? error = null, Color? onError = null,
        Color? errorContainer = null, Color? onErrorContainer = null,
        Color? outline = null, Color? outlineVariant = null,
        Color? scrim = null,
        Color? surfaceBright = null,
        Color? surfaceContainer = null, Color? surfaceContainerHigh = null,
        Color? surfaceContainerHighest = null, Color? surfaceContainerLow = null,
        Color? surfaceContainerLowest = null,
        Color? surfaceDim = null,
        Color? primaryFixed = null, Color? primaryFixedDim = null,
        Color? onPrimaryFixed = null, Color? onPrimaryFixedVariant = null,
        Color? secondaryFixed = null, Color? secondaryFixedDim = null,
        Color? onSecondaryFixed = null, Color? onSecondaryFixedVariant = null,
        Color? tertiaryFixed = null, Color? tertiaryFixedDim = null,
        Color? onTertiaryFixed = null, Color? onTertiaryFixedVariant = null) =>
        BuildColorScheme(light: false,
            primary, onPrimary, primaryContainer, onPrimaryContainer, inversePrimary,
            secondary, onSecondary, secondaryContainer, onSecondaryContainer,
            tertiary, onTertiary, tertiaryContainer, onTertiaryContainer,
            background, onBackground, surface, onSurface, surfaceVariant, onSurfaceVariant,
            surfaceTint, inverseSurface, inverseOnSurface,
            error, onError, errorContainer, onErrorContainer,
            outline, outlineVariant, scrim,
            surfaceBright, surfaceContainer, surfaceContainerHigh, surfaceContainerHighest,
            surfaceContainerLow, surfaceContainerLowest, surfaceDim,
            primaryFixed, primaryFixedDim, onPrimaryFixed, onPrimaryFixedVariant,
            secondaryFixed, secondaryFixedDim, onSecondaryFixed, onSecondaryFixedVariant,
            tertiaryFixed, tertiaryFixedDim, onTertiaryFixed, onTertiaryFixedVariant);

    static ColorScheme BuildColorScheme(bool light,
        Color? primary, Color? onPrimary, Color? primaryContainer, Color? onPrimaryContainer, Color? inversePrimary,
        Color? secondary, Color? onSecondary, Color? secondaryContainer, Color? onSecondaryContainer,
        Color? tertiary, Color? onTertiary, Color? tertiaryContainer, Color? onTertiaryContainer,
        Color? background, Color? onBackground, Color? surface, Color? onSurface, Color? surfaceVariant, Color? onSurfaceVariant,
        Color? surfaceTint, Color? inverseSurface, Color? inverseOnSurface,
        Color? error, Color? onError, Color? errorContainer, Color? onErrorContainer,
        Color? outline, Color? outlineVariant, Color? scrim,
        Color? surfaceBright, Color? surfaceContainer, Color? surfaceContainerHigh, Color? surfaceContainerHighest,
        Color? surfaceContainerLow, Color? surfaceContainerLowest, Color? surfaceDim,
        Color? primaryFixed, Color? primaryFixedDim, Color? onPrimaryFixed, Color? onPrimaryFixedVariant,
        Color? secondaryFixed, Color? secondaryFixedDim, Color? onSecondaryFixed, Color? onSecondaryFixedVariant,
        Color? tertiaryFixed, Color? tertiaryFixedDim, Color? onTertiaryFixed, Color? onTertiaryFixedVariant)
    {
        // Slot order matches Kotlin's lightColorScheme/darkColorScheme
        // parameter list and the ColorSchemeDefault enum declared in
        // ComposeDefaults.cs.
        Color?[] slots = new Color?[]
        {
            primary, onPrimary, primaryContainer, onPrimaryContainer, inversePrimary,
            secondary, onSecondary, secondaryContainer, onSecondaryContainer,
            tertiary, onTertiary, tertiaryContainer, onTertiaryContainer,
            background, onBackground, surface, onSurface, surfaceVariant, onSurfaceVariant,
            surfaceTint, inverseSurface, inverseOnSurface,
            error, onError, errorContainer, onErrorContainer,
            outline, outlineVariant, scrim,
            surfaceBright, surfaceContainer, surfaceContainerHigh, surfaceContainerHighest,
            surfaceContainerLow, surfaceContainerLowest, surfaceDim,
            primaryFixed, primaryFixedDim, onPrimaryFixed, onPrimaryFixedVariant,
            secondaryFixed, secondaryFixedDim, onSecondaryFixed, onSecondaryFixedVariant,
            tertiaryFixed, tertiaryFixedDim, onTertiaryFixed, onTertiaryFixedVariant,
        };

        // ColorSchemeDefault is a long-backed [Flags] enum (48 slots
        // exceeds the 31-bit int boundary), generated by
        // ComposeDefaultsGenerator from the declarative attribute in
        // ComposeDefaults.cs. Split() turns the typed mask into the
        // (mask0, mask1) pair Kotlin's pair-of-int $default trampoline
        // expects.
        long[] colors = new long[slots.Length];
        var defaults = ColorSchemeDefault.None;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is Color c)
                colors[i] = c;
            else
                defaults |= (ColorSchemeDefault)(1L << i);
        }

        var (mask0, mask1) = defaults.Split();
        return light
            ? ComposeBridges.CustomLightColorScheme(colors, mask0, mask1)
            : ComposeBridges.CustomDarkColorScheme(colors, mask0, mask1);
    }

    /// <summary>
    /// Build a dynamic (Material You) light color scheme from the
    /// supplied context, or
    /// <see cref="Application.Context"/> when <paramref name="context"/>
    /// is <see langword="null"/>. Requires Android 12 (API 31) or later.
    /// </summary>
    public static ColorScheme DynamicLightColorScheme(Android.Content.Context? context = null) =>
        DynamicTonalPaletteKt.DynamicLightColorScheme(context ?? Android.App.Application.Context);

    /// <summary>
    /// Build a dynamic (Material You) dark color scheme from the
    /// supplied context, or
    /// <see cref="Application.Context"/> when <paramref name="context"/>
    /// is <see langword="null"/>. Requires Android 12 (API 31) or later.
    /// </summary>
    public static ColorScheme DynamicDarkColorScheme(Android.Content.Context? context = null) =>
        DynamicTonalPaletteKt.DynamicDarkColorScheme(context ?? Android.App.Application.Context);

    /// <summary>
    /// Build a Material 3 <see cref="Shapes"/> with per-slot
    /// overrides. Any parameter left at <see langword="null"/> falls
    /// back to the corresponding M3 baseline shape default.
    /// </summary>
    /// <remarks>
    /// Mirror of Kotlin's
    /// <c>Shapes(extraSmall = …, small = …, medium = …, large = …, extraLarge = …)</c>
    /// constructor. Use <see cref="Shape.RoundedCorners(Dp)"/> /
    /// <see cref="Shape.RoundedPercent(int)"/> /
    /// <see cref="Shape.CutCorners(Dp)"/> to build the per-slot
    /// values.
    /// </remarks>
    public static Shapes BuildShapes(
        Shape? extraSmall = null,
        Shape? small = null,
        Shape? medium = null,
        Shape? large = null,
        Shape? extraLarge = null)
    {
        // ShapesDefault is generated from a declarative
        // `[assembly: ComposeDefaults("ShapesDefault", ...)]`. Bit set =>
        // Kotlin substitutes the per-slot M3 baseline.
        var defaults = ShapesDefault.None;
        if (extraSmall is null) defaults |= ShapesDefault.ExtraSmall;
        if (small      is null) defaults |= ShapesDefault.Small;
        if (medium     is null) defaults |= ShapesDefault.Medium;
        if (large      is null) defaults |= ShapesDefault.Large;
        if (extraLarge is null) defaults |= ShapesDefault.ExtraLarge;

        return ComposeBridges.BuildShapes(
            extraSmall is null ? IntPtr.Zero : ((Java.Lang.Object)extraSmall).Handle,
            small      is null ? IntPtr.Zero : ((Java.Lang.Object)small).Handle,
            medium     is null ? IntPtr.Zero : ((Java.Lang.Object)medium).Handle,
            large      is null ? IntPtr.Zero : ((Java.Lang.Object)large).Handle,
            extraLarge is null ? IntPtr.Zero : ((Java.Lang.Object)extraLarge).Handle,
            (int)defaults);
    }

    /// <summary>
    /// Compose's <c>isSystemInDarkTheme()</c>: <see langword="true"/>
    /// when the device is currently in dark mode. Must be called from
    /// inside a composable's <c>Render</c> body.
    /// </summary>
    public static bool IsSystemInDarkTheme(IComposer composer) =>
        DarkThemeKt.IsSystemInDarkTheme(composer, 0);

    /// <summary>
    /// Build a Material 3 <see cref="Typography"/> with per-slot text
    /// style overrides. Any <see langword="null"/> slot falls back to
    /// the M3 baseline token for that role.
    /// </summary>
    /// <remarks>
    /// Mirror of Kotlin's
    /// <c>Typography(displayLarge = …, …)</c> constructor with all 15
    /// slots optional. Build each <see cref="TextStyle"/> with the
    /// builder properties (<c>Color</c>, <c>FontSize</c>,
    /// <c>FontWeight</c>, …); unset properties on the
    /// <see cref="TextStyle"/> inherit from the M3 default.
    /// </remarks>
    public static Typography BuildTypography(
        TextStyle? displayLarge = null,
        TextStyle? displayMedium = null,
        TextStyle? displaySmall = null,
        TextStyle? headlineLarge = null,
        TextStyle? headlineMedium = null,
        TextStyle? headlineSmall = null,
        TextStyle? titleLarge = null,
        TextStyle? titleMedium = null,
        TextStyle? titleSmall = null,
        TextStyle? bodyLarge = null,
        TextStyle? bodyMedium = null,
        TextStyle? bodySmall = null,
        TextStyle? labelLarge = null,
        TextStyle? labelMedium = null,
        TextStyle? labelSmall = null)
    {
        // TypographyDefault is generated from a declarative
        // `[assembly: ComposeDefaults("TypographyDefault", ...)]`.
        // Bit set => Kotlin substitutes the per-slot M3 baseline token.
        var defaults = TypographyDefault.None;
        if (displayLarge    is null) defaults |= TypographyDefault.DisplayLarge;
        if (displayMedium   is null) defaults |= TypographyDefault.DisplayMedium;
        if (displaySmall    is null) defaults |= TypographyDefault.DisplaySmall;
        if (headlineLarge   is null) defaults |= TypographyDefault.HeadlineLarge;
        if (headlineMedium  is null) defaults |= TypographyDefault.HeadlineMedium;
        if (headlineSmall   is null) defaults |= TypographyDefault.HeadlineSmall;
        if (titleLarge      is null) defaults |= TypographyDefault.TitleLarge;
        if (titleMedium     is null) defaults |= TypographyDefault.TitleMedium;
        if (titleSmall      is null) defaults |= TypographyDefault.TitleSmall;
        if (bodyLarge       is null) defaults |= TypographyDefault.BodyLarge;
        if (bodyMedium      is null) defaults |= TypographyDefault.BodyMedium;
        if (bodySmall       is null) defaults |= TypographyDefault.BodySmall;
        if (labelLarge      is null) defaults |= TypographyDefault.LabelLarge;
        if (labelMedium     is null) defaults |= TypographyDefault.LabelMedium;
        if (labelSmall      is null) defaults |= TypographyDefault.LabelSmall;

        return ComposeBridges.BuildTypography(
            displayLarge?.Build(),
            displayMedium?.Build(),
            displaySmall?.Build(),
            headlineLarge?.Build(),
            headlineMedium?.Build(),
            headlineSmall?.Build(),
            titleLarge?.Build(),
            titleMedium?.Build(),
            titleSmall?.Build(),
            bodyLarge?.Build(),
            bodyMedium?.Build(),
            bodySmall?.Build(),
            labelLarge?.Build(),
            labelMedium?.Build(),
            labelSmall?.Build(),
            (int)defaults);
    }

}
