using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Layout;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using Xunit;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexWrap = Comet.Layout.Yoga.FlexWrap;
using YogaFlexJustify = Comet.Layout.Yoga.FlexJustify;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Tests.Layout
{
	public class FlexLayoutYogaTests : TestBase
	{
		static View MakeLeaf(Size intrinsic)
		{
			var view = new View();
			InitializeHandlers(view);
			var handler = (GenericViewHandler)view.ViewHandler;
			handler.OnGetIntrinsicSize = (_, _) => intrinsic;
			return view;
		}

		static FlexLayout MakeLayout(FlexLayout layout, params View[] children)
		{
			InitializeHandlers(layout);
			foreach (var c in children)
			{
				InitializeHandlers(c);
				layout.Add(c);
			}
			// No default padding — tests want precise coordinates.
			layout.Padding(new Thickness(0));
			return layout;
		}

		static View WithIntrinsic(View v, Size s)
		{
			var handler = (GenericViewHandler)v.ViewHandler;
			handler.OnGetIntrinsicSize = (_, _) => s;
			return v;
		}

		static Rect Arrange(FlexLayout layout, double width, double height)
		{
			var mgr = layout.LayoutManager;
			mgr.Measure(width, height);
			mgr.ArrangeChildren(new Rect(0, 0, width, height));
			return new Rect(0, 0, width, height);
		}

		[Fact]
		public void Row_Direction_Lays_Children_Left_To_Right()
		{
			var a = new View(); var b = new View(); var c = new View();
			var layout = MakeLayout(new FlexLayout(direction: YogaFlexDirection.Row), a, b, c);
			WithIntrinsic(a, new Size(20, 40));
			WithIntrinsic(b, new Size(30, 40));
			WithIntrinsic(c, new Size(40, 40));

			Arrange(layout, 200, 100);

			Assert.Equal(0, a.Frame.X);
			Assert.Equal(20, b.Frame.X);
			Assert.Equal(50, c.Frame.X);
		}

		[Fact]
		public void Column_Direction_Lays_Children_Top_To_Bottom()
		{
			var a = new View(); var b = new View(); var c = new View();
			var layout = MakeLayout(new FlexLayout(direction: YogaFlexDirection.Column), a, b, c);
			WithIntrinsic(a, new Size(40, 20));
			WithIntrinsic(b, new Size(40, 30));
			WithIntrinsic(c, new Size(40, 40));

			Arrange(layout, 100, 200);

			Assert.Equal(0, a.Frame.Y);
			Assert.Equal(20, b.Frame.Y);
			Assert.Equal(50, c.Frame.Y);
		}

		[Fact]
		public void JustifyContent_Center_Centers_On_Main_Axis()
		{
			var a = new View(); var b = new View();
			var layout = MakeLayout(
				new FlexLayout(direction: YogaFlexDirection.Row, justifyContent: YogaFlexJustify.Center),
				a, b);
			WithIntrinsic(a, new Size(20, 40));
			WithIntrinsic(b, new Size(30, 40));

			Arrange(layout, 200, 100);

			// Two children total 50 wide inside a 200-wide container → left padding = 75.
			Assert.Equal(75, a.Frame.X);
			Assert.Equal(95, b.Frame.X);
		}

		[Fact]
		public void AlignItems_Center_Centers_On_Cross_Axis()
		{
			var a = new View();
			var layout = MakeLayout(
				new FlexLayout(direction: YogaFlexDirection.Row, alignItems: YogaFlexAlign.Center),
				a);
			WithIntrinsic(a, new Size(20, 40));
			a.Frame(width: 20, height: 40);
			// Phase 6 (Issue A): bridge no longer pins AlignSelf=Stretch on a child whose
			// LayoutAlignment is unset, so parent AlignItems=Center now governs without
			// requiring an explicit a.FlexAlignSelf(Center) workaround.

			Arrange(layout, 200, 100);

			// Child 40px tall centered in 100-tall container → y = 30.
			Assert.Equal(30, a.Frame.Y);
			Assert.Equal(40, a.Frame.Height);
		}

		[Fact]
		public void FlexGrow_Fills_Remaining_Main_Axis()
		{
			var a = new View(); var b = new View();
			var layout = MakeLayout(new FlexLayout(direction: YogaFlexDirection.Row), a, b);
			a.FlexGrow(1);
			WithIntrinsic(a, new Size(20, 40));
			WithIntrinsic(b, new Size(30, 40));

			Arrange(layout, 200, 100);

			// 'b' keeps its 30px; 'a' consumes the remaining 170px.
			Assert.Equal(170, a.Frame.Width);
			Assert.Equal(170, b.Frame.X);
			Assert.Equal(30, b.Frame.Width);
		}

		[Fact]
		public void Gap_Introduces_Spacing_Between_Children()
		{
			var a = new View(); var b = new View(); var c = new View();
			var layout = MakeLayout(
				new FlexLayout(direction: YogaFlexDirection.Row, gap: 10),
				a, b, c);
			WithIntrinsic(a, new Size(20, 40));
			WithIntrinsic(b, new Size(20, 40));
			WithIntrinsic(c, new Size(20, 40));

			Arrange(layout, 200, 100);

			Assert.Equal(0, a.Frame.X);
			Assert.Equal(30, b.Frame.X);  // 20 width + 10 gap
			Assert.Equal(60, c.Frame.X);  // + 20 + 10
		}

		[Fact]
		public void Wrap_Produces_Multiple_Lines_When_Main_Axis_Overflows()
		{
			var a = new View(); var b = new View(); var c = new View();
			var layout = MakeLayout(
				new FlexLayout(direction: YogaFlexDirection.Row, wrap: YogaFlexWrap.Wrap),
				a, b, c);
			WithIntrinsic(a, new Size(60, 30));
			WithIntrinsic(b, new Size(60, 30));
			WithIntrinsic(c, new Size(60, 30));

			// Container 100 wide → only one 60-wide child fits per row, so each child wraps onto its own line.
			Arrange(layout, 100, 200);

			Assert.Equal(0, a.Frame.Y);
			Assert.True(b.Frame.Y > a.Frame.Y, $"b should be on a later row than a (a.Y={a.Frame.Y}, b.Y={b.Frame.Y})");
			Assert.True(c.Frame.Y > b.Frame.Y, $"c should be on a later row than b (b.Y={b.Frame.Y}, c.Y={c.Frame.Y})");
		}
	}
}
