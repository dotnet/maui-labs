#nullable enable
#if ANDROID
using System.Collections.Generic;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// A retained backend node that bridges Comet's imperative diff
	/// (<see cref="ICometBackendNode"/>) to Jetpack Compose's declarative
	/// recomposition (the vendored <see cref="ComposableNode"/>).
	/// </summary>
	/// <remarks>
	/// The node is created once when a Comet view mounts and lives across
	/// recompositions. Each renderable property is a retained Compose
	/// <see cref="MutableState{T}"/>: <see cref="ApplyProperty"/> writes
	/// <c>.Value</c> (the single steady-state JNI call — <c>setValue</c>), and
	/// <see cref="ComposableNode.Render"/> reads it inside composition so Compose
	/// recomposes only the narrowest scope. Structural changes bump
	/// <see cref="_childVersion"/>, which container renders read so a child
	/// insert/remove/move recomposes the container.
	/// </remarks>
	abstract class ComposeNode : ComposableNode, ICometBackendNode
	{
		protected readonly List<ComposeNode> Children = new();
		readonly MutableState<int> _childVersion = new(0);
		ICometEventSink? _sink;
		readonly MutableState<bool> _hasTap = new(false);

		protected ICometEventSink? Sink => _sink;

		// ICometBackendNode ----------------------------------------------------

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.HasTapGesture)
				_hasTap.Value = value.AsBool;
			else
				ApplyControlProperty(id, in value);
		}

		/// <summary>Applies a control-specific property. Common (View-level) properties are
		/// handled by <see cref="ApplyProperty"/> before this is called.</summary>
		protected abstract void ApplyControlProperty(PropertyId id, in PropertyValue value);

		/// <summary>Builds the modifier this node should apply to its composable — currently a
		/// clickable when the Comet view has a tap gesture. Returns null when none applies.</summary>
		protected Modifier? BuildNodeModifier()
		{
			if (!_hasTap.Value)
				return null;
			return Modifier.Clickable(() =>
				Sink?.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default)));
		}

		public void InsertChild(int index, ICometBackendNode child)
		{
			Children.Insert(index, (ComposeNode)child);
			_childVersion.Value++;
		}

		public void RemoveChildAt(int index)
		{
			Children.RemoveAt(index);
			_childVersion.Value++;
		}

		public void MoveChild(int fromIndex, int toIndex)
		{
			var node = Children[fromIndex];
			Children.RemoveAt(fromIndex);
			Children.Insert(toIndex, node);
			_childVersion.Value++;
		}

		// Compose measures/positions its own tree for now; the Yoga positioned-host
		// model wires through these in a later step.
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

		public void Dispose() { }

		// Composition helpers --------------------------------------------------

		/// <summary>Adds this node's children to a freshly-built vendored container,
		/// subscribing to structural changes so the container recomposes on
		/// insert/remove/move.</summary>
		protected void AddChildrenTo(ComposableContainer container)
		{
			_ = _childVersion.Value; // subscribe
			foreach (var child in Children)
				container.Add(child);
		}
	}
}
#endif
