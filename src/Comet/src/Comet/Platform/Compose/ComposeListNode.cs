#nullable enable
#if ANDROID
using System.Collections.Generic;
using System.Linq;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeLazyColumn = AndroidX.Compose.LazyColumn<int>;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Renders a Comet <c>ListView</c>/<c>CollectionView</c> as a Compose
	/// <c>LazyColumn</c> — genuinely virtualized: each row's template
	/// <see cref="View"/> is materialized into a backend node only when Compose
	/// composes that row (i.e. when it scrolls into view).
	/// </summary>
	/// <remarks>
	/// Single-section lists (the common case) are flattened to a flat row index.
	/// Data changes bump a version <see cref="MutableState{T}"/> via
	/// <see cref="ApplyProperty"/> so the LazyColumn recomposes against the new rows.
	/// </remarks>
	sealed class ComposeListNode : ComposeNode
	{
		readonly IListView _list;
		readonly BackendContext _context;
		readonly MutableState<int> _version = new(0);

		// Compose re-invokes a LazyColumn item's content on every recomposition (and the list
		// recomposes per scroll frame), so materializing + Yoga-laying-out the row in the item
		// lambda re-did that work for every visible row every frame. Cache the materialized node per
		// row so a recomposition is O(1); invalidate when the data version or the row width changes.
		readonly System.Collections.Generic.Dictionary<int, ComposableNode> _rowCache = new();
		int _cachedVersion = -1;
		double _cachedWidth = -1;

		// Scroll-state bridge: a remembered LazyListState surfaces scroll position to C#. We read
		// CanScrollForward inside composition (boundary-triggered, so no per-frame churn) and marshal
		// it to the list's ScrolledAway signal; a registered scroller animates to the end on demand.
		bool _scrollerRegistered;
		bool? _lastScrolledAway;

		public ComposeListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
				_version.Value++; // recompose against the latest rows
		}

		public override void Render(IComposer composer)
		{
			int version = _version.Value; // subscribe so data changes recompose the list

			// Remembered scroll state (survives recomposition). Read CanScrollForward HERE, inside
			// composition, so this list recomposes when it flips at the boundary; then push it to the
			// ScrolledAway signal so a JumpToBottom affordance can react. The first time, hand the
			// list a scroller that animates to the last row (the bottom of a normal-layout chat log).
			var listState = composer.RememberLazyListState();
			if (!_scrollerRegistered)
			{
				_scrollerRegistered = true;
				var captured = listState;
				_list.RegisterScroller(() =>
				{
					int last = (_list.Sections() > 0 ? _list.Rows(0) : 0) - 1;
					if (last >= 0)
						_ = captured.AnimateScrollToItemAsync(last);
				});
			}

			// Single-section (the common case); multi-section flattening is a follow-up.
			int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
			var indices = Enumerable.Range(0, count).ToList();

			// Under Yoga, lay each row out to the list's arranged width so rows render identically
			// to the rest of the tree (and to iOS). FrameWidth is 0 until the engine arranges this
			// list, so fall back to the screen width.
			bool yoga = HasFrame;
			double rowWidth = FrameWidth > 0
				? FrameWidth
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / ComposeNode.Density;

			// Drop the cache when the rows or the width change (otherwise we'd render stale layout).
			if (version != _cachedVersion || rowWidth != _cachedWidth)
			{
				_rowCache.Clear();
				_cachedVersion = version;
				_cachedWidth = rowWidth;
			}

			var lazy = new ComposeLazyColumn(indices, i =>
			{
				if (_rowCache.TryGetValue(i, out var cached))
					return cached;

				// First time this row is needed: build, materialize, and Yoga-lay-out once, then cache.
				var view = _list.ViewFor(0, i);
				var node = (ComposableNode)CometBackendBridge.Materialize(view, _context);
				if (yoga)
					CometBackendLayoutEngine.LayoutContent(view, rowWidth);
				_rowCache[i] = node;
				return node;
			})
			{
				State = listState,
			};

			// Position + size the list from its Yoga frame (offset below the top bar, sized to the
			// remaining height) so it scrolls within its slot rather than laying out at the origin.
			((ComposableNode)lazy).Modifier = BuildNodeModifier();
			lazy.Render(composer);

			// Marshal the scroll position to the list's signal. CanScrollForward is false only when the
			// last row is fully visible (at the bottom); true ⇒ scrolled away ⇒ show JumpToBottom.
			// Defer the write off the composition pass (we're on the UI thread) so we don't mutate Comet
			// reactive state mid-compose; the guard makes it a no-op except at the boundary flip.
			bool away = listState.CanScrollForward;
			if (away != _lastScrolledAway)
			{
				_lastScrolledAway = away;
				var signal = _list.ScrolledAway;
				Comet.ThreadHelper.RunOnMainThread(() => signal.Value = away);
			}
		}
	}
}
#endif
