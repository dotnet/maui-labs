// Bridge between Comet's MAUI-facing View model and the Comet.Layout.Yoga flexbox engine.
// Consumed by Yoga-backed layout managers (VStack/HStack/ZStack/FlexLayout in Phases 4-5)
// to translate Comet view state into a YogaNode tree, then apply Yoga's computed layout
// back onto IView.Arrange.
//
// Key mapping choices:
//   - Comet FrameConstraints.Width/Height  -> YogaNode.Width/Height (Point) or Auto
//   - Comet Margin (Thickness)             -> YogaNode.SetMargin(Left/Top/Right/Bottom)
//   - Comet Flex env (Basis/Grow/Shrink)   -> YogaNode.FlexBasis/Grow/Shrink
//   - Comet FlexAlignSelf enum             -> FlexAlign (Yoga)
//   - LayoutAlignment cross-axis Fill      -> AlignSelf.Stretch
//   - LayoutAlignment main-axis  Fill      -> FlexGrow = 1, FlexShrink = 1 (unless caller overrode)
//
// FlexOrder has no direct Yoga equivalent; callers (tree-build time) should pre-sort
// children by GetFlexOrder() before inserting into the parent YogaNode.

using System;
using Comet.Layout.Yoga;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;

// Disambiguate: Comet.Layout.Yoga and this project both define FlexDirection; use aliases
// so bridge code references the Yoga variants explicitly.
using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Layout;

/// <summary>
/// Translates Comet view state into Yoga node style + layout, and writes results back.
/// Internal: consumed only by Comet's layout managers and tests.
/// </summary>
internal static class YogaMeasureBridge
{
	/// <summary>
	/// Create a leaf YogaNode whose intrinsic size is computed by calling
	/// <c>view.Measure(width, height)</c> via MAUI's existing measure path.
	/// Style (frame/margin/flex) is applied in the same pass.
	/// </summary>
	public static YogaNode CreateLeafNode(
		IView view,
		YogaFlexDirection parentDirection = YogaFlexDirection.Column,
		LayoutAlignment? horizontalOverride = null,
		LayoutAlignment? verticalOverride = null)
	{
		if (view is null) throw new ArgumentNullException(nameof(view));

		var node = new YogaNode();
		ApplyStyle(node, view, parentDirection, horizontalOverride, verticalOverride);
		node.MeasureFunction = (_, availableWidth, widthMode, availableHeight, heightMode) =>
		{
			var w = ResolveAvailable(availableWidth, widthMode);
			var h = ResolveAvailable(availableHeight, heightMode);
			var size = view.Measure(w, h);
			return new YogaSize((float)size.Width, (float)size.Height);
		};
		return node;
	}

	/// <summary>
	/// Apply Comet view style (frame constraints, margin, flex env, alignment) onto <paramref name="node"/>.
	/// Safe to call repeatedly; existing style values are overwritten.
	/// </summary>
	/// <param name="parentDirection">Parent flex direction — determines which axis is "main".</param>
	/// <param name="horizontalOverride">If supplied, overrides env-derived horizontal LayoutAlignment.</param>
	/// <param name="verticalOverride">If supplied, overrides env-derived vertical LayoutAlignment.</param>
	public static void ApplyStyle(
		YogaNode node,
		IView view,
		YogaFlexDirection parentDirection = YogaFlexDirection.Column,
		LayoutAlignment? horizontalOverride = null,
		LayoutAlignment? verticalOverride = null)
	{
		if (node is null) throw new ArgumentNullException(nameof(node));
		if (view is null) throw new ArgumentNullException(nameof(view));

		var cometView = view as View;

		// ── Frame constraints: Width / Height ──
		var constraints = cometView?.GetFrameConstraints();
		node.Width = (constraints?.Width is float w && w > 0) ? YogaValue.Point(w) : YogaValue.Auto;
		node.Height = (constraints?.Height is float h && h > 0) ? YogaValue.Point(h) : YogaValue.Auto;

		// Min/Max: Comet's FrameConstraints doesn't expose these yet, but fall back to IView
		// Minimum/Maximum when the view supplies them (Comet.View maps these via env keys).
		ApplyMinMax(node, view);

		// ── Margin ──
		var margin = cometView?.GetMargin() ?? (view.Margin);
		node.SetMargin(YogaEdge.Left, YogaValue.Point((float)margin.Left));
		node.SetMargin(YogaEdge.Top, YogaValue.Point((float)margin.Top));
		node.SetMargin(YogaEdge.Right, YogaValue.Point((float)margin.Right));
		node.SetMargin(YogaEdge.Bottom, YogaValue.Point((float)margin.Bottom));

		// ── Flex-specific env (basis / grow / shrink / alignSelf) ──
		double flexBasis = cometView?.GetFlexBasis() ?? -1;
		double flexGrow = cometView?.GetFlexGrow() ?? 0;
		double flexShrink = cometView?.GetFlexShrink() ?? 1;
		var flexAlignSelf = cometView?.GetFlexAlignSelf() ?? YogaFlexAlign.Auto;

		node.FlexBasis = flexBasis >= 0 ? YogaValue.Point((float)flexBasis) : YogaValue.Auto;
		node.FlexGrow = (float)flexGrow;
		node.FlexShrink = (float)flexShrink;

		// ── AspectRatio / PositionType (Yoga additions) ──
		double aspect = cometView?.GetAspectRatio() ?? -1;
		if (aspect > 0)
			node.AspectRatio = (float)aspect;

		if (cometView is not null)
			node.PositionType = cometView.GetPositionType();

		// ── AlignSelf: explicit FlexAlignSelf env wins over alignment-derived stretch ──
		var explicitAlignSelf = flexAlignSelf;

		// ── LayoutAlignment → AlignSelf / FlexGrow/Shrink based on parent axis ──
		// IMPORTANT: read alignment from env directly so an *unset* child doesn't pin
		// AlignSelf, letting the parent's AlignItems govern (matches Issue A semantics).
		var horizontal = horizontalOverride ?? GetHorizontalAlignmentOrNull(cometView);
		var vertical = verticalOverride ?? GetVerticalAlignmentOrNull(cometView);

		bool parentIsRow = parentDirection == YogaFlexDirection.Row || parentDirection == YogaFlexDirection.RowReverse;
		var crossAlignment = parentIsRow ? vertical : horizontal;
		var mainAlignment = parentIsRow ? horizontal : vertical;

		// Cross-axis: explicit FlexAlignSelf wins; otherwise an explicit cross-axis
		// LayoutAlignment maps to AlignSelf. If the child didn't set anything we leave
		// AlignSelf=Auto so the parent's AlignItems governs.
		if (flexAlignSelf != YogaFlexAlign.Auto)
		{
			node.AlignSelf = explicitAlignSelf;
		}
		else if (crossAlignment is { } cross)
		{
			node.AlignSelf = MapLayoutAlignmentToAlignSelf(cross);
		}

		// Main-axis Fill → grow/shrink to consume available space (only when the caller
		// explicitly opted into Fill via env, and hasn't supplied an explicit FlexGrow).
		if (mainAlignment == LayoutAlignment.Fill && flexGrow == 0)
		{
			node.FlexGrow = 1f;
		}
	}

	/// <summary>
	/// After <c>YogaAlgorithm.CalculateLayout</c> has been called on the root, arrange
	/// <paramref name="view"/> using the computed rect from <paramref name="node"/>.
	/// <paramref name="parentOffset"/> is the top-left of the parent container in its own
	/// coordinate space (normally <c>Point.Zero</c>; Yoga already accounts for margin).
	/// </summary>
	public static void ArrangeFromNode(IView view, YogaNode node, Point parentOffset = default)
	{
		if (view is null) throw new ArgumentNullException(nameof(view));
		if (node is null) throw new ArgumentNullException(nameof(node));

		var x = node.LayoutX + parentOffset.X;
		var y = node.LayoutY + parentOffset.Y;
		var width = node.LayoutWidth;
		var height = node.LayoutHeight;
		view.Arrange(new Rect(x, y, width, height));
	}

	// ── Helpers ──

	static YogaFlexAlign MapLayoutAlignmentToAlignSelf(LayoutAlignment a) => a switch
	{
		LayoutAlignment.Start => YogaFlexAlign.FlexStart,
		LayoutAlignment.Center => YogaFlexAlign.Center,
		LayoutAlignment.End => YogaFlexAlign.FlexEnd,
		LayoutAlignment.Fill => YogaFlexAlign.Stretch,
		_ => YogaFlexAlign.Auto,
	};

	static LayoutAlignment? GetHorizontalAlignment(IView view, View? cometView)
	{
		if (cometView is not null)
		{
			var env = cometView.GetEnvironment<LayoutAlignment?>(
				cometView, EnvironmentKeys.Layout.HorizontalLayoutAlignment, false);
			if (env is not null) return env;
		}
		// MAUI IView fallback
		return view.HorizontalLayoutAlignment;
	}

	static LayoutAlignment? GetVerticalAlignment(IView view, View? cometView)
	{
		if (cometView is not null)
		{
			var env = cometView.GetEnvironment<LayoutAlignment?>(
				cometView, EnvironmentKeys.Layout.VerticalLayoutAlignment, false);
			if (env is not null) return env;
		}
		return view.VerticalLayoutAlignment;
	}

	/// <summary>
	/// Returns the child's horizontal alignment ONLY if it was explicitly set via env
	/// (.FillHorizontal/.FitHorizontal/.HorizontalLayoutAlignment/.Alignment). Returns
	/// null when the child hasn't expressed an opinion, so the parent's AlignItems can
	/// govern the cross axis. Critical for Issue A semantics — IView.HorizontalLayoutAlignment
	/// defaults to Fill which would otherwise pin AlignSelf=Stretch on every child.
	/// </summary>
	static LayoutAlignment? GetHorizontalAlignmentOrNull(View? cometView)
	{
		if (cometView is null) return null;
		return cometView.GetEnvironment<LayoutAlignment?>(
			cometView, EnvironmentKeys.Layout.HorizontalLayoutAlignment, false);
	}

	static LayoutAlignment? GetVerticalAlignmentOrNull(View? cometView)
	{
		if (cometView is null) return null;
		return cometView.GetEnvironment<LayoutAlignment?>(
			cometView, EnvironmentKeys.Layout.VerticalLayoutAlignment, false);
	}

	static void ApplyMinMax(YogaNode node, IView view)
	{
		var minW = view.MinimumWidth;
		var minH = view.MinimumHeight;
		var maxW = view.MaximumWidth;
		var maxH = view.MaximumHeight;

		if (IsPositiveFinite(minW)) node.MinWidth = YogaValue.Point((float)minW);
		if (IsPositiveFinite(minH)) node.MinHeight = YogaValue.Point((float)minH);
		if (IsPositiveFinite(maxW)) node.MaxWidth = YogaValue.Point((float)maxW);
		if (IsPositiveFinite(maxH)) node.MaxHeight = YogaValue.Point((float)maxH);
	}

	static bool IsPositiveFinite(double v)
		=> !double.IsNaN(v) && !double.IsInfinity(v) && v > 0 && v < Dimension.Maximum;

	static double ResolveAvailable(float available, YogaMeasureMode mode)
	{
		// Undefined/AtMost with NaN → pass infinity so MAUI measures its natural size.
		if (mode == YogaMeasureMode.Undefined || float.IsNaN(available))
			return double.PositiveInfinity;
		return available;
	}
}
