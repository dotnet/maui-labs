using System;
using Microsoft.Maui;
using UIKit;

namespace Comet.iOS
{
	public class CUINavigationController : UINavigationController
	{
		public static UIColor DefaultBarTintColor { get; private set; }
		public static UIColor DefaultTintColor { get; private set; }
		public static UIStringAttributes DefaultTitleTextAttributes { get; private set; }
		public CUINavigationController()
		{
			if (DefaultBarTintColor is null)
			{
				DefaultBarTintColor = NavigationBar.BarTintColor;
				DefaultTintColor = NavigationBar.TintColor;
				DefaultTitleTextAttributes = NavigationBar.TitleTextAttributes;
			}

			// Use clear so the window or parent background shows through the
			// status bar area instead of defaulting to opaque SystemBackground.
			View.BackgroundColor = UIColor.Clear;
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			// iOS requires the navigation controller's root view to carry
			// standard layout margins so the navigation bar (which inherits
			// via PreservesSuperviewLayoutMargins) positions the large title
			// with the correct leading indent (~16 pt on iPhone).
			// In our embedding (CUITabView → UITabBarController →
			// CUINavigationController) the system doesn't apply its default
			// minimum margins, so we set them explicitly.
			View.DirectionalLayoutMargins = new NSDirectionalEdgeInsets(0, 16, 0, 16);
		}
		public override UIViewController[] PopToRootViewController(bool animated)
		{
			return base.PopToRootViewController(animated);
		}
		public override UIViewController PopViewController(bool animated)
		{
			var vc = base.PopViewController(animated);
			var cometVC = vc as CometViewController;
			cometVC?.WasPopped();

			return vc;
		}
	}
}
