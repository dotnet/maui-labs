namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The default look of <see cref="AIChatView"/>: colors, fonts, styles, and the control template.</summary>
/// <remarks>
/// <see cref="AIChatView"/> merges one instance into <c>Application.Current.Resources</c> (at index 0, so the
/// application's own resources win) the first time a view is created. Merge it yourself in App.xaml
/// (<c>&lt;chat:AIChatTheme /&gt;</c>) when you want to base your own styles on its keys with
/// <c>StaticResource</c>; the view then reuses your instance. See <see cref="AIChatThemeKeys"/> for every key.
/// Besides those keys, the theme holds empty implicit styles for the view's internal element types, which keep the
/// application's implicit styles away from the view's own parts.
/// </remarks>
public partial class AIChatTheme : ResourceDictionary
{
    private const string ColorPrefix = "AIChat.Color.";

    internal static readonly string[] ColorNames =
    [
        "Text", "SecondaryText", "Accent", "OnAccent", "UserBubble", "UserText",
        "Surface", "Stroke", "CodeBackground", "Error", "ErrorBackground", "Success",
    ];

    /// <summary>Creates the theme and resolves the active colors for the current app theme.</summary>
    public AIChatTheme()
    {
        InitializeComponent();
        var platform = GetPlatform();
        this[AIChatThemeKeys.FontFamily] = null!;
        this[AIChatThemeKeys.CodeFontFamily] = GetCodeFontFamily(platform);
        ((Style)this[AIChatThemeKeys.InputStyle]).Setters.Add(new Setter { Property = View.MarginProperty, Value = GetInputMargin(platform) });

        // Keeps the app's implicit styles away from the view's own parts.
        ChatElements.AddImplicitStyles(this);

        var application = Application.Current;
        ApplyTheme(GetRequestedTheme(application));
        if (application is not null)
            application.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    /// <summary>
    /// Merges the theme into the application resources unless it is already there. Returns <see langword="false"/>
    /// when there is no application to merge into.
    /// </summary>
    internal static bool EnsureLoaded()
    {
        if (Application.Current is not { } application)
            return false;

        var resources = application.Resources;
        if (Contains(resources))
            return true;

        var theme = new AIChatTheme();
        if (resources.MergedDictionaries is IList<ResourceDictionary> list)
            list.Insert(0, theme);
        else
            resources.MergedDictionaries.Add(theme);

        // Re-resolve now that per-theme palette overrides in the app resources are reachable.
        theme.ApplyTheme(GetRequestedTheme(application));
        return true;
    }

    /// <summary>Copies the light or dark palette into the active <c>AIChat.Color.*</c> keys.</summary>
    internal void ApplyTheme(AppTheme theme)
    {
        var prefix = theme == AppTheme.Dark ? AIChatThemeKeys.DarkPalettePrefix : AIChatThemeKeys.LightPalettePrefix;
        var appResources = Application.Current?.Resources;

        foreach (var name in ColorNames)
        {
            var key = ColorPrefix + name;

            // The app overrides the active color itself: leave ours alone so a theme change can't clobber it.
            if (appResources is not null && IsOverridden(appResources, key))
                continue;

            if (!TryResolve(appResources, prefix + name, out var value) ||
                (TryGetValue(key, out var current) && Equals(current, value)))
            {
                continue;
            }

            this[key] = value;
        }
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        ApplyTheme(e.RequestedTheme);

    private bool TryResolve(ResourceDictionary? appResources, string key, out object value) =>
        (appResources is not null && appResources.TryGetValue(key, out value!)) || TryGetValue(key, out value!);

    private static bool IsOverridden(ResourceDictionary resources, string key)
    {
        if (resources is AIChatTheme)
            return false;

        if (resources.Keys.Contains(key))
            return true;

        foreach (var merged in resources.MergedDictionaries)
        {
            if (IsOverridden(merged, key))
                return true;
        }

        return false;
    }

    private static bool Contains(ResourceDictionary resources)
    {
        if (resources is AIChatTheme)
            return true;

        foreach (var merged in resources.MergedDictionaries)
        {
            if (Contains(merged))
                return true;
        }

        return false;
    }

    private static AppTheme GetRequestedTheme(Application? application)
    {
        try
        {
            return application?.RequestedTheme ?? AppTheme.Light;
        }
        catch
        {
            // Unit tests and unsupported hosts have no platform theme.
            return application?.UserAppTheme is AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        }
    }

    /// <summary>Gets the monospace font family for code on <paramref name="platform"/>.</summary>
    internal static string GetCodeFontFamily(DevicePlatform platform)
    {
        if (platform == DevicePlatform.iOS || platform == DevicePlatform.MacCatalyst || platform == DevicePlatform.macOS)
            return "Menlo";
        if (platform == DevicePlatform.WinUI)
            return "Consolas";

        // Android, and fontconfig on Linux, resolve the generic family.
        return "monospace";
    }

    /// <summary>
    /// Gets the margin of the message box on <paramref name="platform"/>: it puts a line of the body text on the centre of
    /// the composer's 36-point buttons, whose bottom it shares, and keeps one line no taller than they are. The platforms
    /// inset the text differently (measured with the default body size, 15).
    /// </summary>
    internal static Thickness GetInputMargin(DevicePlatform platform)
    {
        // UITextView: 8-point insets.
        if (platform == DevicePlatform.iOS || platform == DevicePlatform.MacCatalyst)
            return new Thickness(0, 0, 0, 1);

        // EditText: Material's insets are deeper, more so at the top.
        if (platform == DevicePlatform.Android)
            return new Thickness(0, -3.5, 0, -1.8);

        // Not measured.
        return default;
    }

    internal static DevicePlatform GetPlatform()
    {
        try
        {
            return DeviceInfo.Platform;
        }
        catch
        {
            // No platform (unit tests).
            return DevicePlatform.Unknown;
        }
    }
}
