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
    public const string ShuttingDown = "shutting-down";
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
    [JsonPropertyName("uiHierarchy")] public bool UiHierarchy { get; init; }
    [JsonPropertyName("foregroundOwner")] public bool ForegroundOwner { get; init; }

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

    public static bool IsSupported(string? orientation) =>
        string.Equals(orientation, Portrait, StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, PortraitUpsideDown, StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, LandscapeLeft, StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, LandscapeRight, StringComparison.OrdinalIgnoreCase);

    public static bool IsLandscape(string? orientation) =>
        string.Equals(orientation, LandscapeLeft, StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, LandscapeRight, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A virtual device the device layer can address.
/// </summary>
public sealed record DeviceTarget
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = MobileCanvasProtocol.ValidatedProtocolVersion;
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
    [JsonPropertyName("isVirtual")] public bool IsVirtual { get; init; } = true;
    [JsonPropertyName("runtimeId")] public string? RuntimeId { get; init; }
    [JsonPropertyName("runtimeName")] public string? RuntimeName { get; init; }
    [JsonPropertyName("osVersion")] public string? OsVersion { get; init; }
    [JsonPropertyName("deviceTypeId")] public string? DeviceTypeId { get; init; }
    [JsonPropertyName("deviceTypeName")] public string? DeviceTypeName { get; init; }
    [JsonPropertyName("modelIdentifier")] public string? ModelIdentifier { get; init; }
    [JsonPropertyName("architecture")] public string? Architecture { get; init; }
    [JsonPropertyName("deviceSet")] public string? DeviceSet { get; init; }

    /// <summary>The AVD name on Android; null elsewhere. A secondary join key.</summary>
    [JsonPropertyName("avdName")] public string? AvdName { get; init; }

    [JsonPropertyName("display")] public DisplayGeometry? Display { get; init; }
    [JsonPropertyName("capabilities")] public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.None;

    /// <summary>True when the device is booted far enough to be driven.</summary>
    [JsonIgnore]
    public bool IsBooted => string.Equals(State, DeviceStates.Booted, StringComparison.OrdinalIgnoreCase);
}

public sealed record DeviceRuntime
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("isAvailable")] public bool IsAvailable { get; init; }
    [JsonPropertyName("supportedArchitectures")] public string[] SupportedArchitectures { get; init; } = [];
    [JsonPropertyName("supportedDeviceTypeIds")] public string[] SupportedDeviceTypeIds { get; init; } = [];
}

public sealed record DeviceType
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("productFamily")] public string? ProductFamily { get; init; }
    [JsonPropertyName("modelIdentifier")] public string? ModelIdentifier { get; init; }
    [JsonPropertyName("minimumRuntimeVersion")] public string? MinimumRuntimeVersion { get; init; }
    [JsonPropertyName("maximumRuntimeVersion")] public string? MaximumRuntimeVersion { get; init; }
}

public sealed record DeviceDiagnosticAction
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("label")] public string Label { get; init; } = "";
}

public sealed record DeviceDependencyCheck
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("actions")] public DeviceDiagnosticAction[] Actions { get; init; } = [];
}

public sealed record DeviceHostDiagnostics
{
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("ready")] public bool Ready { get; init; }
    [JsonPropertyName("checks")] public DeviceDependencyCheck[] Checks { get; init; } = [];
}

public sealed record DeviceCatalog
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = MobileCanvasProtocol.ValidatedProtocolVersion;
    [JsonPropertyName("devices")] public DeviceTarget[] Devices { get; init; } = [];
    [JsonPropertyName("runtimes")] public DeviceRuntime[] Runtimes { get; init; } = [];
    [JsonPropertyName("deviceTypes")] public DeviceType[] DeviceTypes { get; init; } = [];
    [JsonPropertyName("diagnostics")] public DeviceHostDiagnostics[] Diagnostics { get; init; } = [];
}

public sealed record DeviceCreateRequest
{
    [JsonPropertyName("platform")] public string Platform { get; init; } = DevicePlatforms.Ios;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("runtimeId")] public string RuntimeId { get; init; } = "";
    [JsonPropertyName("deviceTypeId")] public string DeviceTypeId { get; init; } = "";
}

public sealed record DeviceSwipe
{
    [JsonPropertyName("startX")] public double StartX { get; init; }
    [JsonPropertyName("startY")] public double StartY { get; init; }
    [JsonPropertyName("endX")] public double EndX { get; init; }
    [JsonPropertyName("endY")] public double EndY { get; init; }
    [JsonPropertyName("duration")] public double Duration { get; init; } = 0.35;
}

public sealed record DeviceRecordingStatus
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; init; } = "";
    [JsonPropertyName("isRecording")] public bool IsRecording { get; init; }
    [JsonPropertyName("outputPath")] public string? OutputPath { get; init; }
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("timeoutSeconds")] public int? TimeoutSeconds { get; init; }
}

public sealed record DeviceUiElement
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("parentId")] public string? ParentId { get; init; }
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("packageId")] public string? PackageId { get; init; }
    [JsonPropertyName("isSystem")] public bool IsSystem { get; init; }
    [JsonPropertyName("interactive")] public bool Interactive { get; init; }
    [JsonPropertyName("bounds")] public DeviceRect? Bounds { get; init; }
}

public sealed record DeviceUiSnapshot
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; init; } = "";
    [JsonPropertyName("capturedAt")] public DateTimeOffset CapturedAt { get; init; }
    [JsonPropertyName("orientation")] public string? Orientation { get; init; }
    [JsonPropertyName("scale")] public double? Scale { get; init; }
    [JsonPropertyName("foregroundOwner")] public string? ForegroundOwner { get; init; }
    [JsonPropertyName("keyboardVisible")] public bool? KeyboardVisible { get; init; }
    [JsonPropertyName("elements")] public DeviceUiElement[] Elements { get; init; } = [];
    [JsonPropertyName("limitations")] public string[] Limitations { get; init; } = [];
}

/// <summary>
/// Why a device host is or is not usable.
/// <para>
/// These are kept distinct because collapsing them is actively harmful: an incompatible or
/// unauthenticated host that reports as "absent" looks exactly like a machine with no device
/// layer installed, so a real, fixable integration failure silently presents as a missing
/// feature and nobody ever investigates it.
/// </para>
/// </summary>
public enum DeviceHostAvailability
{
    /// <summary>No host is installed or running. The ordinary case on most machines.</summary>
    Absent = 0,

    /// <summary>A state file exists but the host did not answer. Usually a stale file.</summary>
    NotResponding = 1,

    /// <summary>The host answered but rejected our credentials.</summary>
    Unauthorized = 2,

    /// <summary>The host speaks a protocol version this build does not support.</summary>
    Incompatible = 3,

    /// <summary>The host is present, authenticated, and usable.</summary>
    Available = 4,
}

/// <summary>
/// Health of the external device host backing an <see cref="IDeviceSurface"/>.
/// </summary>
public sealed record DeviceHostHealth
{
    /// <summary>Why the host is or is not usable.</summary>
    public DeviceHostAvailability Availability { get; init; } = DeviceHostAvailability.Absent;

    /// <summary>True only when the host is fully usable.</summary>
    public bool Available => Availability == DeviceHostAvailability.Available;

    /// <summary>
    /// What a human should do about it. Present for every state except <see cref="DeviceHostAvailability.Available"/>.
    /// </summary>
    public string? Reason { get; init; }

    public string? Version { get; init; }

    /// <summary>The protocol version the host reported, when it got far enough to say.</summary>
    public string? ProtocolVersion { get; init; }

    public static readonly DeviceHostHealth Unavailable = new()
    {
        Availability = DeviceHostAvailability.Absent,
        Reason = "No device host is installed or running.",
    };

    public static DeviceHostHealth NotResponding(string reason) =>
        new() { Availability = DeviceHostAvailability.NotResponding, Reason = reason };

    public static DeviceHostHealth Unauthorized() =>
        new()
        {
            Availability = DeviceHostAvailability.Unauthorized,
            Reason = "The device host rejected DevFlow's control token. It was most likely restarted; "
                   + "retry, or restart the device host to reissue one.",
        };

    public static DeviceHostHealth Incompatible(string? reported, string? hostVersion = null) =>
        new()
        {
            Availability = DeviceHostAvailability.Incompatible,
            ProtocolVersion = reported,
            Version = hostVersion,
            Reason = $"The device host reports protocol '{reported ?? "unknown"}' and version "
                   + $"'{hostVersion ?? "unknown"}', which this build of DevFlow does not support "
                   + $"(it requires protocol {MobileCanvasProtocol.ValidatedProtocolVersion} and host "
                   + $"{MobileCanvasProtocol.ValidatedHostVersion} or newer).",
        };
}

/// <summary>A simulated geographic position.</summary>
public sealed record DeviceLocation
{
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
}