namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class MauiDevFlowAgentService
{
    protected override Task<HttpResponse?> PreparePointerSessionStartAsync(
        HttpRequest request,
        PointerSessionStartRequest body)
    {
        if (_app is null)
            return Task.FromResult<HttpResponse?>(HttpResponse.Error("Agent not bound to app"));

        var reservation = ReserveUiCapture(body, consume: false);
        if (reservation.Error is not null)
            return Task.FromResult<HttpResponse?>(reservation.Error);
        if (reservation.Capture.Epoch > 0)
            request.MutationState = reservation.Capture;

        var capture = GetReservedCapture(request);
        if (body.WindowId.HasValue
            && capture.WindowId.HasValue
            && body.WindowId.Value != capture.WindowId.Value)
        {
            return Task.FromResult<HttpResponse?>(HttpResponse.Error(
                $"Capture epoch {capture.Epoch} belongs to window {capture.WindowId}, not window {body.WindowId.Value}.",
                statusCode: 409,
                reason: "capture-window-mismatch",
                details: new Dictionary<string, object?>
                {
                    ["captureEpoch"] = capture.Epoch,
                    ["captureWindowId"] = capture.WindowId,
                    ["requestedWindowId"] = body.WindowId
                }));
        }

        return Task.FromResult<HttpResponse?>(null);
    }
}
