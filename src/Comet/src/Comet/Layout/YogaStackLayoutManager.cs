// Shared base for Comet's stack-family layout managers (VStack / HStack / ZStack).
// Delegates measurement + arrangement to a Yoga root built on demand from the current
// child list. Subclasses pick axis, gap, and absolute/flow positioning behaviour.
//
// Design notes:
//   - AbstractLayout already caches MeasuredSize (via View.MeasurementValid). We rebuild
//     the Yoga root from scratch inside Measure() — this is cheap, and it avoids a second
//     cache layer fighting AbstractLayout's. Yoga's own dirty tracking is per-node so a
//     fresh root is always clean.
//   - AbstractLayout.CrossPlatformArrange hands us a *padded* rect; we do not re-apply
//     layout padding here (that lives in GetDesiredSize).
//   - Comet's Spacer is translated to a flex-grow=1 node directly (no MeasureFunc) so it
//     consumes leftover space the way the legacy managers did.
//   - Comet passes double.PositiveInfinity for "unconstrained"; Yoga wants float.NaN.

using System;
using System.Collections.Generic;
using Comet.Layout.Yoga;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Layout;

/// <summary>
/// Base class for VStack/HStack/ZStack layout managers. Drives a Yoga root configured
/// by the subclass's direction, default cross-axis alignment and gap.
/// </summary>
public class YogaStackLayoutManager : ILayoutManager, IYogaLayoutInspector
{
	protected readonly ContainerView Layout;
	protected readonly LayoutAlignment DefaultAlignment;
	protected readonly double Spacing;
	readonly YogaFlexDirection _direction;
	readonly bool _absolute;

	// Scratch state shared between Measure → ArrangeChildren.
	YogaNode? _root;
	readonly List<(View view, YogaNode node)> _children = new();
	double _lastWidthConstraint = double.NaN;
	double _lastHeightConstraint = double.NaN;

	public YogaStackLayoutManager(
		ContainerView layout,
		LayoutAlignment defaultAlignment,
		double spacing,
		YogaFlexDirection direction,
		bool absolute = false)
	{
		Layout = layout;
		DefaultAlignment = defaultAlignment;
		Spacing = spacing;
		_direction = direction;
		_absolute = absolute;
	}

	// ── Subclass configuration hooks ──

	/// <summary>Flex direction of the root.</summary>
	protected YogaFlexDirection Direction => _direction;

	/// <summary>True for ZStack — children overlay instead of flowing.</summary>
	protected bool AbsolutePositioning => _absolute;

	/// <summary>Root's AlignItems; controls cross-axis alignment when child has no explicit alignment.</summary>
	protected virtual YogaFlexAlign RootAlignItems =>
		DefaultAlignment switch
		{
			LayoutAlignment.Start => YogaFlexAlign.FlexStart,
			LayoutAlignment.Center => YogaFlexAlign.Center,
			LayoutAlignment.End => YogaFlexAlign.FlexEnd,
			LayoutAlignment.Fill => YogaFlexAlign.Stretch,
			_ => YogaFlexAlign.Stretch,
		};

	// ── ILayoutManager ──

	public Size Measure(double widthConstraint, double heightConstraint)
	{
		_lastWidthConstraint = widthConstraint;
		_lastHeightConstraint = heightConstraint;

		BuildRoot(widthConstraint, heightConstraint);

		if (AbsolutePositioning)
		{
			// Absolute children do not contribute to a Yoga parent's intrinsic size. Compute
			// the stack's desired size as the max across children — preserves the legacy
			// ZStackLayoutManager.Measure behaviour exactly.
			return MeasureAbsolute(widthConstraint, heightConstraint);
		}

		// Measure should return *content size*, not stretch-to-constraint size.
		// Strategy:
		//   1. Apply a temporary MaxWidth/MaxHeight on the cross axis matching the
		//      external constraint — this lets text & wrap-aware children measure
		//      against the available bound (Yoga passes AtMost to MeasureFunc).
		//   2. Temporarily flip RootAlignItems=Stretch → FlexStart, so children that
		//      would otherwise stretch to that bound do NOT inflate the root's
		//      intrinsic content size.
		//   3. CalculateLayout with undefined available size — this sets the cross
		//      axis sizing mode to FitContent.
		// At Arrange time, ArrangeChildren passes exact bounds and AlignItems=Stretch
		// is preserved, so explicit Fill/Stretch children still fill the cross axis.
		bool isRow = Direction == YogaFlexDirection.Row || Direction == YogaFlexDirection.RowReverse;
		var root = _root!;

		var savedMaxWidth = root.MaxWidth;
		var savedMaxHeight = root.MaxHeight;
		var savedAlignItems = root.AlignItems;
		try
		{
			if (isRow)
			{
				if (IsFiniteConstraint(heightConstraint))
					root.MaxHeight = YogaValue.Point((float)heightConstraint);
			}
			else
			{
				if (IsFiniteConstraint(widthConstraint))
					root.MaxWidth = YogaValue.Point((float)widthConstraint);
			}

			// Suppress measure-time cross-axis stretch (it's an arrange-time concern).
			if (savedAlignItems == YogaFlexAlign.Stretch)
				root.AlignItems = YogaFlexAlign.FlexStart;

			root.CalculateLayout(float.NaN, float.NaN);
			return new Size(root.LayoutWidth, root.LayoutHeight);
		}
		finally
		{
			root.MaxWidth = savedMaxWidth;
			root.MaxHeight = savedMaxHeight;
			root.AlignItems = savedAlignItems;
		}
	}

	static bool IsFiniteConstraint(double value)
		=> !(double.IsNaN(value) || double.IsInfinity(value));

	public Size ArrangeChildren(Rect bounds)
	{
		// Defensive: if Measure() wasn't called (or was called with different constraints),
		// rebuild now. AbstractLayout.CrossPlatformArrange usually re-Measures for us.
		if (_root is null || _children.Count != CountChildren())
			BuildRoot(bounds.Width, bounds.Height);

		if (AbsolutePositioning)
		{
			ArrangeAbsolute(bounds);
			return bounds.Size;
		}

		_root!.CalculateLayout(ToYoga(bounds.Width), ToYoga(bounds.Height));

		foreach (var (view, node) in _children)
			YogaMeasureBridge.ArrangeFromNode(view, node, bounds.Location);

		return new Size(_root.LayoutWidth, _root.LayoutHeight);
	}

	// ── Tree build ──

	void BuildRoot(double widthConstraint, double heightConstraint)
	{
		_root = new YogaNode
		{
			FlexDirection = Direction,
			AlignItems = RootAlignItems,
		};
		if (Spacing > 0 && !AbsolutePositioning)
			_root.SetGap(YogaGutter.All, (float)Spacing);

		_children.Clear();

		foreach (var view in Layout)
		{
			YogaNode node;
			if (view is Spacer spacer)
			{
				node = CreateSpacerNode(spacer);
			}
			else
			{
				// Pass null overrides — the bridge reads env directly so unset
				// children inherit the root's AlignItems on the cross axis (matches
				// legacy Comet stack behaviour: VStack with no per-child alignment
				// stretches its children horizontally because the root is Stretch).
				node = YogaMeasureBridge.CreateLeafNode(view, Direction);
				if (AbsolutePositioning)
					ApplyAbsoluteInsets(node, view);
			}

			_root.InsertChild(node, _root.ChildCount);
			_children.Add((view, node));
		}
	}

	int CountChildren()
	{
		int n = 0;
		foreach (var _ in Layout) n++;
		return n;
	}

	// ── Spacer handling ──

	YogaNode CreateSpacerNode(Spacer spacer)
	{
		var node = new YogaNode();
		var constraints = spacer.GetFrameConstraints();

		bool isRow = Direction == YogaFlexDirection.Row || Direction == YogaFlexDirection.RowReverse;

		if (isRow)
		{
			if (constraints?.Width is float w && w > 0)
			{
				node.Width = YogaValue.Point(w);
			}
			else
			{
				node.FlexGrow = 1f;
				node.FlexShrink = 1f;
				node.MinWidth = YogaValue.Point(0);
			}
		}
		else
		{
			if (constraints?.Height is float h && h > 0)
			{
				node.Height = YogaValue.Point(h);
			}
			else
			{
				node.FlexGrow = 1f;
				node.FlexShrink = 1f;
				node.MinHeight = YogaValue.Point(0);
			}
		}

		// Mark the Spacer as measured so downstream SetFrameFromPlatformView paths that
		// consult MeasuredSize don't attempt to re-measure a handler-less view. Matches
		// the legacy VStack/HStack manager contract (-1,-1 sentinel).
		if (!spacer.MeasurementValid)
		{
			spacer.MeasuredSize = new Size(-1, -1);
			spacer.MeasurementValid = true;
		}

		return node;
	}

	// ── Absolute (ZStack) path ──

	void ApplyAbsoluteInsets(YogaNode node, View view)
	{
		node.PositionType = FlexPositionType.Absolute;
		node.SetPosition(YogaEdge.Left, YogaValue.Point(0));
		node.SetPosition(YogaEdge.Top, YogaValue.Point(0));
		node.SetPosition(YogaEdge.Right, YogaValue.Point(0));
		node.SetPosition(YogaEdge.Bottom, YogaValue.Point(0));
	}

	Size MeasureAbsolute(double widthConstraint, double heightConstraint)
	{
		Size measured = default;
		foreach (var (view, _) in _children)
		{
			var s = view.Measure(widthConstraint, heightConstraint);
			measured.Width = Math.Max(measured.Width, s.Width);
			measured.Height = Math.Max(measured.Height, s.Height);
		}
		return measured;
	}

	void ArrangeAbsolute(Rect bounds)
	{
		// ZStack semantics: every child gets the full container rect, honouring per-child
		// LayoutAlignment (Fill/Start/Center/End) via SetFrameFromPlatformView. This matches
		// the legacy ZStackLayoutManager (each child laid out at `bounds` with LayoutSubviews
		// or Arrange) while adding alignment support.
		foreach (var (view, _) in _children)
		{
			if (view is Spacer)
				continue;
			view.SetFrameFromPlatformView(bounds, LayoutAlignment.Fill, LayoutAlignment.Fill);
		}
	}

	// ── Inspector (IYogaLayoutInspector) ──

	public YogaLayoutSnapshot? GetLayoutSnapshot()
	{
		var root = _root;
		if (root is null)
			return null;

		var children = new List<YogaLayoutChildSnapshot>(_children.Count);
		foreach (var (view, node) in _children)
		{
			children.Add(new YogaLayoutChildSnapshot
			{
				ViewTypeName = view.GetType().Name,
				AutomationId = string.IsNullOrEmpty(view.AutomationId) ? null : view.AutomationId,
				Frame = (node.LayoutX, node.LayoutY, node.LayoutWidth, node.LayoutHeight),
				FlexGrow = node.FlexGrow,
				FlexShrink = node.FlexShrink,
				AlignSelf = node.AlignSelf.ToString(),
				PositionType = node.PositionType.ToString(),
			});
		}

		return new YogaLayoutSnapshot
		{
			Frame = (root.LayoutX, root.LayoutY, root.LayoutWidth, root.LayoutHeight),
			FlexDirection = root.FlexDirection.ToString(),
			AlignItems = root.AlignItems.ToString(),
			JustifyContent = root.JustifyContent.ToString(),
			AlignContent = root.AlignContent.ToString(),
			FlexWrap = root.FlexWrap.ToString(),
			Children = children,
		};
	}

	// ── Helpers ──

	static float ToYoga(double constraint)
		=> (double.IsInfinity(constraint) || double.IsNaN(constraint))
			? float.NaN
			: (float)constraint;
}
