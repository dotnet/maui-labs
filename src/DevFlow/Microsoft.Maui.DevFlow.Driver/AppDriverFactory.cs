namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Factory for creating platform-appropriate app drivers.
/// </summary>
public static class AppDriverFactory
{
    // Platforms DevFlow speaks the protocol with, but which are driven by an external toolchain
    // rather than by a driver in this repository. Recognizing them lets callers fail with an
    // actionable message instead of "Unknown platform".
    private static readonly string[] s_platformsWithoutLocalDriver =
    [
        DevFlowPlatform.Tizen,
    ];

    /// <summary>
    /// Whether <paramref name="platform"/> has a local app driver that <see cref="Create"/> can
    /// return. Agents on other platforms remain fully usable over the DevFlow HTTP protocol; only
    /// the host-side lifecycle, theme and recording helpers are unavailable.
    /// </summary>
    public static bool HasLocalDriver(string? platform)
        => DevFlowPlatform.Normalize(platform) switch
        {
            DevFlowPlatform.MacCatalyst
                or DevFlowPlatform.Android
                or DevFlowPlatform.iOS
                or DevFlowPlatform.Windows
                or DevFlowPlatform.Linux => true,
            _ => false
        };

    public static IAppDriver Create(string platform)
    {
        var normalized = DevFlowPlatform.Normalize(platform);

        if (Array.IndexOf(s_platformsWithoutLocalDriver, normalized) >= 0)
        {
            throw new PlatformNotSupportedException(
                $"DevFlow recognizes the '{normalized}' platform but does not provide a local app driver for it. " +
                "Launching, theming and recording are host-side features; connect to the running " +
                $"{DevFlowPlatform.GetDisplayName(normalized)} agent over the DevFlow HTTP protocol instead.");
        }

        return normalized switch
        {
            DevFlowPlatform.MacCatalyst => new MacCatalystAppDriver(),
            DevFlowPlatform.Android => new AndroidAppDriver(),
            DevFlowPlatform.iOS => new iOSSimulatorAppDriver(),
            DevFlowPlatform.Windows => new WindowsAppDriver(),
            DevFlowPlatform.Linux => new LinuxAppDriver(),
            _ => throw new ArgumentException(
                $"Unknown platform: {platform}. Supported: {string.Join(", ", DevFlowPlatform.KnownIds)}")
        };
    }
}
