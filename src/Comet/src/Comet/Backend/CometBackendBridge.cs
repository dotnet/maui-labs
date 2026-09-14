#nullable enable
using System;
using System.Collections.Generic;

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
		[ThreadStatic]
		static List<ICometBackendNode>? _collector;

		/// <summary>
		/// Collects every node created by Materialize calls inside the scope into
		/// <paramref name="into"/>. Own-content nodes use this to track a hosted subtree's
		/// node GENERATION so the previous generation can be disposed on swap — without it,
		/// stale nodes stay subscribed to statics (AfterFlush, control signals, window
		/// metrics) and every swap leaks a generation that keeps reacting. Scopes nest: an
		/// inner own-content node's children collect into ITS scope only.
		/// </summary>
		public static NodeCollectionScope CollectNodes(List<ICometBackendNode> into)
		{
			var previous = _collector;
			_collector = into;
			return new NodeCollectionScope(previous, into);
		}

		public readonly struct NodeCollectionScope : IDisposable
		{
			readonly List<ICometBackendNode>? _previous;
			readonly List<ICometBackendNode> _mine;
			internal NodeCollectionScope(List<ICometBackendNode>? previous, List<ICometBackendNode> mine)
			{
				_previous = previous;
				_mine = mine;
			}
			public void Dispose()
			{
				if (_collector == _mine)
					_collector = _previous;
			}
		}

		/// <summary>Materializes <paramref name="view"/> using each control's own
		/// <c>CreateBackendNode</c> (production path).</summary>
		public static ICometBackendNode Materialize(View view, BackendContext context)
			=> Materialize(view, v => v.CreateBackendNode(context), context);

		/// <summary>Materializes under a known <paramref name="parent"/> view so own-content
		/// nodes (navigation screens, list rows) register beneath their container in the dev
		/// tree and can be pruned as a subtree when replaced.</summary>
		internal static ICometBackendNode Materialize(View view, BackendContext context, View? parent)
			=> Materialize(view, v => v.CreateBackendNode(context), context, parent);

		/// <summary>Materializes <paramref name="view"/> using a supplied node factory
		/// (test path).</summary>
		public static ICometBackendNode Materialize(View view, CometNodeFactory factory, BackendContext context)
			=> Materialize(view, factory, context, parent: null);

		static ICometBackendNode Materialize(View view, CometNodeFactory factory, BackendContext context, View? parent)
		{
			if (view is null) throw new ArgumentNullException(nameof(view));
			if (factory is null) throw new ArgumentNullException(nameof(factory));

			// Components and [Body] views render to their concrete subtree first (GetView
			// runs CheckForBody, so a lazily-discovered [Body] materializes correctly);
			// plain views return themselves.
			var rendered = view.GetView();

			var node = factory(rendered);
			_collector?.Add(node);
			rendered.Node = node;
			node.SetEventSink(new ViewEventSink(rendered));

			// Register ONLY the reload roots ([Body]/Component views, which collapse to a
			// different rendered subtree) as hot-reload active views, and only when hot reload
			// is enabled. TriggerReload calls their Reload() so the replaced type re-renders
			// and diffs onto the retained nodes. Registering every materialized node (leaf) —
			// as an earlier revision did via the Node setter — leaked into the global
			// ActiveViews list in Release too (no IsEnabled gate, never pruned).
			if (!ReferenceEquals(rendered, view) && Microsoft.Maui.HotReload.MauiHotReloadHelper.IsEnabled)
				Microsoft.Maui.HotReload.MauiHotReloadHelper.AddActiveView(view);

			// Track for the in-process dev agent (no-op unless enabled) BEFORE applying
			// properties: own-content nodes (lists) materialize their children during
			// ApplyAllSetProperties, and those children resolve their parent through this entry.
			DevTools.CometDevRegistry.Register(rendered, node, parent);

			rendered.ApplyAllSetProperties(node);

			// Nodes that manage their own content (navigation, lists) pull the views they
			// need themselves; don't materialize the static child tree for them.
			if (rendered is IContainerView container && node is not IBackendManagesOwnContent)
			{
				var children = container.GetChildren();
				for (int i = 0; i < children.Count; i++)
				{
					var child = children[i];
					// Establish the parent link so .Navigation (and other inherited context)
					// propagates down the materialized tree.
					child.Parent = rendered;
					node.InsertChild(i, Materialize(child, factory, context, rendered));
				}
			}

			return node;
		}
	}
}
