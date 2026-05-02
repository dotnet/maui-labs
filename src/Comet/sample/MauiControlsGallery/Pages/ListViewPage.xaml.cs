#pragma warning disable CS0618 // ListView is intentionally used for visual comparison
namespace MauiControlsGallery.Pages;

public partial class ListViewPage : ContentPage
{
	public ListViewPage()
	{
		InitializeComponent();

		FoodListView.ItemsSource = new[]
		{
			new { Name = "Apple", Category = "Fruit", Emoji = "Apple" },
			new { Name = "Banana", Category = "Fruit", Emoji = "Banana" },
			new { Name = "Carrot", Category = "Vegetable", Emoji = "Carrot" },
			new { Name = "Broccoli", Category = "Vegetable", Emoji = "Broccoli" },
			new { Name = "Salmon", Category = "Protein", Emoji = "Fish" },
			new { Name = "Chicken", Category = "Protein", Emoji = "Chicken" },
			new { Name = "Rice", Category = "Grain", Emoji = "Rice" },
			new { Name = "Bread", Category = "Grain", Emoji = "Bread" },
		};

		SettingsListView.ItemsSource = new[]
		{
			new { Title = "Settings", Subtitle = "Configure your preferences" },
			new { Title = "Account", Subtitle = "Manage your account details" },
			new { Title = "Privacy", Subtitle = "Review privacy settings" },
			new { Title = "Notifications", Subtitle = "Manage notification preferences" },
		};
	}
}
