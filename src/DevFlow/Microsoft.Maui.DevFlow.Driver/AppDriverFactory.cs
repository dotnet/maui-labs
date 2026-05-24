namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Factory for creating platform-appropriate app drivers.
/// </summary>
public static class AppDriverFactory
{
    public static IAppDriver Create(string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "maccatalyst" or "mac" or "catalyst" => new MacCatalystAppDriver(),
            "android" => new AndroidAppDriver(),
            "ios" or "iossimulator" => new iOSSimulatorAppDriver(),
            "windows" or "win" or "winui" or "wpf" => new WindowsAppDriver(),
            "linux" or "gtk" => new LinuxAppDriver(),
            _ => throw new ArgumentException($"Unknown platform: {platform}. Supported: maccatalyst, android, ios, windows, wpf, linux")
        };
    }
}
