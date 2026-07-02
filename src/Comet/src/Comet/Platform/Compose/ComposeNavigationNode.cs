#nullable enable
#if ANDROID
using System.Collections.Generic;
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
	sealed class ComposeNavigationNode : ComposeNode, IBackendManagesOwnContent
	{
		NavigationView _nav;
		readonly BackendContext _context;
		readonly List<View> _stack = new();
		// Each screen is materialized + laid out once and kept while it's on the stack, so pushing
		// a screen doesn't re-materialize the ones beneath it (and popping back preserves their state).
		readonly Dictionary<View, ComposableNode> _screens = new();
		readonly MutableState<int> _version = new(0);

		public ComposeNavigationNode(NavigationView nav, BackendContext context)
		{
			_nav = nav;
			_context = context;

			// Root screen = the NavigationView's content (already has Navigation/Parent set).
			if (nav.Content is { } root)
				_stack.Add(root);

			// Comet's Navigate/Pop drive the stack; bump the version to recompose.
			nav.SetPerformNavigate(view =>
			{
				_stack.Add(view);
				_version.Value++;
			});
			nav.SetPerformPop(() =>
			{
				if (_stack.Count > 1)
				{
					var popped = _stack[_stack.Count - 1];
					_stack.RemoveAt(_stack.Count - 1);
					_screens.Remove(popped);   // it's gone — don't keep it cached
					_version.Value++;
				}
			});

			// Re-lay-out the current screen after every reactive flush so a hosted view whose intrinsic
			// size changed (e.g. the input-selector panel expanding) reflows — the top-level RunLayout
			// can't reach here because this node is own-content (a leaf to the engine), so the screen
			// it hosts must drive its own reflow. Arrange only recomposes the nodes that actually moved.
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowTopScreen;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		/// <summary>A (hot) reload swapped the view tree: re-point at the new NavigationView,
		/// reset the stack to its content, and drop the materialized screens (old tree).
		/// The new view's Navigate/Pop delegates were carried over by <c>UpdateFromOldView</c>'s
		/// NavigationView transfer, so they still drive this node.</summary>
		public override void OnOwnerViewChanged(View newView)
		{
			if (newView is not NavigationView nav)
				return;
			_nav = nav;
			_stack.Clear();
			_screens.Clear();
			if (nav.Content is { } root)
				_stack.Add(root);
			_version.Value++;
		}

		static Microsoft.Maui.Graphics.Size ScreenSizeDp()
		{
			// The live available size (shrinks when the soft keyboard resizes the window
			// under AdjustResize); DisplayMetrics fallback before the first layout.
			if (ComposeNode.AvailableSize is { Width: > 0, Height: > 0 } avail)
				return avail;
			var m = global::Android.Content.Res.Resources.System!.DisplayMetrics!;
			return new Microsoft.Maui.Graphics.Size(m.WidthPixels / ComposeNode.Density, m.HeightPixels / ComposeNode.Density);
		}

		void ReflowTopScreen()
		{
			if (_stack.Count == 0)
				return;
			var top = _stack[_stack.Count - 1];
			if (_screens.ContainsKey(top))   // only once it's been materialized + first-laid-out
				CometBackendLayoutEngine.Layout(top, ScreenSizeDp());
		}

		public override void Render(IComposer composer)
		{
			_ = _version.Value; // subscribe so push/pop recomposes
			if (_stack.Count == 0)
				return;

			var top = _stack[_stack.Count - 1];
			if (!_screens.TryGetValue(top, out var node))
			{
				// Materialize the screen, then lay it out full-screen with the Yoga engine (the
				// pushed screen owns the whole viewport, just like the root did).
				node = (ComposableNode)CometBackendBridge.Materialize(top, _context);
				CometBackendLayoutEngine.Layout(top, ScreenSizeDp());
				_screens[top] = node;
			}
			node.Render(composer);
		}
	}
}
#endif
