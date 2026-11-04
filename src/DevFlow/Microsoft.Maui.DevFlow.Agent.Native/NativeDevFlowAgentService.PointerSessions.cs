namespace Microsoft.Maui.DevFlow.Agent.Native;

public partial class NativeDevFlowAgentService
{
    protected override PointerStreamSupport GetPointerStreamSupport()
        => NativeUi.PointerSessionsSupported
            ? new PointerStreamSupport(
                Supported: true,
                Fidelity: "native",
                RequiresOptIn: false,
                SupportsCoalescedSamples: true,
                SupportsPressure: false,
                SupportsButtons: false,
                PointerTypes: ["touch"],
                Reason: null)
            : PointerStreamSupport.Unsupported(NativeUi.PointerSessionUnsupportedReason);

    protected override Task<PointerSessionBackendStartResult> StartPointerSessionBackendAsync(
        PointerSessionStartRequest request,
        CancellationToken cancellationToken)
        => DispatchAsync(() =>
        {
            if (!_bound)
                return PointerSessionBackendStartResult.Failed("Agent not bound to app");

            return NativeUi.TryStartPointerSession(
                request.X,
                request.Y,
                out var state,
                out var detail,
                out var error)
                && state is not null
                    ? PointerSessionBackendStartResult.Started(
                        state,
                        detail ?? "Native pointer session started.")
                    : PointerSessionBackendStartResult.Failed(
                        error ?? "The native backend rejected the pointer start.",
                        NativeUi.PointerSessionsSupported ? 422 : 501,
                        NativeUi.PointerSessionsSupported ? "pointer-start-rejected" : "not_supported");
        }, cancellationToken);

    protected override Task<PointerSessionBackendResult> MovePointerSessionBackendAsync(
        object state,
        IReadOnlyList<PointerSessionSample> samples,
        CancellationToken cancellationToken)
        => DispatchAsync(() =>
        {
            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeUi.TryMovePointerSession(
                        state,
                        sample.X,
                        sample.Y,
                        sample.ElapsedMs,
                        out var error))
                {
                    return PointerSessionBackendResult.Failed(
                        error ?? "The native backend rejected the pointer move.");
                }
            }

            return PointerSessionBackendResult.Applied(
                $"Applied {samples.Count} coalesced native pointer sample(s).");
        }, cancellationToken);

    protected override Task<PointerSessionBackendResult> EndPointerSessionBackendAsync(
        object state,
        PointerSessionSample sample,
        CancellationToken cancellationToken)
        => DispatchAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeUi.TryEndPointerSession(
                state,
                sample.X,
                sample.Y,
                sample.ElapsedMs,
                out var detail,
                out var error)
                ? PointerSessionBackendResult.Applied(detail)
                : PointerSessionBackendResult.Failed(
                    error ?? "The native backend rejected the pointer end.");
        }, cancellationToken);

    protected override Task<PointerSessionBackendResult> CancelPointerSessionBackendAsync(
        object state,
        string reason)
        => DispatchAsync(() =>
            NativeUi.TryCancelPointerSession(
                state,
                reason,
                out var detail,
                out var error)
                ? PointerSessionBackendResult.Applied(detail)
                : PointerSessionBackendResult.Failed(
                    error ?? "The native backend rejected the pointer cancel.",
                    500,
                    "pointer-cancel-failed"));
}
