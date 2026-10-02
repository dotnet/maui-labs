#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using T = CometSamples.BaristaNotes.Styles.CoffeeTheme;
using S = CometSamples.BaristaNotes.Styles.CoffeeSpacing;
using F = CometSamples.BaristaNotes.Styles.CoffeeFontSizes;

namespace CometSamples.BaristaNotes.Styles;

/// <summary>
/// Reusable view modifiers for BaristaNotes — mirrors the reference app's
/// <c>ThemeKeys</c> semantic names using Comet's typed modifier system.
/// </summary>
public static class CoffeeModifiers
{
    public static View Headline(this View v) =>
        v.FontFamily("ManropeSemibold").FontSize(F.TitleLarge).Color(T.TextPrimary);

    public static View SubHeadline(this View v) =>
        v.FontFamily("ManropeSemibold").FontSize(F.TitleMedium).Color(T.TextPrimary);

    public static View SecondaryText(this View v) =>
        v.FontFamily("Manrope").FontSize(F.BodySmall).Color(T.TextSecondary);

    public static View MutedText(this View v) =>
        v.FontFamily("Manrope").FontSize(F.Caption).Color(T.TextMuted);

    public static View SectionLabel(this View v) =>
        v.FontFamily("ManropeSemibold")
         .FontSize(10f)
         .Color(T.TextSecondary)
         .SetEnvironment(nameof(ITextStyle.CharacterSpacing), 2d, true);

    public static View RangeCaption(this View v) =>
        v.FontFamily("ManropeSemibold")
         .FontSize(F.Caption)
         .Color(T.TextSecondary)
         .SetEnvironment(nameof(ITextStyle.CharacterSpacing), 2d, true);

    public static View CardStyle(this View v) =>
        v.Background(T.SurfaceColor)
         .Padding(new Thickness(S.M));

    public static View FlatTile(this View v, float padding = S.M) =>
        v.Background(T.SurfaceColor)
         .Padding(new Thickness(padding));

    public static View SourceText(this View v) =>
        v.FontFamily("Manrope").Color(T.TextPrimary);

    public static TextField SourceInputChrome(this TextField field) =>
        field.Borderless();

    public static TextEditor SourceInputChrome(this TextEditor editor) =>
        editor.Borderless();

    public static View CoffeePageBackground(this View v) =>
        v.Background(T.BackgroundColor);
}
