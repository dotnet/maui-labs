#nullable enable
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetsnack.JetsnackTheme;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// Search (ui/home/search): the uiFloated search bar over the Categories/Lifestyles
	/// two-column grids — each category a 10dp-corner gradient card (name titleMedium on
	/// the left, image on the right; gradients alternate 2_2/2_3 per collection index).
	/// Query results are the next increment (the gold's suggestion/result states).
	/// </summary>
	public static class JetsnackSearch
	{
		static readonly Signal<string> Query = new(string.Empty);

		public static View Screen(double topInset)
		{
			var rows = new List<object> { "bar" };
			foreach (var collection in SearchRepo.GetCategories())
				rows.Add(collection);

			var list = new ListView<object>(() => rows)
			{
				ViewFor = r => r switch
				{
					string => SearchBar(),
					SearchCategoryCollection c => CollectionGrid(c),
					_ => new HStack(),
				},
			};

			return new VStack(spacing: 0f)
			{
				new HStack().Frame(height: (float)topInset).FlexShrink(0),
				list.FlexGrow(1).FlexBasis(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.UiBackground);
		}

		// Search.kt SearchBar: 56dp uiFloated rounded surface, search icon + hint.
		static View SearchBar() => new VStack(spacing: 0f)
		{
			// Wrapper padding, not margin: Fill + margin overflows the right edge
			// (the fill width isn't reduced by the margin).
			new HStack(spacing: 0f)
			{
				new Icon("search").IconSize(24).Color(T.TextHelp)
					.Frame(width: 48, height: 40).Padding(new Thickness(12, 8, 12, 8))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
				SignalExtensions.TextField(Query, "Search Jetsnack")
					.Borderless()
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexGrow(1).FlexBasis(0),
			}
			.Frame(height: 40)
			.Background(T.UiFloated).CornerRadius(20)
			.HorizontalLayoutAlignment(LayoutAlignment.Fill),
		}
		.Padding(new Thickness(24, 8, 24, 8))
		.HorizontalLayoutAlignment(LayoutAlignment.Fill);

		static View CollectionGrid(SearchCategoryCollection collection)
		{
			var grid = new VStack(spacing: 0f)
			{
				T.TitleLarge(collection.Name).Color(T.Brand)
					.Padding(new Thickness(24, 16, 24, 4)),
			};
			// VerticalGrid: two columns, pad 16, item pad 8.
			for (int i = 0; i < collection.Categories.Count; i += 2)
			{
				var row = new HStack(spacing: 0f)
				{
					CategoryCard(collection.Categories[i], (int)collection.Id)
						.FlexGrow(1).FlexBasis(0),
				};
				if (i + 1 < collection.Categories.Count)
					row.Add(CategoryCard(collection.Categories[i + 1], (int)collection.Id)
						.FlexGrow(1).FlexBasis(0));
				else
					row.Add(new HStack().FlexGrow(1).FlexBasis(0));
				grid.Add(row.Padding(new Thickness(16, 0, 16, 0))
					.HorizontalLayoutAlignment(LayoutAlignment.Fill));
			}
			grid.Add(new HStack().Frame(height: 4));
			return grid;
		}

		// Categories.kt SearchCategory: r10 gradient card (2_2 for Categories, 2_3 for
		// Lifestyles), name titleMedium left, circular image right, min image 134.
		static View CategoryCard(SearchCategory category, int collectionIndex) => new HStack(spacing: 0f)
		{
			T.TitleMedium(category.Name).Color(T.TextSecondary)
				.LineBreakMode(LineBreakMode.WordWrap)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.Padding(new Thickness(12, 4, 4, 4))
				.FlexGrow(1).FlexBasis(0),
			new Image(category.ImageRes).Frame(width: 120, height: 120).CornerRadius(60)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.FlexShrink(0),
		}
		.Frame(height: 134)
		.BackgroundGradient(collectionIndex % 2 == 0 ? T.Gradient2_2 : T.Gradient2_3)
		.CornerRadius(10)
		.Margin(new Thickness(8))
		.OnTap(_ => { /* the gold's category filter route is toast-stubbed too */ });
	}
}
