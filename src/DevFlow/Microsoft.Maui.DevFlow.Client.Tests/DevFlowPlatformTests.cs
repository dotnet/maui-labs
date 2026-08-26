using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Contract tests for <see cref="DevFlowPlatform"/>. Platform identity is part of the public wire
/// contract, so these run against both the portable (netstandard2.0) and modern client builds.
/// </summary>
public class DevFlowPlatformTests
{
    [Theory]
    [InlineData("Tizen", DevFlowPlatform.Tizen)]
    [InlineData("tizen", DevFlowPlatform.Tizen)]
    [InlineData("TIZEN", DevFlowPlatform.Tizen)]
    [InlineData("tizen-nui", DevFlowPlatform.Tizen)]
    [InlineData("Tizen 8.0", DevFlowPlatform.Tizen)]
    [InlineData("Android", DevFlowPlatform.Android)]
    [InlineData("iOS", DevFlowPlatform.iOS)]
    [InlineData("MacCatalyst", DevFlowPlatform.MacCatalyst)]
    [InlineData("Mac Catalyst", DevFlowPlatform.MacCatalyst)]
    [InlineData("macOS", DevFlowPlatform.MacOS)]
    [InlineData("WinUI", DevFlowPlatform.Windows)]
    [InlineData("WPF", DevFlowPlatform.Windows)]
    [InlineData("Linux", DevFlowPlatform.Linux)]
    [InlineData("gtk", DevFlowPlatform.Linux)]
    public void Normalize_MapsAgentSpellingsToCanonicalIds(string reported, string expected)
        => Assert.Equal(expected, DevFlowPlatform.Normalize(reported));

    [Fact]
    public void Normalize_TizenIsNotMistakenForLinux()
    {
        // Tizen is a Linux distribution, so an ordering mistake anywhere in the stack silently
        // reports Tizen agents as Linux — the exact failure this contract exists to prevent.
        Assert.Equal(DevFlowPlatform.Tizen, DevFlowPlatform.Normalize("Tizen"));
        Assert.NotEqual(DevFlowPlatform.Linux, DevFlowPlatform.Normalize("Tizen"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_MissingPlatformIsUnknown(string? reported)
        => Assert.Equal(DevFlowPlatform.Unknown, DevFlowPlatform.Normalize(reported));

    [Fact]
    public void Normalize_UnrecognizedPlatformRoundTripsInsteadOfBeingCoerced()
    {
        // Backward/forward compatibility: an older client must stay usable against an agent on a
        // platform it has never heard of, and must not silently claim it is something else.
        Assert.Equal("webassembly", DevFlowPlatform.Normalize("WebAssembly"));
        Assert.False(DevFlowPlatform.IsKnown("WebAssembly"));
    }

    [Fact]
    public void IsKnown_RecognizesTizenAndEveryShippedPlatform()
    {
        Assert.True(DevFlowPlatform.IsKnown("Tizen"));

        foreach (var id in DevFlowPlatform.KnownIds)
            Assert.True(DevFlowPlatform.IsKnown(id), id);
    }

    [Fact]
    public void KnownIds_ContainsTizenAndDoesNotDropExistingPlatforms()
    {
        Assert.Contains(DevFlowPlatform.Tizen, DevFlowPlatform.KnownIds);

        // Additive-only: removing any of these would break already-shipped clients.
        Assert.Contains(DevFlowPlatform.Android, DevFlowPlatform.KnownIds);
        Assert.Contains(DevFlowPlatform.iOS, DevFlowPlatform.KnownIds);
        Assert.Contains(DevFlowPlatform.MacCatalyst, DevFlowPlatform.KnownIds);
        Assert.Contains(DevFlowPlatform.Windows, DevFlowPlatform.KnownIds);
        Assert.Contains(DevFlowPlatform.Linux, DevFlowPlatform.KnownIds);
        Assert.Contains(DevFlowPlatform.MacOS, DevFlowPlatform.KnownIds);
    }

    [Theory]
    [InlineData("Tizen", "Tizen")]
    [InlineData("tizen", "Tizen")]
    [InlineData("WinUI", "Windows")]
    [InlineData("MacCatalyst", "Mac Catalyst")]
    [InlineData("WebAssembly", "WebAssembly")]
    public void GetDisplayName_UsesCanonicalNameAndFallsBackToReportedValue(string reported, string expected)
        => Assert.Equal(expected, DevFlowPlatform.GetDisplayName(reported));

    [Theory]
    [InlineData("Tizen", "tizen")]
    [InlineData("Tizen", "Tizen")]
    [InlineData("Tizen", "tizen-nui")]
    [InlineData("Tizen", "tiz")]
    [InlineData("WinUI", "windows")]
    [InlineData("macOS", "mac")]
    public void Matches_FilterMatchesAgentAcrossSpellings(string agentPlatform, string filter)
        => Assert.True(DevFlowPlatform.Matches(agentPlatform, filter));

    [Theory]
    [InlineData("Tizen", "android")]
    [InlineData("Tizen", "linux")]
    [InlineData("Linux", "tizen")]
    public void Matches_FilterRejectsOtherPlatforms(string agentPlatform, string filter)
        => Assert.False(DevFlowPlatform.Matches(agentPlatform, filter));

    [Fact]
    public void Matches_EmptyFilterMatchesEverything()
    {
        Assert.True(DevFlowPlatform.Matches("Tizen", null));
        Assert.True(DevFlowPlatform.Matches("Tizen", "  "));
    }

    [Fact]
    public void AgentStatus_RoundTripsTizenAndExposesCanonicalPlatformId()
    {
        const string json = """
            {
              "agent": { "name": "Microsoft.Maui.DevFlow.Agent", "version": "0.1.0", "framework": ".NET MAUI", "frameworkId": "maui", "uiFramework": "maui-controls" },
              "device": { "platform": "Tizen", "deviceType": "Virtual", "idiom": "TV" },
              "app": { "name": "TizenSample", "packageId": "org.tizen.example" },
              "running": true
            }
            """;

        var status = JsonSerializer.Deserialize<AgentStatus>(json);

        Assert.NotNull(status);
        Assert.Equal("Tizen", status!.Platform);
        Assert.Equal(DevFlowPlatform.Tizen, status.PlatformId);
        Assert.True(DevFlowPlatform.IsKnown(status.Platform));

        // The wire value is preserved verbatim: normalization is a client-side read concern and
        // must never rewrite what the agent reported.
        var reserialized = JsonSerializer.Serialize(status);
        using var document = JsonDocument.Parse(reserialized);
        Assert.Equal(
            "Tizen",
            document.RootElement.GetProperty("device").GetProperty("platform").GetString());
    }

    [Fact]
    public void AgentStatus_UnknownPlatformStillDeserializes()
    {
        const string json = """
            {
              "device": { "platform": "SomeFuturePlatform" },
              "running": true
            }
            """;

        var status = JsonSerializer.Deserialize<AgentStatus>(json);

        Assert.NotNull(status);
        Assert.Equal("SomeFuturePlatform", status!.Platform);
        Assert.Equal("somefutureplatform", status.PlatformId);
    }

    [Fact]
    public void AgentStatus_MissingDeviceReportsUnknownPlatform()
    {
        var status = JsonSerializer.Deserialize<AgentStatus>("""{ "running": true }""");

        Assert.NotNull(status);
        Assert.Null(status!.Platform);
        Assert.Equal(DevFlowPlatform.Unknown, status.PlatformId);
    }
}
