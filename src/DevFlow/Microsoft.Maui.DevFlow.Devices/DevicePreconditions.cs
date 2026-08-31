using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// Device state a flow needs established before it runs.
/// <para>
/// A test plan could already <em>declare</em> an environment — locale, theme, orientation — but
/// nothing could <em>establish</em> one, so a flow that depended on "location denied" or "offline"
/// was only reproducible by hand. This makes that model executable, which is what turns an
/// environment-dependent flaky test into a deterministic one.
/// </para>
/// <para>
/// Carried as an additive extension field on the flow, per the spec rule that unknown extension
/// fields must survive compatible readers and writers.
/// </para>
/// </summary>
public sealed record DevicePreconditions
{
    /// <summary>The extension field name flows carry these under.</summary>
    public const string ExtensionKey = "devicePreconditions";

    /// <summary>Permission name to state, where state is <c>granted</c> or <c>denied</c>.</summary>
    [JsonPropertyName("permissions")]
    public Dictionary<string, string>? Permissions { get; init; }

    /// <summary>Latitude and longitude to simulate, or <c>null</c> to clear any simulated location.</summary>
    [JsonPropertyName("location")]
    public DeviceLocation? Location { get; init; }

    /// <summary>Whether a location was explicitly specified, distinguishing "clear it" from "leave it".</summary>
    [JsonPropertyName("clearLocation")]
    public bool ClearLocation { get; init; }

    /// <summary>Network condition: <c>online</c>, <c>offline</c>, or a named profile.</summary>
    [JsonPropertyName("network")]
    public string? Network { get; init; }

    /// <summary>Battery percentage from 0 to 100.</summary>
    [JsonPropertyName("battery")]
    public int? Battery { get; init; }

    /// <summary>Display orientation, one of <see cref="DeviceOrientations"/>.</summary>
    [JsonPropertyName("orientation")]
    public string? Orientation { get; init; }

    /// <summary>True when nothing is requested, so applying is a no-op.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        (Permissions is null || Permissions.Count == 0)
        && Location is null
        && !ClearLocation
        && string.IsNullOrWhiteSpace(Network)
        && Battery is null
        && string.IsNullOrWhiteSpace(Orientation);

    /// <summary>
    /// Reads preconditions from a flow's extension data, or <c>null</c> when it declares none.
    /// A malformed block is a hard error rather than a silent skip: running with the wrong
    /// environment and reporting a pass is worse than refusing to run.
    /// </summary>
    public static DevicePreconditions? FromExtensionData(
        IReadOnlyDictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null || !extensionData.TryGetValue(ExtensionKey, out var element))
            return null;

        return element.Deserialize(DevicePreconditionsJsonContext.Default.DevicePreconditions);
    }
}

[JsonSerializable(typeof(DevicePreconditions))]
[JsonSerializable(typeof(DeviceLocation))]
internal sealed partial class DevicePreconditionsJsonContext : JsonSerializerContext;

/// <summary>The outcome of establishing a flow's device preconditions.</summary>
/// <param name="Success">Whether every requested precondition was established.</param>
/// <param name="Applied">What was actually established, for the run report.</param>
/// <param name="Reason">Why establishment failed.</param>
public sealed record DevicePreconditionResult(
    bool Success,
    IReadOnlyList<string> Applied,
    string? Reason = null);

/// <summary>
/// Establishes a flow's device preconditions before it runs.
/// </summary>
public static class DevicePreconditionApplier
{
    /// <summary>
    /// Applies preconditions to a device.
    /// <para>
    /// <b>Fails fast.</b> If a precondition cannot be established — because the platform or host
    /// does not support it — the run is refused rather than continued. Silently skipping would
    /// produce a green test in the wrong environment, which is strictly worse than not having the
    /// feature: it reports confidence that was never earned.
    /// </para>
    /// <para>
    /// Order matters. Permissions are applied before anything that could launch the app, and
    /// orientation last so the app observes a stable display.
    /// </para>
    /// </summary>
    public static Task<DevicePreconditionResult> ApplyAsync(
        IDeviceSurface surface,
        DeviceTarget device,
        DevicePreconditions preconditions,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(surface, device, preconditions, appPackageId: null, cancellationToken);

    /// <summary>Applies preconditions for an exact app package or bundle identifier.</summary>
    public static Task<DevicePreconditionResult> ApplyAsync(
        IDeviceSurface surface,
        DeviceTarget device,
        DevicePreconditions preconditions,
        string appPackageId,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(surface, device, preconditions, appPackageId, cancellationToken);

    private static async Task<DevicePreconditionResult> ApplyCoreAsync(
        IDeviceSurface surface,
        DeviceTarget device,
        DevicePreconditions preconditions,
        string? appPackageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(preconditions);

        var applied = new List<string>();

        if (preconditions.IsEmpty)
            return new DevicePreconditionResult(true, applied);

        // Validate everything knowable without I/O first. Refusing after having already denied a
        // permission and taken the device offline would leave it half-prepared, which is exactly
        // what the fail-fast contract promises not to do.
        if (preconditions.Permissions is { Count: > 0 } requestedPermissions)
        {
            foreach (var (permission, state) in requestedPermissions)
            {
                if (string.IsNullOrWhiteSpace(permission) || permission.Length > 256)
                {
                    return new DevicePreconditionResult(
                        false,
                        applied,
                        "Permission names must be non-empty and no longer than 256 characters. "
                        + "The run was stopped before the device was touched.");
                }

                if (state is null ||
                    (!string.Equals(state, "granted", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(state, "denied", StringComparison.OrdinalIgnoreCase)))
                {
                    return new DevicePreconditionResult(
                        false,
                        applied,
                        $"Permission '{permission}' must be 'granted' or 'denied'. "
                        + "The run was stopped before the device was touched.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(preconditions.Orientation) &&
            !DeviceOrientations.IsSupported(preconditions.Orientation))
        {
            return new DevicePreconditionResult(
                false,
                applied,
                $"Unsupported device orientation '{preconditions.Orientation}'. "
                + "The run was stopped before the device was touched.");
        }

        if (preconditions.Battery is { } requested && requested is < 0 or > 100)
        {
            return new DevicePreconditionResult(
                false,
                applied,
                $"A battery level must be between 0 and 100, but {requested} was requested. "
                + "The run was stopped before the device was touched.");
        }

        if (preconditions.ClearLocation || preconditions.Location is not null)
        {
            return new DevicePreconditionResult(
                false,
                applied,
                "The Mobile Canvas protocol cannot read a simulated location back. "
                + "The run was stopped before the device was touched because the requested location cannot be verified.");
        }

        if (!string.IsNullOrWhiteSpace(preconditions.Network) &&
            string.Equals(device.Platform, DevicePlatforms.Ios, StringComparison.OrdinalIgnoreCase))
        {
            return new DevicePreconditionResult(
                false,
                applied,
                "The iOS simulator changes only the network indicator, not the app's connection. "
                + "The run was stopped before the device was touched.");
        }

        if (!device.IsBooted)
        {
            return new DevicePreconditionResult(
                false,
                applied,
                $"Device '{device.Name}' is not booted, so its environment cannot be prepared.");
        }

        if (preconditions.Permissions is { Count: > 0 } permissions)
        {
            foreach (var (permission, state) in permissions)
            {
                var result = string.IsNullOrWhiteSpace(appPackageId)
                    ? await surface
                        .SetPermissionAsync(device.Id, permission, state, cancellationToken)
                        .ConfigureAwait(false)
                    : await surface
                        .SetPermissionAsync(device.Id, appPackageId, permission, state, cancellationToken)
                        .ConfigureAwait(false);

                if (!result.Success)
                    return Refused($"permission '{permission}' = '{state}'", result.Reason, applied);

                applied.Add($"permission {permission}={state}");
            }
        }

        if (!string.IsNullOrWhiteSpace(preconditions.Network))
        {
            var result = await surface
                .SetNetworkAsync(device.Id, preconditions.Network, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
                return Refused($"network '{preconditions.Network}'", result.Reason, applied);

            applied.Add($"network {preconditions.Network}");
        }

        if (preconditions.Battery is { } battery)
        {
            var result = await surface
                .SetBatteryAsync(device.Id, battery, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
                return Refused($"battery {battery}", result.Reason, applied);

            applied.Add($"battery {battery}%");
        }

        if (!string.IsNullOrWhiteSpace(preconditions.Orientation))
        {
            var result = await surface
                .RotateAsync(device.Id, preconditions.Orientation, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
                return Refused($"orientation '{preconditions.Orientation}'", result.Reason, applied);

            applied.Add($"orientation {preconditions.Orientation}");
        }

        return new DevicePreconditionResult(true, applied);
    }

    private static DevicePreconditionResult Refused(string what, string? reason, List<string> applied) =>
        new(false, applied,
            $"Could not establish {what}: {reason ?? "the device refused it"}. "
            + "The run was stopped rather than continued in the wrong environment.");
}
