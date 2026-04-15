using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using PlatformView = System.Windows.Window;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class WindowHandler : ElementHandler<IWindow, System.Windows.Window>
	{

		public static IPropertyMapper<IWindow, WindowHandler> Mapper = new PropertyMapper<IWindow, WindowHandler>(ElementHandler.ElementMapper)
		{
			[nameof(IWindow.Title)] = MapTitle,
			[nameof(IWindow.Content)] = MapContent,
			[nameof(IWindow.Width)] = MapWidth,
			[nameof(IWindow.Height)] = MapHeight,
			[nameof(IWindow.X)] = MapX,
			[nameof(IWindow.Y)] = MapY,
			[nameof(IWindow.MaximumWidth)] = MapMaximumWidth,
			[nameof(IWindow.MaximumHeight)] = MapMaximumHeight,
			[nameof(IWindow.MinimumWidth)] = MapMinimumWidth,
			[nameof(IWindow.MinimumHeight)] = MapMinimumHeight,
		};

		public static CommandMapper<IWindow, IWindowHandler> CommandMapper = new(ElementCommandMapper)
		{
			//[nameof(IWindow.RequestDisplayDensity)] = MapRequestDisplayDensity,
		};

		public WindowHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public WindowHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public WindowHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override PlatformView CreatePlatformElement() =>
			MauiContext?.Services.GetService<PlatformView>() ?? throw new InvalidOperationException($"MauiContext did not have a valid window.");

		// Store delegate so we can unsubscribe
		Action<AppTheme>? _onThemeChanged;

		protected override void ConnectHandler(PlatformView platformView)
		{
			base.ConnectHandler(platformView);

			if (platformView.Content is null)
				platformView.Content = new WindowRootViewContainer();

			// Set up modal navigation overlay host
			Microsoft.Maui.Platforms.Windows.WPF.ModalNavigationManager.EnsureOverlayHost(platformView);

			// Wire up MenuBar if the window's page has one
			if (VirtualView?.Content is IMenuBarElement menuBarElement && menuBarElement.MenuBar?.Count > 0)
			{
				SetupMenuBar(platformView, menuBarElement);
			}

			// Apply theme-appropriate window background
			ApplyWindowTheme(platformView);
			_onThemeChanged = theme => PlatformView?.Dispatcher.InvokeAsync(() => ApplyWindowTheme(PlatformView));
			ThemeManager.ThemeChanged += _onThemeChanged;
		}

		static void ApplyWindowTheme(PlatformView? window)
		{
			if (window == null) return;
			var app = Microsoft.Maui.Controls.Application.Current;
			bool dark = app?.RequestedTheme == AppTheme.Dark ||
				(app?.RequestedTheme == AppTheme.Unspecified && ThemeManager.GetCurrentTheme() == AppTheme.Dark);
			window.Background = dark
				? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32))
				: new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 243, 243));
		}

		protected override void DisconnectHandler(PlatformView platformView)
		{
			if (_onThemeChanged != null)
				ThemeManager.ThemeChanged -= _onThemeChanged;
			//MauiContext
			//	?.GetNavigationRootManager()
			//	?.Disconnect();

			//if (platformView.Content is WindowRootViewContainer container)
			//{
			//	container.Children.Clear();
			//	platformView.Content = null;
			//}

			//var appWindow = platformView.GetAppWindow();
			//if (appWindow is not null)
			//	appWindow.Changed -= OnWindowChanged;

			base.DisconnectHandler(platformView);
		}

		public static void MapTitle(WindowHandler handler, IWindow window)
		{
			if (handler.PlatformView != null)
				handler.PlatformView.Title = window.Title ?? string.Empty;
		}

		public static void MapContent(WindowHandler handler, IWindow window)
		{
			_ = handler.MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var container = FindRootViewContainer(handler.PlatformView);

			if (container != null && handler.VirtualView.Content is IView content)
			{
				var platformEl = (FrameworkElement)content.ToPlatform(handler.MauiContext);
				container.AddPage(platformEl);
			}
		}

		static WindowRootViewContainer? FindRootViewContainer(System.Windows.Window window)
		{
			if (window.Content is WindowRootViewContainer direct)
				return direct;

			// ModalNavigationManager may have wrapped it in a Grid
			if (window.Content is System.Windows.Controls.Panel panel)
			{
				foreach (var child in panel.Children)
				{
					if (child is WindowRootViewContainer rvc)
						return rvc;
				}
			}

			return null;
		}

		public static void MapX(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.X) && view.X >= 0)
				handler.PlatformView.Left = view.X;
		}

		public static void MapY(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.Y) && view.Y >= 0)
				handler.PlatformView.Top = view.Y;
		}

		public static void MapWidth(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.Width) && view.Width >= 0)
				handler.PlatformView.Width = view.Width;
		}

		public static void MapHeight(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.Height) && view.Height >= 0)
				handler.PlatformView.Height = view.Height;
		}

		public static void MapMaximumWidth(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.MaximumWidth) && !double.IsInfinity(view.MaximumWidth))
				handler.PlatformView.MaxWidth = view.MaximumWidth;
		}

		public static void MapMaximumHeight(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.MaximumHeight) && !double.IsInfinity(view.MaximumHeight))
				handler.PlatformView.MaxHeight = view.MaximumHeight;
		}

		public static void MapMinimumWidth(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.MinimumWidth) && view.MinimumWidth >= 0)
				handler.PlatformView.MinWidth = view.MinimumWidth;
		}

		public static void MapMinimumHeight(WindowHandler handler, IWindow view)
		{
			if (handler.PlatformView != null && !double.IsNaN(view.MinimumHeight) && view.MinimumHeight >= 0)
				handler.PlatformView.MinHeight = view.MinimumHeight;
		}

		static void SetupMenuBar(System.Windows.Window window, IMenuBarElement menuBarElement)
		{
			if (menuBarElement.MenuBar == null || menuBarElement.MenuBar.Count == 0) return;

			var wpfMenu = new System.Windows.Controls.Menu();

			foreach (var barItem in menuBarElement.MenuBar)
			{
				if (barItem is Microsoft.Maui.Controls.MenuBarItem mbi)
				{
					var topItem = new System.Windows.Controls.MenuItem { Header = mbi.Text ?? "Menu" };
					foreach (var child in mbi)
					{
						AddMenuFlyoutItem(topItem, child);
					}
					wpfMenu.Items.Add(topItem);
				}
			}

			// Insert menu at top of window content
			if (window.Content is System.Windows.Controls.Panel panel)
			{
				panel.Children.Insert(0, wpfMenu);
			}
			else if (window.Content is System.Windows.UIElement existing)
			{
				var dock = new System.Windows.Controls.DockPanel();
				window.Content = dock;
				System.Windows.Controls.DockPanel.SetDock(wpfMenu, System.Windows.Controls.Dock.Top);
				dock.Children.Add(wpfMenu);
				dock.Children.Add(existing);
			}
		}

		static void AddMenuFlyoutItem(System.Windows.Controls.MenuItem parent, Microsoft.Maui.IMenuElement element)
		{
			if (element is Microsoft.Maui.Controls.MenuFlyoutItem mfi)
			{
				var mi = new System.Windows.Controls.MenuItem { Header = mfi.Text };
				if (mfi.KeyboardAccelerators is { Count: > 0 } accelerators)
				{
					var accel = accelerators[0];
					mi.InputGestureText = accel?.Key?.ToString() ?? string.Empty;
				}
				mi.Click += (s, e) => mfi.Command?.Execute(mfi.CommandParameter);
				parent.Items.Add(mi);
			}
			else if (element is Microsoft.Maui.Controls.MenuFlyoutSubItem subItem)
			{
				var mi = new System.Windows.Controls.MenuItem { Header = subItem.Text };
				foreach (var child in subItem)
					AddMenuFlyoutItem(mi, child);
				parent.Items.Add(mi);
			}
			else if (element is Microsoft.Maui.Controls.MenuFlyoutSeparator)
			{
				parent.Items.Add(new System.Windows.Controls.Separator());
			}
		}

		//public static void MapToolbar(IWindowHandler handler, IWindow view)
		//{
		//	if (view is IToolbarElement tb)
		//		ViewHandler.MapToolbar(handler, tb);
		//}

		//public static void MapMenuBar(IWindowHandler handler, IWindow view)
		//{
		//	if (view is IMenuBarElement mb)
		//	{
		//		_ = handler.MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");
		//		var windowManager = handler.MauiContext.GetNavigationRootManager();
		//		windowManager.SetMenuBar(mb.MenuBar?.ToPlatform(handler.MauiContext!) as MenuBar);
		//	}
		//}

		//public static void MapFlowDirection(IWindowHandler handler, IWindow view)
		//{
		//	var WindowHandle = handler.PlatformView.GetWindowHandle();

		//	// Retrieve current extended style
		//	var extended_style = PlatformMethods.GetWindowLongPtr(WindowHandle, PlatformMethods.WindowLongFlags.GWL_EXSTYLE);
		//	long updated_style;
		//	if (view.FlowDirection == FlowDirection.RightToLeft)
		//		updated_style = extended_style | (long)PlatformMethods.ExtendedWindowStyles.WS_EX_LAYOUTRTL;
		//	else
		//		updated_style = extended_style & ~((long)PlatformMethods.ExtendedWindowStyles.WS_EX_LAYOUTRTL);

		//	if (updated_style != extended_style)
		//		PlatformMethods.SetWindowLongPtr(WindowHandle, PlatformMethods.WindowLongFlags.GWL_EXSTYLE, updated_style);
		//}

		//public static void MapRequestDisplayDensity(IWindowHandler handler, IWindow window, object? args)
		//{
		//	if (args is DisplayDensityRequest request)
		//		request.SetResult(handler.PlatformView.GetDisplayDensity());
		//}

		//void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
		//{
		//	if (!args.DidSizeChange && !args.DidPositionChange)
		//		return;

		//	UpdateVirtualViewFrame(sender);
		//}

		//void UpdateVirtualViewFrame(AppWindow appWindow)
		//{
		//	var size = appWindow.Size;
		//	var pos = appWindow.Position;

		//	var density = PlatformView.GetDisplayDensity();

		//	VirtualView.FrameChanged(new Rect(
		//		pos.X / density, pos.Y / density,
		//		size.Width / density, size.Height / density));
		//}
	}
}
