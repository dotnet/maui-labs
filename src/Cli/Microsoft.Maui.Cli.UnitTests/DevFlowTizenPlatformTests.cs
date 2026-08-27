using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Tizen is the first platform DevFlow recognizes without owning the agent implementation: the
/// backend lives in Redth/Maui.Tizen. These tests pin the CLI-side half of that contract —
/// registration, filtering and display — so a Tizen agent never has to impersonate another
/// platform to be usable.
/// </summary>
[Collection("CLI")]
public class DevFlowTizenPlatformTests
{
    private static AgentRegistration TizenAgent(string platform = "Tizen") => new()
    {
        Id = "tizen-agent",
        Project = "/src/TizenApp.csproj",
        Tfm = "net10.0-tizen",
        Platform = platform,
        AppName = "TizenApp",
        Framework = "maui",
        UiFramework = "tizen-nui",
        Port = 9223,
        Version = "0.1.0-preview",
        ConnectedAt = DateTime.UnixEpoch
    };

    private static AgentRegistration AndroidAgent() => new()
    {
        Id = "android-agent",
        Project = "/src/AndroidApp.csproj",
        Tfm = "net10.0-android",
        Platform = "Android",
        AppName = "AndroidApp",
        Port = 9224,
        ConnectedAt = DateTime.UnixEpoch
    };

    [Theory]
    [InlineData("tizen")]
    [InlineData("Tizen")]
    [InlineData("TIZEN")]
    [InlineData("tizen-nui")]
    public void FindMatchingAgent_MatchesTizenAgentAcrossFilterSpellings(string platformFilter)
    {
        var match = DevFlowCommands.FindMatchingAgent([AndroidAgent(), TizenAgent()], null, platformFilter);

        Assert.NotNull(match);
        Assert.Equal("tizen-agent", match!.Id);
    }

    [Theory]
    [InlineData("android")]
    [InlineData("linux")]
    [InlineData("ios")]
    public void FindMatchingAgent_DoesNotMatchTizenAgentForOtherPlatforms(string platformFilter)
    {
        // "linux" matters most: Tizen is a Linux distribution, so a sloppy match would hand a
        // Tizen agent to a caller that asked for the GTK backend.
        var match = DevFlowCommands.FindMatchingAgent([TizenAgent()], null, platformFilter);

        Assert.Null(match);
    }

    [Fact]
    public void FindMatchingAgent_TizenFilterDoesNotMatchLinuxAgent()
    {
        var linuxAgent = new AgentRegistration
        {
            Id = "linux-agent",
            Project = "/src/GtkApp.csproj",
            Tfm = "net10.0",
            Platform = "Linux",
            AppName = "GtkApp",
            Port = 9225,
            ConnectedAt = DateTime.UnixEpoch
        };

        Assert.Null(DevFlowCommands.FindMatchingAgent([linuxAgent], null, "tizen"));
    }

    [Fact]
    public void FindMatchingAgent_ExistingPlatformFiltersStillBehaveTheSame()
    {
        AgentRegistration[] agents = [AndroidAgent(), TizenAgent()];

        Assert.Equal("android-agent", DevFlowCommands.FindMatchingAgent(agents, null, "Android")!.Id);
        Assert.Equal("android-agent", DevFlowCommands.FindMatchingAgent(agents, null, "andro")!.Id);
        Assert.Equal("android-agent", DevFlowCommands.FindMatchingAgent(agents, null, null)!.Id);
    }

    [Fact]
    public void FindMatchingAgent_UnknownPlatformAgentIsStillDiscoverable()
    {
        var futureAgent = new AgentRegistration
        {
            Id = "future-agent",
            Project = "/src/FutureApp.csproj",
            Tfm = "net10.0-futureos",
            Platform = "FutureOS",
            AppName = "FutureApp",
            Port = 9226,
            ConnectedAt = DateTime.UnixEpoch
        };

        Assert.Equal("future-agent", DevFlowCommands.FindMatchingAgent([futureAgent], null, "futureos")!.Id);
        Assert.Null(DevFlowCommands.FindMatchingAgent([futureAgent], null, "tizen"));
    }

    [Theory]
    [InlineData("KaiOS", "ios")]
    [InlineData("BIOS", "ios")]
    [InlineData("NotAndroid", "android")]
    public void FindMatchingAgent_KnownFilterDoesNotMatchUnknownEmbeddedToken(string reportedPlatform, string platformFilter)
    {
        var agent = TizenAgent(platform: reportedPlatform);

        Assert.Null(DevFlowCommands.FindMatchingAgent([agent], null, platformFilter));
    }

    [Fact]
    public async Task DiagnoseJson_WhenTizenAgentIsRegistered_ReportsItWithoutAndroidProbing()
    {
        var cli = new CliTestHarness(mockAgentPort: 9223);
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-tizen-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>([TizenAgent()]);
        DevFlowCommands.IsAndroidAdbLikelyAvailable = () => throw new InvalidOperationException("A Tizen agent must not trigger Android probing.");
        DevFlowCommands.CreateAndroidPortForwarder = () => throw new InvalidOperationException("A Tizen agent must not create an Android port forwarder.");

        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeRawAsync("devflow", "diagnose", "--json");

            Assert.Equal(0, result.ExitCode);

            var json = result.ParseJsonOutput();
            Assert.Equal(1, json.GetProperty("agent_count").GetInt32());

            var agent = Assert.Single(json.GetProperty("agents").EnumerateArray());
            Assert.Equal("tizen-agent", agent.GetProperty("id").GetString());
            Assert.Equal("TizenApp", agent.GetProperty("appName").GetString());

            // The registration must round-trip the agent's own spelling — normalization is a read
            // concern, never a rewrite of what the agent reported.
            Assert.Equal("Tizen", agent.GetProperty("platform").GetString());
            Assert.Equal("net10.0-tizen", agent.GetProperty("tfm").GetString());

            Assert.Equal(JsonValueKind.Undefined, GetDiagnosticsAndroid(json));
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
    }

    private static JsonValueKind GetDiagnosticsAndroid(JsonElement json)
        => json.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.TryGetProperty("android", out var android)
            ? android.ValueKind
            : JsonValueKind.Undefined;

    // ── Command guards ────────────────────────────────────────────────────

    [Theory]
    [InlineData("tizen")]
    [InlineData("Tizen")]
    public void AlertCommands_RejectTizenExplicitlyInsteadOfFallingThroughToIos(string platform)
    {
        // `maui devflow ui alert` resolves a platform and then picks a host-side driver, with the
        // iOS simulator as the final else-branch. Without this guard a Tizen agent would be handed
        // to the iOS simulator driver and fail with an unrelated xcrun error.
        var exception = Assert.Throws<InvalidOperationException>(
            () => DevFlowCommands.ThrowIfAlertPlatformUnsupported(platform));

        Assert.Contains("not available for platform", exception.Message);
        Assert.Contains(platform, exception.Message);
    }

    [Theory]
    [InlineData("maccatalyst")]
    [InlineData("android")]
    [InlineData("ios")]
    [InlineData("ios-simulator")]
    [InlineData("windows")]
    [InlineData("linux")]
    public void AlertCommands_StillAcceptEveryPlatformWithAHostDriver(string platform)
        => DevFlowCommands.ThrowIfAlertPlatformUnsupported(platform);

    [Theory]
    [InlineData("macOS", "macos")]
    [InlineData("SomeFuturePlatform", "somefutureplatform")]
    [InlineData("KaiOS", "kaios")]
    [InlineData("Tizen (Linux) 8.0", "tizen")]
    public void AlertRouting_PreservesExplicitDriverlessAndUnknownIdentity(string reported, string expected)
    {
        var resolved = DevFlowCommands.ResolveExplicitAlertPlatform(reported);

        Assert.Equal(expected, resolved);
        var exception = Assert.Throws<InvalidOperationException>(
            () => DevFlowCommands.ThrowIfAlertPlatformUnsupported(resolved!));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MacCatalyst")]
    [InlineData("mac catalyst")]
    public void AlertRouting_UsesFallbackOnlyWhenIdentityIsAbsentOrMacCatalyst(string? reported)
        => Assert.Null(DevFlowCommands.ResolveExplicitAlertPlatform(reported));

    [Theory]
    [InlineData("iOS", "ios-simulator")]
    [InlineData("Android", "android")]
    [InlineData("WinUI", "windows")]
    [InlineData("Linux", "linux")]
    public void AlertRouting_MapsExplicitSupportedIdentityToItsDriver(string reported, string expected)
        => Assert.Equal(expected, DevFlowCommands.ResolveExplicitAlertPlatform(reported));

    // ── MCP tool surface ──────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(AgentTools.ListAgents))]
    [InlineData(nameof(AgentTools.Wait))]
    public void AgentTools_ExposeADescribedOptionalPlatformParameter(string methodName)
    {
        var method = typeof(AgentTools).GetMethod(methodName);
        Assert.NotNull(method);

        var parameter = method!.GetParameters().SingleOrDefault(p => p.Name == "platform");
        Assert.NotNull(parameter);

        // Optional and last, so existing MCP callers keep working unchanged.
        Assert.True(parameter!.IsOptional);
        Assert.Null(parameter.DefaultValue);
        Assert.Equal(method.GetParameters().Length - 1, parameter.Position);

        var description = parameter.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        Assert.Contains("tizen", description!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentRegistration_TizenAgentIsNotTreatedAsAnAndroidAgent()
    {
        // Android agents get adb forward/reverse plumbing. A Tizen agent must never trip it —
        // neither its platform name nor its TFM may be mistaken for Android.
        var tizen = TizenAgent();

        Assert.DoesNotContain("android", tizen.Platform, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-android", tizen.Tfm, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(DevFlowPlatform.Android, DevFlowPlatform.Normalize(tizen.Platform));
        Assert.NotEqual(DevFlowPlatform.Linux, DevFlowPlatform.Normalize(tizen.Platform));
    }

    [Theory]
    [InlineData("NotAndroid", "net10.0-notandroid")]
    [InlineData("KaiOS", "net10.0-kaios")]
    public void AndroidHostRouting_DoesNotSelectUnknownEmbeddedToken(string platform, string tfm)
    {
        var agent = TizenAgent(platform) with { Tfm = tfm };

        Assert.False(DevFlowCommands.IsAndroidAgent(agent));
        Assert.False(McpAgentSession.IsAndroidAgent(agent));
        Assert.False(DevFlowCommands.ShouldPrepareAndroidBrokerReverse(platform));
    }

    [Theory]
    [InlineData("KaiOS")]
    [InlineData("BIOS")]
    public void ScreenshotHostRouting_DoesNotSelectSimctlForUnknownEmbeddedToken(string platform)
        => Assert.False(DevFlowCommands.ShouldTrySimctlScreenshot(platform));

    [Theory]
    [InlineData("Android", "net10.0")]
    [InlineData("Unknown", "net10.0-android")]
    public void AndroidHostRouting_StillRecognizesCanonicalPlatformOrTfm(string platform, string tfm)
    {
        var agent = TizenAgent(platform) with { Tfm = tfm };

        Assert.True(DevFlowCommands.IsAndroidAgent(agent));
        Assert.True(McpAgentSession.IsAndroidAgent(agent));
    }

    [Fact]
    public void FindMatchingAgent_LinuxFilterDoesNotMatchADecoratedTizenAgent()
    {
        // A Tizen agent may legitimately describe itself with the word "Linux". Discovery must
        // still route it to Tizen and keep it away from callers asking for the GTK backend.
        var decorated = TizenAgent(platform: "Tizen (Linux) 8.0");

        Assert.Null(DevFlowCommands.FindMatchingAgent([decorated], null, "linux"));
        Assert.Equal("tizen-agent", DevFlowCommands.FindMatchingAgent([decorated], null, "tizen")!.Id);
    }

    [Fact]
    public void AgentRegistration_TizenPlatformSerializesAndNormalizes()
    {
        var registration = TizenAgent();
        var serialized = JsonSerializer.Serialize(registration);

        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("Tizen", document.RootElement.GetProperty("platform").GetString());

        var deserialized = JsonSerializer.Deserialize<AgentRegistration>(serialized);
        Assert.NotNull(deserialized);
        Assert.Equal("Tizen", deserialized!.Platform);
        Assert.Equal(DevFlowPlatform.Tizen, DevFlowPlatform.Normalize(deserialized.Platform));
        Assert.Equal("Tizen", DevFlowPlatform.GetDisplayName(deserialized.Platform));
    }
}
