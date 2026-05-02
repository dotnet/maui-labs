namespace MauiControlsGallery.Pages;

public partial class CollectionViewPage : ContentPage
{
	static readonly Color[] AccentColors =
	{
		Colors.CornflowerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.MediumOrchid,
		Colors.SandyBrown, Colors.Teal, Colors.IndianRed, Colors.DodgerBlue,
		Colors.SlateBlue, Colors.OliveDrab, Colors.Crimson, Colors.DarkCyan,
	};

	public CollectionViewPage()
	{
		InitializeComponent();

		var verticalItems = Enumerable.Range(1, 30)
			.Select(i => new SimpleItem($"Item {i}", $"Description for item {i}", AccentColors[(i - 1) % AccentColors.Length]))
			.ToList();

		var horizontalItems = Enumerable.Range(1, 20)
			.Select(i => new SimpleItem($"Item {i}", $"Description for item {i}", AccentColors[(i - 1) % AccentColors.Length]))
			.ToList();

		var gridItems = Enumerable.Range(1, 24)
			.Select(i => new SimpleItem($"Item {i}", $"Description for item {i}", AccentColors[(i - 1) % AccentColors.Length]))
			.ToList();

		VerticalList.ItemsSource = verticalItems;
		HorizontalList.ItemsSource = horizontalItems;
		GridList.ItemsSource = gridItems;
	}

	void OnVerticalSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is SimpleItem item)
			SelectionLabel.Text = $"Selected: {item.Name}";
	}
}

public class SimpleItem
{
	public string Name { get; set; }
	public string Description { get; set; }
	public Color AccentColor { get; set; }

	public SimpleItem(string name, string description, Color accentColor)
	{
		Name = name;
		Description = description;
		AccentColor = accentColor;
	}
}
