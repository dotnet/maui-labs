#nullable enable
#if ANDROID
using System.Collections.Generic;
using System.Windows.Input;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Renders a Comet <c>NavigationView</c> as a Compose navigation stack: the top screen
	/// of the stack is composed, and Comet's <c>Navigate</c>/<c>Pop</c> push/pop and
	/// recompose. The node owns the stack (so it implements
	/// <see cref="IBackendManagesOwnContent"/> — the bridge doesn't materialize the
	/// NavigationView's content as a static child).
	/// </summary>
	sealed class ComposeNavigationNode : ComposeNode, IBackendRetainsLogicalContentOnOwnerTransfer
	{
		NavigationView _nav;
		readonly List<View> _stack = new();
		readonly OwnedContentSlot<ComposeNode> _screen;
		readonly MutableState<int> _version = new(0);
		View? _visibleTop;
		ICommand? _observedBackCommand;

		public ComposeNavigationNode(NavigationView nav, BackendContext context)
		{
			_nav = nav;
			_screen = new OwnedContentSlot<ComposeNode>(nav, context);

			_stack.AddRange(nav.GetBackendNavigationStack());

			AttachNavigation(nav);
			PrepareCurrentScreen(out _);
			SetVisibleTop(_stack.Count > 0 ? _stack[^1] : null);

			// Re-lay-out the current screen after every reactive flush so a hosted view whose intrinsic
			// size changed (e.g. the input-selector panel expanding) reflows — the top-level RunLayout
			// can't reach here because this node is own-content (a leaf to the engine), so the screen
			// it hosts must drive its own reflow. Arrange only recomposes the nodes that actually moved.
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowTopScreen;
		}

		void AttachNavigation(NavigationView nav)
		{
			// Comet's Navigate/Pop drive the stack; bump the version to recompose.
			nav.SetPerformNavigate(view =>
			{
				SetVisibleTop(null);
				_stack.Add(view);
				PrepareCurrentScreen(out _);
				SetVisibleTop(view);
				_version.Value++;
			});
			nav.SetPerformPop(() =>
			{
				if (_stack.Count > 1)
					SetVisibleTop(null);
				if (NavigationStackLifecycle.TryPop(
					_stack,
					(popped, _) => ReleaseRenderedScreen(popped),
					out _))
				{
					nav.SetBackendNavigationStack(_stack);
					PrepareCurrentScreen(out _);
					SetVisibleTop(_stack[^1]);
					_version.Value++;
				}
			});
			nav.SetPerformContentReset(ResetToRoot);
			nav.SetCurrentViewProvider(() => _stack.Count == 0 ? null! : _stack[_stack.Count - 1]);
		}

		void ResetToRoot(View root)
		{
			SetVisibleTop(null);
			var retainedRoot = NavigationStackLifecycle.ResetToRoot(
				_stack,
				root,
				(page, _) => ReleaseRenderedScreen(page));
			_nav.SetBackendNavigationStack(_stack);
			PrepareCurrentScreen(out _);
			SetVisibleTop(retainedRoot);
			_version.Value++;
		}

		void ReleaseRenderedScreen(View screen)
		{
			if (_screen.IsActive(screen))
				_screen.Clear();
		}

		void DeactivateRenderedGenerations()
		{
			SetVisibleTop(null);
			_screen.Clear();
		}

		void SetVisibleTop(View? next)
		{
			if (ReferenceEquals(_visibleTop, next))
			{
				ObserveBackCommand(next);
				return;
			}
			_visibleTop?.ViewDidDisappear();
			_visibleTop = next;
			ObserveBackCommand(next);
			_visibleTop?.ViewDidAppear();
		}

		public override void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowTopScreen;
			ObserveBackCommand(null);
			DeactivateRenderedGenerations();
			_screen.Dispose();
			NavigationStackLifecycle.DisposeAll(_stack, (_, _) => { });
			base.Dispose();
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		/// <summary>Returns the size this navigation node should lay its top screen to:
		/// the Yoga-arranged frame when available (the NavigationView is inside a grid row
		/// that gives it less than full screen), else the full viewport as a fallback
		/// before the first Arrange call.</summary>
		Microsoft.Maui.Graphics.Size ContentSize()
			=> HasFrame && FrameWidth > 0 && FrameHeight > 0
				? new Microsoft.Maui.Graphics.Size(FrameWidth, FrameHeight)
				: ScreenSizeDp();

		/// <summary>The node was transferred to a new NavigationView. Re-point always and
		/// preserve the stack when the root is unchanged; switching persistent navigation
		/// roots (for example a tab) resets to that root's own stack.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
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
				PrepareCurrentScreen(out _);
				SetVisibleTop(_stack.Count > 0 ? _stack[^1] : null);
				_version.Value++;
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
			PrepareCurrentScreen(out _);
			SetVisibleTop(_stack.Count > 0 ? _stack[^1] : null);
			_version.Value++;
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
			{
				_visibleTop = replacementRoot;
				ObserveBackCommand(replacementRoot);
			}
		}

		void ObserveBackCommand(View? view)
		{
			var command = view?.GetBackButtonBehavior()?.Command;
			if (ReferenceEquals(_observedBackCommand, command))
				return;
			if (_observedBackCommand is not null)
				_observedBackCommand.CanExecuteChanged -= OnBackCommandCanExecuteChanged;
			_observedBackCommand = command;
			if (_observedBackCommand is not null)
				_observedBackCommand.CanExecuteChanged += OnBackCommandCanExecuteChanged;
		}

		void OnBackCommandCanExecuteChanged(object? sender, System.EventArgs e)
			=> _version.Value++;

		void ReflowTopScreen()
		{
			if (PrepareCurrentScreen(out var rematerialized) is not null && rematerialized)
				_version.Value++;
		}

		ComposeNode MaterializeScreen(View view)
			=> _screen.Materialize(view);

		ComposeNode RematerializeScreen(View view)
			=> MaterializeScreen(view);

		ComposeNode? PrepareCurrentScreen(out bool rematerialized)
		{
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			return NavigationStackLifecycle.PrepareCurrentForExposure(
				_stack,
				view => _screen.TryGet(out var node, out var activeView) &&
					ReferenceEquals(activeView, view)
						? node
						: null,
				RematerializeScreen,
				view => CometBackendLayoutEngine.Layout(view, ContentSize()),
				out rematerialized);
		}

		public override void Render(IComposer composer)
		{
			_ = _version.Value; // subscribe so push/pop recomposes
			var node = PrepareCurrentScreen(out _);
			if (node is null)
				return;

			if (NavigationStackLifecycle.ShouldRegisterSystemBackHandler(_stack))
				new AndroidX.Compose.BackHandler(() => _nav.RequestBack()).Render(composer);
			node.Render(composer);
		}
	}
}
#endif
