using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Guards how <see cref="TvOSSimulatorFixture"/> picks an Apple TV simulator and builds the sample.
/// Pure parsing over synthetic <c>simctl</c> output, so these need no simulator or Xcode.
/// </summary>
public class TvOSSimulatorSelectionTests
{
    static string BuildSimctlJson(params (string Runtime, (string Name, string Udid, string State, bool IsAvailable)[] Devices)[] runtimes)
        => JsonSerializer.Serialize(new
        {
            devices = runtimes.ToDictionary(
                runtime => runtime.Runtime,
                runtime => runtime.Devices.Select(device => new Dictionary<string, object?>
                {
                    ["name"] = device.Name,
                    ["udid"] = device.Udid,
                    ["state"] = device.State,
                    ["isAvailable"] = device.IsAvailable,
                    ["deviceTypeIdentifier"] = "com.apple.CoreSimulator.SimDeviceType." + device.Name.Replace(' ', '-'),
                }).ToArray())
        });

    [Fact]
    public void ParseAppleTVCandidates_IgnoresOtherRuntimesAndUnavailableDevices()
    {
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-27-2", [("iPhone 17", "PHONE", "Booted", true)]),
            ("com.apple.CoreSimulator.SimRuntime.tvOS-27-2",
            [
                ("Apple TV 4K (3rd generation)", "TV-4K", "Shutdown", true),
                ("Apple TV", "TV-GONE", "Shutdown", false),
            ]));

        var candidate = Assert.Single(TvOSSimulatorFixture.ParseAppleTVCandidates(json));

        Assert.Equal("TV-4K", candidate.Udid);
        Assert.Equal("27.2", candidate.Runtime);
    }

    [Fact]
    public void SelectBestDevice_PrefersNewestRuntimeThen4K()
    {
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.tvOS-26-0", [("Apple TV 4K (3rd generation)", "OLD-4K", "Shutdown", true)]),
            ("com.apple.CoreSimulator.SimRuntime.tvOS-27-2",
            [
                ("Apple TV", "NEW-HD", "Shutdown", true),
                ("Apple TV 4K (3rd generation)", "NEW-4K", "Shutdown", true),
            ]));

        var selected = TvOSSimulatorFixture.SelectBestDevice(TvOSSimulatorFixture.ParseAppleTVCandidates(json));

        Assert.Equal("NEW-4K", selected.Udid);
    }

    [Fact]
    public void CreateBuildArguments_UsesPackageBackendByDefault()
    {
        var arguments = TvOSSimulatorFixture.CreateBuildArguments("tvossimulator-arm64", backendProject: null);

        Assert.Equal("-p:RuntimeIdentifier=tvossimulator-arm64", arguments);
    }

    [Fact]
    public void CreateBuildArguments_AddsBackendPackageSource()
    {
        var arguments = TvOSSimulatorFixture.CreateBuildArguments(
            "tvossimulator-arm64",
            backendProject: null,
            backendSource: "https://api.nuget.org/v3/index.json");

        Assert.Equal(
            "-p:RuntimeIdentifier=tvossimulator-arm64 \"-p:RestoreAdditionalProjectSources=https://api.nuget.org/v3/index.json\"",
            arguments);
    }

    [Fact]
    public void CreateBuildArguments_IgnoresPackageSourceForLocalBackend()
    {
        var project = Path.Combine(Path.GetTempPath(), "mauiplatforms", "Platform.Maui.TvOS.csproj");

        var arguments = TvOSSimulatorFixture.CreateBuildArguments("tvossimulator-arm64", project, "https://example.invalid/index.json");

        Assert.DoesNotContain("RestoreAdditionalProjectSources", arguments);
    }

    [Fact]
    public void CreateBuildArguments_BuildsLocalBackendForNet11()
    {
        var project = Path.Combine(Path.GetTempPath(), "mauiplatforms", "Platform.Maui.TvOS.csproj");

        var arguments = TvOSSimulatorFixture.CreateBuildArguments("tvossimulator-x64", project);

        Assert.Contains("-p:RuntimeIdentifier=tvossimulator-x64", arguments);
        Assert.Contains($"\"-p:PlatformMauiTvOSProject={project}\"", arguments);
        Assert.Contains("-p:BaseTargetFramework=net11.0", arguments);
        Assert.Contains("-p:MauiVersion=11.0.10", arguments);
    }
}
