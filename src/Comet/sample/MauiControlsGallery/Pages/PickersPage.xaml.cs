namespace MauiControlsGallery.Pages;

public partial class PickersPage : ContentPage
{
	static readonly string[] Fruits =
	{
		"Apple", "Banana", "Cherry", "Date", "Elderberry",
		"Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango"
	};

	public PickersPage()
	{
		InitializeComponent();
	}

	void OnDateSelected(object? sender, DateChangedEventArgs e)
	{
		DateLabel.Text = $"Selected date: {e.NewDate:D}";
	}

	void OnPickerSelected(object? sender, EventArgs e)
	{
		if (ColorPicker.SelectedIndex >= 0)
			PickerLabel.Text = $"Selected: {ColorPicker.ItemsSource[ColorPicker.SelectedIndex]}";
	}

	void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(e.NewTextValue))
		{
			SearchResultLabel.Text = "Search results will appear here...";
			SearchResultLabel.TextColor = Colors.Gray;
			return;
		}

		var matches = Fruits.Where(f =>
			f.Contains(e.NewTextValue, StringComparison.OrdinalIgnoreCase)).ToArray();

		SearchResultLabel.Text = matches.Length > 0
			? $"Found: {string.Join(", ", matches)}"
			: "No matches found.";
		SearchResultLabel.TextColor = null!;
	}
}
