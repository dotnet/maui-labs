using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// A device identity as reported by a running app about itself.
/// <para>
/// This is the join key that turns two independent tools into one product: without it a user
/// picks an app and a device separately and nothing connects them. An app can only know a few
/// things about the machine it is running on, so several weak signals are carried and matched in
/// priority order rather than insisting on one strong one.
/// </para>
/// </summary>
/// <param name="Platform">One of <see cref="DevicePlatforms"/>, or null when unknown.</param>
/// <param name="Udid">A simulator UDID, exact and preferred when present.</param>
/// <param name="Serial">A device serial, such as an adb serial.</param>
/// <param name="AvdName">The Android virtual device name, when the app can read it.</param>
public readonly record struct DeviceIdentity(
    string? Platform = null,
    string? Udid = null,
    string? Serial = null,
    string? AvdName = null)
{
    /// <summary>True when nothing usable was reported — a desktop app, for example.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Udid)
        && string.IsNullOrWhiteSpace(Serial)
        && string.IsNullOrWhiteSpace(AvdName);

    /// <summary>
    /// A compact wire form, <c>key=value</c> pairs separated by semicolons. Kept deliberately
    /// simple so it survives as a single string field through the existing registration payload
    /// without a schema change on either side.
    /// </summary>
    public string ToWireFormat()
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(Platform)) parts.Add($"platform={Platform}");
        if (!string.IsNullOrWhiteSpace(Udid)) parts.Add($"udid={Udid}");
        if (!string.IsNullOrWhiteSpace(Serial)) parts.Add($"serial={Serial}");
        if (!string.IsNullOrWhiteSpace(AvdName)) parts.Add($"avd={AvdName}");
        return string.Join(";", parts);
    }

    /// <summary>Parses <see cref="ToWireFormat"/>. Unknown keys are ignored so the format can grow.</summary>
    public static DeviceIdentity Parse(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire))
            return default;

        string? platform = null, udid = null, serial = null, avd = null;
        foreach (var part in wire.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (value.Length == 0)
                continue;

            switch (key.ToLowerInvariant())
            {
                case "platform": platform = value; break;
                case "udid": udid = value; break;
                case "serial": serial = value; break;
                case "avd": avd = value; break;
            }
        }

        return new DeviceIdentity(platform, udid, serial, avd);
    }
}

/// <summary>
/// How confidently an agent's self-reported identity matched a device.
/// </summary>
public enum DeviceMatchConfidence
{
    /// <summary>No usable correspondence.</summary>
    None = 0,

    /// <summary>Matched on a name that is not guaranteed unique, such as an AVD name.</summary>
    Weak = 1,

    /// <summary>Matched on an identifier the platform's own tooling uses.</summary>
    Exact = 2,
}

/// <summary>
/// Stable broker lease keys derived independently from an app's self-reported identity or the
/// companion host's device record. Pairing changes therefore do not move the lock namespace.
/// </summary>
public static class DeviceLeaseKeys
{
    public static string? FromIdentity(DeviceIdentity identity)
    {
        if (identity.IsEmpty)
            return null;

        var platform = Normalize(identity.Platform);
        if (string.Equals(platform, DevicePlatforms.Android, StringComparison.Ordinal) &&
            NormalizeName(identity.AvdName) is { Length: > 0 } avd)
        {
            return Hash($"{platform}|avd|{avd}");
        }
        if (Normalize(identity.Udid) is { Length: > 0 } udid)
            return Hash($"{platform}|udid|{udid}");
        if (Normalize(identity.Serial) is { Length: > 0 } serial)
            return Hash($"{platform}|native|{serial}");
        return null;
    }

    public static string? FromTarget(DeviceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var platform = Normalize(target.Platform);
        if (string.Equals(platform, DevicePlatforms.Android, StringComparison.Ordinal) &&
            NormalizeName(target.AvdName) is { Length: > 0 } avd)
        {
            return Hash($"{platform}|avd|{avd}");
        }
        if (Normalize(target.Udid) is { Length: > 0 } udid)
            return Hash($"{platform}|udid|{udid}");
        if (Normalize(target.NativeId) is { Length: > 0 } native)
            return Hash($"{platform}|native|{native}");
        return null;
    }

    private static string Hash(string material) =>
        "device:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant()[..32];

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

    private static string NormalizeName(string? value)
    {
        var normalized = Normalize(value);
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }
}

/// <summary>
/// Joins an app's self-reported <see cref="DeviceIdentity"/> to a <see cref="DeviceTarget"/>.
/// <para>
/// Kept as pure, testable logic separate from any transport because getting this wrong is
/// invisible: a mismatched pair produces an overlay drawn over the wrong device rather than an
/// error, which is far harder to notice than a crash.
/// </para>
/// </summary>
public static class DeviceIdentityMatcher
{
    /// <summary>Scores one identity against one device.</summary>
    public static DeviceMatchConfidence Match(DeviceIdentity identity, DeviceTarget device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (identity.IsEmpty)
            return DeviceMatchConfidence.None;

        // Identifiers and user-chosen names are only meaningful inside one platform's device
        // namespace. A coincidental cross-platform match must never pair an app to the wrong
        // simulator or emulator.
        if (!string.IsNullOrWhiteSpace(identity.Platform) &&
            !string.IsNullOrWhiteSpace(device.Platform) &&
            !Same(identity.Platform, device.Platform))
        {
            return DeviceMatchConfidence.None;
        }

        // A UDID or serial is what the platform's own tooling uses to address the device, so an
        // equal value here is as strong as identity gets.
        if (Same(identity.Udid, device.Udid) || Same(identity.Udid, device.NativeId))
            return DeviceMatchConfidence.Exact;

        if (Same(identity.Serial, device.NativeId) || Same(identity.Serial, device.Udid))
            return DeviceMatchConfidence.Exact;

        // An AVD name is user-chosen and only unique within one machine's AVD set, so it is a
        // fallback rather than proof. It is also the one signal where the two sides can disagree
        // on separators — the emulator property and the device host do not always agree on
        // underscores versus spaces — so it is compared on a normalised form.
        if (SameName(identity.AvdName, device.AvdName) || SameName(identity.AvdName, device.Name))
            return DeviceMatchConfidence.Weak;

        return DeviceMatchConfidence.None;
    }

    /// <summary>
    /// Picks the best device for an identity, or <c>null</c> when nothing matches.
    /// <para>
    /// An ambiguous weak match resolves to <c>null</c> rather than guessing: pairing the wrong
    /// device silently is worse than not pairing at all, because every coordinate that follows
    /// would be applied to the wrong screen.
    /// </para>
    /// </summary>
    public static DeviceTarget? FindBest(DeviceIdentity identity, IEnumerable<DeviceTarget> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (identity.IsEmpty)
            return null;

        DeviceTarget? best = null;
        var bestConfidence = DeviceMatchConfidence.None;
        var bestIsAmbiguous = false;

        foreach (var device in devices)
        {
            var confidence = Match(identity, device);
            if (confidence == DeviceMatchConfidence.None)
                continue;

            if (confidence > bestConfidence)
            {
                best = device;
                bestConfidence = confidence;
                bestIsAmbiguous = false;
            }
            else if (confidence == bestConfidence && !ReferenceEquals(best, device))
            {
                bestIsAmbiguous = true;
            }
        }

        if (best is null)
            return null;

        // Two devices matching equally well on an exact identifier means the identifiers are not
        // as unique as assumed; refuse rather than pick arbitrarily.
        return bestIsAmbiguous ? null : best;
    }

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares two human-facing device names, treating underscores and spaces as equivalent.
    /// Applied only to names, never to identifiers, where an exact comparison is the point.
    /// </summary>
    private static bool SameName(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string value) => value.Trim().Replace('_', ' ');
}
