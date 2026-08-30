using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Testing;
using TestingFlow = Microsoft.Maui.DevFlow.Testing.MauiFlow;
using TestingStep = Microsoft.Maui.DevFlow.Testing.FlowStep;
using TestingStepResult = Microsoft.Maui.DevFlow.Testing.FlowStepResult;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Executes flow extensions that belong to the device around the app rather than the app agent.
/// </summary>
internal sealed class DeviceFlowExecutionExtension(
    IDeviceSurface surface,
    DeviceTarget? device,
    string? appPackageId) : IMauiFlowExecutionExtension
{
    private DevicePreconditions? _preconditions;
    private IReadOnlyList<DeviceStep> _steps = [];

    public static bool HasDeclarations(TestingFlow flow) =>
        flow.ExtensionData is { } extensions &&
        (extensions.ContainsKey(DevicePreconditions.ExtensionKey) ||
         extensions.ContainsKey(DeviceStep.ExtensionKey));

    public async Task<MauiFlowExecutionExtensionResult> PrepareAsync(
        TestingFlow flow,
        CancellationToken cancellationToken)
    {
        if (flow.ExtensionData is { } extensions &&
            extensions.TryGetValue(DevicePreconditions.ExtensionKey, out var declaredPreconditions))
        {
            if (declaredPreconditions.ValueKind != JsonValueKind.Object)
            {
                return MauiFlowExecutionExtensionResult.Failed(
                    "devicePreconditions must be a non-null object.",
                    FlowFailureKinds.Validation);
            }
            try
            {
                _preconditions = DevicePreconditions.FromExtensionData(flow.ExtensionData);
            }
            catch (JsonException)
            {
                return MauiFlowExecutionExtensionResult.Failed(
                    "devicePreconditions contains invalid JSON.",
                    FlowFailureKinds.Validation);
            }
            if (_preconditions is null)
            {
                return MauiFlowExecutionExtensionResult.Failed(
                    "devicePreconditions must be a non-null object.",
                    FlowFailureKinds.Validation);
            }
        }
        else
            _preconditions = null;

        if (!DeviceStep.TryReadFromExtensionData(flow.ExtensionData, out _steps, out var stepError))
        {
            return MauiFlowExecutionExtensionResult.Failed(
                stepError ?? "deviceSteps is invalid.",
                FlowFailureKinds.Validation);
        }

        if (_preconditions is null && _steps.Count == 0)
            return MauiFlowExecutionExtensionResult.Ok;
        if (device is null)
        {
            return MauiFlowExecutionExtensionResult.Failed(
                "The flow declares device work, but its app is not paired to exactly one device at "
                + "exact confidence. A weaker pairing is a guess about which emulator or simulator "
                + "this app is in, and device work is not run on a guess.",
                FlowFailureKinds.Drive);
        }

        var sequences = (flow.Steps ?? [])
            .Where(step => step is not null)
            .Select(step => step!.Seq)
            .ToHashSet();
        var missing = _steps.FirstOrDefault(step => step.AfterStep != 0 && !sequences.Contains(step.AfterStep));
        if (missing is not null)
        {
            return MauiFlowExecutionExtensionResult.Failed(
                $"A device step refers to missing flow step {missing.AfterStep}.",
                FlowFailureKinds.Validation);
        }

        if (_preconditions is not null && !_preconditions.IsEmpty)
        {
            if (_preconditions.Permissions is { Count: > 0 } && string.IsNullOrWhiteSpace(appPackageId))
            {
                return MauiFlowExecutionExtensionResult.Failed(
                    "Permission preconditions require the exact app package or bundle identifier.",
                    FlowFailureKinds.Drive);
            }

            var prepared = string.IsNullOrWhiteSpace(appPackageId)
                ? await DevicePreconditionApplier
                    .ApplyAsync(surface, device, _preconditions, cancellationToken)
                    .ConfigureAwait(false)
                : await DevicePreconditionApplier
                    .ApplyAsync(surface, device, _preconditions, appPackageId, cancellationToken)
                    .ConfigureAwait(false);
            if (!prepared.Success)
            {
                return MauiFlowExecutionExtensionResult.Failed(
                    prepared.Reason ?? "The device preconditions could not be established.",
                    FlowFailureKinds.Drive);
            }
        }

        return await DriveStepsAsync(afterStep: 0, cancellationToken).ConfigureAwait(false);
    }

    public Task<MauiFlowExecutionExtensionResult> AfterStepAsync(
        TestingFlow flow,
        TestingStep step,
        TestingStepResult result,
        CancellationToken cancellationToken) =>
        DriveStepsAsync(step.Seq, cancellationToken);

    private async Task<MauiFlowExecutionExtensionResult> DriveStepsAsync(
        int afterStep,
        CancellationToken cancellationToken)
    {
        if (device is null)
            return MauiFlowExecutionExtensionResult.Ok;

        foreach (var step in _steps.Where(step => step.AfterStep == afterStep))
        {
            DeviceOperationResult? semantic = null;
            if (!string.IsNullOrWhiteSpace(step.NativeId) || !string.IsNullOrWhiteSpace(step.NativeText))
            {
                semantic = await surface
                    .TapUiAsync(device.Id, step.NativeId, step.NativeText, cancellationToken)
                    .ConfigureAwait(false);
                if (semantic.Success)
                    continue;
                if (semantic.FailureKind == DeviceOperationFailureKind.Ambiguous)
                {
                    return MauiFlowExecutionExtensionResult.Failed(
                        $"Device step after flow step {afterStep} was ambiguous: {semantic.Reason}",
                        FlowFailureKinds.Drive);
                }
                if (semantic.FailureKind != DeviceOperationFailureKind.NotFound)
                {
                    return MauiFlowExecutionExtensionResult.Failed(
                        $"Device step after flow step {afterStep} could not safely fall back to coordinates: "
                        + $"{semantic.Reason ?? "native completion was not confirmed"}",
                        FlowFailureKinds.Drive);
                }
            }

            var coordinate = await surface
                .TapAsync(device.Id, new DevicePoint(step.X, step.Y), cancellationToken)
                .ConfigureAwait(false);
            if (!coordinate.Success)
            {
                var semanticReason = semantic?.Reason is { Length: > 0 }
                    ? $" Native lookup also failed: {semantic.Reason}"
                    : "";
                return MauiFlowExecutionExtensionResult.Failed(
                    $"Device step after flow step {afterStep} failed: " +
                    $"{coordinate.Reason ?? "the device refused the tap"}.{semanticReason}",
                    FlowFailureKinds.Drive);
            }
        }

        return MauiFlowExecutionExtensionResult.Ok;
    }
}
