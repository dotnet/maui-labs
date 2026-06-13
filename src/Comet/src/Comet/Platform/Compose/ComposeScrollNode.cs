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
	sealed class ComposeScrollNode : ComposeNode, IBackendManagesOwnContent
	{
		readonly IContainerView _scroll;
		readonly BackendContext _context;
		readonly ScrollState _scrollState = new();
		ComposeNode? _content;
		View? _contentView;

		public ComposeScrollNode(IContainerView scroll, BackendContext context)
		{
			_scroll = scroll;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		ComposeNode? EnsureContent()
		{
			if (_content is not null)
				return _content;

			var children = _scroll.GetChildren();
			_contentView = children is { Count: > 0 } ? children[0] : null;
			if (_contentView is null)
				return null;

			_content = (ComposeNode)CometBackendBridge.Materialize(_contentView, _context);
			return _content;
		}

		public override void Render(IComposer composer)
		{
			var content = EnsureContent();
			if (content is null)
				return;

			// Width pinned to the arranged viewport (fall back to the screen until arranged).
			double width = FrameWidth > 0
				? FrameWidth
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / ComposeNode.Density;

			// Lay the content out taller-than-viewport; its children carry absolute frames and
			// self-position, so the box below just needs to host it and scroll.
			if (HasFrame)
				CometBackendLayoutEngine.LayoutContent(_contentView!, width);

			// Viewport frame (offset+size+background) then verticalScroll; the content box inside
			// is sized to its natural (taller) height by its own frame, so it scrolls.
			var box = new Box();
			var modifier = (BuildNodeModifier() ?? Modifier.Companion).VerticalScroll(_scrollState);
			((ComposableNode)box).Modifier = modifier;
			box.Add(content);
			((ComposableNode)box).Render(composer);
		}
	}
}
#endif
