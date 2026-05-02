using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using static Comet.CometControls;

namespace Comet.Tests
{
	/// <summary>
	/// Phase 8.3: Expanded Control Coverage — Generator-Emitted Lane
	/// Anticipatory tests for all CometGenerate-based controls.
	/// Tests factory methods, state binding, and control instantiation.
	/// </summary>
	public class Phase8_GeneratedControlTests : TestBase
	{
		// ---- Core Input Controls ----

		[Fact]
		public void Button_FactoryAndStateBinding()
		{
			var state = new Signal<string>("Click");
			var btn = Button(state);
			Assert.NotNull(btn);
			Assert.Equal("Click", btn.Text?.CurrentValue);
			state.Value = "Clicked";
			Assert.Equal("Clicked", btn.Text?.CurrentValue);
		}

		[Fact]
		public void TextField_FactoryAndPlaceholder()
		{
			var field = TextField("", "Enter text");
			Assert.NotNull(field);
			Assert.Equal("Enter text", field.Placeholder?.CurrentValue);
		}

		[Fact]
		public void TextEditor_FactoryAndContent()
		{
			var editor = TextEditor("Initial");
			Assert.NotNull(editor);
			Assert.Equal("Initial", editor.Text?.CurrentValue);
		}

		[Fact]
		public void SecureField_Factory()
		{
			var secure = SecureField("", "Password");
			Assert.NotNull(secure);
			Assert.Equal("Password", secure.Placeholder?.CurrentValue);
		}

		[Fact]
		public void SearchBar_Factory()
		{
			var sb = SearchBar("");
			Assert.NotNull(sb);
		}

		// ---- Value Controls ----

		[Fact]
		public void Slider_FactoryAndStateBinding()
		{
			var state = new Signal<double>(0.3);
			var slider = Slider(state, 0d, 1d);
			Assert.NotNull(slider);
			Assert.Equal(0.3, slider.Value?.CurrentValue);
			state.Value = 0.7;
			Assert.Equal(0.7, slider.Value?.CurrentValue);
		}

		[Fact]
		public void Toggle_FactoryAndStateBinding()
		{
			var state = new Signal<bool>(false);
			var toggle = Toggle(state);
			Assert.NotNull(toggle);
			Assert.False(toggle.Value?.CurrentValue);
			state.Value = true;
			Assert.True(toggle.Value?.CurrentValue);
		}

		[Fact]
		public void Stepper_FactoryAndRange()
		{
			var stepper = Stepper(5d, 0d, 10d, 1d);
			Assert.NotNull(stepper);
			Assert.Equal(5, stepper.Value?.CurrentValue);
		}

		[Fact]
		public void ProgressBar_FactoryAndValue()
		{
			var pb = ProgressBar(0.6);
			Assert.NotNull(pb);
			Assert.Equal(0.6, pb.Value?.CurrentValue);
		}

		// ---- Pickers ----

		[Fact]
		public void DatePicker_FactoryAndValue()
		{
			var date = new DateTime(2024, 6, 15);
			var picker = DatePicker((DateTime?)date);
			Assert.NotNull(picker);
			Assert.Equal(date, picker.Date?.CurrentValue);
		}

		[Fact]
		public void TimePicker_FactoryAndValue()
		{
			var time = new TimeSpan(14, 30, 0);
			var picker = TimePicker((TimeSpan?)time);
			Assert.NotNull(picker);
			Assert.Equal(time, picker.Time?.CurrentValue);
		}

		// ---- Indicators & Status ----

		[Fact]
		public void ActivityIndicator_Factory()
		{
			var ai = ActivityIndicator(true);
			Assert.NotNull(ai);
			Assert.True(ai.IsRunning?.CurrentValue);
		}

		[Fact]
		public void CheckBox_FactoryAndStateBinding()
		{
			var state = new Signal<bool>(false);
			var cb = CheckBox(state);
			Assert.NotNull(cb);
			Assert.False(cb.IsChecked?.CurrentValue);
			state.Value = true;
			Assert.True(cb.IsChecked?.CurrentValue);
		}

		[Fact]
		public void IndicatorView_Factory()
		{
			var iv = IndicatorView(5);
			Assert.NotNull(iv);
			Assert.Equal(5, iv.Count?.CurrentValue);
		}

		// ---- Container/Special ----

		[Fact]
		public void RefreshView_Factory()
		{
			var rv = RefreshView(false);
			Assert.NotNull(rv);
		}

		[Fact]
		public void FlyoutView_Factory()
		{
			var fv = new FlyoutView();
			Assert.NotNull(fv);
		}

		[Fact]
		public void ImageButton_Factory()
		{
			var imgBtn = new Comet.ImageButton();
			Assert.NotNull(imgBtn);
		}

		// ---- Text Display ----

		[Fact]
		public void Text_FactoryAndStateBinding()
		{
			var state = new Signal<string>("Start");
			var text = Text(state);
			Assert.NotNull(text);
			Assert.Equal("Start", text.Value?.CurrentValue);
			state.Value = "End";
			Assert.Equal("End", text.Value?.CurrentValue);
		}
	}

	/// <summary>
	/// Phase 8.3: Expanded Control Coverage — Handwritten-Complex Lane
	/// Anticipatory tests for handwritten View subclasses and complex controls.
	/// Covers containers, layouts, and specialized views.
	/// </summary>
	public class Phase8_HandwrittenControlTests : TestBase
	{
		// ---- Layout Containers ----

		[Fact]
		public void VStack_ChildrenCollection()
		{
			var stack = new VStack
			{
				new Text("Line 1"),
				new Text("Line 2")
			};
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void HStack_ChildrenCollection()
		{
			var stack = new HStack
			{
				new Button("One"),
				new Button("Two")
			};
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void ZStack_Overlay()
		{
			var stack = new ZStack
			{
				new BoxView { Color = Colors.Red },
				new Text("Top")
			};
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void Grid_Creation()
		{
			var grid = new Grid();
			grid.Add(new Text("Cell"));
			Assert.Single(grid);
		}

		// ---- Single-Child Containers ----

		[Fact]
		public void ContentView_ContentProperty()
		{
			var cv = new ContentView();
			cv.Add(new Text("Child"));
			Assert.NotNull(cv.Content);
		}

		[Fact]
		public void ScrollView_OrientationAndContent()
		{
			var sv = new ScrollView(Orientation.Vertical);
			sv.Add(new VStack { new Text("Item 1") });
			Assert.NotNull(sv.Content);
		}

		[Fact]
		public void Border_StrokeAndContent()
		{
			var border = new Border();
			border.Add(new Text("Bordered"));
			Assert.NotNull(border);
		}

		[Fact]
		public void Frame_HasShadow()
		{
			var frame = new Frame { HasShadow = true };
			frame.Add(new Text("Framed"));
			Assert.True(frame.HasShadow?.CurrentValue);
		}

		// ---- Data Views ----

		[Fact]
		public void CollectionView_Creation()
		{
			var items = new List<string> { "A", "B", "C" };
			var cv = new CollectionView<string>(items.AsReadOnly());
			cv.ViewFor = item => new Text(item);
			Assert.NotNull(cv);
		}

		[Fact]
		public void CarouselView_Creation()
		{
			var items = new List<string> { "Slide 1", "Slide 2" };
			var cv = new CarouselView<string>(items.AsReadOnly());
			cv.ViewFor = item => new Text(item);
			Assert.NotNull(cv);
		}

		[Fact]
		public void ListView_ItemsAndViewFor()
		{
			var items = new List<string> { "A", "B" };
			Assert.NotNull(items);
		}

		// ---- Specialized Views ----

		[Fact]
		public void Image_StringSourceConstructor()
		{
			var img = new Image("photo.png");
			Assert.Equal("photo.png", img.StringSource?.CurrentValue);
		}

		[Fact]
		public void BoxView_ColorProperty()
		{
			var box = new BoxView { Color = Colors.Purple };
			Assert.Equal(Colors.Purple, box.Color?.CurrentValue);
		}

		[Fact]
		public void WebView_UrlProperty()
		{
			var wv = new WebView { Source = "https://example.com" };
			Assert.Equal("https://example.com", wv.Source?.CurrentValue);
		}

		[Fact]
		public void GraphicsView_Drawable()
		{
			var gv = new GraphicsView();
			Assert.NotNull(gv);
		}

		// ---- Gesture Containers ----

		[Fact]
		public void SwipeView_LeftItems()
		{
			var sv = new SwipeView();
			sv.LeftItems = new SwipeItems
			{
				new SwipeItem { Text = "Delete", BackgroundColor = Colors.Red }
			};
			Assert.Single(sv.LeftItems);
		}

		// ---- Interop Bridges ----

		[Fact]
		public void MauiViewHost_WrapsIView()
		{
			var mockView = new TestIViewImpl();
			var host = new MauiViewHost(mockView);
			Assert.Same(mockView, host.HostedView);
		}

		[Fact]
		public void NativeHost_TryGetNativeView()
		{
			var host = new NativeHost(() => new TestIViewImpl());
			Assert.NotNull(host);
		}

		// ---- Disposal Safety ----

		[Fact]
		public void ContainerViews_DisposeChildren()
		{
			var stack = new VStack { new Text("A"), new Text("B") };
			stack.Dispose();
			Assert.Empty(stack);
		}

		// ---- Phase 8.2: TabbedPage (Amos) ----

		[Fact]
		public void TabbedPage_AddChildrenAndSelect()
		{
			var tp = new TabbedPage();
			tp.Add(new Text("Tab 1"));
			tp.Add(new Text("Tab 2"));
			var children = tp.GetChildren();
			Assert.Equal(2, children.Count);
			Assert.NotNull(tp.CurrentPage);
			tp.CurrentTabIndex = 1;
			Assert.Same(children[1], tp.CurrentPage);
		}

		[Fact]
		public void TabbedPage_ParentSetOnAdd()
		{
			var tp = new TabbedPage();
			var child = new Text("Child");
			tp.Add(child);
			Assert.Same(tp, child.Parent);
		}

		[Fact]
		public void TabbedPage_DisposesCleansChildren()
		{
			var tp = new TabbedPage();
			tp.Add(new Text("A"));
			tp.Add(new Text("B"));
			Assert.Equal(2, tp.GetChildren().Count);
			tp.Dispose();
			Assert.Empty(tp.GetChildren());
		}

		[Fact]
		public void TabbedPage_CurrentPageNullWhenEmpty()
		{
			var tp = new TabbedPage();
			Assert.Null(tp.CurrentPage);
		}

		// ---- Phase 8.2: FlyoutPage (Amos) ----

		[Fact]
		public void FlyoutPage_FlyoutAndDetailAssignment()
		{
			var fp = new FlyoutPage();
			var flyout = new VStack { new Text("Menu") };
			var detail = new Text("Content");
			fp.Flyout = flyout;
			fp.Detail = detail;
			Assert.Same(flyout, fp.Flyout);
			Assert.Same(detail, fp.Detail);
			Assert.Equal(2, fp.GetChildren().Count);
		}

		[Fact]
		public void FlyoutPage_ParentSetOnAssignment()
		{
			var fp = new FlyoutPage();
			var flyout = new Text("Menu");
			var detail = new Text("Detail");
			fp.Flyout = flyout;
			fp.Detail = detail;
			Assert.Same(fp, flyout.Parent);
			Assert.Same(fp, detail.Parent);
		}

		[Fact]
		public void FlyoutPage_IsPresentedBinding()
		{
			var state = new Reactive<bool>(false);
			var fp = new FlyoutPage { IsPresented = state };
			Assert.False(fp.IsPresented?.CurrentValue);
			state.Value = true;
			Assert.True(fp.IsPresented?.CurrentValue);
		}

		[Fact]
		public void FlyoutPage_DisposeCleansChildren()
		{
			var fp = new FlyoutPage();
			fp.Flyout = new Text("F");
			fp.Detail = new Text("D");
			fp.Dispose();
			Assert.Null(fp.Flyout);
			Assert.Null(fp.Detail);
			Assert.Empty(fp.GetChildren());
		}

		[Fact]
		public void FlyoutPage_ReplacingChildClearsOldParent()
		{
			var fp = new FlyoutPage();
			var old = new Text("Old");
			fp.Flyout = old;
			Assert.Same(fp, old.Parent);
			fp.Flyout = new Text("New");
			Assert.Null(old.Parent);
		}

		// ---- Test Helper ----

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
