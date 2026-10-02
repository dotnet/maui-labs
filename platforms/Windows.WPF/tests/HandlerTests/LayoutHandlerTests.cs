using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using Xunit.Abstractions;
using MRect = Microsoft.Maui.Graphics.Rect;
using MSize = Microsoft.Maui.Graphics.Size;
using WElement = System.Windows.FrameworkElement;
using WRect = System.Windows.Rect;
using WSize = System.Windows.Size;

namespace HandlerTests;

[CollectionDefinition("Layout handlers", DisableParallelization = true)]
public sealed class LayoutHandlerCollection;

[Collection("Layout handlers")]
public class LayoutHandlerTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData(FlexWrap.Wrap)]
	[InlineData(FlexWrap.NoWrap)]
	public void FlexLayout_FirstNativeMeasure_ReservesHeightAndArrangesEntries(FlexWrap wrap)
	{
		Run(context =>
		{
			var flex = CreateFlex(wrap);
			var sibling = new HorizontalStackLayout
			{
				Children = { new Entry { Text = "29", WidthRequest = 60 }, new Entry { Text = "1939", WidthRequest = 80 } },
			};
			var above = new Label { Text = "Above" };
			var below = new Label { Text = "End" };
			var root = new VerticalStackLayout { Children = { above, flex, sibling, below } };
			WithNativeLayout(root, context, panel =>
			{
				Assert.All(flex.Children, child => Assert.Equal(MSize.Zero, child.DesiredSize));
				panel.Measure(new WSize(800, double.PositiveInfinity));
				output.WriteLine($"First measure: flex={((IView)flex).DesiredSize}, stack={panel.DesiredSize}");
				Assert.True(((IView)flex).DesiredSize.Height > 0, "Flex height must be positive on the first native measure.");
				panel.Arrange(new WRect(0, 0, 800, panel.DesiredSize.Height));
				Assert.True(flex.Frame.Height > 0);
				AssertFlex(flex, 1);
				AssertNativeChild(flex, panel);
				AssertNativeChild(sibling, panel);
				Assert.True(flex.Frame.Y >= above.Frame.Bottom - 0.1);
				Assert.True(sibling.Frame.Y >= flex.Frame.Bottom - 0.1);
				Assert.True(below.Frame.Y >= sibling.Frame.Bottom - 0.1);
				Assert.True(sibling.Frame.Height > 0);
			});
		});
	}

	[Theory]
	[InlineData(FlexWrap.Wrap)]
	[InlineData(FlexWrap.NoWrap)]
	public void FlexLayout_WidthChanges_ReflowsNativeChildren(FlexWrap wrap)
	{
		Run(context =>
		{
			var flex = CreateFlex(wrap);
			foreach (var child in flex.Children)
				FlexLayout.SetShrink((BindableObject)child, 0);
			var root = new VerticalStackLayout { Children = { flex } };
			WithNativeLayout(root, context, panel =>
			{
				MeasureAndArrange(panel, 800);
				AssertFlex(flex, 1);
				var singleLineHeight = flex.Frame.Height;
				MeasureAndArrange(panel, 220);
				AssertFlex(flex, wrap == FlexWrap.Wrap ? 2 : 1);
				if (wrap == FlexWrap.Wrap)
					Assert.True(flex.Frame.Height >= singleLineHeight * 1.9);
				else
					Assert.InRange(flex.Frame.Height, singleLineHeight - 0.1, singleLineHeight + 0.1);
				MeasureAndArrange(panel, 800);
				AssertFlex(flex, 1);
				Assert.InRange(flex.Frame.Height, singleLineHeight - 0.1, singleLineHeight + 0.1);
			});
		});
	}

	[Fact]
	public void FlexLayout_Replacement_FirstNativeMeasureUsesNewLayout()
	{
		Run(context =>
		{
			var original = new VerticalStackLayout { Children = { new Label { Text = "Old" } } };
			var replacement = CreateFlex(FlexWrap.Wrap);
			var originalPage = new ContentPage { Content = original };
			var replacementPage = new ContentPage { Content = replacement };
			var handler = Assert.IsType<LayoutHandler>(original.ToHandler(context));
			try
			{
				MeasureAndArrange(handler.PlatformView, 800);
				var oldChild = handler.PlatformView.Children[0];
				handler.SetVirtualView(replacement);
				Assert.Same(handler, replacement.Handler);
				Assert.DoesNotContain(oldChild, handler.PlatformView.Children.Cast<System.Windows.UIElement>());
				Assert.All(replacement.Children, child => Assert.Equal(MSize.Zero, child.DesiredSize));
				MeasureAndArrange(handler.PlatformView, 220);
				AssertFlex(replacement, 2);
			}
			finally
			{
				Disconnect(original);
				Disconnect(replacement);
				GC.KeepAlive(originalPage);
				GC.KeepAlive(replacementPage);
			}
		});
	}

	[Fact]
	public void CreatePlatformView_UsesInterfaceMeasureAndArrangeBeforeRebinding()
	{
		Run(context =>
		{
			var layout = new InterfaceLayout();
			var handler = new CreationProbeHandler();
			handler.SetMauiContext(context);
			try
			{
				handler.SetVirtualView(layout);
				Assert.Equal(new WSize(123, 45), handler.InitialDesiredSize);
				Assert.Equal(1, layout.Measures);
				Assert.Equal(1, layout.Arranges);
				Assert.Equal(new MRect(0, 0, 123, 45), layout.LastBounds);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void SetVirtualView_Replacement_UsesNewInterfaceMeasureAndArrange()
	{
		Run(context =>
		{
			var original = new InterfaceLayout();
			var replacement = new InterfaceLayout();
			var handler = new LayoutHandler();
			handler.SetMauiContext(context);
			try
			{
				handler.SetVirtualView(original);
				MeasureAndArrange(handler.PlatformView, 300);
				Assert.Equal(1, original.Measures);
				Assert.Equal(1, original.Arranges);
				handler.SetVirtualView(replacement);
				handler.PlatformView.InvalidateMeasure();
				handler.PlatformView.InvalidateArrange();
				MeasureAndArrange(handler.PlatformView, 200);
				Assert.Equal(new WSize(123, 45), handler.PlatformView.DesiredSize);
				Assert.Equal(1, replacement.Measures);
				Assert.Equal(1, replacement.Arranges);
				Assert.Equal(new MRect(0, 0, 200, 45), replacement.LastBounds);
				Assert.Equal(1, original.Measures);
				Assert.Equal(1, original.Arranges);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Theory]
	[InlineData("Grid", false)]
	[InlineData("Grid", true)]
	[InlineData("VerticalStackLayout", false)]
	[InlineData("VerticalStackLayout", true)]
	[InlineData("HorizontalStackLayout", false)]
	[InlineData("HorizontalStackLayout", true)]
	public void OrdinaryLayouts_FirstNativeMeasure_PreservesChildSizesAndPlacement(string kind, bool explicitHeight)
	{
		Run(context =>
		{
			Layout layout = kind switch
			{
				"Grid" => new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } },
				"VerticalStackLayout" => new VerticalStackLayout(),
				_ => new HorizontalStackLayout(),
			};
			var first = new Entry { Text = "First", WidthRequest = 60 };
			var second = new Entry { Text = "Second", WidthRequest = 140 };
			if (explicitHeight)
			{
				first.HeightRequest = 25;
				second.HeightRequest = 35;
			}
			layout.Add(first);
			layout.Add(second);
			if (layout is Grid)
				Grid.SetRow(second, 1);
			WithNativeLayout(layout, context, panel =>
			{
				MeasureAndArrange(panel, 400);
				Assert.True(panel.DesiredSize.Height > 0);
				AssertNativeChild(first, panel);
				AssertNativeChild(second, panel);
				if (explicitHeight)
				{
					Assert.Equal(25, first.Frame.Height);
					Assert.Equal(35, second.Frame.Height);
				}
				if (layout is HorizontalStackLayout)
				{
					Assert.True(second.Frame.X >= first.Frame.Right - 0.1);
					var firstCenter = first.Frame.Y + first.Frame.Height / 2;
					var secondCenter = second.Frame.Y + second.Frame.Height / 2;
					Assert.InRange(secondCenter, firstCenter - 0.1, firstCenter + 0.1);
				}
				else
					Assert.True(second.Frame.Y >= first.Frame.Bottom - 0.1);
			});
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NestedLayout_WidthChanges_RemeasuresWrappingLabel(bool useGrid)
	{
		Run(context =>
		{
			var label = new Label
			{
				Text = "A wrapping label must be measured again when the available width changes, without taking the height of its siblings.",
				LineBreakMode = LineBreakMode.WordWrap,
			};
			Layout nested = useGrid ? new Grid() : new VerticalStackLayout();
			nested.Add(label);
			var root = new VerticalStackLayout
			{
				Children = { new Label { Text = "Above" }, nested, new Label { Text = "Below" } },
			};
			WithNativeLayout(root, context, panel =>
			{
				MeasureAndArrange(panel, 400);
				AssertNativeChild(nested, panel);
				var wideHeight = label.Frame.Height;
				MeasureAndArrange(panel, 120);
				AssertNativeChild(nested, panel);
				AssertNativeChild(label, Assert.IsType<LayoutPanel>(nested.Handler!.PlatformView));
				Assert.True(label.Frame.Height > wideHeight);
				MeasureAndArrange(panel, 400);
				AssertNativeChild(nested, panel);
				Assert.InRange(label.Frame.Height, wideHeight - 0.1, wideHeight + 0.1);
			});
		});
	}

	[Fact]
	public void NestedFlexLayout_WidthChanges_RefreshesNativeMeasurements()
	{
		Run(context =>
		{
			var nested = new FlexLayout
			{
				Wrap = FlexWrap.Wrap,
				Children =
				{
					new Entry { Text = "First", WidthRequest = 80, HeightRequest = 25 },
					new Entry { Text = "Second", WidthRequest = 80, HeightRequest = 25 },
				},
			};
			var outer = new FlexLayout { Direction = FlexDirection.Column, Children = { nested } };
			WithNativeLayout(outer, context, panel =>
			{
				foreach (var width in new[] { 120, 200, 120 })
				{
					panel.Measure(new WSize(width, 200));
					panel.Arrange(new WRect(0, 0, width, panel.DesiredSize.Height));
					var expectedHeight = width < 160 ? 50 : 25;
					var nativeNested = Assert.IsType<LayoutPanel>(nested.Handler!.PlatformView);
					output.WriteLine($"Width={width}: outer={panel.DesiredSize}, nested={nested.Frame}, native={nativeNested.RenderSize}");
					Assert.InRange(panel.DesiredSize.Height, expectedHeight - 0.1, expectedHeight + 0.1);
					AssertNativeChild(nested, panel);
					foreach (var child in nested.Children)
						AssertNativeChild(child, nativeNested);
				}
			});
		});
	}

	static FlexLayout CreateFlex(FlexWrap wrap) => new()
	{
		Wrap = wrap,
		Children =
		{
			new Entry { Text = "29", WidthRequest = 60 },
			new Entry { Text = "September", WidthRequest = 140 },
			new Entry { Text = "1939", WidthRequest = 80 },
		},
	};

	void AssertFlex(FlexLayout flex, int lines, bool checkRequestedWidths = true)
	{
		var panel = Assert.IsType<LayoutPanel>(flex.Handler!.PlatformView);
		output.WriteLine($"Flex desired={((IView)flex).DesiredSize}, frame={flex.Frame}, native={panel.RenderSize}");
		foreach (var child in flex.Children)
			output.WriteLine($"Child desired={child.DesiredSize}, frame={child.Frame}");
		Assert.True(((IView)flex).DesiredSize.Height > 0 || panel.DesiredSize.Height > 0, "Flex height must be positive on the first native measure.");
		Assert.True(panel.ActualHeight > 0);
		Assert.Equal(3, panel.Children.Count);
		foreach (var child in flex.Children)
		{
			AssertNativeChild(child, panel);
			if (checkRequestedWidths)
				Assert.InRange(child.Frame.Width, ((Entry)child).WidthRequest - 0.1, ((Entry)child).WidthRequest + 0.1);
		}
		Assert.InRange(flex.Children[1].Frame.Y, flex.Children[0].Frame.Y - 0.1, flex.Children[0].Frame.Y + 0.1);
		Assert.True(flex.Children[1].Frame.X >= flex.Children[0].Frame.Right - 0.1);
		if (lines == 2)
		{
			Assert.True(flex.Children[2].Frame.Y >= flex.Children[0].Frame.Bottom - 0.1);
			Assert.InRange(flex.Children[2].Frame.X, -0.1, 0.1);
		}
		else
		{
			Assert.InRange(flex.Children[2].Frame.Y, flex.Children[0].Frame.Y - 0.1, flex.Children[0].Frame.Y + 0.1);
			Assert.True(flex.Children[2].Frame.X >= flex.Children[1].Frame.Right - 0.1);
		}
	}

	static void AssertNativeChild(IView child, LayoutPanel parent)
	{
		var native = Assert.IsAssignableFrom<WElement>(child.Handler!.PlatformView);
		Assert.Contains(native, parent.Children.Cast<System.Windows.UIElement>());
		Assert.True(child.Frame.Width > 0 && child.Frame.Height > 0);
		Assert.True(native.ActualWidth > 0 && native.ActualHeight > 0);
		var origin = native.TranslatePoint(new System.Windows.Point(), parent);
		Assert.InRange(origin.X, child.Frame.X - 0.1, child.Frame.X + 0.1);
		Assert.InRange(origin.Y, child.Frame.Y - 0.1, child.Frame.Y + 0.1);
		Assert.InRange(native.ActualWidth, child.Frame.Width - 0.1, child.Frame.Width + 0.1);
		Assert.InRange(native.ActualHeight, child.Frame.Height - 0.1, child.Frame.Height + 0.1);
		Assert.True(origin.Y + native.ActualHeight <= parent.ActualHeight + 0.1);
	}

	static void MeasureAndArrange(LayoutPanel panel, double width)
	{
		panel.Measure(new WSize(width, double.PositiveInfinity));
		panel.Arrange(new WRect(0, 0, width, panel.DesiredSize.Height));
	}

	static void WithNativeLayout(Layout layout, IMauiContext context, Action<LayoutPanel> test)
	{
		var page = new ContentPage { Content = layout };
		try
		{
			test(Assert.IsType<LayoutPanel>(layout.ToPlatform(context)));
		}
		finally
		{
			Disconnect(layout);
			GC.KeepAlive(page);
		}
	}

	static void Disconnect(IView view)
	{
		if (view is Layout layout)
			foreach (var child in layout.Children)
				Disconnect(child);
		view.Handler?.DisconnectHandler();
	}

	static void Run(Action<IMauiContext> test) => WpfApplicationHost.Run(_ =>
	{
		DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
		using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
		test(new WPFMauiContext(app.Services));
	});

	sealed class InterfaceLayout : VerticalStackLayout, ICrossPlatformLayout
	{
		public int Measures { get; private set; }
		public int Arranges { get; private set; }
		public MRect LastBounds { get; private set; }

		MSize ICrossPlatformLayout.CrossPlatformMeasure(double widthConstraint, double heightConstraint)
		{
			Measures++;
			return new MSize(123, 45);
		}

		MSize ICrossPlatformLayout.CrossPlatformArrange(MRect bounds)
		{
			Arranges++;
			LastBounds = bounds;
			return bounds.Size;
		}
	}

	sealed class CreationProbeHandler : LayoutHandler
	{
		public WSize InitialDesiredSize { get; private set; }

		protected override LayoutPanel CreatePlatformView()
		{
			var panel = base.CreatePlatformView();
			// Exercise the creation delegates before SetVirtualView can overwrite them.
			panel.Measure(new WSize(300, double.PositiveInfinity));
			InitialDesiredSize = panel.DesiredSize;
			panel.Arrange(new WRect(0, 0, 123, 45));
			return panel;
		}
	}
}
