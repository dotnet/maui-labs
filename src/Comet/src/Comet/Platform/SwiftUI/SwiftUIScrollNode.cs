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
	sealed class SwiftUIScrollNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly IContainerView _scroll;
		readonly BackendContext _context;
		readonly CometNode _native;
		View? _contentView;
		double _width;

		public CometNode Native => _native;

		public SwiftUIScrollNode(IContainerView scroll, BackendContext context)
		{
			_scroll = scroll;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("scroll");
			BuildContent();
		}

		void BuildContent()
		{
			var children = _scroll.GetChildren();
			_contentView = children is { Count: > 0 } ? children[0] : null;
			if (_contentView is null)
				return;

			var node = (ISwiftUINativeNode)CometBackendBridge.Materialize(_contentView, _context, _scroll as View);
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }

		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

		public void Arrange(Rect frame)
		{
			// Frame the scroll viewport from its Yoga slot (below the bar, filling the rest).
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

			// (Re)lay the content out to the viewport width once we know it; it wraps taller than
			// the viewport and its children self-position, so SwiftUI's ScrollView scrolls it.
			if (frame.Width > 0 && System.Math.Abs(frame.Width - _width) > 0.5 && _contentView is not null)
			{
				_width = frame.Width;
				CometBackendLayoutEngine.LayoutContent(_contentView, _width);
			}
		}

		// Content is managed internally; the generic child API is unused.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() { }
	}
}
#endif
