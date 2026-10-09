#nullable enable
using Comet.Reactive;
using CometBaristaNotes.Services;
using Microsoft.Maui.Graphics;

namespace CometSamples.BaristaNotes.Styles;

public enum CoffeeThemeMode
{
    Light,
    Dark,
    System,
}

/// <summary>
/// Coffee-themed color palette with light, dark, and system modes — ported from the
/// reference <c>BaristaNotes.Styles.AppColors</c>.
/// </summary>
public static class CoffeeTheme
{
    // Semantic colors (same in both themes)
    public static readonly Color Success = Color.FromArgb("#4CAF50");
    public static readonly Color Warning = Color.FromArgb("#FFA726");
    public static readonly Color Error   = Color.FromArgb("#EF5350");
    public static readonly Color Info    = Color.FromArgb("#42A5F5");

    public static class Light
    {
        public static readonly Color Background      = Color.FromArgb("#D2BCA5");
        public static readonly Color Surface          = Color.FromArgb("#FCEFE1");
        public static readonly Color SurfaceVariant   = Color.FromArgb("#ECDAC4");
        public static readonly Color SurfaceElevated  = Color.FromArgb("#FFF7EC");
        public static readonly Color Primary          = Color.FromArgb("#86543F");
        public static readonly Color OnPrimary        = Color.FromArgb("#F8F6F4");
        public static readonly Color TextPrimary      = Color.FromArgb("#352B23");
        public static readonly Color TextSecondary    = Color.FromArgb("#7C7067");
        public static readonly Color TextMuted        = Color.FromArgb("#A38F7D");
        public static readonly Color Outline          = Color.FromArgb("#D7C5B2");
    }

    public static class Dark
    {
        public static readonly Color Background      = Color.FromArgb("#48362E");
        public static readonly Color Surface          = Color.FromArgb("#48362E");
        public static readonly Color SurfaceVariant   = Color.FromArgb("#7D5A45");
        public static readonly Color SurfaceElevated  = Color.FromArgb("#B3A291");
        public static readonly Color Primary          = Color.FromArgb("#86543F");
        public static readonly Color OnPrimary        = Color.FromArgb("#F8F6F4");
        public static readonly Color TextPrimary      = Color.FromArgb("#F8F6F4");
        public static readonly Color TextSecondary    = Color.FromArgb("#C5BFBB");
        public static readonly Color TextMuted        = Color.FromArgb("#A19085");
        public static readonly Color Outline          = Color.FromArgb("#5A463B");
    }

    /// <summary>Reactive user-selected theme mode.
    /// Read <see cref="IsLight"/> (which touches <c>Mode.Value</c>) inside a Comet
    /// <c>body()</c> to subscribe to theme changes.</summary>
    static readonly Signal<CoffeeThemeMode> _mode = new(CoffeeThemeMode.System);

    public static Signal<CoffeeThemeMode> Mode
    {
        get
        {
            BaristaAppStorage.Current.MarkResolutionStarted();
            return _mode;
        }
    }

    /// <summary>Whether we are in light mode. Reading this inside a Comet body()
    /// subscribes that view to theme change notifications via the <see cref="Mode"/>
    /// signal.</summary>
    public static bool IsLight => Mode.Value switch
    {
        CoffeeThemeMode.Light => true,
        CoffeeThemeMode.Dark => false,
        _ => IsSystemLight(),
    };

    public static void SetMode(CoffeeThemeMode mode) => Mode.Value = mode;

    public static void SetDark(bool dark)
        => Mode.Value = dark ? CoffeeThemeMode.Dark : CoffeeThemeMode.Light;

    static bool IsSystemLight()
    {
#if ANDROID
        var mode = Android.App.Application.Context.Resources?.Configuration?.UiMode
            & Android.Content.Res.UiMode.NightMask;
        return mode != Android.Content.Res.UiMode.NightYes;
#elif IOS
        return UIKit.UIScreen.MainScreen.TraitCollection.UserInterfaceStyle
            != UIKit.UIUserInterfaceStyle.Dark;
#else
        return true;
#endif
    }

    // Convenience accessors that resolve against current mode.
    public static Color BackgroundColor   => IsLight ? Light.Background      : Dark.Background;
    public static Color SurfaceColor      => IsLight ? Light.Surface         : Dark.Surface;
    public static Color SurfaceVariant    => IsLight ? Light.SurfaceVariant  : Dark.SurfaceVariant;
    public static Color SurfaceElevated   => IsLight ? Light.SurfaceElevated : Dark.SurfaceElevated;
    public static Color PrimaryColor      => IsLight ? Light.Primary         : Dark.Primary;
    public static Color OnPrimaryColor    => IsLight ? Light.OnPrimary       : Dark.OnPrimary;
    public static Color TextPrimary       => IsLight ? Light.TextPrimary     : Dark.TextPrimary;
    public static Color TextSecondary     => IsLight ? Light.TextSecondary   : Dark.TextSecondary;
    public static Color TextMuted         => IsLight ? Light.TextMuted       : Dark.TextMuted;
    public static Color OutlineColor      => IsLight ? Light.Outline         : Dark.Outline;
}
