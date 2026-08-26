using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Agent-, driver- and schema-side coverage for Tizen platform identity. The Tizen backend itself
/// lives in Redth/Maui.Tizen; what this repository owns is the contract that lets such an agent
/// report <c>Tizen</c> truthfully.
/// </summary>
public class TizenPlatformIdentityTests
{
    // ── Agent-side detection ──────────────────────────────────────────────

    [Fact]
    public void DetectName_HonoursTheDevFlowPlatformOverride()
    {
        // The escape hatch for an out-of-tree agent on a platform detection cannot see: it must
        // win outright so the agent never has to ship as "Unknown" or as another platform.
        var original = Environment.GetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, "Tizen");

            Assert.Equal("Tizen", DevFlowRuntimePlatform.DetectName());
            Assert.Equal(DevFlowPlatform.Tizen, DevFlowPlatform.Normalize(DevFlowRuntimePlatform.DetectName()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, original);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DetectName_IgnoresAnEmptyOverride(string overrideValue)
    {
        var original = Environment.GetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, overrideValue);

            Assert.Equal(ExpectedHostPlatformName(), DevFlowRuntimePlatform.DetectName());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, original);
        }
    }

    [Fact]
    public void DetectName_ReportsTheHostPlatformAndNotTizen()
    {
        Assert.False(DevFlowRuntimePlatform.IsTizen);
        Assert.Equal(ExpectedHostPlatformName(), DevFlowRuntimePlatform.DetectName());
    }

    [Fact]
    public void DetectName_WindowsNameIsSelectableForTheUiFacingAgent()
    {
        // The UI agent reports "WinUI" and host bootstrap reports "Windows"; both must normalize
        // to the same canonical identifier so a filter written against either one works.
        Assert.Equal(DevFlowPlatform.Windows, DevFlowPlatform.Normalize("WinUI"));
        Assert.Equal(DevFlowPlatform.Windows, DevFlowPlatform.Normalize("Windows"));

        if (OperatingSystem.IsWindows())
            Assert.Equal("WinUI", DevFlowRuntimePlatform.DetectName(windowsName: "WinUI"));
    }

    private static string ExpectedHostPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    // ── Driver factory ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Tizen")]
    [InlineData("tizen")]
    [InlineData("tizen-nui")]
    public void AppDriverFactory_TizenIsRecognizedButHasNoLocalDriver(string platform)
    {
        Assert.False(AppDriverFactory.HasLocalDriver(platform));

        var exception = Assert.Throws<PlatformNotSupportedException>(() => AppDriverFactory.Create(platform));

        // The point of the change: an explicit "recognized, but host-side driving is unavailable"
        // instead of the old "Unknown platform" that made Tizen look unsupported outright.
        Assert.Contains("tizen", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown platform", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("maccatalyst")]
    [InlineData("mac")]
    [InlineData("catalyst")]
    [InlineData("android")]
    [InlineData("ios")]
    [InlineData("iossimulator")]
    [InlineData("windows")]
    [InlineData("win")]
    [InlineData("winui")]
    [InlineData("wpf")]
    [InlineData("linux")]
    [InlineData("gtk")]
    public void AppDriverFactory_ExistingPlatformAliasesStillResolve(string platform)
    {
        Assert.True(AppDriverFactory.HasLocalDriver(platform));

        using var driver = AppDriverFactory.Create(platform);
        Assert.NotNull(driver);
    }

    [Fact]
    public void AppDriverFactory_UnknownPlatformStillThrowsArgumentException()
    {
        Assert.False(AppDriverFactory.HasLocalDriver("nintendo64"));
        Assert.Throws<ArgumentException>(() => AppDriverFactory.Create("nintendo64"));
    }

    // ── Protocol schema ───────────────────────────────────────────────────

    [Fact]
    public void AgentStatusSchema_PlatformEnumIsAdditiveAndIncludesTizen()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(FindSpecRoot(), "schemas", "agent-status.json")))!;
        var values = schema["properties"]!["platform"]!["enum"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        Assert.Contains(DevFlowPlatform.Tizen, values);

        // Additive only: dropping a previously published identifier would break shipped clients.
        foreach (var expected in new[] { "ios", "android", "maccatalyst", "windows", "linux", "macos" })
            Assert.Contains(expected, values);

        // Every identifier the schema advertises must be one the client can normalize to itself.
        foreach (var value in values)
        {
            Assert.Equal(value, DevFlowPlatform.Normalize(value));
            Assert.True(DevFlowPlatform.IsKnown(value), value);
        }

        // …and vice versa, so the schema and the client cannot drift apart.
        Assert.Equal(
            DevFlowPlatform.KnownIds.OrderBy(id => id, StringComparer.Ordinal),
            values.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void AgentStatusExample_RemainsValidAgainstTheWidenedEnum()
    {
        var examplePath = Path.Combine(FindSpecRoot(), "examples", "agent-status-response.json");
        using var document = JsonDocument.Parse(File.ReadAllText(examplePath));

        var platform = document.RootElement.GetProperty("platform").GetString();

        Assert.False(string.IsNullOrWhiteSpace(platform));
        Assert.True(DevFlowPlatform.IsKnown(platform), platform);
    }

    private static string FindSpecRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "DevFlow", "spec");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find docs/DevFlow/spec from the test output directory.");
    }
}
