using System.ComponentModel;
using System.Diagnostics;
using AIExtensions.Sample.Scrolling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// DevFlow actions that exercise <c>ScrollAnchor</c> in the playground without a model or a network: write entries to
/// the AI diagnostics sidebar (its log list anchors to the end), open <see cref="ScrollAnchorDemoPage"/>, and report
/// which anchored views follow their end.
/// </summary>
public static class ScrollAnchorDemoActions
{
    private static readonly ActivitySource Source = new(ChatDiagnostics.SourceName);

    /// <summary>Writes entries to the AI diagnostics sidebar, spread over new traces (one group each).</summary>
    [DevFlowAction("scroll-anchor-logs", Description = "Write entries to the AI diagnostics sidebar, spread over new traces (one group each)")]
    public static string WriteLogs(
        [Description("How many entries to write")] int count = 10,
        [Description("How many new traces (groups) to spread them over")] int traces = 1)
    {
        var logger = CreateLogger();
        traces = Math.Clamp(traces, 1, Math.Max(1, count));
        for (var trace = 0; trace < traces; trace++)
        {
            using var activity = Source.StartActivity("Scroll anchor demo");
            for (var entry = trace; entry < count; entry += traces)
                logger.LogInformation("Scroll anchor demo entry {Entry} of {Count}.", entry + 1, count);
        }

        return $"Wrote {count} entries in {traces} trace(s).";
    }

    /// <summary>Writes one entry to the AI diagnostics sidebar every interval, in the background, in a new trace.</summary>
    [DevFlowAction("scroll-anchor-log-stream", Description = "Write one entry to the AI diagnostics sidebar every interval, in the background, in a new trace (one group)")]
    public static string StreamLogs(
        [Description("How many entries to write")] int count = 30,
        [Description("Milliseconds between entries")] int intervalMilliseconds = 400)
    {
        var logger = CreateLogger();
        _ = Task.Run(async () =>
        {
            using var activity = Source.StartActivity("Scroll anchor stream");
            for (var entry = 1; entry <= count; entry++)
            {
                logger.LogInformation("Scroll anchor stream entry {Entry} of {Count}.", entry, count);
                await Task.Delay(intervalMilliseconds);
            }
        });

        return $"Writing {count} entries, one every {intervalMilliseconds} ms.";
    }

    /// <summary>Opens the page that exercises ScrollAnchor on a grouped CollectionView and a ScrollView.</summary>
    [DevFlowAction("scroll-anchor-demo", Description = "Open a page that exercises ScrollAnchor on a grouped CollectionView and a ScrollView")]
    public static Task<string> OpenDemo() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("The playground has no window yet.");
            await page.Navigation.PushModalAsync(new ScrollAnchorDemoPage());
            return "Opened the scroll anchor demo.";
        });

    /// <summary>Reports whether each view with <c>ScrollAnchor.Mode</c> set follows the end of its content.</summary>
    [DevFlowAction("scroll-anchor-state", Description = "Report whether each view with ScrollAnchor.Mode set follows the end of its content")]
    public static Task<string> GetState() =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault()
                ?? throw new InvalidOperationException("The playground has no window yet.");
            IEnumerable<Element> roots = window.Page is { } page ? [page, .. window.Navigation.ModalStack] : window.Navigation.ModalStack;
            var states = roots.SelectMany(Descendants)
                .OfType<View>()
                .Where(view => ScrollAnchor.GetMode(view) != ScrollAnchorMode.None)
                .Select(view => $"{view.AutomationId ?? view.GetType().Name}: {(ScrollAnchor.GetIsAnchored(view) ? "anchored" : "scrolled away")}")
                .Distinct();
            return string.Join(" | ", states);
        });

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>())
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static ILogger CreateLogger() =>
        (IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()
            ?? throw new InvalidOperationException("The playground has no services yet."))
        .CreateLogger(ChatDiagnostics.SourceName + ".ScrollAnchorDemo");
}
