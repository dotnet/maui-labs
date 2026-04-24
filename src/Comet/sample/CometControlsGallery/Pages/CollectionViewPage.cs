using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	record SimpleItem(string Name, string Description, Color AccentColor);
	record MixedItem(string Title, string Subtitle, string ItemType, Color Color);

	public class CollectionViewPage : View
	{
		static readonly Color[] AccentColors =
		{
			Colors.CornflowerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.MediumOrchid,
			Colors.SandyBrown, Colors.Teal, Colors.IndianRed, Colors.DodgerBlue,
			Colors.SlateBlue, Colors.OliveDrab, Colors.Crimson, Colors.DarkCyan,
		};

		static List<SimpleItem> GenerateItems(int count) =>
			Enumerable.Range(1, count)
				.Select(i => new SimpleItem(
					$"Item {i}",
					$"Description for item {i}",
					AccentColors[(i - 1) % AccentColors.Length]))
				.ToList();

		static readonly List<SimpleItem> VerticalItems = GenerateItems(30);
		static readonly List<SimpleItem> HorizontalItems = GenerateItems(20);
		static readonly List<SimpleItem> GridItems = GenerateItems(24);
		static readonly List<SimpleItem> GridHItems = GenerateItems(20);
		static readonly List<SimpleItem> SelectionItems = GenerateItems(20);
		static readonly List<SimpleItem> HeaderFooterItems = GenerateItems(15);
		static readonly List<SimpleItem> ScrollToItems = GenerateItems(100);

		static readonly string[] TabTitles = { "Vertical", "Horizontal", "Grid(V)", "Grid(H)", "Grouped", "Selector", "Selection", "Large", "Empty", "Hdr/Ftr", "ScrollTo" };

		readonly Reactive<int> activeTab = 0;
		readonly Reactive<string> selectedItem = "Tap an item";
		readonly Reactive<string> selectionStatus = "Tap items to select";
		readonly Reactive<int> selectionModeIndex = 0;
		readonly Reactive<int> emptyItemCount = 0;
		readonly HashSet<string> _multiSelected = new();

		View TabButton(string title, int index) =>
			Button(title, () => activeTab.Value = index)
				.Background(activeTab.Value == index ? Colors.CornflowerBlue : Colors.LightGray)
				.Color(activeTab.Value == index ? Colors.White : Colors.Black)
				.FontSize(11);

		[Body]
		View body()
		{
			return VStack(spacing: 0f,
				Text("CollectionView")
					.FontSize(22)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(16, 16, 16, 8)),
				ScrollView(Orientation.Horizontal,
					HStack(6,
						TabButton(TabTitles[0], 0),
						TabButton(TabTitles[1], 1),
						TabButton(TabTitles[2], 2),
						TabButton(TabTitles[3], 3),
						TabButton(TabTitles[4], 4),
						TabButton(TabTitles[5], 5),
						TabButton(TabTitles[6], 6),
						TabButton(TabTitles[7], 7),
						TabButton(TabTitles[8], 8),
						TabButton(TabTitles[9], 9),
						TabButton(TabTitles[10], 10)
					)
				).Frame(height: 40).Padding(new Thickness(16, 0, 16, 8)),
				activeTab.Value switch
				{
					0 => VerticalListContent(),
					1 => HorizontalListContent(),
					2 => GridContent(),
					3 => GridHContent(),
					4 => GroupedContent(),
					5 => SelectorContent(),
					6 => SelectionContent(),
					7 => LargeListContent(),
					8 => EmptyViewContent(),
					9 => HeaderFooterContent(),
					10 => ScrollToContent(),
					_ => VerticalListContent()
				}
			);
		}

		View VerticalListContent() =>
			VStack(8,
				Text(() => selectedItem.Value)
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 8, 16, 0)),
				new CollectionView<SimpleItem>(() => VerticalItems)
				{
					ViewFor = item =>
						Border(
							HStack(spacing: 12,
								new ShapeView(new RoundedRectangle(2))
									.Background(new SolidPaint(item.AccentColor))
									.Frame(width: 4),
								VStack(2,
									Text(item.Name)
										.FontSize(15)
										.FontWeight(FontWeight.Bold),
									Text(item.Description)
										.FontSize(12)
										.Color(Colors.Gray)
								)
								.Padding(new Thickness(0, 8, 12, 8))
							)
						)
						.StrokeColor(Colors.Gray.WithAlpha(0.3f))
						.StrokeThickness(1)
						.CornerRadius(8),
					ItemsLayout = ItemsLayout.Vertical(spacing: 8),
					SelectionMode = SelectionMode.Single,
				}
				.OnSelected(item =>
					selectedItem.Value = $"Selected: {item.Name}")
				.Padding(new Thickness(16, 0))
			);

		View HorizontalListContent() =>
			VStack(8,
				Text("Scroll horizontally to see more items")
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 8, 16, 0)),
				new CollectionView<SimpleItem>(() => HorizontalItems)
				{
					ViewFor = item =>
						VStack(6,
							new ShapeView(new Circle())
								.Frame(width: 60, height: 60)
								.Background(new SolidPaint(item.AccentColor)),
							Text(item.Name)
								.FontSize(13)
								.FontWeight(FontWeight.Bold)
						)
						.Frame(width: 100)
						.Padding(new Thickness(8)),
					ItemsLayout = ItemsLayout.Horizontal(spacing: 8),
				}
				.Frame(height: 120)
				.Padding(new Thickness(16, 0))
			);

		View GridContent() =>
			VStack(8,
				new CollectionView<SimpleItem>(() => GridItems)
				{
					ViewFor = item =>
						Border(
							VStack(6,
								new ShapeView(new RoundedRectangle(0))
									.Background(item.AccentColor)
									.Frame(height: 50),
								Text(item.Name)
									.FontSize(12)
									.FontWeight(FontWeight.Bold)
							)
							.Padding(new Thickness(8))
						)
						.StrokeColor(Colors.Gray.WithAlpha(0.3f))
						.StrokeThickness(1)
						.CornerRadius(8),
					ItemsLayout = GridItemsLayout.Vertical(span: 3, spacing: 8),
				}
				.Padding(new Thickness(16, 0))
			);

		View GridHContent() =>
			VStack(8,
				Text("2-Row Horizontal Grid")
					.FontSize(18)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(16, 8, 16, 0)),
				Text("Scroll horizontally — items fill 2 rows")
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 0)),
				new CollectionView<SimpleItem>(() => GridHItems)
				{
					ViewFor = item =>
						VStack(4,
							new ShapeView(new RoundedRectangle(6))
								.Background(item.AccentColor)
								.Frame(width: 80, height: 40),
							Text(item.Name)
								.FontSize(11)
								.HorizontalTextAlignment(TextAlignment.Center)
						)
						.Frame(width: 100)
						.Padding(new Thickness(4)),
					ItemsLayout = GridItemsLayout.Horizontal(span: 2, spacing: 8),
				}
				.Frame(height: 200)
				.Padding(new Thickness(16, 0))
			);

		View GroupedContent() =>
			VStack(spacing: 0f,
				BuildGroupedSection("Mammals", new[] {
					("Dog", "Loyal companion"),
					("Cat", "Independent feline"),
					("Horse", "Majestic equine"),
					("Dolphin", "Intelligent marine mammal"),
					("Elephant", "Gentle giant"),
				}),
				BuildGroupedSection("Birds", new[] {
					("Eagle", "Bird of prey"),
					("Parrot", "Colorful talker"),
					("Penguin", "Flightless swimmer"),
					("Owl", "Nocturnal hunter"),
				}),
				BuildGroupedSection("Reptiles", new[] {
					("Turtle", "Slow and steady"),
					("Gecko", "Wall climber"),
					("Iguana", "Tropical lizard"),
				}),
				BuildGroupedSection("Fish", new[] {
					("Clownfish", "Reef dweller"),
					("Salmon", "Upstream swimmer"),
					("Shark", "Ocean predator"),
					("Swordfish", "Fast swimmer"),
					("Pufferfish", "Inflatable defense"),
				})
			).Padding(new Thickness(16, 0));

		View SelectorContent()
		{
			var items = new List<MixedItem>
			{
				new("Welcome Banner", "Featured content at the top", "banner", Colors.CornflowerBlue),
				new("Project Alpha", "In development", "card", Colors.MediumSeaGreen),
				new("Bug fix #123", "Resolved", "compact", Colors.Gray),
				new("Bug fix #124", "Resolved", "compact", Colors.Gray),
				new("Bug fix #125", "In progress", "compact", Colors.Orange),
				new("Release 2.0", "Coming soon — new features inside", "banner", Colors.MediumOrchid),
				new("Project Beta", "Planning phase", "card", Colors.Coral),
				new("Project Gamma", "Testing", "card", Colors.Teal),
				new("Task: update docs", "Pending", "compact", Colors.SandyBrown),
				new("Task: review PR", "Pending", "compact", Colors.SandyBrown),
				new("Achievement", "100 commits this month!", "banner", Colors.Goldenrod),
				new("Project Delta", "Released", "card", Colors.SlateBlue),
			};

			return VStack(spacing: 0f,
				Text("DataTemplateSelector")
					.FontSize(18)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(16, 12)),
				Text("Three template types: banner, card, compact")
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 0, 16, 8)),
				new CollectionView<MixedItem>(() => items)
				{
					ViewFor = item => item.ItemType switch
					{
						"banner" => Border(
							VStack(4,
								Text(item.Title)
									.FontSize(18)
									.FontWeight(FontWeight.Bold)
									.Color(Colors.White)
									.HorizontalTextAlignment(TextAlignment.Center),
								Text(item.Subtitle)
									.FontSize(12)
									.Color(Colors.White.WithAlpha(0.8f))
									.HorizontalTextAlignment(TextAlignment.Center)
							).Padding(new Thickness(20, 16))
						)
						.Background(item.Color)
						.CornerRadius(12)
						.StrokeThickness(0)
						.Margin(new Thickness(16, 6)),
						"compact" => HStack(8,
							new ShapeView(new Circle())
								.Frame(width: 8, height: 8)
								.Background(new SolidPaint(item.Color)),
							Text(item.Title)
								.FontSize(13)
						).Padding(new Thickness(16, 4)),
						_ => Border(
							HStack(spacing: 12,
								new ShapeView(new RoundedRectangle(2))
									.Background(new SolidPaint(item.Color))
									.Frame(width: 4),
								VStack(2,
									Text(item.Title)
										.FontSize(15)
										.FontWeight(FontWeight.Bold),
									Text(item.Subtitle)
										.FontSize(12)
										.Color(Colors.Gray)
								)
							).Padding(new Thickness(0, 10, 12, 10))
						)
						.StrokeColor(Colors.Gray.WithAlpha(0.3f))
						.StrokeThickness(1)
						.CornerRadius(8)
						.Margin(new Thickness(16, 4)),
					},
				}
			);
		}

		View SelectionContent()
		{
			var mode = selectionModeIndex.Value;
			var selMode = mode == 0 ? SelectionMode.Single :
				mode == 1 ? SelectionMode.Multiple : SelectionMode.None;

			return VStack(spacing: 0f,
				HStack(8,
					Button(
						mode == 0 ? "Mode: Single" :
						mode == 1 ? "Mode: Multiple" : "Mode: None",
						() =>
						{
							_multiSelected.Clear();
							selectionModeIndex.Value = (selectionModeIndex.Value + 1) % 3;
						}
					).FontSize(13),
					Button("Clear", () =>
					{
						_multiSelected.Clear();
						selectionStatus.Value = "Selection cleared";
					}).FontSize(13)
				).Padding(new Thickness(16, 8)),
				Text(() => selectionStatus.Value)
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 4)),
				new CollectionView<SimpleItem>(() => SelectionItems)
				{
					ViewFor = item =>
						Border(
							HStack(spacing: 12,
								new ShapeView(new RoundedRectangle(2))
									.Background(new SolidPaint(item.AccentColor))
									.Frame(width: 4),
								VStack(2,
									Text(item.Name)
										.FontSize(15)
										.FontWeight(FontWeight.Bold),
									Text(item.Description)
										.FontSize(12)
										.Color(Colors.Gray)
								)
								.Padding(new Thickness(0, 8, 12, 8))
							)
						)
						.StrokeColor(Colors.Gray.WithAlpha(0.3f))
						.StrokeThickness(1)
						.CornerRadius(8)
						.Margin(new Thickness(16, 4)),
					ItemsLayout = ItemsLayout.Vertical(spacing: 0),
					SelectionMode = selMode,
				}
				.OnSelected(item =>
				{
					if (selMode == SelectionMode.Multiple)
					{
						if (!_multiSelected.Remove(item.Name))
							_multiSelected.Add(item.Name);
						selectionStatus.Value = _multiSelected.Count > 0
							? $"Selected ({_multiSelected.Count}): {string.Join(", ", _multiSelected)}"
							: "No items selected";
					}
					else
					{
						selectionStatus.Value = $"Selected: {item.Name}";
					}
				})
			);
		}

		View LargeListContent()
		{
			var items = Enumerable.Range(1, 10000).Select(i => $"Item {i:N0}").ToList();
			return VStack(spacing: 0f,
				Text("10,000 items — virtualized")
					.FontSize(12)
					.Color(Colors.Gray)
					.Padding(new Thickness(16, 8)),
				Text("Virtualized — only visible items are rendered")
					.FontSize(11)
					.Color(Colors.MediumSeaGreen)
					.Padding(new Thickness(16, 0, 16, 8)),
				new CollectionView<string>(() => items)
				{
					ViewFor = item =>
						Text(item)
							.FontSize(14)
							.Padding(new Thickness(16, 8)),
				}
			);
		}

		View EmptyViewContent()
		{
			var items = Enumerable.Range(1, emptyItemCount.Value)
				.Select(i => new SimpleItem($"Item {i}", $"Added item {i}", Colors.Blue))
				.ToList();

			return VStack(spacing: 0f,
				HStack(8,
					Button("Add 5 Items", () => emptyItemCount.Value += 5)
						.FontSize(13),
					Button("Clear All", () => emptyItemCount.Value = 0)
						.FontSize(13)
				).Padding(new Thickness(16, 8)),
				new CollectionView<SimpleItem>(() => items)
				{
					ViewFor = item =>
						Text(item.Name)
							.FontSize(16)
							.Padding(new Thickness(12, 8)),
					EmptyView = VStack(8,
						Text("(empty)")
							.FontSize(48)
							.HorizontalTextAlignment(TextAlignment.Center),
						Text("No items yet")
							.FontSize(20)
							.FontWeight(FontWeight.Bold)
							.HorizontalTextAlignment(TextAlignment.Center),
						Text("Tap 'Add 5 Items' to populate the list")
							.FontSize(14)
							.Color(Colors.Gray)
							.HorizontalTextAlignment(TextAlignment.Center)
					).VerticalLayoutAlignment(LayoutAlignment.Center)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				}
			);
		}

		View HeaderFooterContent() =>
			new CollectionView<SimpleItem>(() => HeaderFooterItems)
			{
				ViewFor = item =>
					Text(item.Name)
						.FontSize(16)
						.Padding(new Thickness(12, 8)),
				Header = Text("Collection Header — 15 items total")
					.FontSize(14)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(16, 12)),
				Footer = Text("— End of List —")
					.FontSize(14)
					.Color(Colors.Gray)
					.HorizontalTextAlignment(TextAlignment.Center)
					.Padding(new Thickness(16, 12)),
			};

		View ScrollToContent()
		{
			var scrollToCv = new CollectionView<SimpleItem>(() => ScrollToItems)
			{
				ViewFor = item =>
					Text(item.Name)
						.FontSize(14)
						.Padding(new Thickness(12, 6)),
			};
			return VStack(spacing: 0f,
				HStack(8,
					Button("→ First", () => scrollToCv.ScrollTo(0))
						.FontSize(13),
					Button("→ Item 50", () => scrollToCv.ScrollTo(49))
						.FontSize(13),
					Button("→ Last", () => scrollToCv.ScrollTo(ScrollToItems.Count - 1))
						.FontSize(13)
				).Padding(new Thickness(16, 8)),
				scrollToCv
			);
		}

		View BuildGroupedSection(string groupName, (string Name, string Detail)[] items)
		{
			var views = new List<View>
			{
				Text(groupName)
					.FontSize(16)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.CornflowerBlue)
					.Padding(new Thickness(0, 12, 0, 4))
			};

			foreach (var item in items)
			{
				views.Add(
					HStack(12,
						VStack(2,
							Text(item.Name)
								.FontSize(14)
								.FontWeight(FontWeight.Bold),
							Text(item.Detail)
								.FontSize(12)
								.Color(Colors.Gray)
						)
					)
					.Padding(new Thickness(16, 6, 0, 6))
				);
			}

			views.Add(
				new ShapeView(new RoundedRectangle(0))
					.Background(Colors.Grey)
					.Frame(height: 1)
					.Opacity(0.3f)
			);

			return VStack((float?)0, views.ToArray());
		}
	}
}
