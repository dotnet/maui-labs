// Reproductions for Phase 11 visual layout bugs found in Comet.Sample / ViewLayoutTestCase.
// These tests assert behavior that was broken in the rendered iOS sim screenshots.

using Comet.Layout;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using Xunit;

namespace Comet.Tests.Layout;

public class P11SampleLayoutBugTests : TestBase
{
	static View Leaf(double intrinsicW = 10, double intrinsicH = 10)
	{
		var view = new View();
		InitializeHandlers(view);
		var handler = (GenericViewHandler)view.ViewHandler;
		handler.OnGetIntrinsicSize = (_, _) => new Size(intrinsicW, intrinsicH);
		return view;
	}

	// ── Issue D: VStack with .Frame(75,75) children should produce 75-wide squares,
	// not full-width bars stretched by the parent's AlignItems.Stretch.
	[Fact]
	public void VStack_Children_With_Frame_Width_Are_Not_Stretched_By_Parent_AlignItems()
	{
		var stack = new VStack(spacing: 0);
		var child = Leaf(20, 20).Frame(75, 75); // visible 75x75 square requested
		AddRange(stack, child);

		var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);
		// VStack frame is 400 wide; if AlignItems=Stretch wins over Frame.Width(75),
		// the child layout-width will be 400 (BUG). Expected: 75.
		mgr.Measure(400, 400);
		mgr.ArrangeChildren(new Rect(0, 0, 400, 400));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		Assert.Equal(75, snapshot.Children[0].Frame.Width, 1);
		Assert.Equal(75, snapshot.Children[0].Frame.Height, 1);
	}

	// ── Issue E (control case): VStack with three 75-tall children, no Spacers,
	// JustifyContent default = FlexStart → children packed at top of 400-tall frame,
	// not spread across with SpaceAround-like gaps.
	[Fact]
	public void VStack_Without_Spacers_Packs_Children_At_Top_When_FrameHeight_Exceeds_Content()
	{
		var stack = new VStack(spacing: 0);
		AddRange(stack,
			Leaf(20, 20).Frame(75, 75),
			Leaf(20, 20).Frame(75, 75),
			Leaf(20, 20).Frame(75, 75));

		var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);
		mgr.Measure(400, 400);
		mgr.ArrangeChildren(new Rect(0, 0, 400, 400));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		Assert.Equal(0, snapshot.Children[0].Frame.Y, 1);
		Assert.Equal(75, snapshot.Children[1].Frame.Y, 1);
		Assert.Equal(150, snapshot.Children[2].Frame.Y, 1);
	}

	// ── Issue C (control case): HStack with three 75-wide children, default spacing=4.
	// Children packed at left, total leftover space sits to the right of the last child.
	[Fact]
	public void HStack_Children_Use_Default_Spacing_Not_SpaceAround()
	{
		var stack = new HStack();
		AddRange(stack,
			Leaf(20, 20).Frame(75, 75),
			Leaf(20, 20).Frame(75, 75),
			Leaf(20, 20).Frame(75, 75));

		var mgr = new HStackLayoutManager(stack, LayoutAlignment.Fill, 4f);
		mgr.Measure(400, 400);
		mgr.ArrangeChildren(new Rect(0, 0, 400, 400));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		// Expect children at x=0, 79, 158 (75 + 4 gap each)
		Assert.Equal(0, snapshot.Children[0].Frame.X, 1);
		Assert.Equal(79, snapshot.Children[1].Frame.X, 1);
		Assert.Equal(158, snapshot.Children[2].Frame.X, 1);
		Assert.Equal(75, snapshot.Children[0].Frame.Width, 1);
	}

	static void AddRange(ContainerView container, params View[] views)
	{
		foreach (var v in views)
			container.Add(v);
		InitializeHandlers(container);
	}

	// ── Issue F: $100 badge — wrapper VStack with FitHorizontal then Alignment(Trailing)
	// should be content-sized (not stretched) and aligned to the right of its parent.
	// Source pattern in ViewLayoutTestCase.cs:30-41.
	[Fact]
	public void Wrapper_VStack_With_AlignmentTrailing_Is_Content_Sized_At_Right()
	{
		// Inner Text leaf with intrinsic width 30.
		var inner = Leaf(30, 14);

		// Wrapper VStack: corresponds to .FitHorizontal().Alignment(Alignment.Trailing)
		var wrapper = new VStack(spacing: 0);
		wrapper.Add(inner);
		// Apply env keys exactly as the extensions do
		wrapper.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, LayoutAlignment.End, false);
		wrapper.SetEnvironment(EnvironmentKeys.Layout.VerticalLayoutAlignment, LayoutAlignment.Center, false);
		InitializeHandlers(wrapper);

		// Outer VStack(LayoutAlignment.Start, ...) holds the wrapper as one of several children.
		var outer = new VStack(spacing: 0);
		outer.Add(wrapper);
		InitializeHandlers(outer);

		var mgr = new VStackLayoutManager(outer, LayoutAlignment.Start, 0);

		// Trace the wrapper's own measure paths to bisect the failure.
		// 1. Direct LayoutManager.Measure on the wrapper.
		var wrapperMgr = (VStackLayoutManager)wrapper.LayoutManager!;
		var directMeasure = wrapperMgr.Measure(200, double.PositiveInfinity);
		Assert.True(directMeasure.Width < 100,
			$"DIRECT wrapper LayoutManager.Measure returned width={directMeasure.Width}, expected < 100");

		// 2. wrapper.Measure (full IView path through AbstractLayout.GetDesiredSize).
		var iviewMeasure = ((Microsoft.Maui.IView)wrapper).Measure(200, double.PositiveInfinity);
		Assert.True(iviewMeasure.Width < 100,
			$"IView wrapper.Measure returned width={iviewMeasure.Width}, expected < 100 (likely AbstractLayout.GetDesiredSize override forces Fill)");

		mgr.Measure(200, 250); // card content area
		mgr.ArrangeChildren(new Rect(0, 0, 200, 250));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		Assert.Equal("FlexStart", snapshot.AlignItems);
		Assert.Single(snapshot.Children);
		var w = snapshot.Children[0];
		Assert.Equal("FlexEnd", w.AlignSelf);
		// Wrapper width should be content-driven (~30), not stretched to 200
		Assert.True(w.Frame.Width < 100, $"wrapper width {w.Frame.Width} expected < 100 (should be content-sized)");
		// Wrapper X should place it at right edge: parent width - wrapper width
		Assert.True(w.Frame.X > 100, $"wrapper X {w.Frame.X} expected > 100 (right side)");
	}

	// ── Regression: explicit FillHorizontal child in HStack still fills the row.
	// Critical that the AlignItems=Stretch→FlexStart measure-time flip in YogaStackLayoutManager
	// does NOT affect arrange-time. ArrangeChildren passes exact bounds and AlignItems
	// is preserved → AlignSelf=Stretch (from FillHorizontal env) wins.
	[Fact]
	public void HStack_With_FillHorizontal_Child_Still_Fills_Row_At_Arrange()
	{
		var stack = new HStack(spacing: 0);
		var fillingChild = Leaf(20, 20);
		fillingChild.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, LayoutAlignment.Fill, false);
		AddRange(stack, fillingChild);

		var mgr = new HStackLayoutManager(stack, LayoutAlignment.Fill, 0);
		mgr.Measure(400, 100);
		mgr.ArrangeChildren(new Rect(0, 0, 400, 100));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		// FillHorizontal child = AlignSelf=Stretch + FlexGrow=1 (main-axis fill).
		// Should consume the full 400-wide row.
		Assert.Equal(400, snapshot.Children[0].Frame.Width, 1);
	}

	// ── Regression: default unset child in VStack with AlignItems=Stretch still
	// stretches at arrange time. The measure-time AlignItems flip must not bleed.
	[Fact]
	public void VStack_Default_Child_Stretches_At_Arrange_When_Parent_AlignItems_Stretch()
	{
		var stack = new VStack(spacing: 0); // default Fill → AlignItems=Stretch
		var defaultChild = Leaf(20, 20); // no explicit alignment
		AddRange(stack, defaultChild);

		var mgr = new VStackLayoutManager(stack, LayoutAlignment.Fill, 0);
		mgr.Measure(400, 400);
		mgr.ArrangeChildren(new Rect(0, 0, 400, 400));

		var snapshot = ((IYogaLayoutInspector)mgr).GetLayoutSnapshot()!;
		// At arrange, AlignItems=Stretch + child AlignSelf=Auto → child stretches to 400.
		Assert.Equal(400, snapshot.Children[0].Frame.Width, 1);
	}

	// ── Regression: wrapper VStack with default content-sized child (no Frame, no
	// FitHorizontal) should report content size from Measure — symmetric counterpart
	// to the badge case but verifies wrapping by passing a small constraint.
	[Fact]
	public void Wrapper_VStack_Without_Frame_Reports_Content_Size_From_Measure()
	{
		var inner = Leaf(50, 20);
		var wrapper = new VStack(spacing: 0);
		wrapper.Add(inner);
		InitializeHandlers(wrapper);

		var wrapperMgr = (VStackLayoutManager)wrapper.LayoutManager!;
		// Even with a generous 200pt cross-axis constraint, content-only measure should
		// return ~50pt wide, not 200.
		var measured = wrapperMgr.Measure(200, double.PositiveInfinity);
		Assert.Equal(50, measured.Width, 1);
		Assert.Equal(20, measured.Height, 1);
	}

}
