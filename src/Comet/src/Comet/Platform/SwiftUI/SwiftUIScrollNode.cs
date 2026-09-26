#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Renders a Comet vertical <c>ScrollView</c> as a SwiftUI <c>ScrollView</c> (the iOS
	/// counterpart of <see cref="ComposeScrollNode"/>). Owns its single content view (so it
	/// implements <see cref="IBackendManagesOwnContent"/>): it lays the content out with the
	/// shared Yoga engine — width pinned to the viewport, height wrapped — so the content
	/// self-positions and scrolls as one piece, matching the Compose backend.
	/// </summary>
	sealed class SwiftUIScrollNode : ICometBackendNode, IBackendReconcilesOwnContent, ISwiftUINativeNode
	{
		Comet.ScrollView _scroll;
		readonly CometNode _native;
		readonly ScrollOwnerStateBridge _scrollSignals;
		readonly OwnedContentSlot<ICometBackendNode> _content;
		double _width;
		bool _disposed;

		public CometNode Native => _native;

		public SwiftUIScrollNode(Comet.ScrollView scroll, BackendContext context)
		{
			_scroll = scroll;
			_native = CometSwiftUIHost.MakeNode("scroll");
			_scrollSignals = new ScrollOwnerStateBridge(scroll);
			_content = new OwnedContentSlot<ICometBackendNode>(scroll, context);
			// Mirror the live scroll offset onto the ScrollView's AtTop / ScrollOffset signals (the gold's
			// derivedStateOf { scrollState.value == 0 }) — e.g. collapsing the profile FAB on scroll.
			CometSwiftUIHost.SetScrollHandler(_native, OnNativeScroll);
			BuildContent();
		}

		void OnNativeScroll(double offset)
			=> _scrollSignals.Publish(offset, offset <= 1.0);

		void BuildContent()
		{
			var children = _scroll.GetChildren();
			var contentView = children is { Count: > 0 } ? children[0] : null;
			if (contentView is null)
				return;

			var node = (ISwiftUINativeNode)_content.Materialize(contentView);
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
		}

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (_disposed || newView is not Comet.ScrollView scroll)
				return;

			_scroll = scroll;
			_scrollSignals.TransferOwner(scroll);
			_content.TransferOwner(newView);
			var children = _scroll.GetChildren();
			var contentView = children is { Count: > 0 } ? children[0] : null;
			var rendered = contentView?.GetView() ?? contentView;
			var currentNode = rendered?.Node;

			if (contentView is null)
			{
				if (_content.TryGet(out _, out _))
					CometSwiftUIHost.RemoveChild(_native, 0);
				_content.Clear();
				return;
			}

			if (currentNode is not null &&
				_content.RetainTransferred(currentNode, contentView))
			{
				if (_width > 0)
					CometBackendLayoutEngine.LayoutContent(contentView, _width);
				return;
			}

			if (_content.TryGet(out _, out _))
				CometSwiftUIHost.RemoveChild(_native, 0);
			_content.Clear();
			BuildContent();
			if (_width > 0)
				CometBackendLayoutEngine.LayoutContent(contentView, _width);
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }

		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

		public void Arrange(Rect frame)
		{
			if (_disposed)
				return;

			// Frame the scroll viewport from its Yoga slot (below the bar, filling the rest).
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

			// (Re)lay the content out to the viewport width once we know it; it wraps taller than
			// the viewport and its children self-position, so SwiftUI's ScrollView scrolls it.
			if (frame.Width > 0 &&
				System.Math.Abs(frame.Width - _width) > 0.5 &&
				_content.View is { } contentView)
			{
				_width = frame.Width;
				CometBackendLayoutEngine.LayoutContent(contentView, _width);
			}
		}

		// Content is managed internally; the generic child API is unused.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;

			_scrollSignals.Dispose();
			_content.Dispose();
		}
	}
}
#endif
