using System.Security.Cryptography;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Devices;

/// <summary>
/// Correlates an agent layout report with what the device around the app can independently see.
/// <para>
/// The agent's visual tree only contains what MAUI drew. A permission dialog, a share sheet, or
/// the software keyboard is invisible to it, so a finding that says "this control is not hittable"
/// carries no cause. The device layer can see those surfaces, and this composer attaches them to
/// the report as <c>systemEvidence</c>.
/// </para>
/// <para>
/// Three rules make that safe. It is <em>optional</em>: with no device host, no exact pairing, or
/// no hierarchy from the host, the result is <c>unavailable</c> with the reason stated, and the
/// report is otherwise untouched. It is <em>truthful</em>: evidence is only marked
/// <c>complete</c> when the same agent instance produced both scans, the tree did not change
/// across the device capture, the device's own identity and display metadata still match the
/// pairing, and the two captures are within the allowed skew — otherwise it is <c>incomplete</c>
/// and every element is dropped, because a hierarchy captured against different geometry cannot be
/// aligned with these findings. It is <em>bounded</em>: element and limitation counts and every
/// string are capped.
/// </para>
/// <para>
/// It deliberately does not add, remove, or rewrite findings, and does not recompute the agent's
/// diagnostics revision. Finding authorship stays with the agent, which is the only component that
/// holds the reviewed suppression policy for this scan.
/// </para>
/// </summary>
internal static class LayoutSystemEvidenceComposer
{
    /// <summary>Maximum external elements carried in one report.</summary>
    internal const int MaxElements = 100;

    /// <summary>Maximum limitation strings carried in one report.</summary>
    internal const int MaxLimitations = 32;

    /// <summary>How far apart the app and device captures may be and still be correlated.</summary>
    internal const double DefaultMaxSkewMs = 1000;

    private const int MaxIdLength = 256;
    private const int MaxLabelLength = 128;
    private const int MaxLimitationLength = 512;

    private const string ScreenshotLimitation =
        "Screenshots provide metadata only and do not independently prove visual occlusion.";

    /// <summary>
    /// The evidence to attach when the device layer cannot be consulted at all.
    /// </summary>
    public static LayoutSystemEvidence Unavailable(string reason) =>
        new()
        {
            Status = "unavailable",
            GeometryStable = false,
            Limitations = Bound([reason], MaxLimitations),
        };

    /// <summary>
    /// Builds the evidence for one correlated capture.
    /// </summary>
    /// <param name="report">The report the agent produced first.</param>
    /// <param name="recheck">
    /// An immediate second scan from the same agent, used to prove the tree did not move while the
    /// device was captured. Null means the recheck could not be taken, which is never accepted.
    /// </param>
    /// <param name="deviceId">The exact paired device the capture came from.</param>
    /// <param name="display">The display geometry the pairing recorded, for a metadata cross-check.</param>
    /// <param name="snapshot">The device hierarchy, or null when the host could not provide one.</param>
    /// <param name="screenshot">The device screenshot bytes, or null when none was produced.</param>
    /// <param name="appPackageId">The package identity of the app under inspection, when known.</param>
    /// <param name="identityStable">Whether the same agent instance served both scans.</param>
    /// <param name="maxSkewMs">Allowed distance between the app and device captures.</param>
    public static LayoutSystemEvidence Compose(
        LayoutDiagnosticsReport report,
        LayoutDiagnosticsReport? recheck,
        string deviceId,
        DisplayGeometry? display,
        DeviceUiSnapshot? snapshot,
        byte[]? screenshot,
        string? appPackageId,
        bool identityStable,
        double maxSkewMs = DefaultMaxSkewMs)
    {
        ArgumentNullException.ThrowIfNull(report);

        var metadataStable = IsDeviceMetadataStable(snapshot, deviceId, display);
        var capturedAt = snapshot?.CapturedAt;
        var correlatable = Accept(
            report,
            recheck,
            capturedAt,
            maxSkewMs,
            identityStable,
            out var geometryStable,
            out var skewMs);
        var accepted = snapshot is not null && metadataStable && correlatable;

        var elements = accepted
            ? ProjectExternalElements(snapshot!, appPackageId)
            : [];

        // DevFlow's own caveats are built first and bounded separately. A host that returns dozens
        // of junk limitation strings must not be able to evict the reasons this evidence was
        // downgraded — that would leave a reader with "incomplete" and no explanation.
        var limitations = new List<string>();
        if (!identityStable)
            limitations.Add("The connected agent instance changed during the device capture.");
        if (!geometryStable)
            limitations.Add("The app geometry changed during the device capture.");
        if (snapshot is not null && !metadataStable)
        {
            limitations.Add(
                "The device identity, orientation, or display scale was missing or changed during the capture.");
        }
        if (string.IsNullOrWhiteSpace(appPackageId))
        {
            limitations.Add(
                "The app package identity was unavailable, so foreground-owner and package-mismatch evidence was not used.");
        }
        if (snapshot is not null && double.IsFinite(skewMs) && skewMs > maxSkewMs)
        {
            limitations.Add(
                $"The device evidence was captured {Math.Round(skewMs)} ms from the app snapshot.");
        }
        if (snapshot is not null && !double.IsFinite(skewMs))
            limitations.Add("The app snapshot did not carry a usable capture timestamp.");
        if (accepted && elements.Count == MaxElements)
            limitations.Add($"Only the first {MaxElements} external elements are reported.");
        if (snapshot is null)
            limitations.Add("The device host did not provide a UI hierarchy.");
        limitations.Add(ScreenshotLimitation);

        var bounded = Bound(limitations, MaxLimitations);
        if (snapshot is not null && bounded.Count < MaxLimitations)
        {
            foreach (var hostLimitation in Bound(snapshot.Limitations, MaxLimitations - bounded.Count))
            {
                if (!bounded.Contains(hostLimitation, StringComparer.Ordinal))
                    bounded.Add(hostLimitation);
            }
        }

        return new LayoutSystemEvidence
        {
            Status = snapshot is null ? "unavailable" : accepted ? "complete" : "incomplete",
            DeviceId = Bound(deviceId, MaxIdLength),
            CapturedAt = capturedAt?.ToUniversalTime().ToString("O"),
            CaptureSkewMs = double.IsFinite(skewMs) ? skewMs : null,
            GeometryStable = geometryStable,
            ForegroundOwner = Bound(snapshot?.ForegroundOwner, MaxIdLength),
            KeyboardVisible = snapshot?.KeyboardVisible,
            ScreenshotCaptured = screenshot is { Length: > 0 },
            ScreenshotDigest = screenshot is { Length: > 0 }
                ? Convert.ToHexString(SHA256.HashData(screenshot)).ToLowerInvariant()
                : null,
            Elements = elements,
            Limitations = bounded,
        };
    }

    /// <summary>
    /// Whether the two captures describe the same moment closely enough to be correlated.
    /// </summary>
    internal static bool Accept(
        LayoutDiagnosticsReport report,
        LayoutDiagnosticsReport? recheck,
        DateTimeOffset? deviceCapturedAt,
        double maxSkewMs,
        bool identityStable,
        out bool geometryStable,
        out double captureSkewMs)
    {
        // A missing recheck is not "probably fine". Without a second tree revision there is no
        // evidence the app held still, so the capture cannot be called stable.
        geometryStable = recheck is not null &&
            !string.IsNullOrEmpty(report.Snapshot.TreeRevision) &&
            string.Equals(
                report.Snapshot.TreeRevision,
                recheck.Snapshot.TreeRevision,
                StringComparison.Ordinal);

        captureSkewMs = deviceCapturedAt is { } captured &&
            DateTimeOffset.TryParse(
                report.Snapshot.CapturedAt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var agentCapturedAt)
            ? Math.Abs((captured - agentCapturedAt).TotalMilliseconds)
            : double.PositiveInfinity;

        return identityStable &&
            geometryStable &&
            double.IsFinite(captureSkewMs) &&
            captureSkewMs <= maxSkewMs;
    }

    /// <summary>
    /// Whether the hierarchy came from the device the pairing named, still in the geometry the
    /// pairing recorded. A hierarchy from a different device, or from the same device after it
    /// rotated, describes a screen these findings were not measured against.
    /// </summary>
    internal static bool IsDeviceMetadataStable(
        DeviceUiSnapshot? snapshot,
        string deviceId,
        DisplayGeometry? display)
        => snapshot is not null &&
            string.Equals(snapshot.DeviceId, deviceId, StringComparison.Ordinal) &&
            display is not null &&
            !string.IsNullOrWhiteSpace(snapshot.Orientation) &&
            string.Equals(snapshot.Orientation, display.Orientation, StringComparison.OrdinalIgnoreCase) &&
            snapshot.Scale is { } scale &&
            Math.Abs(scale - display.Scale) <= 0.001;

    /// <summary>
    /// The elements that belong to something other than the app under inspection. Elements the
    /// host did not attribute to a package are only treated as external when the host itself
    /// marked them as system UI, so an unattributed app control is never reported as an intruder.
    /// </summary>
    private static List<LayoutSystemElement> ProjectExternalElements(
        DeviceUiSnapshot snapshot,
        string? appPackageId)
        => snapshot.Elements
            .Where(element => IsExternal(element, appPackageId))
            .Select(element => new LayoutSystemElement
            {
                Id = Bound(element.Id, MaxIdLength) ?? "",
                Role = Bound(element.Role, MaxLabelLength),
                Type = Bound(element.Type, MaxLabelLength),
                PackageId = Bound(element.PackageId, MaxIdLength),
                Interactive = element.Interactive,
                Bounds = element.Bounds is { } bounds
                    ? new LayoutRect
                    {
                        X = bounds.X,
                        Y = bounds.Y,
                        Width = bounds.Width,
                        Height = bounds.Height,
                    }
                    : null,
            })
            .Where(element => element.Id.Length > 0)
            .Take(MaxElements)
            .ToList();

    private static bool IsExternal(DeviceUiElement element, string? appPackageId)
        => element.IsSystem ||
            (!string.IsNullOrWhiteSpace(appPackageId) &&
             !string.IsNullOrWhiteSpace(element.PackageId) &&
             !string.Equals(element.PackageId, appPackageId, StringComparison.OrdinalIgnoreCase));

    private static List<string> Bound(IEnumerable<string> values, int maximum)
        => values
            .Select(value => Bound(value, MaxLimitationLength))
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .Take(maximum)
            .Select(value => value!)
            .ToList();

    private static string? Bound(string? value, int maximum)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximum ? value : value[..maximum];
}
