#nullable enable
#if IOS
using System;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	/// <summary>iOS counterpart of <see cref="ComposeSelectorPanelNode"/>: the gold Jetchat expandable
	/// input-selector panel. Hosts one-of-N materialized child views selected by
	/// <see cref="PropertyIds.SelectorPanel_Index"/> (nothing when collapsed), and measures to the active
	/// panel's natural height so the shared Yoga engine grows the footer (and shrinks the message list) —
	/// the same layout-driven swap as Android. The reflow is driven by <see cref="SwiftUINavigationNode"/>
	/// re-laying-out its top screen on <c>ReactiveScheduler.AfterFlush</c> (the SelectorPanel backend
	/// schedules the flush on a selector change).</summary>
	sealed class SwiftUISelectorPanelNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly SelectorPanel _panel;
		readonly BackendContext _context;
		readonly CometNode _native;
		ISwiftUINativeNode?[] _nodes = Array.Empty<ISwiftUINativeNode?>();
		int _selectorValue;
		int _hosted = -1;     // index of the child currently inserted into _native (-1 = none)
		double _width;
		bool _initialized;

		public CometNode Native => _native;

		public SwiftUISelectorPanelNode(SelectorPanel panel, BackendContext context)
		{
			_panel = panel;
			_context = context;
			// A Yoga-container host: with a frame it overlays its (self-positioning) child at topLeading,
			// exactly like the list/nav hosts. Collapsed = no child + zero measured height.
			_native = CometSwiftUIHost.MakeNode("vstack");
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.SelectorPanel_Index)
			{
				_selectorValue = value.AsInt;
				UpdateHostedChild();
			}
			else if (id == PropertyIds.BackgroundColor && value.AsColor is { } c)
			{
				// The panel surface (gold Surface(tonalElevation = 8.dp)); only visible while a panel is
				// open (collapsed → zero height → nothing drawn).
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));
			}
		}

		void EnsureContent()
		{
			if (_initialized)
				return;
			_initialized = true;
			var panels = _panel.Panels;
			_nodes = new ISwiftUINativeNode?[panels.Count];
			for (int i = 0; i < panels.Count; i++)
				if (panels[i] is { } v)
					_nodes[i] = (ISwiftUINativeNode)CometBackendBridge.Materialize(v, _context, _panel);
		}

		View? ActiveView()
		{
			var panels = _panel.Panels;
			return _selectorValue > 0 && _selectorValue < panels.Count ? panels[_selectorValue] : null;
		}

		// Show the active panel (or nothing when collapsed / on a dialog-handled selector with a null slot).
		// Swapping the hosted child changes this node's measured height; the nav's AfterFlush reflow then
		// re-measures the screen so the footer grows/collapses.
		void UpdateHostedChild()
		{
			EnsureContent();
			int active = _selectorValue > 0 && _selectorValue < _nodes.Length && _nodes[_selectorValue] is not null
				? _selectorValue
				: -1;
			if (active == _hosted)
				return;
			_hosted = active;
			CometSwiftUIHost.ClearChildren(_native);
			if (active >= 0)
			{
				CometSwiftUIHost.InsertChild(_native, 0, _nodes[active]!.Native);
				LayoutActive();
			}
		}

		void LayoutActive()
		{
			if (_width > 0 && ActiveView() is { } v)
				CometBackendLayoutEngine.LayoutContent(v, _width);
		}

		// Own-content leaf: report the active panel's height (laying its subtree out to the host width so it
		// renders positioned) so Yoga grows the footer; zero when collapsed → the slot disappears.
		public Size Measure(double widthConstraint, double heightConstraint)
		{
			EnsureContent();
			var view = ActiveView();
			if (view is null)
				return Size.Zero;
			double width = double.IsInfinity(widthConstraint) || widthConstraint <= 0
				? UIScreen.MainScreen.Bounds.Width
				: widthConstraint;
			return CometBackendLayoutEngine.LayoutContent(view, width);
		}

		public void Arrange(Rect frame)
		{
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);
			if (frame.Width > 0 && Math.Abs(frame.Width - _width) > 0.5)
			{
				_width = frame.Width;
				LayoutActive();
			}
		}

		// The node manages its own (one-of-N) content; the generic child API is unused.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() { }

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);
	}
}
#endif
