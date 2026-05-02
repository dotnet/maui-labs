using System;
using System.Drawing;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Comet.iOS
{
	public class CometViewController : UIViewController
	{
		private CometView _containerView;
		private View _startingCurrentView;
		public IMauiContext MauiContext { get; set; }

		/// <summary>
		/// Reference to the parent NavigationView. Used to resolve fallback toolbar
		/// items when the pushed view doesn't declare its own.
		/// </summary>
		internal NavigationView NavigationViewRef { get; set; }

		public CometViewController()
		{
			// Ensure edge-to-edge layout so content extends behind the
			// translucent navigation bar and tab bar.
			EdgesForExtendedLayout = UIRectEdge.All;
			ExtendedLayoutIncludesOpaqueBars = true;
		}

		public View CurrentView
		{
			get => _containerView?.CurrentView as View ?? _startingCurrentView;
			set
			{
				if (_containerView is not null)
					_containerView.CurrentView = value;
				else
					_startingCurrentView = value;

				Title = value?.GetTitle() ?? "";

				// Re-apply nav bar style when the view updates (e.g. on theme change
				// re-render) so chrome colors stay in sync.
				if (IsViewLoaded && NavigationController is not null)
					ApplyStyle();
			}
		}

		public object PlatformView => null;

		public bool HasContainer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		bool wasPopped;
		public void WasPopped() => wasPopped = true;

		public override void LoadView()
		{
			base.View = _containerView = new CometView(MauiContext);
			_containerView.CurrentView = _startingCurrentView;
			Title = _startingCurrentView?.GetTitle() ?? "";
			_startingCurrentView = null;
		}
		internal CometView ContainerView
		{
			get => _containerView;
			set
			{
				_containerView?.RemoveFromSuperview();
				View = _containerView = value;
			}
		}

		/// <summary>
		/// Walks the Comet view tree to find the effective background paint.
		/// Components and views with a Body delegate their rendering to a child
		/// view tree, so the background is often on the child, not the component.
		/// </summary>
		static Paint GetEffectiveBackground(View view)
		{
			if (view is null) return null;

			var bg = view.GetBackground();
			if (bg is not null) return bg;

			// Walk into the rendered body view tree
			if (view.Body is not null)
			{
				var bodyView = view.GetView() as View;
				if (bodyView is not null)
				{
					bg = bodyView.GetBackground();
					if (bg is not null) return bg;
				}
			}

			return null;
		}

		public override void ViewDidAppear(bool animated)
		{
			base.ViewDidAppear(animated);
			CurrentView?.ViewDidAppear();

			// Set the UIWindow background to match the content so safe area
			// edges (status bar, home indicator) show the correct color instead of
			// black/white letterboxing.
			if (View?.Window is not null)
			{
				UIKit.UIColor bgColor = null;
				var bg = GetEffectiveBackground(CurrentView);
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color is not null)
					bgColor = solid.Color.ToPlatform();
				bgColor ??= _containerView?.BackgroundColor;
				if (bgColor is not null)
					View.Window.BackgroundColor = bgColor;
			}
		}

		public override void ViewWillAppear(bool animated)
		{
			base.ViewWillAppear(animated);

			var view = CurrentView;

			// Propagate background color to the container view so it extends
			// into safe area insets (prevents white/black letterboxing).
			if (view is not null && _containerView is not null)
			{
				var bg = GetEffectiveBackground(view);
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color is not null)
				{
					_containerView.BackgroundColor = solid.Color.ToPlatform();
				}
			}

			ApplyStyle();
			ApplyToolbarItemsFromView();
		}

		/// <summary>
		/// Applies toolbar items from the current view to the navigation item.
		/// Called in ViewWillAppear after LoadView has built the Component's view tree,
		/// so .ToolbarItems() set inside Render() are discoverable via BuiltView.
		/// </summary>
		void ApplyToolbarItemsFromView()
		{
			var view = CurrentView;
			if (view is null) return;

			// Check the view (Component) and its built view for toolbar items
			var items = view.GetToolbarItems();

			// Fall back to the NavigationView's toolbar items
			if (items.Count == 0 && NavigationViewRef is not null)
				items = NavigationViewRef.ToolbarItems;

			if (items is null || items.Count == 0) return;

			var rightItems = new System.Collections.Generic.List<UIBarButtonItem>();
			foreach (var item in items)
			{
				if (item.Order == ToolbarItemOrder.Secondary) continue;
				var toolbarAction = item.OnClicked;
				UIBarButtonItem barItem;

				// Render font icon as UIImage if font family is specified
				if (!string.IsNullOrEmpty(item.IconGlyph) && !string.IsNullOrEmpty(item.IconFontFamily))
				{
					var image = Handlers.NavigationViewHandler.CreateFontIconImage(item.IconGlyph, item.IconFontFamily, 24);
					if (image is not null)
					{
						barItem = new UIBarButtonItem(
							image.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate),
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
				// Try SF Symbol (works for "plus", "trash", "star.fill", etc.)
				else if (!string.IsNullOrEmpty(item.IconGlyph))
				{
					var sfImage = UIImage.GetSystemImage(item.IconGlyph);
					if (sfImage is not null)
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
				NavigationItem.RightBarButtonItems = rightItems.ToArray();
		}

		public override void ViewDidDisappear(bool animated)
		{
			base.ViewDidDisappear(animated);
			CurrentView?.ViewDidDisappear();
			if (wasPopped)
			{
				CurrentView?.Dispose();
				CurrentView = null;
			}
		}

		public void ApplyStyle()
		{
			if (NavigationController is null)
				return;

			var navBar = NavigationController.NavigationBar;
			var barColor = CurrentView?.GetNavigationBackgroundColor()?.ToPlatform();
			var textColor = CurrentView?.GetNavigationTextColor()?.ToPlatform();

			// Use the standard iOS translucent blur for the nav bar.
			// ConfigureWithDefaultBackground gives the native glass/blur effect
			// that lets scrolled content show through the bar.
			var appearance = new UINavigationBarAppearance();
			appearance.ConfigureWithDefaultBackground();
			appearance.ShadowColor = UIColor.Clear;

			if (barColor is not null)
				appearance.BackgroundColor = barColor;

			if (textColor is not null)
			{
				appearance.LargeTitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				appearance.TitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				navBar.TintColor = textColor;
			}

			// StandardAppearance: the bar look when content is scrolled behind it.
			navBar.StandardAppearance = appearance;

			// ScrollEdgeAppearance: the bar look when content is at the top edge
			// (not scrolled). Use transparent so the bar blends with the page
			// background when the user hasn't scrolled yet.
			var edgeAppearance = new UINavigationBarAppearance();
			edgeAppearance.ConfigureWithTransparentBackground();
			edgeAppearance.ShadowColor = UIColor.Clear;
			if (textColor is not null)
			{
				edgeAppearance.LargeTitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				edgeAppearance.TitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
			}
			navBar.ScrollEdgeAppearance = edgeAppearance;

			navBar.CompactAppearance = appearance;
			navBar.Translucent = true;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				CurrentView?.Dispose();
				CurrentView = null;
			}
			base.Dispose(disposing);
		}

	}
}
