#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WGrid = System.Windows.Controls.Grid;
using WBorder = System.Windows.Controls.Border;
using WWindow = System.Windows.Window;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Manages modal page presentation for MAUI.
	/// Modal pages are shown as overlay panels within the WWindow content.
	/// </summary>
	public static class ModalNavigationManager
	{
		static readonly List<FrameworkElement> _modalStack = new();
		static WGrid? _modalOverlayHost;

		/// <summary>
		/// Ensures the modal overlay host is set up in the WWindow.
		/// Called during WWindow initialization.
		/// </summary>
		public static void EnsureOverlayHost(WWindow window)
		{
			if (_modalOverlayHost != null) return;

			var existingContent = window.Content as UIElement;
			if (existingContent == null) return;

			// Wrap existing content in a Grid that can host modal overlays
			var host = new WGrid();
			window.Content = host;
			host.Children.Add(existingContent);

			_modalOverlayHost = host;
		}

		/// <summary>
		/// Push a modal page with optional animation.
		/// </summary>
		public static void PushModal(IView page, IMauiContext mauiContext, bool animated = true)
		{
			if (_modalOverlayHost == null)
			{
				// Try to find the host from the current WWindow
				var window = System.Windows.Application.Current?.MainWindow;
				if (window != null)
					EnsureOverlayHost(window);
			}

			if (_modalOverlayHost == null) return;

			try
			{
				var platformView = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)page, mauiContext);

				// Create overlay: semi-transparent background + content
				var overlay = new WGrid();
				overlay.Children.Add(new WBorder
				{
					Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0)),
				});

				var contentBorder = new WBorder
				{
					Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
					Margin = new System.Windows.Thickness(40),
					Child = (UIElement)platformView,
					CornerRadius = new System.Windows.CornerRadius(8),
					Effect = new System.Windows.Media.Effects.DropShadowEffect
					{
						BlurRadius = 20,
						ShadowDepth = 5,
						Opacity = 0.3,
					},
				};
				overlay.Children.Add(contentBorder);

				_modalStack.Add(overlay);
				_modalOverlayHost.Children.Add(overlay);

				if (animated)
				{
					overlay.Opacity = 0;
					var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
					{
						EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
					};
					overlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

					contentBorder.RenderTransform = new TranslateTransform(0, 50);
					var slideUp = new DoubleAnimation(50, 0, TimeSpan.FromMilliseconds(300))
					{
						EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
					};
					((TranslateTransform)contentBorder.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideUp);
				}
			}
			catch { }
		}

		/// <summary>
		/// Pop the top modal page with optional animation.
		/// </summary>
		public static void PopModal(bool animated = true)
		{
			if (_modalOverlayHost == null || _modalStack.Count == 0) return;

			var overlay = _modalStack[^1];
			_modalStack.RemoveAt(_modalStack.Count - 1);

			if (animated)
			{
				var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
				fadeOut.Completed += (s, e) =>
				{
					_modalOverlayHost.Children.Remove(overlay);
				};
				overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
			}
			else
			{
				_modalOverlayHost.Children.Remove(overlay);
			}
		}

		/// <summary>
		/// Check if any modal pages are currently shown.
		/// </summary>
		public static bool HasModals => _modalStack.Count > 0;
		public static int ModalCount => _modalStack.Count;
	}

	/// <summary>
	/// Provides animated page transitions for NavigationPage push/pop.
	/// </summary>
	public static class PageTransitionHelper
	{
		public static void AnimatePageIn(FrameworkElement element, bool fromRight = true)
		{
			if (element == null) return;

			element.Opacity = 0;
			var translate = new TranslateTransform(fromRight ? 100 : -100, 0);
			element.RenderTransform = translate;

			var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};
			element.BeginAnimation(UIElement.OpacityProperty, fadeIn);

			var slideIn = new DoubleAnimation(fromRight ? 100 : -100, 0, TimeSpan.FromMilliseconds(300))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};
			translate.BeginAnimation(TranslateTransform.XProperty, slideIn);
		}

		public static void AnimatePageOut(FrameworkElement element, bool toLeft = true, Action? onComplete = null)
		{
			if (element == null) { onComplete?.Invoke(); return; }

			var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
			fadeOut.Completed += (s, e) => onComplete?.Invoke();
			element.BeginAnimation(UIElement.OpacityProperty, fadeOut);

			var translate = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
			element.RenderTransform = translate;
			var slideOut = new DoubleAnimation(0, toLeft ? -100 : 100, TimeSpan.FromMilliseconds(250));
			translate.BeginAnimation(TranslateTransform.XProperty, slideOut);
		}
	}
}

