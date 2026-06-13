#nullable enable
#if ANDROID
using System;
using Android.Content;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Hosts a Comet view tree as a single root Jetpack Compose composition. Builds the
	/// retained <see cref="ComposeNode"/> tree from the Comet view via
	/// <see cref="CometBackendBridge"/>, then drives it through one
	/// <see cref="ComposeView"/> set as the activity/root content.
	/// </summary>
	public sealed class ComposeBackendRoot
	{
		readonly BackendContext _context;
		ComposeNode? _root;

		public ComposeBackendRoot(IServiceProvider services)
			=> _context = new BackendContext(services);

		/// <summary>Materializes <paramref name="view"/> into a Compose tree and returns the
		/// hosting <see cref="ComposeView"/> to set as content.</summary>
		public ComposeView CreateView(Context context, View view)
		{
			_root = (ComposeNode)CometBackendBridge.Materialize(view, _context);

			var composeView = new ComposeView(context);
			composeView.SetContent(_ => _root);
			return composeView;
		}
	}
}
#endif
