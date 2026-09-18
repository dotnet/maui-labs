#nullable enable
#if IOS
using System;
using System.Collections.Generic;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Foundation;
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
	sealed class SwiftUINavigationNode : ICometBackendNode, IBackendRetainsLogicalContentOnOwnerTransfer, ISwiftUINativeNode
	{
		NavigationView _nav;
		readonly CometNode _native;
		readonly List<View> _stack = new();
		// NavigationStack needs one stable path host per logical entry. Only the top
		// host contains a materialized Comet generation; buried hosts stay empty.
		readonly List<CometNode> _screenHosts = new();
		readonly OwnedContentSlot<ICometBackendNode> _screen;
		View? _visibleTop;
		bool _activeNodeAttached;
		double _frameW;
		double _frameH;
		bool _hasFrame;

		public CometNode Native => _native;

		public SwiftUINavigationNode(NavigationView nav, BackendContext context)
		{
			_nav = nav;
			_native = CometSwiftUIHost.MakeNode("navigation");
			_screen = new OwnedContentSlot<ICometBackendNode>(nav, context);

			_stack.AddRange(nav.GetBackendNavigationStack());
			AttachNavigation(nav);
			ActivateStack();

			// Re-lay-out the top screen after every reactive flush, so a nested own-content node that
			// changes its measured height (e.g. the input-selector panel growing the footer / shrinking the
			// message list) reflows. The global root reflow only re-lays the top-level layout root, not this
			// nav's owned subtree — mirrors ComposeNavigationNode's AfterFlush hook (the P3 discovery).
			Comet.Reactive.ReactiveScheduler.AfterFlush += RelayoutTop;
		}

		void AttachNavigation(NavigationView nav)
		{
			CometSwiftUIHost.SetBackRequestHandler(_native, () => nav.RequestBack());
			nav.SetPerformNavigate(view =>
			{
				Push(view);
			});
			nav.SetPerformPop(() =>
			{
				if (_stack.Count > 1)
					PopTop();
			});
			nav.SetPerformContentReset(ResetStack);
			nav.SetCurrentViewProvider(() => _stack.Count == 0 ? null! : _stack[_stack.Count - 1]);
		}

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not NavigationView nav)
				return;
			var transfer = NavigationStackLifecycle.DetermineOwnerTransfer(
				_nav,
				_stack,
				nav,
				isHotReload);
			_nav.SetRetainsLogicalStackAfterOwnerTransfer(
				transfer == NavigationOwnerTransferAction.SwitchStack);
			nav.SetRetainsLogicalStackAfterOwnerTransfer(false);

			if (transfer == NavigationOwnerTransferAction.SwitchStack)
			{
				_nav.SetBackendNavigationStack(_stack);
				_nav.DetachBackendCallbacks();
				DeactivateRenderedGenerations();
				_stack.Clear();
				_nav = nav;
				_screen.TransferOwner(nav);
				AttachNavigation(nav);
				_stack.AddRange(nav.GetBackendNavigationStack());
				nav.SetBackendNavigationStack(_stack);
				ActivateStack();
				return;
			}

			if (transfer == NavigationOwnerTransferAction.PreserveStack)
			{
				if (_stack.Count > 0 && nav.Content is { } replacementRoot)
					TransferEquivalentRoot(_stack[0], replacementRoot);
				nav.SetBackendNavigationStack(_stack);
				if (!ReferenceEquals(_nav, nav))
					_nav.DetachBackendCallbacks();
				_nav = nav;
				_screen.TransferOwner(nav);
				AttachNavigation(nav);
				return;
			}

			if (!ReferenceEquals(_nav, nav))
				_nav.DetachBackendCallbacks();
			DeactivateRenderedGenerations();
			NavigationStackLifecycle.DisposeAll(_stack, (_, _) => { });
			_nav = nav;
			_screen.TransferOwner(nav);
			AttachNavigation(nav);
			if (!isHotReload)
				_stack.AddRange(nav.GetBackendNavigationStack());
			if (_stack.Count == 0 && nav.Content is { } root)
				_stack.Add(root);
			nav.SetBackendNavigationStack(_stack);
			ActivateStack();
		}

		void TransferEquivalentRoot(View oldRoot, View replacementRoot)
		{
			if (ReferenceEquals(oldRoot, replacementRoot))
				return;

			_stack[0] = replacementRoot;
			if (_screen.TryGet(out var node, out var activeRoot) &&
				ReferenceEquals(activeRoot, oldRoot))
				_screen.RetainTransferred(node!, replacementRoot);
			if (ReferenceEquals(_visibleTop, oldRoot))
				_visibleTop = replacementRoot;
		}

		void ResetStack(View? root)
		{
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			SetVisibleTop(null);
			DeactivateCurrentScreen();
			var retainedRoot = NavigationStackLifecycle.ResetToRoot(
				_stack,
				root,
				ReleaseRenderedScreen);
			EnsureScreenHosts();
			if (retainedRoot is not null)
				ActivateScreen(retainedRoot, 0);
			_nav.SetBackendNavigationStack(_stack);
			CometSwiftUIHost.SetDouble(_native, "navigationdepth", _stack.Count - 1);
			RelayoutTop();
			SetVisibleTop(retainedRoot);
		}

		void DeactivateRenderedGenerations()
		{
			SetVisibleTop(null);
			DeactivateCurrentScreen();
			while (_screenHosts.Count > 0)
				RemoveScreenHost(_screenHosts.Count - 1);
		}

		void ActivateStack()
		{
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			EnsureScreenHosts();
			if (_stack.Count > 0)
				ActivateScreen(_stack[^1], _stack.Count - 1);
			CometSwiftUIHost.SetDouble(_native, "navigationdepth", _stack.Count - 1);
			RelayoutTop();
			SetVisibleTop(_stack.Count > 0 ? _stack[^1] : null);
		}

		void EnsureScreenHosts()
		{
			while (_screenHosts.Count > _stack.Count)
				RemoveScreenHost(_screenHosts.Count - 1);
			while (_screenHosts.Count < _stack.Count)
			{
				var index = _screenHosts.Count;
				var host = CometSwiftUIHost.MakeNode("zstack");
				_screenHosts.Add(host);
				CometSwiftUIHost.InsertChild(_native, index, host);
			}
		}

		void RemoveScreenHost(int index)
		{
			CometSwiftUIHost.RemoveChild(_native, index);
			_screenHosts.RemoveAt(index);
		}

		void DeactivateCurrentScreen()
		{
			if (_activeNodeAttached && _screen.View is { } active)
			{
				var index = _stack.IndexOf(active);
				if (index >= 0 && index < _screenHosts.Count)
					CometSwiftUIHost.RemoveChild(_screenHosts[index], 0);
			}

			_activeNodeAttached = false;
			_screen.Clear();
		}

		ISwiftUINativeNode ActivateScreen(View view, int index)
		{
			DeactivateCurrentScreen();
			var node = (ISwiftUINativeNode)_screen.Materialize(view);
			CometSwiftUIHost.InsertChild(_screenHosts[index], 0, node.Native);
			_activeNodeAttached = true;
			return node;
		}

		// Lay the current top screen out to the arranged frame (or full-screen before the first
		// Arrange call). The native keyboard overlays the page, matching UIKit/MAUI Entry behavior;
		// text inputs provide their own keyboard-dismiss accessory.
		void RelayoutTop()
		{
			if (_stack.Count == 0)
				return;
			var top = _stack[_stack.Count - 1];
			ApplyBackButtonBehavior();
			var size = ContentSize();
			NavigationStackLifecycle.PrepareCurrentForExposure<ISwiftUINativeNode>(
				_stack,
				view => _screen.TryGet(out var node, out var activeView) &&
					ReferenceEquals(activeView, view)
						? node as ISwiftUINativeNode
						: null,
				RematerializeTopScreen,
				view => CometBackendLayoutEngine.Layout(view, size),
				out _);
			foreach (var host in _screenHosts)
				CometSwiftUIHost.SetFrame(host, 0, 0, size.Width, size.Height);
		}

		Size ContentSize()
		{
			double w, h;
			if (_hasFrame && _frameW > 0 && _frameH > 0)
			{
				w = _frameW;
				h = _frameH;
			}
			else
			{
				var b = UIScreen.MainScreen.Bounds;
				w = b.Width;
				h = b.Height;
			}
			if (h < 0) h = 0;
			return new Size(w, h);
		}

		void ApplyBackButtonBehavior()
		{
			var state = NavigationBackChromeState.Resolve(_stack);
			CometSwiftUIHost.SetBool(_native, "backvisible",
				state.IsVisible);
			CometSwiftUIHost.SetBool(_native, "backenabled", state.IsEnabled);
			CometSwiftUIHost.SetString(_native, "backtitle", state.Title);
		}

		void Push(View view)
		{
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			SetVisibleTop(null);
			DeactivateCurrentScreen();
			_stack.Add(view);
			EnsureScreenHosts();
			ActivateScreen(view, _stack.Count - 1);
			CometSwiftUIHost.SetDouble(_native, "navigationdepth", _stack.Count - 1);
			RelayoutTop();
			SetVisibleTop(view);
		}

		void PopTop()
		{
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			SetVisibleTop(null);
			DeactivateCurrentScreen();
			NavigationStackLifecycle.TryPop(_stack, ReleaseRenderedScreen, out _);
			EnsureScreenHosts();
			if (_stack.Count > 0)
				ActivateScreen(_stack[^1], _stack.Count - 1);
			CometSwiftUIHost.SetDouble(_native, "navigationdepth", _stack.Count - 1);
			_nav.SetBackendNavigationStack(_stack);
			RelayoutTop();
			SetVisibleTop(_stack.Count > 0 ? _stack[^1] : null);
		}

		void ReleaseRenderedScreen(View screen, int index)
		{
			if (_screen.IsActive(screen))
				DeactivateCurrentScreen();
			RemoveScreenHost(index);
		}

		ISwiftUINativeNode RematerializeTopScreen(View screen)
			=> ActivateScreen(screen, _stack.Count - 1);

		void SetVisibleTop(View? next)
		{
			if (ReferenceEquals(_visibleTop, next))
				return;
			_visibleTop?.ViewDidDisappear();
			_visibleTop = next;
			_visibleTop?.ViewDidAppear();
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame)
		{
			bool changed = !_hasFrame
				|| System.Math.Abs(frame.Width - _frameW) > 0.5
				|| System.Math.Abs(frame.Height - _frameH) > 0.5;
			_frameW = frame.Width;
			_frameH = frame.Height;
			_hasFrame = true;
			if (changed)
			{
				CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);
				RelayoutTop();
			}
		}
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= RelayoutTop;
			DeactivateRenderedGenerations();
			_screen.Dispose();
			NavigationStackLifecycle.DisposeAll(_stack, (_, _) => { });
		}
	}
}
#endif
