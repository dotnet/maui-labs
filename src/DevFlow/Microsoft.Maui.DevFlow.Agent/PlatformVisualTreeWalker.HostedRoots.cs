using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Finds MAUI pages that a library attaches straight to the native window instead
/// of to <see cref="Window.Page"/> or the modal stack. Popup libraries do this: for
/// example UXDivers.Popups converts its PopupPage with <c>ToPlatform()</c> and adds
/// it as a subview of the UIWindow. Such a page is a normal MAUI element with a
/// handler, but no MAUI parent leads to it, so the regular walk never reaches it.
/// Apple platforms only for now: <see cref="Microsoft.Maui.Platform.MauiView.View"/> links a
/// native view to its MAUI view publicly. Android has no public equivalent yet.
/// </summary>
public partial class PlatformVisualTreeWalker
{
    // Popup hosts sit near the top of the native hierarchy. The search stops at the
    // first MAUI view on each branch, so it never walks the whole page.
    private const int HostedRootSearchDepth = 8;

    protected override IReadOnlyList<IVisualTreeElement> GetHostedRoots(Window window)
    {
        var roots = new List<IVisualTreeElement>();
#if IOS || MACCATALYST
        if (window.Handler?.PlatformView is UIKit.UIWindow uiWindow)
            CollectHostedRoots(uiWindow, window, roots, depth: 0);
#endif
        return roots;
    }

#if IOS || MACCATALYST
    private static void CollectHostedRoots(UIKit.UIView view, Window window, List<IVisualTreeElement> roots, int depth)
    {
        if (view.Hidden || view.Alpha <= 0)
            return;

        if (view is Microsoft.Maui.Platform.MauiView { View: IVisualTreeElement element })
        {
            AddIfHosted(element, window, roots);
            return;
        }

        if (depth >= HostedRootSearchDepth)
            return;

        foreach (var subview in view.Subviews)
            CollectHostedRoots(subview, window, roots, depth + 1);
    }

    private static void AddIfHosted(IVisualTreeElement element, Window window, List<IVisualTreeElement> roots)
    {
        // Null: the window's own page tree or modal stack, which the regular walk covers.
        if (FindDetachedRoot(element, window) is not { } root || root is Window || root is Application)
            return;

        if (!roots.Contains(root))
            roots.Add(root);
    }
#endif
}
