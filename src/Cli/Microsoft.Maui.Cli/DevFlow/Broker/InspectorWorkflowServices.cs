using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Broker-owned services exposed to one Inspector target. This is deliberately an adapter over
/// the coordinator and trust stores: it does not implement replay, artifact parsing, or policy.
/// </summary>
internal sealed class InspectorWorkflowServices
{
    private readonly WorkflowRunCoordinator _runs;
    private readonly ArtifactTrustImportService _imports;
    private readonly ArtifactTrustStore _artifacts;
    private readonly WorkflowRunTarget _target;
    private readonly string _dispatchTicket;
    private readonly Func<bool> _isTargetCurrent;
    private readonly CancellationToken _cancellationToken;

    public InspectorWorkflowServices(
        WorkflowRunCoordinator runs,
        ArtifactTrustImportService imports,
        ArtifactTrustStore artifacts,
        WorkflowRunTarget target,
        string dispatchTicket,
        Func<bool> isTargetCurrent,
        CancellationToken cancellationToken)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _imports = imports ?? throw new ArgumentNullException(nameof(imports));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dispatchTicket = dispatchTicket ?? throw new ArgumentNullException(nameof(dispatchTicket));
        _isTargetCurrent = isTargetCurrent ?? throw new ArgumentNullException(nameof(isTargetCurrent));
        _cancellationToken = cancellationToken;
    }

    public WorkflowRunTarget Target => _target;

    public bool IsTargetCurrent() => _isTargetCurrent();

    public WorkflowRunCapabilitiesResponse GetCapabilities() => _runs.GetCapabilities();

    public WorkflowRunPreflightResult Preflight(WorkflowRunStartRequest request)
        => _runs.Preflight(request, _target, _isTargetCurrent);

    public WorkflowRunStartResult Start(
        WorkflowRunStartRequest request,
        Func<AgentClient, IFlowReplayEvidenceCapture?>? evidenceCaptureFactory = null,
        WorkflowRunLeaseHandoff? leaseHandoff = null,
        bool recordDeviceRun = false,
        Action<string, string>? deviceRecordingCaptured = null)
    {
        if (!_isTargetCurrent())
        {
            return WorkflowRunStartResult.Conflict(
                "The requested agent instance is stale or no longer connected.");
        }

        return _runs.Start(
            request,
            _target,
            _isTargetCurrent,
            new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = evidenceCaptureFactory,
                ReproductionExpectation = request.ReproductionExpectation,
                RecordDeviceRun = recordDeviceRun,
                DeviceRecordingCaptured = deviceRecordingCaptured,
            },
            leaseHandoff,
            dispatchOrigin: WorkflowRunDispatchOrigin.InspectorWorkbench,
            dispatchTicket: _dispatchTicket);
    }

    public WorkflowRunAccessResult GetRunStatus(string runId, string? capabilityToken)
        => _runs.GetStatus(runId, capabilityToken);

    public WorkflowRunRepairContextResult GetRunRepairContext(string runId, string? capabilityToken)
    {
        var result = _runs.GetRepairContext(runId, capabilityToken);
        if (result.Context is null)
            return result;
        if (!string.Equals(result.Context.Target.AgentId, _target.AgentId, StringComparison.Ordinal) ||
            !string.Equals(
                result.Context.Target.AgentInstanceId,
                _target.AgentInstanceId,
                StringComparison.Ordinal))
        {
            return WorkflowRunRepairContextResult.Unavailable(
                "The broker-owned run does not target this exact Inspector app instance.");
        }
        return result;
    }

    public WorkflowRunCancelResult CancelRun(string runId, string? capabilityToken)
        => _runs.Cancel(runId, capabilityToken);

    public WorkflowRunPriorSelectorResolutionResult GetPriorSelectorResolution(
        string sourceRunId,
        string sourceStepId)
        => _runs.GetPriorSelectorResolution(sourceRunId, sourceStepId);

    public InspectorArtifactTrustResult ImportArtifact(ReadOnlyMemory<byte> bytes, string? kind)
    {
        if (!ArtifactTrustImportKinds.IsKnown(kind))
        {
            return InspectorArtifactTrustResult.Failure(
                400,
                "An explicit supported artifact kind is required.");
        }

        var imported = _imports.Import(bytes, kind!, policy: null, verifiedProvenance: null, _cancellationToken);
        if (!imported.Ok || imported.Artifact is null)
        {
            return InspectorArtifactTrustResult.Failure(
                400,
                imported.Error ?? "The artifact could not be imported.");
        }

        var stored = _artifacts.Add(imported.Artifact);
        return !stored.Ok
            ? InspectorArtifactTrustResult.Failure(
                409,
                stored.Error ?? "The artifact could not be retained.")
            : InspectorArtifactTrustResult.Success(
                201,
                new ArtifactTrustRouteResponse
                {
                    Ok = true,
                    CapabilityToken = stored.CapabilityToken,
                    Status = stored.Status,
                });
    }

    public ArtifactTrustStoreReadResult GetArtifactStatus(string artifactId, string? capabilityToken)
        => _artifacts.GetStatus(artifactId, capabilityToken);

    public ArtifactTrustStoreReadResult GetArtifactProjection(string artifactId, string? capabilityToken)
        => _artifacts.GetSafeProjection(artifactId, capabilityToken);

    public ArtifactTrustStoreRepairResult GetArtifactRepairTrust(
        string artifactId,
        string? capabilityToken,
        string localRunId)
        => _artifacts.GetRepairTrust(artifactId, capabilityToken, localRunId);

    public ArtifactTrustStoreBindResult BindLocalReproduction(
        string artifactId,
        string? capabilityToken,
        MauiLocalReproductionExpectation current,
        string localRunId)
    {
        var local = _runs.GetLocalReproductionFacts(localRunId);
        if (!local.Ok || local.Facts is null)
        {
            return new ArtifactTrustStoreBindResult
            {
                StatusCode = 409,
                Error = local.Error ?? "The local run cannot establish reproduction facts.",
            };
        }

        return _artifacts.BindLocalReproduction(
            artifactId,
            capabilityToken,
            local.Facts,
            current);
    }

}

internal sealed class WorkflowRepairVerificationAccess
{
    public string? RunId { get; init; }
    public string? CapabilityToken { get; init; }
}

internal sealed class InspectorArtifactTrustResult
{
    public int StatusCode { get; private init; }
    public ArtifactTrustRouteResponse Response { get; private init; } = ArtifactTrustRouteResponse.Failure("Unknown error.");

    public static InspectorArtifactTrustResult Success(int statusCode, ArtifactTrustRouteResponse response)
        => new() { StatusCode = statusCode, Response = response };

    public static InspectorArtifactTrustResult Failure(int statusCode, string error)
        => Success(statusCode, ArtifactTrustRouteResponse.Failure(error));
}
