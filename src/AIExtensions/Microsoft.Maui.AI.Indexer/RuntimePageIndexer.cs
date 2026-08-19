using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Produces a semantic snapshot of the currently presented MAUI page.
/// </summary>
/// <remarks>
/// The compile-time <see cref="IndexedPageCatalog"/> remains the complete app corpus.
/// This runtime index adds the currently materialized controls, resolved binding values,
/// visibility, focus, and control state for questions about what is on screen now.
/// </remarks>
public static class RuntimePageIndexer
{
    /// <summary>
    /// Captures the first available application window on the MAUI dispatcher.
    /// </summary>
    /// <returns>
    /// The current page snapshot, or <see langword="null"/> when there is no running
    /// MAUI application, window, or presented page.
    /// </returns>
    public static async Task<CurrentPageSnapshot?> CaptureCurrentAsync(
        CurrentPageSnapshotOptions? options = null)
    {
        var application = Application.Current;
        if (application is null)
            return null;

        options ??= new CurrentPageSnapshotOptions();
        options.Validate();

        var dispatcher = application.Dispatcher;
        if (dispatcher is not null && dispatcher.IsDispatchRequired)
        {
            return await dispatcher.DispatchAsync(
                () => CaptureCurrent(application, options)).ConfigureAwait(false);
        }

        return CaptureCurrent(application, options);
    }

    /// <summary>
    /// Captures the page currently presented by <paramref name="window"/>.
    /// Call this overload from the MAUI UI thread.
    /// </summary>
    public static CurrentPageSnapshot? Capture(
        Window window,
        CurrentPageSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        options ??= new CurrentPageSnapshotOptions();
        options.Validate();

        var presentedRoot = ResolvePresentedRoot(window);
        if (presentedRoot is Shell { FlyoutIsPresented: true } shell)
        {
            return new CurrentPageSnapshot(
                shell.GetType().Name,
                RuntimeMarkdownBuilder.RenderShellFlyout(shell, options));
        }

        var page = presentedRoot is null ? null : ResolvePageContainer(presentedRoot);
        return page is null ? null : Capture(page, options);
    }

    /// <summary>
    /// Captures a specific materialized page. Call this overload from the MAUI UI thread.
    /// </summary>
    public static CurrentPageSnapshot Capture(
        Page page,
        CurrentPageSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        options ??= new CurrentPageSnapshotOptions();
        options.Validate();

        var pageName = page.GetType().Name;
        var markdown = RuntimeMarkdownBuilder.Render(page, pageName, options);
        return new CurrentPageSnapshot(pageName, markdown);
    }

    private static CurrentPageSnapshot? CaptureCurrent(
        Application application,
        CurrentPageSnapshotOptions options)
    {
        var window = application.Windows.FirstOrDefault(static candidate => candidate.Page is not null);
        return window is null ? null : Capture(window, options);
    }

    private static Page? ResolvePresentedRoot(Window window)
    {
        var root = window.Page;
        if (root is null)
            return null;

        var modal = window.Navigation?.ModalStack.LastOrDefault()
            ?? root.Navigation?.ModalStack.LastOrDefault();

        return modal ?? root;
    }

    private static Page? ResolvePageContainer(Page root)
    {
        var current = root;
        var visited = new HashSet<Page>(ReferenceEqualityComparer.Instance);

        while (visited.Add(current))
        {
            if (IndexingProperties.GetExcludeWithChildren(current)
                || !current.IsVisible
                || current.Opacity <= 0)
                return null;

            if (current is Shell activeShell && IsActiveShellPathExcluded(activeShell))
                return null;

            var next = current switch
            {
                Shell shell when shell.CurrentPage is not null => shell.CurrentPage,
                NavigationPage navigation when navigation.CurrentPage is not null => navigation.CurrentPage,
                TabbedPage tabs when tabs.CurrentPage is not null => tabs.CurrentPage,
                FlyoutPage { IsPresented: true } => null,
                FlyoutPage flyout when flyout.Detail is not null => flyout.Detail,
                _ => null,
            };

            if (next is null)
                break;

            current = next;
        }

        return current;
    }

    internal static bool IsActiveShellPathExcluded(Shell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        return shell.CurrentItem is { } item && IndexingProperties.GetExcludeWithChildren(item)
            || shell.CurrentItem?.CurrentItem is { } section
                && IndexingProperties.GetExcludeWithChildren(section)
            || shell.CurrentItem?.CurrentItem?.CurrentItem is { } content
                && IndexingProperties.GetExcludeWithChildren(content);
    }
}
