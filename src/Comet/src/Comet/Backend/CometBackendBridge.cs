#nullable enable
using System;

namespace Comet.Backend
{
	/// <summary>Creates the backend node for a given view. Pluggable so host tests can
	/// substitute a recording fake for the platform node.</summary>
	public delegate ICometBackendNode CometNodeFactory(View view);

	/// <summary>
	/// Walks a Comet view tree and materializes it into a retained
	/// <see cref="ICometBackendNode"/> tree, applying each view's set-only properties and
	/// nesting container children. This is the initial-mount counterpart to the diff's
	/// incremental node patching.
	/// </summary>
	public static class CometBackendBridge
	{
		/// <summary>Materializes <paramref name="view"/> using each control's own
		/// <c>CreateBackendNode</c> (production path).</summary>
		public static ICometBackendNode Materialize(View view, BackendContext context)
			=> Materialize(view, v => v.CreateBackendNode(context), context);

		/// <summary>Materializes <paramref name="view"/> using a supplied node factory
		/// (test path).</summary>
		public static ICometBackendNode Materialize(View view, CometNodeFactory factory, BackendContext context)
		{
			if (view is null) throw new ArgumentNullException(nameof(view));
			if (factory is null) throw new ArgumentNullException(nameof(factory));

			// Components with a Body render to their concrete subtree first.
			var rendered = view.HasContent ? view.GetView() : view;

			var node = factory(rendered);
			rendered.Node = node;
			node.SetEventSink(new ViewEventSink(rendered));
			rendered.ApplyAllSetProperties(node);

			if (rendered is IContainerView container)
			{
				var children = container.GetChildren();
				for (int i = 0; i < children.Count; i++)
					node.InsertChild(i, Materialize(children[i], factory, context));
			}

			return node;
		}
	}
}
