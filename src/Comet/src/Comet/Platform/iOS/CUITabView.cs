using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
namespace Comet.iOS
{
	public class CUITabView : UIView
	{
		public IMauiContext Context { get; set; }
		UITabBarController tabViewController = new UITabBarController();
		// Explicitly-set tab titles to re-apply after iOS overrides
		List<string> _explicitTabTitles;
		public CUITabView()
		{
			tabViewController.View.BackgroundColor = UIColor.Clear;
			Add(tabViewController.View);

			// Translucent tab bar so child VCs extend under it and
			// ScrollView content scrolls behind it.
			tabViewController.TabBar.Translucent = true;
			var appearance = new UITabBarAppearance();
			appearance.ConfigureWithTransparentBackground();
			appearance.BackgroundEffect = UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemUltraThinMaterial);
			tabViewController.TabBar.StandardAppearance = appearance;
			tabViewController.TabBar.ScrollEdgeAppearance = appearance;
		}
		public void Setup(IList<View> views)
		{
			if (views is null)
			{
				tabViewController.ViewControllers = null;
				return;
			}
			var controllers = views.Select(x =>
			{
				// If the view already has a handler providing a UIViewController
				// (e.g. NavigationView → CUINavigationController with toolbar items),
				// reuse it so toolbar items and navigation stack are preserved.
				UIViewController vc;
				if (x.Handler is IPlatformViewHandler pvh && pvh.ViewController is not null)
				{
					vc = pvh.ViewController;
				}
				else
				{
					// Ensure the view is realized so its handler is created
					x.ToPlatform(Context);
					if (x.Handler is IPlatformViewHandler pvh2 && pvh2.ViewController is not null)
					{
						vc = pvh2.ViewController;
					}
					else
					{
						vc = new CometViewController { MauiContext = Context, CurrentView = x };
					}
				}
				return new Tuple<View, UIViewController>(x, vc);
			}).ToList();
			foreach (var pair in controllers)
			{
				var title = pair.Item1.GetEnvironment<string>(EnvironmentKeys.TabView.Title);
				var imagePath = pair.Item1.GetEnvironment<string>(EnvironmentKeys.TabView.Image);
				UIImage image = null;
				if (!string.IsNullOrWhiteSpace(imagePath))
				{
					// Try SF Symbols first, then fall back to bundle
					image = UIImage.GetSystemImage(imagePath) ?? UIImage.FromBundle(imagePath);
				}
				pair.Item2.TabBarItem = new UITabBarItem()
				{
					Title = title ?? "",
					Image = image,
				};
			};

			tabViewController.ViewControllers = controllers.Select(x => x.Item2).ToArray();

			// Store explicit tab titles and re-apply them after ViewControllers assignment.
			// iOS propagates UINavigationController.topViewController.Title → TabBarItem.Title
			// when ViewControllers are set and when CometViewController.LoadView runs later,
			// which can overwrite our explicit TabText("Activity") with the page's .Title().
			_explicitTabTitles = new List<string>();
			for (int i = 0; i < controllers.Count; i++)
			{
				var explicitTitle = controllers[i].Item1.GetEnvironment<string>(EnvironmentKeys.TabView.Title);
				_explicitTabTitles.Add(explicitTitle);
				if (!string.IsNullOrEmpty(explicitTitle))
					tabViewController.ViewControllers[i].TabBarItem.Title = explicitTitle;
			}
		}

		/// <summary>
		/// Apply the selected tab index from the Comet TabView to the native UITabBarController.
		/// </summary>
		public void ApplySelectedIndex(int index)
		{
			if (tabViewController.ViewControllers is not null && index >= 0 && index < tabViewController.ViewControllers.Length)
				tabViewController.SelectedIndex = (nint)index;
		}

		/// <summary>
		/// Apply tab bar colors from environment keys set on the TabView.
		/// Called from the handler after Setup.
		/// </summary>
		public void ApplyTabBarAppearance(View tabView)
		{
			if (tabView is null) return;

			var bgColor = tabView.GetEnvironment<Color>(EnvironmentKeys.TabView.BarBackgroundColor);
			var tintColor = tabView.GetEnvironment<Color>(EnvironmentKeys.TabView.BarTintColor);
			var unselectedColor = tabView.GetEnvironment<Color>(EnvironmentKeys.TabView.BarUnselectedColor);

			if (bgColor is null && tintColor is null && unselectedColor is null)
				return;

			// Translucent appearance with blur so content scrolls behind the bar
			var appearance = new UITabBarAppearance();
			appearance.ConfigureWithTransparentBackground();
			appearance.BackgroundEffect = UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemUltraThinMaterial);
			appearance.ShadowColor = UIColor.Clear;

			if (bgColor is not null)
				appearance.BackgroundColor = bgColor.WithAlpha(0.85f).ToPlatform();

			tabViewController.TabBar.StandardAppearance = appearance;
			tabViewController.TabBar.ScrollEdgeAppearance = appearance;
			tabViewController.TabBar.Translucent = true;

			if (tintColor is not null)
				tabViewController.TabBar.TintColor = tintColor.ToPlatform();

			if (unselectedColor is not null)
				tabViewController.TabBar.UnselectedItemTintColor = unselectedColor.ToPlatform();
		}

		public override void MovedToSuperview()
		{
			base.MovedToSuperview();
			var vc = this.GetViewController();
			if (vc is not null)
			{
				vc.AddChildViewController(tabViewController);
				tabViewController.DidMoveToParentViewController(vc);
			}
		}
		public override void RemoveFromSuperview()
		{
			tabViewController.WillMoveToParentViewController(null);
			tabViewController.RemoveFromParentViewController();
			base.RemoveFromSuperview();
		}
		public override void LayoutSubviews()
		{
			base.LayoutSubviews();
			tabViewController.View.Frame = this.Bounds;

			// Re-apply explicit tab titles on every layout pass.
			// CometViewController.LoadView() sets vc.Title from the content view,
			// and iOS propagates that to UINavigationController.TabBarItem.Title.
			// This ensures our TabText() values always win.
			if (_explicitTabTitles is not null && tabViewController.ViewControllers is not null)
			{
				for (int i = 0; i < _explicitTabTitles.Count && i < tabViewController.ViewControllers.Length; i++)
				{
					if (!string.IsNullOrEmpty(_explicitTabTitles[i]))
						tabViewController.ViewControllers[i].TabBarItem.Title = _explicitTabTitles[i];
				}
			}
		}
	}
}
