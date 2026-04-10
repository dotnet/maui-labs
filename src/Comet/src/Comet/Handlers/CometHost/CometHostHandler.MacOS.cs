using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers;

public partial class CometHostHandler : ViewHandler<CometHost, CometHostHandler.CometHostNSContainerView>
{
	public CometHostHandler() : base(CometHostMapper) { }

	protected override CometHostNSContainerView CreatePlatformView()
		=> new CometHostNSContainerView();

	protected override void ConnectHandler(CometHostNSContainerView platformView)
	{
		base.ConnectHandler(platformView);
		UpdateCometView();
	}

	protected override void DisconnectHandler(CometHostNSContainerView platformView)
	{
		platformView.ClearContent();
		base.DisconnectHandler(platformView);
	}

	public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
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
			var renderView = cometView.GetView();
			IView viewToRender = (renderView is not null && renderView != cometView) ? renderView : cometView;

			var platformView = viewToRender.ToMacOSPlatform(MauiContext);
			if (platformView is not null)
				PlatformView.SetContent(platformView, viewToRender);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CometHostHandler.MacOS] UpdateCometView failed: {ex}");
		}
	}

	public class CometHostNSContainerView : NSView
	{
		NSView _contentView;
		IView _virtualView;

		public CometHostNSContainerView()
		{
			WantsLayer = true;
		}

		public void SetContent(NSView platformView, IView virtualView)
		{
			_contentView?.RemoveFromSuperview();
			_contentView = platformView;
			_virtualView = virtualView;
			if (_contentView is not null)
			{
				_contentView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
				AddSubview(_contentView);
				NeedsLayout = true;
			}
		}

		public void ClearContent()
		{
			_contentView?.RemoveFromSuperview();
			_contentView = null;
			_virtualView = null;
		}

		public override void Layout()
		{
			base.Layout();
			if (_contentView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
				return;

			_virtualView?.Measure(Bounds.Width, Bounds.Height);
			_virtualView?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height));
			_contentView.Frame = Bounds;
		}
	}
}
