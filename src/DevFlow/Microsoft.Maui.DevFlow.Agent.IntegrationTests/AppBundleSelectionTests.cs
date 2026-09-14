using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Guards how a macOS or Mac Catalyst <c>.app</c> bundle is chosen out of a build tree that can
/// hold several of them.
/// </summary>
/// <remarks>
/// Launching the wrong one does not fail: an x64 bundle runs happily under Rosetta on Apple
/// Silicon, so the suite would keep passing while testing an architecture nobody asked for. The
/// choice therefore has to be asserted rather than inferred from a green run. These are pure
/// string tests over synthetic paths, so they need no build output and run on every axis.
/// </remarks>
public class AppBundleSelectionTests
{
    static string HostArch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    static string ForeignArch => HostArch == "arm64" ? "x64" : "arm64";

    static string BundleUnderRid(string ridPrefix, string arch) =>
        Path.Combine("bin", "Debug", "net10.0-macos", $"{ridPrefix}-{arch}", "Sample.app");

    static string UniversalBundle() =>
        Path.Combine("bin", "Debug", "net10.0-macos", "Sample.app");

    [Theory]
    [InlineData("osx")]
    [InlineData("maccatalyst")]
    public void SelectHostArchitectureAppBundle_PrefersHostRid_OverForeignRid(string ridPrefix)
    {
        var host = BundleUnderRid(ridPrefix, HostArch);
        var foreign = BundleUnderRid(ridPrefix, ForeignArch);

        // Foreign first, so a naive "take the first match" would pick wrong.
        Assert.Equal(host, AppFixtureBase.SelectHostArchitectureAppBundle([foreign, host], ridPrefix));
        Assert.Equal(host, AppFixtureBase.SelectHostArchitectureAppBundle([host, foreign], ridPrefix));
    }

    [Theory]
    [InlineData("osx")]
    [InlineData("maccatalyst")]
    public void SelectHostArchitectureAppBundle_PrefersHostRid_OverUniversal(string ridPrefix)
    {
        var host = BundleUnderRid(ridPrefix, HostArch);

        Assert.Equal(
            host,
            AppFixtureBase.SelectHostArchitectureAppBundle([UniversalBundle(), host], ridPrefix));
    }

    [Theory]
    [InlineData("osx")]
    [InlineData("maccatalyst")]
    public void SelectHostArchitectureAppBundle_FallsBackToUniversal_WhenNoHostRid(string ridPrefix)
    {
        // A Release build lipo's a universal bundle at the target-framework root; it contains the
        // host slice even though no directory names the host RID.
        var universal = UniversalBundle();

        Assert.Equal(
            universal,
            AppFixtureBase.SelectHostArchitectureAppBundle(
                [BundleUnderRid(ridPrefix, ForeignArch), universal], ridPrefix));
    }

    [Theory]
    [InlineData("osx")]
    [InlineData("maccatalyst")]
    public void SelectHostArchitectureAppBundle_Throws_WhenOnlyForeignArchitecture(string ridPrefix)
    {
        var foreign = BundleUnderRid(ridPrefix, ForeignArch);

        var ex = Assert.Throws<InvalidOperationException>(
            () => AppFixtureBase.SelectHostArchitectureAppBundle([foreign], ridPrefix));

        Assert.Contains($"{ridPrefix}-{HostArch}", ex.Message);
    }
}
