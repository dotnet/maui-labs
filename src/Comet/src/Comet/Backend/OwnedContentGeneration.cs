#nullable enable
using System;
using System.Collections.Generic;

namespace Comet.Backend
{
	/// <summary>
	/// Owns one materialized backend generation whose roots are hosted outside the
	/// bridge's normal child walk (dialog slots, hosted panels, and similar content).
	/// Disposal removes the registry subtrees, releases every backend node, and clears
	/// retained node references so persistent logical views can be materialized again.
	/// </summary>
	internal sealed class OwnedContentGeneration : IDisposable
	{
		View _owner;
		readonly CometNodeFactory _factory;
		readonly BackendContext _context;
		readonly List<View> _roots = new();
		readonly List<ICometBackendNode> _nodes = new();
		bool _disposed;

		public OwnedContentGeneration(View owner, BackendContext context)
			: this(owner, view => view.CreateBackendNode(context), context)
		{
		}

		internal OwnedContentGeneration(
			View owner,
			CometNodeFactory factory,
			BackendContext context)
		{
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			_factory = factory ?? throw new ArgumentNullException(nameof(factory));
			_context = context;
		}

		public void TransferOwner(View owner)
		{
			if (_disposed)
				return;
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
		}

		/// <summary>
		/// Rebinds a materialized generation to the replacement logical root after its
		/// backend node identity was transferred during reconciliation.
		/// </summary>
		public bool TransferRoot(View oldRoot, View newRoot)
		{
			if (_disposed)
				return false;
			if (oldRoot is null)
				throw new ArgumentNullException(nameof(oldRoot));
			if (newRoot is null)
				throw new ArgumentNullException(nameof(newRoot));

			for (var index = 0; index < _roots.Count; index++)
			{
				if (!ReferenceEquals(_roots[index], oldRoot))
					continue;
				_roots[index] = newRoot;
				return true;
			}

			return false;
		}

		public ICometBackendNode Materialize(View root)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(OwnedContentGeneration));
			if (root is null)
				throw new ArgumentNullException(nameof(root));

			_roots.Add(root);
			using (CometBackendBridge.CollectNodes(_nodes))
				return CometBackendBridge.Materialize(root, _factory, _context, _owner);
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;

			var ownedNodes = new HashSet<ICometBackendNode>(_nodes);
			foreach (var root in _roots)
			{
				var rendered = root.GetView() ?? root;
				if (rendered.Node is null || ownedNodes.Contains(rendered.Node))
					DevTools.CometDevRegistry.UnregisterSubtree(rendered, includeRoot: true);
			}

			CometBackendBridge.DisposeNodes(_nodes);
			foreach (var root in _roots)
				CometBackendBridge.ClearMaterializedNodes(root, ownedNodes);
			_roots.Clear();
		}
	}
}
