#nullable enable
using System;

namespace Comet.Backend
{
	/// <summary>
	/// Owns one lazily materialized content generation and tracks its current logical
	/// root. A transferred node can be rebound to a replacement root without changing
	/// generation ownership; replacing or disposing the slot releases every node in
	/// the generation exactly once.
	/// </summary>
	internal sealed class OwnedContentSlot<TNode> : IDisposable
		where TNode : class, ICometBackendNode
	{
		View _owner;
		readonly CometNodeFactory _factory;
		readonly BackendContext _context;
		OwnedContentGeneration? _generation;
		TNode? _node;
		View? _view;

		public OwnedContentSlot(View owner, BackendContext context)
			: this(owner, view => view.CreateBackendNode(context), context)
		{
		}

		internal OwnedContentSlot(
			View owner,
			CometNodeFactory factory,
			BackendContext context)
		{
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			_factory = factory ?? throw new ArgumentNullException(nameof(factory));
			_context = context;
		}

		public bool IsDisposed { get; private set; }
		public View? View => IsDisposed ? null : _view;
		public bool IsActive(View view)
			=> !IsDisposed &&
				ReferenceEquals(_view, view) &&
				_node is not null;

		public void TransferOwner(View owner)
		{
			if (IsDisposed)
				return;
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			_generation?.TransferOwner(owner);
		}

		public bool TryGet(out TNode? node, out View? view)
		{
			node = IsDisposed ? null : _node;
			view = IsDisposed ? null : _view;
			return node is not null && view is not null;
		}

		public TNode Materialize(View view)
		{
			if (IsDisposed)
				throw new ObjectDisposedException(nameof(OwnedContentSlot<TNode>));
			if (view is null)
				throw new ArgumentNullException(nameof(view));

			Clear();
			var generation = new OwnedContentGeneration(_owner, _factory, _context);
			try
			{
				var node = (TNode)generation.Materialize(view);
				_generation = generation;
				_node = node;
				_view = view;
				return node;
			}
			catch
			{
				generation.Dispose();
				throw;
			}
		}

		/// <summary>
		/// Rebinds the slot's logical root after reconciliation transferred the same
		/// retained node to a replacement view.
		/// </summary>
		public bool RetainTransferred(TNode node, View view)
		{
			if (IsDisposed ||
				!ReferenceEquals(_node, node) ||
				_generation is null)
				return false;

			if (view is null)
				throw new ArgumentNullException(nameof(view));
			if (_view is { } oldView)
				_generation.TransferRoot(oldView, view);
			_view = view;
			return true;
		}

		public void Clear()
		{
			if (IsDisposed)
				return;

			var generation = _generation;
			_generation = null;
			_node = null;
			_view = null;
			generation?.Dispose();
		}

		public void Dispose()
		{
			if (IsDisposed)
				return;

			Clear();
			IsDisposed = true;
		}
	}
}
