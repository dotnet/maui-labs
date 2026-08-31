using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Pairing an app agent to the device it runs on is what makes the device layer part of DevFlow
/// rather than a second tool. A wrong pairing is silent — it draws the overlay over the wrong
/// screen — so these tests pin both the matches and the refusals.
/// </summary>
public class DeviceIdentityMatcherTests
{
    private static DeviceTarget Simulator(string udid, string name = "iPhone 16") => new()
    {
        Id = $"ios:{udid}",
        Platform = DevicePlatforms.Ios,
        NativeId = udid,
        Udid = udid,
        Name = name,
    };

    private static DeviceTarget Emulator(string serial, string? avd = null, string name = "Pixel 8") => new()
    {
        Id = $"android:{serial}",
        Platform = DevicePlatforms.Android,
        NativeId = serial,
        AvdName = avd,
        Name = name,
    };

    [Fact]
    public void Match_IsExact_ForASimulatorUdid()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Ios, Udid: "A1B2-C3D4");

        Assert.Equal(DeviceMatchConfidence.Exact, DeviceIdentityMatcher.Match(identity, Simulator("A1B2-C3D4")));
    }

    [Fact]
    public void Match_IsExact_ForAnAdbSerial()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Android, Serial: "emulator-5554");

        Assert.Equal(DeviceMatchConfidence.Exact, DeviceIdentityMatcher.Match(identity, Emulator("emulator-5554")));
    }

    [Fact]
    public void Match_RejectsACrossPlatformIdentifierCollision()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Android, Serial: "A1B2-C3D4");

        Assert.Equal(
            DeviceMatchConfidence.None,
            DeviceIdentityMatcher.Match(identity, Simulator("A1B2-C3D4")));
    }

    [Fact]
    public void Match_RejectsACrossPlatformNameCollision()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Android, AvdName: "iPhone 16");

        Assert.Equal(
            DeviceMatchConfidence.None,
            DeviceIdentityMatcher.Match(identity, Simulator("A1B2-C3D4")));
    }

    [Fact]
    public void Match_IgnoresCaseAndSurroundingWhitespace()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Ios, Udid: "  a1b2-c3d4  ");

        Assert.Equal(DeviceMatchConfidence.Exact, DeviceIdentityMatcher.Match(identity, Simulator("A1B2-C3D4")));
    }

    [Fact]
    public void Match_IsWeak_ForAnAvdName()
    {
        // An AVD name is user-chosen and unique only within one machine's AVD set.
        var identity = new DeviceIdentity(DevicePlatforms.Android, AvdName: "Pixel_8_API_35");

        Assert.Equal(
            DeviceMatchConfidence.Weak,
            DeviceIdentityMatcher.Match(identity, Emulator("emulator-5554", avd: "Pixel_8_API_35")));
    }

    [Fact]
    public void Match_TreatsUnderscoresAndSpacesAsEquivalent_InAvdNames()
    {
        // The emulator's own property and the device host do not always agree on separators, and
        // a name is the one signal where that disagreement is cosmetic rather than meaningful.
        var identity = new DeviceIdentity(DevicePlatforms.Android, AvdName: "Pixel_8_API_35");

        Assert.Equal(
            DeviceMatchConfidence.Weak,
            DeviceIdentityMatcher.Match(identity, Emulator("emulator-5554", avd: "Pixel 8 API 35")));
    }

    [Fact]
    public void Match_StillRequiresExactIdentifiers_WhenSeparatorsDiffer()
    {
        // The normalisation applied to names must never leak into identifier comparison, where
        // an exact match is the entire point.
        var identity = new DeviceIdentity(DevicePlatforms.Android, Serial: "emulator_5554");

        Assert.Equal(DeviceMatchConfidence.None, DeviceIdentityMatcher.Match(identity, Emulator("emulator-5554")));
    }

    [Fact]
    public void Match_IsNone_ForAnEmptyIdentity()
    {
        // A desktop app reports nothing, and must not accidentally pair with a phone.
        Assert.Equal(DeviceMatchConfidence.None, DeviceIdentityMatcher.Match(default, Simulator("A1B2")));
    }

    [Fact]
    public void Match_IsNone_WhenNothingCorresponds()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Ios, Udid: "AAAA");

        Assert.Equal(DeviceMatchConfidence.None, DeviceIdentityMatcher.Match(identity, Simulator("BBBB")));
    }

    [Fact]
    public void FindBest_PrefersAnExactMatchOverAWeakOne()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Android, Serial: "emulator-5556", AvdName: "Pixel_8");
        var devices = new[]
        {
            Emulator("emulator-5554", avd: "Pixel_8"),
            Emulator("emulator-5556", avd: "Other"),
        };

        var best = DeviceIdentityMatcher.FindBest(identity, devices);

        Assert.NotNull(best);
        Assert.Equal("emulator-5556", best!.NativeId);
    }

    [Fact]
    public void FindBest_RefusesToGuess_WhenTwoDevicesMatchEquallyWell()
    {
        // Two AVDs can share a name across device sets. Pairing the wrong one is worse than not
        // pairing, because every coordinate afterwards would target the wrong screen.
        var identity = new DeviceIdentity(DevicePlatforms.Android, AvdName: "Pixel_8");
        var devices = new[]
        {
            Emulator("emulator-5554", avd: "Pixel_8"),
            Emulator("emulator-5556", avd: "Pixel_8"),
        };

        Assert.Null(DeviceIdentityMatcher.FindBest(identity, devices));
    }

    [Fact]
    public void FindBest_ReturnsNull_ForAnEmptyIdentity()
    {
        Assert.Null(DeviceIdentityMatcher.FindBest(default, new[] { Simulator("A1B2") }));
    }

    [Fact]
    public void FindBest_ReturnsNull_WhenNoDevicesAreKnown()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Ios, Udid: "A1B2");

        Assert.Null(DeviceIdentityMatcher.FindBest(identity, []));
    }

    [Fact]
    public void WireFormat_RoundTrips()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Android, Serial: "emulator-5554", AvdName: "Pixel_8_API_35");

        var parsed = DeviceIdentity.Parse(identity.ToWireFormat());

        Assert.Equal(identity, parsed);
    }

    [Fact]
    public void WireFormat_OmitsEmptyFields()
    {
        var identity = new DeviceIdentity(DevicePlatforms.Ios, Udid: "A1B2");

        Assert.Equal("platform=ios;udid=A1B2", identity.ToWireFormat());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("=novalue")]
    [InlineData("key=")]
    public void Parse_DegradesToEmpty_ForUnusableInput(string? wire)
    {
        Assert.True(DeviceIdentity.Parse(wire).IsEmpty);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeys_SoTheFormatCanGrow()
    {
        var parsed = DeviceIdentity.Parse("udid=A1B2;somethingNew=x");

        Assert.Equal("A1B2", parsed.Udid);
    }
}
