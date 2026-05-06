using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public class TabbedContainerView : MacOSContainerView
{
    readonly NSSegmentedControl _tabBar;
    readonly NSView _contentArea;

    NSView? _currentPageView;

    public Action<nint>? OnTabSelected { get; set; }
    public Action<CGRect>? OnContentLayout { get; set; }
    public NSSegmentedControl TabBar => _tabBar;

    public TabbedContainerView()
    {
        _tabBar = new NSSegmentedControl
        {
            SegmentStyle = NSSegmentStyle.Automatic,
            TrackingMode = NSSegmentSwitchTracking.SelectOne,
        };
        _tabBar.Activated += (s, e) => OnTabSelected?.Invoke(_tabBar.SelectedSegment);

        _contentArea = new NSView
        {
            WantsLayer = true,
        };

        AddSubview(_tabBar);
        AddSubview(_contentArea);
    }

    public void SetTabs(IList<string> titles)
    {
        _tabBar.SegmentCount = titles.Count;
        for (int i = 0; i < titles.Count; i++)
        {
            _tabBar.SetLabel(titles[i], i);
            _tabBar.SetWidth(0, i); // auto-size
        }
    }

    public void SelectTab(int index)
    {
        _tabBar.SelectedSegment = index;
    }

    public void ShowContent(NSView view)
    {
        _currentPageView?.RemoveFromSuperview();
        _currentPageView = view;

        view.Frame = _contentArea.Bounds;
        view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _contentArea.AddSubview(view);
    }

    public override void Layout()
    {
        base.Layout();

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Position tab bar below safe area (title bar)
        var safeTop = (nfloat)SafeAreaInsets.Top;
        var tabBarHeight = (nfloat)30;
        var padding = (nfloat)10;

        var tabSize = _tabBar.FittingSize;
        var tabX = (bounds.Width - tabSize.Width) / 2;
        _tabBar.Frame = new CGRect(tabX, safeTop + padding, tabSize.Width, tabBarHeight);

        var contentTop = safeTop + padding + tabBarHeight + padding;
        _contentArea.Frame = new CGRect(0, contentTop, bounds.Width, bounds.Height - contentTop);

        if (_currentPageView != null)
        {
            _currentPageView.Frame = _contentArea.Bounds;
            OnContentLayout?.Invoke(_contentArea.Bounds);
        }
    }
}

public partial class TabbedPageHandler : MacOSViewHandler<ITabbedView, TabbedContainerView>
{
    public static readonly IPropertyMapper<ITabbedView, TabbedPageHandler> Mapper =
        new PropertyMapper<ITabbedView, TabbedPageHandler>(ViewMapper)
        {
            [nameof(TabbedPage.BarBackgroundColor)] = MapBarBackgroundColor,
            [nameof(TabbedPage.BarTextColor)] = MapBarTextColor,
            [nameof(TabbedPage.SelectedTabColor)] = MapSelectedTabColor,
            [nameof(TabbedPage.UnselectedTabColor)] = MapUnselectedTabColor,
        };

    public TabbedPageHandler() : base(Mapper)
    {
    }

    TabbedPage? TabbedPage => VirtualView as TabbedPage;
    bool _isSelectingPage;

    protected override TabbedContainerView CreatePlatformView()
    {
        var view = new TabbedContainerView();
        view.OnTabSelected = OnTabSelected;
        view.OnContentLayout = OnContentLayout;
        return view;
    }

    protected override void ConnectHandler(TabbedContainerView platformView)
    {
        base.ConnectHandler(platformView);

        if (TabbedPage != null)
        {
            TabbedPage.PagesChanged += OnPagesChanged;
            TabbedPage.CurrentPageChanged += OnCurrentPageChanged;
            SetupTabs();
        }
    }

    protected override void DisconnectHandler(TabbedContainerView platformView)
    {
        if (TabbedPage != null)
        {
            TabbedPage.PagesChanged -= OnPagesChanged;
            TabbedPage.CurrentPageChanged -= OnCurrentPageChanged;
        }

        base.DisconnectHandler(platformView);
    }

    void OnPagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SetupTabs();
    }

    void OnCurrentPageChanged(object? sender, EventArgs e)
    {
        if (_isSelectingPage || TabbedPage?.CurrentPage == null)
            return;

        var index = TabbedPage.Children.IndexOf(TabbedPage.CurrentPage);
        if (index >= 0)
            SelectPage(index);
    }

    void SetupTabs()
    {
        if (TabbedPage == null)
            return;

        var titles = new List<string>();
        foreach (var page in TabbedPage.Children)
            titles.Add(page.Title ?? "Tab");

        PlatformView.SetTabs(titles);

        if (TabbedPage.Children.Count > 0)
            SelectPage(0);
    }

    void OnTabSelected(nint index)
    {
        SelectPage((int)index);
    }

    void SelectPage(int index)
    {
        if (TabbedPage == null || index < 0 || index >= TabbedPage.Children.Count || MauiContext == null)
            return;

        _isSelectingPage = true;
        try
        {
            TabbedPage.CurrentPage = TabbedPage.Children[index];
            PlatformView.SelectTab(index);

            var page = TabbedPage.Children[index];
            var platformView = ((IView)page).ToMacOSPlatform(MauiContext);
            PlatformView.ShowContent(platformView);
        }
        finally
        {
            _isSelectingPage = false;
        }
    }

    void OnContentLayout(CGRect bounds)
    {
        if (TabbedPage?.CurrentPage == null || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var currentPage = (IView)TabbedPage.CurrentPage;
        currentPage.Measure((double)bounds.Width, (double)bounds.Height);
        currentPage.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
    }

    public static void MapBarBackgroundColor(TabbedPageHandler handler, ITabbedView view)
    {
        if (view is TabbedPage tp && tp.BarBackgroundColor is Microsoft.Maui.Graphics.Color bgColor)
        {
            var tabBar = handler.PlatformView.TabBar;
            tabBar.WantsLayer = true;
            tabBar.Layer!.BackgroundColor = bgColor.ToPlatformColor().CGColor;
            tabBar.Layer.CornerRadius = 5;
        }
    }

    public static void MapBarTextColor(TabbedPageHandler handler, ITabbedView view)
    {
        // NSSegmentedControl doesn't expose a direct text color API;
        // text color follows system appearance on macOS.
    }

    public static void MapSelectedTabColor(TabbedPageHandler handler, ITabbedView view)
    {
        if (view is TabbedPage tp && tp.SelectedTabColor is Microsoft.Maui.Graphics.Color selColor)
        {
            var tabBar = handler.PlatformView.TabBar;
            tabBar.WantsLayer = true;
            tabBar.SelectedSegmentBezelColor = selColor.ToPlatformColor();
        }
    }

    public static void MapUnselectedTabColor(TabbedPageHandler handler, ITabbedView view)
    {
        // NSSegmentedControl uses system styling for unselected segments;
        // no per-segment unselected color API is available in AppKit.
    }
}
