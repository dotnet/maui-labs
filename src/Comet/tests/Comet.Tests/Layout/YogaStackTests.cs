// Phase 5 acceptance tests for the Yoga-backed stack layout managers.
// Exercises VStack / HStack / ZStack via their LayoutManager directly — this keeps the
// tests deterministic and handler-free (measurement comes from a fake handler's intrinsic
// size). Phase 6 will triage any broader regressions in existing LayoutTests.

using System;
using Comet.Layout;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using Xunit;

namespace Comet.Tests.Layout
{
	public class YogaStackTests : TestBase
	{
		static View Leaf(double w, double h)
		{
			var view = new View();
			InitializeHandlers(view);
			var handler = (GenericViewHandler)view.ViewHandler;
			handler.OnGetIntrinsicSize = (_, _) => new Size(w, h);
			return view;
		}

		static void AddRange(ContainerView container, params View[] views)
		{
			foreach (var v in views)
				container.Add(v);
			// Ensure every child view has a handler wired.
			InitializeHandlers(container);
		}

		// ── VStack ──────────────────────────────────────────────────────────────────

		[Fact]
		public void VStack_Three_Fixed_Children_Stacks_With_Gaps()
		{
			var stack = new VStack(spacing: 5);
			AddRange(stack, Leaf(40, 20), Leaf(40, 30), Leaf(40, 25));

			var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 5);

			var measured = mgr.Measure(200, double.PositiveInfinity);

			// 20 + 5 + 30 + 5 + 25 = 85
			Assert.Equal(85, measured.Height, 1);

			mgr.ArrangeChildren(new Rect(0, 0, 200, 85));

			Assert.Equal(0, stack[0].Frame.Y, 1);
			Assert.Equal(25, stack[1].Frame.Y, 1);   // 20 + gap 5
			Assert.Equal(60, stack[2].Frame.Y, 1);   // 25 + 30 + gap 5
		}

		[Fact]
		public void VStack_Spacer_Between_Fixed_Children_Expands()
		{
			var stack = new VStack(spacing: 0);
			var top = Leaf(40, 20);
			var bottom = Leaf(40, 20);
			AddRange(stack, top, new Spacer(), bottom);

			var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);

			mgr.Measure(200, 200);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 200));

			Assert.Equal(0, top.Frame.Y, 1);
			// Bottom leaf should be flush with the bottom of the 200pt container.
			Assert.Equal(180, bottom.Frame.Y, 1);
		}

		[Fact]
		public void VStack_Child_Center_Is_Horizontally_Centered()
		{
			var stack = new VStack();
			var centered = Leaf(40, 20).HorizontalLayoutAlignment(LayoutAlignment.Center);
			AddRange(stack, centered);

			var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);

			mgr.Measure(200, 200);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 200));

			// Child 40pt wide centered in 200pt → X = 80.
			Assert.Equal(80, centered.Frame.X, 1);
		}

		// ── HStack ──────────────────────────────────────────────────────────────────

		[Fact]
		public void HStack_Spacing_Is_Honoured_Between_Children()
		{
			var stack = new HStack(spacing: 10);
			var a = Leaf(30, 20);
			var b = Leaf(30, 20);
			var c = Leaf(30, 20);
			AddRange(stack, a, b, c);

			var mgr = new HStackLayoutManager(stack, LayoutAlignment.Fill, 10);
			mgr.Measure(double.PositiveInfinity, 50);
			mgr.ArrangeChildren(new Rect(0, 0, 110, 50));  // 30*3 + 10*2 = 110

			Assert.Equal(0, a.Frame.X, 1);
			Assert.Equal(40, b.Frame.X, 1);   // 30 + gap 10
			Assert.Equal(80, c.Frame.X, 1);   // 60 + gap 10
		}

		[Fact]
		public void HStack_Fill_Child_Consumes_Remaining_Width()
		{
			var stack = new HStack(spacing: 0);
			var a = Leaf(30, 20);
			var flex = Leaf(10, 20).FillHorizontal();
			var c = Leaf(30, 20);
			AddRange(stack, a, flex, c);

			var mgr = new HStackLayoutManager(stack, LayoutAlignment.Fill, 0);
			mgr.Measure(200, 20);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 20));

			// a:30 + flex:140 + c:30 = 200
			Assert.Equal(140, flex.Frame.Width, 1);
			Assert.Equal(170, c.Frame.X, 1);
		}

		// ── ZStack ──────────────────────────────────────────────────────────────────

		[Fact]
		public void ZStack_Size_Is_Max_Of_Children()
		{
			var stack = new ZStack();
			AddRange(stack, Leaf(40, 20), Leaf(60, 10));

			var mgr = new ZStackLayoutManager(stack);
			var size = mgr.Measure(double.PositiveInfinity, double.PositiveInfinity);

			Assert.Equal(60, size.Width, 1);
			Assert.Equal(20, size.Height, 1);
		}

		[Fact]
		public void ZStack_Two_Children_Both_Positioned_At_Zero()
		{
			var stack = new ZStack();
			var a = Leaf(40, 20);
			var b = Leaf(60, 10);
			AddRange(stack, a, b);

			var mgr = new ZStackLayoutManager(stack);
			mgr.Measure(200, 200);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 200));

			Assert.Equal(0, a.Frame.X, 1);
			Assert.Equal(0, a.Frame.Y, 1);
			Assert.Equal(0, b.Frame.X, 1);
			Assert.Equal(0, b.Frame.Y, 1);
		}

		[Fact]
		public void ZStack_Fill_Children_Stretch_To_Container()
		{
			var stack = new ZStack();
			var a = Leaf(40, 20).FillHorizontal().FillVertical();
			AddRange(stack, a);

			var mgr = new ZStackLayoutManager(stack);
			mgr.Measure(200, 150);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 150));

			Assert.Equal(200, a.Frame.Width, 1);
			Assert.Equal(150, a.Frame.Height, 1);
		}
	}
}
