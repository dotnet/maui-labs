#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
		readonly HashSet<DependencyObject> _registeredNativeElements = new();
		readonly Dictionary<ShellItem, FlyoutTemplateView> _flyoutTemplateItems = new();
		FlyoutTemplateView? _flyoutHeader;
		FlyoutTemplateView? _flyoutFooter;
		Microsoft.Maui.Controls.Page? _backButtonOwner;
		bool _flyoutOpen;
		bool _updatingTabs;
		FlyoutBehavior _currentBehavior = FlyoutBehavior.Flyout;

		public Action<ShellItem>? OnShellItemSelected { get; set; }
		internal Action<ShellSection>? OnShellSectionSelected { get; set; }
		public Action? OnBackButtonClicked { get; set; }
		public Action<bool>? OnFlyoutOpenChanged { get; set; }
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

		void TabControl_SelectionChanged(object sender, global::System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (!_updatingTabs && ReferenceEquals(e.OriginalSource, _tabControl) &&
				e.AddedItems.Count > 0 &&
				_tabControl.SelectedItem is global::System.Windows.Controls.TabItem { Tag: ShellSection section })
				OnShellSectionSelected?.Invoke(section);
		}

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
			if (_currentBehavior == FlyoutBehavior.Locked
				|| _currentBehavior == FlyoutBehavior.Disabled && open)
				return;
			if (_flyoutOpen == open)
				return;
			_flyoutOpen = open;
			_flyoutPanel.Visibility = open ? WVisibility.Visible : WVisibility.Collapsed;
			_flyoutOverlay.Visibility = open ? WVisibility.Visible : WVisibility.Collapsed;
			OnFlyoutOpenChanged?.Invoke(open);
		}

		public void SetFlyoutBehavior(FlyoutBehavior behavior)
		{
			_currentBehavior = behavior;
			switch (behavior)
			{
				case FlyoutBehavior.Disabled:
					var wasOpen = _flyoutOpen;
					_hamburgerButton.Visibility = WVisibility.Collapsed;
					_flyoutPanel.Visibility = WVisibility.Collapsed;
					_flyoutOverlay.Visibility = WVisibility.Collapsed;
					_flyoutOpen = false;
					if (wasOpen)
						OnFlyoutOpenChanged?.Invoke(false);
					break;
				case FlyoutBehavior.Flyout:
					_hamburgerButton.Visibility = WVisibility.Visible;
					if (!_flyoutOpen)
						_flyoutPanel.Visibility = WVisibility.Collapsed;
					break;
				case FlyoutBehavior.Locked:
					var wasClosed = !_flyoutOpen;
					_hamburgerButton.Visibility = WVisibility.Collapsed;
					_flyoutPanel.Visibility = WVisibility.Visible;
					_flyoutOverlay.Visibility = WVisibility.Collapsed;
					_flyoutOpen = true;
					if (wasClosed)
						OnFlyoutOpenChanged?.Invoke(true);
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
			UnregisterNativeElements();
			RegisterNativeElement(shell, _hamburgerButton, "ShellFlyoutToggle");
			UpdateBackButtonRegistration(shell.CurrentPage);

			UpdateFlyoutTemplate(shell, header: true);
			UpdateFlyoutTemplate(shell, header: false);

			bool hasFlyoutItems = false;
			var itemTemplate = shell.ItemTemplate;
			var nativeItems = new List<FrameworkElement>();
			var retainedItems = new HashSet<ShellItem>();

			foreach (var item in shell.Items)
			{
				if (item is not TabBar && item.FlyoutItemIsVisible)
				{
					hasFlyoutItems = true;
					var capturedItem = item;

					FrameworkElement itemElement;

					// Try to use the Shell.ItemTemplate (CustomFlyoutItem)
					if (itemTemplate != null && MauiContext != null)
					{
						View? pendingView = null;
						try
						{
							var resolved = itemTemplate;
							if (itemTemplate is Microsoft.Maui.Controls.DataTemplateSelector selector)
								resolved = selector.SelectTemplate(item, shell);

							if (_flyoutTemplateItems.TryGetValue(item, out var existing))
							{
								if (existing.Matches(itemTemplate, resolved, item))
								{
									nativeItems.Add(existing.Native);
									retainedItems.Add(item);
									RegisterNativeElement(item, existing.Native, "ShellFlyout");
									continue;
								}
								ReleaseFlyoutItem(item);
							}

							var content = resolved?.CreateContent() as View;
							if (content != null)
							{
								pendingView = content;
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
								RoutedEventHandler onClick = (s, e) =>
								{
									OnShellItemSelected?.Invoke(capturedItem);
									if (_currentBehavior != FlyoutBehavior.Locked)
										ToggleFlyout(false);
								};
								container.Click += onClick;
								_flyoutTemplateItems.Add(item, new FlyoutTemplateView(itemTemplate, resolved, item, content, container, onClick));
								pendingView = null;
								retainedItems.Add(item);
								itemElement = container;
								nativeItems.Add(itemElement);
								RegisterNativeElement(capturedItem, itemElement, "ShellFlyout");
								continue;
							}
						}
						catch (Exception ex)
						{
							MauiContext.Services.GetService<ILogger<ShellContainerView>>()?
								.LogError(ex, "Creating Shell flyout item template failed.");
						}
						finally
						{
							if (pendingView != null)
							{
								FlyoutTemplateView.DisconnectView(pendingView);
								pendingView.BindingContext = null;
							}
						}
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
					nativeItems.Add(btn);
					RegisterNativeElement(capturedItem, btn, "ShellFlyout");
				}
			}

			foreach (var removed in _flyoutTemplateItems.Keys.Except(retainedItems).ToArray())
				ReleaseFlyoutItem(removed);
			foreach (var removed in _flyoutItems.Children.Cast<FrameworkElement>().Except(nativeItems).ToArray())
				_flyoutItems.Children.Remove(removed);
			for (var index = 0; index < nativeItems.Count; index++)
			{
				var native = nativeItems[index];
				if (index < _flyoutItems.Children.Count && ReferenceEquals(_flyoutItems.Children[index], native))
					continue;
				_flyoutItems.Children.Remove(native);
				_flyoutItems.Children.Insert(index, native);
			}

			if (hasFlyoutItems)
				SetFlyoutBehavior(shell.FlyoutBehavior);
			else
				SetFlyoutBehavior(FlyoutBehavior.Disabled);

			UpdateTabs(shell);

			// Apply theme after building items
			UpdateFlyoutTheme();
			UpdateToolbarTheme();
		}

		void UpdateFlyoutTemplate(Shell shell, bool header)
		{
			var host = header ? _flyoutHeaderHost : _flyoutFooterHost;
			var cached = header ? _flyoutHeader : _flyoutFooter;
			var template = header ? shell.FlyoutHeaderTemplate : shell.FlyoutFooterTemplate;
			var data = header ? shell.FlyoutHeader : shell.FlyoutFooter;
			var context = data ?? shell.BindingContext;
			if (MauiContext == null)
				return;

			View? pendingView = null;
			var refreshProjection = new List<Action>();
			var disconnectProjection = new List<Action>();
			try
			{
				var resolved = template is Microsoft.Maui.Controls.DataTemplateSelector selector
					? selector.SelectTemplate(context, shell) : template;
				if (cached != null && cached.Matches(template, resolved, context))
				{
					cached.RefreshProjection();
					return;
				}
				if (cached == null && template == null && header && data is View existingFallback &&
					existingFallback.Handler?.PlatformView is FrameworkElement existingNative &&
					ReferenceEquals(host.Content, existingNative))
					return;

				host.Content = null;
				cached?.Disconnect();
				if (header)
					_flyoutHeader = null;
				else
					_flyoutFooter = null;

				if (resolved?.CreateContent() is View view)
				{
					pendingView = view;
					view.BindingContext = context;
					var native = header ? BuildNativeHeader(view, refreshProjection)
						: BuildNativeFooter(view, refreshProjection, disconnectProjection);
					if (native == null)
					{
						native = (FrameworkElement)Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)view, MauiContext);
						if (header)
							native.Height = view.HeightRequest > 0 ? view.HeightRequest : 120;
						else
							native.MinHeight = 80;
					}
					host.Content = native;
					var rendered = new FlyoutTemplateView(template, resolved, context, view, native,
						refreshProjection: refreshProjection, disconnectProjection: disconnectProjection);
					if (header)
						_flyoutHeader = rendered;
					else
						_flyoutFooter = rendered;
					pendingView = null;
				}
				else if (template == null && header && data is View fallback)
					host.Content = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)fallback, MauiContext);
			}
			catch (Exception ex)
			{
				MauiContext.Services.GetService<ILogger<ShellContainerView>>()?
					.LogError(ex, "Creating Shell flyout {Surface} template failed.", header ? "header" : "footer");
			}
			finally
			{
				if (pendingView != null)
				{
					foreach (var disconnect in disconnectProjection)
						disconnect();
					FlyoutTemplateView.DisconnectView(pendingView);
					pendingView.BindingContext = null;
				}
			}
		}

		void ReleaseFlyoutItem(ShellItem item)
		{
			if (_flyoutTemplateItems.Remove(item, out var cached))
			{
				_flyoutItems.Children.Remove(cached.Native);
				cached.Disconnect();
			}
		}

		internal void ClearFlyoutTemplates()
		{
			if (_flyoutHeader != null)
				_flyoutHeaderHost.Content = null;
			if (_flyoutFooter != null)
				_flyoutFooterHost.Content = null;
			_flyoutHeader?.Disconnect();
			_flyoutFooter?.Disconnect();
			_flyoutHeader = null;
			_flyoutFooter = null;
			foreach (var item in _flyoutTemplateItems.Keys.ToArray())
				ReleaseFlyoutItem(item);
		}

		sealed class FlyoutTemplateView(
			Microsoft.Maui.Controls.DataTemplate? template,
			Microsoft.Maui.Controls.DataTemplate? resolvedTemplate,
			object? context, View view, FrameworkElement native, RoutedEventHandler? onClick = null,
			List<Action>? refreshProjection = null, List<Action>? disconnectProjection = null)
		{
			public FrameworkElement Native { get; } = native;

			public bool Matches(Microsoft.Maui.Controls.DataTemplate? candidate,
				Microsoft.Maui.Controls.DataTemplate? resolved, object? data)
				=> ReferenceEquals(template, candidate) && ReferenceEquals(resolvedTemplate, resolved) && ReferenceEquals(context, data);

			public void RefreshProjection()
			{
				if (refreshProjection != null)
					foreach (var refresh in refreshProjection)
						refresh();
			}

			public void Disconnect()
			{
				if (Native is WButton button && onClick != null)
					button.Click -= onClick;
				if (disconnectProjection != null)
					foreach (var disconnect in disconnectProjection)
						disconnect();
				DisconnectView(view);
				view.BindingContext = null;
			}

			internal static void DisconnectView(IVisualTreeElement element)
			{
				foreach (var child in element.GetVisualChildren())
					DisconnectView(child);
				if (element is IElement { Handler: { } handler } mauiElement)
				{
					handler.DisconnectHandler();
					mauiElement.Handler = null;
				}
			}
		}

		public void UpdateTabs(Shell shell)
		{
			var item = shell.CurrentItem;
			var sections = item != null && shell.Items.Contains(item)
				? ((IShellItemController)item).GetItems().ToArray()
				: Array.Empty<ShellSection>();
			bool hasTabs = sections.Length > 1;
			if (!hasTabs)
				sections = Array.Empty<ShellSection>();

			_updatingTabs = true;
			try
			{
				var tabs = _tabControl.Items.OfType<global::System.Windows.Controls.TabItem>().ToArray();
				if (tabs.Length != sections.Length ||
					tabs.Where((tab, index) => !ReferenceEquals(tab.Tag, sections[index])).Any())
				{
					RebuildTabs(sections);
				}
				else
				{
					foreach (var tab in tabs)
					{
						var section = (ShellSection)tab.Tag;
						tab.Header = section.Title ?? section.Route ?? "Tab";
						tab.IsEnabled = section.IsEnabled;
						if (!_registeredNativeElements.Contains(tab))
							RegisterNativeElement(section, tab, "ShellTab");
					}
				}
				_tabControl.Visibility = hasTabs ? WVisibility.Visible : WVisibility.Collapsed;
			}
			finally
			{
				_updatingTabs = false;
			}
			UpdateTabSelection(shell);
		}

		void RebuildTabs(IEnumerable<ShellSection> sections)
		{
			foreach (global::System.Windows.Controls.TabItem tab in _tabControl.Items)
			{
				if (_registeredNativeElements.Remove(tab))
					NativeElementDiagnosticsBridge.Unregister(tab);
			}
			_tabControl.Items.Clear();

			foreach (var section in sections)
			{
				var tab = new global::System.Windows.Controls.TabItem
				{
					Header = section.Title ?? section.Route ?? "Tab",
					Tag = section,
					IsEnabled = section.IsEnabled,
				};
				_tabControl.Items.Add(tab);
				RegisterNativeElement(section, tab, "ShellTab");
			}
		}

		internal void UpdateTabIsEnabled(Shell shell, ShellSection section)
		{
			var item = shell.CurrentItem;
			if (item == null || !shell.Items.Contains(item) ||
				!((IShellItemController)item).GetItems().Contains(section))
				return;

			var tab = _tabControl.Items.OfType<global::System.Windows.Controls.TabItem>()
				.FirstOrDefault(tab => ReferenceEquals(tab.Tag, section));
			if (tab == null)
				return;

			var updatingTabs = _updatingTabs;
			_updatingTabs = true;
			try
			{
				tab.IsEnabled = section.IsEnabled;
			}
			finally
			{
				_updatingTabs = updatingTabs;
			}
		}

		internal void UpdateTabSelection(Shell shell)
		{
			_updatingTabs = true;
			try
			{
				_tabControl.SelectedItem = _tabControl.Items
					.OfType<global::System.Windows.Controls.TabItem>()
					.FirstOrDefault(tab => ReferenceEquals(tab.Tag, shell.CurrentItem?.CurrentItem));
			}
			finally
			{
				_updatingTabs = false;
			}
		}

		void RegisterNativeElement(object owner, DependencyObject nativeElement, string role)
		{
			NativeElementDiagnosticsBridge.Register(owner, nativeElement, role);
			_registeredNativeElements.Add(nativeElement);
		}

		public void UpdateBackButtonRegistration(Microsoft.Maui.Controls.Page? page)
		{
			if (ReferenceEquals(_backButtonOwner, page))
				return;

			if (_registeredNativeElements.Remove(_backButton))
				NativeElementDiagnosticsBridge.Unregister(_backButton);
			_backButtonOwner = page;
			if (page is not null)
				RegisterNativeElement(page, _backButton, "BackButton");
		}

		public void UnregisterNativeElements()
		{
			foreach (var nativeElement in _registeredNativeElements)
				NativeElementDiagnosticsBridge.Unregister(nativeElement);
			_registeredNativeElements.Clear();
			_backButtonOwner = null;
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
		FrameworkElement? BuildNativeHeader(View headerView, List<Action> refreshProjection)
		{
			try
			{
				var container = new WGrid
				{
					HorizontalAlignment = WHorizontalAlignment.Stretch,
				};
				void RefreshHeight()
				{
					var height = headerView.HeightRequest;
					if (!(height > 0) && headerView is Microsoft.Maui.Controls.Grid grid)
						height = grid.RowDefinitions.Where(row => row.Height.IsAbsolute).Sum(row => row.Height.Value);
					container.Height = height > 0 ? height : 120;
				}
				RefreshHeight();
				refreshProjection.Add(RefreshHeight);

				// Walk children to find images
				WalkHeaderView(headerView, container, refreshProjection);

				if (container.Children.Count > 0)
					return container;
			}
			catch (Exception ex)
			{
				refreshProjection.Clear();
				MauiContext?.Services.GetService<ILogger<ShellContainerView>>()?
					.LogError(ex, "Creating Shell flyout header projection failed.");
			}
			return null;
		}

		void WalkHeaderView(Microsoft.Maui.Controls.Element element, WGrid container, List<Action> refreshProjection)
		{
			if (element is Microsoft.Maui.Controls.Image img)
			{
				var wpfImage = new global::System.Windows.Controls.Image
				{
					HorizontalAlignment = WHorizontalAlignment.Center,
					VerticalAlignment = WVerticalAlignment.Center,
					Stretch = System.Windows.Media.Stretch.Uniform,
				};
				string? previousPath = null;
				void RefreshImage()
				{
					wpfImage.Width = img.WidthRequest > 0 ? img.WidthRequest : double.NaN;
					wpfImage.Height = img.HeightRequest > 0 ? img.HeightRequest : double.NaN;
					var resolvedPath = img.Source is Microsoft.Maui.Controls.FileImageSource fis && !string.IsNullOrEmpty(fis.File)
						? ImageHandler.ResolveImagePath(fis.File) : null;
					if (string.Equals(previousPath, resolvedPath, StringComparison.Ordinal))
						return;
					wpfImage.Source = null;
					if (resolvedPath != null)
					{
						if (resolvedPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
							wpfImage.Source = ImageHandler.RenderSvgToBitmap(resolvedPath);
						else
						{
							var bitmap = new global::System.Windows.Media.Imaging.BitmapImage();
							bitmap.BeginInit();
							bitmap.CacheOption = global::System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
							bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
							bitmap.EndInit();
							wpfImage.Source = bitmap;
						}
					}
					previousPath = resolvedPath;
				}
				RefreshImage();
				refreshProjection.Add(RefreshImage);

				container.Children.Add(wpfImage);
				return;
			}

			// Recurse into child elements
			if (element is Microsoft.Maui.Controls.Layout layout)
			{
				foreach (var child in layout.Children)
				{
					if (child is Microsoft.Maui.Controls.Element childElement)
						WalkHeaderView(childElement, container, refreshProjection);
				}
			}
			else if (element is Microsoft.Maui.Controls.ContentView cv && cv.Content is Microsoft.Maui.Controls.Element cvContent)
			{
				WalkHeaderView(cvContent, container, refreshProjection);
			}
		}

		/// <summary>
		/// Builds a native WPF rendering of the footer template content.
		/// This avoids MAUI LayoutPanel positioning issues in constrained flyout areas.
		/// Looks for Picker controls with ItemsSource and creates a WPF ComboBox.
		/// </summary>
		FrameworkElement? BuildNativeFooter(View footerView, List<Action> refreshProjection, List<Action> disconnectProjection)
		{
			try
			{
				var stack = new global::System.Windows.Controls.StackPanel
				{
					Margin = new WThickness(15),
				};

				WalkFooterView(footerView, stack, refreshProjection, disconnectProjection);

				if (stack.Children.Count > 0)
					return stack;
			}
			catch (Exception ex)
			{
				foreach (var disconnect in disconnectProjection)
					disconnect();
				disconnectProjection.Clear();
				refreshProjection.Clear();
				MauiContext?.Services.GetService<ILogger<ShellContainerView>>()?
					.LogError(ex, "Creating Shell flyout footer projection failed.");
			}
			return null;
		}

		void WalkFooterView(Microsoft.Maui.Controls.Element element, global::System.Windows.Controls.StackPanel container,
			List<Action> refreshProjection, List<Action> disconnectProjection)
		{
			if (element is Microsoft.Maui.Controls.Label label)
			{
				var text = new global::System.Windows.Controls.TextBlock
				{
					Margin = new WThickness(0, 0, 0, 4),
				};
				void RefreshLabel()
				{
					text.Text = label.Text ?? string.Empty;
					text.FontSize = label.FontSize > 0 ? label.FontSize : 14;
					text.Foreground = IsDarkTheme() ? new WSolidColorBrush(WColor.FromRgb(200, 200, 200))
						: new WSolidColorBrush(WColor.FromRgb(60, 60, 60));
				}
				RefreshLabel();
				refreshProjection.Add(RefreshLabel);
				container.Children.Add(text);
			}
			else if (element is Microsoft.Maui.Controls.Picker picker)
			{
				var combo = new global::System.Windows.Controls.ComboBox
				{
					MinWidth = 100,
					Margin = new WThickness(0, 2, 0, 0),
				};

				bool updating = false;
				bool connected = true;
				void RefreshPicker()
				{
					var items = picker.ItemsSource != null
						? picker.ItemsSource.Cast<object?>().Select(item => item?.ToString() ?? string.Empty).ToArray()
						: picker.Items.ToArray();
					updating = true;
					try
					{
						if (!combo.Items.Cast<string>().SequenceEqual(items))
						{
							combo.Items.Clear();
							foreach (var item in items)
								combo.Items.Add(item);
						}
						if (combo.SelectedIndex != picker.SelectedIndex)
							combo.SelectedIndex = picker.SelectedIndex;
					}
					finally
					{
						updating = false;
					}
				}
				RefreshPicker();
				refreshProjection.Add(RefreshPicker);
				SelectionChangedEventHandler onSelectionChanged = (s, e) =>
				{
					if (!updating && combo.SelectedIndex >= 0)
						picker.SelectedIndex = combo.SelectedIndex;
				};
				System.ComponentModel.PropertyChangedEventHandler onPropertyChanged = (s, e) =>
				{
					if (e.PropertyName == nameof(Microsoft.Maui.Controls.Picker.SelectedIndex))
						combo.Dispatcher.InvokeAsync(() =>
						{
							if (connected)
								RefreshPicker();
						});
				};
				combo.SelectionChanged += onSelectionChanged;
				picker.PropertyChanged += onPropertyChanged;
				disconnectProjection.Add(() =>
				{
					connected = false;
					combo.SelectionChanged -= onSelectionChanged;
					picker.PropertyChanged -= onPropertyChanged;
				});

				container.Children.Add(combo);
			}

			// Recurse into child elements
			if (element is Microsoft.Maui.Controls.Layout layout)
			{
				foreach (var child in layout.Children)
				{
					if (child is Microsoft.Maui.Controls.Element childElement)
						WalkFooterView(childElement, container, refreshProjection, disconnectProjection);
				}
			}
			else if (element is Microsoft.Maui.Controls.ContentView cv && cv.Content is Microsoft.Maui.Controls.Element cvContent)
			{
				WalkFooterView(cvContent, container, refreshProjection, disconnectProjection);
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
				[nameof(Shell.FlyoutIsPresented)] = MapFlyoutIsPresented,
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

		public override void SetVirtualView(IView view)
		{
			if (!ReferenceEquals(((IElementHandler)this).VirtualView, view) &&
				((IElementHandler)this).PlatformView is ShellContainerView container)
				container.ClearFlyoutTemplates();
			if (!ReferenceEquals(_observedShell, view))
				DisconnectShellItems();
			if (!ReferenceEquals(_selectionShell, view))
				DisconnectShellSelection();

			base.SetVirtualView(view);
			ConnectShellSelection();
			ConnectShellItems();
		}

		protected override ShellContainerView CreatePlatformView()
		{
			var container = new ShellContainerView();
			container.OnShellItemSelected = OnShellItemSelected;
			container.OnBackButtonClicked = OnBackButtonClicked;
			container.OnFlyoutOpenChanged = open =>
			{
				if (VirtualView != null && VirtualView.FlyoutIsPresented != open)
					VirtualView.FlyoutIsPresented = open;
			};
			container.MauiContext = MauiContext;
			return container;
		}

		protected override void ConnectHandler(ShellContainerView platformView)
		{
			base.ConnectHandler(platformView);
			platformView.OnShellSectionSelected = OnShellSectionSelected;
			ConnectShellSelection();
			if (VirtualView != null)
			{
				ConnectShellItems();
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
			platformView.OnShellSectionSelected = null;
			DisconnectShellSelection();
			platformView.UnregisterNativeElements();
			platformView.ClearFlyoutTemplates();
			DisconnectShellItems();
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
			QueueSelectionUpdate();
		}

		void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "CurrentPage" || e.PropertyName == "CurrentItem")
			{
				QueueSelectionUpdate();
			}
			else if (e.PropertyName == nameof(Shell.FlyoutBehavior))
			{
				PlatformView?.Dispatcher.InvokeAsync(() =>
				{
					if (ReferenceEquals(sender, _selectionShell))
						PlatformView.SetFlyoutBehavior(VirtualView.FlyoutBehavior);
				});
			}
			else if (e.PropertyName == nameof(Shell.FlyoutIsPresented))
			{
				PlatformView?.Dispatcher.InvokeAsync(() =>
				{
					if (ReferenceEquals(sender, _selectionShell))
						PlatformView.ToggleFlyout(VirtualView.FlyoutIsPresented);
				});
			}
			else if (e.PropertyName == nameof(Shell.FlyoutWidth))
			{
				if (VirtualView != null && VirtualView.FlyoutWidth > 0)
					PlatformView?.Dispatcher.InvokeAsync(() =>
					{
						if (ReferenceEquals(sender, _selectionShell))
							PlatformView.SetFlyoutWidth(VirtualView.FlyoutWidth);
					});
			}
			else if (e.PropertyName == nameof(Shell.FlyoutBackgroundColor))
			{
				if (VirtualView != null)
					MapFlyoutBackground(this, VirtualView);
			}
		}

		void ShowCurrentPage()
		{
			if (((IElementHandler)this).VirtualView == null || ((IElementHandler)this).PlatformView == null || MauiContext == null) return;

			try
			{
				var item = VirtualView.CurrentItem;
				var section = item?.CurrentItem;
				if (item == null || !VirtualView.Items.Contains(item) ||
					section == null || !item.Items.Contains(section))
				{
					PlatformView.UpdateBackButtonRegistration(null);
					PlatformView.ShowPage(null, null, false);
					return;
				}

				Microsoft.Maui.Controls.Page? currentPage = VirtualView.CurrentPage;

				if (currentPage == null)
				{
					// Check navigation stack first (for pushed pages via GoToAsync)
					if (section?.Stack?.Count > 1)
						currentPage = section.Stack[section.Stack.Count - 1];

					// Let Shell cache and parent the selected page so navigation and
					// visual-tree inspection see the same instance that WPF displays.
					if (currentPage == null)
					{
						var content = section?.CurrentItem;
						if (content != null)
							currentPage = ((IShellContentController)content).GetOrCreateContent();
					}
				}

				if (currentPage == null)
				{
					PlatformView.UpdateBackButtonRegistration(null);
					PlatformView.ShowPage(null, null, false);
					return;
				}

				var platformView = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)currentPage, MauiContext);
				var title = currentPage.Title ?? VirtualView.CurrentItem?.Title ?? string.Empty;
				bool hasNavStack = section?.Stack?.Count > 1;

				PlatformView.UpdateBackButtonRegistration(currentPage);
				PlatformView.ShowPage(platformView as FrameworkElement, title, hasNavStack);
			}
			catch (Exception ex)
			{
				MauiContext.Services.GetService<ILogger<ShellHandler>>()?
					.LogError(ex, "Showing current Shell page failed.");
			}
		}

		void OnShellSectionSelected(ShellSection section)
		{
			var shell = _selectionShell;
			var item = shell?.CurrentItem;
			if (shell == null || item == null)
				return;

			try
			{
				var controller = (IShellItemController)item;
				if (shell.Items.Contains(item) && controller.GetItems().Contains(section) &&
					section.IsVisible && section.IsEnabled && !ReferenceEquals(item.CurrentItem, section))
					controller.ProposeSection(section, true);
			}
			catch (Exception ex)
			{
				MauiContext?.Services.GetService<ILogger<ShellHandler>>()?
					.LogError(ex, "Selecting Shell tab failed.");
			}
			finally
			{
				// Cancellation and deferrals leave the model on the previous tab.
				// Accepted deferred navigation is synchronized by the selection observer.
				if (ReferenceEquals(_selectionShell, shell))
					PlatformView.UpdateTabSelection(shell);
			}
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

		static void MapFlyoutIsPresented(ShellHandler handler, Shell shell)
			=> handler.PlatformView.ToggleFlyout(shell.FlyoutIsPresented);

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
			handler.UpdateValue(nameof(Shell.BackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackground));
			handler.ShowCurrentPage();
		}

		static void MapCurrentItem(ShellHandler handler, Shell shell)
		{
			handler.PlatformView.UpdateTabs(shell);
			handler.ShowCurrentPage();
		}
	}
}
