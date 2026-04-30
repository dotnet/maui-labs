using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace Comet.Handlers
{
	public partial class NativeHostHandler : ViewHandler<NativeHost, NativeHostHandler.NativeHostContainerView>, INativeHostHandler
	{
		FrameworkElement hostedNativeView;
		object sourceToken;
		bool ownsNativeView;
		NativeHost connectedHost;

		public NativeHostHandler() : base(Mapper)
		{
		}

		protected override NativeHostContainerView CreatePlatformView()
			=> new NativeHostContainerView();

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			UpdateHostedView();
		}

		protected override void ConnectHandler(NativeHostContainerView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateHostedView();
		}

		protected override void DisconnectHandler(NativeHostContainerView platformView)
		{
			TearDownHostedView(platformView);
			base.DisconnectHandler(platformView);
		}

		object INativeHostHandler.GetNativeView() => hostedNativeView;

		void INativeHostHandler.SyncNativeView() => UpdateHostedView();

		Size INativeHostHandler.MeasureNativeView(Size availableSize) => MeasureHostedView(availableSize);

		void UpdateHostedView()
		{
			if (VirtualView is null || MauiContext is null || PlatformView is null)
				return;

			var currentToken = VirtualView.SourceToken;
			if (hostedNativeView is null || !Equals(sourceToken, currentToken))
			{
				TearDownHostedView(PlatformView);

				var nativeView = VirtualView.GetOrCreateNativeView(MauiContext);
				if (nativeView is not FrameworkElement frameworkElement)
					throw new InvalidOperationException($"NativeHost requires a FrameworkElement on Windows. Actual type: {nativeView?.GetType().FullName ?? "null"}");

				hostedNativeView = frameworkElement;
				sourceToken = currentToken;
				ownsNativeView = VirtualView.OwnsNativeView;
				connectedHost = VirtualView;
				PlatformView.SetHostedView(hostedNativeView);
				connectedHost.ApplyConnected(hostedNativeView, MauiContext);
			}
			else
			{
				connectedHost = VirtualView;
				connectedHost.ApplyUpdated(hostedNativeView, MauiContext);
			}

			PlatformView.InvalidateMeasure();
		}

		void TearDownHostedView(NativeHostContainerView platformView)
		{
			if (hostedNativeView is null)
				return;

			var releasedView = hostedNativeView;
			connectedHost?.ApplyDisconnected(releasedView);
			platformView?.ClearHostedView();
			if (ownsNativeView && releasedView is IDisposable disposable)
				disposable.Dispose();
			connectedHost?.ReleaseNativeView(releasedView, ownsNativeView);
			hostedNativeView = null;
			sourceToken = null;
			connectedHost = null;
			ownsNativeView = false;
		}

		Size MeasureHostedView(Size availableSize)
		{
			if (VirtualView is not null && VirtualView.TryMeasureOverride(availableSize, out var measured))
				return measured;

			if (hostedNativeView is null)
				return availableSize;

			hostedNativeView.Measure(new global::Windows.Foundation.Size(
				double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width,
				double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height));

			var desired = hostedNativeView.DesiredSize;
			var size = new Size(desired.Width, desired.Height);
			if (size.Width <= 0 && !double.IsInfinity(availableSize.Width))
				size.Width = availableSize.Width;
			if (size.Height <= 0 && !double.IsInfinity(availableSize.Height))
				size.Height = availableSize.Height;
			if (size.Height <= 0)
				size.Height = 44;

			return size;
		}

		public class NativeHostContainerView : WinGrid
		{
			FrameworkElement hostedView;

			public void SetHostedView(FrameworkElement platformView)
			{
				if (hostedView == platformView)
					return;

				ClearHostedView();
				hostedView = platformView;
				if (hostedView.Parent is Panel parent)
					parent.Children.Remove(hostedView);
				hostedView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
				hostedView.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
				Children.Add(hostedView);
			}

			public void ClearHostedView()
			{
				if (hostedView is not null)
					Children.Remove(hostedView);
				hostedView = null;
			}
		}
	}
}
