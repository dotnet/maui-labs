using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The agent's self-reported device identity is the join key that pairs a running app with the
/// device around it. It must be best-effort: an app that cannot work out what it is running on
/// still has to register and stay inspectable.
/// </summary>
public class DeviceIdentityProviderTests : IDisposable
{
    private readonly string? _originalSimulatorUdid = Environment.GetEnvironmentVariable("SIMULATOR_UDID");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", _originalSimulatorUdid);
        DeviceIdentityProvider.Resolver = null;
    }

    [Fact]
    public void ResolveFromEnvironment_ReadsTheSimulatorUdid()
    {
        // The Apple simulator runtime injects this into every process it hosts, and the value is
        // exactly the UDID simctl uses — so it is both free to read and an exact join key.
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "1E2D3C4B-5A69");

        var identity = DeviceIdentity.Parse(DeviceIdentityProvider.ResolveFromEnvironment());

        Assert.Equal(DevicePlatforms.Ios, identity.Platform);
        Assert.Equal("1E2D3C4B-5A69", identity.Udid);
    }

    [Fact]
    public void ResolveFromEnvironment_TrimsTheValue()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "  1E2D3C4B  ");

        Assert.Equal("1E2D3C4B", DeviceIdentity.Parse(DeviceIdentityProvider.ResolveFromEnvironment()).Udid);
    }

    [Fact]
    public void ResolveFromEnvironment_ReturnsNothing_OffASimulator()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", null);

        Assert.Null(DeviceIdentityProvider.ResolveFromEnvironment());
    }

    [Fact]
    public void ResolveFromEnvironment_IgnoresABlankValue()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "   ");

        Assert.Null(DeviceIdentityProvider.ResolveFromEnvironment());
    }

    [Fact]
    public void Resolve_PrefersThePlatformResolver()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "from-environment");
        DeviceIdentityProvider.Resolver = () => "platform=android;serial=emulator-5554";

        var identity = DeviceIdentity.Parse(DeviceIdentityProvider.Resolve());

        Assert.Equal("emulator-5554", identity.Serial);
        Assert.Null(identity.Udid);
    }

    [Fact]
    public void Resolve_FallsBackToTheEnvironment_WhenThePlatformKnowsNothing()
    {
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "1E2D3C4B");
        DeviceIdentityProvider.Resolver = () => null;

        Assert.Equal("1E2D3C4B", DeviceIdentity.Parse(DeviceIdentityProvider.Resolve()).Udid);
    }

    [Fact]
    public void Resolve_SurvivesAThrowingPlatformResolver()
    {
        // Pairing is an enhancement, never a requirement. A platform API that throws must not
        // stop the app from registering with the broker.
        DeviceIdentityProvider.Resolver = () => throw new InvalidOperationException("platform API unavailable");

        var exception = Record.Exception(() => DeviceIdentityProvider.Resolve());

        Assert.Null(exception);
    }

    [Fact]
    public void ResolvedIdentity_PairsWithTheMatchingDevice()
    {
        // The end-to-end point of the whole mechanism: what an app reports about itself must
        // resolve to the device the device layer independently discovered.
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", "1E2D3C4B-5A69");
        DeviceIdentityProvider.Resolver = null;

        var identity = DeviceIdentity.Parse(DeviceIdentityProvider.Resolve());
        var devices = new[]
        {
            new DeviceTarget { Id = "ios:OTHER", NativeId = "OTHER", Udid = "OTHER", Name = "iPhone 15" },
            new DeviceTarget { Id = "ios:1E2D3C4B-5A69", NativeId = "1E2D3C4B-5A69", Udid = "1E2D3C4B-5A69", Name = "iPhone 16" },
        };

        var paired = DeviceIdentityMatcher.FindBest(identity, devices);

        Assert.NotNull(paired);
        Assert.Equal("iPhone 16", paired!.Name);
    }

    [Fact]
    public void DesktopApp_PairsWithNothing()
    {
        // A Windows or macOS MAUI app has no virtual device around it, and must not accidentally
        // pair with whichever emulator happens to be booted.
        Environment.SetEnvironmentVariable("SIMULATOR_UDID", null);
        DeviceIdentityProvider.Resolver = () => null;

        var identity = DeviceIdentity.Parse(DeviceIdentityProvider.Resolve());
        var devices = new[]
        {
            new DeviceTarget { Id = "android:emulator-5554", NativeId = "emulator-5554", Name = "Pixel 8" },
        };

        Assert.True(identity.IsEmpty);
        Assert.Null(DeviceIdentityMatcher.FindBest(identity, devices));
    }
}
