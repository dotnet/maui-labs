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
		View? _layoutRoot;
		Microsoft.Maui.Graphics.Size _availableDp;

		public ComposeBackendRoot(IServiceProvider services)
			=> _context = new BackendContext(services);

		/// <summary>When true, the C# Yoga engine computes layout and the Compose nodes are
		/// positioned absolutely from the computed frames. Default false (native layout).</summary>
		public bool UseYogaLayout { get; set; }

		/// <summary>Optional hook to wrap the composed root — e.g. in a <c>MaterialTheme</c> carrying
		/// the app's color scheme — so real Material controls (Button, Icon, ripples) pick up the
		/// theme. The app supplies this since the theme/colors are app-specific, not backend policy.</summary>
		public Func<AndroidX.Compose.ComposableNode, AndroidX.Compose.ComposableNode>? WrapContent { get; set; }

		/// <summary>Materializes <paramref name="view"/> into a Compose tree and returns the
		/// hosting <see cref="ComposeView"/> to set as content.</summary>
		public ComposeView CreateView(Context context, View view)
		{
			var metrics = context.Resources!.DisplayMetrics!;
			ComposeNode.Density = metrics.Density;

			_root = (ComposeNode)CometBackendBridge.Materialize(view, _context);

			if (UseYogaLayout)
			{
				_layoutRoot = view.HasContent ? view.GetView() : view;
				_availableDp = new Microsoft.Maui.Graphics.Size(
					metrics.WidthPixels / metrics.Density,
					metrics.HeightPixels / metrics.Density);
				RunLayout();
				// Reflow after each reactive flush (content size changes re-measure + re-arrange).
				Comet.Reactive.ReactiveScheduler.AfterFlush += RunLayout;
			}

			var composeView = new ComposeView(context);
			composeView.SetContent(_ => WrapContent is null ? _root : WrapContent(_root));
			return composeView;
		}

		void RunLayout()
		{
			if (_layoutRoot is not null)
				CometBackendLayoutEngine.Layout(_layoutRoot, _availableDp);
		}
	}
}
#endif
