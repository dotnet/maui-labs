using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The device layer is optional, and the MCP surface has to say so.
/// <para>
/// The companion that backs these tools is a separately installed, experimental binary that neither
/// the CLI nor the VS Code extension ships. Advertising four device tools to every session would
/// offer a capability that is usually absent, and every tool in an inventory costs the model some
/// of its attention on the tools that do work. VS Code gates the same surface behind
/// <c>mauiDevflow.registerMobileCanvasMcpServer</c>, off by default; this is the CLI half of that
/// one decision, not a second policy.
/// </para>
/// </summary>
public class MobileCanvasMcpOptInTests
{
    private static readonly string[] DeviceTools =
    [
        "maui_device_boot",
        "maui_device_list",
        "maui_device_shutdown",
        "maui_device_tap",
    ];

    private static MauiPreviewFeatureFlags Flags(bool mobileCanvas, string? killSwitches = null)
        => MauiPreviewFeatureFlagConfiguration.FromEnvironment(name => name switch
        {
            "DEVFLOW_PREVIEW_MOBILE_CANVAS" => mobileCanvas ? "true" : null,
            "DEVFLOW_PREVIEW_KILL_SWITCHES" => killSwitches,
            _ => null,
        });

    [Fact]
    public void FullProfile_DoesNotAdvertiseTheDeviceLayerByDefault()
    {
        var inventory = McpServerHost.GetToolInventory(
            McpServerProfile.Full,
            MauiPreviewFeatureFlags.CreateDefault());

        Assert.NotEmpty(inventory);
        foreach (var tool in DeviceTools)
            Assert.DoesNotContain(tool, inventory);
    }

    [Fact]
    public void FullProfile_AdvertisesTheDeviceLayerOnlyWhenTheGateIsOn()
    {
        var off = McpServerHost.GetToolInventory(McpServerProfile.Full, Flags(mobileCanvas: false));
        var on = McpServerHost.GetToolInventory(McpServerProfile.Full, Flags(mobileCanvas: true));

        foreach (var tool in DeviceTools)
        {
            Assert.DoesNotContain(tool, off);
            Assert.Contains(tool, on);
        }

        // The gate adds the device layer and changes nothing else: it is not a licence to widen the
        // profile generally.
        Assert.Equal(off.Concat(DeviceTools).Order(StringComparer.Ordinal), on.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheKillSwitchOverridesTheGate()
    {
        var inventory = McpServerHost.GetToolInventory(
            McpServerProfile.Full,
            Flags(mobileCanvas: true, killSwitches: "mobile-canvas"));

        foreach (var tool in DeviceTools)
            Assert.DoesNotContain(tool, inventory);
    }

    [Fact]
    public void TheGateNeverTouchesTheRestrictedTestAgentProfile()
    {
        var authoring = MauiPreviewFeatureFlagConfiguration.FromEnvironment(name => name switch
        {
            "DEVFLOW_PREVIEW_AGENT_AUTHORING" => "true",
            "DEVFLOW_PREVIEW_MOBILE_CANVAS" => "true",
            _ => null,
        });

        var inventory = McpServerHost.GetToolInventory(McpServerProfile.TestAgent, authoring);

        Assert.Equal(14, inventory.Count);
        foreach (var tool in DeviceTools)
            Assert.DoesNotContain(tool, inventory);
    }

    /// <summary>
    /// The published inventory is a hand-maintained list, so it proves only that the list did not
    /// change. This ties it to the tool names actually declared on <see cref="DeviceTools"/>.
    /// </summary>
    [Fact]
    public void TheAdvertisedDeviceToolsAreExactlyTheOnesDeviceToolsDeclares()
    {
        var declared = typeof(Microsoft.Maui.Cli.DevFlow.Mcp.Tools.DeviceTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(static method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(static attribute => attribute is not null)
            .Select(static attribute => attribute!.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DeviceTools.OrderBy(static name => name, StringComparer.Ordinal), declared);
    }

    /// <summary>
    /// Reflection cannot see the registration itself. The full-profile branch must register
    /// <c>DeviceTools</c> only inside the gate, or the served surface would silently disagree with
    /// the advertised inventory.
    /// </summary>
    [Fact]
    public void TheFullProfileRegistersDeviceToolsOnlyBehindTheGate()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "McpServerHost.cs"));

        var start = source.IndexOf("else if (profile == McpServerProfile.Full)", StringComparison.Ordinal);
        Assert.True(start >= 0, "The full-profile registration branch is missing.");
        var branch = source[start..];

        var gate = branch.IndexOf("IsMobileCanvasEnabled(previewFlags)", StringComparison.Ordinal);
        var registration = branch.IndexOf(".WithTools<DeviceTools>()", StringComparison.Ordinal);
        Assert.True(gate >= 0, "The full profile no longer gates the device layer.");
        Assert.True(registration > gate, "DeviceTools is registered before the gate is consulted.");
        Assert.Single(Regex.Matches(branch, @"\.WithTools<DeviceTools>\(\)"));
    }

    /// <summary>
    /// Both surfaces are off by default and say so in the same terms. A reader who turns one on has
    /// to be able to find the other.
    /// </summary>
    [Fact]
    public void TheVsCodeSettingAndTheCliGateAreBothDefaultOff()
    {
        var package = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "DevFlow", "js", "vscode-inspector", "package.json"));

        Assert.Contains("mauiDevflow.registerMobileCanvasMcpServer", package, StringComparison.Ordinal);
        Assert.Equal("DEVFLOW_PREVIEW_MOBILE_CANVAS", McpServerHost.PreviewMobileCanvasVariable);
        Assert.False(MauiPreviewFeatureFlags.CreateDefault().MobileCanvasEnabled);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
