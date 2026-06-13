#nullable enable
using System;
using Comet.Layout;
using Comet.Layout.Yoga;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
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
	/// <para>Each leaf's intrinsic size is obtained by crossing into native exactly once via the
	/// node's <c>Measure</c> (e.g. a SwiftUI <c>Text</c> or Compose text measures itself); the
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

		static YogaNode Build(View view, YogaFlexDirection parentDirection)
		{
			var node = new YogaNode();
			YogaMeasureBridge.ApplyStyle(node, view, parentDirection);

			if (IsLayoutContainer(view))
			{
				var direction = view is HStack ? YogaFlexDirection.Row : YogaFlexDirection.Column;
				node.FlexDirection = direction;

				// Comet's stack default alignment is Fill → flexbox stretch: children fill the
				// cross axis, so a Text gets a definite width and wraps (grows in height) rather
				// than measuring at single-line intrinsic width.
				node.AlignItems = YogaFlexAlign.Stretch;

				if (view is IStackLayout stack && stack.Spacing > 0)
					node.SetGap(YogaGutter.All, (float)stack.Spacing);

				// Comet Padding insets children (Yoga container padding); the native .padding()
				// is not applied in absolute mode, so the engine owns it.
				var pad = view.GetPadding();
				if (pad.Left != 0) node.SetPadding(YogaEdge.Left, YogaValue.Point((float)pad.Left));
				if (pad.Top != 0) node.SetPadding(YogaEdge.Top, YogaValue.Point((float)pad.Top));
				if (pad.Right != 0) node.SetPadding(YogaEdge.Right, YogaValue.Point((float)pad.Right));
				if (pad.Bottom != 0) node.SetPadding(YogaEdge.Bottom, YogaValue.Point((float)pad.Bottom));

				var children = ((IContainerView)view).GetChildren();
				for (int i = 0; i < children.Count; i++)
					node.InsertChild(Build(children[i], direction), node.ChildCount);
			}
			else
			{
				// Leaf: its intrinsic size comes from the native control via the backend node.
				var leaf = view;
				node.MeasureFunction = (_, availableWidth, widthMode, availableHeight, heightMode) =>
				{
					var w = Resolve(availableWidth, widthMode);
					var h = Resolve(availableHeight, heightMode);
					var size = leaf.Node?.Measure(w, h) ?? Size.Zero;
					return new YogaSize((float)size.Width, (float)size.Height);
				};
			}

			return node;
		}

		static void Arrange(View view, YogaNode node)
		{
			view.Node?.Arrange(new Rect(node.LayoutX, node.LayoutY, node.LayoutWidth, node.LayoutHeight));

			if (IsLayoutContainer(view))
			{
				var children = ((IContainerView)view).GetChildren();
				for (int i = 0; i < children.Count && i < node.ChildCount; i++)
					Arrange(children[i], node.GetChild(i));
			}
		}

		// A flow container whose children Yoga positions. Own-content nodes (list/nav) host
		// their own children and so are leaves to the layout pass.
		static bool IsLayoutContainer(View view)
			=> view is IContainerView && view.Node is not IBackendManagesOwnContent;

		static double Resolve(float available, YogaMeasureMode mode)
			=> (mode == YogaMeasureMode.Undefined || float.IsNaN(available))
				? double.PositiveInfinity
				: available;
	}
}
