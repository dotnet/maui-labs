#nullable enable
#if IOS
using System.Collections.Generic;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Renders a Comet <c>ListView</c> as a SwiftUI <c>List</c> (the iOS counterpart of
	/// <c>ComposeListNode</c>). Owns its rows (so it implements
	/// <see cref="IBackendManagesOwnContent"/>): on a data-version change it materializes
	/// each row's template view into a child node of the native "list" node, which the
	/// shim renders via <c>List { ForEach … }</c> (SwiftUI lazily realizes row views).
	/// </summary>
	sealed class SwiftUIListNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly IListView _list;
		readonly BackendContext _context;
		readonly CometNode _native;
		readonly List<View> _rows = new();
		double _width;

		public CometNode Native => _native;

		public SwiftUIListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("list");

			// Drive ScrollToBottom (JumpToBottom FAB / after-send) through the native
			// ScrollViewReader — the iOS counterpart of the Compose LazyListState scroller.
			_list.RegisterScroller(() => CometSwiftUIHost.ScrollToBottom(_native));

			// The shim reports last-row visibility (0 = newest on screen / at bottom, 1 = scrolled away);
			// mirror it onto IListView.ScrolledAway so the JumpToBottom FAB shows/hides on scroll — the
			// iOS counterpart of ComposeListNode's snapshotFlow(CanScrollBackward). Also remember the
			// latest value so the initial seed can stop as soon as it lands (and not fight a user scroll).
			CometSwiftUIHost.SetScrollHandler(_native, away =>
			{
				_lastAway = away;
				_list.ScrolledAway.Value = away > 0.5;
			});

			// Top-relative twin (first-row visibility) → ScrolledFromTop, the iOS counterpart
			// of ComposeListNode's CanScrollBackward flow (Reply's ExtendedFAB collapse).
			CometSwiftUIHost.SetScrollTopHandler(_native, away =>
				_list.ScrolledFromTop.Value = away > 0.5);
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
				Rebuild();
		}

		void Rebuild()
		{
			// Drop the previous rows from the dev tree (they register under the ListView).
			if (_list is View listView)
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(listView, includeRoot: false);

			CometSwiftUIHost.ClearChildren(_native);
			_rows.Clear();
			int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
			for (int i = 0; i < count; i++)
			{
				var view = _list.ViewFor(0, i);
				var node = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, _context, _list as View);
				CometSwiftUIHost.InsertChild(_native, i, node.Native);
				_rows.Add(view);
				LayoutRow(view); // no-op until the list has been arranged (width known)
			}
		}

		// Lay each row out to the list's arranged width with the shared Yoga engine, height-wrapped,
		// so rows render identically to the Compose backend (avatar + author + wrapping body). Each
		// row's nodes self-position from the frames this pushes; the row root self-sizes for the List.
		void LayoutRow(View row)
		{
			if (_width > 0)
				CometBackendLayoutEngine.LayoutContent(row, _width);
		}

		// The node manages its own rows; the generic child API is unused.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

		public void Arrange(Rect frame)
		{
			// Position + size the List from its Yoga frame (below the top bar, filling the rest).
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

			// First time we learn our width (or it changed), (re)lay the rows out to it.
			if (frame.Width > 0 && System.Math.Abs(frame.Width - _width) > 0.5)
			{
				_width = frame.Width;
				foreach (var row in _rows)
					LayoutRow(row);

				// AnchorBottom (chat log): open at the newest message — the iOS twin of
				// ComposeListNode's one-shot ScrollToItem(last) seed. Nudged a few times
				// because a ScrollViewReader scroll to a far target undershoots while rows
				// are still realizing. Ordinary lists (inbox) open at the top.
				if (_list.AnchorBottom && !_seededToNewest && _rows.Count > 0)
				{
					_seededToNewest = true;
					SeedToNewest();
				}
			}
		}

		bool _seededToNewest;
		bool _disposed;
		double _lastAway = 1;   // 1 = not at bottom yet

		// Seed the list at the newest message (iOS twin of ComposeListNode's one-shot
		// ScrollToItem(last)). A ScrollViewReader scroll to a far target undershoots while rows
		// are still realizing, so nudge a few times — but STOP as soon as the shim reports we've
		// landed at the bottom (_lastAway low) or the node is torn down, so it doesn't re-scroll
		// redundantly or yank a user who scrolled up during the window.
		async void SeedToNewest()
		{
			try
			{
				foreach (var delay in new[] { 350, 900, 1700 })
				{
					await System.Threading.Tasks.Task.Delay(delay);
					if (_disposed || _lastAway <= 0.5)
						return;
					ThreadHelper.RunOnMainThread(() =>
					{
						if (!_disposed)
							CometSwiftUIHost.ScrollToBottom(_native);
					});
				}
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[SwiftUIListNode] seed-scroll failed: {ex.Message}");
			}
		}

		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() => _disposed = true;
	}
}
#endif
