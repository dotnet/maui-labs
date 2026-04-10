using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers
{
	public partial class NavigationViewHandler : ViewHandler<NavigationView, NSView>
	{
		NSView _contentContainer;
		NSView _currentPageView;

		protected override NSView CreatePlatformView()
		{
			_contentContainer = new NSView { WantsLayer = true };

			var nav = VirtualView;
			if (nav?.Content is not null)
			{
				var platformView = nav.Content.ToMacOSPlatform(MauiContext);
				_contentContainer.AddSubview(platformView);
				_currentPageView = platformView;
			}

			nav?.SetPerformNavigate((toView) =>
			{
				if (toView is NavigationView newNav)
				{
					newNav.SetPerformNavigate(nav);
					newNav.SetPerformPop(nav);
				}
				toView.Navigation = nav;
				ShowView(toView);
			});
			nav?.SetPerformPop(() =>
			{
				// Pop back to content root
				ShowView(nav.Content);
			});
			nav?.SetPerformContentReset((newContent) =>
			{
				ShowView(newContent);
			});

			return _contentContainer;
		}

		void ShowView(IView view)
		{
			if (view is null || MauiContext is null)
				return;

			_currentPageView?.RemoveFromSuperview();
			var platformView = view.ToMacOSPlatform(MauiContext);
			_contentContainer.AddSubview(platformView);
			_currentPageView = platformView;

			if (_contentContainer.Bounds.Width > 0)
				LayoutContent(_contentContainer.Bounds);
		}

		void LayoutContent(CGRect bounds)
		{
			if (_currentPageView is null || bounds.Width <= 0 || bounds.Height <= 0)
				return;

			_currentPageView.Frame = bounds;
			var currentView = VirtualView?.Content;
			if (currentView is IView iv)
			{
				iv.Measure((double)bounds.Width, (double)bounds.Height);
				iv.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
			}
		}
	}
}
