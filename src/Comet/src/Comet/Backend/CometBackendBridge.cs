#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
		sealed class MaterializationRegistration
		{
			public MaterializationRegistration(CometNodeFactory factory, BackendContext context)
			{
				Factory = factory;
				Context = context;
			}

			public CometNodeFactory Factory { get; }
			public BackendContext Context { get; }
		}

		sealed class MaterializedNodeRegistration
		{
			public MaterializedNodeRegistration(
				View view,
				List<ICometBackendNode>? generation)
			{
				View = new WeakReference<View>(view);
				Generation = generation;
			}

			public WeakReference<View> View { get; set; }
			public List<ICometBackendNode>? Generation { get; }
		}

		[ThreadStatic]
		static List<ICometBackendNode>? _collector;
		static readonly ConditionalWeakTable<View, MaterializationRegistration> _registrations = new();
		static readonly ConditionalWeakTable<ICometBackendNode, MaterializedNodeRegistration> _nodeRegistrations = new();
		static readonly ConditionalWeakTable<ICometBackendNode, object> _disposedNodes = new();
		static readonly object _nodeRegistrationGate = new();

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

		internal static ICometBackendNode MaterializeChild(View child, View parent)
		{
			if (!_registrations.TryGetValue(parent, out var registration))
				throw new InvalidOperationException(
					$"No backend materialization context is registered for {parent.GetType().Name}.");

			List<ICometBackendNode>? generation = null;
			if (_collector is null && parent.Node is { } parentNode)
			{
				lock (_nodeRegistrationGate)
				{
					if (_nodeRegistrations.TryGetValue(parentNode, out var nodeRegistration))
						generation = nodeRegistration.Generation;
				}
			}

			// Reactive structural diffs run after the original collection scope ended.
			// Keep newly inserted descendants in their parent's owned generation so
			// deactivating a navigation screen releases the complete current subtree.
			if (generation is not null)
			{
				using var collection = CollectNodes(generation);
				return Materialize(
					child,
					registration.Factory,
					registration.Context,
					parent);
			}

			return Materialize(
				child,
				registration.Factory,
				registration.Context,
				parent);
		}

		internal static void TransferMaterializationRegistration(View oldView, View newView)
		{
			if (!_registrations.TryGetValue(oldView, out var registration))
				return;

			_registrations.Remove(oldView);
			_registrations.Remove(newView);
			_registrations.Add(newView, registration);
		}

		internal static void TransferMaterializedNodeRegistration(
			View oldView,
			View newView,
			ICometBackendNode node)
		{
			lock (_nodeRegistrationGate)
			{
				if (_nodeRegistrations.TryGetValue(node, out var registration) &&
					registration.View.TryGetTarget(out var registeredView) &&
					ReferenceEquals(registeredView, oldView))
					registration.View = new WeakReference<View>(newView);
			}
		}

		/// <summary>
		/// Releases a materialization that was created for a replacement view before the
		/// reconciler transferred the retained node into it. The obsolete node is detached
		/// from its generation so that generation cannot dispose it a second time later.
		/// </summary>
		internal static void ReleaseMaterializedNode(View view)
		{
			var node = view.Node;
			if (node is null)
				return;

			DevTools.CometDevRegistry.UnregisterSubtree(view, includeRoot: true);
			view.Node = null;
			DisposeNode(node);
		}

		/// <summary>
		/// Disposes a node once, removing it from any collected generation and clearing the
		/// owning view's node reference only when it still points at this exact node.
		/// </summary>
		internal static void DisposeNode(ICometBackendNode node)
		{
			if (node is null)
				return;

			MaterializedNodeRegistration? registration = null;
			lock (_nodeRegistrationGate)
			{
				if (_disposedNodes.TryGetValue(node, out _))
					return;
				_disposedNodes.Add(node, new object());
				if (_nodeRegistrations.TryGetValue(node, out registration))
				{
					registration.Generation?.Remove(node);
					_nodeRegistrations.Remove(node);
				}
			}

			if (registration?.View.TryGetTarget(out var view) == true &&
				ReferenceEquals(view.Node, node))
			{
				DevTools.CometDevRegistry.Unregister(view);
				view.Node = null;
			}

			node.Dispose();
		}

		internal static void DisposeNodes(List<ICometBackendNode> nodes)
		{
			if (nodes is null)
				return;

			while (nodes.Count > 0)
			{
				var node = nodes[^1];
				nodes.RemoveAt(nodes.Count - 1);
				DisposeNode(node);
			}
		}

		/// <summary>
		/// Clears retained node references from a hosted generation after its nodes were
		/// disposed. Persistent logical views can then be materialized again when their
		/// owning navigation section becomes active.
		/// </summary>
		internal static void ClearMaterializedNodes(View view)
		{
			if (view is null)
				return;

			var rendered = view.GetView();
			rendered.Node = null;
			if (rendered is not IContainerView container)
				return;

			foreach (var child in container.GetChildren())
			{
				if (child is not null)
					ClearMaterializedNodes(child);
			}
		}

		/// <summary>
		/// Clears only node references that belong to a specific owned generation. A logical
		/// root can be rematerialized before an older generation is disposed; in that case the
		/// replacement generation's live nodes must remain attached.
		/// </summary>
		internal static void ClearMaterializedNodes(
			View view,
			IReadOnlySet<ICometBackendNode> ownedNodes)
		{
			if (view is null || ownedNodes is null)
				return;

			var rendered = view.GetView() ?? view;
			if (rendered.Node is { } node && ownedNodes.Contains(node))
				rendered.Node = null;
			if (rendered is not IContainerView container)
				return;

			foreach (var child in container.GetChildren())
			{
				if (child is not null)
					ClearMaterializedNodes(child, ownedNodes);
			}
		}

		internal static ICometBackendNode Materialize(
			View view,
			CometNodeFactory factory,
			BackendContext context,
			View? parent)
		{
			if (view is null) throw new ArgumentNullException(nameof(view));
			if (factory is null) throw new ArgumentNullException(nameof(factory));

			// Components and [Body] views render to their concrete subtree first (GetView
			// runs CheckForBody, so a lazily-discovered [Body] materializes correctly);
			// plain views return themselves.
			var rendered = view.GetView();
			if (rendered.Node is not null)
				ReleaseMaterializedNode(rendered);
			_registrations.Remove(rendered);
			_registrations.Add(rendered, new MaterializationRegistration(factory, context));

			// Pre-register the view's ID so own-content constructors (navigation Push,
			// list row materialization) find the parent when materializing children inside
			// factory(). The node parameter is unused by Register — it only tracks the
			// view identity and parent link. The full registration below is idempotent.
			DevTools.CometDevRegistry.Register(rendered, null!, parent);

			var node = factory(rendered);
			var generation = _collector;
			generation?.Add(node);
			lock (_nodeRegistrationGate)
			{
				_nodeRegistrations.Remove(node);
				_nodeRegistrations.Add(
					node,
					new MaterializedNodeRegistration(rendered, generation));
			}
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

			BackendPropertyState.Apply(rendered, node);

			// Nodes that manage their own content (navigation, lists) pull the views they
			// need themselves; don't materialize the static child tree for them.
			if (rendered is IContainerView container && node is not IBackendManagesOwnContent)
			{
				var children = container.GetChildren();
				var backendIndex = 0;
				for (int i = 0; i < children.Count; i++)
				{
					var child = children[i];
					if (child is null)
						continue;
					// Establish the parent link so .Navigation (and other inherited context)
					// propagates down the materialized tree.
					child.Parent = rendered;
					node.InsertChild(backendIndex++, Materialize(child, factory, context, rendered));
				}
			}

			return node;
		}
	}
}
