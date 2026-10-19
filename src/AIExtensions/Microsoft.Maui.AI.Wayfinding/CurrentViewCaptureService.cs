using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.Media;

namespace Microsoft.Maui.AI.Wayfinding;

public sealed record CapturedViewImage(
    byte[] PngData,
    string TargetAutomationId,
    int Width,
    int Height);

public interface ICurrentViewCaptureService
{
    Task<CapturedViewImage> CaptureAsync(
        string? targetAutomationId = null,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<CapturedViewImage>> CaptureManyAsync(
        IReadOnlyList<string> targetAutomationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetAutomationIds);
        var captures = new List<CapturedViewImage>(targetAutomationIds.Count);
        foreach (var targetAutomationId in targetAutomationIds)
        {
            captures.Add(await CaptureAsync(
                targetAutomationId,
                cancellationToken));
        }
        return captures;
    }
}

public sealed class CurrentViewCaptureService : ICurrentViewCaptureService
{
    public Task<CapturedViewImage> CaptureAsync(
        string? targetAutomationId = null,
        CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(
            () => CaptureOnMainThreadAsync(
                targetAutomationId,
                cancellationToken));

    public Task<IReadOnlyList<CapturedViewImage>> CaptureManyAsync(
        IReadOnlyList<string> targetAutomationIds,
        CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(
            () => CaptureManyOnMainThreadAsync(
                targetAutomationIds,
                cancellationToken));

    private static async Task<CapturedViewImage> CaptureOnMainThreadAsync(
        string? targetAutomationId,
        CancellationToken cancellationToken)
    {
        var context = ResolveCaptureContext()
            ?? throw new InvalidOperationException(
                "No currently presented MAUI page is available to capture.");
        var target = string.IsNullOrWhiteSpace(targetAutomationId)
            ? context.CurrentPage
            : FindByAutomationId(context.Roots, targetAutomationId!)
                ?? throw new InvalidOperationException(
                    $"No visible view with AutomationId '{targetAutomationId}' was found on the current screen.");

        return await CaptureTargetAsync(
            target,
            targetAutomationId,
            context.CurrentPage,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<CapturedViewImage>> CaptureManyOnMainThreadAsync(
        IReadOnlyList<string> targetAutomationIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetAutomationIds);
        var context = ResolveCaptureContext()
            ?? throw new InvalidOperationException(
                "No currently presented MAUI page is available to capture.");
        var captures = new List<CapturedViewImage>(targetAutomationIds.Count);
        foreach (var targetAutomationId in targetAutomationIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetAutomationId);
            var target = FindByAutomationId(context.Roots, targetAutomationId)
                ?? throw new InvalidOperationException(
                    $"No visible view with AutomationId '{targetAutomationId}' was found on the current screen.");
            captures.Add(await CaptureTargetAsync(
                target,
                targetAutomationId,
                context.CurrentPage,
                cancellationToken));

            if (!IsCurrentCaptureContext(context))
            {
                throw new InvalidOperationException(
                    "The current screen changed while visual regions were being captured.");
            }
        }

        return captures;
    }

    private static async Task<CapturedViewImage> CaptureTargetAsync(
        VisualElement target,
        string? targetAutomationId,
        Page currentPage,
        CancellationToken cancellationToken)
    {
        if (!target.IsVisible || target.Opacity <= 0)
        {
            throw new InvalidOperationException(
                $"The view '{targetAutomationId ?? currentPage.GetType().Name}' is not visible.");
        }
        if (ContainsExcludedContent(target))
        {
            throw new InvalidOperationException(
                "The selected visual contains content excluded from the semantic UI index.");
        }

        var screenshot = await ((IView)target).CaptureAsync();
        if (screenshot is null)
        {
            throw new NotSupportedException(
                $"View capture is not supported for {target.GetType().Name} on this platform.");
        }

        await using var stream = await screenshot.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException("The captured view image was empty.");

        return new CapturedViewImage(
            bytes,
            target.AutomationId ?? currentPage.GetType().Name,
            screenshot.Width,
            screenshot.Height);
    }

    private static CaptureContext? ResolveCaptureContext()
    {
        var shell = Shell.Current;
        if (shell is null)
            return null;

        var modal = shell.Navigation.ModalStack.LastOrDefault();
        var currentPage = modal ?? shell.CurrentPage;
        if (currentPage is null)
            return null;

        var roots = new List<IVisualTreeElement>();
        if (modal is null && shell.FlyoutIsPresented)
        {
            AddRoot(shell.FlyoutHeader);
            AddRoot(shell.FlyoutContent);
            AddRoot(shell.FlyoutFooter);
        }
        roots.Add(currentPage);

        return new CaptureContext(
            shell,
            currentPage,
            modal is null && shell.FlyoutIsPresented,
            roots);

        void AddRoot(object? candidate)
        {
            if (candidate is IVisualTreeElement visual)
                roots.Add(visual);
        }
    }

    private static VisualElement? FindByAutomationId(
        IEnumerable<IVisualTreeElement> roots,
        string automationId)
    {
        foreach (var root in roots)
        {
            var match = FindByAutomationId(root, automationId);
            if (match is not null)
                return match;
        }
        return null;
    }

    private static VisualElement? FindByAutomationId(
        IVisualTreeElement root,
        string automationId)
    {
        if (root is BindableObject bindable
            && IndexingProperties.GetExcludeWithChildren(bindable))
        {
            return null;
        }
        if (root is VisualElement element
            && (!element.IsVisible || element.Opacity <= 0))
        {
            return null;
        }
        if (root is VisualElement visibleElement
            && string.Equals(
                visibleElement.AutomationId,
                automationId,
                StringComparison.Ordinal))
        {
            return visibleElement;
        }

        foreach (var child in root.GetVisualChildren())
        {
            var match = FindByAutomationId(child, automationId);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static bool ContainsExcludedContent(IVisualTreeElement root)
    {
        var pending = new Stack<IVisualTreeElement>();
        pending.Push(root);
        var visited = new HashSet<IVisualTreeElement>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;
            if (current is BindableObject bindable
                && IndexingProperties.GetExcludeWithChildren(bindable))
            {
                return true;
            }

            foreach (var child in current.GetVisualChildren())
                pending.Push(child);
        }

        return false;
    }

    private static bool IsCurrentCaptureContext(CaptureContext expected)
    {
        var current = ResolveCaptureContext();
        return current is not null
            && ReferenceEquals(current.Shell, expected.Shell)
            && ReferenceEquals(current.CurrentPage, expected.CurrentPage)
            && current.FlyoutIsPresented == expected.FlyoutIsPresented;
    }

    private sealed record CaptureContext(
        Shell Shell,
        Page CurrentPage,
        bool FlyoutIsPresented,
        IReadOnlyList<IVisualTreeElement> Roots);
}
