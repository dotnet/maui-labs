using System;
using System.Drawing;
using System.Threading.Tasks;
using CoreGraphics;
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

		public CometViewController()
		{
			// Ensure edge-to-edge layout regardless of initialization path
			// (LoadView vs ContainerView setter).
			EdgesForExtendedLayout = UIRectEdge.All;
			ExtendedLayoutIncludesOpaqueBars = true;
		}

		public View CurrentView
		{
			get => _containerView?.CurrentView as View ?? _startingCurrentView;
			set
			{
				if (_containerView != null)
					_containerView.CurrentView = value;
				else
					_startingCurrentView = value;

				Title = value?.GetTitle() ?? "";

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
			if (view == null) return null;

			var bg = view.GetBackground();
			if (bg != null) return bg;

			// Walk into the rendered body view tree
			if (view.Body != null)
			{
				var bodyView = view.GetView() as View;
				if (bodyView != null)
				{
					bg = bodyView.GetBackground();
					if (bg != null) return bg;
				}
			}

			return null;
		}

		public override void ViewDidAppear(bool animated)
		{
			base.ViewDidAppear(animated);
			CurrentView?.ViewDidAppear();

			// Always set the UIWindow background to match the content so safe area
			// edges (status bar, home indicator) show the correct color instead of
			// black/white letterboxing.
			if (View?.Window != null)
			{
				UIKit.UIColor bgColor = null;
				var bg = GetEffectiveBackground(CurrentView);
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color != null)
					bgColor = solid.Color.ToPlatform();
				bgColor ??= _containerView?.BackgroundColor;
				if (bgColor != null)
					View.Window.BackgroundColor = bgColor;
			}
		}

		public override void ViewWillAppear(bool animated)
		{
			base.ViewWillAppear(animated);

			var view = CurrentView;

			// Propagate background color to the container view so it extends
			// into safe area insets (prevents white/black letterboxing).
			// Walk the view tree because the background is often on the rendered
			// body (e.g. ZStack from Render()), not on the Component itself.
			if (view != null && _containerView != null)
			{
				var bg = GetEffectiveBackground(view);
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color != null)
				{
					_containerView.BackgroundColor = solid.Color.ToPlatform();
				}
			}

			ApplyStyle();
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
			if (NavigationController == null)
				return;

			var barColor = CurrentView?.GetNavigationBackgroundColor()?.ToPlatform();
			var textColor = CurrentView?.GetNavigationTextColor()?.ToPlatform();

			// Also try background from the content view tree (NavigationView or body)
			if (barColor == null)
			{
				var bg = GetEffectiveBackground(CurrentView);
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color != null)
					barColor = solid.Color.ToPlatform();
			}

			// Translucent nav bar so content scrolls behind it
			var appearance = new UINavigationBarAppearance();
			appearance.ConfigureWithDefaultBackground();
			appearance.ShadowColor = UIColor.Clear;

			if (textColor != null)
			{
				appearance.LargeTitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				appearance.TitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				NavigationController.NavigationBar.TintColor = textColor;
			}

			NavigationController.NavigationBar.StandardAppearance = appearance;
			NavigationController.NavigationBar.ScrollEdgeAppearance = appearance;
			NavigationController.NavigationBar.CompactAppearance = appearance;
			NavigationController.NavigationBar.Translucent = true;
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
