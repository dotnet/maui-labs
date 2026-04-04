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

			if (nav.Navigation != null)
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

				// Apply toolbar items from the pushed view
				ApplyToolbarItems(newVc, toView, nav);

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
			navigationController.PushViewController(vc, true);

			// Add leading bar button (hamburger icon) if configured
			if (nav.LeadingBarAction != null)
			{
				var action = nav.LeadingBarAction;
				vc.NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
					nav.LeadingBarIcon ?? "☰",
					UIBarButtonItemStyle.Plain,
					(s, e) => action());
			}

			// Apply toolbar items from the NavigationView to the root view controller
			ApplyToolbarItems(vc, null, nav);

			return navigationController.View;
		}

		/// <summary>
		/// Applies toolbar items to a view controller's navigation item.
		/// Checks the pushed view's own toolbar items first, then falls back
		/// to the NavigationView's toolbar items.
		/// </summary>
		static void ApplyToolbarItems(CometViewController vc, View pushedView, NavigationView nav)
		{
			// Determine which toolbar items to use: view's own items take priority
			List<ToolbarItem> items = null;
			if (pushedView != null)
			{
				var viewItems = pushedView.GetToolbarItems();
				if (viewItems.Count > 0)
					items = viewItems;
			}
			// Fall back to NavigationView's toolbar items
			if (items == null || items.Count == 0)
				items = nav.ToolbarItems;

			if (items == null || items.Count == 0)
				return;

			var rightItems = new List<UIBarButtonItem>();
			foreach (var item in items)
			{
				if (item.Order == ToolbarItemOrder.Secondary) continue;
				var toolbarAction = item.OnClicked;
				UIBarButtonItem barItem;

				// Render font icon as UIImage if font family is specified
				if (!string.IsNullOrEmpty(item.IconGlyph) && !string.IsNullOrEmpty(item.IconFontFamily))
				{
					var image = CreateFontIconImage(item.IconGlyph, item.IconFontFamily, 24);
					if (image != null)
					{
						barItem = new UIBarButtonItem(
							image,
							UIBarButtonItemStyle.Plain,
							(s, e) => toolbarAction?.Invoke());
					}
					else
					{
						barItem = new UIBarButtonItem(
							item.IconGlyph ?? item.Text ?? "",
							UIBarButtonItemStyle.Plain,
							(s, e) => toolbarAction?.Invoke());
					}
				}
				// Use SF Symbol name if glyph looks like an SF Symbol identifier
				else if (!string.IsNullOrEmpty(item.IconGlyph) && item.IconGlyph.Contains('.'))
				{
					var sfImage = UIImage.GetSystemImage(item.IconGlyph);
					if (sfImage != null)
					{
						barItem = new UIBarButtonItem(
							sfImage,
							UIBarButtonItemStyle.Plain,
							(s, e) => toolbarAction?.Invoke());
					}
					else
					{
						barItem = new UIBarButtonItem(
							item.Text ?? item.IconGlyph,
							UIBarButtonItemStyle.Plain,
							(s, e) => toolbarAction?.Invoke());
					}
				}
				else
				{
					barItem = new UIBarButtonItem(
						item.IconGlyph ?? item.Text ?? "",
						UIBarButtonItemStyle.Plain,
						(s, e) => toolbarAction?.Invoke());
				}
				barItem.Enabled = item.IsEnabled;
				rightItems.Add(barItem);
			}
			if (rightItems.Count > 0)
				vc.NavigationItem.RightBarButtonItems = rightItems.ToArray();
		}

		protected override void ConnectHandler(UIView platformView)
		{
			base.ConnectHandler(platformView);
			ApplyNavigationBarBackground();

			// When the handler is transferred to a new NavigationView (during diff),
			// the title must be refreshed from the current VirtualView.
			if (rootViewController != null)
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
				var bgPaint = VirtualView?.GetBackground();
				if (bgPaint is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color != null)
				{
					var uiColor = solid.Color.ToPlatform();
					var appearance = new UINavigationBarAppearance();
					appearance.ConfigureWithOpaqueBackground();
					appearance.BackgroundColor = uiColor;
					appearance.ShadowColor = UIColor.Clear;
					navController.NavigationBar.StandardAppearance = appearance;
					navController.NavigationBar.ScrollEdgeAppearance = appearance;
					navController.NavigationBar.CompactAppearance = appearance;
				}
			}
		}

		static UIImage CreateFontIconImage(string glyph, string fontFamily, nfloat size)
		{
			var font = UIFont.FromName(fontFamily, size);
			if (font == null)
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
