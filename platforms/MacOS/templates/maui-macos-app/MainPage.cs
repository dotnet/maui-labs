namespace MauiMacOSApp;

public class MainPage : ContentPage
{
	public MainPage()
	{
		Content = new VerticalStackLayout
		{
			Spacing = 25,
			Padding = new Thickness(30, 0),
			VerticalOptions = LayoutOptions.Center,
			Children =
			{
				new Label
				{
					Text = "Hello, macOS!",
					HorizontalOptions = LayoutOptions.Center,
					FontSize = 32
				},
				new Label
				{
					Text = "Welcome to .NET MAUI on macOS",
					HorizontalOptions = LayoutOptions.Center,
					FontSize = 18
				}
			}
		};
	}
}
