namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Factory for creating platform-appropriate app drivers.
/// </summary>
public static class AppDriverFactory
{
    // Platforms DevFlow speaks the protocol with, but which have no driver in this repository:
    // Tizen is driven by an external toolchain, and the macOS AppKit agent has no host-side driver
    // (MacCatalystAppDriver covers Mac Catalyst only). Recognizing them lets callers fail with an
    // actionable message instead of "Unknown platform".
    private static readonly string[] s_platformsWithoutLocalDriver =
    [
        DevFlowPlatform.Tizen,
        DevFlowPlatform.MacOS,
    ];

    private static readonly string[] s_platformsWithLocalDriver =
    [
        DevFlowPlatform.MacCatalyst,
        DevFlowPlatform.Android,
        DevFlowPlatform.iOS,
        DevFlowPlatform.Windows,
        DevFlowPlatform.Linux,
    ];

    /// <summary>
    /// Whether <paramref name="platform"/> has a local app driver that <see cref="Create"/> can
    /// return. Agents on other platforms remain fully usable over the DevFlow HTTP protocol; only
    /// the host-side lifecycle, theme and recording helpers are unavailable.
    /// </summary>
    public static bool HasLocalDriver(string? platform)
        => Array.IndexOf(s_platformsWithLocalDriver, DevFlowPlatform.Normalize(platform)) >= 0;

    /// <summary>
    /// Creates the host-side driver for <paramref name="platform"/>.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// <paramref name="platform"/> is a platform DevFlow recognizes but ships no local driver for,
    /// such as <c>tizen</c> or <c>macos</c>. Note that such values previously produced an
    /// <see cref="ArgumentException"/>, being indistinguishable from an unknown platform.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="platform"/> is not a platform DevFlow recognizes at all.
    /// </exception>
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
            // Deliberately lists only the platforms this method can actually construct, which is a
            // subset of the identities DevFlowPlatform recognizes.
            _ => throw new ArgumentException(
                $"Unknown platform: {platform}. Supported: {string.Join(", ", s_platformsWithLocalDriver)}")
        };
    }
}
