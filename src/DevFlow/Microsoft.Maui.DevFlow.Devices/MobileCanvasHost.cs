using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// Versioning of the external device host's wire protocol.
/// </summary>
public static class MobileCanvasProtocol
{
    /// <summary>The upstream host release this adapter's contract tests are pinned to.</summary>
    public const string ValidatedHostVersion = "0.1.16";

    /// <summary>The exact upstream revision used to validate the host contract.</summary>
    public const string ValidatedHostRevision = "0f0d7806a08d41b3b0b932c05b313686486f75ca";

    /// <summary>The complete protocol version advertised by the validated host.</summary>
    public const string ValidatedProtocolVersion = "1.0";

    /// <summary>Whether a reported protocol version exactly matches the validated contract.</summary>
    public static bool IsSupported(string? reported) =>
        string.Equals(reported?.Trim(), ValidatedProtocolVersion, StringComparison.Ordinal);

    /// <summary>
    /// Whether a running host is new enough for every route this adapter may call.
    /// </summary>
    public static bool IsHostCompatible(string? protocolVersion, string? hostVersion)
    {
        if (!IsSupported(protocolVersion) ||
            !TryParseProductVersion(hostVersion, out var reported) ||
            !Version.TryParse(ValidatedHostVersion, out var baseline))
        {
            return false;
        }

        return reported >= baseline;
    }

    private static bool TryParseProductVersion(string? value, out Version version)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains('-'))
        {
            version = null!;
            return false;
        }
        var normalized = trimmed.Split('+', 2)[0];
        return Version.TryParse(normalized, out version!);
    }
}

/// <summary>
/// State written by an external Mobile Canvas host to <c>~/.mobile-canvas/host.json</c>.
/// <para>
/// Field names mirror that product's own contract exactly. Only what DevFlow needs is modelled,
/// and unknown fields are tolerated: the file is owned by another product, so a shape change must
/// degrade to "no usable host" rather than throwing into a caller that was only asking whether a
/// device was available.
/// </para>
/// <para>
/// Note that this file exists only while the host is <em>running</em> — it is removed on shutdown.
/// Its absence therefore means "no host running", which is not the same as "not installed".
/// </para>
/// </summary>
public sealed record MobileCanvasHostState
{
    [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("port")] public int Port { get; init; }
    [JsonPropertyName("processId")] public int ProcessId { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }

    /// <summary>
    /// Bearer token a trusted local control client presents on every request. Distinct from the
    /// single-use bootstrap secret the host issues to canvas panels, which is a different flow.
    /// </summary>
    [JsonPropertyName("controlToken")] public string? ControlToken { get; init; }

    /// <summary>Where discovery found this state file. Not part of the host wire contract.</summary>
    [JsonIgnore]
    public MobileCanvasHostStateOrigin Origin { get; init; }

    [JsonIgnore]
    public bool IsUsable => Port is > 0 and <= 65535;

    [JsonIgnore]
    public string BaseUrl => $"http://127.0.0.1:{Port}";
}

public enum MobileCanvasHostStateOrigin
{
    Unknown,
    ProtocolScoped,
    Legacy,
}

/// <summary>
/// Discovers a locally installed Mobile Canvas host.
/// <para>
/// Discovery is deliberately file-based and never launches anything as a side effect of merely
/// looking. A DevFlow session that only wants to know whether device control is possible must not
/// start a background daemon to find out.
/// </para>
/// </summary>
public static class MobileCanvasHost
{
    /// <summary>Overrides the home directory. For tests only.</summary>
    public static string? HomeOverride { get; set; }

    /// <summary>The per-user directory the external host owns.</summary>
    public static string HomeDirectory =>
        HomeOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mobile-canvas");

    /// <summary>The state file the external host writes when it is running.</summary>
    public static string StateFilePath =>
        Path.Combine(HomeDirectory, "hosts", $"v{MobileCanvasProtocol.ValidatedProtocolVersion}", "host.json");

    /// <summary>The unversioned location used by hosts that predate protocol-scoped singletons.</summary>
    public static string LegacyStateFilePath => Path.Combine(HomeDirectory, "host.json");

    /// <summary>
    /// Reads the host state, or <c>null</c> when no usable host is present.
    /// <para>
    /// Every failure mode — missing file, partial write, foreign schema — collapses to
    /// <c>null</c>, because "there is no device layer here" is an ordinary answer that the whole
    /// design is built to handle gracefully.
    /// </para>
    /// </summary>
    public static MobileCanvasHostState? TryRead()
    {
        try
        {
            var path = File.Exists(StateFilePath)
                ? StateFilePath
                : LegacyStateFilePath;
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            var state = JsonSerializer.Deserialize(stream, MobileCanvasJsonContext.Default.MobileCanvasHostState);
            return state?.IsUsable == true
                ? state with
                {
                    Origin = string.Equals(path, StateFilePath, StringComparison.Ordinal)
                        ? MobileCanvasHostStateOrigin.ProtocolScoped
                        : MobileCanvasHostStateOrigin.Legacy,
                }
                : null;
        }
        catch (IOException)
        {
            // The host rewrites this file; a torn read is transient and not an error worth raising.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            // A schema we do not recognise means we cannot safely talk to it.
            return null;
        }
    }

    /// <summary>Whether a Mobile Canvas host appears to be installed and running.</summary>
    public static bool IsPresent() => TryRead() is not null;

    /// <summary>
    /// Whether state is safe to use for control. Legacy state remains discoverable so diagnostics
    /// can explain and replace it, but it is never used to send mutations or expose a live feed.
    /// </summary>
    public static bool IsTrustedForControl(MobileCanvasHostState? state) =>
        state is not null &&
        state.Origin != MobileCanvasHostStateOrigin.Legacy &&
        MobileCanvasProtocol.IsHostCompatible(state.SchemaVersion, state.Version);
}

/// <summary>A point payload sent to the device host's input endpoints.</summary>
internal sealed record DevicePointPayload(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("duration")] double Duration = 0);

internal sealed record DeviceTextInputPayload(
    [property: JsonPropertyName("text")] string Text);

internal sealed record DeviceKeyInputPayload(
    [property: JsonPropertyName("keyCode")] ulong KeyCode);

internal sealed record DeviceButtonInputPayload(
    [property: JsonPropertyName("button")] string Button);

internal sealed record ConfirmedDeviceOperationPayload(
    [property: JsonPropertyName("confirm")] bool Confirm);

internal sealed record PermissionChangePayload(
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("permission")] string Permission,
    [property: JsonPropertyName("action")] string Action);

internal sealed record PermissionChangeResponse(
    [property: JsonPropertyName("success")] bool Success);

internal sealed record DeviceLocationPayload(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude);

internal sealed record BatteryPayload(
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("state")] string? State = null);

internal sealed record NetworkPayload(
    [property: JsonPropertyName("profile")] string? Profile,
    [property: JsonPropertyName("latencyMs")] int? LatencyMs = null);

internal sealed record RotatePayload(
    [property: JsonPropertyName("orientation")] string Orientation);

internal sealed record HardwareStateResponse
{
    [JsonPropertyName("batteryLevel")] public int? BatteryLevel { get; init; }
    [JsonPropertyName("batteryState")] public string? BatteryState { get; init; }
    [JsonPropertyName("networkIsIndicatorOnly")] public bool NetworkIsIndicatorOnly { get; init; }
}

internal sealed record RecordingStartPayload(
    [property: JsonPropertyName("outputPath")] string? OutputPath,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds);

internal sealed record UiQueryPayload(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("identifier")] string? Identifier,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("exact")] bool Exact,
    [property: JsonPropertyName("interactableOnly")] bool InteractableOnly,
    [property: JsonPropertyName("limit")] int Limit);

internal sealed record UiTapResponse
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
}

[JsonSerializable(typeof(MobileCanvasHostState))]
[JsonSerializable(typeof(DeviceTarget))]
[JsonSerializable(typeof(DeviceTarget[]))]
[JsonSerializable(typeof(DeviceCatalog))]
[JsonSerializable(typeof(DeviceCreateRequest))]
[JsonSerializable(typeof(DeviceSwipe))]
[JsonSerializable(typeof(DeviceRecordingStatus))]
[JsonSerializable(typeof(DisplayGeometry))]
[JsonSerializable(typeof(DeviceCapabilities))]
[JsonSerializable(typeof(DevicePointPayload))]
[JsonSerializable(typeof(DeviceTextInputPayload))]
[JsonSerializable(typeof(DeviceKeyInputPayload))]
[JsonSerializable(typeof(DeviceButtonInputPayload))]
[JsonSerializable(typeof(ConfirmedDeviceOperationPayload))]
[JsonSerializable(typeof(PermissionChangePayload))]
[JsonSerializable(typeof(PermissionChangeResponse))]
[JsonSerializable(typeof(DeviceLocationPayload))]
[JsonSerializable(typeof(BatteryPayload))]
[JsonSerializable(typeof(NetworkPayload))]
[JsonSerializable(typeof(RotatePayload))]
[JsonSerializable(typeof(HardwareStateResponse))]
[JsonSerializable(typeof(RecordingStartPayload))]
[JsonSerializable(typeof(UiQueryPayload))]
[JsonSerializable(typeof(UiTapResponse))]
[JsonSerializable(typeof(DeviceUiSnapshot))]
internal sealed partial class MobileCanvasJsonContext : JsonSerializerContext;
