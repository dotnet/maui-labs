// Phase 8 acceptance tests for IYogaLayoutInspector — verifies that
// YogaStackLayoutManager and YogaFlexLayoutManager expose the cached
// Yoga root and per-child frames after a measure/arrange pass.

using System.Linq;
using Comet.Layout;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using Xunit;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;

namespace Comet.Tests.Layout
{
	public class YogaLayoutInspectorTests : TestBase
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
			InitializeHandlers(container);
		}

		[Fact]
		public void Snapshot_Before_Measure_Returns_Null()
		{
			var stack = new VStack();
			var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);
			var inspector = (IYogaLayoutInspector)mgr;

			Assert.Null(inspector.GetLayoutSnapshot());
		}

		[Fact]
		public void VStack_Snapshot_Has_Three_Children_Stacked_Vertically()
		{
			var stack = new VStack(spacing: 0);
			AddRange(stack, Leaf(40, 20), Leaf(40, 30), Leaf(40, 25));

			var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);
			mgr.Measure(200, double.PositiveInfinity);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 75));

			var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot();
			Assert.NotNull(snapshot);
			Assert.Equal("Column", snapshot!.FlexDirection);
			Assert.Equal(3, snapshot.Children.Count);

			// Frames stacked vertically (no spacing): 0, 20, 50.
			Assert.Equal(0, snapshot.Children[0].Frame.Y, 1);
			Assert.Equal(20, snapshot.Children[1].Frame.Y, 1);
			Assert.Equal(50, snapshot.Children[2].Frame.Y, 1);
			Assert.Equal(20, snapshot.Children[0].Frame.Height, 1);
		}

		[Fact]
		public void FlexLayout_Snapshot_Propagates_FlexGrow()
		{
			var a = new View();
			var b = new View();
			InitializeHandlers(a);
			InitializeHandlers(b);
			((GenericViewHandler)a.ViewHandler).OnGetIntrinsicSize = (_, _) => new Size(20, 40);
			((GenericViewHandler)b.ViewHandler).OnGetIntrinsicSize = (_, _) => new Size(30, 40);

			var layout = new FlexLayout(direction: YogaFlexDirection.Row);
			InitializeHandlers(layout);
			layout.Add(a);
			layout.Add(b);
			layout.Padding(new Thickness(0));
			a.FlexGrow(1);

			var mgr = new YogaFlexLayoutManager(layout);
			mgr.Measure(200, 100);
			mgr.ArrangeChildren(new Rect(0, 0, 200, 100));

			var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot();
			Assert.NotNull(snapshot);
			Assert.Equal("Row", snapshot!.FlexDirection);
			Assert.Equal(2, snapshot.Children.Count);
			Assert.Equal(1f, snapshot.Children[0].FlexGrow);
			Assert.Equal(0f, snapshot.Children[1].FlexGrow);
			// 'a' consumed leftover (200 - 30 = 170), 'b' kept its 30.
			Assert.Equal(170f, snapshot.Children[0].Frame.Width, 1);
			Assert.Equal(30f, snapshot.Children[1].Frame.Width, 1);
		}
	}
}
