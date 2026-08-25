using System.ComponentModel;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

public sealed class AppVisualTools(VisualAnalysisService visualAnalysis)
{
    [ExportAIFunction("describe_current_visual")]
    [Description(
        "Capture and visually analyze a rendered view on the current page. Use for charts, " +
        "drawings, maps, diagrams, or other pixel-only content that get_current_app_state cannot read.")]
    public Task<string> DescribeCurrentVisualAsync(
        [Description(
            "AutomationId of the rendered view to capture. Use 'OrderInsightsCharts' for the Orders charts. " +
            "Leave blank to use the default OrderInsightsCharts target.")]
        string? targetAutomationId = null,
        [Description(
            "Optional question to answer about the image, such as 'Which category has the highest spending?'")]
        string? question = null,
        CancellationToken cancellationToken = default)
        => visualAnalysis.DescribeAsync(
            targetAutomationId,
            question,
            cancellationToken);
}
