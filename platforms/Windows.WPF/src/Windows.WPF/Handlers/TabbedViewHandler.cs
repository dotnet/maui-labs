#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using WGrid = System.Windows.Controls.Grid;
using WColor = System.Windows.Media.Color;
using WTabControl = System.Windows.Controls.TabControl;
using WTabItem = System.Windows.Controls.TabItem;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class TabbedViewHandler : WPFViewHandler<ITabbedView, WTabControl>
	{
		public static readonly IPropertyMapper<ITabbedView, TabbedViewHandler> Mapper =
			new PropertyMapper<ITabbedView, TabbedViewHandler>(ViewMapper)
			{
				[nameof(TabbedPage.BarBackgroundColor)] = MapBarBackgroundColor,
				[nameof(TabbedPage.BarTextColor)] = MapBarTextColor,
				[nameof(TabbedPage.SelectedTabColor)] = MapSelectedTabColor,
				[nameof(TabbedPage.UnselectedTabColor)] = MapUnselectedTabColor,
			};

		TabbedPage? TabbedPage => VirtualView as TabbedPage;
		bool _isSelectingTab;

		public TabbedViewHandler() : base(Mapper) { }

		protected override WTabControl CreatePlatformView()
		{
			return new WTabControl
			{
				TabStripPlacement = Dock.Top,
			};
		}

		protected override void ConnectHandler(WTabControl platformView)
		{
			base.ConnectHandler(platformView);
			platformView.SelectionChanged += OnSelectionChanged;

			if (TabbedPage != null)
			{
				TabbedPage.PagesChanged += OnPagesChanged;
				TabbedPage.CurrentPageChanged += OnCurrentPageChanged;
				SetupTabs();
			}
		}

		protected override void DisconnectHandler(WTabControl platformView)
		{
			platformView.SelectionChanged -= OnSelectionChanged;
			if (TabbedPage != null)
			{
				TabbedPage.PagesChanged -= OnPagesChanged;
				TabbedPage.CurrentPageChanged -= OnCurrentPageChanged;
			}
			base.DisconnectHandler(platformView);
		}

		void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			SetupTabs();
		}

		void OnCurrentPageChanged(object? sender, EventArgs e)
		{
			if (_isSelectingTab || TabbedPage?.CurrentPage == null)
				return;

			var index = TabbedPage.Children.IndexOf(TabbedPage.CurrentPage);
			if (index >= 0)
				PlatformView.SelectedIndex = index;
		}

		void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (_isSelectingTab || TabbedPage == null || PlatformView.SelectedIndex < 0)
				return;

			var index = PlatformView.SelectedIndex;
			if (index >= 0 && index < TabbedPage.Children.Count)
			{
				_isSelectingTab = true;
				try
				{
					TabbedPage.CurrentPage = TabbedPage.Children[index];
					LoadTabContent(index);
				}
				finally
				{
					_isSelectingTab = false;
				}
			}
		}

		void SetupTabs()
		{
			if (TabbedPage == null || MauiContext == null) return;

			_isSelectingTab = true;
			try
			{
				PlatformView.Items.Clear();
				foreach (var page in TabbedPage.Children)
				{
					var tabItem = new WTabItem
					{
						Header = page.Title ?? "Tab",
					};
					PlatformView.Items.Add(tabItem);
				}

				if (TabbedPage.Children.Count > 0)
				{
					PlatformView.SelectedIndex = 0;
					TabbedPage.CurrentPage = TabbedPage.Children[0];
					LoadTabContent(0);
				}
			}
			finally
			{
				_isSelectingTab = false;
			}
		}

		void LoadTabContent(int index)
		{
			if (TabbedPage == null || MauiContext == null ||
				index < 0 || index >= TabbedPage.Children.Count)
				return;

			var page = TabbedPage.Children[index];
			var platformView = ElementExtensions.ToPlatform((IElement)page, MauiContext);

			if (PlatformView.Items[index] is WTabItem tabItem)
			{
				tabItem.Content = platformView;
			}
		}

		public static void MapBarBackgroundColor(TabbedViewHandler handler, ITabbedView view)
		{
			if (view is TabbedPage tp && tp.BarBackgroundColor is Microsoft.Maui.Graphics.Color bgColor)
			{
				handler.PlatformView.Background = ToBrush(bgColor);
			}
		}

		public static void MapBarTextColor(TabbedViewHandler handler, ITabbedView view)
		{
			if (view is TabbedPage tp && tp.BarTextColor is Microsoft.Maui.Graphics.Color textColor)
			{
				handler.PlatformView.Foreground = ToBrush(textColor);
			}
		}

		public static void MapSelectedTabColor(TabbedViewHandler handler, ITabbedView view)
		{
			// WPF TabControl selected tab color requires ControlTemplate customization
		}

		public static void MapUnselectedTabColor(TabbedViewHandler handler, ITabbedView view)
		{
			// WPF TabControl unselected tab color requires ControlTemplate customization
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
