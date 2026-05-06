#nullable enable
using System;
using System.Windows;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Handlers
{
	public abstract partial class WPFViewHandler<TVirtualView, TPlatformView> : ViewHandler<TVirtualView, TPlatformView>
		where TVirtualView : class, IView
		where TPlatformView : class
	{

		public override void PlatformArrange(Microsoft.Maui.Graphics.Rect rect)
		{
			var platformView = (ContainerView ?? PlatformView) as UIElement;

			if (platformView == null)
				return;

			if (rect.Width < 0 || rect.Height < 0)
				return;

			var wpfRect = new global::System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height);

			if (platformView.Dispatcher.CheckAccess())
			{
				platformView.Arrange(wpfRect);
			}
			else
			{
				platformView.Dispatcher.BeginInvoke(() => platformView.Arrange(wpfRect));
			}

			this.Invoke(nameof(IView.Frame), rect);
		}

		public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			var platformView = (ContainerView ?? PlatformView) as FrameworkElement;

			if (platformView == null)
				return Graphics.Size.Zero;

			if (widthConstraint < 0 || heightConstraint < 0)
				return Graphics.Size.Zero;

			// GetDesiredSize must run on UI thread since it calls Measure
			if (!platformView.Dispatcher.CheckAccess())
			{
				return platformView.Dispatcher.Invoke(() => MeasureOnThread(platformView, widthConstraint, heightConstraint));
			}

			return MeasureOnThread(platformView, widthConstraint, heightConstraint);
		}

		static Graphics.Size MeasureOnThread(FrameworkElement platformView, double widthConstraint, double heightConstraint)
		{
			widthConstraint = AdjustForExplicitSize(widthConstraint, platformView.Width);
			heightConstraint = AdjustForExplicitSize(heightConstraint, platformView.Height);

			var measureConstraint = new global::System.Windows.Size(widthConstraint, heightConstraint);

			platformView.Measure(measureConstraint);

			return new Graphics.Size(platformView.DesiredSize.Width, platformView.DesiredSize.Height);
		}

		static double AdjustForExplicitSize(double externalConstraint, double explicitValue)
		{
			// Even with an explicit value specified, Windows will limit the size of the control to 
			// the size of the parent's explicit size. Since we want our controls to get their
			// explicit sizes regardless (and possibly exceed the size of their layouts), we need
			// to measure them at _at least_ their explicit size.

			if (double.IsNaN(explicitValue))
			{
				// NaN for an explicit height/width on Windows means "unspecified", so we just use the external value
				return externalConstraint;
			}

			// If the control's explicit height/width is larger than the containers, use the control's value
			return Math.Max(externalConstraint, explicitValue);
		}

		//protected override void SetupContainer()
		//{
		//	if (PlatformView == null || ContainerView != null)
		//		return;

		//	var oldParent = (Panel?)PlatformView.Parent;

		//	var oldIndex = oldParent?.Children.IndexOf(PlatformView);
		//	oldParent?.Children.Remove(PlatformView);

		//	ContainerView ??= new WrapperView();
		//	((WrapperView)ContainerView).Child = PlatformView;

		//	if (oldIndex is int idx && idx >= 0)
		//		oldParent?.Children.Insert(idx, ContainerView);
		//	else
		//		oldParent?.Children.Add(ContainerView);
		//}

		//protected override void RemoveContainer()
		//{
		//	if (PlatformView == null || ContainerView == null || PlatformView.Parent != ContainerView)
		//	{
		//		CleanupContainerView(ContainerView);
		//		ContainerView = null;
		//		return;
		//	}

		//	var oldParent = (Panel?)ContainerView.Parent;

		//	var oldIndex = oldParent?.Children.IndexOf(ContainerView);
		//	oldParent?.Children.Remove(ContainerView);

		//	CleanupContainerView(ContainerView);
		//	ContainerView = null;

		//	if (oldIndex is int idx && idx >= 0)
		//		oldParent?.Children.Insert(idx, PlatformView);
		//	else
		//		oldParent?.Children.Add(PlatformView);

		//	void CleanupContainerView(FrameworkElement? containerView)
		//	{
		//		if (containerView is WrapperView wrapperView)
		//		{
		//			wrapperView.Child = null;
		//			wrapperView.Dispose();
		//		}
		//	}
		//}
		protected WPFViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null) : base(mapper, commandMapper)
		{
		}
	}
}