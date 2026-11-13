using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

public sealed partial class InspectorServer
{
    private async Task<(int, string, byte[])> HandleEvidencePreviewAsync(string? body)
    {
        EvidenceRequest request;
        try { request = ReadEvidenceRequest(body); }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return BadRequest("Invalid evidence options. Use boolean consent flags, a workflow string, and limits from 1 to 500.");
        }

        var plan = await EvidenceCapture.PreviewAsync(_client, request, _lifetimeCts.Token);
        return Ok(EvidenceJson.Serialize(new EvidencePreviewResponse { Ok = true, Plan = plan }));
    }

    private async Task<(int, string, byte[])> HandleEvidenceCaptureAsync(string? body)
    {
        EvidenceRequest request;
        try { request = ReadEvidenceRequest(body); }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return BadRequest("Invalid evidence options. Use boolean consent flags, a workflow string, and limits from 1 to 500.");
        }

        var (_, bytes) = await EvidenceCapture.CaptureToBytesAsync(_client, request, _lifetimeCts.Token);
        return (200, "application/zip", bytes);
    }

    private EvidenceRequest ReadEvidenceRequest(string? body)
    {
        var options = EvidenceJson.Deserialize<EvidenceInspectorOptions>(body ?? "{}")
            ?? throw new ArgumentException("Evidence options must be an object.");
        if (options.LogLimit is < 1 or > EvidenceFormat.MaxLogLimit ||
            options.NetworkLimit is < 1 or > EvidenceFormat.MaxNetworkLimit)
            throw new ArgumentException("Evidence limits are out of range.");

        return new EvidenceRequest
        {
            Source = "inspector",
            ProjectHint = _project,
            IncludeScreenshot = options.IncludeScreenshot,
            WorkflowMarkdown = options.IncludeWorkflow == false ? null : options.Workflow,
            SelectedElementId = options.ElementId,
            LogLimit = options.LogLimit,
            NetworkLimit = options.NetworkLimit,
        };
    }
}
