using System.Collections;
using System.Collections.Specialized;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;
using CoreFoundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class CarouselViewHandler : MacOSViewHandler<CarouselView, NSScrollView>
{
	public static readonly IPropertyMapper<CarouselView, CarouselViewHandler> Mapper =
		new PropertyMapper<CarouselView, CarouselViewHandler>(ViewMapper)
		{
			[nameof(ItemsView.ItemsSource)] = MapItemsSource,
			[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
			[nameof(CarouselView.Position)] = MapPosition,
			[nameof(CarouselView.IsSwipeEnabled)] = MapIsSwipeEnabled,
			[nameof(CarouselView.PeekAreaInsets)] = MapPeekAreaInsets,
			[nameof(CarouselView.Loop)] = MapLoop,
		};

	FlippedDocumentView? _documentView;
	NSView? _itemsContainer;
	INotifyCollectionChanged? _observableSource;
	NSObject? _scrollObserver;
	int _itemCount;
	bool _isScrolling;
	bool _isSwipeEnabled = true;

	public CarouselViewHandler() : base(Mapper)
	{
	}

	protected override NSScrollView CreatePlatformView()
	{
		var scrollView = new NSScrollView
		{
			HasVerticalScroller = false,
			HasHorizontalScroller = false,
			AutohidesScrollers = true,
			DrawsBackground = false,
		};

		_documentView = new FlippedDocumentView();
		_itemsContainer = new NSView();
		_documentView.AddSubview(_itemsContainer);
		scrollView.DocumentView = _documentView;

		// Enable horizontal scrolling via scroll wheel/trackpad
		scrollView.HorizontalScrollElasticity = NSScrollElasticity.Allowed;
		scrollView.VerticalScrollElasticity = NSScrollElasticity.None;

		return scrollView;
	}

	protected override void ConnectHandler(NSScrollView platformView)
	{
		base.ConnectHandler(platformView);

		// Listen for scroll end to snap to nearest page
		var clipView = platformView.ContentView;
		clipView.PostsBoundsChangedNotifications = true;
		_scrollObserver = NSNotificationCenter.DefaultCenter.AddObserver(
			NSView.BoundsChangedNotification,
			OnScrollBoundsChanged,
			clipView);
	}

	protected override void DisconnectHandler(NSScrollView platformView)
	{
		if (_scrollObserver != null)
		{
			NSNotificationCenter.DefaultCenter.RemoveObserver(_scrollObserver);
			_scrollObserver = null;
		}
		UnsubscribeCollection();
		base.DisconnectHandler(platformView);
	}

	void OnScrollBoundsChanged(Foundation.NSNotification notification)
	{
		if (_isScrolling || _itemsContainer == null || VirtualView == null || _itemCount == 0)
			return;

		// Debounce: snap after scroll settles
		_isScrolling = true;
		DispatchQueue.MainQueue.DispatchAfter(
			new DispatchTime(DispatchTime.Now, 200_000_000), // 200ms
			() =>
			{
				_isScrolling = false;
				if (VirtualView != null && PlatformView != null && _itemCount > 0)
				{
					try { SnapToNearestPage(); }
					catch { /* handler may have been disconnected */ }
				}
			});
	}

	void SnapToNearestPage()
	{
		if (_itemCount == 0 || PlatformView == null)
			return;

		var scrollX = PlatformView.ContentView.Bounds.X;
		var pageWidth = PlatformView.Frame.Width;
		if (pageWidth <= 0)
			return;

		var pageIndex = (int)Math.Round(scrollX / pageWidth);
		pageIndex = Math.Clamp(pageIndex, 0, _itemCount - 1);

		ScrollToPosition(pageIndex, true);

		if (VirtualView != null && VirtualView.Position != pageIndex)
		{
			VirtualView.Position = pageIndex;
			UpdateCurrentItem(pageIndex);
		}
	}

	void ScrollToPosition(int position, bool animated)
	{
		if (PlatformView == null || _itemCount == 0)
			return;

		var pageWidth = PlatformView.Frame.Width;
		var targetX = position * pageWidth;
		var scrollView = PlatformView;

		if (animated)
		{
			NSAnimationContext.RunAnimation(ctx =>
			{
				ctx.Duration = 0.3;
				ctx.AllowsImplicitAnimation = true;
				scrollView.ContentView.ScrollToPoint(new CGPoint(targetX, 0));
			}, () =>
			{
				try { scrollView.ReflectScrolledClipView(scrollView.ContentView); }
				catch { /* view may have been disconnected */ }
			});
		}
		else
		{
			PlatformView.ContentView.ScrollToPoint(new CGPoint(targetX, 0));
			PlatformView.ReflectScrolledClipView(PlatformView.ContentView);
		}
	}

	void UpdateCurrentItem(int position)
	{
		if (VirtualView?.ItemsSource == null)
			return;

		var items = VirtualView.ItemsSource;
		int index = 0;
		foreach (var item in items)
		{
			if (index == position)
			{
				VirtualView.CurrentItem = item;
				return;
			}
			index++;
		}
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		LayoutItems(rect);
	}

	void LayoutItems(Rect rect)
	{
		if (_itemsContainer == null || _documentView == null)
			return;

		var subviews = _itemsContainer.Subviews;
		if (subviews.Length == 0)
			return;

		var pageWidth = rect.Width;
		var pageHeight = rect.Height;
		nfloat x = 0;

		foreach (var subview in subviews)
		{
			subview.Frame = new CGRect(x, 0, pageWidth, pageHeight);
			x += (nfloat)pageWidth;
		}

		_itemsContainer.Frame = new CGRect(0, 0, x, pageHeight);
		_documentView.Frame = new CGRect(0, 0, x, pageHeight);

		// Maintain current position after layout
		if (VirtualView != null)
			ScrollToPosition(VirtualView.Position, false);
	}

	public static void MapItemsSource(CarouselViewHandler handler, CarouselView view)
	{
		handler.ReloadItems();
	}

	public static void MapItemTemplate(CarouselViewHandler handler, CarouselView view)
	{
		handler.ReloadItems();
	}

	public static void MapPosition(CarouselViewHandler handler, CarouselView view)
	{
		handler.ScrollToPosition(view.Position, true);
		handler.UpdateCurrentItem(view.Position);
	}

	public static void MapIsSwipeEnabled(CarouselViewHandler handler, CarouselView view)
	{
		handler._isSwipeEnabled = view.IsSwipeEnabled;
		if (handler.PlatformView != null)
		{
			handler.PlatformView.HorizontalScrollElasticity = view.IsSwipeEnabled
				? NSScrollElasticity.Allowed
				: NSScrollElasticity.None;
		}
	}

	public static void MapPeekAreaInsets(CarouselViewHandler handler, CarouselView view)
	{
		// Re-layout to account for peek insets
		if (handler.PlatformView?.Frame.Width > 0)
			handler.LayoutItems(new Rect(0, 0, handler.PlatformView.Frame.Width, handler.PlatformView.Frame.Height));
	}

	public static void MapLoop(CarouselViewHandler handler, CarouselView view)
	{
		// Loop support would require duplicating items; skip for now
	}

	void UnsubscribeCollection()
	{
		if (_observableSource != null)
		{
			_observableSource.CollectionChanged -= OnCollectionChanged;
			_observableSource = null;
		}
	}

	void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		ReloadItems();
	}

	void ReloadItems()
	{
		if (_itemsContainer == null || MauiContext == null)
			return;

		UnsubscribeCollection();

		foreach (var subview in _itemsContainer.Subviews)
			subview.RemoveFromSuperview();

		var itemsSource = VirtualView?.ItemsSource;
		if (itemsSource == null)
		{
			_itemCount = 0;
			return;
		}

		if (itemsSource is INotifyCollectionChanged observable)
		{
			_observableSource = observable;
			_observableSource.CollectionChanged += OnCollectionChanged;
		}

		var template = VirtualView?.ItemTemplate;
		_itemCount = 0;

		foreach (var item in itemsSource)
		{
			var view = CreateItemView(item, template);
			if (view != null)
			{
				var platformView = view.ToMacOSPlatform(MauiContext);
				_itemsContainer.AddSubview(platformView);
				_itemCount++;
			}
		}

		if (PlatformView.Frame.Width > 0)
			LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
	}

	static IView? CreateItemView(object item, DataTemplate? template)
	{
		if (template != null)
		{
			var content = template.CreateContent();
			if (content is View view)
			{
				view.BindingContext = item;
				return view;
			}
		}

		return new Label { Text = item?.ToString() ?? string.Empty };
	}
}
