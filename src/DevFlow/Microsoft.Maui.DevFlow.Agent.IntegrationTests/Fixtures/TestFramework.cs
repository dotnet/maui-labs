namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Selects which sample app the integration suite drives.
///
/// The same tests run against the MAUI sample and against the plain .NET (native) samples, so the
/// UI assertions are shared. Anything that only exists in MAUI — Shell navigation, app theme,
/// preferences, secure storage, sensors, background jobs, BlazorWebView — is tagged with
/// <c>[Trait(TestFramework.Trait, TestFramework.Maui)]</c> and filtered out of native runs with
/// <c>--filter "framework!=maui"</c>.
/// </summary>
public static class TestFramework
{
    /// <summary>xUnit trait name used to scope a test to one framework.</summary>
    public const string Trait = "framework";

    public const string Maui = "maui";
    public const string Native = "native";

    /// <summary>The framework under test: <see cref="Maui"/> (default) or <see cref="Native"/>.</summary>
    public static string Name { get; } = Resolve();

    public static bool IsNative => Name == Native;

    /// <summary>
    /// Maps a fixture platform onto the folder name of the matching native sample head.
    /// iOS and Mac Catalyst have separate heads because a plain .NET app cannot multi-target
    /// the way a MAUI single project does.
    /// </summary>
    public static string NativeHeadFor(string platform) => platform switch
    {
        "android" => "Android",
        "ios" => "iOS",
        "maccatalyst" => "MacCatalyst",
        "macos" => "MacOS",
        _ => throw new InvalidOperationException(
            $"There is no native sample head for platform '{platform}'. " +
            $"Native runs support android, ios, maccatalyst and macos; " +
            $"unset DEVFLOW_TEST_FRAMEWORK to run against the MAUI sample instead."),
    };

    private static string Resolve()
    {
        var value = Environment.GetEnvironmentVariable("DEVFLOW_TEST_FRAMEWORK")?.Trim().ToLowerInvariant();

        return value switch
        {
            null or "" or Maui => Maui,
            Native => Native,
            _ => throw new InvalidOperationException(
                $"Unknown test framework '{value}'. Supported values: {Maui}, {Native}. " +
                "Set the DEVFLOW_TEST_FRAMEWORK environment variable."),
        };
    }
}
