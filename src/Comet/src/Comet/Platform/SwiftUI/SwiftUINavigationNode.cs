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

			// Shrink the laid-out height when the keyboard is up so the footer rises above it
			// (SwiftUI's auto-avoidance doesn't reach our absolute-positioned nodes).
			CometSwiftUIKeyboard.EnsureStarted();
			CometSwiftUIKeyboard.Changed += RelayoutTop;

			// Re-lay-out the top screen after every reactive flush, so a nested own-content node that
			// changes its measured height (e.g. the input-selector panel growing the footer / shrinking the
			// message list) reflows. The global root reflow only re-lays the top-level layout root, not this
			// nav's owned subtree — mirrors ComposeNavigationNode's AfterFlush hook (the P3 discovery).
			Comet.Reactive.ReactiveScheduler.AfterFlush += RelayoutTop;

			ShowTop();
		}

		// Lay the current top screen out to the keyboard-adjusted height (full width, height minus
		// whatever the keyboard covers) — the single place the screen's available height is decided.
		void RelayoutTop()
		{
			if (_shown is not { } top)
				return;
			var b = UIScreen.MainScreen.Bounds;
			CometBackendLayoutEngine.Layout(top, new Size(b.Width, b.Height - CometSwiftUIKeyboard.Inset));
		}

		// Nodes materialized for the current top screen; disposed on the next swap so a
		// popped/replaced screen's nodes release their static hooks.
		List<ICometBackendNode>? _generation;

		void ShowTop()
		{
			// Hold flushes so the screen swap is atomic (see SwiftUIHostedCompositionNode.Refresh).
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			// Drop the previous screen's nodes from the dev tree before swapping it out.
			if (_shown is { } prev)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(prev, includeRoot: true);
				_shown = null;
			}
			if (_generation is { } stale)
			{
				_generation = null;
				foreach (var n in stale)
					n.Dispose();
			}

			CometSwiftUIHost.ClearChildren(_native);
			if (_stack.Count == 0)
				return;
			var top = _stack[_stack.Count - 1];
			// Register the screen under the NavigationView so the dev tree nests correctly.
			var generation = new List<ICometBackendNode>();
			ISwiftUINativeNode node;
			using (CometBackendBridge.CollectNodes(generation))
				node = (ISwiftUINativeNode)CometBackendBridge.Materialize(top, _context, _nav);
			_generation = generation;
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
			_shown = top;

			// Lay the screen out with the shared Yoga engine (mirrors ComposeNavigationNode): without
			// this the screen's whole subtree has no Yoga frame and falls back to native SwiftUI layout
			// (which centers on the cross axis). Height is keyboard-adjusted so the footer stays visible.
			RelayoutTop();
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= RelayoutTop;
			CometSwiftUIKeyboard.Changed -= RelayoutTop;
			if (_shown is { } shown)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(shown, includeRoot: true);
				_shown = null;
			}
			if (_generation is { } nodes)
			{
				_generation = null;
				foreach (var n in nodes)
					n.Dispose();
			}
		}
	}
}
#endif
