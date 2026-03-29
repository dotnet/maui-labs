using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates MAUI Shell navigation with flyout sidebar,
/// tab-based sections, and URI-based routing on Linux GTK4.
/// </summary>
public class ShellDemoPage : ContentPage
{
	public ShellDemoPage()
	{
		Title = "Shell Demo";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			Spacing = 16,
			Children =
			{
				new Label
				{
					Text = "🐚 Shell Navigation Demo",
					FontSize = 22,
					FontAttributes = FontAttributes.Bold,
				},
				new Label
				{
					Text = "This page lets you launch a Shell-based navigation experience. " +
					       "Shell provides flyout menu, tab bars, and URI-based routing.",
					TextColor = Colors.DimGray,
				},
				new Button
				{
					Text = "Launch Shell App",
					BackgroundColor = Colors.DodgerBlue,
					TextColor = Colors.White,
					CornerRadius = 8,
					Command = new Command(() =>
					{
						// Replace the main page with a Shell instance
						if (Application.Current != null)
							Application.Current.MainPage = new DemoShell();
					}),
				},
			}
		};
	}
}

/// <summary>
/// A Shell with multiple flyout items, tab sections, and sample content pages.
/// </summary>
public class DemoShell : Shell
{
	public DemoShell()
	{
		Title = "My Shell App";
		FlyoutBehavior = FlyoutBehavior.Flyout;
		FlyoutIsPresented = true;
		FlyoutHeader = "Shell Navigation";
		FlyoutFooter = "GTK4 Backend v1.0";
		FlyoutBackgroundColor = Colors.WhiteSmoke;

		// Register routes for GoToAsync navigation with query parameters
		Routing.RegisterRoute("details", typeof(ShellDetailsPage));

		// Home item (single section, single content)
		var homeItem = new ShellItem { Title = "🏠 Home", Route = "home" };
		var homeSection = new ShellSection { Title = "Home" };
		homeSection.Items.Add(new ShellContent
		{
			Title = "Home",
			ContentTemplate = new DataTemplate(() => new ShellHomePage()),
		});
		homeItem.Items.Add(homeSection);
		Items.Add(homeItem);

		// Explore item (multiple tab sections)
		var exploreItem = new ShellItem { Title = "🔍 Explore", Route = "explore" };

		var browseSection = new ShellSection { Title = "Browse" };
		browseSection.Items.Add(new ShellContent
		{
			Title = "Browse",
			ContentTemplate = new DataTemplate(() => new ShellBrowsePage()),
		});

		var searchSection = new ShellSection { Title = "Search" };
		searchSection.Items.Add(new ShellContent
		{
			Title = "Search",
			ContentTemplate = new DataTemplate(() => new ShellSearchPage()),
		});

		exploreItem.Items.Add(browseSection);
		exploreItem.Items.Add(searchSection);
		Items.Add(exploreItem);

		// Settings item
		var settingsItem = new ShellItem { Title = "⚙️ Settings", Route = "settings" };
		var settingsSection = new ShellSection { Title = "Settings" };
		settingsSection.Items.Add(new ShellContent
		{
			Title = "Settings",
			ContentTemplate = new DataTemplate(() => new ShellSettingsPage()),
		});
		settingsItem.Items.Add(settingsSection);
		Items.Add(settingsItem);

		// About item
		var aboutItem = new ShellItem { Title = "ℹ️ About", Route = "about" };
		var aboutSection = new ShellSection { Title = "About" };
		aboutSection.Items.Add(new ShellContent
		{
			Title = "About",
			ContentTemplate = new DataTemplate(() => new ShellAboutPage()),
		});
		aboutItem.Items.Add(aboutSection);
		Items.Add(aboutItem);
	}
}

// === Shell content pages ===

class ShellHomePage : ContentPage
{
	public ShellHomePage()
	{
		Title = "Home";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 12,
			Children =
			{
				new Label
				{
					Text = "🏠 Welcome Home",
					FontSize = 28,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "Use the flyout menu on the left to navigate between sections. " +
					       "The 'Explore' section has multiple tabs.",
					HorizontalTextAlignment = TextAlignment.Center,
					TextColor = Colors.DimGray,
					MaximumWidthRequest = 400,
				},
				new Button
				{
					Text = "← Back to Main App",
					Command = new Command(() =>
					{
						if (Application.Current is Sample.App)
							Application.Current.MainPage = new Sample.MainShell();
					}),
				},
				new Label { Text = "GoToAsync Navigation:", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
				new Button
				{
					Text = "GoToAsync → Settings",
					BackgroundColor = Colors.Teal, TextColor = Colors.White,
					Command = new Command(async () => await Shell.Current.GoToAsync("//settings")),
				},
				new Button
				{
					Text = "GoToAsync → About",
					BackgroundColor = Colors.SlateBlue, TextColor = Colors.White,
					Command = new Command(async () => await Shell.Current.GoToAsync("//about")),
				},
				new Label { Text = "GoToAsync with Query Params:", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
				new Button
				{
					Text = "GoToAsync → Details?id=42&name=Widget",
					BackgroundColor = Colors.Coral, TextColor = Colors.White,
					Command = new Command(async () => await Shell.Current.GoToAsync("details?id=42&name=Widget")),
				},
			}
		};
	}
}

class ShellBrowsePage : ContentPage
{
	public ShellBrowsePage()
	{
		Title = "Browse";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			Children =
			{
				new Label { Text = "📂 Browse", FontSize = 22, FontAttributes = FontAttributes.Bold },
				new Label { Text = "This is the Browse tab under the Explore section.", TextColor = Colors.DimGray },
				new ListView
				{
					ItemsSource = new[] { "Documents", "Pictures", "Music", "Videos", "Downloads" },
					HeightRequest = 200,
				},
			}
		};
	}
}

class ShellSearchPage : ContentPage
{
	public ShellSearchPage()
	{
		Title = "Search";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			Children =
			{
				new Label { Text = "🔎 Search", FontSize = 22, FontAttributes = FontAttributes.Bold },
				new SearchBar { Placeholder = "Search for items..." },
				new Label
				{
					Text = "This is the Search tab. Switch between Browse and Search using the tab bar above.",
					TextColor = Colors.DimGray,
					Margin = new Thickness(0, 12, 0, 0),
				},
			}
		};
	}
}

class ShellSettingsPage : ContentPage
{
	public ShellSettingsPage()
	{
		Title = "Settings";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			Spacing = 16,
			Children =
			{
				new Label { Text = "⚙️ Settings", FontSize = 22, FontAttributes = FontAttributes.Bold },
				new HorizontalStackLayout
				{
					Spacing = 12,
					Children =
					{
						new Label { Text = "Dark Mode", VerticalOptions = LayoutOptions.Center },
						new Switch(),
					}
				},
				new HorizontalStackLayout
				{
					Spacing = 12,
					Children =
					{
						new Label { Text = "Notifications", VerticalOptions = LayoutOptions.Center },
						new Switch { IsToggled = true },
					}
				},
				new HorizontalStackLayout
				{
					Spacing = 12,
					Children =
					{
						new Label { Text = "Font Size", VerticalOptions = LayoutOptions.Center },
						new Slider { Minimum = 10, Maximum = 30, Value = 14, WidthRequest = 200 },
					}
				},
			}
		};
	}
}

class ShellAboutPage : ContentPage
{
	public ShellAboutPage()
	{
		Title = "About";
		Content = new StackLayout
		{
			Padding = new Thickness(24),
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 8,
			Children =
			{
				new Label
				{
					Text = "ℹ️ About",
					FontSize = 22,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "Platform.Maui.Linux.Gtk4",
					FontSize = 16,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "A community .NET MAUI backend for Linux using GTK4.\n" +
					       "Shell navigation with flyout menu, tabs, and URI routing.",
					HorizontalTextAlignment = TextAlignment.Center,
					TextColor = Colors.DimGray,
				},
			}
		};
	}
}

/// <summary>
/// Detail page that receives query parameters via IQueryAttributable.
/// Demonstrates Shell GoToAsync("route?id=42&amp;name=Widget") support.
/// </summary>
[QueryProperty(nameof(ItemId), "id")]
[QueryProperty(nameof(ItemName), "name")]
class ShellDetailsPage : ContentPage
{
	readonly Label _idLabel;
	readonly Label _nameLabel;

	string? _itemId;
	string? _itemName;

	public string? ItemId
	{
		get => _itemId;
		set { _itemId = value; _idLabel.Text = $"ID: {value ?? "(none)"}"; }
	}

	public string? ItemName
	{
		get => _itemName;
		set { _itemName = value; _nameLabel.Text = $"Name: {value ?? "(none)"}"; }
	}

	public ShellDetailsPage()
	{
		Title = "Details";
		_idLabel = new Label { Text = "ID: (loading...)", FontSize = 16 };
		_nameLabel = new Label { Text = "Name: (loading...)", FontSize = 16 };

		Content = new StackLayout
		{
			Padding = new Thickness(24),
			Spacing = 12,
			Children =
			{
				new Label { Text = "📋 Detail Page (Query Params)", FontSize = 22, FontAttributes = FontAttributes.Bold },
				new Label { Text = "This page received data via Shell query parameters:", TextColor = Colors.DimGray },
				new BoxView { HeightRequest = 2, Color = Colors.Coral },
				_idLabel,
				_nameLabel,
				new BoxView { HeightRequest = 2, Color = Colors.Coral },
				new Button
				{
					Text = "← Back",
					BackgroundColor = Colors.Gray, TextColor = Colors.White,
					Command = new Command(async () => await Shell.Current.GoToAsync("..")),
				},
			}
		};
	}
}
