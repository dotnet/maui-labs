namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Factory that creates the appropriate platform fixture based on
/// the DEVFLOW_TEST_PLATFORM environment variable.
/// </summary>
public static class AppFixtureFactory
{
    public static IAppFixture Create()
    {
        return CreateForPlatform(
            Environment.GetEnvironmentVariable("DEVFLOW_TEST_PLATFORM"),
            OperatingSystem.IsWindows());
    }

    internal static IAppFixture CreateForPlatform(string? configuredPlatform, bool isWindows)
    {
        var platform = configuredPlatform?.ToLowerInvariant();
        if (string.IsNullOrEmpty(platform))
        {
            platform = isWindows ? "windows" : "maccatalyst";
        }

        return platform switch
        {
            "maccatalyst" or "mac" or "catalyst" => new MacCatalystFixture(),
            // AppKit is deliberately selected only by its explicit experimental key. Do not
            // add it to the "mac" alias above: that alias remains Mac Catalyst compatibility.
            "macos" => new MacOSFixture(),
            "ios" => new iOSSimulatorFixture(),
            "android" => new AndroidEmulatorFixture(),
            "windows" => new WindowsFixture(),
            _ => throw new InvalidOperationException(
                $"Unknown test platform '{platform}'. " +
                "Supported values: maccatalyst, macos (experimental AppKit), ios, android, windows. " +
                "Set the DEVFLOW_TEST_PLATFORM environment variable.")
        };
    }
}
