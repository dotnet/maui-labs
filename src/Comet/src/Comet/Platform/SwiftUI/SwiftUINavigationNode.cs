#nullable enable
#if IOS
using System.Collections.Generic;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Renders a Comet <c>NavigationView</c> as a SwiftUI navigation stack (the iOS
	/// counterpart of <c>ComposeNavigationNode</c>): the C# side owns the screen stack, and
	/// the top screen is materialized as this node's single child, which the shim's
	/// "navigation" kind renders. <c>Navigate</c> pushes and <c>Pop</c> pops, re-rendering.
	/// </summary>
	sealed class SwiftUINavigationNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly BackendContext _context;
		readonly NavigationView _nav;
		readonly CometNode _native;
		readonly List<View> _stack = new();
		View? _shown;

		public CometNode Native => _native;

		public SwiftUINavigationNode(NavigationView nav, BackendContext context)
		{
			_context = context;
			_nav = nav;
			_native = CometSwiftUIHost.MakeNode("navigation");

			if (nav.Content is { } root)
				_stack.Add(root);

			nav.SetPerformNavigate(view =>
			{
				_stack.Add(view);
				ShowTop();
			});
			nav.SetPerformPop(() =>
			{
				if (_stack.Count > 1)
				{
					_stack.RemoveAt(_stack.Count - 1);
					ShowTop();
				}
			});

			ShowTop();
		}

		void ShowTop()
		{
			// Drop the previous screen's nodes from the dev tree before swapping it out.
			if (_shown is { } prev)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(prev, includeRoot: true);
				_shown = null;
			}

			CometSwiftUIHost.ClearChildren(_native);
			if (_stack.Count == 0)
				return;
			var top = _stack[_stack.Count - 1];
			// Register the screen under the NavigationView so the dev tree nests correctly.
			var node = (ISwiftUINativeNode)CometBackendBridge.Materialize(top, _context, _nav);
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
			_shown = top;

			// Lay the screen out full-screen with the shared Yoga engine (mirrors ComposeNavigationNode):
			// without this the screen's whole subtree has no Yoga frame and falls back to native SwiftUI
			// layout (which centers on the cross axis), instead of the Yoga-arranged absolute frames.
			var b = UIScreen.MainScreen.Bounds;
			CometBackendLayoutEngine.Layout(top, new Size(b.Width, b.Height));
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() { }
	}
}
#endif
