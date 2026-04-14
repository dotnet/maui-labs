using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample;

/// <summary>
/// Theme-aware color palette. Use the Set* extension methods to apply theme bindings,
/// or the static color properties for non-bindable contexts.
/// </summary>
public static class AppColors
{
    // Page backgrounds
    public static Color PageBackgroundLight => Color.FromArgb("#F0F0F5");
    public static Color PageBackgroundDark => Color.FromArgb("#1A1A2E");
    public static Color SurfaceLight => Color.FromArgb("#FFFFFF");
    public static Color SurfaceDark => Color.FromArgb("#2A2A4A");
    public static Color SidebarLight => Color.FromArgb("#E8E8F0");
    public static Color SidebarDark => Color.FromArgb("#1E1E3A");

    // Text
    public static Color TextPrimaryLight => Color.FromArgb("#1A1A2E");
    public static Color TextPrimaryDark => Colors.White;
    public static Color TextSecondaryLight => Color.FromArgb("#555555");
    public static Color TextSecondaryDark => Color.FromArgb("#AAAAAA");
    public static Color TextMuted => Color.FromArgb("#888888");
    public static Color StatusTextLight => Color.FromArgb("#2ECC71");
    public static Color StatusTextDark => Color.FromArgb("#A8E6CF");

    // Section headers
    public static Color SectionHeaderLight => Color.FromArgb("#D04040");
    public static Color SectionHeaderDark => Color.FromArgb("#FF6B6B");
    public static Color SectionHeaderAltLight => Color.FromArgb("#2980B9");
    public static Color SectionHeaderAltDark => Color.FromArgb("#4FC3F7");
    public static Color SectionHeaderAccentLight => Color.FromArgb("#C0392B");
    public static Color SectionHeaderAccentDark => Color.FromArgb("#E94560");

    // Accent / action buttons (same in both themes — vibrant)
    public static Color AccentBlue => Color.FromArgb("#4A90E2");
    public static Color AccentPurple => Color.FromArgb("#7B68EE");
    public static Color AccentGreen => Color.FromArgb("#2ECC71");
    public static Color AccentRed => Color.FromArgb("#E74C3C");
    public static Color AccentOrange => Color.FromArgb("#F39C12");
    public static Color AccentTeal => Color.FromArgb("#1ABC9C");
    public static Color AccentPink => Color.FromArgb("#FF6B6B");

    // Controls
    public static Color EntryBgLight => Color.FromArgb("#F5F5F5");
    public static Color EntryBgDark => Colors.White;
    public static Color PickerBgLight => Color.FromArgb("#EEEEEE");
    public static Color PickerBgDark => Color.FromArgb("#333333");
    public static Color DividerLight => Color.FromArgb("#CCCCCC");
    public static Color DividerDark => Color.FromArgb("#FF6B6B");

    // Extension methods for applying theme bindings via the public SetAppThemeColor API
    public static T WithPageBackground<T>(this T view) where T : VisualElement
    {
        view.SetAppThemeColor(VisualElement.BackgroundColorProperty, PageBackgroundLight, PageBackgroundDark);
        return view;
    }

    public static T WithSurfaceBackground<T>(this T view) where T : VisualElement
    {
        view.SetAppThemeColor(VisualElement.BackgroundColorProperty, SurfaceLight, SurfaceDark);
        return view;
    }

    public static T WithSidebarBackground<T>(this T view) where T : VisualElement
    {
        view.SetAppThemeColor(VisualElement.BackgroundColorProperty, SidebarLight, SidebarDark);
        return view;
    }

    public static Label WithPrimaryText(this Label label)
    {
        label.SetAppThemeColor(Label.TextColorProperty, TextPrimaryLight, TextPrimaryDark);
        return label;
    }

    public static Label WithSecondaryText(this Label label)
    {
        label.SetAppThemeColor(Label.TextColorProperty, TextSecondaryLight, TextSecondaryDark);
        return label;
    }

    public static Label WithStatusText(this Label label)
    {
        label.SetAppThemeColor(Label.TextColorProperty, StatusTextLight, StatusTextDark);
        return label;
    }

    public static Label WithSectionStyle(this Label label)
    {
        label.SetAppThemeColor(Label.TextColorProperty, SectionHeaderLight, SectionHeaderDark);
        return label;
    }

    public static Entry WithEntryTheme(this Entry entry)
    {
        entry.SetAppThemeColor(Entry.BackgroundColorProperty, EntryBgLight, EntryBgDark);
        entry.SetAppThemeColor(Entry.TextColorProperty, TextPrimaryLight, TextPrimaryDark);
        entry.PlaceholderColor = TextMuted;
        return entry;
    }

    public static Picker WithPickerTheme(this Picker picker)
    {
        picker.SetAppThemeColor(Picker.BackgroundColorProperty, PickerBgLight, PickerBgDark);
        picker.SetAppThemeColor(Picker.TextColorProperty, TextPrimaryLight, TextPrimaryDark);
        return picker;
    }
}
