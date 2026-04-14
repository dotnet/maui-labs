#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WButton = System.Windows.Controls.Button;
using WTextBlock = System.Windows.Controls.TextBlock;
using WColor = System.Windows.Media.Color;
using WBrush = System.Windows.Media.Brush;
using WThickness = System.Windows.Thickness;
using WVisibility = System.Windows.Visibility;
using WHorizontalAlignment = System.Windows.HorizontalAlignment;
using WVerticalAlignment = System.Windows.VerticalAlignment;
using WFontWeights = System.Windows.FontWeights;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace Microsoft.Maui.Handlers.WPF
{
	public class NavigationContainerView : System.Windows.Controls.DockPanel
	{
		readonly System.Windows.Controls.DockPanel _toolbarPanel;
		readonly WButton _backButton;
		readonly WTextBlock _titleLabel;
		readonly System.Windows.Controls.StackPanel _toolbarItemsPanel;
		readonly System.Windows.Controls.ContentControl _contentArea;

		public Action? OnBackButtonClicked { get; set; }

		public NavigationContainerView()
		{
			_toolbarPanel = new System.Windows.Controls.DockPanel
			{
				Height = 40,
				LastChildFill = true,
			};
			UpdateToolbarTheme();

			_backButton = new WButton
			{
				Content = "← Back",
				Margin = new WThickness(8, 4, 4, 4),
				Padding = new WThickness(8, 2, 8, 2),
				VerticalAlignment = WVerticalAlignment.Center,
				Visibility = WVisibility.Collapsed,
			};
			_backButton.Click += (s, e) => OnBackButtonClicked?.Invoke();
			System.Windows.Controls.DockPanel.SetDock(_backButton, System.Windows.Controls.Dock.Left);

			_toolbarItemsPanel = new System.Windows.Controls.StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = WHorizontalAlignment.Right,
				VerticalAlignment = WVerticalAlignment.Center,
				Margin = new WThickness(4, 4, 8, 4),
			};
			System.Windows.Controls.DockPanel.SetDock(_toolbarItemsPanel, System.Windows.Controls.Dock.Right);

			_titleLabel = new WTextBlock
			{
				FontSize = 16,
				FontWeight = WFontWeights.SemiBold,
				VerticalAlignment = WVerticalAlignment.Center,
				HorizontalAlignment = WHorizontalAlignment.Center,
				TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
			};

			_toolbarPanel.Children.Add(_backButton);
			_toolbarPanel.Children.Add(_toolbarItemsPanel);
			_toolbarPanel.Children.Add(_titleLabel);

			System.Windows.Controls.DockPanel.SetDock(_toolbarPanel, System.Windows.Controls.Dock.Top);

			_contentArea = new System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = WHorizontalAlignment.Stretch,
				VerticalContentAlignment = WVerticalAlignment.Stretch,
			};

			Children.Add(_toolbarPanel);
			Children.Add(_contentArea);
		}

		public void SetTitle(string? title) => _titleLabel.Text = title ?? string.Empty;

		public void SetBackButtonVisible(bool visible)
		{
			_backButton.Visibility = visible ? WVisibility.Visible : WVisibility.Collapsed;
		}

		public void SetContent(System.Windows.FrameworkElement? content)
		{
			_contentArea.Content = content;
		}

		public void SetBarBackground(WBrush? brush)
		{
			if (brush != null)
				_toolbarPanel.Background = brush;
		}

		void UpdateToolbarTheme()
		{
			bool dark = Platform.WPF.ThemeManager.GetCurrentTheme() == ApplicationModel.AppTheme.Dark;
			if (Microsoft.Maui.Controls.Application.Current is { } app && app.RequestedTheme != ApplicationModel.AppTheme.Unspecified)
				dark = app.RequestedTheme == ApplicationModel.AppTheme.Dark;
			_toolbarPanel.Background = dark
				? new System.Windows.Media.SolidColorBrush(WColor.FromRgb(30, 30, 30))
				: new System.Windows.Media.SolidColorBrush(WColor.FromRgb(240, 240, 240));
		}

		public void SetBarTextColor(WBrush? brush)
		{
			if (brush != null)
				_titleLabel.Foreground = brush;
		}

		public void SetToolbarItems(IList<ToolbarItem>? items)
		{
			_toolbarItemsPanel.Children.Clear();
			if (items == null) return;

			foreach (var item in items.Where(i => i.Order != ToolbarItemOrder.Secondary))
			{
				var btn = new WButton
				{
					Content = item.Text ?? string.Empty,
					Margin = new WThickness(2, 0, 2, 0),
					Padding = new WThickness(8, 2, 8, 2),
					IsEnabled = item.IsEnabled,
				};
				btn.Click += (s, e) =>
				{
					if (item.IsEnabled)
						((IMenuItemController)item).Activate();
				};
				_toolbarItemsPanel.Children.Add(btn);
			}
		}
	}

	public partial class NavigationViewHandler : WPFViewHandler<IStackNavigationView, NavigationContainerView>, INavigationViewHandler
	{
		public static IPropertyMapper<IStackNavigationView, NavigationViewHandler> Mapper =
			new PropertyMapper<IStackNavigationView, NavigationViewHandler>(ViewMapper)
			{
			};

		public static CommandMapper<IStackNavigationView, NavigationViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
				[nameof(IStackNavigation.RequestNavigation)] = RequestNavigation,
			};

		readonly List<IView> _navigationStack = new();

		public NavigationViewHandler() : base(Mapper, CommandMapper) { }

		IStackNavigationView INavigationViewHandler.VirtualView => VirtualView;
		object INavigationViewHandler.PlatformView => PlatformView;

		protected override NavigationContainerView CreatePlatformView()
		{
			var container = new NavigationContainerView();
			container.OnBackButtonClicked = OnBackButtonClicked;
			return container;
		}

		protected override void ConnectHandler(NavigationContainerView platformView)
		{
			base.ConnectHandler(platformView);
		}

		void OnBackButtonClicked()
		{
			if (VirtualView is NavigationPage navPage && navPage.Navigation.NavigationStack.Count > 1)
			{
				navPage.PopAsync();
			}
		}

		public static void RequestNavigation(INavigationViewHandler handler, IStackNavigationView view, object? args)
		{
			if (handler is NavigationViewHandler wpfHandler && args is NavigationRequest request)
			{
				wpfHandler.HandleNavigation(request);
			}
		}

		void HandleNavigation(NavigationRequest request)
		{
			_navigationStack.Clear();
			_navigationStack.AddRange(request.NavigationStack);

			var currentPage = _navigationStack.LastOrDefault();
			ShowPage(currentPage);

			PlatformView.SetBackButtonVisible(_navigationStack.Count > 1);

			if (currentPage is MauiPage page)
			{
				PlatformView.SetTitle(page.Title);
				PlatformView.SetToolbarItems(page.ToolbarItems);
			}

			if (VirtualView is NavigationPage navPage)
			{
				if (navPage.BarBackgroundColor != null)
					PlatformView.SetBarBackground(ToBrush(navPage.BarBackgroundColor));
				if (navPage.BarTextColor != null)
					PlatformView.SetBarTextColor(ToBrush(navPage.BarTextColor));
			}

			((IStackNavigationView)VirtualView).NavigationFinished(_navigationStack);
		}

		void ShowPage(IView? page)
		{
			if (page == null || MauiContext == null)
				return;

			var platformView = page.ToPlatform(MauiContext);
			PlatformView.SetContent(platformView as System.Windows.FrameworkElement);
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(WColor.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}
	}
}
