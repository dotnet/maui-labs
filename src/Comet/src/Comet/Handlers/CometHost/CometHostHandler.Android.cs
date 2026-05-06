using System;
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.HotReload;
using Microsoft.Maui.Platform;

namespace Comet.Handlers;

/// <summary>
/// Android handler for CometHost. Creates a container FrameLayout that hosts
/// the Comet View's rendered body content (typically a MauiViewHost).
/// </summary>
public partial class CometHostHandler : ViewHandler<CometHost, CometHostHandler.CometHostContainerView>
{
	public CometHostHandler() : base(CometHostMapper) { }

	protected override CometHostContainerView CreatePlatformView()
		=> new CometHostContainerView(Context);

	protected override void ConnectHandler(CometHostContainerView platformView)
	{
		base.ConnectHandler(platformView);
		UpdateCometView();
	}

	protected override void DisconnectHandler(CometHostContainerView platformView)
	{
		platformView.ClearContent();
		base.DisconnectHandler(platformView);
	}

	public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		if (VirtualView is IContentView contentView)
		{
			var size = contentView.CrossPlatformMeasure(widthConstraint, heightConstraint);
			if (size.Width > 0 && size.Height > 0)
				return size;
		}
		var w = double.IsInfinity(widthConstraint) ? 400 : widthConstraint;
		var h = double.IsInfinity(heightConstraint) ? 800 : heightConstraint;
		return new Microsoft.Maui.Graphics.Size(w, h);
	}

	void UpdateCometView()
	{
		if (VirtualView?.CometView is null || MauiContext is null)
			return;

		try
		{
			var cometView = VirtualView.CometView;

			// Give the container the context it needs to re-render on Reload()
			PlatformView.MauiContext = MauiContext;
			PlatformView.SetCometView(cometView);

			// Set the reload handler so Component.SetState → Reload() can
			// notify the host to re-render the platform view tree.
			if (cometView is Microsoft.Maui.HotReload.IHotReloadableView ihr)
			{
				ihr.ReloadHandler = PlatformView;
			}
			
			// Get the render view (body content) to avoid CometViewHandler handler circularity
			var renderView = cometView.GetView();
			IView viewToRender = (renderView is not null && renderView != cometView) ? renderView : cometView;
			
			var platformView = viewToRender.ToPlatform(MauiContext);
			if (platformView is not null)
				PlatformView.SetContent(platformView, viewToRender);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CometHostHandler] UpdateCometView failed: {ex.Message}");
		}
	}

	public class CometHostContainerView : FrameLayout, IReloadHandler
	{
		global::Android.Views.View _contentView;
		IView _virtualView;
		View _cometView;

		public CometHostContainerView(global::Android.Content.Context context) : base(context) { }

		internal IMauiContext MauiContext { get; set; }

		internal void SetCometView(View cometView)
		{
			_cometView = cometView;
		}

		public void SetContent(global::Android.Views.View platformView, IView virtualView)
		{
			if (_contentView is not null)
				RemoveView(_contentView);
			_contentView = platformView;
			_virtualView = virtualView;
			if (_contentView is not null)
			{
				AddView(_contentView, new FrameLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
			}
		}

		public void ClearContent()
		{
			if (_contentView is not null)
				RemoveView(_contentView);
			_contentView = null;
			_virtualView = null;
		}

		/// <summary>
		/// Called when the Comet view's state changes (Component.SetState → Reload).
		/// After ResetView diffs the virtual tree and transfers handlers, we just
		/// need to tell the platform to re-measure and re-layout with the updated
		/// virtual view tree. We also update the virtual view reference so
		/// measurement uses the current render output.
		/// </summary>
		public void Reload()
		{
			if (_cometView is null || MauiContext is null) return;

			var renderView = _cometView.GetView();
			IView viewToRender = (renderView is not null && renderView != _cometView) ? renderView : _cometView;

			// After ResetView's DiffUpdate, handlers have been transferred from
			// old to new views. The virtual tree is correct but the platform
			// ViewGroup children may be stale (pointing at old child instances).
			// Do a top-down sync of the entire platform view tree to match
			// the new virtual tree.
			var existingHandler = viewToRender.Handler;
			if (existingHandler?.PlatformView is global::Android.Views.View existingPlatformView)
			{
				if (existingPlatformView != _contentView)
				{
					SetContent(existingPlatformView, viewToRender);
				}
				else
				{
					_virtualView = viewToRender;
				}

				// Recursively sync platform children to match virtual tree
				SyncViewTreePlatformChildren(viewToRender);

				// Invalidate all measurements so the LayoutManager recalculates
				InvalidateViewTreeMeasurements(viewToRender);

				// Immediately re-measure and re-arrange the virtual view tree
				// so the LayoutManager assigns correct Frame positions BEFORE
				// the platform layout pass runs. Without this, children retain
				// stale positions from the previous render.
				var density = Context?.Resources?.DisplayMetrics?.Density ?? 1;
				var w = Width > 0 ? Width / density : Resources.DisplayMetrics.WidthPixels / density;
				var h = Height > 0 ? Height / density : Resources.DisplayMetrics.HeightPixels / density;
				viewToRender.Measure(w, h);
				viewToRender.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, w, h));

				// Now request the platform layout pass to apply the new positions
				_contentView?.RequestLayout();
				RequestLayout();
			}
			else
			{
				// No handler yet — need full platform view creation
				var platformView = viewToRender.ToPlatform(MauiContext);
				if (platformView is not null)
					SetContent(platformView, viewToRender);
			}
		}

		/// <summary>
		/// Recursively sync platform ViewGroup children to match the Comet
		/// virtual view tree. Called once after the entire diff completes.
		/// </summary>
		static void SyncViewTreePlatformChildren(IView view)
		{
			if (view is not Comet.ContainerView container) return;
			if (view.Handler is not ILayoutHandler layoutHandler) return;

			var virtualChildren = ((IContainerView)container).GetChildren();

			// Use MAUI's ILayoutHandler API (not raw ViewGroup manipulation)
			// so the handler's internal child list stays in sync with the
			// platform ViewGroup. This ensures the layout pass applies
			// correct positions to the correct platform children.
			layoutHandler.Clear();
			foreach (var child in virtualChildren)
			{
				if (child is not null)
					layoutHandler.Add(child);
			}

			// Recurse into children
			foreach (var child in virtualChildren)
			{
				if (child is not null)
					SyncViewTreePlatformChildren(child);
			}
		}

		static void InvalidateViewTreeMeasurements(IView view)
		{
			if (view is Comet.View cometView)
				cometView.InvalidateMeasurement();
			if (view is Comet.ContainerView container)
			{
				foreach (var child in ((IContainerView)container).GetChildren())
				{
					if (child is not null)
						InvalidateViewTreeMeasurements(child);
				}
			}
		}

		protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
		{
			base.OnLayout(changed, left, top, right, bottom);
			if (_contentView is null) return;

			var width = right - left;
			var height = bottom - top;
			if (width > 0 && height > 0 && _virtualView is not null)
			{
				var density = Context?.Resources?.DisplayMetrics?.Density ?? 1;
				var widthDp = width / density;
				var heightDp = height / density;
				_virtualView.Measure(widthDp, heightDp);
				_virtualView.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, widthDp, heightDp));
			}
		}
	}
}
