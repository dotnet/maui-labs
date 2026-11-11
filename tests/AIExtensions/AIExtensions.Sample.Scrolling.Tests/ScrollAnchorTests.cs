using System.Collections.ObjectModel;

namespace AIExtensions.Sample.Scrolling.Tests;

/// <summary>
/// Tests the attached properties and the handler lifecycle: a fake handler stands in for MAUI's, and a fake adapter for
/// the platform's scroll view.
/// </summary>
public sealed class ScrollAnchorTests : IDisposable
{
    private readonly Func<ScrollAnchorController, View, object, ScrollAnchorAdapter?> _originalFactory = ScrollAnchor.AdapterFactory;
    private readonly List<FakeAdapter> _adapters = [];

    public ScrollAnchorTests() =>
        ScrollAnchor.AdapterFactory = (controller, view, platformView) =>
        {
            var adapter = new FakeAdapter(controller, platformView);
            _adapters.Add(adapter);
            return adapter;
        };

    public void Dispose() => ScrollAnchor.AdapterFactory = _originalFactory;

    [Fact]
    public void ModeEndStartsFollowingTheEndOfAViewWithAHandler()
    {
        var list = new CollectionView { Handler = new FakeHandler() };

        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);

        Assert.Equal(ScrollAnchorMode.End, ScrollAnchor.GetMode(list));
        Assert.True(ScrollAnchor.GetIsAnchored(list));
        var adapter = Assert.Single(_adapters);
        Assert.Same(list.Handler!.PlatformView, adapter.PlatformView);
        Assert.Equal(1, adapter.Invalidations);
    }

    [Fact]
    public void TheModeDefaultsToNoneAndIsAnchoredToFalse()
    {
        var list = new CollectionView { Handler = new FakeHandler() };

        Assert.Equal(ScrollAnchorMode.None, ScrollAnchor.GetMode(list));
        Assert.False(ScrollAnchor.GetIsAnchored(list));
        Assert.Empty(_adapters);
    }

    [Fact]
    public void ThePlatformAdapterComesAndGoesWithTheHandler()
    {
        var list = new CollectionView();
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        Assert.Empty(_adapters);

        list.Handler = new FakeHandler();
        var first = Assert.Single(_adapters);

        list.Handler = new FakeHandler();
        Assert.True(first.IsDisposed);
        Assert.Equal(2, _adapters.Count);
        Assert.False(_adapters[1].IsDisposed);

        list.Handler = null;
        Assert.True(_adapters[1].IsDisposed);
        Assert.Equal(2, _adapters.Count);
    }

    [Fact]
    public void ModeNoneDisposesTheAdapterAndStopsReportingAnchored()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);

        ScrollAnchor.SetMode(list, ScrollAnchorMode.None);

        Assert.True(Assert.Single(_adapters).IsDisposed);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        // Nothing is left subscribed: a new handler doesn't bring an adapter back.
        list.Handler = new FakeHandler();
        Assert.Single(_adapters);
    }

    [Fact]
    public void SettingTheModeAgainKeepsTheSameAdapter()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);

        Assert.Single(_adapters);
    }

    [Fact]
    public void IsAnchoredReportsWhetherTheViewFollowsItsEnd()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        var adapter = Assert.Single(_adapters);
        var changes = new List<bool>();
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == ScrollAnchor.IsAnchoredProperty.PropertyName)
                changes.Add(ScrollAnchor.GetIsAnchored(list));
        };

        Assert.Equal(600, adapter.Report(0, 1000, 400));
        Assert.Null(adapter.Report(600, 1000, 400));
        Assert.Null(adapter.Report(500, 1000, 400, userScrolling: true));
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        Assert.Null(adapter.Report(600, 1000, 400, userScrolling: true));
        Assert.True(ScrollAnchor.GetIsAnchored(list));
        Assert.Equal([false, true], changes);
    }

    [Fact]
    public void IsAnchoredIsReadOnly()
    {
        var list = new CollectionView();

        Assert.True(ScrollAnchor.IsAnchoredProperty.IsReadOnly);
        list.SetValue(ScrollAnchor.IsAnchoredProperty, true);
        Assert.False(ScrollAnchor.GetIsAnchored(list));
    }

    [Fact]
    public void IsAnchoredIsFalseWhileThereIsNoPlatformViewToFollowTheEndWith()
    {
        var list = new CollectionView();
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        list.Handler = new FakeHandler();
        Assert.True(ScrollAnchor.GetIsAnchored(list));

        list.Handler = null;
        Assert.False(ScrollAnchor.GetIsAnchored(list));
    }

    [Fact]
    public void IsAnchoredStaysFalseOnAPlatformWithoutAnAdapter()
    {
        // Such as Windows: the view doesn't follow its end, so a "jump to latest" button stays available.
        ScrollAnchor.AdapterFactory = (_, _, _) => null;
        var list = new CollectionView { Handler = new FakeHandler() };

        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        ScrollAnchor.ScrollToEnd(list, animate: false);
        Assert.False(ScrollAnchor.GetIsAnchored(list));
    }

    [Fact]
    public void ScrollToEndFollowsTheEndAgain()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        var adapter = Assert.Single(_adapters);
        adapter.Report(600, 1000, 400);
        adapter.Report(100, 1000, 400, userScrolling: true);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        ScrollAnchor.ScrollToEnd(list);

        Assert.True(ScrollAnchor.GetIsAnchored(list));
        Assert.Equal([true], adapter.ScrollsToEnd);

        ScrollAnchor.ScrollToEnd(list, animate: false);
        Assert.Equal([true, false], adapter.ScrollsToEnd);
    }

    [Fact]
    public void AScrollToFromTheAppIsJudgedByWhereItLands()
    {
        var list = new CollectionView { Handler = new FakeHandler(), ItemsSource = Enumerable.Range(0, 50).ToList() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        var adapter = Assert.Single(_adapters);
        adapter.Report(600, 1000, 400);

        list.ScrollTo(3, position: ScrollToPosition.Start, animate: false);
        Assert.Equal(2, adapter.Invalidations);

        // The platform jumped there, re-estimating the content's size on the way: that isn't scrolled back.
        Assert.Null(adapter.Report(120, 1040, 400));
        Assert.False(ScrollAnchor.GetIsAnchored(list));
        Assert.Null(adapter.Report(120, 1100, 400));
    }

    [Fact]
    public void ANewPlatformViewFollowsTheEndFromTheStartAgain()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        _adapters[0].Report(600, 1000, 400);
        _adapters[0].Report(100, 1000, 400, userScrolling: true);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        list.Handler = new FakeHandler();

        Assert.True(ScrollAnchor.GetIsAnchored(list));
        Assert.Equal(600, _adapters[1].Report(0, 1000, 400));
    }

    [Fact]
    public void ScrollViewsAreSupported()
    {
        var scrollView = new ScrollView { Handler = new FakeHandler() };

        ScrollAnchor.SetMode(scrollView, ScrollAnchorMode.End);

        Assert.Single(_adapters);
        Assert.True(ScrollAnchor.GetIsAnchored(scrollView));
    }

    [Fact]
    public void HorizontalViewsAreLeftAlone()
    {
        var list = new CollectionView { ItemsLayout = LinearItemsLayout.Horizontal, Handler = new FakeHandler() };
        var scrollView = new ScrollView { Orientation = ScrollOrientation.Horizontal, Handler = new FakeHandler() };

        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        ScrollAnchor.SetMode(scrollView, ScrollAnchorMode.End);

        Assert.Empty(_adapters);
        Assert.False(ScrollAnchor.GetIsAnchored(list));
        Assert.False(ScrollAnchor.GetIsAnchored(scrollView));
    }

    [Fact]
    public void AListFollowsItsEndOnlyWhileItsLayoutIsVertical()
    {
        var list = new CollectionView { Handler = new FakeHandler() };
        ScrollAnchor.SetMode(list, ScrollAnchorMode.End);
        var first = Assert.Single(_adapters);
        first.Report(600, 1000, 400);
        first.Report(100, 1000, 400, userScrolling: true);

        // Another vertical layout keeps the adapter, and with it that the user scrolled away.
        list.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);
        Assert.Single(_adapters);
        Assert.False(first.IsDisposed);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        list.ItemsLayout = LinearItemsLayout.Horizontal;
        Assert.True(first.IsDisposed);
        Assert.False(ScrollAnchor.GetIsAnchored(list));

        list.ItemsLayout = LinearItemsLayout.Vertical;
        Assert.Equal(2, _adapters.Count);
        Assert.True(ScrollAnchor.GetIsAnchored(list));
    }

    [Fact]
    public void AScrollViewFollowsItsEndOnlyWhileItScrollsVertically()
    {
        var scrollView = new ScrollView { Orientation = ScrollOrientation.Horizontal, Handler = new FakeHandler() };
        ScrollAnchor.SetMode(scrollView, ScrollAnchorMode.End);
        Assert.Empty(_adapters);

        scrollView.Orientation = ScrollOrientation.Both;
        var adapter = Assert.Single(_adapters);
        Assert.True(ScrollAnchor.GetIsAnchored(scrollView));

        scrollView.Orientation = ScrollOrientation.Vertical;
        Assert.Single(_adapters);

        scrollView.Orientation = ScrollOrientation.Neither;
        Assert.True(adapter.IsDisposed);
        Assert.False(ScrollAnchor.GetIsAnchored(scrollView));
    }

    [Fact]
    public void OtherViewsIgnoreTheMode()
    {
        var label = new Label { Handler = new FakeHandler() };
        var carousel = new CarouselView { Handler = new FakeHandler() };

        ScrollAnchor.SetMode(label, ScrollAnchorMode.End);
        ScrollAnchor.SetMode(carousel, ScrollAnchorMode.End);

        Assert.Empty(_adapters);
        Assert.False(ScrollAnchor.GetIsAnchored(label));
        Assert.False(ScrollAnchor.GetIsAnchored(carousel));
    }

    [Fact]
    public void ScrollToEndWithoutTheModeScrollsAGroupedListToItsLastItem()
    {
        var groups = new ObservableCollection<List<string>> { new() { "a", "b" }, new() { "c", "d", "e" }, new() };
        var list = new CollectionView { IsGrouped = true, ItemsSource = groups };
        ScrollToRequestEventArgs? request = null;
        list.ScrollToRequested += (_, e) => request = e;

        ScrollAnchor.ScrollToEnd(list, animate: false);

        Assert.NotNull(request);
        Assert.Equal(1, request.GroupIndex);
        Assert.Equal(2, request.Index);
        Assert.Equal(ScrollToPosition.End, request.ScrollToPosition);
        Assert.False(request.IsAnimated);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => ScrollAnchor.GetMode(null!));
        Assert.Throws<ArgumentNullException>(() => ScrollAnchor.SetMode(null!, ScrollAnchorMode.End));
        Assert.Throws<ArgumentNullException>(() => ScrollAnchor.GetIsAnchored(null!));
        Assert.Throws<ArgumentNullException>(() => ScrollAnchor.ScrollToEnd(null!));
    }

    [Fact]
    public void TheModeValuesLeaveRoomForStartAsInBlazor()
    {
        Assert.Equal(0, (int)ScrollAnchorMode.None);
        Assert.Equal(2, (int)ScrollAnchorMode.End);
        Assert.True(typeof(ScrollAnchorMode).IsDefined(typeof(FlagsAttribute), inherit: false));
    }

    private sealed class FakeAdapter(ScrollAnchorController controller, object platformView) : ScrollAnchorAdapter(controller)
    {
        public object PlatformView { get; } = platformView;

        public int Invalidations { get; private set; }

        public List<bool> ScrollsToEnd { get; } = [];

        public bool IsDisposed { get; private set; }

        public double? Report(double offset, double extent, double viewport, bool userScrolling = false) =>
            Controller.Update(new ScrollMetrics(offset, extent, viewport), userScrolling);

        public override void Invalidate() => Invalidations++;

        public override void ScrollToEnd(bool animate) => ScrollsToEnd.Add(animate);

        public override void Dispose() => IsDisposed = true;
    }

    private sealed class FakeHandler : IViewHandler
    {
        public object PlatformView { get; } = new();

        public IView? VirtualView { get; private set; }

        IElement? IElementHandler.VirtualView => VirtualView;

        public IMauiContext? MauiContext => null;

        public bool HasContainer { get; set; }

        public object? ContainerView => null;

        public void SetMauiContext(IMauiContext mauiContext)
        {
        }

        public void SetVirtualView(IElement view) => VirtualView = (IView)view;

        public void UpdateValue(string property)
        {
        }

        public void Invoke(string command, object? args = null)
        {
        }

        public void DisconnectHandler() => VirtualView = null;

        public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;

        public void PlatformArrange(Rect frame)
        {
        }
    }
}
