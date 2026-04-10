using System;
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AView = global::Android.Views.View;
using AViewGroup = global::Android.Views.ViewGroup;

namespace Comet.Handlers
{
	public partial class NativeHostHandler : ViewHandler<NativeHost, NativeHostHandler.NativeHostContainerView>, INativeHostHandler
	{
		AView hostedNativeView;
		object sourceToken;
		bool ownsNativeView;
		NativeHost connectedHost;

		public NativeHostHandler() : base(Mapper)
		{
		}

		protected override NativeHostContainerView CreatePlatformView()
			=> new NativeHostContainerView(Context);

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
				if (nativeView is not AView androidView)
					throw new InvalidOperationException($"NativeHost requires an Android.Views.View on Android. Actual type: {nativeView?.GetType().FullName ?? "null"}");

				hostedNativeView = androidView;
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

			PlatformView.RequestLayout();
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

			var density = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
			hostedNativeView.Measure(
				CreateMeasureSpec(availableSize.Width, density),
				CreateMeasureSpec(availableSize.Height, density));

			var size = new Size(hostedNativeView.MeasuredWidth / density, hostedNativeView.MeasuredHeight / density);
			if (size.Width <= 0 && !double.IsInfinity(availableSize.Width))
				size.Width = availableSize.Width;
			if (size.Height <= 0 && !double.IsInfinity(availableSize.Height))
				size.Height = availableSize.Height;
			if (size.Height <= 0)
				size.Height = 44;

			return size;
		}

		static int CreateMeasureSpec(double availableSize, float density)
		{
			if (double.IsInfinity(availableSize) || availableSize <= 0)
				return AView.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);

			return AView.MeasureSpec.MakeMeasureSpec((int)Math.Ceiling(availableSize * density), MeasureSpecMode.AtMost);
		}

		public class NativeHostContainerView : FrameLayout
		{
			AView hostedView;

			public NativeHostContainerView(global::Android.Content.Context context) : base(context)
			{
			}

			public void SetHostedView(AView platformView)
			{
				if (hostedView == platformView)
					return;

				ClearHostedView();
				hostedView = platformView;
				if (hostedView.Parent is AViewGroup parent)
					parent.RemoveView(hostedView);
				AddView(hostedView, new FrameLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.MatchParent));
			}

			public void ClearHostedView()
			{
				if (hostedView is not null)
					RemoveView(hostedView);
				hostedView = null;
			}

			protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
			{
				base.OnLayout(changed, left, top, right, bottom);
				hostedView?.Layout(0, 0, right - left, bottom - top);
			}
		}
	}
}
