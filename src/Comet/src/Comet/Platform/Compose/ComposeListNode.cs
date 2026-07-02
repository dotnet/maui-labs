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
		IListView _list;
		readonly BackendContext _context;
		readonly MutableState<int> _version = new(0);

		// Compose re-invokes a LazyColumn item's content on every recomposition (and the list
		// recomposes per scroll frame), so materializing + Yoga-laying-out the row in the item
		// lambda re-did that work for every visible row every frame. Cache the materialized node per
		// row so a recomposition is O(1); invalidate when the data version or the row width changes.
		readonly System.Collections.Generic.Dictionary<int, ComposableNode> _rowCache = new();
		int _cachedVersion = -1;
		double _cachedWidth = -1;

		// Scroll-state bridge: a remembered LazyListState surfaces scroll position to C#. A
		// LaunchedEffect + snapshotFlow watches CanScrollBackward outside the untracked root
		// composable scope, so ScrolledAway fires correctly on scroll; a registered scroller
		// animates to index 0 (newest = top of a newest-first list) on demand.
		bool _scrollerRegistered;

		public ComposeListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
		}

		/// <summary>The node was transferred to a new ListView (ordinary re-render or hot reload).
		/// Re-point at the new list and re-bind the JumpToBottom scroller + ScrolledAway signal to
		/// it; bump the version so the new list's data is read (Render's version check drops the
		/// stale row cache). Scroll position is preserved either way — the LazyListState persists
		/// via Remember and the one-shot seed is composition-keyed, so it does not re-fire here.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not IListView list)
				return;
			_list = list;
			_scrollerRegistered = false;
			_version.Value++;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
				_version.Value++; // recompose against the latest rows
		}

		public override void Render(IComposer composer)
		{
			int version = _version.Value; // subscribe so data changes recompose the list

			// Remembered scroll state (survives recomposition). The first time, register a scroller
			// that animates to the last item (newest = bottom of a forward-order list).
			var listState = composer.RememberLazyListState();
			if (!_scrollerRegistered)
			{
				_scrollerRegistered = true;
				var captured = listState;
				_list.RegisterScroller(() =>
				{
					int lastIndex = _list.Sections() > 0 ? System.Math.Max(0, _list.Rows(0) - 1) : 0;
					_ = captured.AnimateScrollToItemAsync(lastIndex);
				});
			}

			// snapshotFlow tracks canScrollForward: true when the newest messages are below the
			// viewport (user has scrolled up). LaunchedEffect(true) starts once and cancels when
			// the list leaves the composition.
			var capturedList = _list;
			var capturedState = listState;
			composer.LaunchedEffect(true, async ct =>
			{
				// Open at the newest message (bottom) — the gold's reverseLayout initial position.
				// Instant (not animated) so launch doesn't visibly scroll through the older history;
				// scrollToItem(last) clamps to the maximum, landing the newest row at the bottom.
				int lastIndex = capturedList.Sections() > 0 ? System.Math.Max(0, capturedList.Rows(0) - 1) : 0;
				if (lastIndex > 0)
					await capturedState.ScrollToItemAsync(lastIndex);

				await foreach (var away in ComposeExtensions.SnapshotFlow(() => capturedState.CanScrollForward)
					.WithCancellation(ct))
				{
					var signal = capturedList.ScrolledAway;
					Comet.ThreadHelper.RunOnMainThread(() => signal.Value = away);
				}
			});

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
		}
	}
}
#endif
