namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Selects which .NET generation of a community-backend sample (GTK, WPF, AppKit) the suite builds.
///
/// Those backends ship net10.0 assets (MAUI 10 compatibility) and net11.0 assets (MAUI 11 public
/// extensibility contracts), and their DevFlow samples multi-target both. Set
/// <c>DEVFLOW_TEST_DOTNET=11</c> to drive the net11.0 build; the default is <c>10</c>.
/// </summary>
public static class TestDotNet
{
    public const string Ten = "10";
    public const string Eleven = "11";

    /// <summary>The .NET major version under test: <see cref="Ten"/> (default) or <see cref="Eleven"/>.</summary>
    public static string Version { get; } = Resolve();

    public static bool IsEleven => Version == Eleven;

    /// <summary>Returns the target framework matching <see cref="Version"/>.</summary>
    public static string Select(string net10TargetFramework, string net11TargetFramework)
        => IsEleven ? net11TargetFramework : net10TargetFramework;

    private static string Resolve()
    {
        var value = Environment.GetEnvironmentVariable("DEVFLOW_TEST_DOTNET")?.Trim().ToLowerInvariant();

        return value switch
        {
            null or "" or Ten or "net10" or "net10.0" => Ten,
            Eleven or "net11" or "net11.0" => Eleven,
            _ => throw new InvalidOperationException(
                $"Unknown DEVFLOW_TEST_DOTNET value '{value}'. Supported values: {Ten}, {Eleven}."),
        };
    }
}
