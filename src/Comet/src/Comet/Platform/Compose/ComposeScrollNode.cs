#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Renders a Comet vertical <c>ScrollView</c> as a Compose <c>Box</c> with a
	/// <c>verticalScroll</c> modifier. The scroll owns its content (so it implements
	/// <see cref="IBackendManagesOwnContent"/>): it lays the single content view out with the
	/// shared Yoga engine — width pinned to the viewport, height wrapped to the content — then
	/// hosts that taller-than-viewport content in the scrollable box. This is the non-virtualized
	/// counterpart of <see cref="ComposeListNode"/>, for screens that scroll as one piece
	/// (settings, profile, article detail) rather than a lazy row list.
	/// </summary>
	sealed class ComposeScrollNode : ComposeNode, IBackendReconcilesOwnContent
	{
		Comet.ScrollView _scroll;
		readonly ScrollState _scrollState = new();
		readonly ScrollOwnerStateBridge _scrollSignals;
		readonly OwnedContentSlot<ComposeNode> _content;
		readonly MutableState<int> _contentVersion = new(0);
		bool _disposed;

		public ComposeScrollNode(Comet.ScrollView scroll, BackendContext context)
		{
			_scroll = scroll;
			_scrollSignals = new ScrollOwnerStateBridge(scroll);
			_content = new OwnedContentSlot<ComposeNode>(scroll, context);
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		/// <summary>Re-point at the replacement ScrollView after its live content has been
		/// reconciled by the bridge. The retained native child follows the replacement logical
		/// view; a structurally new child is materialized lazily on the next composition.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (_disposed || newView is not Comet.ScrollView scroll)
				return;
			_scroll = scroll;
			_scrollSignals.TransferOwner(scroll);
			_content.TransferOwner(newView);

			var children = _scroll.GetChildren();
			var contentView = children is { Count: > 0 } ? children[0] : null;
			if (contentView is null)
			{
				_content.Clear();
				_contentVersion.Value++;
				return;
			}

			var rendered = contentView.GetView() ?? contentView;
			if (rendered.Node is not ComposeNode current ||
				!_content.RetainTransferred(current, contentView))
				_content.Clear();
			_contentVersion.Value++;
		}

		bool EnsureContent(out ComposeNode? node, out View? contentView)
		{
			if (_content.TryGet(out node, out contentView))
				return true;
			if (_content.IsDisposed)
				return false;

			var children = _scroll.GetChildren();
			contentView = children is { Count: > 0 } ? children[0] : null;
			if (contentView is null)
				return false;

			// Reuse a node transferred by the logical content diff.
			var rendered = contentView.GetView() ?? contentView;
			if (rendered.Node is ComposeNode existing &&
				_content.RetainTransferred(existing, contentView))
			{
				node = existing;
				return true;
			}

			node = _content.Materialize(contentView);
			return true;
		}

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;
			if (!EnsureContent(out var content, out var contentView))
				return;

			double width = FrameWidth > 0
				? FrameWidth
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / ComposeNode.Density;

			if (HasFrame)
				CometBackendLayoutEngine.LayoutContent(contentView!, width);

			var box = new Box();
			var modifier = (BuildNodeModifier() ?? Modifier.Companion).VerticalScroll(_scrollState);
			((ComposableNode)box).Modifier = modifier;
			box.Add(content!);
			((ComposableNode)box).Render(composer);

			// Continuously marshal the scroll offset (Dp) + at-top flag to the ScrollView's signals: a
			// parallax header translates with the offset, and overlays (ProfileFab) extend/contract from
			// at-top. SnapshotFlow tracks the live scrollState.value (px); LaunchedEffect(true) starts once
			// and cancels when the scroll leaves the composition. Mirrors ComposeListNode's scroll bridge.
			var captured = _scrollState;
			composer.LaunchedEffect(true, async ct =>
			{
				await foreach (var px in ComposeExtensions.SnapshotFlow(() => captured.Value).WithCancellation(ct))
				{
					double dp = px / ComposeNode.Density;
					_scrollSignals.Publish(dp, px == 0);
				}
			});
		}

		public override void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			_scrollSignals.Dispose();
			_content.Dispose();
			base.Dispose();
		}

		/// <summary>Exposes the retained ScrollState so DevFlow scroll injection can
		/// drive the offset programmatically.</summary>
		internal ScrollState GetScrollState() => _scrollState;
	}
}
#endif
