using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

static class GridRowHelper
{
	public static T Row<T>(this T view, int row) where T : BindableObject
	{
		Grid.SetRow(view, row);
		return view;
	}
}

public class CollectionViewPage : ContentPage
{
	public CollectionViewPage()
	{
		Title = "CollectionView";

		var contentArea = new Grid
		{
			RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Star)),
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Fill,
		};

		var pages = new (string title, Func<View> builder)[]
		{
			("Simple List", BuildSimpleList),
			("Templated", BuildTemplatedList),
			("Multi-Select", BuildMultiSelectList),
			("CollectionView", BuildCollectionViewDemo),
			("Grouped", BuildGroupedDemo),
			("10K Virtual", BuildVirtualizedDemo),
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
			var built = pages[picker.SelectedIndex].builder();
			contentArea.Children.Add(built);
		};

		// Activate first example
		picker.SelectedIndex = 0;

		Content = new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			Padding = new Thickness(24),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "CollectionView", FontSize = 24, FontAttributes = FontAttributes.Bold },
				new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue }.Row(1),
				picker.Row(2),
				contentArea.Row(3),
			}
		};
	}

	// --- Tab 1: Simple filterable string list ---

	View BuildSimpleList()
	{
		var items = Enumerable.Range(1, 50)
			.Select(i => $"Item {i} — {GetDescription(i)}")
			.ToList();

		var selectedLabel = new Label { Text = "Tap an item to select it", FontSize = 14, TextColor = Colors.Gray };
		var countLabel = new Label { Text = $"Showing {items.Count} items", FontSize = 12, TextColor = Colors.DodgerBlue };
		var searchBar = new SearchBar { Placeholder = "Filter items..." };
		var stackList = new VerticalStackLayout { Spacing = 0 };
		PopulateSimpleList(stackList, items, selectedLabel);

		searchBar.TextChanged += (s, e) =>
		{
			var filtered = string.IsNullOrWhiteSpace(e.NewTextValue)
				? items
				: items.Where(i => i.Contains(e.NewTextValue, StringComparison.OrdinalIgnoreCase)).ToList();
			countLabel.Text = $"Showing {filtered.Count} items";
			PopulateSimpleList(stackList, filtered, selectedLabel);
		};

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			RowSpacing = 8,
			Children =
			{
				searchBar,
				countLabel.Row(1),
				selectedLabel.Row(2),
				new ScrollView { Content = stackList }.Row(3),
			}
		};
	}

	void PopulateSimpleList(VerticalStackLayout stack, IList<string> items, Label selectedLabel)
	{
		stack.Children.Clear();
		foreach (var item in items)
		{
			var btn = new Button
			{
				Text = item,
				BackgroundColor = Colors.Transparent,
				TextColor = Colors.Black,
				FontSize = 14,
				HorizontalOptions = LayoutOptions.Fill,
			};
			var captured = item;
			btn.Clicked += (s, e) =>
			{
				selectedLabel.Text = $"Selected: {captured}";
				selectedLabel.TextColor = Colors.DodgerBlue;
			};
			stack.Children.Add(btn);
			stack.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#f0f0f0") });
		}
	}

	// --- Tab 2: Templated list with rich item rendering ---

	record ContactItem(string Name, string Email, string Initials, Color AvatarColor);

	View BuildTemplatedList()
	{
		var contacts = new List<ContactItem>
		{
			new("Alice Johnson", "alice@example.com", "AJ", Colors.Coral),
			new("Bob Smith", "bob@example.com", "BS", Colors.CornflowerBlue),
			new("Carol White", "carol@example.com", "CW", Colors.MediumSeaGreen),
			new("David Brown", "david@example.com", "DB", Colors.MediumOrchid),
			new("Eve Davis", "eve@example.com", "ED", Colors.SandyBrown),
			new("Frank Miller", "frank@example.com", "FM", Colors.SlateBlue),
			new("Grace Lee", "grace@example.com", "GL", Colors.Teal),
			new("Hank Wilson", "hank@example.com", "HW", Colors.IndianRed),
			new("Ivy Chen", "ivy@example.com", "IC", Colors.DarkCyan),
			new("Jack Taylor", "jack@example.com", "JT", Colors.OliveDrab),
			new("Karen Moore", "karen@example.com", "KM", Colors.Crimson),
			new("Leo Martinez", "leo@example.com", "LM", Colors.DodgerBlue),
		};

		var selectedLabel = new Label { Text = "Tap a contact to view details", FontSize = 14, TextColor = Colors.Gray };
		var stack = new VerticalStackLayout { Spacing = 4 };

		foreach (var contact in contacts)
		{
			var card = BuildContactCard(contact, selectedLabel);
			stack.Children.Add(card);
		}

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "Contacts", FontSize = 16, FontAttributes = FontAttributes.Bold },
				selectedLabel.Row(1),
				new ScrollView { Content = stack }.Row(2),
			}
		};
	}

	View BuildContactCard(ContactItem contact, Label selectedLabel)
	{
		var avatar = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
			BackgroundColor = contact.AvatarColor,
			WidthRequest = 40,
			HeightRequest = 40,
			StrokeThickness = 0,
			Content = new Label
			{
				Text = contact.Initials,
				TextColor = Colors.White,
				FontSize = 16,
				FontAttributes = FontAttributes.Bold,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
			}
		};

		var info = new VerticalStackLayout
		{
			Spacing = 2,
			Children =
			{
				new Label { Text = contact.Name, FontSize = 15, FontAttributes = FontAttributes.Bold },
				new Label { Text = contact.Email, FontSize = 12, TextColor = Colors.Gray },
			}
		};

		var row = new HorizontalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(12, 8),
			Children = { avatar, info }
		};

		var tapGesture = new TapGestureRecognizer();
		tapGesture.Tapped += (s, e) =>
		{
			selectedLabel.Text = $"📧 {contact.Name} ({contact.Email})";
			selectedLabel.TextColor = contact.AvatarColor;
		};

		var container = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Stroke = Color.FromArgb("#e0e0e0"),
			StrokeThickness = 1,
			BackgroundColor = Colors.White,
			Content = row,
		};
		container.GestureRecognizers.Add(tapGesture);
		return container;
	}

	// --- Tab 3: Multi-select list with checkboxes ---

	View BuildMultiSelectList()
	{
		var tasks = new List<(string name, string priority)>
		{
			("Review pull request #42", "High"),
			("Update documentation", "Medium"),
			("Fix login page CSS", "High"),
			("Write unit tests for API", "Medium"),
			("Deploy staging build", "Low"),
			("Refactor database queries", "High"),
			("Design new onboarding flow", "Medium"),
			("Update dependencies", "Low"),
			("Fix memory leak in worker", "High"),
			("Add dark mode support", "Medium"),
			("Create CI/CD pipeline", "Medium"),
			("Optimize image loading", "Low"),
			("Implement search feature", "High"),
			("Localize UI strings", "Low"),
			("Add telemetry events", "Medium"),
		};

		var selectionLabel = new Label { Text = "0 tasks selected", FontSize = 14, TextColor = Colors.Gray };
		var selected = new HashSet<int>();

		var selectAllBox = new CheckBox { IsChecked = false };
		var selectAllRow = new HorizontalStackLayout
		{
			Spacing = 8,
			Padding = new Thickness(12, 4),
			Children = { selectAllBox, new Label { Text = "Select All", FontSize = 14, VerticalTextAlignment = TextAlignment.Center } }
		};

		var stack = new VerticalStackLayout { Spacing = 0 };
		var checkBoxes = new List<CheckBox>();

		for (int i = 0; i < tasks.Count; i++)
		{
			var (name, priority) = tasks[i];
			var idx = i;

			var cb = new CheckBox();
			checkBoxes.Add(cb);

			var priorityColor = priority switch
			{
				"High" => Colors.Red,
				"Medium" => Colors.Orange,
				_ => Colors.Gray,
			};

			var row = new HorizontalStackLayout
			{
				Spacing = 8,
				Padding = new Thickness(12, 6),
				Children =
				{
					cb,
					new Border
					{
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
						BackgroundColor = priorityColor,
						Padding = new Thickness(6, 2),
						StrokeThickness = 0,
						Content = new Label { Text = priority, FontSize = 10, TextColor = Colors.White },
					},
					new Label { Text = name, FontSize = 14, VerticalTextAlignment = TextAlignment.Center },
				}
			};

			cb.CheckedChanged += (s, e) =>
			{
				if (e.Value) selected.Add(idx); else selected.Remove(idx);
				selectionLabel.Text = selected.Count == 0
					? "0 tasks selected"
					: $"{selected.Count} task{(selected.Count == 1 ? "" : "s")} selected";
				selectionLabel.TextColor = selected.Count > 0 ? Colors.DodgerBlue : Colors.Gray;
			};

			stack.Children.Add(row);
			stack.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#f0f0f0") });
		}

		selectAllBox.CheckedChanged += (s, e) =>
		{
			foreach (var cb in checkBoxes)
				cb.IsChecked = e.Value;
		};

		var deleteBtn = new Button
		{
			Text = "🗑️ Delete Selected",
			BackgroundColor = Colors.Red,
			TextColor = Colors.White,
			FontSize = 13,
			IsEnabled = true,
		};
		deleteBtn.Clicked += (s, e) =>
		{
			selectionLabel.Text = selected.Count > 0
				? $"Would delete {selected.Count} task(s)"
				: "No tasks selected";
		};

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
				new RowDefinition(GridLength.Auto)
			),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "Task List", FontSize = 16, FontAttributes = FontAttributes.Bold },
				selectionLabel.Row(1),
				selectAllRow.Row(2),
				new BoxView { HeightRequest = 1, Color = Colors.LightGray }.Row(3),
				new ScrollView { Content = stack }.Row(4),
				deleteBtn.Row(5),
			}
		};
	}

	// --- Tab 4: Actual CollectionView control demo ---

	View BuildCollectionViewDemo()
	{
		var contacts = new List<ContactItem>
		{
			new("Alice Johnson", "alice@example.com", "AJ", Colors.Coral),
			new("Bob Smith", "bob@example.com", "BS", Colors.CornflowerBlue),
			new("Carol White", "carol@example.com", "CW", Colors.MediumSeaGreen),
			new("David Brown", "david@example.com", "DB", Colors.MediumOrchid),
			new("Eve Davis", "eve@example.com", "ED", Colors.SandyBrown),
			new("Frank Miller", "frank@example.com", "FM", Colors.SlateBlue),
			new("Grace Lee", "grace@example.com", "GL", Colors.Teal),
			new("Hank Wilson", "hank@example.com", "HW", Colors.IndianRed),
		};

		var selectedLabel = new Label
		{
			Text = "No selection",
			FontSize = 14,
			TextColor = Colors.Gray,
		};

		var cv = new CollectionView
		{
			ItemsSource = contacts,
			Header = "📇 Contacts (ItemTemplate)",
			Footer = $"{contacts.Count} contacts",
			EmptyView = "No contacts found!",
			SelectionMode = SelectionMode.Single,
			VerticalOptions = LayoutOptions.Fill,
			ItemTemplate = new DataTemplate(() =>
			{
				var avatar = new BoxView
				{
					WidthRequest = 36,
					HeightRequest = 36,
				};
				avatar.SetBinding(BoxView.ColorProperty, "AvatarColor");

				var nameLabel = new Label
				{
					FontSize = 14,
					FontAttributes = FontAttributes.Bold,
				};
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var emailLabel = new Label
				{
					FontSize = 11,
					TextColor = Colors.Gray,
				};
				emailLabel.SetBinding(Label.TextProperty, "Email");

				return new Border
				{
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
					Stroke = Color.FromArgb("#e0e0e0"),
					StrokeThickness = 1,
					Padding = new Thickness(8, 6),
					Content = new HorizontalStackLayout
					{
						Spacing = 10,
						Children =
						{
							avatar,
							new VerticalStackLayout
							{
								Spacing = 2,
								Children = { nameLabel, emailLabel },
							},
						},
					},
				};
			}),
		};

		cv.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is ContactItem contact)
			{
				selectedLabel.Text = $"📧 {contact.Name} ({contact.Email})";
				selectedLabel.TextColor = contact.AvatarColor;
			}
			else
			{
				selectedLabel.Text = "No selection";
				selectedLabel.TextColor = Colors.Gray;
			}
		};

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "CollectionView + ItemTemplate", FontSize = 16, FontAttributes = FontAttributes.Bold },
				new Label { Text = "Uses DataTemplate with data bindings for each row.", FontSize = 12, TextColor = Colors.Gray }.Row(1),
				selectedLabel.Row(2),
				cv.Row(3),
			}
		};
	}

	// --- Tab 5: Grouped CollectionView ---

	View BuildGroupedDemo()
	{
		var groups = new List<AnimalGroup>
		{
			new("🐱 Cats", ["Tabby", "Siamese", "Persian", "Maine Coon", "Bengal"]),
			new("🐶 Dogs", ["Labrador", "Poodle", "Bulldog", "Beagle", "Husky"]),
			new("🐦 Birds", ["Parrot", "Eagle", "Sparrow", "Penguin"]),
			new("🐠 Fish", ["Goldfish", "Clownfish", "Salmon"]),
		};

		var cv = new CollectionView
		{
			ItemsSource = groups,
			IsGrouped = true,
			Header = "🐾 Grouped Animals",
			Footer = $"{groups.Sum(g => g.Count)} animals in {groups.Count} groups",
			VerticalOptions = LayoutOptions.Fill,
			SelectionMode = SelectionMode.Single,
			GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label
				{
					FontSize = 15,
					FontAttributes = FontAttributes.Bold,
					TextColor = Colors.Teal,
				};
				label.SetBinding(Label.TextProperty, "Name");
				return new VerticalStackLayout
				{
					Padding = new Thickness(8, 10, 8, 4),
					Children = { label },
				};
			}),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 14 };
				label.SetBinding(Label.TextProperty, ".");
				return new VerticalStackLayout
				{
					Padding = new Thickness(24, 4),
					Children = { label },
				};
			}),
		};

		var selectedLabel = new Label { Text = "Select an animal", FontSize = 14, TextColor = Colors.Gray };
		cv.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is string animal)
			{
				selectedLabel.Text = $"Selected: {animal}";
				selectedLabel.TextColor = Colors.Teal;
			}
		};

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "Grouped CollectionView", FontSize = 16, FontAttributes = FontAttributes.Bold },
				new Label { Text = "IsGrouped=true with GroupHeaderTemplate + ItemTemplate", FontSize = 12, TextColor = Colors.Gray }.Row(1),
				selectedLabel.Row(2),
				cv.Row(3),
			}
		};
	}

	// --- Tab 6: Virtualized 10K item list ---

	View BuildVirtualizedDemo()
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var items = Enumerable.Range(1, 10_000)
			.Select(i => new LargeListItem(i, $"Item #{i}", $"Description for item {i}", GetItemColor(i)))
			.ToList();
		sw.Stop();

		var infoLabel = new Label
		{
			Text = $"10,000 items generated in {sw.ElapsedMilliseconds}ms. Scroll to test virtualization.",
			FontSize = 12,
			TextColor = Colors.Gray,
		};
		var selectedLabel = new Label { Text = "No selection", FontSize = 14, TextColor = Colors.Gray };
		var posLabel = new Label { Text = "Position: top", FontSize = 12, TextColor = Colors.DimGray };

		var cv = new CollectionView
		{
			ItemsSource = items,
			Header = $"📊 10,000 Items (Virtualized)",
			Footer = $"Total: {items.Count:N0} items",
			SelectionMode = SelectionMode.Single,
			VerticalOptions = LayoutOptions.Fill,
			ItemTemplate = new DataTemplate(() =>
			{
				var idLabel = new Label { FontSize = 11, TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
				idLabel.SetBinding(Label.TextProperty, "IdText");

				var colorBox = new BoxView { WidthRequest = 32, HeightRequest = 32 };
				colorBox.SetBinding(BoxView.ColorProperty, "Color");

				var nameLabel = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var descLabel = new Label { FontSize = 11, TextColor = Colors.Gray };
				descLabel.SetBinding(Label.TextProperty, "Description");

				return new HorizontalStackLayout
				{
					Spacing = 10,
					Padding = new Thickness(8, 4),
					Children =
					{
						colorBox,
						new VerticalStackLayout
						{
							Spacing = 1,
							Children = { nameLabel, descLabel },
						},
					},
				};
			}),
		};

		cv.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is LargeListItem item)
			{
				selectedLabel.Text = $"Selected: {item.Name}";
				selectedLabel.TextColor = item.Color;
			}
		};

		// Jump buttons to test scroll
		var jumpTop = new Button { Text = "Jump to #1", FontSize = 12, BackgroundColor = Colors.Gray, TextColor = Colors.White };
		jumpTop.Clicked += (s, e) => { cv.ScrollTo(0); posLabel.Text = "Position: #1"; };

		var jumpMid = new Button { Text = "Jump to #5000", FontSize = 12, BackgroundColor = Colors.Gray, TextColor = Colors.White };
		jumpMid.Clicked += (s, e) => { cv.ScrollTo(4999); posLabel.Text = "Position: #5000"; };

		var jumpEnd = new Button { Text = "Jump to #10000", FontSize = 12, BackgroundColor = Colors.Gray, TextColor = Colors.White };
		jumpEnd.Clicked += (s, e) => { cv.ScrollTo(9999); posLabel.Text = "Position: #10000"; };

		return new Grid
		{
			RowDefinitions = new RowDefinitionCollection(
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			),
			RowSpacing = 8,
			Children =
			{
				new Label { Text = "Virtualized CollectionView (10K items)", FontSize = 16, FontAttributes = FontAttributes.Bold },
				infoLabel.Row(1),
				selectedLabel.Row(2),
				posLabel.Row(3),
				new HorizontalStackLayout { Spacing = 8, Children = { jumpTop, jumpMid, jumpEnd } }.Row(4),
				cv.Row(5),
			}
		};
	}

	static Color GetItemColor(int i) => (i % 7) switch
	{
		0 => Colors.CornflowerBlue,
		1 => Colors.Coral,
		2 => Colors.MediumSeaGreen,
		3 => Colors.MediumOrchid,
		4 => Colors.SandyBrown,
		5 => Colors.SlateBlue,
		_ => Colors.Teal,
	};

	record LargeListItem(int Id, string Name, string Description, Color Color)
	{
		public string IdText => $"#{Id}";
	}

	static string GetDescription(int i) => (i % 5) switch
	{
		0 => "🔴 Important task",
		1 => "🟢 Completed item",
		2 => "🔵 In progress",
		3 => "🟡 Pending review",
		_ => "⚪ Backlog item",
	};
}

/// <summary>
/// A grouped list of animals for CollectionView grouping demo.
/// Implements IList&lt;string&gt; so it works as both the group header and the group items.
/// </summary>
class AnimalGroup : List<string>
{
	public string Name { get; }
	public AnimalGroup(string name, IEnumerable<string> items) : base(items)
	{
		Name = name;
	}
	public override string ToString() => Name;
}
