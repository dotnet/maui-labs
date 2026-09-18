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
	sealed class ComposeListNode : ComposeNode, IBackendManagesOwnContent
	{
		IListView _list;
		readonly BackendContext _context;
		readonly MutableState<int> _version = new(0);

		// Compose re-invokes a LazyColumn item's content on every recomposition (and the list
		// recomposes per scroll frame), so materializing + Yoga-laying-out the row in the item
		// lambda re-did that work for every visible row every frame. Cache the materialized node per
		// row so a recomposition is O(1); invalidate when the data version or the row width changes.
		readonly System.Collections.Generic.Dictionary<int, NativeListRow> _rowCache = new();
		OwnedContentGeneration? _rowGeneration;
		int _cachedVersion = -1;
		double _cachedWidth = -1;

		// Scroll-state bridge: a remembered LazyListState surfaces scroll position to C#. A
		// LaunchedEffect + snapshotFlow watches CanScrollBackward outside the untracked root
		// composable scope, so ScrolledAway fires correctly on scroll; a registered scroller
		// animates to index 0 (newest = top of a newest-first list) on demand.
		bool _scrollerRegistered;
		readonly ListInitialScrollState _initialScroll = new();
		PaddingValues? _centerContentPadding;
		double _centerContentPaddingExtent = -1;

		public ComposeListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
		}

		/// <summary>The node was transferred to a new ListView (ordinary re-render or hot reload).
		/// Re-point at the new list and re-bind the JumpToBottom scroller + ScrolledAway signal to
		/// it; bump the version so the new list's data is read (Render's version check drops the
		/// stale row cache). Initial scroll remains one-shot for the retained native node, so
		/// ordinary row updates preserve the user's current scroll position.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not IListView list)
				return;
			_list = list;
			_scrollerRegistered = false;
			// TransferBackendNodeFrom applies List_Version immediately after this callback.
			// Let that one patch release the old rows and invalidate composition exactly once.
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
			{
				ReleaseRows();
				_version.Value++; // recompose against the latest rows
			}
		}

		void ReleaseRows()
		{
			_rowGeneration?.Dispose();
			_rowGeneration = null;
			foreach (var row in _rowCache.Values)
				row.Dispose();
			_rowCache.Clear();
		}

		NativeListRow GetRow(int index)
		{
			if (_rowCache.TryGetValue(index, out var row))
				return row;

			var generation = _rowGeneration ??=
				new OwnedContentGeneration((View)_list, _context);
			row = NativeListRow.Materialize(_list.ViewFor(0, index), generation);
			_rowCache[index] = row;
			return row;
		}

		double RowWidth => FrameWidth > 0
			? FrameWidth
			: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / ComposeNode.Density;

		public override void Render(IComposer composer)
		{
			int version = _version.Value; // subscribe so data changes recompose the list

			// Container flavor resolves FIRST so every later decision (state bridge, row
			// width, container) agrees: Horizontal wins (grid is a vertical concept), then
			// adaptive grid, then the plain LazyColumn.
			bool horizontal = _list.Horizontal;
			double gridMin = horizontal ? 0 : _list.GridAdaptiveMinWidth;

			// The scroll-state bridge (ScrolledAway/ScrolledFromTop/LastScrolledBackward,
			// AnchorBottom, ScrollToBottom) attaches a LazyListState, which only the
			// LazyColumn accepts — the grid needs a rememberLazyGridState binding
			// (backlogged) and the row/carousel paths have never carried it. Skip the
			// observers entirely for those flavors instead of watching a detached state
			// that never changes.
			bool scrollBridge = !horizontal && gridMin <= 0;

			var listState = composer.RememberLazyListState();
			int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
			if (scrollBridge && !_scrollerRegistered)
			{
				_scrollerRegistered = true;
				var captured = listState;
				_list.RegisterScroller(() =>
				{
					int lastIndex = _list.Sections() > 0 ? System.Math.Max(0, _list.Rows(0) - 1) : 0;
					_ = captured.AnimateScrollToItemAsync(lastIndex);
				});
				_list.RegisterScrollTo((index, position, animate) =>
				{
					int itemCount = _list.Sections() > 0 ? _list.Rows(0) : 0;
					if (itemCount <= 0)
						return;

					index = System.Math.Clamp(index, 0, itemCount - 1);
					int scrollOffset = 0;
					if (position == ListScrollPosition.Center && FrameHeight > 0)
					{
						var targetExtent = GetRow(index).MeasureExtent(RowWidth).Height;
						// Centered selector lists have half-viewport content padding so the
						// boundary rows can reach the viewport center. Compose's scroll offset
						// is measured from that padded content start, so use half the target
						// extent instead of applying the viewport-center offset a second time.
						var desiredOffset = _centerContentPadding is not null
							? targetExtent / 2
							: System.Math.Max(0, FrameHeight / 2 - targetExtent / 2);
						scrollOffset = (int)System.Math.Round(desiredOffset * ComposeNode.Density);
					}

					_ = animate
						? captured.AnimateScrollToItemAsync(index, scrollOffset)
						: captured.ScrollToItemAsync(index, scrollOffset);
				});
			}

			// snapshotFlow tracks canScrollForward: true when the newest messages are below the
			// viewport (user has scrolled up). LaunchedEffect(true) starts once and cancels when
			// the list leaves the composition.
			var capturedList = _list;
			var capturedState = listState;
			if (scrollBridge)
			{
				composer.LaunchedEffect(true, async ct =>
				{
					// AnchorBottom (chat log): open at the newest message — the gold Jetchat's
					// reverseLayout initial position; instant so launch doesn't visibly scroll.
					// Ordinary lists (inbox) open at the top.
					int lastIndex = capturedList.Sections() > 0 ? System.Math.Max(0, capturedList.Rows(0) - 1) : 0;
					if (capturedList.InitialScrollIndex < 0 &&
						capturedList.AnchorBottom &&
						lastIndex > 0)
						await capturedState.ScrollToItemAsync(lastIndex);

					await foreach (var away in ComposeExtensions.SnapshotFlow(() => capturedState.CanScrollForward)
						.WithCancellation(ct))
					{
						var signal = _list.ScrolledAway;   // re-read: owner re-points _list on re-render
						Comet.ThreadHelper.RunOnMainThread(() => signal.Value = away);
					}
				});

				// Top-relative twin: canScrollBackward = content above the viewport. Drives
				// Reply's ExtendedFAB (expanded at the top, contracted once scrolled).
				composer.LaunchedEffect(2, async ct =>
				{
					await foreach (var away in ComposeExtensions.SnapshotFlow(() => capturedState.CanScrollBackward)
						.WithCancellation(ct))
					{
						var signal = _list.ScrolledFromTop;   // re-read: owner re-points _list on re-render
						Comet.ThreadHelper.RunOnMainThread(() => signal.Value = away);
					}
				});

				// Scroll DIRECTION: lastScrolledBackward — the gold FAB re-expands on any upward
				// scroll (ReplyListContent.kt:124-125), not only at the very top.
				composer.LaunchedEffect(3, async ct =>
				{
					await foreach (var backward in ComposeExtensions.SnapshotFlow(() => capturedState.LastScrolledBackward)
						.WithCancellation(ct))
					{
						var signal = _list.LastScrolledBackward;   // re-read: owner re-points _list on re-render
						Comet.ThreadHelper.RunOnMainThread(() => signal.Value = backward);
					}
				});
			}

			// Single-section (the common case); multi-section flattening is a follow-up.
			var indices = Enumerable.Range(0, count).ToList();

			// Under Yoga, lay each row out to the list's arranged width so rows render identically
			// to the rest of the tree (and to iOS). FrameWidth is 0 until the engine arranges this
			// list, so fall back to the screen width.
			bool yoga = HasFrame;
			double rowWidth = RowWidth;

			// Drop the cache when the rows or the width change (otherwise we'd render stale layout).
			if (version != _cachedVersion || rowWidth != _cachedWidth)
			{
				ReleaseRows();
				_cachedVersion = version;
				_cachedWidth = rowWidth;
			}

			// Adaptive grid (Jetcaster Home): cells share the row width across the column
			// count Compose's GridCells.Adaptive will compute — mirror that math so the
			// Yoga-laid cell content matches the rendered cell box. (gridMin is already 0
			// for horizontal lists — the flavor precedence is resolved once, up top.)
			int gridColumns = gridMin > 0 ? System.Math.Max(1, (int)(rowWidth / gridMin)) : 0;

			// Carousel item width: the stated Dp, or (when the caller forgot it) the
			// FIRST item's intrinsic width — a 0 here would Yoga-lay every item at zero
			// and hand the M3 carousel a 0dp itemWidth (an invisible carousel).
			double carouselWidth = 0;
			if (horizontal && _list.Carousel != ListCarousel.None && count > 0)
			{
				carouselWidth = _list.CarouselItemWidth > 0
					? _list.CarouselItemWidth
					: GetRow(0).MeasureIntrinsicExtent().Width;
			}

			ComposableNode BuildRow(int i)
			{
				// First time this row is needed: build, materialize, and Yoga-lay-out once, then cache.
				var row = GetRow(i);
				if (yoga && !row.IsArranged)
				{
					// Vertical rows fill the list's width; grid cells the computed column
					// width; carousel items their (resolved) item width; LazyRow items
					// their own intrinsic width (a fixed-size card).
					double w = horizontal && carouselWidth > 0 ? carouselWidth
						: horizontal ? row.MeasureIntrinsicExtent().Width
						: gridColumns > 0 ? rowWidth / gridColumns
						: rowWidth;
					row.Layout(w);
				}
				return (ComposableNode)row.Node!;
			}

			PaddingValues? contentPadding = null;
			int initialIndex = System.Math.Clamp(_list.InitialScrollIndex, 0, System.Math.Max(0, count - 1));
			double edgeSpacing = scrollBridge
				? ListInitialScrollState.EdgeSpacing(
					FrameHeight,
					_list.InitialScrollPosition)
				: 0;
			if (_list.InitialScrollIndex >= 0 && edgeSpacing > 0)
			{
				// Match the reference CollectionView contract: symmetric half-viewport
				// start/end space lets the first and last rows reach the viewport center.
				if (_centerContentPadding is null ||
					System.Math.Abs(edgeSpacing - _centerContentPaddingExtent) > 0.5)
				{
					_centerContentPadding?.Dispose();
					_centerContentPadding = new PaddingValues(
						top: new Dp((float)edgeSpacing),
						bottom: new Dp((float)edgeSpacing));
					_centerContentPaddingExtent = edgeSpacing;
				}
				contentPadding = _centerContentPadding;
			}

			double targetExtent = 0;
			if (scrollBridge &&
				count > 0 &&
				_list.InitialScrollIndex >= 0 &&
				_list.InitialScrollPosition == ListScrollPosition.Center &&
				FrameHeight > 0)
			{
				_ = BuildRow(initialIndex);
				targetExtent = GetRow(initialIndex).MeasureExtent(rowWidth).Height;
			}

			if (scrollBridge &&
				_initialScroll.TrySchedule(
					FrameHeight,
					count,
					_list.InitialScrollIndex,
					_list.InitialScrollPosition,
					targetExtent,
					out var initialPlan))
			{
				int scrollOffset = (int)System.Math.Round(
					initialPlan.TargetOffset * ComposeNode.Density);
				var initialState = listState;
				composer.LaunchedEffect(initialPlan.Generation, initialPlan.Index, async ct =>
				{
					if (!_initialScroll.IsCurrent(initialPlan.Generation))
						return;
					await initialState.ScrollToItemAsync(
						initialPlan.Index,
						initialPlan.Position == ListScrollPosition.Center ? scrollOffset : 0,
						ct);
				});
			}

			// Horizontal: the REAL Compose LazyRow, or the real M3 carousel the gold
			// uses (Jetcaster followed-podcasts / top-podcasts rows).
			if (horizontal)
			{
				ComposableNode row = _list.Carousel switch
				{
					ListCarousel.MultiBrowse => new AndroidX.Compose.HorizontalMultiBrowseCarousel<int>(
						indices, (float)carouselWidth, BuildRow),
					ListCarousel.Uncontained => new AndroidX.Compose.HorizontalUncontainedCarousel<int>(
						indices, (float)carouselWidth, BuildRow),
					_ => new AndroidX.Compose.LazyRow<int>(indices, BuildRow),
				};
				row.Modifier = BuildNodeModifier();
				row.Render(composer);
				return;
			}

			// Adaptive grid: the REAL LazyVerticalGrid (full-span header rows are a
			// follow-up — Jetcaster's headers land as ordinary cells until spans bind).
			if (gridColumns > 0)
			{
				var grid = new AndroidX.Compose.LazyVerticalGrid<int>(
					AndroidX.Compose.GridCells.Adaptive((float)gridMin), indices, BuildRow);
				((ComposableNode)grid).Modifier = BuildNodeModifier();
				grid.Render(composer);
				return;
			}

			var lazy = new ComposeLazyColumn(indices, BuildRow)
			{
				State = listState,
				ContentPadding = contentPadding,
				SnapToCenter = _list.SnapToCenter,
			};

			// Position + size the list from its Yoga frame (offset below the top bar, sized to the
			// remaining height) so it scrolls within its slot rather than laying out at the origin.
			((ComposableNode)lazy).Modifier = BuildNodeModifier();
			lazy.Render(composer);
		}

		public override void Dispose()
		{
			_initialScroll.Dispose();
			ReleaseRows();
			_centerContentPadding?.Dispose();
			_centerContentPadding = null;
			base.Dispose();
		}
	}
}
#endif
