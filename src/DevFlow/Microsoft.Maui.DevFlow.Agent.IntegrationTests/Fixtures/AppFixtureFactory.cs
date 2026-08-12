namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Factory that creates the appropriate platform fixture based on
/// the DEVFLOW_TEST_PLATFORM environment variable.
/// </summary>
public static class AppFixtureFactory
{
    public static IAppFixture Create()
    {
        var platform = Environment.GetEnvironmentVariable("DEVFLOW_TEST_PLATFORM")?.ToLowerInvariant();

        if (string.IsNullOrEmpty(platform))
        {
            platform = OperatingSystem.IsWindows() ? "windows" : "maccatalyst";
        }

        return platform switch
        {
            "maccatalyst" or "mac" or "catalyst" => new MacCatalystFixture(),
            // macOS is native-only: MAUI has no in-box AppKit head to drive.
            "macos" or "appkit" => new MacOSFixture(),
            "ios" => new iOSSimulatorFixture(),
            "android" => new AndroidEmulatorFixture(),
            "windows" => new WindowsFixture(),
            "wpf" => new WpfFixture(),
            "gtk" or "linux" => new GtkFixture(),
            _ => throw new InvalidOperationException(
                $"Unknown test platform '{platform}'. " +
                "Supported values: maccatalyst, macos, ios, android, windows, wpf, gtk. " +
                "Set the DEVFLOW_TEST_PLATFORM environment variable.")
        };
    }
}
