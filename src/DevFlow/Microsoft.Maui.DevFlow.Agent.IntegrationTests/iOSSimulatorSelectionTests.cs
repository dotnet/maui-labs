using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Guards how <see cref="iOSSimulatorFixture"/> picks an iPhone simulator out of
/// <c>xcrun simctl list devices --json</c>.
/// </summary>
/// <remarks>
/// A booted iPhone simulator whose user-visible name has been customized (e.g. renamed to
/// "GDUI-Test" in Simulator.app or via <c>simctl rename</c>) used to be silently excluded, because
/// selection checked <c>name.Contains("iPhone")</c> even though simctl's own
/// <c>deviceTypeIdentifier</c> unambiguously identifies the device as an iPhone. The fixture would
/// then boot or pick a different, unintended simulator instead of using the one already running —
/// and nothing failed loudly, so this went unnoticed until it was hit locally. These are pure JSON
/// parsing/selection tests over synthetic <c>simctl</c> output, so they need no simulator, no Xcode
/// install, and no device, and run on every axis (device-free fixture-selection tests, matching
/// <see cref="AppBundleSelectionTests"/>).
/// </remarks>
public class iOSSimulatorSelectionTests
{
    static string BuildSimctlJson(params (string Runtime, (string Name, string Udid, string State, string? DeviceTypeIdentifier, bool IsAvailable)[] Devices)[] runtimes)
    {
        var devicesObject = new Dictionary<string, object>();
        foreach (var (runtime, devices) in runtimes)
        {
            devicesObject[runtime] = devices.Select(d => new Dictionary<string, object?>
            {
                ["name"] = d.Name,
                ["udid"] = d.Udid,
                ["state"] = d.State,
                ["isAvailable"] = d.IsAvailable,
                ["deviceTypeIdentifier"] = d.DeviceTypeIdentifier,
            }).ToArray();
        }

        return JsonSerializer.Serialize(new { devices = devicesObject });
    }

    [Fact]
    public void ParseIPhoneCandidates_IncludesARenamedIPhone_ByDeviceTypeIdentifier()
    {
        // The whole point of this fixture: a user-renamed iPhone must still be found, because
        // deviceTypeIdentifier — not the display name — is what actually says "this is an iPhone".
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-17-5",
            [
                ("GDUI-Test", "AAAA-1111", "Booted", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", true),
            ]));

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        var candidate = Assert.Single(candidates);
        Assert.Equal("AAAA-1111", candidate.Udid);
        Assert.Equal("GDUI-Test", candidate.Name);
        Assert.Equal("Booted", candidate.State);
    }

    [Fact]
    public void ParseIPhoneCandidates_ExcludesIPad()
    {
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-17-5",
            [
                ("iPad Pro (11-inch) (4th generation)", "BBBB-2222", "Shutdown",
                    "com.apple.CoreSimulator.SimDeviceType.iPad-Pro-11-inch-4th-generation", true),
            ]));

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ParseIPhoneCandidates_ExcludesNonPhoneDeviceTypes_EvenWithIPhoneLikeNames()
    {
        // Apple Watch and Apple TV runtimes never appear under an "iOS" runtime key in practice,
        // but the device-type check on its own — independent of the runtime filter — must still
        // reject anything whose deviceTypeIdentifier is not an iPhone.
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-17-5",
            [
                ("iPhone-ish Watch", "CCCC-3333", "Shutdown",
                    "com.apple.CoreSimulator.SimDeviceType.Apple-Watch-Series-9-45mm", true),
                ("My iPhone TV", "DDDD-4444", "Shutdown",
                    "com.apple.CoreSimulator.SimDeviceType.Apple-TV-4K-3rd-generation-4K", true),
            ]));

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ParseIPhoneCandidates_ExcludesUnavailableDevices()
    {
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-17-5",
            [
                ("iPhone 15", "EEEE-5555", "Shutdown",
                    "com.apple.CoreSimulator.SimDeviceType.iPhone-15", false),
            ]));

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ParseIPhoneCandidates_FiltersByIosVersionPattern()
    {
        var json = BuildSimctlJson(
            ("com.apple.CoreSimulator.SimRuntime.iOS-16-4",
            [
                ("iPhone 14", "FFFF-6666", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-14", true),
            ]),
            ("com.apple.CoreSimulator.SimRuntime.iOS-17-5",
            [
                ("iPhone 15", "GGGG-7777", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", true),
            ]));

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: "17.x");

        var candidate = Assert.Single(candidates);
        Assert.Equal("GGGG-7777", candidate.Udid);
    }

    [Fact]
    public void ParseIPhoneCandidates_OlderSimctlJsonWithoutDeviceTypeIdentifier_FallsBackToName()
    {
        // Older simctl versions never emitted deviceTypeIdentifier at all. The fallback keeps the
        // fixture working there — at the cost of the exact bug this class exists to fix, which is
        // an acceptable trade-off for JSON that will never describe a renamed device correctly.
        var json = """
            {
              "devices": {
                "com.apple.CoreSimulator.SimRuntime.iOS-14-5": [
                  { "name": "iPhone 11", "udid": "HHHH-8888", "state": "Shutdown" }
                ]
              }
            }
            """;

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        var candidate = Assert.Single(candidates);
        Assert.Equal("HHHH-8888", candidate.Udid);
        Assert.Null(candidate.DeviceTypeIdentifier);
    }

    [Fact]
    public void ParseIPhoneCandidates_OlderSimctlJsonWithoutDeviceTypeIdentifier_StillExcludesIPad()
    {
        var json = """
            {
              "devices": {
                "com.apple.CoreSimulator.SimRuntime.iOS-14-5": [
                  { "name": "iPad Air", "udid": "IIII-9999", "state": "Shutdown" }
                ]
              }
            }
            """;

        var candidates = iOSSimulatorFixture.ParseIPhoneCandidates(json, versionPattern: null);

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData("com.apple.CoreSimulator.SimDeviceType.iPhone-15-Pro-Max", "Anything", true)]
    [InlineData("com.apple.CoreSimulator.SimDeviceType.iPhone-SE-3rd-generation", "GDUI-Test", true)]
    [InlineData("com.apple.CoreSimulator.SimDeviceType.iPad-Pro-11-inch-4th-generation", "Anything", false)]
    [InlineData("com.apple.CoreSimulator.SimDeviceType.iPad-Pro-11-inch-4th-generation", "iPhone Test Device", false)]
    [InlineData("com.apple.CoreSimulator.SimDeviceType.Apple-Watch-Series-9-45mm", "Anything", false)]
    [InlineData(null, "iPhone 15", true)]
    [InlineData(null, "GDUI-Test", false)]
    [InlineData(null, "iPad Air", false)]
    public void IsIPhoneDeviceType_ClassifiesByIdentifierFirstThenNameFallback(
        string? deviceTypeIdentifier, string name, bool expected)
        => Assert.Equal(expected, iOSSimulatorFixture.IsIPhoneDeviceType(deviceTypeIdentifier, name));

    [Fact]
    public void SelectBestDevice_PrefersHighestIPhoneModelNumber()
    {
        var candidates = new List<SimulatorDeviceCandidate>
        {
            new("A", "iPhone 13", "16.4", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-13"),
            new("B", "iPhone 15", "17.5", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-15"),
            new("C", "iPhone 14", "17.0", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-14"),
        };

        var best = iOSSimulatorFixture.SelectBestDevice(candidates);

        Assert.Equal("B", best.Udid);
    }

    [Fact]
    public void SelectBestDevice_ARenamedIPhoneWithNoModelNumberInName_IsStillSelectable()
    {
        // A rename that strips the model number out of the name (e.g. "GDUI-Test") is a real,
        // supported outcome of this fixture: the device must still be selectable when it is the
        // only candidate, even though ExtractIPhoneModelNumber cannot read a number from its name.
        var candidates = new List<SimulatorDeviceCandidate>
        {
            new("Z", "GDUI-Test", "17.5", "Booted", "com.apple.CoreSimulator.SimDeviceType.iPhone-15"),
        };

        var best = iOSSimulatorFixture.SelectBestDevice(candidates);

        Assert.Equal("Z", best.Udid);
    }

    [Fact]
    public void SelectBestDevice_UsesDeviceTypeModelForRenamedIPhones()
    {
        var candidates = new List<SimulatorDeviceCandidate>
        {
            new("A", "iPhone 15", "17.5", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-15"),
            new("B", "GDUI-Test", "18.6", "Shutdown", "com.apple.CoreSimulator.SimDeviceType.iPhone-16-Pro"),
        };

        var best = iOSSimulatorFixture.SelectBestDevice(candidates);

        Assert.Equal("B", best.Udid);
    }
}
