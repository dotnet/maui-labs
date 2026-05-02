using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Maui.Handlers;
using WMenu = System.Windows.Controls.Menu;
using WMenuItem = System.Windows.Controls.MenuItem;
using WSeparator = System.Windows.Controls.Separator;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class MenuBarHandler
	{
		public static void SetupMenuBar(System.Windows.Window window, Microsoft.Maui.Controls.Page? page)
		{
			if (window == null) return;

			// Find or create the menu bar
			var dockPanel = window.Content as DockPanel;
			if (dockPanel == null)
			{
				// Wrap existing content in DockPanel
				var existing = window.Content as UIElement;
				dockPanel = new DockPanel { LastChildFill = true };
				if (existing != null)
					dockPanel.Children.Add(existing);
				window.Content = dockPanel;
			}

			// Remove existing menu
			WMenu? existingMenu = null;
			foreach (UIElement child in dockPanel.Children)
			{
				if (child is WMenu m)
				{
					existingMenu = m;
					break;
				}
			}
			if (existingMenu != null)
				dockPanel.Children.Remove(existingMenu);

			if (page?.MenuBarItems == null || page.MenuBarItems.Count == 0)
				return;

			var menu = new WMenu();
			DockPanel.SetDock(menu, Dock.Top);

			foreach (var mbi in page.MenuBarItems)
			{
				var topItem = new WMenuItem { Header = mbi.Text };
				BuildMenuItems(topItem, mbi);
				menu.Items.Add(topItem);
			}

			dockPanel.Children.Insert(0, menu);
		}

		static void BuildMenuItems(WMenuItem parent, Microsoft.Maui.Controls.MenuBarItem barItem)
		{
			foreach (var element in barItem)
			{
				if (element is Microsoft.Maui.Controls.MenuFlyoutSeparator)
				{
					parent.Items.Add(new WSeparator());
				}
				else if (element is Microsoft.Maui.Controls.MenuFlyoutSubItem subItem)
				{
					var wpfSub = new WMenuItem { Header = subItem.Text };
					BuildSubMenuItems(wpfSub, subItem);
					parent.Items.Add(wpfSub);
				}
				else if (element is Microsoft.Maui.Controls.MenuFlyoutItem flyoutItem)
				{
					var wpfItem = CreateMenuItem(flyoutItem);
					parent.Items.Add(wpfItem);
				}
			}
		}

		static void BuildSubMenuItems(WMenuItem parent, Microsoft.Maui.Controls.MenuFlyoutSubItem subItem)
		{
			foreach (var element in subItem)
			{
				if (element is Microsoft.Maui.Controls.MenuFlyoutSeparator)
				{
					parent.Items.Add(new WSeparator());
				}
				else if (element is Microsoft.Maui.Controls.MenuFlyoutSubItem nested)
				{
					var wpfNested = new WMenuItem { Header = nested.Text };
					BuildSubMenuItems(wpfNested, nested);
					parent.Items.Add(wpfNested);
				}
				else if (element is Microsoft.Maui.Controls.MenuFlyoutItem flyoutItem)
				{
					var wpfItem = CreateMenuItem(flyoutItem);
					parent.Items.Add(wpfItem);
				}
			}
		}

		static WMenuItem CreateMenuItem(Microsoft.Maui.Controls.MenuFlyoutItem flyoutItem)
		{
			var wpfItem = new WMenuItem
			{
				Header = flyoutItem.Text,
				IsEnabled = flyoutItem.IsEnabled,
			};

			// Keyboard accelerators
			foreach (var accel in flyoutItem.KeyboardAccelerators)
			{
				try
				{
					var key = Enum.TryParse<Key>(accel.Key, true, out var k) ? k : Key.None;
					var modifiers = ModifierKeys.None;
					if (accel.Modifiers.HasFlag(Microsoft.Maui.KeyboardAcceleratorModifiers.Ctrl))
						modifiers |= ModifierKeys.Control;
					if (accel.Modifiers.HasFlag(Microsoft.Maui.KeyboardAcceleratorModifiers.Shift))
						modifiers |= ModifierKeys.Shift;
					if (accel.Modifiers.HasFlag(Microsoft.Maui.KeyboardAcceleratorModifiers.Alt))
						modifiers |= ModifierKeys.Alt;

					if (key != Key.None)
					{
						wpfItem.InputGestureText = GetGestureText(modifiers, key);
					}
				}
				catch { }
			}

			wpfItem.Click += (s, e) =>
			{
				try
				{
					var cmd = flyoutItem.Command;
					if (cmd?.CanExecute(flyoutItem.CommandParameter) == true)
						cmd.Execute(flyoutItem.CommandParameter);

					// Also invoke Clicked event via reflection
					var method = typeof(Microsoft.Maui.Controls.MenuFlyoutItem).GetMethod("OnClicked",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					method?.Invoke(flyoutItem, null);
				}
				catch { }
			};

			return wpfItem;
		}

		static string GetGestureText(ModifierKeys modifiers, Key key)
		{
			var parts = new List<string>();
			if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
			if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
			if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
			parts.Add(key.ToString());
			return string.Join("+", parts);
		}
	}
}
