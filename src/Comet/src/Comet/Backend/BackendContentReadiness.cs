#nullable enable

namespace Comet.Backend
{
	internal static class BackendContentReadiness
	{
		/// <summary>
		/// Finds missing nodes in the bridge-managed tree. Own-content nodes are lifecycle
		/// boundaries: an unmaterialized child beneath one can be an intentionally inactive
		/// dialog, list, tab, or navigation slot and must not invalidate its containing screen.
		/// </summary>
		public static bool HasUnmaterializedBridgeContent(View root)
		{
			var rendered = root.GetView() ?? root;
			if (rendered.Node is null)
				return true;
			if (rendered.Node is IBackendManagesOwnContent)
				return false;
			if (rendered is not IContainerView container)
				return false;

			var children = container.GetChildren();
			for (var i = 0; i < children.Count; i++)
			{
				if (children[i] is { } child &&
					HasUnmaterializedBridgeContent(child))
					return true;
			}
			return false;
		}
	}
}
