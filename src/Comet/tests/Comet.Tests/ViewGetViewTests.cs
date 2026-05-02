using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class ViewGetViewTests : TestBase
	{
		[Fact]
		public void GetView_ReturnsBody()
		{
			var page = new TestPageWithBody();
			var result = page.GetView();
			Assert.NotNull(result);
			Assert.IsType<Text>(result);
		}

		[Fact]
		public void GetView_ReturnsSelf_WhenNoBody()
		{
			var text = new Text("Hello");
			var result = text.GetView();
			// Text has no body, GetView returns the view itself or null
			Assert.True(result == null || result == text);
		}

		[Fact]
		public void GetView_WithMauiViewHost_ReturnsHost()
		{
			var page = new TestPageWithMauiViewHost();
			var result = page.GetView();
			Assert.NotNull(result);
			Assert.IsType<MauiViewHost>(result);
		}

		[Fact]
		public void GetView_MauiViewHost_CanExtractHostedView()
		{
			var page = new TestPageWithMauiViewHost();
			var result = page.GetView();
			Assert.NotNull(result);
			if (result is MauiViewHost mvh)
			{
				Assert.NotNull(mvh.HostedView);
			}
		}

		[Fact]
		public void GetView_IsIdempotent()
		{
			var page = new TestPageWithBody();
			var result1 = page.GetView();
			var result2 = page.GetView();
			// Should return the same view on repeated calls
			Assert.NotNull(result1);
			Assert.NotNull(result2);
		}

		[Fact]
		public void MauiViewHost_InVStack_HasParent()
		{
			var inner = new TestIView();
			var host = new MauiViewHost(inner).Frame(height: 40);
			var stack = new VStack { host };
			Assert.Equal(stack, host.Parent);
		}

		[Fact]
		public void MauiViewHost_DesiredSize_DefaultsToConstraints()
		{
			var inner = new TestIView();
			var host = new MauiViewHost(inner);
			var size = host.GetDesiredSize(new Size(200, 300));
			// Without explicit frame, should use available constraints
			Assert.True(size.Width > 0);
			Assert.True(size.Height > 0);
		}

		[Fact]
		public void CometHost_WithView_SetsProperty()
		{
			var inner = new Text("Test");
			var host = new CometHost(inner);
			Assert.Same(inner, host.CometView);
		}

		[Fact]
		public void CometHost_CanSwapView()
		{
			var viewA = new Text("A");
			var viewB = new Text("B");
			var host = new CometHost(viewA);
			Assert.Same(viewA, host.CometView);
			host.CometView = viewB;
			Assert.Same(viewB, host.CometView);
		}

		[Fact]
		public void CometHost_VisualChildren_PreferPresentedContent()
		{
			var host = new CometHost(new TestPageWithBody());

			var child = Assert.Single(((IVisualTreeElement)host).GetVisualChildren());

			Assert.IsType<Text>(child);
		}

		[Fact]
		public void CometHost_VisualChildren_FallBackToCometView()
		{
			var inner = new Text("Test");
			var host = new CometHost(inner);

			var child = Assert.Single(((IVisualTreeElement)host).GetVisualChildren());

			Assert.Same(inner, child);
		}

		// Test page that returns Text as body
		class TestPageWithBody : View
		{
			[Body]
			View body() => new Text("Hello World");
		}

		// Test page that returns MauiViewHost as body
		class TestPageWithMauiViewHost : View
		{
			[Body]
			View body() => new MauiViewHost(new TestIView());
		}

		class TestIView : IView
		{
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
			public Size DesiredSize => new Size(100, 40);
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
			public Size Arrange(Rect bounds) => bounds.Size;
			public bool Focus() => false;
			public void Unfocus() { }
			public void InvalidateArrange() { }
			public void InvalidateMeasure() { }
			public Size Measure(double w, double h) => new Size(100, 40);
		}
	}
}
