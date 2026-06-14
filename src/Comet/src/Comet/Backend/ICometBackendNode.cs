#nullable enable
using System;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>
	/// One node in the retained backend tree — the single abstraction a platform
	/// renderer (Jetpack Compose, SwiftUI, future WinUI) implements. Comet's diff
	/// produces a stream of typed mutations against these nodes; the backend turns
	/// them into native UI.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nodes are <em>retained</em>: created once per Comet view instance and kept
	/// across rebuilds, so the diff applies the minimal patch set rather than
	/// re-emitting the tree. Only properties a view actually set are ever applied
	/// (see the generated <c>ApplyAllSetProperties</c> / <c>ApplyChangedProperties</c>),
	/// which is how default values stop crossing the boundary.
	/// </para>
	/// <para>
	/// Layout is computed in C# by Comet's Yoga engine; the backend is a positioned
	/// host. <see cref="Measure"/> is the one call that crosses into native to obtain a
	/// leaf's intrinsic size; <see cref="Arrange"/> pushes the Yoga-computed frame down.
	/// </para>
	/// </remarks>
	public interface ICometBackendNode : IDisposable
	{
		/// <summary>Applies a single typed property value. Called only for set/changed properties.</summary>
		void ApplyProperty(PropertyId id, in PropertyValue value);

		/// <summary>Inserts a child node at the given index in this node's child list.</summary>
		void InsertChild(int index, ICometBackendNode child);

		/// <summary>Removes the child at the given index.</summary>
		void RemoveChildAt(int index);

		/// <summary>Moves a child from one index to another (keyed-reorder support).</summary>
		void MoveChild(int fromIndex, int toIndex);

		/// <summary>
		/// Returns this node's intrinsic size under the given constraints. Used for leaf
		/// measurement by the Yoga bridge. Container nodes that defer to Yoga may return
		/// <see cref="Size.Zero"/>.
		/// </summary>
		Size Measure(double widthConstraint, double heightConstraint);

		/// <summary>
		/// Returns this node's first text baseline as an offset (Dp) from the top of its measured
		/// box, or <c>null</c> when the node has no text baseline. Used by the layout engine to
		/// align a row of text on a shared baseline (the Yoga baseline function). The offset must
		/// match where the backend actually renders the baseline, not just the font ascent.
		/// </summary>
		double? MeasureBaseline(double width, double height) => null;

		/// <summary>Positions this node at the Yoga-computed frame (parent-relative).</summary>
		void Arrange(Rect frame);

		/// <summary>Sets (or clears) the sink that receives this node's events and gestures.</summary>
		void SetEventSink(ICometEventSink? sink);
	}

	/// <summary>
	/// Marker for backend nodes that render their own content dynamically (e.g. a
	/// navigation stack or a virtualized list) rather than via the static child tree.
	/// The bridge skips generic child materialization for these — the node pulls the
	/// views it needs itself.
	/// </summary>
	public interface IBackendManagesOwnContent { }
}
