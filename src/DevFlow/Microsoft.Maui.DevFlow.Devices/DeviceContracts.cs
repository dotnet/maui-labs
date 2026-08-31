using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// Platform identifiers used by the device layer. These mirror the wire values used by the
/// external device host so a value read from the wire round-trips without translation.
/// </summary>
public static class DevicePlatforms
{
    public const string Ios = "ios";
    public const string Android = "android";
}

/// <summary>
/// Lifecycle states a virtual device can report.
/// </summary>
public static class DeviceStates
{
    public const string Unknown = "unknown";
    public const string Shutdown = "shutdown";
    public const string Booting = "booting";
    public const string Booted = "booted";
}

/// <summary>
/// How a display's rounded corners are drawn.
/// </summary>
public static class DisplayCornerCurves
{
    public const string Circular = "circular";
    public const string Continuous = "continuous";
}

/// <summary>
/// What a device backend can actually do, negotiated rather than assumed.
/// <para>
/// Every consumer must gate on these instead of inferring from the platform: iOS control is
/// macOS-only and additionally needs <c>idb</c> for input, and hardware H.264 encoding is not
/// available on every host, so a device that streams on one machine only screenshots on another.
/// </para>
/// </summary>
public sealed record DeviceCapabilities
{
    [JsonPropertyName("boot")] public bool Boot { get; init; }
    [JsonPropertyName("shutdown")] public bool Shutdown { get; init; }
    [JsonPropertyName("restart")] public bool Restart { get; init; }
    [JsonPropertyName("erase")] public bool Erase { get; init; }
    [JsonPropertyName("delete")] public bool Delete { get; init; }
    [JsonPropertyName("reveal")] public bool Reveal { get; init; }
    [JsonPropertyName("tap")] public bool Tap { get; init; }
    [JsonPropertyName("longPress")] public bool LongPress { get; init; }
    [JsonPropertyName("swipe")] public bool Swipe { get; init; }
    [JsonPropertyName("scroll")] public bool Scroll { get; init; }
    [JsonPropertyName("text")] public bool Text { get; init; }
    [JsonPropertyName("key")] public bool Key { get; init; }
    [JsonPropertyName("button")] public bool Button { get; init; }
    [JsonPropertyName("rotate")] public bool Rotate { get; init; }
    [JsonPropertyName("screenshot")] public bool Screenshot { get; init; }
    [JsonPropertyName("liveStream")] public bool LiveStream { get; init; }
    [JsonPropertyName("recording")] public bool Recording { get; init; }

    /// <summary>Everything unavailable. The honest answer when no device backend is present.</summary>
    public static readonly DeviceCapabilities None = new();
}

/// <summary>
/// The physical geometry of a device display.
/// <para>
/// Both a pixel size and a point size are carried because the two coordinate spaces are used by
/// different layers: video frames arrive in pixels, while app-level coordinates are in points.
/// Deriving one from the other with a hard-coded density is the classic source of overlays that
/// look correct on one device and are subtly offset on another.
/// </para>
/// </summary>
public sealed record DisplayGeometry
{
    [JsonPropertyName("pixelWidth")] public int PixelWidth { get; init; }
    [JsonPropertyName("pixelHeight")] public int PixelHeight { get; init; }
    [JsonPropertyName("pointWidth")] public double PointWidth { get; init; }
    [JsonPropertyName("pointHeight")] public double PointHeight { get; init; }

    /// <summary>Pixels per point. Reported rather than derived so a backend can correct it.</summary>
    [JsonPropertyName("scale")] public double Scale { get; init; } = 1;

    [JsonPropertyName("orientation")] public string Orientation { get; init; } = DeviceOrientations.Portrait;

    /// <summary>
    /// Corner radius in points, or <c>null</c> when the platform did not report one. Zero is a
    /// meaningful answer that means the display really is square-cornered.
    /// </summary>
    [JsonPropertyName("cornerRadius")] public double? CornerRadius { get; init; }

    [JsonPropertyName("cornerCurve")] public string CornerCurve { get; init; } = DisplayCornerCurves.Circular;
}

/// <summary>
/// Display orientations. The value describes the orientation the display currently presents,
/// not the rotation that must be applied to a video frame — see
/// <see cref="DeviceCoordinateSpace.FrameQuarterTurns"/> for that.
/// </summary>
public static class DeviceOrientations
{
    public const string Portrait = "portrait";
    public const string PortraitUpsideDown = "portraitUpsideDown";
    public const string LandscapeLeft = "landscapeLeft";
    public const string LandscapeRight = "landscapeRight";

    public static bool IsLandscape(string? orientation) =>
        string.Equals(orientation, LandscapeLeft, StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, LandscapeRight, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A virtual device the device layer can address.
/// </summary>
public sealed record DeviceTarget
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("provider")] public string Provider { get; init; } = "";

    /// <summary>
    /// The identifier the platform's own tooling uses — an adb serial such as
    /// <c>emulator-5554</c>, or a simulator UDID. This is the value an agent can recognise
    /// about itself, so it is the primary join key for pairing an app to its device.
    /// </summary>
    [JsonPropertyName("nativeId")] public string NativeId { get; init; } = "";

    [JsonPropertyName("udid")] public string? Udid { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = DeviceStates.Unknown;
    [JsonPropertyName("isAvailable")] public bool IsAvailable { get; init; }
    [JsonPropertyName("runtimeName")] public string? RuntimeName { get; init; }
    [JsonPropertyName("osVersion")] public string? OsVersion { get; init; }

    /// <summary>The AVD name on Android; null elsewhere. A secondary join key.</summary>
    [JsonPropertyName("avdName")] public string? AvdName { get; init; }

    [JsonPropertyName("display")] public DisplayGeometry? Display { get; init; }
    [JsonPropertyName("capabilities")] public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.None;

    /// <summary>True when the device is booted far enough to be driven.</summary>
    [JsonIgnore]
    public bool IsBooted => string.Equals(State, DeviceStates.Booted, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Health of the external device host backing an <see cref="IDeviceSurface"/>.
/// </summary>
public sealed record DeviceHostHealth
{
    /// <summary>True when a device host was discovered and answered.</summary>
    public bool Available { get; init; }

    /// <summary>Human-readable reason the host is unavailable, for surfacing in diagnostics.</summary>
    public string? Reason { get; init; }

    public string? Version { get; init; }

    public static readonly DeviceHostHealth Unavailable =
        new() { Available = false, Reason = "No device host is installed." };
}
