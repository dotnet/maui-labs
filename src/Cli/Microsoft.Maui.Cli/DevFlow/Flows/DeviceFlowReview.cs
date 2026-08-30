using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;
using TestingFlow = Microsoft.Maui.DevFlow.Testing.MauiFlow;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Produces the bounded, deterministic review projection for device mutations embedded in a flow.
/// </summary>
internal sealed record DeviceFlowReview(
    IReadOnlyList<string> Effects,
    string Digest,
    string? Error = null,
    bool RecordDeviceRun = false)
{
    public bool Valid => Error is null;

    public static DeviceFlowReview Describe(
        TestingFlow? flow,
        bool includeDeviceRecording = false)
    {
        if (flow is null)
        {
            return includeDeviceRecording
                ? Invalid("A flow is required to review device recording.")
                : new DeviceFlowReview([], string.Empty);
        }

        var flowRecordsDevice = (flow.ExpectedEvidence ?? []).Any(expected =>
            string.Equals(
                expected?.Kind,
                Microsoft.Maui.DevFlow.Testing.MauiFlowEvidenceKinds.DeviceRecording,
                StringComparison.Ordinal));
        var recordsDevice = flowRecordsDevice || includeDeviceRecording;
        var extensions = flow.ExtensionData;
        if (!recordsDevice &&
            (extensions is null ||
             !extensions.ContainsKey(DevicePreconditions.ExtensionKey) &&
             !extensions.ContainsKey(DeviceStep.ExtensionKey)))
        {
            return new DeviceFlowReview(
                [],
                ReviewDigest(flow, includeDeviceRecording),
                RecordDeviceRun: false);
        }

        var effects = new List<string>();
        if (recordsDevice)
            effects.Add("Record the surrounding device screen for the full run");
        if (extensions?.TryGetValue(DevicePreconditions.ExtensionKey, out var preconditionElement) == true)
        {
            if (preconditionElement.ValueKind != JsonValueKind.Object)
                return Invalid("devicePreconditions must be a non-null object.");

            DevicePreconditions? preconditions;
            try
            {
                preconditions = DevicePreconditions.FromExtensionData(extensions);
            }
            catch (JsonException)
            {
                return Invalid("devicePreconditions contains invalid JSON.");
            }
            if (preconditions is null)
                return Invalid("devicePreconditions must be a non-null object.");

            foreach (var (permission, state) in (preconditions.Permissions ?? [])
                .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                effects.Add($"Permission {Bounded(permission, 128)} -> {Bounded(state, 32)} for the selected app");
            }
            if (preconditions.Location is { } location)
            {
                effects.Add(
                    $"Simulated location -> {location.Latitude.ToString("R", CultureInfo.InvariantCulture)}, " +
                    location.Longitude.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (preconditions.ClearLocation)
                effects.Add("Clear simulated location");
            if (!string.IsNullOrWhiteSpace(preconditions.Network))
                effects.Add($"Network profile -> {Bounded(preconditions.Network, 64)}");
            if (preconditions.Battery is { } battery)
                effects.Add($"Battery level -> {battery}%");
            if (!string.IsNullOrWhiteSpace(preconditions.Orientation))
                effects.Add($"Device orientation -> {Bounded(preconditions.Orientation, 64)}");
        }

        if (!DeviceStep.TryReadFromExtensionData(extensions, out var steps, out var stepError))
            return Invalid(stepError ?? "deviceSteps is invalid.");

        foreach (var step in steps)
        {
            var when = step.AfterStep == 0 ? "before app step 1" : $"after app step {step.AfterStep}";
            var target = !string.IsNullOrWhiteSpace(step.NativeId)
                ? $"native id {Quoted(step.NativeId, 160)}"
                : !string.IsNullOrWhiteSpace(step.NativeText)
                    ? $"native text {Quoted(step.NativeText, 160)}"
                    : "coordinate fallback only";
            effects.Add(
                $"Device tap {when}: {target} at " +
                $"({step.X.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"{step.Y.ToString("R", CultureInfo.InvariantCulture)})" +
                (step.IsFragile ? " [fragile]" : ""));
        }

        if (effects.Count > 32 ||
            Encoding.UTF8.GetByteCount(string.Join("; ", effects)) > 768)
            return Invalid("The flow declares too many device effects to review safely.");

        return new DeviceFlowReview(
            effects,
            ReviewDigest(flow, includeDeviceRecording),
            RecordDeviceRun: recordsDevice);
    }

    private static DeviceFlowReview Invalid(string error) =>
        new([], string.Empty, error);

    private static string ReviewDigest(TestingFlow flow, bool includeDeviceRecording)
    {
        var flowDigest = Microsoft.Maui.DevFlow.Testing.MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"devflow-device-review-v1\n{flowDigest}\ninspector-recording-consent:{includeDeviceRecording}")))
            .ToLowerInvariant();
    }

    private static string Quoted(string value, int maximum) =>
        $"\"{Bounded(value, maximum).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string Bounded(string? value, int maximum)
    {
        var safe = new string((value ?? "")
            .Where(character => !char.IsControl(character))
            .Take(maximum)
            .ToArray());
        return safe.Length == 0 ? "(empty)" : safe;
    }
}
