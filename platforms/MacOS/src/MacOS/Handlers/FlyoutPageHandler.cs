using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Container view for FlyoutPage that uses NSSplitView for a native macOS sidebar experience.
/// When <paramref name="useNativeSidebar"/> is true, uses NSSplitViewController for
/// inset sidebar with behind-window vibrancy and titlebar integration.
/// </summary>
public class FlyoutContainerView : MacOSContainerView, INSSplitViewDelegate
{
    NSSplitView _splitView;
    readonly NSView _flyoutContainer;
    readonly NSView _detailContainer;

    NSView? _currentFlyoutView;
    NSView? _currentDetailView;
    bool _initialDividerSet;
    NSLayoutConstraint? _flyoutWidthConstraint;

    // NSSplitViewController mode (native sidebar)
    NSSplitViewController? _splitViewController;
    NSSplitViewItem? _sidebarSplitItem;
    readonly bool _useNativeSidebar;

    /// <summary>
    /// Exposes the NSSplitViewController so WindowHandler can set it as
    /// the window's contentViewController for titlebar integration.
    /// </summary>
    internal NSSplitViewController? SplitViewController => _splitViewController;

    public Action<CGRect>? OnFlyoutLayout { get; set; }
    public Action<CGRect>? OnDetailLayout { get; set; }

    double _flyoutWidth = 185;
    public double FlyoutWidth
    {
        get => _flyoutWidth;
        set
        {
            _flyoutWidth = value;
            if (_useNativeSidebar && _sidebarSplitItem != null)
            {
                _splitViewController?.SplitView?.SetPositionOfDivider((nfloat)value, 0);
            }
            else
            {
                if (_flyoutWidthConstraint != null)
                    _flyoutWidthConstraint.Constant = (nfloat)value;
                _splitView.SetPositionOfDivider((nfloat)value, 0);
            }
        }
    }

    public FlyoutContainerView(bool useNativeSidebar = false)
    {
        _useNativeSidebar = useNativeSidebar;

        if (_useNativeSidebar)
        {
            // NSSplitViewController mode — inset sidebar with vibrancy
            _flyoutContainer = new NSVisualEffectView
            {
                BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
                Material = NSVisualEffectMaterial.Sidebar,
                State = NSVisualEffectState.FollowsWindowActiveState,
            };
            _detailContainer = new FlippedDocumentView();
            _detailContainer.WantsLayer = true;
            ((FlippedDocumentView)_detailContainer).Layer!.MasksToBounds = true;

            // Observe frame changes to re-layout MAUI content
            _detailContainer.PostsFrameChangedNotifications = true;
            Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification, OnDetailFrameChanged, _detailContainer);

            _flyoutContainer.PostsFrameChangedNotifications = true;
            Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification, OnFlyoutFrameChanged, _flyoutContainer);

            _splitViewController = new NSSplitViewController();

            var sidebarVC = new NSViewController { View = _flyoutContainer };
            var contentVC = new NSViewController { View = _detailContainer };

            _sidebarSplitItem = NSSplitViewItem.CreateSidebar(sidebarVC);
            _sidebarSplitItem.MinimumThickness = 150;
            _sidebarSplitItem.MaximumThickness = 400;
            _sidebarSplitItem.CanCollapse = false;
            _sidebarSplitItem.AllowsFullHeightLayout = true;
            _sidebarSplitItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.None;

            var contentItem = NSSplitViewItem.CreateContentList(contentVC);
            contentItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.Line;

            _splitViewController.AddSplitViewItem(_sidebarSplitItem);
            _splitViewController.AddSplitViewItem(contentItem);

            _splitView = _splitViewController.SplitView;

            // Embed the split view controller's view
            var splitVCView = _splitViewController.View;
            splitVCView.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(splitVCView);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                splitVCView.TopAnchor.ConstraintEqualTo(TopAnchor),
                splitVCView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                splitVCView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
                splitVCView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            });
        }
        else
        {
            // Plain NSSplitView mode (original behavior)
            _flyoutContainer = new NSView { WantsLayer = true, TranslatesAutoresizingMaskIntoConstraints = false };
            _detailContainer = new NSView { WantsLayer = true };

            _splitView = new NSSplitView
            {
                IsVertical = true,
                DividerStyle = NSSplitViewDividerStyle.Thin,
                TranslatesAutoresizingMaskIntoConstraints = false,
                Delegate = this,
            };

            _splitView.AddArrangedSubview(_flyoutContainer);
            _splitView.AddArrangedSubview(_detailContainer);

            _flyoutWidthConstraint = _flyoutContainer.WidthAnchor.ConstraintEqualTo((nfloat)_flyoutWidth);
            _flyoutWidthConstraint.Priority = (float)NSLayoutPriority.DefaultHigh;
            _flyoutWidthConstraint.Active = true;

            _splitView.SetHoldingPriority(251, 0);
            _splitView.SetHoldingPriority(249, 1);

            AddSubview(_splitView);

            // Observe frame changes for resize handling
            _detailContainer.PostsFrameChangedNotifications = true;
            Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification, OnDetailFrameChanged, _detailContainer);

            _flyoutContainer.PostsFrameChangedNotifications = true;
            Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification, OnFlyoutFrameChanged, _flyoutContainer);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                _splitView.TopAnchor.ConstraintEqualTo(TopAnchor),
                _splitView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                _splitView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
                _splitView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            });
        }
    }

    public void ShowFlyout(NSView view)
    {
        _currentFlyoutView?.RemoveFromSuperview();
        _currentFlyoutView = view;

        var bounds = _flyoutContainer.Bounds;
        view.Frame = bounds;
        view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _flyoutContainer.AddSubview(view);

        // Trigger layout if the container already has valid bounds
        if (bounds.Width > 0 && bounds.Height > 0)
            OnFlyoutLayout?.Invoke(bounds);
    }

    public void ShowDetail(NSView view)
    {
        _currentDetailView?.RemoveFromSuperview();
        _currentDetailView = view;

        var bounds = _detailContainer.Bounds;
        view.Frame = bounds;
        view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _detailContainer.AddSubview(view);

        // Trigger layout if the container already has valid bounds
        if (bounds.Width > 0 && bounds.Height > 0)
            OnDetailLayout?.Invoke(bounds);
    }

    public void SetFlyoutVisible(bool visible)
    {
        if (_useNativeSidebar && _sidebarSplitItem != null)
        {
            _sidebarSplitItem.Collapsed = !visible;
            return;
        }

        if (visible)
        {
            if (_splitView.IsSubviewCollapsed(_flyoutContainer))
                _splitView.SetPositionOfDivider((nfloat)FlyoutWidth, 0);
        }
        else
        {
            _splitView.SetPositionOfDivider(0, 0);
        }
    }

    void OnDetailFrameChanged(Foundation.NSNotification notification)
    {
        if (_currentDetailView == null)
            return;

        var bounds = _detailContainer.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _currentDetailView.Frame = bounds;
        OnDetailLayout?.Invoke(bounds);
    }

    void OnFlyoutFrameChanged(Foundation.NSNotification notification)
    {
        if (_currentFlyoutView == null)
            return;

        var bounds = _flyoutContainer.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _currentFlyoutView.Frame = bounds;
        OnFlyoutLayout?.Invoke(bounds);
    }

    public override void Layout()
    {
        base.Layout();

        if (!_initialDividerSet && Bounds.Width > 0)
        {
            _initialDividerSet = true;
            _splitView.SetPositionOfDivider((nfloat)_flyoutWidth, 0);
        }

        if (_currentFlyoutView != null)
        {
            _currentFlyoutView.Frame = _flyoutContainer.Bounds;
            OnFlyoutLayout?.Invoke(_flyoutContainer.Bounds);
        }

        if (_currentDetailView != null)
        {
            _currentDetailView.Frame = _detailContainer.Bounds;
            OnDetailLayout?.Invoke(_detailContainer.Bounds);
        }
    }

    // NSSplitViewDelegate — lock the flyout (sidebar) width
    [Foundation.Export("splitView:constrainMinCoordinate:ofSubviewAt:")]
    public nfloat SetMinCoordinate(NSSplitView splitView, nfloat proposedMinimumPosition, nint dividerIndex)
    {
        return (nfloat)FlyoutWidth;
    }

    [Foundation.Export("splitView:constrainMaxCoordinate:ofSubviewAt:")]
    public nfloat SetMaxCoordinate(NSSplitView splitView, nfloat proposedMaximumPosition, nint dividerIndex)
    {
        return (nfloat)FlyoutWidth;
    }

    // Prevent NSSplitView from proportionally resizing the flyout pane during window resize
    [Foundation.Export("splitView:shouldAdjustSizeOfSubview:")]
    public bool ShouldAdjustSizeOfSubview(NSSplitView splitView, NSView view)
    {
        // Only the detail pane should resize; flyout stays fixed
        return view != _flyoutContainer;
    }
}

public partial class FlyoutPageHandler : MacOSViewHandler<IFlyoutView, FlyoutContainerView>
{
    public static readonly IPropertyMapper<IFlyoutView, FlyoutPageHandler> Mapper =
        new PropertyMapper<IFlyoutView, FlyoutPageHandler>(ViewMapper)
        {
            [nameof(IFlyoutView.Flyout)] = MapFlyout,
            [nameof(IFlyoutView.Detail)] = MapDetail,
            [nameof(IFlyoutView.IsPresented)] = MapIsPresented,
            [nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
            [nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
        };

    FlyoutPage? FlyoutPage => VirtualView as FlyoutPage;

    public FlyoutPageHandler() : base(Mapper)
    {
    }

    protected override FlyoutContainerView CreatePlatformView()
    {
        bool useNative = VirtualView is Microsoft.Maui.Controls.FlyoutPage fp
            && MacOSFlyoutPage.GetUseNativeSidebar(fp);
        var view = new FlyoutContainerView(useNative);
        view.OnFlyoutLayout = OnFlyoutLayout;
        view.OnDetailLayout = OnDetailLayout;
        return view;
    }

    void OnFlyoutLayout(CGRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var flyout = VirtualView?.Flyout;
        if (flyout != null)
        {
            flyout.Measure((double)bounds.Width, (double)bounds.Height);
            flyout.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
        }
    }

    void OnDetailLayout(CGRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var detail = VirtualView?.Detail;
        if (detail != null)
        {
            detail.Measure((double)bounds.Width, (double)bounds.Height);
            detail.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
        }
    }

    public static void MapFlyout(FlyoutPageHandler handler, IFlyoutView view)
    {
        if (handler.MauiContext == null || view.Flyout == null)
            return;

        var platformView = view.Flyout.ToMacOSPlatform(handler.MauiContext);
        handler.PlatformView.ShowFlyout(platformView);
    }

    public static void MapDetail(FlyoutPageHandler handler, IFlyoutView view)
    {
        if (handler.MauiContext == null || view.Detail == null)
            return;

        var platformView = view.Detail.ToMacOSPlatform(handler.MauiContext);
        handler.PlatformView.ShowDetail(platformView);
    }

    public static void MapIsPresented(FlyoutPageHandler handler, IFlyoutView view)
    {
        handler.PlatformView.SetFlyoutVisible(view.IsPresented);
    }

    public static void MapFlyoutBehavior(FlyoutPageHandler handler, IFlyoutView view)
    {
        // On macOS with NSSplitView, Locked = always visible sidebar, Flyout = collapsible
        // Disabled = hide the flyout entirely
        switch (view.FlyoutBehavior)
        {
            case FlyoutBehavior.Disabled:
                handler.PlatformView.SetFlyoutVisible(false);
                break;
            case FlyoutBehavior.Locked:
                handler.PlatformView.SetFlyoutVisible(true);
                break;
            case FlyoutBehavior.Flyout:
                handler.PlatformView.SetFlyoutVisible(view.IsPresented);
                break;
        }
    }

    public static void MapFlyoutWidth(FlyoutPageHandler handler, IFlyoutView view)
    {
        handler.PlatformView.FlyoutWidth = view.FlyoutWidth > 0 ? view.FlyoutWidth : 185;
        handler.PlatformView.SetFlyoutVisible(true);
    }
}
