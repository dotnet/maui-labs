using System;
using System.Collections.Generic;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	record GroupedAnimal(string Name, string Detail);

	public class GroupedListsPage : View
	{
		static readonly IReadOnlyList<GroupedAnimal> Mammals = new List<GroupedAnimal>
		{
			new("Dog", "Loyal companion"),
			new("Cat", "Independent feline"),
			new("Horse", "Majestic equine"),
			new("Dolphin", "Intelligent marine mammal"),
			new("Elephant", "Gentle giant"),
		};

		static readonly IReadOnlyList<GroupedAnimal> Birds = new List<GroupedAnimal>
		{
			new("Eagle", "Bird of prey"),
			new("Parrot", "Colorful talker"),
			new("Penguin", "Flightless swimmer"),
			new("Owl", "Nocturnal hunter"),
		};

		static readonly IReadOnlyList<GroupedAnimal> Reptiles = new List<GroupedAnimal>
		{
			new("Turtle", "Slow and steady"),
			new("Gecko", "Wall climber"),
			new("Iguana", "Tropical lizard"),
		};

		static readonly IReadOnlyList<GroupedAnimal> Fish = new List<GroupedAnimal>
		{
			new("Clownfish", "Reef dweller"),
			new("Salmon", "Upstream swimmer"),
			new("Shark", "Ocean predator"),
			new("Swordfish", "Fast swimmer"),
			new("Pufferfish", "Inflatable defense"),
		};

		[Body]
		View body()
		{
			var list = new SectionedListView<GroupedAnimal>();

			list.Add(MakeSection("Mammals", Mammals));
			list.Add(MakeSection("Birds", Birds));
			list.Add(MakeSection("Reptiles", Reptiles));
			list.Add(MakeSection("Fish", Fish));

			return GalleryPageHelpers.Scaffold("Grouped Lists",
				GalleryPageHelpers.SectionHeader("Grouped CollectionView"),
				list.Frame(height: 500)
			);
		}

		static Section<GroupedAnimal> MakeSection(string groupName, IReadOnlyList<GroupedAnimal> items)
		{
			return new Section<GroupedAnimal>(items)
			{
				Header = Text(groupName)
					.FontSize(16)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.CornflowerBlue)
					.Padding(new Thickness(16, 12, 16, 4)),
				Footer = new ShapeView(new Rectangle())
					.Background(Colors.Grey)
					.Frame(height: 1)
					.Opacity(0.3f)
					.Margin(new Thickness(16, 4, 16, 8)),
				ViewFor = item =>
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
					.Padding(new Thickness(32, 6, 16, 6)),
			};
		}
	}
}
