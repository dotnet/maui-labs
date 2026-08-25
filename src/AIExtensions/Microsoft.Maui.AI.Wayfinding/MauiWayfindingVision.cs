using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Wayfinding;

/// <summary>Analyzes a captured MAUI view through a vision-enabled chat client.</summary>
public sealed class MauiVisualAnalysisService(
    IServiceProvider services,
    ICurrentViewCaptureService captureService,
    MauiWayfindingOptions options)
{
    public async Task<string> DescribeAsync(
        string? targetAutomationId,
        string? question,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(targetAutomationId)
            ? options.DefaultVisualTargetAutomationId
            : targetAutomationId;
        var image = await captureService.CaptureAsync(
            target,
            cancellationToken);
        var prompt = options.VisionInstructions
            + (string.IsNullOrWhiteSpace(question)
                ? ""
                : $"\n\nThe user specifically asked: {question.Trim()}");
        var imageContent = new DataContent(
            image.PngData,
            "image/png")
        {
            Name = "screen.png",
        };
        var chatClient = services.GetKeyedService<IChatClient>(
                MauiWayfindingOptions.VisionChatClientServiceKey)
            ?? throw new InvalidOperationException(
                $"Vision is enabled, but no raw IChatClient is registered with service key " +
                $"'{MauiWayfindingOptions.VisionChatClientServiceKey}'.");
        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(
                ChatRole.System,
                "You are a precise visual UI analyst. Ground every statement in the supplied image."),
            new ChatMessage(
                ChatRole.User,
                [
                    new TextContent(prompt),
                    imageContent,
                ]),
        ],
        cancellationToken: cancellationToken);

        return string.IsNullOrWhiteSpace(response.Text)
            ? "The visual model returned no description."
            : response.Text.Trim();
    }
}

/// <summary>AI-callable rendered-view analysis.</summary>
public sealed class MauiWayfindingVisionTools(
    MauiVisualAnalysisService visualAnalysis)
{
    [ExportAIFunction("describe_current_visual")]
    [Description(
        "Capture and visually analyze a rendered view on the current page. " +
        "Use for charts, drawings, maps, diagrams, or other pixel-only content.")]
    public Task<string> DescribeCurrentVisualAsync(
        [Description(
            "AutomationId of the rendered view to capture. Leave blank to use the configured default target.")]
        string? targetAutomationId = null,
        [Description(
            "Optional question to answer about the image.")]
        string? question = null,
        CancellationToken cancellationToken = default)
        => visualAnalysis.DescribeAsync(
            targetAutomationId,
            question,
            cancellationToken);
}
