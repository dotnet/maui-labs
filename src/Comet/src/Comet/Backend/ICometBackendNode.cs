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
	/// across rebuilds, so the diff applies property patches rather than re-emitting
	/// the tree. Explicit declarative properties are tracked per retained node; when a
	/// later declaration omits one, its protocol default is emitted once to clear stale
	/// native state.
	/// </para>
	/// <para>
	/// Layout is computed in C# by Comet's Yoga engine; the backend is a positioned
	/// host. <see cref="Measure"/> is the one call that crosses into native to obtain a
	/// leaf's intrinsic size; <see cref="Arrange"/> pushes the Yoga-computed frame down.
	/// </para>
	/// </remarks>
	public interface ICometBackendNode : IDisposable
	{
		/// <summary>Applies a single typed property value or a reset to its protocol default.</summary>
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

		/// <summary>Insets this node's rendered content down by <paramref name="dp"/> within its
		/// arranged box (the layout engine has already grown the box to match). Used to realize
		/// <c>baselineHeight</c>: pad the top so the text's first baseline lands at a fixed offset.
		/// Default no-op for backends that don't support it.</summary>
		void SetContentTopInset(double dp) { }

		/// <summary>Positions this node at the Yoga-computed frame (parent-relative).</summary>
		void Arrange(Rect frame);

		/// <summary>Sets (or clears) the sink that receives this node's events and gestures.</summary>
		void SetEventSink(ICometEventSink? sink);

		/// <summary>
		/// Called when a diff transferred this retained node from an old view instance to
		/// <paramref name="newView"/> (see <c>View.TransferBackendNodeFrom</c>). Own-content
		/// nodes re-point their captured view reference to <paramref name="newView"/> in all
		/// cases; they invalidate content they materialized from the old tree ONLY when
		/// <paramref name="isHotReload"/> is true — an ordinary reactive re-render (Component
		/// <c>SetState</c>) must preserve retained state (navigation stack, list scroll, cached
		/// screens), whereas a hot reload replaces the code and needs a re-materialize. Default
		/// no-op (leaf nodes carry no owner reference or materialized content).
		/// </summary>
		void OnOwnerViewChanged(View newView, bool isHotReload) { }
	}

	/// <summary>
	/// Marker for backend nodes that render their own content dynamically (e.g. a
	/// navigation stack or a virtualized list) rather than via the static child tree.
	/// The bridge skips generic child materialization for these — the node pulls the
	/// views it needs itself.
	/// </summary>
	public interface IBackendManagesOwnContent { }

	/// <summary>
	/// Marks an own-content node that deliberately keeps logical children from the
	/// outgoing owner after its retained backend node transfers to a replacement view.
	/// For unkeyed persistent-control swaps, the reconciler keeps that outgoing owner out
	/// of normal old-tree disposal because disposing it would also dispose logical children
	/// the node still hosts. A keyed declaration is a complete replacement owner instead;
	/// such a node must reconcile or re-home any retained children in
	/// <see cref="ICometBackendNode.OnOwnerViewChanged"/>.
	/// </summary>
	public interface IBackendRetainsLogicalContentOnOwnerTransfer : IBackendManagesOwnContent { }

	/// <summary>
	/// Marks an own-content node whose single live content subtree participates in the
	/// normal logical diff before the retained owner node is transferred. Dynamic owners
	/// with inactive slots (dialogs, lists, tabs, and navigation stacks) must not implement
	/// this contract because their content is materialized only by their own lifecycle.
	/// </summary>
	public interface IBackendReconcilesOwnContent : IBackendManagesOwnContent { }
}
