using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Mutation-lease and workflow-recording plumbing. Framework neutral: the lease coordinator and
/// the broker handshake do not depend on a UI framework, while the observation payload that
/// describes *what* was mutated is produced by <see cref="CreateMutationObservationAsync"/>,
/// which backends override.
/// </summary>
public partial class DevFlowAgentService
{
    private readonly MutationLeaseCoordinator _mutationLease;

    private readonly MutationRecordingTracker _mutationRecording = new();

    private async Task<MutationLeaseStatus> ValidateMutationLeaseAsync(HttpRequest request)
    {
        if (!_options.RequireMutationLease)
            return MutationLeaseStatus.Unrestricted();

        request.Headers.TryGetValue("X-DevFlow-Lease", out var leaseId);
        if (string.IsNullOrWhiteSpace(leaseId))
            request.Headers.TryGetValue("X-DevFlow-Writer", out leaseId);
        return await _mutationLease.ValidateAsync(leaseId).ConfigureAwait(false);
    }

    private async Task<HttpResponse> HandleMutationLeaseControl(HttpRequest request)
    {
        var body = request.BodyAs<MutationLeaseRequest>() ?? new MutationLeaseRequest();
        if (string.IsNullOrWhiteSpace(body.LeaseId))
        {
            request.Headers.TryGetValue("X-DevFlow-Lease", out var leaseId);
            if (string.IsNullOrWhiteSpace(leaseId))
                request.Headers.TryGetValue("X-DevFlow-Writer", out leaseId);
            body.LeaseId = leaseId;
        }

        body.Action = string.IsNullOrWhiteSpace(body.Action)
            ? "status"
            : body.Action.Trim().ToLowerInvariant();
        var result = await _mutationLease.ControlAsync(body).ConfigureAwait(false);
        return HttpResponse.Json(result);
    }

    private async Task<HttpResponse> HandleMutationRecordingControl(HttpRequest request)
    {
        var body = request.BodyAs<MutationRecordingRequest>() ?? new MutationRecordingRequest();
        body.Action = string.IsNullOrWhiteSpace(body.Action)
            ? "status"
            : body.Action.Trim().ToLowerInvariant();
        var leaseId = request.MutationLease?.LeaseId;
        if (!string.Equals(body.Action, "status", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(leaseId))
            return HttpResponse.Error("A mutation lease is required to control recording.", 409, "lease");

        body.LeaseId = leaseId;
        var broker = _brokerRegistration;
        if (broker?.HasBrokerAuthority != true)
            return HttpResponse.Error("Workflow recording requires the DevFlow broker.", 503, "broker");

        var result = await broker.ControlMutationRecordingAsync(body).ConfigureAwait(false);
        UpdateMutationRecordingState(result);
        return result is null
            ? HttpResponse.Error("The DevFlow broker did not respond.", 503, "broker")
            : HttpResponse.Json(result);
    }

    private static bool IsMutationRecordingStatusRequest(HttpRequest request)
    {
        try
        {
            var body = request.BodyAs<MutationRecordingRequest>();
            return body is null || string.IsNullOrWhiteSpace(body.Action) ||
                string.Equals(body.Action.Trim(), "status", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ObserveMutationAsync(HttpRequest request, HttpResponse response)
    {
        var leaseId = request.MutationLease?.LeaseId;
        var broker = _brokerRegistration;
        if (string.IsNullOrWhiteSpace(leaseId) || broker?.HasBrokerAuthority != true ||
            !_mutationRecording.IsActive)
            return;

        var observation = await CreateMutationObservationAsync(request).ConfigureAwait(false);
        if (observation is null)
            return;

        var result = await broker.ControlMutationRecordingAsync(new MutationRecordingRequest
        {
            Action = "observe",
            LeaseId = leaseId,
            RecordingId = _mutationRecording.RecordingId,
            Observation = observation
        }).ConfigureAwait(false);
        UpdateMutationRecordingState(result);
    }

    private void UpdateMutationRecordingState(MutationRecordingStatus? status)
        => _mutationRecording.Update(status);

    /// <summary>
    /// Describes the mutation a request performed so the broker can append it to a recording.
    /// Backends that can resolve elements and routes override this; returning <c>null</c> means
    /// the request contributes no recordable step.
    /// </summary>
    internal virtual Task<MutationObservation?> CreateMutationObservationAsync(HttpRequest request)
        => Task.FromResult<MutationObservation?>(null);

    protected static string? ReadJsonString(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static double? ReadJsonDouble(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number)
            ? number
            : null;

    protected static int? ReadJsonInt(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number)
            ? number
            : null;
}
