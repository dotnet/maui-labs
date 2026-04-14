using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class ListViewPage : ContentPage
{
	public ListViewPage()
	{
		Title = "ListView";

		var listView = new ListView
		{
			HasUnevenRows = true,
			ItemsSource = new[]
			{
				new { Name = "Apple", Category = "Fruit", Emoji = "🍎" },
				new { Name = "Banana", Category = "Fruit", Emoji = "🍌" },
				new { Name = "Carrot", Category = "Vegetable", Emoji = "🥕" },
				new { Name = "Broccoli", Category = "Vegetable", Emoji = "🥦" },
				new { Name = "Salmon", Category = "Protein", Emoji = "🐟" },
				new { Name = "Chicken", Category = "Protein", Emoji = "🍗" },
				new { Name = "Rice", Category = "Grain", Emoji = "🍚" },
				new { Name = "Bread", Category = "Grain", Emoji = "🍞" },
			},
			ItemTemplate = new DataTemplate(() =>
			{
				var cell = new ViewCell();
				var hStack = new HorizontalStackLayout { Padding = new Thickness(12, 8), Spacing = 10 };

				var emoji = new Label { FontSize = 20, VerticalOptions = LayoutOptions.Center };
				emoji.SetBinding(Label.TextProperty, "Emoji");
				hStack.Children.Add(emoji);

				var vStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
				var name = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold };
				name.SetBinding(Label.TextProperty, "Name");
				vStack.Children.Add(name);
				var category = new Label { FontSize = 11, TextColor = Colors.Gray };
				category.SetBinding(Label.TextProperty, "Category");
				vStack.Children.Add(category);
				hStack.Children.Add(vStack);

				cell.View = hStack;
				return cell;
			}),
			Header = "Food Items",
			Footer = "8 items total",
		};

		var selectedLabel = new Label
		{
			Text = "Selected: (none)",
			FontSize = 14,
			Padding = new Thickness(12, 8),
			TextColor = Colors.DodgerBlue,
		};

		listView.ItemSelected += (s, e) =>
		{
			if (e.SelectedItem != null)
				selectedLabel.Text = $"Selected: {e.SelectedItem}";
		};

		// TextCell ListView
		var textCellListView = new ListView
		{
			ItemsSource = new[]
			{
				new { Title = "Settings", Subtitle = "Configure your preferences" },
				new { Title = "Account", Subtitle = "Manage your account details" },
				new { Title = "Privacy", Subtitle = "Review privacy settings" },
				new { Title = "Notifications", Subtitle = "Manage notification preferences" },
			},
			ItemTemplate = new DataTemplate(() =>
			{
				var cell = new TextCell();
				cell.SetBinding(TextCell.TextProperty, "Title");
				cell.SetBinding(TextCell.DetailProperty, "Subtitle");
				cell.DetailColor = Colors.Gray;
				return cell;
			}),
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "ViewCell with DataTemplate", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.CornflowerBlue },
					new Border
					{
						Stroke = Colors.Gray,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Content = listView,
						HeightRequest = 350,
					},
					selectedLabel,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label { Text = "TextCell ListView", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.CornflowerBlue },
					new Border
					{
						Stroke = Colors.Gray,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Content = textCellListView,
						HeightRequest = 200,
					},
				}
			}
		};
	}
}
