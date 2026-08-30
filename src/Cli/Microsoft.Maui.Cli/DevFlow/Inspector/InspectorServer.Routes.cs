using System.Text;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

public sealed partial class InspectorServer
{
    private async Task<(int, string, byte[])> HandleGetRouteAsync(HttpRequestInfo request)
        => request.Path switch
        {
            "/" or "" => await HandleRootAsync(),
            "/api/state" => await HandleStateAsync(),
            "/api/inspect/snapshot" => await HandleInspectSnapshotAsync(),
            "/api/eventSupport" => await HandleEventSupportAsync(),
            "/api/checkpoint/status" => HandleResumeCheckpointStatus(),
            "/api/flows/replay/evidence" => HandleReplayEvidenceDownload(),
            "/screenshot.png" => await HandleScreenshotAsync(request.Query.GetValueOrDefault("frame")),
            _ when TryResolveAsset(request.Path, out var asset) =>
                HandleEmbeddedFile(asset.ResourceName, asset.ContentType),
            _ => NotFound()
        };

    private async Task<(int, string, byte[])> HandlePostRouteAsync(
        HttpRequestInfo request,
        string leaseId,
        string holderKind,
        string holderLabel)
        => request.Path switch
        {
            "/api/tap" => await HandleProxyTapAsync(request.Body),
            "/api/scroll" => await HandleProxyScrollAsync(request.Body),
            "/api/gesture" => await HandleProxyGestureAsync(request.Body),
            "/api/back" => await HandleProxyBackAsync(),
            "/api/fill" => await HandleProxyFillAsync(request.Body),
            "/api/key" => await HandleProxyKeyAsync(request.Body),
            "/api/hitTest" => await HandleProxyHitTestAsync(request.Body),
            "/api/inspect/query" => await HandleInspectQueryAsync(request.Body),
            "/api/getProperties" => await HandleProxyGetPropertiesAsync(request.Body),
            "/api/getProperty" => await HandleProxyGetPropertyAsync(request.Body),
            "/api/setProperty" => await HandleProxySetPropertyAsync(request.Body),
            "/api/persistProperty" => await HandlePersistPropertyAsync(request.Body),
            "/api/navigate" => await HandleProxyNavigateAsync(request.Body),
            "/api/checkpoint" => await HandleCheckpointAsync(request.Body),
            "/api/checkpoint/save" => await HandleResumeCheckpointAsync("save"),
            "/api/checkpoint/restore" => await HandleResumeCheckpointAsync("restore"),
            "/api/checkpoint/clear" => HandleResumeCheckpointClear(),
            "/api/source" => await HandleSourceAsync(request.Body),
            "/api/flows/record/start" => await HandleFlowRecordStartAsync(request.Body),
            "/api/flows/record/step" => await HandleFlowRecordStepAsync(request.Body),
            "/api/flows/record/stop" => await HandleFlowRecordStopAsync(request.Body),
            "/api/flows/record/cancel" => await HandleFlowRecordCancelAsync(request.Body),
            "/api/flows/record/status" => await HandleFlowRecordStatusAsync(request.Body),
            "/api/flows/files/list" => await HandleFlowFilesListAsync(),
            "/api/flows/files/load" => await HandleFlowFileLoadAsync(request.Body),
            "/api/plans/list" => await HandlePlanListAsync(),
            "/api/plans/load" => await HandlePlanLoadAsync(request.Body),
            "/api/plans/validate" => await HandlePlanValidateAsync(request.Body),
            "/api/plans/save" => await HandlePlanSaveAsync(request.Body),
            "/api/flows/validate" => await HandleFlowValidateAsync(request.Body),
            "/api/flows/diff" => await HandleFlowDiffAsync(request.Body),
            "/api/flows/commit" => await HandleFlowCommitAsync(request.Body),
            "/api/flows/selector/verify" => await HandleSelectorVerifyAsync(request.Body),
            "/api/flows/assert/verify" => await HandleAssertionVerifyAsync(request.Body),
            "/api/flows/replay" => await HandleFlowReplayAsync(
                request.Body,
                leaseId,
                holderKind,
                holderLabel),
            "/api/logs" => await HandleLogsAsync(request.Body),
            "/api/network" => await HandleNetworkAsync(request.Body),
            "/api/network/detail" => await HandleNetworkDetailAsync(request.Body),
            "/api/problems" => await HandleProblemsAsync(request.Body),
            "/api/performance/start" => await HandlePerformanceStartAsync(
                request.Body,
                leaseId,
                holderKind,
                holderLabel),
            "/api/performance/snapshot" => await HandlePerformanceSnapshotAsync(),
            "/api/performance/stop" => await HandlePerformanceStopAsync(),
            "/api/evidence/preview" => await HandleEvidencePreviewAsync(request.Body),
            "/api/evidence/capture" => await HandleEvidenceCaptureAsync(request.Body),
            "/api/preferences" => await HandlePreferencesAsync(request.Body),
            "/api/device" => await HandleDeviceAsync(request.Body),
            "/api/sensors" => await HandleSensorsAsync(request.Body),
            "/api/geolocation" => await HandleGeolocationAsync(request.Body),
            "/api/files/roots" => await HandleFilesRootsAsync(request.Body),
            "/api/files/list" => await HandleFilesListAsync(request.Body),
            "/api/alerts" => await HandleAlertsAsync(),
            "/api/alerts/dismiss" => await HandleAlertDismissAsync(request.Body),
            "/api/cdp/webviews" => await HandleCdpWebViewsAsync(request.Body),
            "/api/cdp/source" => await HandleCdpSourceAsync(request.Body),
            "/api/cdp/eval" => await HandleCdpEvalAsync(request.Body),
            "/api/control" => await HandleControlAsync(request.Body, leaseId, holderKind, holderLabel),
            _ => NotFound()
        };

    private static bool TryResolveAsset(string path, out InspectorAsset asset)
    {
        if (Assets.TryGetValue(path, out var resolved))
        {
            asset = resolved;
            return true;
        }

        asset = null!;
        return false;
    }

    private static (int, string, byte[]) NotFound()
        => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"));

    private sealed record InspectorAsset(string ResourceName, string ContentType);

    /// <summary>
    /// An Inspector panel whose browser code calls a route that only some layers serve. The page
    /// hides a panel that is not advertised here, so a tab can never fetch a route this server
    /// answers with 404. <c>InspectorPreviewFeatureFlagTests</c> probes each route and asserts the
    /// flag matches what <see cref="HandlePostRouteAsync"/> actually handles.
    /// </summary>
    internal sealed record InspectorOptionalSurface(string MetaName, string Route, bool Served);

    internal static readonly IReadOnlyList<InspectorOptionalSurface> OptionalSurfaces =
    [
        // Layout diagnostics and the managed device host are served by later layers. Their browser
        // modules ship here so the panels stay one file, but they stay hidden until the route
        // exists.
        new("devflow-surface-layout-diagnostics", "/api/diagnostics/layout", Served: false),
        new("devflow-surface-device-host", "/api/device/host", Served: false),
    ];

    /// <summary>
    /// Every browser asset this layer actually embeds. The table is deliberately exhaustive: an
    /// entry with no matching file under <c>DevFlow/Inspector/Web/</c> would advertise a page the
    /// server cannot serve, so new modules are added here only together with the file itself.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, InspectorAsset> Assets =
        new Dictionary<string, InspectorAsset>(StringComparer.Ordinal)
        {
            ["/devflow.js"] = new("devflow.js", "application/javascript"),
            ["/inspector-api.js"] = new("inspector-api.js", "application/javascript"),
            ["/inspector-dialog.js"] = new("inspector-dialog.js", "application/javascript"),
            ["/inspector-data-context.js"] = new("inspector-data-context.js", "application/javascript"),
            ["/inspector-data-controller.js"] = new("inspector-data-controller.js", "application/javascript"),
            ["/inspector-data-ui.js"] = new("inspector-data-ui.js", "application/javascript"),
            ["/inspector-diagnostics.js"] = new("inspector-diagnostics.js", "application/javascript"),
            ["/inspector-video.js"] = new("inspector-video.js", "application/javascript"),
            ["/inspector-evidence.js"] = new("inspector-evidence.js", "application/javascript"),
            ["/inspector-study.js"] = new("inspector-study.js", "application/javascript"),
            ["/inspector-agent-requests.js"] = new("inspector-agent-requests.js", "application/javascript"),
            ["/inspector-host-bridge.js"] = new("inspector-host-bridge.js", "application/javascript"),
            ["/inspector-workbench.js"] = new("inspector-workbench.js", "application/javascript"),
            ["/inspector-plan.js"] = new("inspector-plan.js", "application/javascript"),
            ["/inspector-steps.js"] = new("inspector-steps.js", "application/javascript"),
            ["/inspector-run.js"] = new("inspector-run.js", "application/javascript"),
            ["/inspector-trace.js"] = new("inspector-trace.js", "application/javascript"),
            ["/inspector-repair.js"] = new("inspector-repair.js", "application/javascript"),
            ["/inspector-improve.js"] = new("inspector-improve.js", "application/javascript"),
            ["/inspector-source.js"] = new("inspector-source.js", "application/javascript"),
            ["/inspector-properties.js"] = new("inspector-properties.js", "application/javascript"),
            ["/inspector-tree.js"] = new("inspector-tree.js", "application/javascript"),
            ["/devflow.css"] = new("devflow.css", "text/css"),
            ["/inspector-workbench.css"] = new("inspector-workbench.css", "text/css"),
        };
}
