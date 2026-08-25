using Microsoft.Maui.Media;

namespace AIExtensions.Sample.Garden.Services;

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

    private static async Task<CapturedViewImage> CaptureOnMainThreadAsync(
        string? targetAutomationId,
        CancellationToken cancellationToken)
    {
        var currentPage = ResolveCurrentPage()
            ?? throw new InvalidOperationException(
                "No currently presented MAUI page is available to capture.");
        var target = string.IsNullOrWhiteSpace(targetAutomationId)
            ? currentPage
            : FindByAutomationId(currentPage, targetAutomationId!)
                ?? throw new InvalidOperationException(
                    $"No visible view with AutomationId '{targetAutomationId}' was found on {currentPage.GetType().Name}.");

        if (!target.IsVisible || target.Opacity <= 0)
        {
            throw new InvalidOperationException(
                $"The view '{targetAutomationId ?? currentPage.GetType().Name}' is not visible.");
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

    private static Page? ResolveCurrentPage()
    {
        var shell = Shell.Current;
        if (shell is null)
            return null;

        return shell.Navigation.ModalStack.LastOrDefault()
            ?? shell.CurrentPage;
    }

    private static VisualElement? FindByAutomationId(
        IVisualTreeElement root,
        string automationId)
    {
        if (root is VisualElement element
            && string.Equals(
                element.AutomationId,
                automationId,
                StringComparison.Ordinal))
        {
            return element;
        }

        foreach (var child in root.GetVisualChildren())
        {
            var match = FindByAutomationId(child, automationId);
            if (match is not null)
                return match;
        }

        return null;
    }
}
