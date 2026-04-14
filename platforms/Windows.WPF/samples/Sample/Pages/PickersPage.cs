using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class PickersPage : ContentPage
{
	public PickersPage()
	{
		Title = "Pickers";

		var dateLabel = new Label { Text = "Selected date: (none)", FontSize = 14 };
		var datePicker = new DatePicker { Date = DateTime.Today };
		datePicker.DateSelected += (s, e) => dateLabel.Text = $"Selected date: {e.NewDate:D}";

		var timeLabel = new Label { Text = $"Selected time: {DateTime.Now:hh\\:mm tt}", FontSize = 14 };
		var timePicker = new TimePicker { Time = DateTime.Now.TimeOfDay };

		var pickerLabel = new Label { Text = "Selected: (none)", FontSize = 14 };
		var picker = new Picker { Title = "Pick a color" };
		picker.Items.Add("Red");
		picker.Items.Add("Green");
		picker.Items.Add("Blue");
		picker.Items.Add("Orange");
		picker.Items.Add("Purple");
		picker.SelectedIndexChanged += (s, e) =>
		{
			if (picker.SelectedIndex >= 0)
				pickerLabel.Text = $"Selected: {picker.Items[picker.SelectedIndex]}";
		};

		var searchResults = new Label { Text = "Search results will appear here...", FontSize = 14, TextColor = Colors.Gray };
		var searchBar = new SearchBar { Placeholder = "Search fruits..." };
		var fruits = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango" };
		searchBar.TextChanged += (s, e) =>
		{
			if (string.IsNullOrWhiteSpace(e.NewTextValue))
			{
				searchResults.Text = "Search results will appear here...";
				return;
			}
			var matches = fruits.Where(f => f.Contains(e.NewTextValue, StringComparison.OrdinalIgnoreCase));
			searchResults.Text = matches.Any()
				? $"Found: {string.Join(", ", matches)}"
				: "No matches found.";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "DatePicker", FontSize = 16, FontAttributes = FontAttributes.Bold },
					datePicker,
					dateLabel,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label { Text = "TimePicker", FontSize = 16, FontAttributes = FontAttributes.Bold },
					timePicker,
					timeLabel,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label { Text = "Picker (Dropdown)", FontSize = 16, FontAttributes = FontAttributes.Bold },
					picker,
					pickerLabel,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label { Text = "SearchBar", FontSize = 16, FontAttributes = FontAttributes.Bold },
					searchBar,
					searchResults,
				}
			}
		};
	}
}
