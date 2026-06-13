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
		readonly NavigationView _nav;
		readonly BackendContext _context;
		readonly List<View> _stack = new();
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
					_stack.RemoveAt(_stack.Count - 1);
					_version.Value++;
				}
			});
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		public override void Render(IComposer composer)
		{
			_ = _version.Value; // subscribe so push/pop recomposes
			if (_stack.Count == 0)
				return;

			var top = _stack[_stack.Count - 1];
			((ComposableNode)CometBackendBridge.Materialize(top, _context)).Render(composer);
		}
	}
}
#endif
