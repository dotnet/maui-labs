namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public enum AppiumSmokePlatform
{
    Android,
    IOS,
    Mac2,
    Windows,
}

public enum AppiumSmokeHost
{
    Windows,
    MacOS,
    Linux,
    Other,
}

public enum AppiumSmokeNativeIdentifierKind
{
    AndroidContentDescription,
    AppleAccessibilityIdentifier,
    WindowsAutomationId,
}

public static class AppiumSmokePlatformSupport
{
    public static AppiumSmokeHost CurrentHost =>
        OperatingSystem.IsWindows() ? AppiumSmokeHost.Windows :
        OperatingSystem.IsMacOS() ? AppiumSmokeHost.MacOS :
        OperatingSystem.IsLinux() ? AppiumSmokeHost.Linux :
        AppiumSmokeHost.Other;

    public static bool TryParse(string? value, out AppiumSmokePlatform platform)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "android":
                platform = AppiumSmokePlatform.Android;
                return true;
            case "ios":
                platform = AppiumSmokePlatform.IOS;
                return true;
            case "mac2":
            case "maccatalyst":
            case "mac-catalyst":
                platform = AppiumSmokePlatform.Mac2;
                return true;
            case "windows":
                platform = AppiumSmokePlatform.Windows;
                return true;
            default:
                platform = default;
                return false;
        }
    }

    public static string PlatformName(AppiumSmokePlatform platform) => platform switch
    {
        AppiumSmokePlatform.Android => "Android",
        AppiumSmokePlatform.IOS => "iOS",
        AppiumSmokePlatform.Mac2 => "mac",
        AppiumSmokePlatform.Windows => "Windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    public static string AutomationName(AppiumSmokePlatform platform) => platform switch
    {
        AppiumSmokePlatform.Android => "UiAutomator2",
        AppiumSmokePlatform.IOS => "XCUITest",
        AppiumSmokePlatform.Mac2 => "Mac2",
        AppiumSmokePlatform.Windows => "Windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    public static bool IsHostSupported(AppiumSmokePlatform platform, AppiumSmokeHost host) => platform switch
    {
        AppiumSmokePlatform.Android => host is AppiumSmokeHost.Windows or AppiumSmokeHost.MacOS or AppiumSmokeHost.Linux,
        AppiumSmokePlatform.IOS or AppiumSmokePlatform.Mac2 => host == AppiumSmokeHost.MacOS,
        AppiumSmokePlatform.Windows => host == AppiumSmokeHost.Windows,
        _ => false,
    };

    public static string HostRequirement(AppiumSmokePlatform platform) => platform switch
    {
        AppiumSmokePlatform.Android => "a Windows, macOS, or Linux host",
        AppiumSmokePlatform.IOS or AppiumSmokePlatform.Mac2 => "a macOS host",
        AppiumSmokePlatform.Windows => "a Windows host",
        _ => "a supported host",
    };

    public static bool SupportsSystemPermissionDialog(AppiumSmokePlatform platform) =>
        platform is AppiumSmokePlatform.Android or AppiumSmokePlatform.IOS;

    public static AppiumSmokeNativeIdentifierKind NativeIdentifierKind(AppiumSmokePlatform platform) => platform switch
    {
        AppiumSmokePlatform.Android => AppiumSmokeNativeIdentifierKind.AndroidContentDescription,
        AppiumSmokePlatform.IOS or AppiumSmokePlatform.Mac2 => AppiumSmokeNativeIdentifierKind.AppleAccessibilityIdentifier,
        AppiumSmokePlatform.Windows => AppiumSmokeNativeIdentifierKind.WindowsAutomationId,
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };
}
