using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class CollectionViewPage : TabbedPage
{
	public CollectionViewPage()
	{
		Title = "CollectionView";

		Children.Add(new VerticalListTab());
		Children.Add(new HorizontalListTab());
		Children.Add(new VerticalGridTab());
		Children.Add(new HorizontalGridTab());
		Children.Add(new GroupedTab());
		Children.Add(new TemplateSelectorTab());
		Children.Add(new SelectionTab());
		Children.Add(new LargeListTab());
		Children.Add(new EmptyViewTab());
		Children.Add(new HeaderFooterTab());
		Children.Add(new ScrollToTab());
	}
}

#region Data Models

record SimpleItem(string Name, string Description, Color AccentColor);

record GroupedItem(string Name, string Detail);

class AnimalGroup : ObservableCollection<GroupedItem>
{
	public string GroupName { get; }
	public AnimalGroup(string name, IEnumerable<GroupedItem> items) : base(items)
	{
		GroupName = name;
	}
}

record MixedItem(string Title, string Subtitle, string ItemType, Color Color);

#endregion

#region Template Selector

class ItemTypeTemplateSelector : DataTemplateSelector
{
	public DataTemplate? CardTemplate { get; set; }
	public DataTemplate? CompactTemplate { get; set; }
	public DataTemplate? BannerTemplate { get; set; }

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
	{
		if (item is MixedItem mi)
		{
			return mi.ItemType switch
			{
				"banner" => BannerTemplate!,
				"compact" => CompactTemplate!,
				_ => CardTemplate!,
			};
		}
		return CardTemplate!;
	}
}

#endregion

#region Tab Pages

class VerticalListTab : ContentPage
{
	public VerticalListTab()
	{
		Title = "Vertical";
		var items = CollectionViewHelpers.GenerateSimpleItems(30);
		var statusLabel = new Label { Text = "Tap an item", FontSize = 12, TextColor = Colors.Gray, Margin = new Thickness(16, 8) };

		var cv = new CollectionView
		{
			ItemsSource = items,
			SelectionMode = SelectionMode.Single,
			ItemsLayout = LinearItemsLayout.Vertical,
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var descLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
				descLabel.SetBinding(Label.TextProperty, "Description");

				var accent = new BoxView { WidthRequest = 4, CornerRadius = 2 };
				accent.SetBinding(BoxView.ColorProperty, "AccentColor");

				return new Border
				{
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
					Stroke = Colors.Gray.WithAlpha(0.3f),
					StrokeThickness = 1,
					Margin = new Thickness(16, 4),
					Padding = 0,
					Content = new HorizontalStackLayout
					{
						Spacing = 12,
						Padding = new Thickness(0, 8, 12, 8),
						Children =
						{
							accent,
							new VerticalStackLayout
							{
								Spacing = 2,
								VerticalOptions = LayoutOptions.Center,
								Children = { nameLabel, descLabel }
							}
						}
					}
				};
			})
		};

		cv.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is SimpleItem si)
				statusLabel.Text = $"Selected: {si.Name}";
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(statusLabel, 0, 0);
		grid.Add(cv, 0, 1);
		Content = grid;
	}
}

class HorizontalListTab : ContentPage
{
	public HorizontalListTab()
	{
		Title = "Horizontal";
		var items = CollectionViewHelpers.GenerateSimpleItems(20);

		var cv = new CollectionView
		{
			ItemsSource = items,
			ItemsLayout = LinearItemsLayout.Horizontal,
			HeightRequest = 120,
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var accent = new BoxView { HeightRequest = 60, WidthRequest = 60, CornerRadius = 30 };
				accent.SetBinding(BoxView.ColorProperty, "AccentColor");

				return new VerticalStackLayout
				{
					Spacing = 6,
					WidthRequest = 100,
					Padding = new Thickness(8),
					HorizontalOptions = LayoutOptions.Center,
					Children = { accent, nameLabel }
				};
			})
		};

		Content = new Grid
		{
			Padding = new Thickness(16),
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(new GridLength(120)),
				new RowDefinition(GridLength.Auto),
			},
			Children =
			{
				new Label { Text = "Horizontal List", FontSize = 18, FontAttributes = FontAttributes.Bold },
			}
		};
		var g = (Grid)Content;
		g.Add(new Label { Text = "Scroll horizontally to see more items", FontSize = 12, TextColor = Colors.Gray }, 0, 1);
		g.Add(cv, 0, 2);
		g.Add(new Label { Text = "Content below the horizontal list", FontSize = 14, TextColor = Colors.Gray }, 0, 3);
	}
}

class VerticalGridTab : ContentPage
{
	public VerticalGridTab()
	{
		Title = "Grid (V)";
		var items = CollectionViewHelpers.GenerateSimpleItems(24);

		var cv = new CollectionView
		{
			ItemsSource = items,
			ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical)
			{
				HorizontalItemSpacing = 8,
				VerticalItemSpacing = 8,
			},
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var accent = new BoxView { HeightRequest = 50, CornerRadius = 8 };
				accent.SetBinding(BoxView.ColorProperty, "AccentColor");

				return new Border
				{
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
					Stroke = Colors.Gray.WithAlpha(0.3f),
					StrokeThickness = 1,
					Padding = new Thickness(8),
					Content = new VerticalStackLayout
					{
						Spacing = 6,
						Children = { accent, nameLabel }
					}
				};
			})
		};

		var grid = new Grid
		{
			Padding = new Thickness(16),
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(new Label { Text = "3-Column Vertical Grid", FontSize = 18, FontAttributes = FontAttributes.Bold }, 0, 0);
		grid.Add(cv, 0, 1);
		Content = grid;
	}
}

class HorizontalGridTab : ContentPage
{
	public HorizontalGridTab()
	{
		Title = "Grid (H)";
		var items = CollectionViewHelpers.GenerateSimpleItems(20);

		var cv = new CollectionView
		{
			ItemsSource = items,
			HeightRequest = 200,
			ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Horizontal)
			{
				HorizontalItemSpacing = 8,
				VerticalItemSpacing = 8,
			},
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 11, HorizontalTextAlignment = TextAlignment.Center };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var accent = new BoxView { HeightRequest = 40, WidthRequest = 80, CornerRadius = 6 };
				accent.SetBinding(BoxView.ColorProperty, "AccentColor");

				return new VerticalStackLayout
				{
					Spacing = 4,
					WidthRequest = 100,
					Padding = new Thickness(4),
					Children = { accent, nameLabel }
				};
			})
		};

		var grid = new Grid
		{
			Padding = new Thickness(16),
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(new GridLength(200)),
			}
		};
		grid.Add(new Label { Text = "2-Row Horizontal Grid", FontSize = 18, FontAttributes = FontAttributes.Bold }, 0, 0);
		grid.Add(new Label { Text = "Scroll horizontally — items fill 2 rows", FontSize = 12, TextColor = Colors.Gray }, 0, 1);
		grid.Add(cv, 0, 2);
		Content = grid;
	}
}

class GroupedTab : ContentPage
{
	public GroupedTab()
	{
		Title = "Grouped";
		var groups = new ObservableCollection<AnimalGroup>
		{
			new("🐾 Mammals", new[]
			{
				new GroupedItem("Dog", "Loyal companion"),
				new GroupedItem("Cat", "Independent feline"),
				new GroupedItem("Horse", "Majestic equine"),
				new GroupedItem("Dolphin", "Intelligent marine mammal"),
				new GroupedItem("Elephant", "Gentle giant"),
			}),
			new("🐦 Birds", new[]
			{
				new GroupedItem("Eagle", "Bird of prey"),
				new GroupedItem("Parrot", "Colorful talker"),
				new GroupedItem("Penguin", "Flightless swimmer"),
				new GroupedItem("Owl", "Nocturnal hunter"),
			}),
			new("🦎 Reptiles", new[]
			{
				new GroupedItem("Turtle", "Slow and steady"),
				new GroupedItem("Gecko", "Wall climber"),
				new GroupedItem("Iguana", "Tropical lizard"),
			}),
			new("🐟 Fish", new[]
			{
				new GroupedItem("Clownfish", "Reef dweller"),
				new GroupedItem("Salmon", "Upstream swimmer"),
				new GroupedItem("Shark", "Ocean predator"),
				new GroupedItem("Swordfish", "Fast swimmer"),
				new GroupedItem("Pufferfish", "Inflatable defense"),
			}),
		};

		var cv = new CollectionView
		{
			ItemsSource = groups,
			IsGrouped = true,
			GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label
				{
					FontSize = 16,
					FontAttributes = FontAttributes.Bold,
					Padding = new Thickness(16, 12, 16, 4),
					TextColor = Colors.CornflowerBlue,
				};
				label.SetBinding(Label.TextProperty, "GroupName");
				return label;
			}),
			GroupFooterTemplate = new DataTemplate(() =>
			{
				return new Border
				{
					HeightRequest = 1,
					BackgroundColor = Colors.Gray,
					Opacity = 0.3,
					StrokeThickness = 0,
					Margin = new Thickness(16, 4, 16, 8),
				};
			}),
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var detailLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
				detailLabel.SetBinding(Label.TextProperty, "Detail");

				return new HorizontalStackLayout
				{
					Spacing = 12,
					Padding = new Thickness(32, 6, 16, 6),
					Children =
					{
						new VerticalStackLayout
						{
							Spacing = 2,
							Children = { nameLabel, detailLabel }
						}
					}
				};
			})
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(new Label
		{
			Text = "Grouped CollectionView",
			FontSize = 18,
			FontAttributes = FontAttributes.Bold,
			Padding = new Thickness(16, 12),
		}, 0, 0);
		grid.Add(cv, 0, 1);
		Content = grid;
	}
}

class TemplateSelectorTab : ContentPage
{
	public TemplateSelectorTab()
	{
		Title = "Selector";

		var cardTemplate = new DataTemplate(() =>
		{
			var title = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold };
			title.SetBinding(Label.TextProperty, "Title");
			var sub = new Label { FontSize = 12, TextColor = Colors.Gray };
			sub.SetBinding(Label.TextProperty, "Subtitle");
			var accent = new BoxView { WidthRequest = 4, CornerRadius = 2 };
			accent.SetBinding(BoxView.ColorProperty, "Color");

			return new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
				Stroke = Colors.Gray.WithAlpha(0.3f),
				StrokeThickness = 1,
				Margin = new Thickness(16, 4),
				Padding = 0,
				Content = new HorizontalStackLayout
				{
					Spacing = 12,
					Padding = new Thickness(0, 10, 12, 10),
					Children = { accent, new VerticalStackLayout { Spacing = 2, Children = { title, sub } } }
				}
			};
		});

		var compactTemplate = new DataTemplate(() =>
		{
			var title = new Label { FontSize = 13 };
			title.SetBinding(Label.TextProperty, "Title");
			var dot = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4 };
			dot.SetBinding(BoxView.ColorProperty, "Color");

			return new HorizontalStackLayout
			{
				Spacing = 8,
				Padding = new Thickness(16, 4),
				Children = { dot, title }
			};
		});

		var bannerTemplate = new DataTemplate(() =>
		{
			var title = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center };
			title.SetBinding(Label.TextProperty, "Title");
			var sub = new Label { FontSize = 12, TextColor = Colors.White.WithAlpha(0.8f), HorizontalTextAlignment = TextAlignment.Center };
			sub.SetBinding(Label.TextProperty, "Subtitle");
			var bg = new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
				StrokeThickness = 0,
				Padding = new Thickness(20, 16),
				Margin = new Thickness(16, 6),
				Content = new VerticalStackLayout { Spacing = 4, Children = { title, sub } }
			};
			bg.SetBinding(Border.BackgroundColorProperty, "Color");
			return bg;
		});

		var selector = new ItemTypeTemplateSelector
		{
			CardTemplate = cardTemplate,
			CompactTemplate = compactTemplate,
			BannerTemplate = bannerTemplate,
		};

		var items = new List<MixedItem>
		{
			new("🎉 Welcome Banner", "Featured content at the top", "banner", Colors.CornflowerBlue),
			new("Project Alpha", "In development", "card", Colors.MediumSeaGreen),
			new("Bug fix #123", "Resolved", "compact", Colors.Gray),
			new("Bug fix #124", "Resolved", "compact", Colors.Gray),
			new("Bug fix #125", "In progress", "compact", Colors.Orange),
			new("🚀 Release 2.0", "Coming soon — new features inside", "banner", Colors.MediumOrchid),
			new("Project Beta", "Planning phase", "card", Colors.Coral),
			new("Project Gamma", "Testing", "card", Colors.Teal),
			new("Task: update docs", "Pending", "compact", Colors.SandyBrown),
			new("Task: review PR", "Pending", "compact", Colors.SandyBrown),
			new("🏆 Achievement", "100 commits this month!", "banner", Colors.Goldenrod),
			new("Project Delta", "Released", "card", Colors.SlateBlue),
		};

		var cv = new CollectionView
		{
			ItemsSource = items,
			ItemTemplate = selector,
		};

		var header = new VerticalStackLayout
		{
			Children =
			{
				new Label
				{
					Text = "DataTemplateSelector",
					FontSize = 18,
					FontAttributes = FontAttributes.Bold,
					Padding = new Thickness(16, 12),
				},
				new Label
				{
					Text = "Three template types: banner, card, compact",
					FontSize = 12,
					TextColor = Colors.Gray,
					Padding = new Thickness(16, 0, 16, 8),
				},
			}
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(header, 0, 0);
		grid.Add(cv, 0, 1);
		Content = grid;
	}
}

class SelectionTab : ContentPage
{
	public SelectionTab()
	{
		Title = "Selection";
		var items = CollectionViewHelpers.GenerateSimpleItems(20);
		var statusLabel = new Label
		{
			Text = "Tap items to select — using Single mode",
			FontSize = 12,
			TextColor = Colors.Gray,
			Margin = new Thickness(16, 4),
		};

		var cv = new CollectionView
		{
			ItemsSource = items,
			SelectionMode = SelectionMode.Single,
			ItemsLayout = LinearItemsLayout.Vertical,
			ItemTemplate = new DataTemplate(() =>
			{
				var nameLabel = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold };
				nameLabel.SetBinding(Label.TextProperty, "Name");

				var descLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
				descLabel.SetBinding(Label.TextProperty, "Description");

				var accent = new BoxView { WidthRequest = 4, CornerRadius = 2 };
				accent.SetBinding(BoxView.ColorProperty, "AccentColor");

				return new Border
				{
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
					Stroke = Colors.Gray.WithAlpha(0.3f),
					StrokeThickness = 1,
					Margin = new Thickness(16, 4),
					Padding = 0,
					Content = new HorizontalStackLayout
					{
						Spacing = 12,
						Padding = new Thickness(0, 8, 12, 8),
						Children =
						{
							accent,
							new VerticalStackLayout
							{
								Spacing = 2,
								VerticalOptions = LayoutOptions.Center,
								Children = { nameLabel, descLabel }
							}
						}
					}
				};
			})
		};

		cv.SelectionChanged += (s, e) =>
		{
			if (cv.SelectionMode == SelectionMode.Single)
			{
				if (e.CurrentSelection.FirstOrDefault() is SimpleItem si)
					statusLabel.Text = $"Selected: {si.Name}";
				else
					statusLabel.Text = "No selection";
			}
			else
			{
				statusLabel.Text = $"Selected {e.CurrentSelection.Count} item(s)";
			}
		};

		var modeButton = new Button
		{
			Text = "Mode: Single",
			Margin = new Thickness(16, 4),
			HorizontalOptions = LayoutOptions.Start,
		};
		modeButton.Clicked += (s, e) =>
		{
			if (cv.SelectionMode == SelectionMode.Single)
			{
				cv.SelectionMode = SelectionMode.Multiple;
				modeButton.Text = "Mode: Multiple";
				statusLabel.Text = "Tap items to select — using Multiple mode";
			}
			else if (cv.SelectionMode == SelectionMode.Multiple)
			{
				cv.SelectionMode = SelectionMode.None;
				modeButton.Text = "Mode: None";
				statusLabel.Text = "Selection disabled";
			}
			else
			{
				cv.SelectionMode = SelectionMode.Single;
				modeButton.Text = "Mode: Single";
				statusLabel.Text = "Tap items to select — using Single mode";
			}
		};

		var clearButton = new Button
		{
			Text = "Clear Selection",
			Margin = new Thickness(4),
			HorizontalOptions = LayoutOptions.Start,
		};
		clearButton.Clicked += (s, e) =>
		{
			cv.SelectedItem = null;
			cv.SelectedItems?.Clear();
			statusLabel.Text = "Selection cleared";
		};

		var buttonRow = new HorizontalStackLayout
		{
			Spacing = 8,
			Margin = new Thickness(12, 0),
			Children = { modeButton, clearButton }
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(buttonRow, 0, 0);
		grid.Add(statusLabel, 0, 1);
		grid.Add(cv, 0, 2);
		Content = grid;
	}
}

class LargeListTab : ContentPage
{
	public LargeListTab()
	{
		Title = "Large (10k)";

		var items = new List<string>();
		for (int i = 0; i < 10000; i++)
			items.Add($"Item {i + 1:N0}");

		var countLabel = new Label
		{
			Text = $"10,000 items — virtualized",
			FontSize = 12,
			TextColor = Colors.Gray,
			Padding = new Thickness(16, 8),
		};

		var cv = new CollectionView
		{
			ItemsSource = items,
			ItemsLayout = LinearItemsLayout.Vertical,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 14, Padding = new Thickness(16, 8) };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			})
		};

		var header = new VerticalStackLayout
		{
			Children =
			{
				countLabel,
				new Label
				{
					Text = "Virtualized — only visible items are rendered",
					FontSize = 11,
					TextColor = Colors.MediumSeaGreen,
					Padding = new Thickness(16, 0, 16, 8),
				},
			}
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			}
		};
		grid.Add(header, 0, 0);
		grid.Add(cv, 0, 1);
		Content = grid;
	}
}

#endregion

#region Helpers

static class CollectionViewHelpers
{
	static readonly Color[] AccentColors =
	{
		Colors.CornflowerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.MediumOrchid,
		Colors.SandyBrown, Colors.Teal, Colors.IndianRed, Colors.DodgerBlue,
		Colors.SlateBlue, Colors.OliveDrab, Colors.Crimson, Colors.DarkCyan,
	};

	public static List<SimpleItem> GenerateSimpleItems(int count) =>
		Enumerable.Range(1, count)
			.Select(i => new SimpleItem(
				$"Item {i}",
				$"Description for item {i}",
				AccentColors[(i - 1) % AccentColors.Length]))
			.ToList();
}

#endregion

#region EmptyView Tab

class EmptyViewTab : ContentPage
{
	readonly ObservableCollection<SimpleItem> _items = new();
	readonly CollectionView _collectionView;

	public EmptyViewTab()
	{
		Title = "EmptyView";

		_collectionView = new CollectionView
		{
			ItemsSource = _items,
			EmptyView = new StackLayout
			{
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Center,
				Children =
				{
					new Label { Text = "📭", FontSize = 48, HorizontalTextAlignment = TextAlignment.Center },
					new Label { Text = "No items yet", FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
					new Label { Text = "Tap 'Add Items' to populate the list", FontSize = 14, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
				}
			},
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 16, Margin = new Thickness(12, 8) };
				label.SetBinding(Label.TextProperty, "Name");
				return label;
			}),
		};

		var addButton = new Button { Text = "Add 5 Items" };
		addButton.Clicked += (s, e) =>
		{
			var start = _items.Count + 1;
			for (int i = start; i < start + 5; i++)
				_items.Add(new SimpleItem($"Item {i}", $"Added item {i}", Colors.Blue));
		};

		var clearButton = new Button { Text = "Clear All" };
		clearButton.Clicked += (s, e) => _items.Clear();

		Content = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			},
			Children =
			{
				new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(8), Children = { addButton, clearButton } },
			}
		};
		Grid.SetRow(_collectionView, 1);
		((Grid)Content).Children.Add(_collectionView);
	}
}

#endregion

#region Header/Footer Tab

class HeaderFooterTab : ContentPage
{
	public HeaderFooterTab()
	{
		Title = "Header/Footer";

		var items = CollectionViewHelpers.GenerateSimpleItems(15);

		var cv = new CollectionView
		{
			ItemsSource = items,
			Header = "📋 Collection Header — 15 items total",
			Footer = "— End of List —",
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 16, Margin = new Thickness(12, 8) };
				label.SetBinding(Label.TextProperty, "Name");
				return label;
			}),
		};

		var grid = new Grid
		{
			RowDefinitions = { new RowDefinition(GridLength.Star) },
		};
		grid.Add(cv);
		Content = grid;
	}
}

#endregion

#region ScrollTo Tab

class ScrollToTab : ContentPage
{
	readonly CollectionView _collectionView;
	readonly List<SimpleItem> _items;

	public ScrollToTab()
	{
		Title = "ScrollTo";

		_items = CollectionViewHelpers.GenerateSimpleItems(100);

		_collectionView = new CollectionView
		{
			ItemsSource = _items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 14, Margin = new Thickness(12, 6) };
				label.SetBinding(Label.TextProperty, "Name");
				return label;
			}),
		};

		var scrollToStart = new Button { Text = "→ First" };
		scrollToStart.Clicked += (s, e) => _collectionView.ScrollTo(0, position: ScrollToPosition.Start, animate: true);

		var scrollToMiddle = new Button { Text = "→ Item 50" };
		scrollToMiddle.Clicked += (s, e) => _collectionView.ScrollTo(49, position: ScrollToPosition.Center, animate: true);

		var scrollToEnd = new Button { Text = "→ Last" };
		scrollToEnd.Clicked += (s, e) => _collectionView.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: true);

		var scrollToItem = new Button { Text = "→ Item 75 (by item)" };
		scrollToItem.Clicked += (s, e) => _collectionView.ScrollTo(_items[74], position: ScrollToPosition.Center, animate: true);

		var buttonBar = new HorizontalStackLayout
		{
			Spacing = 8,
			Margin = new Thickness(8),
			Children = { scrollToStart, scrollToMiddle, scrollToEnd, scrollToItem }
		};

		Content = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			},
			Children = { buttonBar }
		};
		Grid.SetRow(_collectionView, 1);
		((Grid)Content).Children.Add(_collectionView);
	}
}

#endregion
