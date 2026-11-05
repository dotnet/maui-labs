#nullable enable
using System;
using System.Collections.Generic;

namespace Comet.Backend
{
	internal enum NavigationOwnerTransferAction
	{
		PreserveStack,
		SwitchStack,
		ResetStack,
	}

	internal readonly struct NavigationBackChromeState
	{
		public NavigationBackChromeState(bool isVisible, bool isEnabled, string title)
		{
			IsVisible = isVisible;
			IsEnabled = isEnabled;
			Title = title;
		}

		public bool IsVisible { get; }
		public bool IsEnabled { get; }
		public string Title { get; }

		public static NavigationBackChromeState Resolve(IReadOnlyList<View> stack)
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));

			var top = stack.Count > 0 ? stack[stack.Count - 1] : null;
			var behavior = top?.GetBackButtonBehavior();
			return new NavigationBackChromeState(
				stack.Count > 1 && (behavior?.IsVisible ?? true),
				behavior?.IsEnabled ?? true,
				behavior?.Title ?? string.Empty);
		}
	}

	internal static class NavigationStackLifecycle
	{
		public static bool ShouldRegisterSystemBackHandler(IReadOnlyList<View> stack)
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));
			if (stack.Count > 1)
				return true;
			if (stack.Count == 0)
				return false;

			var behavior = stack[0].GetBackButtonBehavior();
			return behavior is not null &&
				(!behavior.IsEnabled ||
					behavior.Command?.CanExecute(behavior.CommandParameter) == true);
		}

		public static NavigationOwnerTransferAction DetermineOwnerTransfer(
			NavigationView currentOwner,
			IReadOnlyList<View> stack,
			NavigationView replacement,
			bool isHotReload)
		{
			if (currentOwner is null)
				throw new ArgumentNullException(nameof(currentOwner));
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));
			if (replacement is null)
				throw new ArgumentNullException(nameof(replacement));
			if (isHotReload)
				return NavigationOwnerTransferAction.ResetStack;

			var currentKey = currentOwner.GetKey();
			var replacementKey = replacement.GetKey();
			var hasExplicitOwnerIdentity =
				!string.IsNullOrEmpty(currentKey) ||
				!string.IsNullOrEmpty(replacementKey);
			if (hasExplicitOwnerIdentity &&
				!string.Equals(currentKey, replacementKey, StringComparison.Ordinal))
				return NavigationOwnerTransferAction.SwitchStack;

			if (!hasExplicitOwnerIdentity &&
				!currentOwner.HasSameLogicalOwnerIdentity(replacement))
				return NavigationOwnerTransferAction.SwitchStack;

			if (!RequiresOwnerReset(stack, replacement, false))
				return NavigationOwnerTransferAction.PreserveStack;

			return NavigationOwnerTransferAction.ResetStack;
		}

		public static bool RequiresOwnerReset(
			IReadOnlyList<View> stack,
			NavigationView replacement,
			bool isHotReload)
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));
			if (replacement is null)
				throw new ArgumentNullException(nameof(replacement));
			if (isHotReload)
				return true;

			var currentRoot = stack.Count > 0 ? stack[0] : null;
			return !NavigationView.HasSameBackendRoot(currentRoot, replacement.Content);
		}

		public static bool TryPop(
			List<View> stack,
			Action<View, int> release,
			out View? popped)
		{
			if (stack.Count <= 1)
			{
				popped = null;
				return false;
			}

			var index = stack.Count - 1;
			popped = stack[index];
			stack.RemoveAt(index);
			release(popped, index);
			popped.Dispose();
			return true;
		}

		/// <summary>
		/// Makes the current stack entry safe to expose. Cached screens can keep their logical
		/// identity while a buried reactive update replaces bridge-managed content underneath
		/// them. Before native navigation reveals that screen, rematerialize any incomplete
		/// generation and always run layout so the exposed nodes already have current frames.
		/// </summary>
		public static TNode? PrepareCurrentForExposure<TNode>(
			IReadOnlyList<View> stack,
			Func<View, TNode?> currentNode,
			Func<View, TNode> rematerialize,
			Action<View> layout,
			out bool rematerialized)
			where TNode : class
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));
			if (currentNode is null)
				throw new ArgumentNullException(nameof(currentNode));
			if (rematerialize is null)
				throw new ArgumentNullException(nameof(rematerialize));
			if (layout is null)
				throw new ArgumentNullException(nameof(layout));

			rematerialized = false;
			if (stack.Count == 0)
				return null;

			var current = stack[stack.Count - 1];
			var node = currentNode(current);
			if (node is null || BackendContentReadiness.HasUnmaterializedBridgeContent(current))
			{
				node = rematerialize(current);
				rematerialized = true;
			}

			layout(current);
			return node;
		}

		public static View? ResetToRoot(
			List<View> stack,
			View? requestedRoot,
			Action<View, int> release)
		{
			var retainedRoot = stack.Count > 0 &&
				requestedRoot is not null &&
				NavigationView.HasSameBackendRoot(stack[0], requestedRoot)
					? stack[0]
					: requestedRoot;

			for (var index = stack.Count - 1; index >= 0; index--)
			{
				var page = stack[index];
				if (ReferenceEquals(page, retainedRoot))
					continue;

				stack.RemoveAt(index);
				release(page, index);
				page.Dispose();
			}

			stack.Clear();
			if (retainedRoot is not null)
				stack.Add(retainedRoot);
			return retainedRoot;
		}

		public static void DisposeAll(
			List<View> stack,
			Action<View, int> release)
		{
			for (var index = stack.Count - 1; index >= 0; index--)
			{
				var page = stack[index];
				stack.RemoveAt(index);
				release(page, index);
				page.Dispose();
			}
		}

		public static void DisposeRemovedOwnedPages(
			IReadOnlyList<View> previous,
			IReadOnlyList<View> replacement,
			NavigationView owner,
			Action<View, int> release)
		{
			if (previous is null)
				throw new ArgumentNullException(nameof(previous));
			if (replacement is null)
				throw new ArgumentNullException(nameof(replacement));
			if (owner is null)
				throw new ArgumentNullException(nameof(owner));
			if (release is null)
				throw new ArgumentNullException(nameof(release));

			for (var index = previous.Count - 1; index >= 0; index--)
			{
				var page = previous[index];
				if (replacement.Any(retained => ReferenceEquals(retained, page)) ||
					(page.Navigation is not null &&
					 !ReferenceEquals(page.Navigation, owner)))
					continue;

				release(page, index);
				page.Dispose();
			}
		}

		public static void DisposeOwnedPages(
			IReadOnlyList<View> stack,
			NavigationView owner,
			Action<View, int> release)
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));
			if (owner is null)
				throw new ArgumentNullException(nameof(owner));
			if (release is null)
				throw new ArgumentNullException(nameof(release));

			for (var index = stack.Count - 1; index >= 0; index--)
			{
				var page = stack[index];
				if (page.Navigation is not null &&
					!ReferenceEquals(page.Navigation, owner))
					continue;

				release(page, index);
				page.Dispose();
			}
		}
	}
}
