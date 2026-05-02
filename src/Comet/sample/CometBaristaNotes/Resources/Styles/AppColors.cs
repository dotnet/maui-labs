namespace BaristaNotes.Styles;

/// <summary>
/// Original color definitions from the MauiReactor BaristaNotes app.
/// DEPRECATED: Use <see cref="CometBaristaNotes.Styles.CoffeeTheme"/> and
/// <see cref="CometBaristaNotes.Styles.CoffeeTokens"/> for theme-aware colors.
/// Retained as a reference during migration.
/// </summary>
[Obsolete("Use CoffeeTheme.Light/Dark and CoffeeTokens instead.")]
public static class AppColors
{
    // Semantic colors (same in both themes)
    public static Color Success { get; } = Color.FromArgb("#4CAF50");
    public static Color Warning { get; } = Color.FromArgb("#FFA726");
    public static Color Error { get; } = Color.FromArgb("#EF5350");
    public static Color Info { get; } = Color.FromArgb("#42A5F5");

    /// <summary>
    /// Light mode coffee palette.
    /// </summary>
    public static class Light
    {
        // Backgrounds & Surfaces
        public static Color Background { get; } = Color.FromArgb("#D2BCA5");
        public static Color Surface { get; } = Color.FromArgb("#FCEFE1");
        public static Color SurfaceVariant { get; } = Color.FromArgb("#ECDAC4");
        public static Color SurfaceElevated { get; } = Color.FromArgb("#FFF7EC");

        // Brand & Accent
        public static Color Primary { get; } = Color.FromArgb("#86543F");
        public static Color OnPrimary { get; } = Color.FromArgb("#F8F6F4");

        // Typography
        public static Color TextPrimary { get; } = Color.FromArgb("#352B23");
        public static Color TextSecondary { get; } = Color.FromArgb("#7C7067");
        public static Color TextMuted { get; } = Color.FromArgb("#A38F7D");

        // Borders & Dividers
        public static Color Outline { get; } = Color.FromArgb("#D7C5B2");
    }

    /// <summary>
    /// Dark mode coffee palette.
    /// </summary>
    public static class Dark
    {
        // Backgrounds & Surfaces
        public static Color Background { get; } = Color.FromArgb("#48362E");
        public static Color Surface { get; } = Color.FromArgb("#48362E");
        public static Color SurfaceVariant { get; } = Color.FromArgb("#7D5A45");
        public static Color SurfaceElevated { get; } = Color.FromArgb("#B3A291");

        // Brand & Accent
        public static Color Primary { get; } = Color.FromArgb("#86543F");
        public static Color OnPrimary { get; } = Color.FromArgb("#F8F6F4");

        // Typography
        public static Color TextPrimary { get; } = Color.FromArgb("#F8F6F4");
        public static Color TextSecondary { get; } = Color.FromArgb("#C5BFBB");
        public static Color TextMuted { get; } = Color.FromArgb("#A19085");

        // Borders & Dividers
        public static Color Outline { get; } = Color.FromArgb("#5A463B");
    }
}