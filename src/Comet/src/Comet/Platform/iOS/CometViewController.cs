using System;
using System.Drawing;
using System.Threading.Tasks;
using CoreGraphics;
using Microsoft.Maui;
using UIKit;

namespace Comet.iOS
{
	public class CometViewController : UIViewController
	{
		private CometView _containerView;
		private View _startingCurrentView;
		public IMauiContext MauiContext { get; set; }

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
			// Check if the view wants to ignore safe area for edge-to-edge rendering.
			// When IgnoreSafeArea is set, content extends under the status bar and
			// navigation bar, allowing background colors to fill the entire screen.
			var view = _startingCurrentView;
			if (view is ISafeAreaView safeAreaView && safeAreaView.IgnoreSafeArea)
			{
				EdgesForExtendedLayout = UIRectEdge.All;
				ExtendedLayoutIncludesOpaqueBars = true;
			}
			else
			{
				EdgesForExtendedLayout = UIRectEdge.None;
			}

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
		public override void ViewDidAppear(bool animated)
		{
			base.ViewDidAppear(animated);
			CurrentView?.ViewDidAppear();
		}

		public override void ViewWillAppear(bool animated)
		{
			base.ViewWillAppear(animated);

			// Re-check safe area setting when the view appears (pushed views may differ)
			var view = CurrentView;
			if (view is ISafeAreaView safeAreaView && safeAreaView.IgnoreSafeArea)
			{
				EdgesForExtendedLayout = UIRectEdge.All;
				ExtendedLayoutIncludesOpaqueBars = true;
			}
			else
			{
				EdgesForExtendedLayout = UIRectEdge.None;
			}

			// Propagate background color to the container view so it extends
			// into safe area insets (prevents white/black letterboxing)
			if (view != null && _containerView != null)
			{
				var bg = view.GetBackground();
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

			// Also try background from parent view (NavigationView)
			if (barColor == null)
			{
				var bg = CurrentView?.GetBackground();
				if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color != null)
					barColor = solid.Color.ToPlatform();
			}

			var appearance = new UINavigationBarAppearance();
			appearance.ConfigureWithOpaqueBackground();

			if (barColor != null)
				appearance.BackgroundColor = barColor;

			if (textColor != null)
			{
				appearance.TitleTextAttributes = new UIStringAttributes { ForegroundColor = textColor };
				NavigationController.NavigationBar.TintColor = textColor;
			}

			appearance.ShadowColor = UIColor.Clear;
			NavigationController.NavigationBar.StandardAppearance = appearance;
			NavigationController.NavigationBar.ScrollEdgeAppearance = appearance;
			NavigationController.NavigationBar.CompactAppearance = appearance;
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
