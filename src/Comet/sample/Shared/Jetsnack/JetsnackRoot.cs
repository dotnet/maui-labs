#nullable enable
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetsnack.JetsnackTheme;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// Jetsnack chrome + routing (ui/home/Home.kt HomeSections + JetsnackBottomBar).
	/// The gold's bottom bar is its OWN composable (brand background, selected item =
	/// icon + UPPERCASE label, others icon-only inactive) — hand-composed here from the
	/// same structure. Routes swap through ContentSwitcher (retained-node contract).
	/// </summary>
	public class JetsnackRoot : View
	{
		/// <summary>0 Feed, 1 Search, 2 Cart, 3 Profile (HomeSections order).</summary>
		public static readonly Signal<int> SelectedTab = new(0);
		public static readonly Signal<long> CurrentSnack = new(1);
		public static readonly Signal<bool> DetailOpen = new(false);
		public static readonly Signal<bool> FiltersOpen = new(false);

		static readonly Signal<int> DetailContent = new(0);

		static JetsnackRoot()
		{
			DetailOpen.PropertyChanged += (_, _) => DetailContent.Value = DetailOpen.Peek() ? 1 : 0;
			SelectedTab.PropertyChanged += (_, _) => DetailOpen.Value = false;
		}

		public static void OpenSnack(long id)
		{
			CurrentSnack.Value = id;
			DetailOpen.Value = true;
		}

		readonly double _topInset;

		public JetsnackRoot(double topInset = 0) => _topInset = topInset;

		[Body]
		View body()
		{
			JetsnackIcons.Register();

			// Feed with the DestinationBar overlaid at the top (the gold's Box overlay —
			// the bar floats over the scrolling feed).
			var feed = new ZStack
			{
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)(_topInset + 56)).FlexShrink(0),
					JetsnackHome.Feed(openFilters: () => FiltersOpen.Value = true)
						.FlexGrow(1).FlexBasis(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)_topInset).Background(T.UiBackground).FlexShrink(0),
					JetsnackHome.DestinationBar().HorizontalLayoutAlignment(LayoutAlignment.Fill).FlexShrink(0),
					new HStack().FlexGrow(1),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill);

			// Compact push: DetailOpen swaps feed ↔ detail full-screen (system back closes) —
			// the ListDetail primitive, same as JetNews.
			var feedWithDetail = new ListDetail(DetailOpen, feed,
				JetsnackDetail.Screen(CurrentSnack, _topInset, onBack: () => DetailOpen.Value = false));

			var routes = new ContentSwitcher(SelectedTab, new View[]
			{
				feedWithDetail,
				Placeholder("Search lands in the next increment"),
				Placeholder("Cart lands in the next increment"),
				// The gold Profile IS a work-in-progress placeholder.
				Placeholder("This is currently work in progress"),
			});

			return new ZStack
			{
				new VStack(spacing: 0f)
				{
					routes.FlexGrow(1).FlexBasis(0),
					BottomBar().FlexShrink(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				// Filters sheet floats over everything; reactive-opacity show/hide.
				JetsnackFilters.Overlay(FiltersOpen),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.UiBackground);
		}

		static readonly (string Icon, string Label)[] Tabs =
		{
			("home", "HOME"), ("search", "SEARCH"), ("shopping_cart", "MY CART"), ("account_circle", "PROFILE"),
		};

		/// <summary>JetsnackBottomBar: brand background; SELECTED item = bordered pill with
		/// icon + uppercase label (iconInteractive), others icon-only (iconInteractiveInactive).</summary>
		View BottomBar()
		{
			var row = new HStack(spacing: 0f);
			for (int i = 0; i < Tabs.Length; i++)
			{
				int index = i;
				bool selected = SelectedTab.Peek() == index;
				var cell = new HStack(spacing: 0f) { new HStack().FlexGrow(1) };
				var content = new HStack(spacing: 8f)
				{
					new Icon(Tabs[i].Icon).IconSize(24)
						.Color(selected ? T.IconInteractive : T.IconInteractiveInactive)
						.VerticalLayoutAlignment(LayoutAlignment.Center),
				};
				if (selected)
					content.Add(new Text(Tabs[i].Label).FontSize(14).FontWeight(FontWeight.Medium)
						.Color(T.IconInteractive)
						.VerticalLayoutAlignment(LayoutAlignment.Center));
				var pill = new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					content.HorizontalLayoutAlignment(LayoutAlignment.Center),
					new HStack().FlexGrow(1),
				}
				.Frame(height: 40)
				.Padding(new Thickness(16, 0, 16, 0));
				if (selected)
					pill = (VStack)pill.Border(1, T.IconInteractive).CornerRadius(20);
				cell.Add(pill.VerticalLayoutAlignment(LayoutAlignment.Center));
				cell.Add(new HStack().FlexGrow(1));
				row.Add(cell.FlexGrow(selected ? 1.6f : 1f).FlexBasis(0)
					.OnTap(_ => SelectedTab.Value = index));
			}
			return new VStack(spacing: 0f)
			{
				row.Frame(height: 56),
				new HStack().Frame(height: 24),   // gesture-nav inset strip
			}.Background(T.Brand);
		}

		static View Placeholder(string message) => new VStack(spacing: 0f)
		{
			new HStack().FlexGrow(1),
			new Text(message).FontSize(16).FontWeight(FontWeight.Medium).Color(T.TextSecondary)
				.HorizontalLayoutAlignment(LayoutAlignment.Center),
			new Text("Grab a beverage and check back later!").FontSize(14).Color(T.TextHelp)
				.HorizontalLayoutAlignment(LayoutAlignment.Center).Padding(new Thickness(0, 8, 0, 0)),
			new HStack().FlexGrow(1),
		}
		.HorizontalLayoutAlignment(LayoutAlignment.Fill)
		.VerticalLayoutAlignment(LayoutAlignment.Fill)
		.Background(T.UiBackground);
	}
}
