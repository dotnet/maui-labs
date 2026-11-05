using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The app-base-directory tie-breaker used when ranking DevFlow actions.
///
/// <para>
/// Every action is ranked through this helper, so throwing here takes down the whole invoke
/// API. It ran fine on desktop and iOS but crashed on Android, where assemblies loaded from
/// the APK report a bare file name with no directory component.
/// </para>
/// </summary>
public class InvokeActionRankingTests
{
    [Theory]
    // The Android case: Path.GetDirectoryName returns "" (not null) for a bare file name,
    // and Path.GetFullPath("") throws ArgumentException.
    [InlineData("Microsoft.Maui.DevFlow.Agent.Core.dll", "/data/user/0/com.example.app/files")]
    // Assemblies embedded in a single-file bundle report no location at all.
    [InlineData("", "/data/user/0/com.example.app/files")]
    [InlineData(null, "/data/user/0/com.example.app/files")]
    // AppContext.BaseDirectory is not guaranteed to be populated on every host.
    [InlineData("/data/user/0/com.example.app/files/App.dll", "")]
    [InlineData("/data/user/0/com.example.app/files/App.dll", null)]
    public void IsInAppBaseDirectory_ReturnsFalseInsteadOfThrowing(string? location, string? baseDirectory)
    {
        // The contract that matters is "never throw" — action discovery must survive
        // any assembly whose location it cannot make sense of.
        var result = Record.Exception(() => DevFlowAgentService.IsInAppBaseDirectory(location, baseDirectory));

        Assert.Null(result);
        Assert.False(DevFlowAgentService.IsInAppBaseDirectory(location, baseDirectory));
    }

    [Fact]
    public void IsInAppBaseDirectory_MatchesWhenAssemblySitsInTheBaseDirectory()
    {
        var baseDir = Path.GetDirectoryName(typeof(DevFlowAgentService).Assembly.Location);
        Assert.False(string.IsNullOrEmpty(baseDir));

        var assemblyPath = Path.Combine(baseDir!, "Some.Assembly.dll");

        Assert.True(DevFlowAgentService.IsInAppBaseDirectory(assemblyPath, baseDir));
    }

    [Fact]
    public void IsInAppBaseDirectory_IgnoresTrailingSeparatorDifferences()
    {
        var baseDir = Path.GetDirectoryName(typeof(DevFlowAgentService).Assembly.Location)!;
        var assemblyPath = Path.Combine(baseDir, "Some.Assembly.dll");

        Assert.True(DevFlowAgentService.IsInAppBaseDirectory(assemblyPath, baseDir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void IsInAppBaseDirectory_DoesNotMatchADifferentDirectory()
    {
        var baseDir = Path.GetDirectoryName(typeof(DevFlowAgentService).Assembly.Location)!;
        var elsewhere = Path.Combine(baseDir, "plugins", "Other.Assembly.dll");

        Assert.False(DevFlowAgentService.IsInAppBaseDirectory(elsewhere, baseDir));
    }

    [Fact]
    public void IsInAppBaseDirectory_ToleratesMalformedPaths()
    {
        // Not a filesystem path at all; must be rejected rather than thrown on.
        Assert.False(DevFlowAgentService.IsInAppBaseDirectory("::::/not/a/path\0/x.dll", "/tmp"));
    }
}
