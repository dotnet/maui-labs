using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Indexer;

namespace Microsoft.Maui.AI.Wayfinding;

/// <summary>Analyzes a captured MAUI view through a vision-enabled chat client.</summary>
public sealed class MauiVisualAnalysisService(
    IServiceProvider services,
    ICurrentViewCaptureService captureService,
    ICurrentPageContextProvider currentPage,
    MauiWayfindingOptions options)
{
    public async Task<string> DescribeAsync(
        IReadOnlyList<string> targetAutomationIds,
        string? question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetAutomationIds);
        var targets = targetAutomationIds
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets.Length == 0)
        {
            throw new ArgumentException(
                "At least one AutomationId from the current UI index is required.",
                nameof(targetAutomationIds));
        }
        if (targets.Length > options.MaximumVisualTargets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetAutomationIds),
                $"At most {options.MaximumVisualTargets} visual targets can be analyzed at once.");
        }

        var snapshot = await currentPage.CaptureAsync(options.CurrentPage)
            ?? throw new InvalidOperationException(
                "No current semantic UI snapshot is available for visual target validation.");
        var availableTargets = snapshot.AutomationIds.ToHashSet(StringComparer.Ordinal);
        var unavailableTargets = targets
            .Where(target => !availableTargets.Contains(target))
            .ToArray();
        if (unavailableTargets.Length > 0)
        {
            throw new InvalidOperationException(
                "Every visual target must be an AutomationId exposed by the current semantic UI.");
        }

        var images = await captureService.CaptureManyAsync(
            targets,
            cancellationToken);
        if (images.Count != targets.Length)
            throw new InvalidOperationException("Visual capture returned an incomplete result set.");
        var totalBytes = images.Sum(image => (long)image.PngData.Length);
        if (totalBytes > options.MaximumVisualPayloadBytes)
        {
            throw new InvalidOperationException(
                $"The selected visuals exceed the {options.MaximumVisualPayloadBytes} byte payload limit.");
        }

        var prompt = options.VisionInstructions
            + (string.IsNullOrWhiteSpace(question)
                ? ""
                : $"\n\nThe user specifically asked: {question.Trim()}");
        var contents = new List<AIContent>(images.Count + 1)
        {
            new TextContent(prompt),
        };
        for (var index = 0; index < images.Count; index++)
        {
            contents.Add(new DataContent(
                images[index].PngData,
                "image/png")
            {
                Name = $"visual-{index + 1}.png",
            });
        }

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
                contents),
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
            "One or more AutomationId values copied from semantically described controls in " +
            "get_current_app_state. Select the relevant control, or a described parent that contains " +
            "multiple visuals.")]
        string[] targetAutomationIds,
        [Description(
            "Optional question to answer about the image.")]
        string? question = null,
        CancellationToken cancellationToken = default)
        => visualAnalysis.DescribeAsync(
            targetAutomationIds,
            question,
            cancellationToken);
}
