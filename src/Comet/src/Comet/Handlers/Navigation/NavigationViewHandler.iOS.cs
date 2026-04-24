using Comet.iOS;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Comet.Handlers
{
	public partial class NavigationViewHandler : ViewHandler<NavigationView, UIView>, IPlatformViewHandler
	{
		UIViewController viewController;
		CometViewController rootViewController;
		UIViewController IPlatformViewHandler.ViewController => viewController;
		protected override UIView CreatePlatformView()
		{
			var vc = new Comet.iOS.CometViewController { MauiContext = MauiContext, CurrentView = VirtualView.Content };
			rootViewController = vc;
			var nav = VirtualView;

			// Set title from NavigationView (content view may not carry the title)
			var navTitle = nav.GetTitle();
			if (!string.IsNullOrEmpty(navTitle))
				vc.Title = navTitle;

			if (nav.Navigation is not null)
			{
				viewController = vc;
				return viewController.View;
			}
			var navigationController = new CUINavigationController();
			viewController = navigationController;

			nav.SetPerformNavigate((toView) => {
				if (toView is NavigationView newNav)
				{
					newNav.SetPerformNavigate(nav);
					newNav.SetPerformPop(nav);
				}

				toView.Navigation = nav;
				var newVc = new Comet.iOS.CometViewController { MauiContext = MauiContext, CurrentView = toView };

				// Store the NavigationView reference so CometViewController can
				// apply toolbar items in ViewWillAppear (after Render has been called).
				newVc.NavigationViewRef = nav;
				navigationController.PushViewController(newVc, true);
			});
			nav.SetPerformPop(() => navigationController.PopViewController(true));
			nav.SetPerformContentReset((newContent) =>
			{
				navigationController.PopToRootViewController(false);
				vc.CurrentView = newContent;
				// Update title from the current NavigationView — the content view
				// may not carry the title in its own environment.
				var title = VirtualView?.GetTitle();
				if (!string.IsNullOrEmpty(title))
					vc.Title = title;
			});
			// Use inline (compact) title by default; opt in to large titles
			// when the NavigationView has NavigationPrefersLargeTitles=true.
			var prefersLargeTitles = nav.GetNavigationPrefersLargeTitles();
			navigationController.NavigationBar.PrefersLargeTitles = prefersLargeTitles;
			if (prefersLargeTitles)
				vc.NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;

			navigationController.PushViewController(vc, true);

			// Add leading bar button (hamburger icon) if configured
			if (nav.LeadingBarAction is not null)
			{
				var action = nav.LeadingBarAction;
				vc.NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
					nav.LeadingBarIcon ?? "☰",
					UIBarButtonItemStyle.Plain,
					(s, e) => action());
			}

			// Toolbar items are resolved in CometViewController.ViewWillAppear
			// (after the Component's Render has been called and BuiltView is available).
			// Set NavigationViewRef so it can fall back to NavigationView-level items.
			vc.NavigationViewRef = nav;

			return navigationController.View;
		}

		protected override void ConnectHandler(UIView platformView)
		{
			base.ConnectHandler(platformView);
			ApplyNavigationBarBackground();

			// When the handler is transferred to a new NavigationView (during diff),
			// the title must be refreshed from the current VirtualView.
			if (rootViewController is not null)
			{
				var title = VirtualView?.GetTitle();
				if (!string.IsNullOrEmpty(title))
					rootViewController.Title = title;
			}
		}

		void ApplyNavigationBarBackground()
		{
			if (viewController is CUINavigationController navController)
			{
				// Let CometViewController.ApplyStyle() handle the full nav bar appearance.
				// Resolve tint color dynamically from the view's environment so it
				// updates on theme change (no hardcoded light-mode colors).
				var textColor = VirtualView?.GetNavigationTextColor()?.ToPlatform();
				if (textColor is not null)
					navController.NavigationBar.TintColor = textColor;
				navController.NavigationBar.Translucent = true;
			}
		}

		internal static UIImage CreateFontIconImage(string glyph, string fontFamily, nfloat size)
		{
			var font = UIFont.FromName(fontFamily, size);
			if (font is null)
				return null;

			var text = new Foundation.NSString(glyph);
			var attributes = new UIStringAttributes { Font = font };
			var textSize = text.GetSizeUsingAttributes(attributes);
			if (textSize.Width <= 0 || textSize.Height <= 0)
				return null;

			UIGraphics.BeginImageContextWithOptions(textSize, false, 0);
			text.DrawString(CoreGraphics.CGPoint.Empty, attributes);
			var image = UIGraphics.GetImageFromCurrentImageContext();
			UIGraphics.EndImageContext();

			return image?.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
		}
	}
}
