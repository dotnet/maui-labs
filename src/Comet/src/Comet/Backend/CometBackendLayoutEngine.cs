#nullable enable
using System;
using Comet.Layout;
using Comet.Layout.Yoga;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Backend
{
	/// <summary>
	/// Computes layout for a materialized Comet view tree with the C# Yoga flexbox engine and
	/// pushes the result onto the retained backend nodes through <see cref="ICometBackendNode.Measure"/>
	/// / <see cref="ICometBackendNode.Arrange"/>. This makes Yoga the single cross-platform
	/// layout authority — the Compose/SwiftUI backends become positioned hosts rather than each
	/// running its own native flexbox — so a given Comet tree lays out identically everywhere.
	/// </summary>
	/// <remarks>
	/// <para>Each leaf's intrinsic size is obtained through the node's <c>Measure</c> contract
	/// (e.g. a SwiftUI <c>Text</c> or Compose text measures itself). Retained leaves may cache
	/// constraint-matched answers until a property or structural mutation invalidates them; the
	/// stack/flex math runs in C#. The computed frame is parent-relative and pushed via
	/// <c>Arrange</c>. Own-content nodes (lists, navigation) are treated as leaves here — they
	/// virtualize/host their own children natively.</para>
	/// <para>Layout style (explicit size, margin, flex-grow/shrink/basis, alignment) is read from
	/// the owning view via the shared <see cref="YogaMeasureBridge"/>, the same translation the
	/// MAUI-facing layout managers use, so behaviour stays consistent across the two paths.</para>
	/// </remarks>
	public static class CometBackendLayoutEngine
	{
		/// <summary>Lays out <paramref name="root"/> within <paramref name="available"/> and
		/// pushes the computed frames onto the backend node tree.</summary>
		public static void Layout(View root, Size available)
		{
			if (root is null) throw new ArgumentNullException(nameof(root));

			var yoga = Build(root, YogaFlexDirection.Column);

			// The root fills the available space (the screen), so a root container's background
			// covers the whole surface and its children lay out within — rather than Yoga
			// shrinking the root to its content height.
			yoga.Width = Comet.Layout.Yoga.YogaValue.Point((float)available.Width);
			yoga.Height = Comet.Layout.Yoga.YogaValue.Point((float)available.Height);

			yoga.CalculateLayout((float)available.Width, (float)available.Height);
			Arrange(root, yoga);
		}

		internal static Size MeasureNode(
			ICometBackendNode node,
			double widthConstraint,
			double heightConstraint)
			=> node is IBackendMeasureCache cache
				? cache.MeasureCached(widthConstraint, heightConstraint)
				: node.Measure(widthConstraint, heightConstraint);

		/// <summary>Lays out <paramref name="content"/> at a fixed <paramref name="width"/> with
		/// its height wrapping the content, pushes the computed frames onto the node tree, and
		/// returns the content's natural size. This is the model for a scroll viewport or a
		/// virtualized list row: the cross axis is pinned to the host's width while the main axis
		/// grows to whatever the content needs (the part that then scrolls / sets the row height).</summary>
		public static Size LayoutContent(View content, double width)
		{
			if (content is null) throw new ArgumentNullException(nameof(content));

			var yoga = Build(content, YogaFlexDirection.Column);

			// Pin the width; leave height Auto so Yoga wraps to the content's natural height
			// rather than filling a (non-existent) viewport.
			yoga.Width = Comet.Layout.Yoga.YogaValue.Point((float)width);

			yoga.CalculateLayout((float)width, float.NaN);
			Arrange(content, yoga);
			return new Size(yoga.LayoutWidth, yoga.LayoutHeight);
		}

		/// <summary>Measures <paramref name="content"/>'s natural (unconstrained) size — both axes
		/// wrap to the content. This is the intrinsic-size source for a composite NATIVE control that
		/// sizes itself (a FAB, a chip): the engine measures the real Comet sub-tree (so text width
		/// comes from the actual font/measure path, not an estimate), and the node adds the control's
		/// documented insets. It does NOT arrange the content (no frames pushed) — the native control
		/// lays its own content out; this only reports the size up to the parent's Yoga layout.</summary>
		public static Size Measure(View content)
		{
			if (content is null) throw new ArgumentNullException(nameof(content));

			var yoga = Build(content, YogaFlexDirection.Column);
			yoga.CalculateLayout(float.NaN, float.NaN);   // NaN/NaN ⇒ both axes size to content
			return new Size(yoga.LayoutWidth, yoga.LayoutHeight);
		}

		/// <summary>Measures <paramref name="content"/> at a fixed width while allowing its
		/// height to wrap naturally, without arranging the tree.</summary>
		internal static Size MeasureContent(View content, double width)
		{
			if (content is null) throw new ArgumentNullException(nameof(content));

			var yoga = Build(content, YogaFlexDirection.Column);
			yoga.Width = Comet.Layout.Yoga.YogaValue.Point((float)width);
			yoga.CalculateLayout((float)width, float.NaN);
			return new Size(yoga.LayoutWidth, yoga.LayoutHeight);
		}

		static YogaNode Build(View view, YogaFlexDirection parentDirection)
		{
			view = ResolveBackendView(view);
			var node = new YogaNode();
			YogaMeasureBridge.ApplyStyle(node, view, parentDirection);

			// Comet Padding → Yoga padding for containers AND leaves: a container's padding
			// insets its children (their arranged frames include it); a leaf's padding grows
			// its measured box (Yoga adds it around the measure-func result) and the leaf
			// node insets its own content at render (ComposeNode.PadsOwnContent).
			var pad = view.GetPadding();
			if (pad.Left != 0) node.SetPadding(YogaEdge.Left, YogaValue.Point((float)pad.Left));
			if (pad.Top != 0) node.SetPadding(YogaEdge.Top, YogaValue.Point((float)pad.Top));
			if (pad.Right != 0) node.SetPadding(YogaEdge.Right, YogaValue.Point((float)pad.Right));
			if (pad.Bottom != 0) node.SetPadding(YogaEdge.Bottom, YogaValue.Point((float)pad.Bottom));

			if (view is Grid grid)
			{
				((GridLayoutManager)grid.LayoutManager).Invalidate();
				node.MeasureFunction = (_, availableWidth, widthMode, availableHeight, heightMode) =>
				{
					var width = Resolve(availableWidth, widthMode);
					var height = Resolve(availableHeight, heightMode);
					var measured = grid.LayoutManager.Measure(width, height);
					return new YogaSize((float)measured.Width, (float)measured.Height);
				};
			}
			else if (IsLayoutContainer(view))
			{
				bool isDepth = view is ZStack;
				var direction = view is HStack ? YogaFlexDirection.Row : YogaFlexDirection.Column;
				node.FlexDirection = direction;

				if (isDepth)
				{
					// ZStack → Compose Box: children overlap (z-order = child order, so a later
					// child paints over an earlier one). Each child is absolutely positioned within
					// this container, so its background/clickable cover its own arranged frame and
					// the layers don't push each other in flow. The container is Relative so it forms
					// the containing block for those absolute children (otherwise they'd escape to
					// the root). Insetless children are centered by the container's justify/align;
					// a child's Comet alignment becomes Yoga insets (see ApplyZStackOverlay).
					node.PositionType = Comet.Layout.Yoga.FlexPositionType.Relative;
					node.JustifyContent = Comet.Layout.Yoga.FlexJustify.Center;
					node.AlignItems = YogaFlexAlign.Center;
				}
				else
				{
					// Comet's stack default alignment is Fill → flexbox stretch: children fill the
					// cross axis, so a Text gets a definite width and wraps (grows in height) rather
					// than measuring at single-line intrinsic width.
					node.AlignItems = YogaFlexAlign.Stretch;

					if (view is IStackLayout stack && stack.Spacing > 0)
						node.SetGap(YogaGutter.All, (float)stack.Spacing);
				}

				var children = ((IContainerView)view).GetChildren();
				var nonNullChildCount = 0;
				for (var i = 0; i < children.Count; i++)
					if (children[i] is not null)
						nonNullChildCount++;
				var materializedChildCount = 0;
				for (int i = 0; i < children.Count; i++)
				{
					var child = children[i];
					if (child is null)
						continue;
					var childNode = Build(child, direction);
					// Content hosts give their single presented child the full content slot.
					// Without this, a virtualized child such as CollectionView keeps its
					// intrinsic zero height inside a fixed-height RefreshView.
					if (view is IContentView && nonNullChildCount == 1)
					{
						childNode.FlexGrow = 1f;
						childNode.FlexShrink = 1f;
					}
					if (isDepth)
						ApplyZStackOverlay(childNode, child, (ContainerView)view);
					node.InsertChild(childNode, materializedChildCount++);
				}
			}
			else
			{
				// Leaf: its intrinsic size comes from the native control via the backend node.
				var leaf = view;

				// baselineHeight: pin the first baseline at a fixed offset from the top. The first
				// baseline is independent of wrap width (it's the first line), so compute the top pad
				// once, push it to the node (which insets its content), and grow the measured box to
				// match — exactly Jetchat's baselineHeight layout (pad = height − firstBaseline).
				float baselinePad = 0f;
				if (leaf.GetBaselineHeight() is double targetBaseline &&
					leaf.Node?.MeasureBaseline(double.PositiveInfinity, double.PositiveInfinity) is double firstBaseline)
				{
					baselinePad = (float)Math.Max(0, targetBaseline - firstBaseline);
					leaf.Node?.SetContentTopInset(baselinePad);
				}

				node.MeasureFunction = (_, availableWidth, widthMode, availableHeight, heightMode) =>
				{
					var w = Resolve(availableWidth, widthMode);
					var h = Resolve(availableHeight, heightMode);
					var size = leaf.Node is { } backendNode
						? MeasureNode(backendNode, w, h)
						: Size.Zero;
					return new YogaSize((float)size.Width, (float)size.Height + baselinePad);
				};

				// Baseline-aligned text: report the node's first-baseline offset so Yoga can line up
				// the row on a shared baseline. When the node has no text baseline, fall back to the
				// node height (Yoga's default), which degrades to bottom alignment.
				if (leaf.GetBaselineAlign())
					node.BaselineFunction = (_, w, h) =>
						(float)(leaf.Node?.MeasureBaseline(w, h) ?? h);
			}

			return node;
		}

		// Positions a ZStack child as a Compose Box layer: absolute, with its Comet alignment
		// translated to Yoga insets so it overlaps the siblings rather than stacking in flow —
		// Fill stretches edge-to-edge, Start/End pins to one edge, Center/unset defers to the
		// container's centered justify/align. Insets win over justify/align in Yoga, so an
		// edge-pinned overlay and a centered base layer coexist (e.g. a full-bleed background
		// with a bottom-right FAB floating over it).
		static void ApplyZStackOverlay(YogaNode child, View childView, ContainerView zstack)
		{
			child.PositionType = Comet.Layout.Yoga.FlexPositionType.Absolute;
			ApplyOverlayInset(child, childView.GetHorizontalLayoutAlignment(zstack, LayoutAlignment.Center),
				YogaEdge.Left, YogaEdge.Right);
			ApplyOverlayInset(child, childView.GetVerticalLayoutAlignment(zstack, LayoutAlignment.Center),
				YogaEdge.Top, YogaEdge.Bottom);
		}

		static void ApplyOverlayInset(YogaNode child, LayoutAlignment alignment, YogaEdge start, YogaEdge end)
		{
			switch (alignment)
			{
				case LayoutAlignment.Fill:   // both edges pinned → stretch to fill the container
					child.SetPosition(start, YogaValue.Point(0));
					child.SetPosition(end, YogaValue.Point(0));
					break;
				case LayoutAlignment.Start:
					child.SetPosition(start, YogaValue.Point(0));
					break;
				case LayoutAlignment.End:
					child.SetPosition(end, YogaValue.Point(0));
					break;
				// Center (and unset, which defaults to Center here): no inset — the container's
				// Center justify/align positions the child.
			}
		}

		static void Arrange(View view, YogaNode node)
		{
			view = ResolveBackendView(view);
			var frame = new Rect(node.LayoutX, node.LayoutY, node.LayoutWidth, node.LayoutHeight);
			view.Frame = frame;
			view.Node?.Arrange(frame);

			if (view is Grid grid)
			{
				ArrangeGrid(grid, node.LayoutWidth, node.LayoutHeight);
			}
			else if (IsLayoutContainer(view))
			{
				var children = ((IContainerView)view).GetChildren();
				var nodeIndex = 0;
				for (int i = 0; i < children.Count && nodeIndex < node.ChildCount; i++)
				{
					if (children[i] is not { } child)
						continue;
					Arrange(child, node.GetChild(nodeIndex++));
				}
			}
		}

		static void ArrangeGrid(Grid grid, double width, double height)
		{
			var padding = grid.GetPadding();
			var contentWidth = Math.Max(0, width - padding.HorizontalThickness);
			var contentHeight = Math.Max(0, height - padding.VerticalThickness);
			grid.LayoutManager.Measure(contentWidth, contentHeight);
			grid.LayoutManager.ArrangeChildren(new Rect(
				padding.Left, padding.Top, contentWidth, contentHeight));

			var children = ((IContainerView)grid).GetChildren();
			for (int i = 0; i < children.Count; i++)
			{
				var child = children[i];
				if (child is null)
					continue;
				var frame = child.Frame;
				var backendChild = ResolveBackendView(child);
				var yoga = Build(backendChild, YogaFlexDirection.Column);
				yoga.SetMargin(YogaEdge.Left, YogaValue.Point(0));
				yoga.SetMargin(YogaEdge.Top, YogaValue.Point(0));
				yoga.SetMargin(YogaEdge.Right, YogaValue.Point(0));
				yoga.SetMargin(YogaEdge.Bottom, YogaValue.Point(0));
				yoga.Width = YogaValue.Point((float)frame.Width);
				yoga.Height = YogaValue.Point((float)frame.Height);
				yoga.CalculateLayout((float)frame.Width, (float)frame.Height);
				ArrangeGridChild(backendChild, yoga, frame);
			}
		}

		static void ArrangeGridChild(View view, YogaNode node, Rect frame)
		{
			view.Frame = frame;
			view.Node?.Arrange(frame);

			if (view is Grid nested)
			{
				ArrangeGrid(nested, frame.Width, frame.Height);
				return;
			}

			if (!IsLayoutContainer(view))
				return;

			var children = ((IContainerView)view).GetChildren();
			var nodeIndex = 0;
			for (int i = 0; i < children.Count && nodeIndex < node.ChildCount; i++)
			{
				if (children[i] is not { } child)
					continue;
				Arrange(child, node.GetChild(nodeIndex++));
			}
		}

		// A flow container whose children Yoga positions. Own-content nodes (list/nav) host
		// their own children and so are leaves to the layout pass.
		static bool IsLayoutContainer(View view)
			=> view is IContainerView && view.Node is not IBackendManagesOwnContent;

		static View ResolveBackendView(View view)
			=> view.Node is not null ? view : view.BuiltView ?? view;

		static double Resolve(float available, YogaMeasureMode mode)
			=> (mode == YogaMeasureMode.Undefined || float.IsNaN(available))
				? double.PositiveInfinity
				: available;
	}
}
