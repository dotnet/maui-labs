using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class TableViewPage : ContentPage
{
	public TableViewPage()
	{
		Title = "TableView";

		var tableView = new TableView
		{
			HasUnevenRows = true,
			Intent = TableIntent.Settings,
			Root = new TableRoot("Settings")
			{
				new TableSection("Account")
				{
					new TextCell { Text = "Username", Detail = "john.appleseed" },
					new TextCell { Text = "Email", Detail = "john@example.com" },
					new ImageCell { Text = "Profile Photo", Detail = "Tap to change" },
				},
				new TableSection("Preferences")
				{
					new SwitchCell { Text = "Dark Mode", On = false },
					new SwitchCell { Text = "Notifications", On = true },
					new SwitchCell { Text = "Auto-update", On = true },
				},
				new TableSection("Input")
				{
					new EntryCell { Label = "Name", Text = "John", Placeholder = "Enter your name" },
					new EntryCell { Label = "Bio", Placeholder = "Write something..." },
				},
				new TableSection("About")
				{
					new TextCell { Text = "Version", Detail = "1.0.0" },
					new TextCell { Text = "Build", Detail = "2024.02.20" },
					new TextCell { Text = "License", Detail = "MIT" },
				},
			}
		};

		Content = new Grid
		{
			Padding = new Thickness(24),
			RowSpacing = 10,
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			},
			Children =
			{
			}
		};

		var subtitle = new Label { Text = "Settings-style grouped table with various cell types:", FontSize = 14, TextColor = Colors.Gray };
		Grid.SetRow(subtitle, 0);
		((Grid)Content).Children.Add(subtitle);

		var border = new Border
		{
			Stroke = Colors.Gray,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Content = tableView,
		};
		Grid.SetRow(border, 1);
		((Grid)Content).Children.Add(border);
	}
}
