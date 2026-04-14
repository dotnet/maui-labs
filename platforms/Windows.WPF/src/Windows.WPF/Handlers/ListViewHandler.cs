using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Maui.Handlers;
using WListBox = System.Windows.Controls.ListBox;
using WDockPanel = System.Windows.Controls.DockPanel;
using WBorder = System.Windows.Controls.Border;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ListViewHandler : WPFViewHandler<Microsoft.Maui.Controls.ListView, FrameworkElement>
	{
		MauiListViewListBox? _listBox;
		WDockPanel? _container;
		ContentControl? _headerControl;
		ContentControl? _footerControl;

		public static readonly PropertyMapper<Microsoft.Maui.Controls.ListView, ListViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.ListView.ItemsSource)] = MapItemsSource,
				[nameof(Microsoft.Maui.Controls.ListView.SelectedItem)] = MapSelectedItem,
				[nameof(Microsoft.Maui.Controls.ListView.Header)] = MapHeader,
				[nameof(Microsoft.Maui.Controls.ListView.Footer)] = MapFooter,
				[nameof(Microsoft.Maui.Controls.ListView.SeparatorColor)] = MapSeparatorColor,
			};

		public ListViewHandler() : base(Mapper) { }

		protected override FrameworkElement CreatePlatformView()
		{
			_container = new WDockPanel { LastChildFill = true };

			_headerControl = new ContentControl();
			WDockPanel.SetDock(_headerControl, Dock.Top);
			_container.Children.Add(_headerControl);

			_footerControl = new ContentControl();
			WDockPanel.SetDock(_footerControl, Dock.Bottom);
			_container.Children.Add(_footerControl);

			_listBox = new MauiListViewListBox
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				BorderThickness = new System.Windows.Thickness(0),
			};
			_listBox.SelectionChanged += OnSelectionChanged;
			_container.Children.Add(_listBox);

			return _container;
		}

		void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (VirtualView != null && _listBox != null)
				VirtualView.SelectedItem = _listBox.SelectedItem;
		}

		static void MapItemsSource(ListViewHandler handler, Microsoft.Maui.Controls.ListView view)
		{
			if (handler._listBox == null) return;
			handler._listBox.MauiTemplate = view.ItemTemplate;
			handler._listBox.MauiListView = view;
			handler._listBox.ItemsSource = view.ItemsSource as IEnumerable;
		}

		static void MapSelectedItem(ListViewHandler handler, Microsoft.Maui.Controls.ListView view)
		{
			if (handler._listBox?.SelectedItem != view.SelectedItem)
				handler._listBox!.SelectedItem = view.SelectedItem;
		}

		static void MapHeader(ListViewHandler handler, Microsoft.Maui.Controls.ListView view)
		{
			if (handler._headerControl == null) return;

			if (view.HeaderTemplate != null && view.Header != null)
			{
				var content = view.HeaderTemplate.CreateContent() as View;
				if (content != null)
				{
					content.BindingContext = view.Header;
					var mauiContext = view.Handler?.MauiContext;
					if (mauiContext != null)
					{
						handler._headerControl.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)content, mauiContext);
						return;
					}
				}
			}

			if (view.Header is View headerView)
			{
				var mauiContext = view.Handler?.MauiContext;
				if (mauiContext != null)
				{
					handler._headerControl.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)headerView, mauiContext);
					return;
				}
			}

			if (view.Header != null)
			{
				handler._headerControl.Content = new TextBlock
				{
					Text = view.Header.ToString(),
					FontWeight = System.Windows.FontWeights.Bold,
					Padding = new System.Windows.Thickness(8, 4, 8, 4),
				};
			}
			else
			{
				handler._headerControl.Content = null;
			}
		}

		static void MapFooter(ListViewHandler handler, Microsoft.Maui.Controls.ListView view)
		{
			if (handler._footerControl == null) return;

			if (view.FooterTemplate != null && view.Footer != null)
			{
				var content = view.FooterTemplate.CreateContent() as View;
				if (content != null)
				{
					content.BindingContext = view.Footer;
					var mauiContext = view.Handler?.MauiContext;
					if (mauiContext != null)
					{
						handler._footerControl.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)content, mauiContext);
						return;
					}
				}
			}

			if (view.Footer is View footerView)
			{
				var mauiContext = view.Handler?.MauiContext;
				if (mauiContext != null)
				{
					handler._footerControl.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)footerView, mauiContext);
					return;
				}
			}

			if (view.Footer != null)
			{
				handler._footerControl.Content = new TextBlock
				{
					Text = view.Footer.ToString(),
					Padding = new System.Windows.Thickness(8, 4, 8, 4),
				};
			}
			else
			{
				handler._footerControl.Content = null;
			}
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		static void MapSeparatorColor(ListViewHandler handler, Microsoft.Maui.Controls.ListView view)
		{
			if (handler._listBox == null) return;
			handler._listBox.SeparatorBrush = ToBrush(view.SeparatorColor);
		}

		protected override void DisconnectHandler(FrameworkElement platformView)
		{
			if (_listBox != null)
				_listBox.SelectionChanged -= OnSelectionChanged;
			base.DisconnectHandler(platformView);
		}
	}

	public class MauiListViewListBox : WListBox
	{
		internal Microsoft.Maui.Controls.DataTemplate? MauiTemplate { get; set; }
		internal Microsoft.Maui.Controls.ListView? MauiListView { get; set; }
		internal System.Windows.Media.Brush? SeparatorBrush { get; set; }

		protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
		{
			base.PrepareContainerForItemOverride(element, item);

			if (element is ListBoxItem lbi && MauiTemplate != null && MauiListView != null)
			{
				lbi.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
				lbi.Padding = new System.Windows.Thickness(0);

				if (SeparatorBrush != null)
				{
					lbi.BorderBrush = SeparatorBrush;
					lbi.BorderThickness = new System.Windows.Thickness(0, 0, 0, 1);
				}

				try
				{
					var resolvedTemplate = MauiTemplate;
					if (MauiTemplate is Microsoft.Maui.Controls.DataTemplateSelector selector)
						resolvedTemplate = selector.SelectTemplate(item, MauiListView);

					if (resolvedTemplate == null) return;

					var content = resolvedTemplate.CreateContent() as View;
					if (content == null) return;

					content.BindingContext = item;

					var mauiContext = MauiListView.Handler?.MauiContext;
					if (mauiContext == null) return;

					var platformView = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)content, mauiContext);
					lbi.Content = platformView;
				}
				catch (Exception ex)
				{
					lbi.Content = new TextBlock
					{
						Text = item?.ToString() ?? "",
						TextWrapping = TextWrapping.Wrap,
						Padding = new System.Windows.Thickness(8, 4, 8, 4),
					};
					System.Diagnostics.Debug.WriteLine($"[ListView] Template error: {ex.Message}");
				}
			}
		}
	}
}
