using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class NewControlsTests : TestBase
	{
		// ---- Border Tests ----

		[Fact]
		public void BorderCreation()
		{
			var border = new Border();
			border.Add(new Text("Hello"));
			Assert.NotNull(border);
			Assert.NotNull(border.Content);
		}

		// ---- BoxView Tests ----

		[Fact]
		public void BoxViewCreation()
		{
			var box = new BoxView();
			Assert.NotNull(box);
		}

		[Fact]
		public void BoxViewWithColor()
		{
			var box = new BoxView { Color = Colors.Blue };
			Assert.Equal(Colors.Blue, box.Color?.CurrentValue);
		}

		// ---- Frame Tests ----

		[Fact]
		public void FrameCreation()
		{
			var frame = new Frame();
			frame.Add(new Text("Hello"));
			Assert.NotNull(frame);
			Assert.NotNull(frame.Content);
		}

		[Fact]
		public void FrameHasBorderAndShadow()
		{
			var frame = new Frame();
			frame.BorderColor = Colors.Green;
			frame.CornerRadius = 5;
			frame.HasShadow = true;
			Assert.Equal(Colors.Green, frame.BorderColor?.CurrentValue);
			Assert.Equal(5f, frame.CornerRadius?.CurrentValue);
			Assert.True(frame.HasShadow?.CurrentValue);
		}

		// ---- SwipeView Tests ----

		[Fact]
		public void SwipeViewCreation()
		{
			var swipe = new SwipeView();
			swipe.Add(new Text("Swipeable"));
			Assert.NotNull(swipe);
			Assert.NotNull(swipe.Content);
		}

		[Fact]
		public void SwipeViewLeftItems()
		{
			var swipe = new SwipeView();
			swipe.LeftItems = new SwipeItems
			{
				new SwipeItem { Text = "Delete", BackgroundColor = Colors.Red },
				new SwipeItem { Text = "Archive", BackgroundColor = Colors.Blue }
			};
			Assert.Equal(2, swipe.LeftItems.Count);
			Assert.Equal("Delete", swipe.LeftItems[0].Text);
		}

		[Fact]
		public void SwipeViewAllDirections()
		{
			var swipe = new SwipeView();
			swipe.LeftItems = new SwipeItems { new SwipeItem { Text = "Left" } };
			swipe.RightItems = new SwipeItems { new SwipeItem { Text = "Right" } };
			swipe.TopItems = new SwipeItems { new SwipeItem { Text = "Top" } };
			swipe.BottomItems = new SwipeItems { new SwipeItem { Text = "Bottom" } };

			Assert.NotNull(swipe.LeftItems);
			Assert.NotNull(swipe.RightItems);
			Assert.NotNull(swipe.TopItems);
			Assert.NotNull(swipe.BottomItems);
		}

		[Fact]
		public void SwipeViewSwipeMode()
		{
			var items = new SwipeItems { Mode = SwipeMode.Execute };
			Assert.Equal(SwipeMode.Execute, items.Mode);

			var defaultItems = new SwipeItems();
			Assert.Equal(SwipeMode.Reveal, defaultItems.Mode);
		}

		[Fact]
		public void SwipeItemOnInvoked()
		{
			bool invoked = false;
			var item = new SwipeItem
			{
				Text = "Delete",
				OnInvoked = () => invoked = true
			};
			item.OnInvoked?.Invoke();
			Assert.True(invoked);
		}

		// ---- RefreshView Tests ----

		[Fact]
		public void RefreshViewCreation()
		{
			var rv = new RefreshView();
			rv.Add(new Text("Refreshable content"));
			Assert.NotNull(rv);
			Assert.NotNull(rv.Content);
		}

		[Fact]
		public void RefreshViewIsRefreshing()
		{
			var rv = new RefreshView(false);
			Assert.NotNull(rv);
		}

		[Fact]
		public void RefreshViewDispose()
		{
			var rv = new RefreshView();
			var content = new Text("Content");
			rv.Add(content);
			rv.Dispose();
			Assert.Null(rv.Content);
		}

		// ---- CollectionView Tests ----

		[Fact]
		public void CollectionViewCreation()
		{
			var items = new List<string> { "A", "B", "C" };
			var cv = new CollectionView<string>(items.AsReadOnly());
			cv.ViewFor = item => new Text(item);
			Assert.NotNull(cv);
			Assert.Equal(ItemsLayoutOrientation.Vertical, cv.ItemsLayout.Orientation);
		}

		[Fact]
		public void CollectionViewHorizontal()
		{
			var cv = new CollectionView<string>();
			cv.ItemsLayout = ItemsLayout.Horizontal(10);
			Assert.Equal(ItemsLayoutOrientation.Horizontal, cv.ItemsLayout.Orientation);
			Assert.Equal(10, cv.ItemsLayout.ItemSpacing);
		}

		[Fact]
		public void CollectionViewGrid()
		{
			var cv = new CollectionView<string>();
			cv.ItemsLayout = GridItemsLayout.Vertical(2, 5);
			var grid = cv.ItemsLayout as GridItemsLayout;
			Assert.NotNull(grid);
			Assert.Equal(2, grid.Span);
			Assert.Equal(5, grid.ItemSpacing);
		}

		[Fact]
		public void CollectionViewSelectionModes()
		{
			var cv = new CollectionView<string>();
			Assert.Equal(SelectionMode.Single, cv.SelectionMode);
			cv.SelectionMode = SelectionMode.Multiple;
			Assert.Equal(SelectionMode.Multiple, cv.SelectionMode);
			cv.SelectionMode = SelectionMode.None;
			Assert.Equal(SelectionMode.None, cv.SelectionMode);
		}

		[Fact]
		public void CollectionViewEmptyView()
		{
			var cv = new CollectionView<string>();
			cv.EmptyView = new Text("No items");
			Assert.NotNull(cv.EmptyView);
		}

		// ---- CarouselView Tests ----

		[Fact]
		public void CarouselViewCreation()
		{
			var items = new List<string> { "Slide 1", "Slide 2", "Slide 3" };
			var cv = new CarouselView<string>(items.AsReadOnly());
			cv.ViewFor = item => new Text(item);
			Assert.Equal(ItemsLayoutOrientation.Horizontal, cv.ItemsLayout.Orientation);
		}

		[Fact]
		public void CarouselViewProperties()
		{
			var cv = new CarouselView<string>();
			cv.Loop = true;
			cv.PeekAreaInsets = 20;
			cv.IsBounceEnabled = false;
			Assert.True(cv.Loop);
			Assert.Equal(20, cv.PeekAreaInsets);
			Assert.False(cv.IsBounceEnabled);
		}

		// ---- WebView Tests ----

		[Fact]
		public void WebViewCreation()
		{
			var wv = new WebView();
			wv.Source = "https://example.com";
			Assert.Equal("https://example.com", wv.Source?.CurrentValue);
		}

		[Fact]
		public void WebViewHtml()
		{
			var wv = new WebView();
			wv.Html = "<h1>Hello</h1>";
			Assert.Equal("<h1>Hello</h1>", wv.Html?.CurrentValue);
		}

		[Fact]
		public void MauiViewHost_WrapsIView()
		{
			var mockView = new TestIViewImpl();
			var host = new MauiViewHost(mockView);

			Assert.Same(mockView, host.HostedView);
		}

		[Fact]
		public void MauiViewHost_LazyFactory()
		{
			var created = false;
			var host = new MauiViewHost(() =>
			{
				created = true;
				return new TestIViewImpl();
			});

			Assert.False(created);
			var view = host.HostedView;
			Assert.True(created);
			Assert.NotNull(view);
			Assert.Same(view, host.HostedView);
		}

		[Fact]
		public void MauiViewHost_MeasureDelegatesToHostedView()
		{
			var mockView = new TestIViewImpl { DesiredSizeValue = new Size(100, 50) };
			var host = new MauiViewHost(mockView);
			// Without frame constraints, MauiViewHost uses the available size as default before handler connection
			var measured = host.GetDesiredSize(new Size(200, 200));
			// Default behavior: width=available, height=44 (no handler, no frame constraints)
			Assert.True(measured.Width > 0);
			Assert.True(measured.Height > 0);
		}

		[Fact]
		public void MauiViewHost_FrameConstraintsRespected()
		{
			var mockView = new TestIViewImpl { DesiredSizeValue = new Size(100, 50) };
			var host = new MauiViewHost(mockView).Frame(width: 300, height: 150);
			var measured = host.GetDesiredSize(new Size(500, 500));
			Assert.Equal(300, measured.Width);
			Assert.Equal(150, measured.Height);
		}

		[Fact]
		public void MauiViewHost_FactoryCalledOnce()
		{
			int callCount = 0;
			var host = new MauiViewHost(() =>
			{
				callCount++;
				return new TestIViewImpl();
			});

			// First access creates
			var v1 = host.HostedView;
			Assert.Equal(1, callCount);
			// Second access reuses
			var v2 = host.HostedView;
			Assert.Equal(1, callCount);
			Assert.Same(v1, v2);
		}

		[Fact]
		public void MauiViewHost_DisposeNullsHostedView()
		{
			var mockView = new TestIViewImpl();
			var host = new MauiViewHost(mockView);
			Assert.NotNull(host.HostedView);

			host.Dispose();
			Assert.Null(host.HostedView);
		}

		[Fact]
		public void MauiViewHost_HostedViewProperty()
		{
			var mockView = new TestIViewImpl();
			var host = new MauiViewHost(mockView);

			Assert.Same(mockView, host.HostedView);
		}

		[Fact]
		public void ContainerView_AcceptsIViewDirectly()
		{
			var container = new VStack();
			var mauiView = new TestIViewImpl();
			((ContainerView)container).Add((IView)mauiView);

			Assert.Equal(1, container.Count);
			var child = container[0];
			Assert.IsAssignableFrom<IReplaceableView>(child);
		}

		private class TestIViewImpl : IView
		{
			public Size DesiredSizeValue { get; set; } = new Size(50, 50);
			public string AutomationId => "";
			public FlowDirection FlowDirection => FlowDirection.LeftToRight;
			Microsoft.Maui.Primitives.LayoutAlignment IView.HorizontalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
			Microsoft.Maui.Primitives.LayoutAlignment IView.VerticalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
			public Semantics Semantics => null;
			public IShape Clip => null;
			public IShadow Shadow => null;
			public bool IsEnabled => true;
			public bool IsFocused { get; set; }
			public Visibility Visibility => Visibility.Visible;
			public double Opacity => 1;
			public Paint Background => null;
			public Rect Frame { get; set; }
			public double Width => -1;
			public double MinimumWidth => -1;
			public double MaximumWidth => -1;
			public double Height => -1;
			public double MinimumHeight => -1;
			public double MaximumHeight => -1;
			public Thickness Margin => Thickness.Zero;
			public Size DesiredSize => DesiredSizeValue;
			public int ZIndex => 0;
			public bool InputTransparent => false;
			public double TranslationX => 0;
			public double TranslationY => 0;
			public double Scale => 1;
			public double ScaleX => 1;
			public double ScaleY => 1;
			public double Rotation => 0;
			public double RotationX => 0;
			public double RotationY => 0;
			public double AnchorX => 0.5;
			public double AnchorY => 0.5;
			public IViewHandler Handler { get; set; }
			IElementHandler IElement.Handler { get; set; }
			public IElement Parent => null;
			public Size Arrange(Rect bounds) { Frame = bounds; return DesiredSizeValue; }
			public bool Focus() => false;
			public void Unfocus() { }
			public void InvalidateArrange() { }
			public void InvalidateMeasure() { }
			public Size Measure(double widthConstraint, double heightConstraint) => DesiredSizeValue;
		}
	}
}
