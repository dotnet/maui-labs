using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.Garden.Services;

public sealed class VisualAnalysisService(
    IChatClient chatClient,
    ICurrentViewCaptureService captureService)
{
    public async Task<string> DescribeAsync(
        string? targetAutomationId,
        string? question,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(targetAutomationId)
            ? "OrderInsightsCharts"
            : targetAutomationId;
        var image = await captureService.CaptureAsync(
            target,
            cancellationToken);
        var prompt =
            """
            Analyze this rendered app view as visual evidence.

            Describe the image as a whole, then identify any charts, chart titles,
            visible labels, relative values, largest and smallest items, and useful
            comparisons. Include exact numbers only when they are clearly legible.
            Do not infer data that is not visible, and say when text or values are
            uncertain. Ignore ordinary app chrome outside the captured view.
            """
            + (string.IsNullOrWhiteSpace(question)
                ? ""
                : $"\n\nThe user specifically asked: {question.Trim()}");

        var imageContent = new DataContent(
            image.PngData,
            "image/png")
        {
            Name = $"{image.TargetAutomationId}.png",
        };
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
