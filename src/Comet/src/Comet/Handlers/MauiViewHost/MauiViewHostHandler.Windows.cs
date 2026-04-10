using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;
using WPanel = Microsoft.UI.Xaml.Controls.Panel;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;

namespace Comet.Handlers
{
	public partial class MauiViewHostHandler : ViewHandler<MauiViewHost, WGrid>
	{
		public static IPropertyMapper<MauiViewHost, MauiViewHostHandler> Mapper =
			new PropertyMapper<MauiViewHost, MauiViewHostHandler>(ViewHandler.ViewMapper);

		public MauiViewHostHandler() : base(Mapper) { }

		private WFrameworkElement _hostedPlatformView;

		protected override WGrid CreatePlatformView()
			=> new WGrid();

		protected override void ConnectHandler(WGrid platformView)
		{
			base.ConnectHandler(platformView);
			UpdateHostedView();
		}

		protected override void DisconnectHandler(WGrid platformView)
		{
			if (VirtualView?.HostedView?.Handler is IElementHandler hostedHandler)
			{
				hostedHandler.DisconnectHandler();
				if (hostedHandler is IDisposable disposableHandler)
					disposableHandler.Dispose();
			}
			if (_hostedPlatformView is not null)
			{
				platformView.Children.Remove(_hostedPlatformView);
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
				PlatformView.Children.Remove(_hostedPlatformView);

			try
			{
				_hostedPlatformView = VirtualView.HostedView.ToPlatform(MauiContext) as WFrameworkElement;
			}
			catch (Exception)
			{
				// Handler not found — try CometApp's MauiContext (has third-party handlers)
				var fallbackCtx = CometApp.MauiContext;
				if (fallbackCtx is not null && fallbackCtx != MauiContext)
				{
					try
					{
						_hostedPlatformView = VirtualView.HostedView.ToPlatform(fallbackCtx) as WFrameworkElement;
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
				_hostedPlatformView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
				_hostedPlatformView.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
				PlatformView.Children.Add(_hostedPlatformView);
			}
		}
	}
}
