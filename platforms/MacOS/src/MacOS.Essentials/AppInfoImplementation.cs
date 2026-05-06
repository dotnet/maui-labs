using System.Runtime.InteropServices;
using Foundation;
using AppKit;
using ObjCRuntime;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class AppInfoImplementation : IAppInfo
{
    public AppPackagingModel PackagingModel => AppPackagingModel.Packaged;

    public string PackageName => GetBundleValue("CFBundleIdentifier") ?? string.Empty;

    public string Name => GetBundleValue("CFBundleDisplayName") ?? GetBundleValue("CFBundleName") ?? string.Empty;

    public Version Version => Utils.ParseVersion(VersionString);

    public string VersionString => GetBundleValue("CFBundleShortVersionString") ?? "1.0.0";

    public string BuildString => GetBundleValue("CFBundleVersion") ?? "1";

    public AppTheme RequestedTheme
    {
        get
        {
            // Read the system theme from UserDefaults, independent of any
            // app-level NSApplication.Appearance override.
            var style = Foundation.NSUserDefaults.StandardUserDefaults.StringForKey("AppleInterfaceStyle");
            return string.Equals(style, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;
        }
    }

    public LayoutDirection RequestedLayoutDirection =>
        NSApplication.SharedApplication.UserInterfaceLayoutDirection == NSUserInterfaceLayoutDirection.RightToLeft
            ? LayoutDirection.RightToLeft
            : LayoutDirection.LeftToRight;

    public void ShowSettingsUI()
    {
        MainThreadHelper.BeginInvokeOnMainThread(() =>
        {
            NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:"));
        });
    }

    static string? GetBundleValue(string key)
        => NSBundle.MainBundle.ObjectForInfoDictionary(key)?.ToString();
}

static class Utils
{
    public static Version ParseVersion(string? version)
    {
        if (Version.TryParse(version, out var result))
            return result;
        return new Version(1, 0);
    }
}
