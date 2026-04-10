using System;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Comet.Handlers
{
	public partial class NativeHostHandler : ViewHandler<NativeHost, NativeHostHandler.NativeHostContainerView>, INativeHostHandler
	{
		UIView hostedNativeView;
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
				if (nativeView is not UIView uiView)
					throw new InvalidOperationException($"NativeHost requires a UIView on iOS/macCatalyst. Actual type: {nativeView?.GetType().FullName ?? "null"}");

				hostedNativeView = uiView;
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

			PlatformView.SetNeedsLayout();
			PlatformView.InvalidateIntrinsicContentSize();
		}

		void TearDownHostedView(NativeHostContainerView platformView)
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

			var fitSize = hostedNativeView.SizeThatFits(new CGSize(
				double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
				double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height));

			var size = new Size(fitSize.Width, fitSize.Height);
			var intrinsic = hostedNativeView.IntrinsicContentSize;

			if (size.Width <= 0 && intrinsic.Width > 0)
				size.Width = intrinsic.Width;
			if (size.Height <= 0 && intrinsic.Height > 0)
				size.Height = intrinsic.Height;
			if (size.Width <= 0 && !double.IsInfinity(availableSize.Width))
				size.Width = availableSize.Width;
			if (size.Height <= 0 && !double.IsInfinity(availableSize.Height))
				size.Height = availableSize.Height;
			if (size.Height <= 0)
				size.Height = 44;

			return size;
		}

		public class NativeHostContainerView : UIView
		{
			UIView hostedView;

			public void SetHostedView(UIView platformView)
			{
				if (hostedView == platformView)
					return;

				hostedView?.RemoveFromSuperview();
				hostedView = platformView;
				if (hostedView?.Superview is not null && hostedView.Superview != this)
					hostedView.RemoveFromSuperview();
				if (hostedView is not null)
				{
					hostedView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
					AddSubview(hostedView);
					SetNeedsLayout();
				}
			}

			public void ClearHostedView()
			{
				hostedView?.RemoveFromSuperview();
				hostedView = null;
			}

			public override void LayoutSubviews()
			{
				base.LayoutSubviews();
				if (hostedView is not null)
					hostedView.Frame = Bounds;
			}

			public override CGSize SizeThatFits(CGSize size)
			{
				if (hostedView is not null)
					return hostedView.SizeThatFits(size);
				return base.SizeThatFits(size);
			}
		}
	}
}
