using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Maui.Handlers;
using WGrid = System.Windows.Controls.Grid;
using WProgressBar = System.Windows.Controls.ProgressBar;

namespace Microsoft.Maui.Handlers.WPF
{
	/// <summary>
	/// RefreshView wraps content and provides a refresh indicator.
	/// WPF doesn't have native pull-to-refresh; we show a progress bar at top during refresh.
	/// </summary>
	public partial class RefreshViewHandler : WPFViewHandler<Microsoft.Maui.Controls.RefreshView, WGrid>
	{
		WProgressBar? _indicator;
		ContentControl? _content;

		public static readonly PropertyMapper<Microsoft.Maui.Controls.RefreshView, RefreshViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.RefreshView.IsRefreshing)] = MapIsRefreshing,
				[nameof(Microsoft.Maui.Controls.RefreshView.Content)] = MapContent,
				[nameof(Microsoft.Maui.Controls.RefreshView.RefreshColor)] = MapRefreshColor,
			};

		public RefreshViewHandler() : base(Mapper) { }

		protected override WGrid CreatePlatformView()
		{
			var grid = new WGrid();
			grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
			grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

			_indicator = new WProgressBar
			{
				IsIndeterminate = true,
				Height = 4,
				Visibility = System.Windows.Visibility.Collapsed,
			};
			WGrid.SetRow(_indicator, 0);
			grid.Children.Add(_indicator);

			_content = new ContentControl
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch,
			};
			WGrid.SetRow(_content, 1);
			grid.Children.Add(_content);

			return grid;
		}

		static void MapIsRefreshing(RefreshViewHandler handler, Microsoft.Maui.Controls.RefreshView view)
		{
			if (handler._indicator != null)
				handler._indicator.Visibility = view.IsRefreshing
					? System.Windows.Visibility.Visible
					: System.Windows.Visibility.Collapsed;
		}

		static void MapContent(RefreshViewHandler handler, Microsoft.Maui.Controls.RefreshView view)
		{
			if (handler._content == null || view.Content == null) return;
			var mauiContext = handler.MauiContext;
			if (mauiContext == null) return;
			try
			{
				handler._content.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)view.Content, mauiContext);
			}
			catch { }
		}

		static void MapRefreshColor(RefreshViewHandler handler, Microsoft.Maui.Controls.RefreshView view)
		{
			if (handler._indicator != null && view.RefreshColor != null)
			{
				var c = view.RefreshColor;
				handler._indicator.Foreground = new System.Windows.Media.SolidColorBrush(
					System.Windows.Media.Color.FromArgb(
						(byte)(c.Alpha * 255), (byte)(c.Red * 255),
						(byte)(c.Green * 255), (byte)(c.Blue * 255)));
			}
		}
	}

	/// <summary>
	/// SwipeView wraps content with swipe-to-reveal actions.
	/// Desktop approximation: right-click context menu with swipe items.
	/// </summary>
	public partial class SwipeViewHandler : WPFViewHandler<Microsoft.Maui.Controls.SwipeView, WGrid>
	{
		ContentControl? _content;

		public static readonly PropertyMapper<Microsoft.Maui.Controls.SwipeView, SwipeViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.SwipeView.Content)] = MapContent,
			};

		public SwipeViewHandler() : base(Mapper) { }

		protected override WGrid CreatePlatformView()
		{
			var grid = new WGrid();
			_content = new ContentControl
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch,
			};
			grid.Children.Add(_content);

			// Build context menu from swipe items
			grid.Loaded += (s, e) => BuildContextMenu(grid);

			return grid;
		}

		void BuildContextMenu(WGrid g)
		{
			if (VirtualView == null) return;

			var menu = new System.Windows.Controls.ContextMenu();
			AddSwipeItems(menu, VirtualView.LeftItems);
			AddSwipeItems(menu, VirtualView.RightItems);
			AddSwipeItems(menu, VirtualView.TopItems);
			AddSwipeItems(menu, VirtualView.BottomItems);

			if (menu.Items.Count > 0)
				g.ContextMenu = menu;
		}

		static void AddSwipeItems(System.Windows.Controls.ContextMenu menu, Microsoft.Maui.Controls.SwipeItems? items)
		{
			if (items == null) return;
			foreach (var item in items)
			{
				if (item is Microsoft.Maui.Controls.SwipeItem si)
				{
					var mi = new System.Windows.Controls.MenuItem { Header = si.Text };
					if (si.BackgroundColor != null)
					{
						var c = si.BackgroundColor;
						mi.Background = new System.Windows.Media.SolidColorBrush(
							System.Windows.Media.Color.FromArgb(
								(byte)(c.Alpha * 255), (byte)(c.Red * 255),
								(byte)(c.Green * 255), (byte)(c.Blue * 255)));
					}
					mi.Click += (s, e) =>
					{
						si.Command?.Execute(si.CommandParameter);
						try
						{
							var method = typeof(Microsoft.Maui.Controls.SwipeItem).GetMethod("OnInvoked",
								System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
							method?.Invoke(si, null);
						}
						catch { }
					};
					menu.Items.Add(mi);
				}
			}
		}

		static void MapContent(SwipeViewHandler handler, Microsoft.Maui.Controls.SwipeView view)
		{
			if (handler._content == null || view.Content == null) return;
			var mauiContext = handler.MauiContext;
			if (mauiContext == null) return;
			try
			{
				handler._content.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)view.Content, mauiContext);
			}
			catch { }
		}
	}

	/// <summary>
	/// TableView — settings-style grouped sections.
	/// Maps to a WPF StackPanel with group headers and item rows.
	/// </summary>
	public partial class TableViewHandler : WPFViewHandler<Microsoft.Maui.Controls.TableView, ScrollViewer>
	{
		public static readonly PropertyMapper<Microsoft.Maui.Controls.TableView, TableViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.TableView.Root)] = MapRoot,
			};

		public TableViewHandler() : base(Mapper) { }

		protected override ScrollViewer CreatePlatformView()
		{
			return new ScrollViewer
			{
				VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
			};
		}

		static void MapRoot(TableViewHandler handler, Microsoft.Maui.Controls.TableView view)
		{
			if (view.Root == null) return;

			var stack = new StackPanel();

			foreach (var section in view.Root)
			{
				// Section header
				if (!string.IsNullOrEmpty(section.Title))
				{
					stack.Children.Add(new TextBlock
					{
						Text = section.Title,
						FontSize = 13,
						FontWeight = System.Windows.FontWeights.SemiBold,
						Foreground = System.Windows.Media.Brushes.Gray,
						Margin = new System.Windows.Thickness(12, 16, 12, 4),
					});
				}

				// Section cells
				foreach (var cell in section)
				{
					var cellView = CreateCellView(cell);
					if (cellView != null)
						stack.Children.Add(cellView);
				}
			}

			handler.PlatformView.Content = stack;
		}

		static UIElement? CreateCellView(Microsoft.Maui.Controls.Cell cell)
		{
			var border = new System.Windows.Controls.Border
			{
				BorderBrush = System.Windows.Media.Brushes.LightGray,
				BorderThickness = new System.Windows.Thickness(0, 0, 0, 0.5),
				Padding = new System.Windows.Thickness(12, 8, 12, 8),
			};

			if (cell is Microsoft.Maui.Controls.TextCell textCell)
			{
				var sp = new StackPanel();
				sp.Children.Add(new TextBlock { Text = textCell.Text ?? "", FontSize = 14 });
				if (!string.IsNullOrEmpty(textCell.Detail))
				{
					sp.Children.Add(new TextBlock
					{
						Text = textCell.Detail,
						FontSize = 12,
						Foreground = System.Windows.Media.Brushes.Gray,
					});
				}
				border.Child = sp;
				border.MouseLeftButtonUp += (s, e) =>
				{
					textCell.Command?.Execute(textCell.CommandParameter);
				};
			}
			else if (cell is Microsoft.Maui.Controls.SwitchCell switchCell)
			{
				var dock = new DockPanel();
				dock.Children.Add(new TextBlock
				{
					Text = switchCell.Text ?? "",
					VerticalAlignment = System.Windows.VerticalAlignment.Center,
					FontSize = 14,
				});
				var toggle = new System.Windows.Controls.CheckBox
				{
					IsChecked = switchCell.On,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				};
				toggle.Checked += (s, e) => switchCell.On = true;
				toggle.Unchecked += (s, e) => switchCell.On = false;
				DockPanel.SetDock(toggle, Dock.Right);
				dock.Children.Add(toggle);
				border.Child = dock;
			}
			else if (cell is Microsoft.Maui.Controls.EntryCell entryCell)
			{
				var dock = new DockPanel();
				if (!string.IsNullOrEmpty(entryCell.Label))
				{
					dock.Children.Add(new TextBlock
					{
						Text = entryCell.Label,
						VerticalAlignment = System.Windows.VerticalAlignment.Center,
						FontSize = 14,
						Margin = new System.Windows.Thickness(0, 0, 8, 0),
					});
				}
				var textBox = new System.Windows.Controls.TextBox
				{
					Text = entryCell.Text ?? "",
					VerticalAlignment = System.Windows.VerticalAlignment.Center,
				};
				textBox.TextChanged += (s, e) => entryCell.Text = textBox.Text;
				dock.Children.Add(textBox);
				border.Child = dock;
			}
			else
			{
				border.Child = new TextBlock { Text = cell.ToString() ?? "Cell", FontSize = 14 };
			}

			return border;
		}
	}

	/// <summary>
	/// CarouselView — horizontal swipeable collection.
	/// WPF approximation: ListBox with horizontal panel + arrow buttons.
	/// </summary>
	public partial class CarouselViewHandler : WPFViewHandler<Microsoft.Maui.Controls.CarouselView, WGrid>
	{
		System.Windows.Controls.ListBox? _listBox;

		public static readonly PropertyMapper<Microsoft.Maui.Controls.CarouselView, CarouselViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.ItemsView.ItemsSource)] = MapItemsSource,
				[nameof(Microsoft.Maui.Controls.CarouselView.CurrentItem)] = MapCurrentItem,
				[nameof(Microsoft.Maui.Controls.CarouselView.Position)] = MapPosition,
			};

		public CarouselViewHandler() : base(Mapper) { }

		protected override WGrid CreatePlatformView()
		{
			var grid = new WGrid();
			grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
			grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

			var leftBtn = new System.Windows.Controls.Button { Content = "◀", Width = 30, VerticalAlignment = System.Windows.VerticalAlignment.Center };
			var rightBtn = new System.Windows.Controls.Button { Content = "▶", Width = 30, VerticalAlignment = System.Windows.VerticalAlignment.Center };
			WGrid.SetColumn(leftBtn, 0);
			WGrid.SetColumn(rightBtn, 2);

			_listBox = new System.Windows.Controls.ListBox
			{
				BorderThickness = new System.Windows.Thickness(0),
				Background = System.Windows.Media.Brushes.Transparent,
			};
			ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, System.Windows.Controls.ScrollBarVisibility.Hidden);
			ScrollViewer.SetVerticalScrollBarVisibility(_listBox, System.Windows.Controls.ScrollBarVisibility.Disabled);
			// Set horizontal items panel
			var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
			panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
			_listBox.ItemsPanel = new ItemsPanelTemplate(panelFactory);
			WGrid.SetColumn(_listBox, 1);

			leftBtn.Click += (s, e) =>
			{
				if (_listBox.SelectedIndex > 0)
					_listBox.SelectedIndex--;
			};
			rightBtn.Click += (s, e) =>
			{
				if (_listBox.Items.Count > 0 && _listBox.SelectedIndex < _listBox.Items.Count - 1)
					_listBox.SelectedIndex++;
			};
			_listBox.SelectionChanged += (s, e) =>
			{
				if (VirtualView != null && _listBox.SelectedItem != null)
				{
					VirtualView.CurrentItem = _listBox.SelectedItem;
					VirtualView.Position = _listBox.SelectedIndex;
				}
			};

			grid.Children.Add(leftBtn);
			grid.Children.Add(_listBox);
			grid.Children.Add(rightBtn);

			return grid;
		}

		static void MapItemsSource(CarouselViewHandler handler, Microsoft.Maui.Controls.CarouselView view)
		{
			if (handler._listBox != null)
				handler._listBox.ItemsSource = view.ItemsSource as System.Collections.IEnumerable;
		}

		static void MapCurrentItem(CarouselViewHandler handler, Microsoft.Maui.Controls.CarouselView view)
		{
			if (handler._listBox != null && view.CurrentItem != null)
				handler._listBox.SelectedItem = view.CurrentItem;
		}

		static void MapPosition(CarouselViewHandler handler, Microsoft.Maui.Controls.CarouselView view)
		{
			if (handler._listBox != null && view.Position >= 0 && view.Position < handler._listBox.Items.Count)
				handler._listBox.SelectedIndex = view.Position;
		}
	}

	/// <summary>
	/// IndicatorView — page indicator dots.
	/// WPF: row of small ellipses with active one highlighted.
	/// </summary>
	public partial class IndicatorViewHandler : WPFViewHandler<Microsoft.Maui.Controls.IndicatorView, StackPanel>
	{
		public static readonly PropertyMapper<Microsoft.Maui.Controls.IndicatorView, IndicatorViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.IndicatorView.Count)] = MapCount,
				[nameof(Microsoft.Maui.Controls.IndicatorView.Position)] = MapPosition,
				[nameof(Microsoft.Maui.Controls.IndicatorView.IndicatorColor)] = MapIndicatorColor,
				[nameof(Microsoft.Maui.Controls.IndicatorView.SelectedIndicatorColor)] = MapSelectedIndicatorColor,
			};

		public IndicatorViewHandler() : base(Mapper) { }

		protected override StackPanel CreatePlatformView()
		{
			return new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			};
		}

		static void RebuildIndicators(IndicatorViewHandler handler)
		{
			var panel = handler.PlatformView;
			var view = handler.VirtualView;
			if (panel == null || view == null) return;

			panel.Children.Clear();
			for (int i = 0; i < view.Count; i++)
			{
				var dot = new System.Windows.Shapes.Ellipse
				{
					Width = view.IndicatorSize > 0 ? view.IndicatorSize : 8,
					Height = view.IndicatorSize > 0 ? view.IndicatorSize : 8,
					Margin = new System.Windows.Thickness(3),
				};

				var color = i == view.Position ? view.SelectedIndicatorColor : view.IndicatorColor;
				if (color != null)
				{
					dot.Fill = new System.Windows.Media.SolidColorBrush(
						System.Windows.Media.Color.FromArgb(
							(byte)(color.Alpha * 255), (byte)(color.Red * 255),
							(byte)(color.Green * 255), (byte)(color.Blue * 255)));
				}
				else
				{
					dot.Fill = i == view.Position
						? System.Windows.Media.Brushes.DodgerBlue
						: System.Windows.Media.Brushes.LightGray;
				}

				panel.Children.Add(dot);
			}
		}

		static void MapCount(IndicatorViewHandler handler, Microsoft.Maui.Controls.IndicatorView view) => RebuildIndicators(handler);
		static void MapPosition(IndicatorViewHandler handler, Microsoft.Maui.Controls.IndicatorView view) => RebuildIndicators(handler);
		static void MapIndicatorColor(IndicatorViewHandler handler, Microsoft.Maui.Controls.IndicatorView view) => RebuildIndicators(handler);
		static void MapSelectedIndicatorColor(IndicatorViewHandler handler, Microsoft.Maui.Controls.IndicatorView view) => RebuildIndicators(handler);
	}
}




