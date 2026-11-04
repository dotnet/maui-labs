namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class MauiDevFlowAgentService
{
    protected override async Task<HttpResponse?> PreparePointerSessionStartAsync(
        HttpRequest request,
        PointerSessionStartRequest body)
    {
        if (_app is null)
            return HttpResponse.Error("Agent not bound to app");

        if (await PrepareUiMutationAsync(request, body) is { } staleCapture)
            return staleCapture;

        var capture = GetReservedCapture(request);
        if (body.WindowId.HasValue
            && capture.WindowId.HasValue
            && body.WindowId.Value != capture.WindowId.Value)
        {
            return HttpResponse.Error(
                $"Capture epoch {capture.Epoch} belongs to window {capture.WindowId}, not window {body.WindowId.Value}.",
                statusCode: 409,
                reason: "capture-window-mismatch",
                details: new Dictionary<string, object?>
                {
                    ["captureEpoch"] = capture.Epoch,
                    ["captureWindowId"] = capture.WindowId,
                    ["requestedWindowId"] = body.WindowId
                });
        }

        return null;
    }
}
