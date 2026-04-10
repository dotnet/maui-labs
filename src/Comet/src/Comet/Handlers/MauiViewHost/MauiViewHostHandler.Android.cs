using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AView = global::Android.Views.View;
using AFrameLayout = global::Android.Widget.FrameLayout;
using AViewGroup = global::Android.Views.ViewGroup;

namespace Comet.Handlers
{
	/// <summary>
	/// Android handler for MauiViewHost. Creates a container FrameLayout that hosts
	/// the MAUI Controls platform view directly.
	/// </summary>
	public partial class MauiViewHostHandler : ViewHandler<MauiViewHost, AFrameLayout>
	{
		public static IPropertyMapper<MauiViewHost, MauiViewHostHandler> Mapper =
			new PropertyMapper<MauiViewHost, MauiViewHostHandler>(ViewHandler.ViewMapper);

		public MauiViewHostHandler() : base(Mapper) { }

		private AView _hostedPlatformView;

		protected override AFrameLayout CreatePlatformView()
			=> new AFrameLayout(Context);

		protected override void ConnectHandler(AFrameLayout platformView)
		{
			base.ConnectHandler(platformView);
			UpdateHostedView();
		}

		protected override void DisconnectHandler(AFrameLayout platformView)
		{
			if (VirtualView?.HostedView?.Handler is IElementHandler hostedHandler)
			{
				hostedHandler.DisconnectHandler();
				if (hostedHandler is IDisposable disposableHandler)
					disposableHandler.Dispose();
			}
			if (_hostedPlatformView is not null)
			{
				platformView.RemoveView(_hostedPlatformView);
				if (_hostedPlatformView is IDisposable disposable)
					disposable.Dispose();
				_hostedPlatformView = null;
			}
			base.DisconnectHandler(platformView);
		}

		void UpdateHostedView()
		{
			if (VirtualView?.HostedView is null || MauiContext is null)
				return;

			if (_hostedPlatformView is not null)
				PlatformView.RemoveView(_hostedPlatformView);

			try
			{
				_hostedPlatformView = VirtualView.HostedView.ToPlatform(MauiContext);
			}
			catch (Exception)
			{
				// Handler not found — try CometApp's MauiContext (has third-party handlers)
				var fallbackCtx = CometApp.MauiContext;
				if (fallbackCtx is not null && fallbackCtx != MauiContext)
				{
					try
					{
						_hostedPlatformView = VirtualView.HostedView.ToPlatform(fallbackCtx);
					}
					catch (Exception ex2)
					{
						System.Diagnostics.Debug.WriteLine(
							$"[MauiViewHostHandler] All ToPlatform failed for {VirtualView.HostedView.GetType().Name}: {ex2.Message}");
						return;
					}
				}
				else
				{
					return;
				}
			}

			if (_hostedPlatformView is not null)
			{
				PlatformView.AddView(_hostedPlatformView,
					new AFrameLayout.LayoutParams(
						AViewGroup.LayoutParams.MatchParent,
						AViewGroup.LayoutParams.MatchParent));
			}
		}
	}
}
