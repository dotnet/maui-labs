#nullable enable
using System;
using System.Collections.Generic;

namespace Comet.Backend
{
	/// <summary>
	/// Lazily materializes indexed route content and keeps each generation alive while the
	/// owning switcher remains alive. Switching back to a route reuses its native node tree
	/// instead of rebuilding the complete Comet subtree.
	/// </summary>
	internal sealed class RetainedContentCache<TNode> : IDisposable
		where TNode : class, ICometBackendNode
	{
		sealed class Entry
		{
			public Entry(
				View view,
				View renderedRoot,
				TNode node,
				OwnedContentGeneration generation)
			{
				View = view;
				RenderedRoot = renderedRoot;
				Node = node;
				Generation = generation;
			}

			public View View { get; set; }
			public View RenderedRoot { get; set; }
			public TNode Node { get; }
			public OwnedContentGeneration Generation { get; }
			public bool Active { get; set; }
		}

		View _owner;
		readonly CometNodeFactory _factory;
		readonly BackendContext _context;
		readonly List<Entry?> _entries = new();
		bool _disposed;

		public RetainedContentCache(View owner, BackendContext context)
			: this(owner, view => view.CreateBackendNode(context), context)
		{
		}

		internal RetainedContentCache(
			View owner,
			CometNodeFactory factory,
			BackendContext context)
		{
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			_factory = factory ?? throw new ArgumentNullException(nameof(factory));
			_context = context;
		}

		public (TNode Node, View View) GetOrMaterialize(int index, View requested)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(RetainedContentCache<TNode>));
			if (index < 0)
				throw new ArgumentOutOfRangeException(nameof(index));
			if (requested is null)
				throw new ArgumentNullException(nameof(requested));

			EnsureCapacity(index + 1);
			if (_entries[index] is { } retained)
			{
				if (CanRetain(retained.View, requested))
				{
					if (ReferenceEquals(retained.View, requested) ||
						TryReconcile(retained, requested))
						return (retained.Node, retained.View);
				}
				DisposeEntry(index);
			}

			var generation = new OwnedContentGeneration(_owner, _factory, _context);
			try
			{
				var node = (TNode)generation.Materialize(requested);
				var renderedRoot = requested.GetView() ?? requested;
				_entries[index] = new Entry(requested, renderedRoot, node, generation);
				return (node, requested);
			}
			catch
			{
				generation.Dispose();
				throw;
			}
		}

		public bool TryGet(int index, out TNode? node, out View? view)
		{
			if (!_disposed &&
				index >= 0 &&
				index < _entries.Count &&
				_entries[index] is { } entry)
			{
				node = entry.Node;
				view = entry.View;
				return true;
			}

			node = null;
			view = null;
			return false;
		}

		public void SetActive(int index, bool active)
		{
			if (_disposed ||
				index < 0 ||
				index >= _entries.Count ||
				_entries[index] is not { } entry ||
				entry.Active == active)
				return;
			entry.Active = active;
			if (entry.Node is IBackendContentActivation activation)
				activation.SetContentActive(active);
			DevTools.CometDevRegistry.SetSubtreeActive(entry.RenderedRoot, active);
		}

		public void TransferOwner(
			View owner,
			IReadOnlyList<View> routes,
			bool reset)
		{
			if (_disposed)
				return;
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			if (routes is null)
				throw new ArgumentNullException(nameof(routes));
			if (reset)
			{
				Clear();
				return;
			}

			for (var index = _entries.Count - 1; index >= 0; index--)
			{
				if (_entries[index] is not { } entry)
					continue;
				if (index >= routes.Count ||
					!CanRetain(entry.View, routes[index]))
				{
					DisposeEntry(index);
					continue;
				}

				entry.Generation.TransferOwner(owner);
			}
		}

		static bool CanRetain(View current, View replacement)
		{
			if (ReferenceEquals(current, replacement))
				return true;
			if (current.GetType() != replacement.GetType())
				return false;

			var currentKey = current.GetKey();
			var replacementKey = replacement.GetKey();
			return string.IsNullOrEmpty(currentKey) &&
					string.IsNullOrEmpty(replacementKey)
				|| string.Equals(currentKey, replacementKey, StringComparison.Ordinal);
		}

		static bool TryReconcile(Entry retained, View requested)
		{
			var previous = retained.View;
			var reconciled = requested.Diff(previous, checkRenderers: false);
			var renderedRoot = reconciled.GetView() ?? reconciled;

			// A same logical route may render a different root control after rebuilding.
			// The ordinary diff cannot transfer that root node, so fall back to replacing
			// the generation rather than returning a disconnected retained node.
			if (!ReferenceEquals(renderedRoot.Node, retained.Node))
				return false;

			if (!ReferenceEquals(previous, reconciled))
			{
				retained.Generation.TransferRoot(previous, reconciled);
				retained.View = reconciled;
			}
			retained.RenderedRoot = renderedRoot;
			return true;
		}

		void EnsureCapacity(int count)
		{
			while (_entries.Count < count)
				_entries.Add(null);
		}

		void DisposeEntry(int index)
		{
			if (index < 0 ||
				index >= _entries.Count ||
				_entries[index] is not { } entry)
				return;

			_entries[index] = null;
			if (entry.Active)
			{
				if (entry.Node is IBackendContentActivation activation)
					activation.SetContentActive(false);
				DevTools.CometDevRegistry.SetSubtreeActive(entry.RenderedRoot, false);
			}
			entry.Generation.Dispose();
		}

		public void Clear()
		{
			for (var index = _entries.Count - 1; index >= 0; index--)
				DisposeEntry(index);
			_entries.Clear();
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			Clear();
			_disposed = true;
		}
	}
}
