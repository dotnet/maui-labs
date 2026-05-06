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
	/// Manages modal page presentation for MAUI on WPF.
	/// State is stored per-WWindow via an attached DependencyProperty so multiple
	/// windows can each maintain their own modal stack independently.
	/// </summary>
	public static class ModalNavigationManager
	{
		sealed class ModalContext
		{
			public WGrid OverlayHost { get; }
			public List<FrameworkElement> Stack { get; } = new();
			public ModalContext(WGrid overlayHost) => OverlayHost = overlayHost;
		}

		static readonly DependencyProperty ModalContextProperty = DependencyProperty.RegisterAttached(
			"ModalContext", typeof(ModalContext), typeof(WWindow),
			new PropertyMetadata(null));

		static ModalContext? GetContext(WWindow window) => (ModalContext?)window.GetValue(ModalContextProperty);
		static void SetContext(WWindow window, ModalContext value) => window.SetValue(ModalContextProperty, value);

		/// <summary>
		/// Ensures the modal overlay host is set up in the WWindow.
		/// Idempotent and safe to call multiple times for the same window.
		/// </summary>
		public static void EnsureOverlayHost(WWindow window)
		{
			if (window == null) return;
			if (GetContext(window) != null) return;

			var existingContent = window.Content as UIElement;
			if (existingContent == null) return;

			// Wrap existing content in a Grid that can host modal overlays
			var host = new WGrid();
			window.Content = host;
			host.Children.Add(existingContent);

			SetContext(window, new ModalContext(host));
		}

		static WWindow? ResolveWindow(WWindow? explicitWindow)
		{
			if (explicitWindow != null) return explicitWindow;

			// Prefer the active window so modals appear on the right window in multi-window apps.
			var app = System.Windows.Application.Current;
			if (app == null) return null;
			foreach (var w in app.Windows.OfType<WWindow>())
			{
				if (w.IsActive) return w;
			}
			return app.MainWindow;
		}

		/// <summary>
		/// Push a modal page with optional animation.
		/// </summary>
		public static void PushModal(IView page, IMauiContext mauiContext, bool animated = true)
			=> PushModal(null, page, mauiContext, animated);

		/// <summary>
		/// Push a modal page on a specific window.
		/// </summary>
		public static void PushModal(WWindow? window, IView page, IMauiContext mauiContext, bool animated = true)
		{
			window = ResolveWindow(window);
			if (window == null) return;

			EnsureOverlayHost(window);
			var ctx = GetContext(window);
			if (ctx == null) return;

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

				ctx.Stack.Add(overlay);
				ctx.OverlayHost.Children.Add(overlay);

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
		/// Pop the top modal page from the active window with optional animation.
		/// </summary>
		public static void PopModal(bool animated = true) => PopModal(null, animated);

		/// <summary>
		/// Pop the top modal page from a specific window with optional animation.
		/// </summary>
		public static void PopModal(WWindow? window, bool animated = true)
		{
			window = ResolveWindow(window);
			if (window == null) return;

			var ctx = GetContext(window);
			if (ctx == null || ctx.Stack.Count == 0) return;

			var overlay = ctx.Stack[^1];
			ctx.Stack.RemoveAt(ctx.Stack.Count - 1);

			if (animated)
			{
				var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
				fadeOut.Completed += (s, e) => ctx.OverlayHost.Children.Remove(overlay);
				overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
			}
			else
			{
				ctx.OverlayHost.Children.Remove(overlay);
			}
		}

		/// <summary>
		/// True if any modal pages are currently shown on the active window.
		/// </summary>
		public static bool HasModals => HasModalsOn(null);

		/// <summary>
		/// Number of modals on the active window.
		/// </summary>
		public static int ModalCount => ModalCountOn(null);

		public static bool HasModalsOn(WWindow? window)
		{
			window = ResolveWindow(window);
			return window != null && (GetContext(window)?.Stack.Count ?? 0) > 0;
		}

		public static int ModalCountOn(WWindow? window)
		{
			window = ResolveWindow(window);
			return window == null ? 0 : (GetContext(window)?.Stack.Count ?? 0);
		}
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

