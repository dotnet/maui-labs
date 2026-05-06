#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using WButton = global::System.Windows.Controls.Button;
using WGrid = global::System.Windows.Controls.Grid;
using WBorder = global::System.Windows.Controls.Border;
using WBrush = global::System.Windows.Media.Brush;
using WColumnDefinition = global::System.Windows.Controls.ColumnDefinition;
using WRowDefinition = global::System.Windows.Controls.RowDefinition;
using WVisibility = global::System.Windows.Visibility;
using WThickness = global::System.Windows.Thickness;
using WHorizontalAlignment = global::System.Windows.HorizontalAlignment;
using WVerticalAlignment = global::System.Windows.VerticalAlignment;
using WSolidColorBrush = global::System.Windows.Media.SolidColorBrush;
using WColor = global::System.Windows.Media.Color;
using WGridLength = global::System.Windows.GridLength;
using WGridUnitType = global::System.Windows.GridUnitType;
using WFontWeights = global::System.Windows.FontWeights;

namespace Microsoft.Maui.Handlers.WPF
{
	/// <summary>
	/// Shell container — provides flyout navigation, toolbar with back button, and content area.
	/// </summary>
	public class ShellContainerView : WGrid
	{
		readonly WGrid _flyoutPanel;
		readonly global::System.Windows.Controls.StackPanel _flyoutItems;
		readonly global::System.Windows.Controls.ContentControl _flyoutHeaderHost;
		readonly global::System.Windows.Controls.ContentControl _flyoutFooterHost;
		readonly global::System.Windows.Controls.ContentControl _contentArea;
		readonly global::System.Windows.Controls.TabControl _tabControl;
		readonly WButton _hamburgerButton;
		readonly WButton _backButton;
		readonly global::System.Windows.Controls.TextBlock _titleLabel;
		readonly WBorder _flyoutOverlay;
		readonly global::System.Windows.Controls.DockPanel _toolbar;
		readonly global::System.Windows.Controls.StackPanel _toolbarItemsPanel;
		bool _flyoutOpen;
		FlyoutBehavior _currentBehavior = FlyoutBehavior.Flyout;

		public Action<ShellItem>? OnShellItemSelected { get; set; }
		public Action? OnBackButtonClicked { get; set; }
		public IMauiContext? MauiContext { get; set; }

		public ShellContainerView()
		{
			ColumnDefinitions.Add(new WColumnDefinition { Width = WGridLength.Auto });
			ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(1, WGridUnitType.Star) });

			// Flyout panel with header / scrollable items / footer using DockPanel
			_flyoutPanel = new WGrid
			{
				Width = 250,
				Visibility = WVisibility.Collapsed,
				ClipToBounds = true,
			};
			_flyoutPanel.RowDefinitions.Add(new WRowDefinition { Height = WGridLength.Auto }); // header
			_flyoutPanel.RowDefinitions.Add(new WRowDefinition { Height = new WGridLength(1, WGridUnitType.Star) }); // items
			_flyoutPanel.RowDefinitions.Add(new WRowDefinition { Height = WGridLength.Auto }); // footer

			_flyoutHeaderHost = new global::System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = WHorizontalAlignment.Stretch,
				ClipToBounds = true,
			};
			SetRow(_flyoutHeaderHost, 0);
			_flyoutPanel.Children.Add(_flyoutHeaderHost);

			var scrollViewer = new global::System.Windows.Controls.ScrollViewer
			{
				VerticalScrollBarVisibility = global::System.Windows.Controls.ScrollBarVisibility.Auto,
			};
			_flyoutItems = new global::System.Windows.Controls.StackPanel();
			scrollViewer.Content = _flyoutItems;
			SetRow(scrollViewer, 1);
			_flyoutPanel.Children.Add(scrollViewer);

			_flyoutFooterHost = new global::System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = WHorizontalAlignment.Stretch,
				VerticalContentAlignment = WVerticalAlignment.Stretch,
				ClipToBounds = true,
			};
			SetRow(_flyoutFooterHost, 2);
			_flyoutPanel.Children.Add(_flyoutFooterHost);

			SetColumn(_flyoutPanel, 0);

			_flyoutOverlay = new WBorder
			{
				Background = new WSolidColorBrush(WColor.FromArgb(80, 0, 0, 0)),
				Visibility = WVisibility.Collapsed,
			};
			_flyoutOverlay.MouseLeftButtonDown += (s, e) => ToggleFlyout(false);

			// Main content grid
			var mainGrid = new WGrid();
			mainGrid.RowDefinitions.Add(new WRowDefinition { Height = new WGridLength(44) });
			mainGrid.RowDefinitions.Add(new WRowDefinition { Height = WGridLength.Auto });
			mainGrid.RowDefinitions.Add(new WRowDefinition { Height = new WGridLength(1, WGridUnitType.Star) });

			// Toolbar
			_toolbar = new global::System.Windows.Controls.DockPanel
			{
				Height = 44,
			};

			_hamburgerButton = new WButton
			{
				Content = "☰", FontSize = 18, Width = 44, Height = 44,
				Visibility = WVisibility.Collapsed,
			};
			_hamburgerButton.Click += (s, e) => ToggleFlyout(!_flyoutOpen);
			global::System.Windows.Controls.DockPanel.SetDock(_hamburgerButton, global::System.Windows.Controls.Dock.Left);
			_toolbar.Children.Add(_hamburgerButton);

			_backButton = new WButton
			{
				Content = "← Back", Margin = new WThickness(4, 4, 4, 4),
				Padding = new WThickness(8, 2, 8, 2),
				VerticalAlignment = WVerticalAlignment.Center,
				Visibility = WVisibility.Collapsed,
			};
			_backButton.Click += (s, e) => OnBackButtonClicked?.Invoke();
			global::System.Windows.Controls.DockPanel.SetDock(_backButton, global::System.Windows.Controls.Dock.Left);
			_toolbar.Children.Add(_backButton);

			// Toolbar items (right side)
			_toolbarItemsPanel = new global::System.Windows.Controls.StackPanel
			{
				Orientation = global::System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = WHorizontalAlignment.Right,
			};
			global::System.Windows.Controls.DockPanel.SetDock(_toolbarItemsPanel, global::System.Windows.Controls.Dock.Right);
			_toolbar.Children.Add(_toolbarItemsPanel);

			_titleLabel = new global::System.Windows.Controls.TextBlock
			{
				FontSize = 16, FontWeight = WFontWeights.SemiBold,
				VerticalAlignment = WVerticalAlignment.Center,
				Margin = new WThickness(8, 0, 0, 0),
			};
			_toolbar.Children.Add(_titleLabel);

			SetRow(_toolbar, 0);
			mainGrid.Children.Add(_toolbar);

			_tabControl = new global::System.Windows.Controls.TabControl { Visibility = WVisibility.Collapsed };
			_tabControl.SelectionChanged += TabControl_SelectionChanged;
			SetRow(_tabControl, 1);
			mainGrid.Children.Add(_tabControl);

			_contentArea = new global::System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = WHorizontalAlignment.Stretch,
				VerticalContentAlignment = WVerticalAlignment.Stretch,
			};
			SetRow(_contentArea, 2);
			mainGrid.Children.Add(_contentArea);

			SetColumn(mainGrid, 1);
			Children.Add(_flyoutPanel);
			Children.Add(mainGrid);
		}

		void TabControl_SelectionChanged(object sender, global::System.Windows.Controls.SelectionChangedEventArgs e) { }

		static bool IsDarkTheme()
		{
			var app = Microsoft.Maui.Controls.Application.Current;
			if (app != null && app.RequestedTheme != AppTheme.Unspecified)
				return app.RequestedTheme == AppTheme.Dark;
			return ThemeManager.GetCurrentTheme() == AppTheme.Dark;
		}

		void UpdateFlyoutTheme()
		{
			bool dark = IsDarkTheme();
			_flyoutPanel.Background = dark
				? new WSolidColorBrush(WColor.FromRgb(30, 30, 30))
				: new WSolidColorBrush(WColor.FromRgb(240, 240, 240));

			// Update fallback label colors in flyout items
			var fg = dark
				? new WSolidColorBrush(WColor.FromRgb(255, 255, 255))
				: new WSolidColorBrush(WColor.FromRgb(0, 0, 0));
			foreach (var child in _flyoutItems.Children)
			{
				if (child is WButton btn && btn.Content is WGrid g)
				{
					foreach (var gc in g.Children)
					{
						if (gc is global::System.Windows.Controls.TextBlock tb)
							tb.Foreground = fg;
					}
				}
			}
		}

		void UpdateToolbarTheme()
		{
			bool dark = IsDarkTheme();
			var bg = dark
				? new WSolidColorBrush(WColor.FromRgb(30, 30, 30))
				: new WSolidColorBrush(WColor.FromRgb(240, 240, 240));
			var fg = dark
				? new WSolidColorBrush(WColor.FromRgb(255, 255, 255))
				: new WSolidColorBrush(WColor.FromRgb(0, 0, 0));
			_toolbar.Background = bg;
			_titleLabel.Foreground = fg;
			_hamburgerButton.Foreground = fg;
			_hamburgerButton.Background = global::System.Windows.Media.Brushes.Transparent;
			_hamburgerButton.BorderThickness = new WThickness(0);
			_backButton.Foreground = fg;
			_backButton.Background = global::System.Windows.Media.Brushes.Transparent;
			_backButton.BorderThickness = new WThickness(0);
		}

		/// <summary>
		/// Call when theme changes to update Shell chrome colors.
		/// </summary>
		public void UpdateTheme()
		{
			UpdateFlyoutTheme();
			UpdateToolbarTheme();
		}

		public void ToggleFlyout(bool open)
		{
			if (_currentBehavior == FlyoutBehavior.Locked)
				return; // Locked flyout cannot be toggled
			_flyoutOpen = open;
			_flyoutPanel.Visibility = open ? WVisibility.Visible : WVisibility.Collapsed;
			_flyoutOverlay.Visibility = open ? WVisibility.Visible : WVisibility.Collapsed;
		}

		public void SetFlyoutBehavior(FlyoutBehavior behavior)
		{
			_currentBehavior = behavior;
			switch (behavior)
			{
				case FlyoutBehavior.Disabled:
					_hamburgerButton.Visibility = WVisibility.Collapsed;
					_flyoutPanel.Visibility = WVisibility.Collapsed;
					_flyoutOpen = false;
					break;
				case FlyoutBehavior.Flyout:
					_hamburgerButton.Visibility = WVisibility.Visible;
					if (!_flyoutOpen)
						_flyoutPanel.Visibility = WVisibility.Collapsed;
					break;
				case FlyoutBehavior.Locked:
					_hamburgerButton.Visibility = WVisibility.Collapsed;
					_flyoutPanel.Visibility = WVisibility.Visible;
					_flyoutOverlay.Visibility = WVisibility.Collapsed;
					_flyoutOpen = true;
					break;
			}
		}

		public void SetFlyoutBackground(WBrush? brush)
		{
			if (brush != null) _flyoutPanel.Background = brush;
		}

		public void SetFlyoutWidth(double width)
		{
			_flyoutPanel.Width = width;
		}

		public void SetBarBackground(WBrush? brush)
		{
			if (brush != null) _toolbar.Background = brush;
		}

		public void SetBarForeground(WBrush? brush)
		{
			if (brush != null)
			{
				_titleLabel.Foreground = brush;
				_hamburgerButton.Foreground = brush;
				_backButton.Foreground = brush;
			}
		}

		public void BuildFlyoutItems(Shell shell)
		{
			_flyoutItems.Children.Clear();
			_tabControl.Items.Clear();

			// Render FlyoutHeader template
			if (shell.FlyoutHeaderTemplate != null && MauiContext != null)
			{
				try
				{
					var headerContent = shell.FlyoutHeaderTemplate.CreateContent();
					if (headerContent is View headerView)
					{
						headerView.BindingContext = shell.FlyoutHeader ?? shell.BindingContext;

						// Build native WPF header to avoid MAUI LayoutPanel sizing issues.
						// The header template is typically a Grid with an Image centered.
						var nativeHeader = BuildNativeHeader(headerView);
						if (nativeHeader != null)
						{
							_flyoutHeaderHost.Content = nativeHeader;
						}
						else
						{
							var platformHeader = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)headerView, MauiContext);
							if (platformHeader is FrameworkElement fe)
							{
								// Force explicit height from the view's RowDefinitions or HeightRequest
								double h = headerView.HeightRequest;
								if (double.IsNaN(h) || h <= 0)
									h = 120; // default flyout header height
								fe.Height = h;
							}
							_flyoutHeaderHost.Content = platformHeader;
						}
					}
				}
				catch { }
			}
			else if (shell.FlyoutHeader is View headerFallback && MauiContext != null)
			{
				try
				{
					var platformHeader = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)headerFallback, MauiContext);
					_flyoutHeaderHost.Content = platformHeader;
				}
				catch { }
			}

			// Render FlyoutFooter template
			if (shell.FlyoutFooterTemplate != null && MauiContext != null)
			{
				try
				{
					var footerContent = shell.FlyoutFooterTemplate.CreateContent();
					if (footerContent is View footerView)
					{
						footerView.BindingContext = shell.FlyoutFooter ?? shell.BindingContext;

						// Build a native WPF rendering of the footer for reliable layout.
						// The MAUI LayoutPanel can miscalculate positions in constrained areas.
						var nativeFooter = BuildNativeFooter(footerView);
						if (nativeFooter != null)
						{
							_flyoutFooterHost.Content = nativeFooter;
						}
						else
						{
							var platformFooter = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)footerView, MauiContext);
							if (platformFooter is FrameworkElement fe)
								fe.MinHeight = 80;
							_flyoutFooterHost.Content = platformFooter;
						}
					}
				}
				catch { }
			}

			bool hasFlyoutItems = false;
			bool hasTabs = false;
			var itemTemplate = shell.ItemTemplate;

			foreach (var item in shell.Items)
			{
				if (item.FlyoutItemIsVisible)
				{
					hasFlyoutItems = true;
					var capturedItem = item;

					FrameworkElement itemElement;

					// Try to use the Shell.ItemTemplate (CustomFlyoutItem)
					if (itemTemplate != null && MauiContext != null)
					{
						try
						{
							var resolved = itemTemplate;
							if (itemTemplate is Microsoft.Maui.Controls.DataTemplateSelector selector)
								resolved = selector.SelectTemplate(item, shell);

							var content = resolved?.CreateContent() as View;
							if (content != null)
							{
								content.BindingContext = item;
								var platformItem = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)content, MauiContext);
								// Use Button instead of Border for UIAutomation InvokePattern support
								var container = new WButton
								{
									Content = (System.Windows.UIElement)platformItem,
									Cursor = global::System.Windows.Input.Cursors.Hand,
									Background = global::System.Windows.Media.Brushes.Transparent,
									BorderThickness = new WThickness(0),
									Padding = new WThickness(0, 4, 0, 4),
									HorizontalContentAlignment = WHorizontalAlignment.Stretch,
								};
								container.Click += (s, e) =>
								{
									OnShellItemSelected?.Invoke(capturedItem);
									if (_currentBehavior != FlyoutBehavior.Locked)
										ToggleFlyout(false);
								};
								itemElement = container;
								_flyoutItems.Children.Add(itemElement);
								continue;
							}
						}
						catch { }
					}

					// Fallback: simple button with icon if available
					var btn = new WButton
					{
						HorizontalContentAlignment = WHorizontalAlignment.Stretch,
						Background = global::System.Windows.Media.Brushes.Transparent,
						BorderThickness = new WThickness(0),
						Padding = new WThickness(0),
						Tag = item,
						Cursor = global::System.Windows.Input.Cursors.Hand,
					};

					var itemGrid = new WGrid();
					itemGrid.ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(15) }); // spacer
					itemGrid.ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(50) }); // icon
					itemGrid.ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(1, WGridUnitType.Star) }); // text

					// Try to render FlyoutIcon
					var flyoutIcon = item.FlyoutIcon;
					if (flyoutIcon != null && MauiContext != null)
					{
						try
						{
							var img = new global::System.Windows.Controls.Image
							{
								Width = 35, Height = 35,
								Margin = new WThickness(5),
								VerticalAlignment = WVerticalAlignment.Center,
							};
							SetIconSource(img, flyoutIcon, MauiContext);
							SetColumn(img, 1);
							itemGrid.Children.Add(img);
						}
						catch { }
					}

					var label = new global::System.Windows.Controls.TextBlock
					{
						Text = item.Title ?? item.Route ?? "Item",
						FontSize = 14,
						FontStyle = global::System.Windows.FontStyles.Italic,
						VerticalAlignment = WVerticalAlignment.Center,
						Margin = new WThickness(4, 8, 8, 8),
					};
					SetColumn(label, 2);
					itemGrid.Children.Add(label);

					btn.Content = itemGrid;
					btn.Click += (s, e) =>
					{
						OnShellItemSelected?.Invoke(capturedItem);
						if (_currentBehavior != FlyoutBehavior.Locked)
							ToggleFlyout(false);
					};
					_flyoutItems.Children.Add(btn);
				}

				if (item.Items.Count > 1)
				{
					hasTabs = true;
					foreach (var section in item.Items)
					{
						var tabItem = new global::System.Windows.Controls.TabItem { Header = section.Title ?? section.Route ?? "Tab", Tag = section };
						_tabControl.Items.Add(tabItem);
					}
				}
			}

			if (hasFlyoutItems)
				SetFlyoutBehavior(shell.FlyoutBehavior);
			else
				SetFlyoutBehavior(FlyoutBehavior.Disabled);

			_tabControl.Visibility = hasTabs ? WVisibility.Visible : WVisibility.Collapsed;

			// Apply theme after building items
			UpdateFlyoutTheme();
			UpdateToolbarTheme();
		}

		static void SetIconSource(global::System.Windows.Controls.Image img, Microsoft.Maui.Controls.ImageSource? source, IMauiContext mauiContext)
		{
			if (source == null) return;
			try
			{
				if (source is Microsoft.Maui.Controls.FileImageSource fileSource)
				{
					var fileName = fileSource.File;
					if (string.IsNullOrEmpty(fileName)) return;
					var resolvedPath = ImageHandler.ResolveImagePath(fileName);
					if (resolvedPath != null)
					{
						if (resolvedPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
						{
							var svgSource = ImageHandler.RenderSvgToBitmap(resolvedPath, 35, 35);
							if (svgSource != null) { img.Source = svgSource; return; }
						}
						img.Source = new global::System.Windows.Media.Imaging.BitmapImage(new Uri(resolvedPath, UriKind.Absolute));
					}
					else
					{
						img.Source = new global::System.Windows.Media.Imaging.BitmapImage(new Uri(fileName, UriKind.RelativeOrAbsolute));
					}
				}
				else if (source is Microsoft.Maui.Controls.FontImageSource fontSource)
				{
					// Render font glyph as text in the image's parent
					if (img.Parent is WGrid grid)
					{
						var col = GetColumn(img);
						grid.Children.Remove(img);
						var tb = new global::System.Windows.Controls.TextBlock
						{
							Text = fontSource.Glyph,
							FontSize = fontSource.Size > 0 ? fontSource.Size : 16,
							HorizontalAlignment = WHorizontalAlignment.Center,
							VerticalAlignment = WVerticalAlignment.Center,
							Margin = new WThickness(5),
						};
						if (fontSource.Color != null)
						{
							var c = fontSource.Color;
							tb.Foreground = new WSolidColorBrush(WColor.FromArgb(
								(byte)(c.Alpha * 255), (byte)(c.Red * 255),
								(byte)(c.Green * 255), (byte)(c.Blue * 255)));
						}
						if (!string.IsNullOrEmpty(fontSource.FontFamily))
						{
							try
							{
								var fontManager = mauiContext.Services.GetService<IFontManager>();
								if (fontManager is WPFFontManager wpfFontManager)
								{
									var family = wpfFontManager.GetFontFamily(Microsoft.Maui.Font.OfSize(fontSource.FontFamily, fontSource.Size));
									tb.FontFamily = family;
								}
							}
							catch { }
						}
						SetColumn(tb, col);
						grid.Children.Add(tb);
					}
				}
			}
			catch { }
		}

		/// <summary>
		/// Builds a native WPF rendering of the flyout header template.
		/// MAUI LayoutPanel inside a WPF Auto-height ContentControl gets ∞ bounds,
		/// causing the header image to render at 0x0 or broken coordinates.
		/// This walks the MAUI view tree and creates native WPF Image controls.
		/// </summary>
		FrameworkElement? BuildNativeHeader(View headerView)
		{
			try
			{
				// Determine header height from the view (RowDefinitions="120" in the XAML)
				double headerHeight = 120;
				if (headerView.HeightRequest > 0)
					headerHeight = headerView.HeightRequest;
				else if (headerView is Microsoft.Maui.Controls.Grid grid && grid.RowDefinitions.Count > 0)
				{
					double total = 0;
					foreach (var rd in grid.RowDefinitions)
					{
						if (rd.Height.IsAbsolute)
							total += rd.Height.Value;
					}
					if (total > 0) headerHeight = total;
				}

				var container = new WGrid
				{
					Height = headerHeight,
					HorizontalAlignment = WHorizontalAlignment.Stretch,
				};

				// Walk children to find images
				WalkHeaderView(headerView, container);

				if (container.Children.Count > 0)
					return container;
			}
			catch { }
			return null;
		}

		void WalkHeaderView(Microsoft.Maui.Controls.Element element, WGrid container)
		{
			if (element is Microsoft.Maui.Controls.Image img)
			{
				var wpfImage = new global::System.Windows.Controls.Image
				{
					HorizontalAlignment = WHorizontalAlignment.Center,
					VerticalAlignment = WVerticalAlignment.Center,
					Stretch = System.Windows.Media.Stretch.Uniform,
				};
				if (img.WidthRequest > 0) wpfImage.Width = img.WidthRequest;
				if (img.HeightRequest > 0) wpfImage.Height = img.HeightRequest;

				// Resolve image source
				if (img.Source is Microsoft.Maui.Controls.FileImageSource fis && !string.IsNullOrEmpty(fis.File))
				{
					var resolvedPath = ImageHandler.ResolveImagePath(fis.File);
					if (resolvedPath != null)
					{
						try
						{
							if (resolvedPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
							{
								var svgSource = ImageHandler.RenderSvgToBitmap(resolvedPath);
								if (svgSource != null)
									wpfImage.Source = svgSource;
							}
							else
							{
								wpfImage.Source = new System.Windows.Media.Imaging.BitmapImage(
									new Uri(resolvedPath, UriKind.Absolute));
							}
						}
						catch { }
					}
				}

				container.Children.Add(wpfImage);
				return;
			}

			// Recurse into child elements
			if (element is Microsoft.Maui.Controls.Layout layout)
			{
				foreach (var child in layout.Children)
				{
					if (child is Microsoft.Maui.Controls.Element childElement)
						WalkHeaderView(childElement, container);
				}
			}
			else if (element is Microsoft.Maui.Controls.ContentView cv && cv.Content is Microsoft.Maui.Controls.Element cvContent)
			{
				WalkHeaderView(cvContent, container);
			}
		}

		/// <summary>
		/// Builds a native WPF rendering of the footer template content.
		/// This avoids MAUI LayoutPanel positioning issues in constrained flyout areas.
		/// Looks for Picker controls with ItemsSource and creates a WPF ComboBox.
		/// </summary>
		FrameworkElement? BuildNativeFooter(View footerView)
		{
			try
			{
				var stack = new global::System.Windows.Controls.StackPanel
				{
					Margin = new WThickness(15),
				};

				WalkFooterView(footerView, stack);

				if (stack.Children.Count > 0)
					return stack;
			}
			catch { }
			return null;
		}

		void WalkFooterView(Microsoft.Maui.Controls.Element element, global::System.Windows.Controls.StackPanel container)
		{
			if (element is Microsoft.Maui.Controls.Label label)
			{
				bool dark = IsDarkTheme();
				container.Children.Add(new global::System.Windows.Controls.TextBlock
				{
					Text = label.Text ?? string.Empty,
					FontSize = label.FontSize > 0 ? label.FontSize : 14,
					Margin = new WThickness(0, 0, 0, 4),
					Foreground = dark ? new WSolidColorBrush(WColor.FromRgb(200, 200, 200))
									  : new WSolidColorBrush(WColor.FromRgb(60, 60, 60)),
				});
			}
			else if (element is Microsoft.Maui.Controls.Picker picker)
			{
				var combo = new global::System.Windows.Controls.ComboBox
				{
					MinWidth = 100,
					Margin = new WThickness(0, 2, 0, 0),
				};

				// Populate items
				if (picker.ItemsSource != null)
				{
					foreach (var item in picker.ItemsSource)
						combo.Items.Add(item?.ToString() ?? string.Empty);
				}
				else
				{
					for (int i = 0; i < picker.Items.Count; i++)
						combo.Items.Add(picker.Items[i]);
				}

				// Set initial selection
				if (picker.SelectedIndex >= 0 && picker.SelectedIndex < combo.Items.Count)
					combo.SelectedIndex = picker.SelectedIndex;

				// Two-way sync: ComboBox → MAUI Picker
				combo.SelectionChanged += (s, e) =>
				{
					if (combo.SelectedIndex >= 0)
						picker.SelectedIndex = combo.SelectedIndex;
				};

				// MAUI Picker → ComboBox (for programmatic changes)
				picker.PropertyChanged += (s, e) =>
				{
					if (e.PropertyName == nameof(Microsoft.Maui.Controls.Picker.SelectedIndex))
					{
						if (combo.SelectedIndex != picker.SelectedIndex && picker.SelectedIndex >= 0)
							combo.Dispatcher.InvokeAsync(() => combo.SelectedIndex = picker.SelectedIndex);
					}
				};

				container.Children.Add(combo);
			}

			// Recurse into child elements
			if (element is Microsoft.Maui.Controls.Layout layout)
			{
				foreach (var child in layout.Children)
				{
					if (child is Microsoft.Maui.Controls.Element childElement)
						WalkFooterView(childElement, container);
				}
			}
			else if (element is Microsoft.Maui.Controls.ContentView cv && cv.Content is Microsoft.Maui.Controls.Element cvContent)
			{
				WalkFooterView(cvContent, container);
			}
		}

		public void ShowPage(FrameworkElement? content, string? title, bool showBack)
		{
			_contentArea.Content = content;
			_titleLabel.Text = title ?? string.Empty;
			_backButton.Visibility = showBack ? WVisibility.Visible : WVisibility.Collapsed;
		}

		public void SetContent(FrameworkElement? content) => _contentArea.Content = content;
	}

	public partial class ShellHandler : WPFViewHandler<Shell, ShellContainerView>
	{
		public static IPropertyMapper<Shell, ShellHandler> Mapper =
			new PropertyMapper<Shell, ShellHandler>(ViewMapper)
			{
				[nameof(Shell.FlyoutBehavior)] = MapFlyoutBehavior,
				[nameof(Shell.FlyoutBackgroundColor)] = MapFlyoutBackground,
				[nameof(Shell.FlyoutBackground)] = MapFlyoutBackgroundBrush,
				[nameof(Shell.FlyoutWidth)] = MapFlyoutWidth,
				[nameof(Shell.BackgroundColor)] = MapShellBackground,
				[nameof(Shell.Items)] = MapItems,
				[nameof(Shell.CurrentItem)] = MapCurrentItem,
			};

		public static CommandMapper<Shell, ShellHandler> CommandMapper =
			new(ViewCommandMapper) { };

		public ShellHandler() : base(Mapper, CommandMapper) { }

		protected override ShellContainerView CreatePlatformView()
		{
			var container = new ShellContainerView();
			container.OnShellItemSelected = OnShellItemSelected;
			container.OnBackButtonClicked = OnBackButtonClicked;
			container.MauiContext = MauiContext;
			return container;
		}

		protected override void ConnectHandler(ShellContainerView platformView)
		{
			base.ConnectHandler(platformView);
			if (VirtualView != null)
			{
				VirtualView.Navigated += OnShellNavigated;
				VirtualView.Navigating += OnShellNavigating;
				VirtualView.PropertyChanged += OnShellPropertyChanged;
				if (MauiContext != null)
				{
					platformView.MauiContext = MauiContext;
					platformView.BuildFlyoutItems(VirtualView);
					
					// Ensure theme-related properties are applied after items built
					MapFlyoutBackground(this, VirtualView);
					MapFlyoutBackgroundBrush(this, VirtualView);
					MapShellBackground(this, VirtualView);

					// Apply FlyoutBehavior (OnIdiom should have resolved by now)
					MapFlyoutBehavior(this, VirtualView);

					// Apply FlyoutWidth if set
					if (VirtualView.FlyoutWidth > 0)
						platformView.SetFlyoutWidth(VirtualView.FlyoutWidth);

					ShowCurrentPage();
				}

				// Subscribe to theme changes to update Shell chrome
				ThemeManager.ThemeChanged += OnThemeChanged;
			}
		}

		protected override void DisconnectHandler(ShellContainerView platformView)
		{
			if (VirtualView != null)
			{
				VirtualView.Navigated -= OnShellNavigated;
				VirtualView.Navigating -= OnShellNavigating;
				VirtualView.PropertyChanged -= OnShellPropertyChanged;
			}
			ThemeManager.ThemeChanged -= OnThemeChanged;
			base.DisconnectHandler(platformView);
		}

		void OnThemeChanged(AppTheme theme)
		{
			PlatformView?.Dispatcher.InvokeAsync(() =>
			{
				PlatformView?.UpdateTheme();
				// Re-show current page to pick up AppThemeBinding changes
				ShowCurrentPage();
			});
		}

		void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
		{
		}

		void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
		{
			PlatformView?.Dispatcher.InvokeAsync(() => ShowCurrentPage(), 
				System.Windows.Threading.DispatcherPriority.Background);
		}

		void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "CurrentPage" || e.PropertyName == "CurrentItem")
			{
				PlatformView?.Dispatcher.InvokeAsync(() => ShowCurrentPage(),
					System.Windows.Threading.DispatcherPriority.Background);
			}
			else if (e.PropertyName == nameof(Shell.FlyoutBehavior))
			{
				PlatformView?.Dispatcher.InvokeAsync(() =>
					PlatformView?.SetFlyoutBehavior(VirtualView!.FlyoutBehavior));
			}
			else if (e.PropertyName == nameof(Shell.FlyoutWidth))
			{
				if (VirtualView != null && VirtualView.FlyoutWidth > 0)
					PlatformView?.Dispatcher.InvokeAsync(() =>
						PlatformView?.SetFlyoutWidth(VirtualView.FlyoutWidth));
			}
			else if (e.PropertyName == nameof(Shell.FlyoutBackgroundColor))
			{
				if (VirtualView != null)
					MapFlyoutBackground(this, VirtualView);
			}
		}

		void ShowCurrentPage()
		{
			if (VirtualView == null || MauiContext == null) return;

			try
			{
				Microsoft.Maui.Controls.Page? currentPage = VirtualView.CurrentPage;
				var section = VirtualView.CurrentItem?.CurrentItem;

				if (currentPage == null)
				{
					// Check navigation stack first (for pushed pages via GoToAsync)
					if (section?.Stack?.Count > 1)
						currentPage = section.Stack[section.Stack.Count - 1];

					// Fall back to ShellContent template
					if (currentPage == null)
					{
						var content = section?.CurrentItem;
						if (content != null)
						{
							try { currentPage = ((IShellContentController)content).Page; }
							catch { }

							if (currentPage == null)
							{
								currentPage = content.ContentTemplate?.CreateContent() as Microsoft.Maui.Controls.Page
									?? content.Content as Microsoft.Maui.Controls.Page;
							}
						}
					}
				}

				if (currentPage == null) return;

				var platformView = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)currentPage, MauiContext);
				var title = currentPage.Title ?? VirtualView.CurrentItem?.Title ?? string.Empty;
				bool hasNavStack = section?.Stack?.Count > 1;

				PlatformView.ShowPage(platformView as FrameworkElement, title, hasNavStack);
			}
			catch { }
		}

		async void OnShellItemSelected(ShellItem item)
		{
			if (VirtualView == null) return;
			
			try
			{
				// Use absolute route navigation to pop any pushed pages and show section root
				var route = item.Route;
				if (!string.IsNullOrEmpty(route))
				{
					await VirtualView.GoToAsync("//" + route);
				}
				else
				{
					VirtualView.CurrentItem = item;
				}
			}
			catch
			{
				VirtualView.CurrentItem = item;
			}
			ShowCurrentPage();
		}

		async void OnBackButtonClicked()
		{
			if (VirtualView == null) return;

			try
			{
				await VirtualView.GoToAsync("..");
				// GoToAsync may not fire Navigated, so refresh manually
				ShowCurrentPage();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Shell] Back navigation failed: {ex.Message}");
			}
		}

		static void MapFlyoutBehavior(ShellHandler handler, Shell shell)
			=> handler.PlatformView.SetFlyoutBehavior(shell.FlyoutBehavior);

		static void MapFlyoutWidth(ShellHandler handler, Shell shell)
		{
			if (shell.FlyoutWidth > 0)
				handler.PlatformView.SetFlyoutWidth(shell.FlyoutWidth);
		}

		static void MapFlyoutBackground(ShellHandler handler, Shell shell)
		{
			if (shell.FlyoutBackgroundColor != null)
			{
				var c = shell.FlyoutBackgroundColor;
				handler.PlatformView.SetFlyoutBackground(new WSolidColorBrush(WColor.FromArgb(
					(byte)(c.Alpha * 255), (byte)(c.Red * 255),
					(byte)(c.Green * 255), (byte)(c.Blue * 255))));
			}
		}

		static void MapFlyoutBackgroundBrush(ShellHandler handler, Shell shell)
		{
			var brush = shell.FlyoutBackground;
			if (brush == null) return;

			Microsoft.Maui.Graphics.Color? color = null;
			if (brush is Microsoft.Maui.Controls.SolidColorBrush scb)
				color = scb.Color;
			else
			{
				// ImmutableBrush or other types - try to get color via reflection
				var colorProp = brush.GetType().GetProperty("Color");
				if (colorProp?.GetValue(brush) is Microsoft.Maui.Graphics.Color c)
					color = c;
			}

			if (color != null)
			{
				handler.PlatformView.SetFlyoutBackground(new WSolidColorBrush(WColor.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255))));
			}
		}

		static void MapShellBackground(ShellHandler handler, Shell shell)
		{
			if (shell.BackgroundColor != null)
			{
				var c = shell.BackgroundColor;
				var brush = new WSolidColorBrush(WColor.FromArgb(
					(byte)(c.Alpha * 255), (byte)(c.Red * 255),
					(byte)(c.Green * 255), (byte)(c.Blue * 255)));
				handler.PlatformView.SetBarBackground(brush);
			}
			else
			{
				handler.PlatformView.UpdateTheme();
			}
		}

		static void MapItems(ShellHandler handler, Shell shell)
		{
			handler.PlatformView.BuildFlyoutItems(shell);
			handler.ShowCurrentPage();
		}

		static void MapCurrentItem(ShellHandler handler, Shell shell)
			=> handler.ShowCurrentPage();
	}
}
