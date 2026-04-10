using System;
using CoreGraphics;
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Comet.Handlers;

/// <summary>
/// iOS handler for CometHost. Renders the Comet View's body content (typically a MauiViewHost)
/// directly by calling GetView() then ToPlatform() on the rendered content.
/// </summary>
public partial class CometHostHandler : ViewHandler<CometHost, CometHostHandler.CometHostContainerView>
{
	public CometHostHandler() : base(CometHostMapper) { }

	protected override CometHostContainerView CreatePlatformView()
		=> new CometHostContainerView();

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
		// Delegate to CometHost.CrossPlatformMeasure which measures the actual Comet content.
		// This is critical for CollectionView cells which must auto-size to their content.
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
			
			// Get the render view (body content) to avoid CometViewHandler handler circularity
			var renderView = cometView.GetView();
			IView viewToRender = (renderView is not null && renderView != cometView) ? renderView : cometView;
			
			var platformView = viewToRender.ToPlatform(MauiContext);
			if (platformView is not null)
				PlatformView.SetContent(platformView, viewToRender, cometView, MauiContext);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CometHostHandler] UpdateCometView failed: {ex.Message}");
		}
	}

	public class CometHostContainerView : UIView
	{
		UIView _contentView;
		IView _virtualView;
		Comet.View _rootCometView;
		IMauiContext _mauiContext;

		public CometHostContainerView()
		{
			ClipsToBounds = true;
		}

		public void SetContent(UIView platformView, IView virtualView, Comet.View rootCometView = null, IMauiContext mauiContext = null)
		{
			_contentView?.RemoveFromSuperview();
			_contentView = platformView;
			_virtualView = virtualView;
			if (rootCometView is not null)
				_rootCometView = rootCometView;
			if (mauiContext is not null)
				_mauiContext = mauiContext;
			if (_contentView is not null)
			{
				_contentView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
				AddSubview(_contentView);
				SetNeedsLayout();
			}
		}

		public void ClearContent()
		{
			_contentView?.RemoveFromSuperview();
			_contentView = null;
			_virtualView = null;
			_rootCometView = null;
			_mauiContext = null;
		}

		public override void LayoutSubviews()
		{
			base.LayoutSubviews();
			if (_contentView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
				return;

			// Re-resolve the virtual view from the root Comet View in case its
			// body was rebuilt (state change). Without this, _virtualView references
			// a disposed Grid and resize layout breaks.
			if (_rootCometView is not null)
			{
				var currentView = _rootCometView.GetView();
				if (currentView is not null && currentView != _virtualView)
					_virtualView = currentView;
			}

			_virtualView?.Measure(Bounds.Width, Bounds.Height);
			_virtualView?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height));
			_contentView.Frame = Bounds;
			_contentView.SetNeedsLayout();
			_contentView.LayoutIfNeeded();
		}

		public override CGSize SizeThatFits(CGSize size)
		{
			if (_virtualView is not null)
			{
				var measured = _virtualView.Measure(size.Width, size.Height);
				if (measured.Width > 0 && measured.Height > 0)
					return new CGSize(measured.Width, measured.Height);
			}
			return size;
		}
	}
}
