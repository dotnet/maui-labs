using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class NativeHostHandler : ViewHandler<NativeHost, NativeHostHandler.NativeHostNSContainerView>, INativeHostHandler
	{
		NSView hostedNativeView;
		object sourceToken;
		bool ownsNativeView;
		NativeHost connectedHost;

		public NativeHostHandler() : base(Mapper)
		{
		}

		protected override NativeHostNSContainerView CreatePlatformView()
			=> new NativeHostNSContainerView();

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			UpdateHostedView();
		}

		protected override void ConnectHandler(NativeHostNSContainerView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateHostedView();
		}

		protected override void DisconnectHandler(NativeHostNSContainerView platformView)
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
				if (nativeView is not NSView nsView)
					throw new InvalidOperationException(
						$"NativeHost requires an NSView on macOS. Actual type: {nativeView?.GetType().FullName ?? "null"}");

				hostedNativeView = nsView;
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

			PlatformView.NeedsLayout = true;
			PlatformView.InvalidateIntrinsicContentSize();
		}

		void TearDownHostedView(NativeHostNSContainerView platformView)
		{
			if (hostedNativeView is null)
				return;

			var releasedView = hostedNativeView;
			connectedHost?.ApplyDisconnected(releasedView);
			platformView?.ClearHostedView();
			if (ownsNativeView)
				releasedView.Dispose();
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

			var intrinsic = hostedNativeView.IntrinsicContentSize;
			var w = intrinsic.Width > 0 ? (double)intrinsic.Width : (double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width);
			var h = intrinsic.Height > 0 ? (double)intrinsic.Height : (double.IsInfinity(availableSize.Height) ? 44 : availableSize.Height);

			return new Size(w, h);
		}

		public class NativeHostNSContainerView : NSView
		{
			NSView _hostedView;

			public NativeHostNSContainerView() { WantsLayer = true; }

			public void SetHostedView(NSView platformView)
			{
				if (_hostedView == platformView)
					return;

				_hostedView?.RemoveFromSuperview();
				_hostedView = platformView;
				if (_hostedView?.Superview is not null && _hostedView.Superview != this)
					_hostedView.RemoveFromSuperview();
				if (_hostedView is not null)
				{
					_hostedView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
					AddSubview(_hostedView);
					NeedsLayout = true;
				}
			}

			public void ClearHostedView()
			{
				_hostedView?.RemoveFromSuperview();
				_hostedView = null;
			}

			public override void Layout()
			{
				base.Layout();
				if (_hostedView is not null)
					_hostedView.Frame = Bounds;
			}
		}
	}
}
