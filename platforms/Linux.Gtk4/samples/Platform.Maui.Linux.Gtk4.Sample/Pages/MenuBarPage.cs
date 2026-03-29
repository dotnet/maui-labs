using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates MenuBarItem and ToolbarItem support on the Linux GTK4 backend.
/// Menu bar appears at the top of the window; toolbar items in the header bar.
/// </summary>
public class MenuBarPage : ContentPage
{
	Label _statusLabel;

	public MenuBarPage()
	{
		Title = "Menu & Toolbar";

		_statusLabel = new Label
		{
			Text = "Use the menu bar or toolbar buttons above.",
			FontSize = 16,
			TextColor = Colors.Gray,
			HorizontalTextAlignment = TextAlignment.Center,
			Padding = new Thickness(0, 20),
		};

		// --- Menu Bar ---
		var fileMenu = new MenuBarItem { Text = "File" };
		fileMenu.Add(new MenuFlyoutItem
		{
			Text = "New",
			Command = new Command(() => SetStatus("File → New clicked")),
		});
		fileMenu.Add(new MenuFlyoutItem
		{
			Text = "Open",
			Command = new Command(() => SetStatus("File → Open clicked")),
		});
		fileMenu.Add(new MenuFlyoutItem
		{
			Text = "Save",
			Command = new Command(() => SetStatus("File → Save clicked")),
		});
		fileMenu.Add(new MenuFlyoutSeparator());
		fileMenu.Add(new MenuFlyoutItem
		{
			Text = "Exit",
			Command = new Command(() => SetStatus("File → Exit clicked")),
		});

		var editMenu = new MenuBarItem { Text = "Edit" };
		editMenu.Add(new MenuFlyoutItem
		{
			Text = "Cut",
			Command = new Command(() => SetStatus("Edit → Cut clicked")),
		});
		editMenu.Add(new MenuFlyoutItem
		{
			Text = "Copy",
			Command = new Command(() => SetStatus("Edit → Copy clicked")),
		});
		editMenu.Add(new MenuFlyoutItem
		{
			Text = "Paste",
			Command = new Command(() => SetStatus("Edit → Paste clicked")),
		});

		var helpMenu = new MenuBarItem { Text = "Help" };
		helpMenu.Add(new MenuFlyoutItem
		{
			Text = "About",
			Command = new Command(() => SetStatus("Help → About clicked")),
		});

		MenuBarItems.Add(fileMenu);
		MenuBarItems.Add(editMenu);
		MenuBarItems.Add(helpMenu);

		// --- Toolbar Items ---
		ToolbarItems.Add(new ToolbarItem
		{
			Text = "Refresh",
			Order = ToolbarItemOrder.Primary,
			Command = new Command(() => SetStatus("Toolbar: Refresh clicked")),
		});
		ToolbarItems.Add(new ToolbarItem
		{
			Text = "Settings",
			Order = ToolbarItemOrder.Secondary,
			Command = new Command(() => SetStatus("Toolbar: Settings clicked")),
		});

		Content = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(24),
			Children =
			{
				new Label
				{
					Text = "Menu Bar & Toolbar",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
				},
				new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },
				new Label
				{
					Text = "This page defines MenuBarItems (File, Edit, Help) and ToolbarItems " +
					       "(Refresh, Settings). On GTK4, the menu bar appears as a PopoverMenuBar " +
					       "below the title bar, and toolbar items appear as buttons in the navigation bar.",
					FontSize = 14,
					TextColor = Colors.DimGray,
				},
				new BoxView { HeightRequest = 1, Color = Colors.LightGray },
				_statusLabel,
				new Label
				{
					Text = "Last action log:",
					FontSize = 14,
					FontAttributes = FontAttributes.Bold,
					Padding = new Thickness(0, 12, 0, 0),
				},
				BuildInfoSection(),
			}
		};
	}

	void SetStatus(string message)
	{
		_statusLabel.Text = $"✅ {message}";
		_statusLabel.TextColor = Colors.DodgerBlue;
	}

	static View BuildInfoSection()
	{
		return new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				InfoRow("Menu bar", "File (New, Open, Save, Exit), Edit (Cut, Copy, Paste), Help (About)"),
				InfoRow("Toolbar (Primary)", "Refresh — appears in the header bar"),
				InfoRow("Toolbar (Secondary)", "Settings — appears in the header bar overflow"),
			}
		};
	}

	static View InfoRow(string label, string value)
	{
		return new HorizontalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = label, FontAttributes = FontAttributes.Bold, FontSize = 13, WidthRequest = 140 },
				new Label { Text = value, FontSize = 13, TextColor = Colors.DimGray },
			}
		};
	}
}
