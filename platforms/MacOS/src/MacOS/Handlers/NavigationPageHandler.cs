using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Container view for NavigationPage content. Navigation chrome (back button, title,
/// toolbar items) is rendered in the native macOS NSToolbar via MacOSToolbarManager.
/// </summary>
public class NavigationContainerView : MacOSContainerView
{
    public Action<CGRect>? OnLayout { get; set; }

    public override void Layout()
    {
        base.Layout();
        OnLayout?.Invoke(Bounds);
    }
}

public partial class NavigationPageHandler : MacOSViewHandler<IStackNavigationView, NavigationContainerView>, IStackNavigation
{
    public static readonly IPropertyMapper<IStackNavigationView, NavigationPageHandler> Mapper =
        new PropertyMapper<IStackNavigationView, NavigationPageHandler>(ViewMapper)
        {
            [nameof(NavigationPage.BarBackgroundColor)] = MapBarBackgroundColor,
            [nameof(NavigationPage.BarTextColor)] = MapBarTextColor,
        };

    public static readonly CommandMapper<IStackNavigationView, NavigationPageHandler> CommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(IStackNavigation.RequestNavigation)] = RequestNavigationCommand
        };

    readonly List<IView> _navigationStack = new();
    NSView? _currentPageView;

    public NavigationPageHandler() : base(Mapper, CommandMapper)
    {
    }

    static void RequestNavigationCommand(NavigationPageHandler handler, IStackNavigationView view, object? args)
    {
        if (args is NavigationRequest request)
            handler.RequestNavigation(request);
    }

    protected override NavigationContainerView CreatePlatformView()
    {
        var container = new NavigationContainerView();
        container.OnLayout = OnContainerLayout;
        return container;
    }

    protected override void ConnectHandler(NavigationContainerView platformView)
    {
        base.ConnectHandler(platformView);
    }

    void OnContainerLayout(CGRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (_currentPageView != null)
        {
            _currentPageView.Frame = new CGRect(0, 0, bounds.Width, bounds.Height);

            var currentPage = _navigationStack.LastOrDefault();
            if (currentPage != null)
            {
                currentPage.Measure((double)bounds.Width, (double)bounds.Height);
                currentPage.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
            }
        }
    }

    public void RequestNavigation(NavigationRequest request)
    {
        _navigationStack.Clear();
        _navigationStack.AddRange(request.NavigationStack);

        var currentPage = _navigationStack.LastOrDefault();
        ShowPage(currentPage);

        ((IStackNavigationView)VirtualView).NavigationFinished(_navigationStack);
    }

    void ShowPage(IView? page)
    {
        if (page == null || MauiContext == null)
            return;

        if (_currentPageView != null)
        {
            _currentPageView.RemoveFromSuperview();
            _currentPageView = null;
        }

        var platformView = page.ToMacOSPlatform(MauiContext);
        PlatformView.AddSubview(platformView);
        _currentPageView = platformView;

        // Trigger layout
        if (PlatformView.Bounds.Width > 0)
            OnContainerLayout(PlatformView.Bounds);
    }

    public void NavigationFinished(IReadOnlyList<IView> newStack)
    {
    }

    public static void MapBarBackgroundColor(NavigationPageHandler handler, IStackNavigationView view)
    {
        // Bar colors are handled by the native NSToolbar appearance
    }

    public static void MapBarTextColor(NavigationPageHandler handler, IStackNavigationView view)
    {
        // Bar colors are handled by the native NSToolbar appearance
    }
}
