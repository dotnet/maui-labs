using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates ListView and TableView on the GTK4 backend.
/// Both are deprecated in .NET MAUI 10 but still supported for compatibility.
/// </summary>
#pragma warning disable CS0618
public class ListViewTableViewPage : ContentPage
{
	public ListViewTableViewPage()
	{
		Title = "ListView & TableView";

		var contentArea = new VerticalStackLayout();

		var pages = new (string title, Func<View> builder)[]
		{
			("ListView", BuildListViewDemo),
			("ListView+Template", BuildTemplatedListView),
			("TableView", BuildTableViewDemo),
		};

		var picker = new Picker
		{
			Title = "Select example",
			FontSize = 14,
			HorizontalOptions = LayoutOptions.Start,
		};
		foreach (var (title, _) in pages)
			picker.Items.Add(title);

		picker.SelectedIndexChanged += (s, e) =>
		{
			if (picker.SelectedIndex < 0) return;
			contentArea.Children.Clear();
			contentArea.Children.Add(pages[picker.SelectedIndex].builder());
		};

		picker.SelectedIndex = 0;

		Content = new VerticalStackLayout
		{
			Spacing = 8,
			Padding = new Thickness(24),
			Children =
			{
				new Label { Text = "ListView & TableView", FontSize = 24, FontAttributes = FontAttributes.Bold },
				new Label { Text = "Deprecated in MAUI 10 — prefer CollectionView. Shown for compatibility.", FontSize = 12, TextColor = Colors.Gray },
				new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },
				picker,
				contentArea,
			}
		};
	}

	static View BuildListViewDemo()
	{
		var selectedLabel = new Label { Text = "Tap an item", FontSize = 14, TextColor = Colors.Gray };

		var items = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon" };

		var listView = new ListView
		{
			ItemsSource = items,
			Header = "🍎 Fruits",
			Footer = $"{items.Length} items",
			HeightRequest = 350,
		};

		listView.ItemSelected += (s, e) =>
		{
			if (e.SelectedItem != null)
			{
				selectedLabel.Text = $"Selected: {e.SelectedItem}";
				selectedLabel.TextColor = Colors.DodgerBlue;
			}
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = "Simple ListView", FontSize = 16, FontAttributes = FontAttributes.Bold },
				selectedLabel,
				listView,
			}
		};
	}

	record ContactItem(string Name, string Email, string Department, Color DeptColor);

	static View BuildTemplatedListView()
	{
		var contacts = new List<ContactItem>
		{
			new("Alice", "alice@corp.com", "Engineering", Colors.CornflowerBlue),
			new("Bob", "bob@corp.com", "Design", Colors.Coral),
			new("Carol", "carol@corp.com", "Engineering", Colors.CornflowerBlue),
			new("Dave", "dave@corp.com", "Marketing", Colors.MediumSeaGreen),
			new("Eve", "eve@corp.com", "Design", Colors.Coral),
			new("Frank", "frank@corp.com", "Engineering", Colors.CornflowerBlue),
		};

		var selectedLabel = new Label { Text = "Tap a contact", FontSize = 14, TextColor = Colors.Gray };

		var listView = new ListView
		{
			ItemsSource = contacts,
			HeightRequest = 350,
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontAttributes = FontAttributes.Bold };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var emailLabel = new Label { FontSize = 11, TextColor = Colors.Gray };
				emailLabel.SetBinding(Label.TextProperty, "Email");

				var deptLabel = new Label { FontSize = 10, TextColor = Colors.White };
				deptLabel.SetBinding(Label.TextProperty, "Department");

				return new ViewCell
				{
					View = new HorizontalStackLayout
					{
						Spacing = 10,
						Padding = new Thickness(8, 6),
						Children =
						{
							new VerticalStackLayout
							{
								Spacing = 2,
								Children = { nameLabel, emailLabel },
							},
						}
					}
				};
			}),
		};

		listView.ItemSelected += (s, e) =>
		{
			if (e.SelectedItem is ContactItem c)
			{
				selectedLabel.Text = $"📧 {c.Name} ({c.Email}) — {c.Department}";
				selectedLabel.TextColor = c.DeptColor;
			}
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = "ListView + ViewCell Template", FontSize = 16, FontAttributes = FontAttributes.Bold },
				selectedLabel,
				listView,
			}
		};
	}

	static View BuildTableViewDemo()
	{
		var statusLabel = new Label { Text = "Change settings below", FontSize = 14, TextColor = Colors.Gray };

		var tableView = new TableView
		{
			Intent = TableIntent.Settings,
			HeightRequest = 400,
			Root = new TableRoot("Settings")
			{
				new TableSection("Profile")
				{
					new EntryCell { Label = "Name", Placeholder = "Enter your name" },
					new EntryCell { Label = "Email", Placeholder = "Enter email" },
				},
				new TableSection("Preferences")
				{
					new SwitchCell { Text = "Dark Mode", On = false },
					new SwitchCell { Text = "Notifications", On = true },
					new SwitchCell { Text = "Auto-Update", On = true },
				},
				new TableSection("About")
				{
					new TextCell { Text = "Version", Detail = "1.0.0" },
					new TextCell { Text = "Platform", Detail = "Linux GTK4" },
					new TextCell { Text = "License", Detail = "MIT" },
				},
			}
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = "TableView (Settings)", FontSize = 16, FontAttributes = FontAttributes.Bold },
				statusLabel,
				tableView,
			}
		};
	}
}
#pragma warning restore CS0618
