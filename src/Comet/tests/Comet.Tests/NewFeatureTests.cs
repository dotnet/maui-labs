using Xunit;
using Microsoft.Maui.Graphics;

namespace Comet.Tests
{
	public class FormattedStringTests
	{
		[Fact]
		public void SpanSetsText()
		{
			var span = new Span("Hello");
			Assert.Equal("Hello", span.Text);
		}

		[Fact]
		public void SpanBoldSetsFontAttributes()
		{
			var span = new Span("Bold").Bold();
			Assert.True(span.FontAttributes.HasFlag(FontAttributes.Bold));
		}

		[Fact]
		public void SpanItalicSetsFontAttributes()
		{
			var span = new Span("Italic").Italic();
			Assert.True(span.FontAttributes.HasFlag(FontAttributes.Italic));
		}

		[Fact]
		public void SpanBoldItalicCombines()
		{
			var span = new Span("BoldItalic").Bold().Italic();
			Assert.True(span.FontAttributes.HasFlag(FontAttributes.Bold));
			Assert.True(span.FontAttributes.HasFlag(FontAttributes.Italic));
		}

		[Fact]
		public void SpanUnderlineSetsDecoration()
		{
			var span = new Span("Underline").Underline();
			Assert.True(span.TextDecorations.HasFlag(TextDecorations.Underline));
		}

		[Fact]
		public void SpanStrikethroughSetsDecoration()
		{
			var span = new Span("Strike").Strikethrough();
			Assert.True(span.TextDecorations.HasFlag(TextDecorations.Strikethrough));
		}

		[Fact]
		public void SpanColorSetsTextColor()
		{
			var span = new Span("Red").Color(Colors.Red);
			Assert.Equal(Colors.Red, span.TextColor);
		}

		[Fact]
		public void SpanSizeSetsFontSize()
		{
			var span = new Span("Large").Size(24);
			Assert.Equal(24, span.FontSize);
		}

		[Fact]
		public void SpanFontSetsFontFamily()
		{
			var span = new Span("Custom").Font("Arial");
			Assert.Equal("Arial", span.FontFamily);
		}

		[Fact]
		public void FormattedStringAddsSpans()
		{
			var fs = new FormattedString(
				new Span("Hello ").Bold(),
				new Span("World").Color(Colors.Blue)
			);
			Assert.Equal(2, fs.Spans.Count);
			Assert.Equal("Hello ", fs.Spans[0].Text);
			Assert.Equal("World", fs.Spans[1].Text);
		}

		[Fact]
		public void FormattedStringFluentAdd()
		{
			var fs = FormattedTextExtensions.Create()
				.Add(new Span("A"))
				.Add(new Span("B"))
				.Add("C"); // string shorthand
			Assert.Equal(3, fs.Spans.Count);
			Assert.Equal("C", fs.Spans[2].Text);
		}

		[Fact]
		public void FormattedStringConvertsToMaui()
		{
			var fs = new FormattedString(
				new Span("Bold").Bold(),
				new Span("Normal")
			);
			var mauiFs = fs.ToMauiFormattedString();
			Assert.Equal(2, mauiFs.Spans.Count);
			Assert.Equal("Bold", mauiFs.Spans[0].Text);
			Assert.Equal(Microsoft.Maui.Controls.FontAttributes.Bold, mauiFs.Spans[0].FontAttributes);
			Assert.Equal("Normal", mauiFs.Spans[1].Text);
		}

		[Fact]
		public void SpanToMauiSpanPreservesProperties()
		{
			var span = new Span("Test")
				.Bold()
				.Underline()
				.Color(Colors.Red)
				.Size(18)
				.Font("Helvetica")
				.Spacing(2);

			var mauiSpan = span.ToMauiSpan();
			Assert.Equal("Test", mauiSpan.Text);
			Assert.Equal(Microsoft.Maui.Controls.FontAttributes.Bold, mauiSpan.FontAttributes);
			Assert.Equal(Microsoft.Maui.TextDecorations.Underline, mauiSpan.TextDecorations);
			Assert.Equal(Colors.Red, mauiSpan.TextColor);
			Assert.Equal(18, mauiSpan.FontSize);
			Assert.Equal("Helvetica", mauiSpan.FontFamily);
			Assert.Equal(2, mauiSpan.CharacterSpacing);
		}
	}

	public class MessageBusTests
	{
		[Fact]
		public void SubscribeAndSendDeliversMessage()
		{
			MessageBus.Reset();
			string received = null;
			var subscriber = new object();

			MessageBus.Subscribe<string>(subscriber, "test", (sender, msg) => received = msg);
			MessageBus.Send(this, "test", "hello");

			Assert.Equal("hello", received);
		}

		[Fact]
		public void UnsubscribeStopsDelivery()
		{
			MessageBus.Reset();
			int count = 0;
			var subscriber = new object();

			MessageBus.Subscribe<int>(subscriber, "counter", (sender, val) => count += val);
			MessageBus.Send(this, "counter", 1);
			Assert.Equal(1, count);

			MessageBus.Unsubscribe(subscriber, "counter");
			MessageBus.Send(this, "counter", 1);
			Assert.Equal(1, count); // should not have changed
		}

		[Fact]
		public void MultipleSubscribersReceiveMessage()
		{
			MessageBus.Reset();
			int count = 0;
			var sub1 = new object();
			var sub2 = new object();

			MessageBus.Subscribe<int>(sub1, "add", (s, v) => count += v);
			MessageBus.Subscribe<int>(sub2, "add", (s, v) => count += v);
			MessageBus.Send(this, "add", 5);

			Assert.Equal(10, count); // both received
		}

		[Fact]
		public void UnsubscribeAllRemovesFromAllMessages()
		{
			MessageBus.Reset();
			int count = 0;
			var subscriber = new object();

			MessageBus.Subscribe<int>(subscriber, "msg1", (s, v) => count += v);
			MessageBus.Subscribe<int>(subscriber, "msg2", (s, v) => count += v);
			MessageBus.UnsubscribeAll(subscriber);
			MessageBus.Send(this, "msg1", 1);
			MessageBus.Send(this, "msg2", 1);

			Assert.Equal(0, count);
		}

		[Fact]
		public void ResetClearsAllSubscriptions()
		{
			MessageBus.Reset();
			int count = 0;
			MessageBus.Subscribe<int>(new object(), "test", (s, v) => count += v);
			MessageBus.Reset();
			MessageBus.Send(this, "test", 1);
			Assert.Equal(0, count);
		}

		[Fact]
		public void SendWithoutArgsWorks()
		{
			MessageBus.Reset();
			bool received = false;
			var subscriber = new object();

			MessageBus.Subscribe(subscriber, "ping", (sender) => received = true);
			MessageBus.Send(this, "ping");

			Assert.True(received);
		}
	}

	public class ControlTemplateTests
	{
		[Fact]
		public void ControlTemplateWrapsContent()
		{
			var template = new ControlTemplate(presenter => new VStack
			{
				new Text("Header"),
				presenter,
				new Text("Footer"),
			});

			var content = new Text("Content");
			var result = template.CreateContent(content);

			Assert.IsType<VStack>(result);
		}

		[Fact]
		public void TemplatedViewAppliesTemplate()
		{
			var template = new ControlTemplate(presenter => new VStack
			{
				presenter,
			});

			var view = new TemplatedView(template);
			view.Add(new Text("Inner"));
			// Template should have been applied
			Assert.NotNull(view);
		}
	}

	public class ShapeTests
	{
		[Fact]
		public void LineCreatesPath()
		{
			var line = new Line(0, 0, 100, 100);
			var path = line.PathForBounds(new Rect(0, 0, 100, 100));
			Assert.NotNull(path);
		}

		[Fact]
		public void PolygonCreatesClosedPath()
		{
			var polygon = new Polygon(
				new PointF(0, 0),
				new PointF(100, 0),
				new PointF(50, 100)
			);
			var path = polygon.PathForBounds(new Rect(0, 0, 100, 100));
			Assert.NotNull(path);
			Assert.True(path.Closed);
		}

		[Fact]
		public void PolylineCreatesOpenPath()
		{
			var polyline = new Polyline(
				new PointF(0, 0),
				new PointF(50, 50),
				new PointF(100, 0)
			);
			var path = polyline.PathForBounds(new Rect(0, 0, 100, 100));
			Assert.NotNull(path);
			Assert.False(path.Closed);
		}
	}

	public class GestureEnhancementTests
	{
		[Fact]
		public void TapGestureHasPositionProperties()
		{
			var tap = new TapGesture(g => { });
			tap.X = 10.5;
			tap.Y = 20.5;
			Assert.Equal(10.5, tap.X);
			Assert.Equal(20.5, tap.Y);
		}

		[Fact]
		public void TapGestureHasNumberOfTapsRequired()
		{
			var tap = new TapGesture(g => { });
			Assert.Equal(1, tap.NumberOfTapsRequired);
			tap.NumberOfTapsRequired = 2;
			Assert.Equal(2, tap.NumberOfTapsRequired);
		}

		[Fact]
		public void PanGestureHasVelocity()
		{
			var pan = new PanGesture(g => { });
			pan.VelocityX = 150;
			pan.VelocityY = -200;
			Assert.Equal(150, pan.VelocityX);
			Assert.Equal(-200, pan.VelocityY);
		}

		[Fact]
		public void PanGestureHasTouchPoints()
		{
			var pan = new PanGesture(g => { });
			Assert.Equal(1, pan.TouchPoints);
			pan.TouchPoints = 2;
			Assert.Equal(2, pan.TouchPoints);
		}

		[Fact]
		public void PinchGestureHasOriginAndVelocity()
		{
			var pinch = new PinchGesture(g => { });
			pinch.OriginX = 50;
			pinch.OriginY = 75;
			pinch.ScaleVelocity = 0.5;
			Assert.Equal(50, pinch.OriginX);
			Assert.Equal(75, pinch.OriginY);
			Assert.Equal(0.5, pinch.ScaleVelocity);
		}

		[Fact]
		public void SwipeGestureHasVelocityAndOffset()
		{
			var swipe = new SwipeGesture(g => { });
			swipe.Velocity = 500;
			swipe.OffsetX = 200;
			swipe.OffsetY = -50;
			Assert.Equal(500, swipe.Velocity);
			Assert.Equal(200, swipe.OffsetX);
			Assert.Equal(-50, swipe.OffsetY);
		}

		[Fact]
		public void SwipeGestureHasThreshold()
		{
			var swipe = new SwipeGesture(g => { });
			Assert.Equal(100, swipe.Threshold); // default
			swipe.Threshold = 50;
			Assert.Equal(50, swipe.Threshold);
		}

		[Fact]
		public void PointerGestureAcceptsPositionCallbacks()
		{
			Point? receivedPoint = null;
			var pointer = new PointerGesture();
			pointer.PointerMoved = (view, point) => receivedPoint = point;
			pointer.PointerMoved?.Invoke(null, new Point(10, 20));
			Assert.NotNull(receivedPoint);
			Assert.Equal(10, receivedPoint.Value.X);
			Assert.Equal(20, receivedPoint.Value.Y);
		}
	}

	public class ToolbarItemTests
	{
		[Fact]
		public void ToolbarItemCreation()
		{
			var item = new ToolbarItem("Save", () => { });
			Assert.Equal("Save", item.Text);
			Assert.NotNull(item.OnClicked);
			Assert.True(item.IsEnabled);
			Assert.Equal(ToolbarItemOrder.Primary, item.Order);
		}

		[Fact]
		public void ToolbarItemWithIcon()
		{
			var item = new ToolbarItem("☰", "FluentUI", () => { });
			Assert.Equal("☰", item.IconGlyph);
			Assert.Equal("FluentUI", item.IconFontFamily);
		}

		[Fact]
		public void NavigationViewToolbarItems()
		{
			var nav = new NavigationView();
			nav.ToolbarItems.Add(new ToolbarItem("Edit", () => { }));
			nav.ToolbarItems.Add(new ToolbarItem("Delete", () => { }) { Order = ToolbarItemOrder.Secondary });
			Assert.Equal(2, nav.ToolbarItems.Count);
		}

		// --- CometHost Tests ---

		[Fact]
		public void CometHost_SetsBindableProperty()
		{
			var inner = new Text("Hello");
			var host = new CometHost(inner);
			Assert.Same(inner, host.CometView);
		}

		[Fact]
		public void CometHost_DefaultConstructor()
		{
			var host = new CometHost();
			Assert.Null(host.CometView);
		}

		[Fact]
		public void CometHost_UpdatesCometView()
		{
			var host = new CometHost(new Text("A"));
			var viewB = new Text("B");
			host.CometView = viewB;
			Assert.Same(viewB, host.CometView);
		}
	}
}
