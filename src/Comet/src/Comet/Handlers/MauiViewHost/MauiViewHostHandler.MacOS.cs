using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers
{
	public partial class MauiViewHostHandler : ViewHandler<MauiViewHost, MauiViewHostHandler.MauiViewHostNSContainerView>
	{
		public static IPropertyMapper<MauiViewHost, MauiViewHostHandler> Mapper =
			new PropertyMapper<MauiViewHost, MauiViewHostHandler>(ViewHandler.ViewMapper);

		public MauiViewHostHandler() : base(Mapper) { }

		protected override MauiViewHostNSContainerView CreatePlatformView()
			=> new MauiViewHostNSContainerView();

		protected override void ConnectHandler(MauiViewHostNSContainerView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateHostedView();
		}

		protected override void DisconnectHandler(MauiViewHostNSContainerView platformView)
		{
			if (VirtualView?.HostedView?.Handler is IElementHandler hostedHandler)
			{
				hostedHandler.DisconnectHandler();
				if (hostedHandler is IDisposable disposable)
					disposable.Dispose();
			}
			platformView.ClearHostedView();
			base.DisconnectHandler(platformView);
		}

		void UpdateHostedView()
		{
			if (VirtualView?.HostedView is null || MauiContext is null)
				return;

			try
			{
				var hostedPlatformView = VirtualView.HostedView.ToMacOSPlatform(MauiContext);
				if (hostedPlatformView is not null)
					PlatformView.SetHostedView(hostedPlatformView, VirtualView.HostedView);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[MauiViewHostHandler.MacOS] UpdateHostedView failed: {ex.Message}");
			}
		}

		public class MauiViewHostNSContainerView : NSView
		{
			NSView _hostedPlatformView;
			IView _hostedVirtualView;

			public MauiViewHostNSContainerView() { WantsLayer = true; }

			public void SetHostedView(NSView platformView, IView virtualView)
			{
				_hostedPlatformView?.RemoveFromSuperview();
				_hostedPlatformView = platformView;
				_hostedVirtualView = virtualView;
				if (_hostedPlatformView is not null)
				{
					AddSubview(_hostedPlatformView);
					NeedsLayout = true;
				}
			}

			public void ClearHostedView()
			{
				_hostedPlatformView?.RemoveFromSuperview();
				_hostedPlatformView = null;
				_hostedVirtualView = null;
			}

			public override void Layout()
			{
				base.Layout();
				if (_hostedPlatformView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
					return;

				var bounds = new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height);
				_hostedVirtualView?.Measure(Bounds.Width, Bounds.Height);
				_hostedVirtualView?.Arrange(bounds);
				_hostedPlatformView.Frame = Bounds;
			}

			public override CGSize IntrinsicContentSize
			{
				get
				{
					if (_hostedPlatformView is not null)
						return _hostedPlatformView.IntrinsicContentSize;
					return base.IntrinsicContentSize;
				}
			}
		}
	}
}
