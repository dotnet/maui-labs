#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetsnack.JetsnackTheme;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// Jetsnack Feed (ui/home/Feed.kt + ui/components/Snacks.kt), values-from-source.
	/// Jetsnack is a CUSTOM design system — the gold hand-composes its chrome and cards
	/// from Rows/Columns/Surfaces, so composing the SAME structure from Comet primitives
	/// (with the new BackgroundGradient fills) IS the faithful reproduction.
	/// </summary>
	public static class JetsnackHome
	{
		// Snacks.kt: HighlightCardWidth = 170, height 250, gradient band 160/100, image 120.
		const float HighlightCardWidth = 170f;

		public static void OpenSnack(long id) => JetsnackRoot.OpenSnack(id);

		// ── Feed rows: spacer(status+56 for the overlaid DestinationBar) is handled by the
		// root; list = FilterBar + per-collection section + divider (Feed.kt SnackCollectionList). ──
		abstract record FeedRow;
		sealed record FilterBarRow : FeedRow;
		sealed record CollectionRow(SnackCollection Collection) : FeedRow;

		static IReadOnlyList<FeedRow> FeedRows()
		{
			var rows = new List<FeedRow> { new FilterBarRow() };
			rows.AddRange(SnackRepo.GetSnacks().Select(c => (FeedRow)new CollectionRow(c)));
			return rows;
		}

		public static View Feed(System.Action openFilters)
		{
			var list = new ListView<FeedRow>(FeedRows)
			{
				ViewFor = r => r switch
				{
					FilterBarRow => FilterBar(openFilters),
					CollectionRow c => CollectionSection(c.Collection),
					_ => new HStack(),
				},
			};
			return list;
		}

		/// <summary>DestinationBar.kt: 56dp TopAppBar — "Delivery to…" titleMedium
		/// textSecondary centered + expand_more tinted brand; hairline divider below.</summary>
		public static View DestinationBar() => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				new HStack().Frame(width: 48).FlexShrink(0),   // balances the action slot
				T.TitleMedium("Delivery to 1600 Amphitheater Way")
					.Color(T.TextSecondary).MaxLines(1)
					.HorizontalLayoutAlignment(LayoutAlignment.Center)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexGrow(1).FlexBasis(0),
				new Icon("expand_more").IconSize(24).Color(T.Brand)
					.Frame(width: 48, height: 56).Padding(new Thickness(12, 16, 12, 16))
					.FlexShrink(0),
			}.Frame(height: 56),
			Divider(1),
		}.Background(T.UiBackground);

		// FilterBar.kt: a REAL LazyRow (spacing 8, contentPadding start 12) — the gold's
		// bar scrolls horizontally; the filter circle is the first item.
		abstract record BarItem;
		sealed record FilterCircle : BarItem;
		sealed record ChipItem(Filter Filter) : BarItem;

		static ListView<BarItem>? _filterBar;
		static bool _filterBarHooked;

		static View FilterBar(System.Action openFilters)
		{
			var items = new List<BarItem> { new FilterCircle() };
			items.AddRange(SnackRepo.Filters.Select(f => (BarItem)new ChipItem(f)));
			var row = new ListView<BarItem>(() => items)
			{
				Horizontal = true,
				ViewFor = item => new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					item switch
					{
						FilterCircle => (View)new Icon("filter_list").IconSize(20).Color(T.Brand)
							.Frame(width: 46, height: 46).Padding(new Thickness(13))
							.BorderGradient(T.Gradient2_2).CornerRadius(23)
							.OnTap(_ => openFilters()),
						ChipItem c => FilterChip(c.Filter),
						_ => new HStack(),
					},
					new HStack().FlexGrow(1),
				}.Frame(height: 66).Padding(new Thickness(0, 0, 8, 0)),
			};
			_filterBar = row;
			if (!_filterBarHooked)
			{
				// Subscribe ONCE (static filters outlive every rebuild); reload the
				// current bar instance so the chip ON/OFF visuals follow the signal.
				_filterBarHooked = true;
				foreach (var f in SnackRepo.Filters)
					f.Enabled.PropertyChanged += (_, _) => _filterBar?.ReloadData();
			}
			return row.Frame(height: 66).Margin(left: 12)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		/// <summary>FilterChip.kt: shapes.small pill — OFF = uiBackground with the
		/// interactiveSecondary DIAGONAL GRADIENT border, bodySmall textSecondary;
		/// ON = brandSecondary fill, black text, no border.</summary>
		public static View FilterChip(Filter filter)
		{
			bool on = filter.Enabled.Peek();
			var chip = new HStack(spacing: 0f)
			{
				T.BodySmall(filter.Name)
					.Color(on ? Colors.Black : T.TextSecondary)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
			}
			.Frame(height: 28)
			.Padding(new Thickness(20, 6, 20, 6))
			.CornerRadius(14)
			.OnTap(_ => filter.Enabled.Value = !filter.Enabled.Peek());
			return on
				? chip.Background(T.BrandSecondary)
				: chip.BorderGradient(T.Gradient2_2).Background(T.UiBackground);
		}

		// ── Collection section (Snacks.kt SnackCollection): header (name titleLarge BRAND,
		// min 56, start 24, arrow at end) + Highlight card row or plain circle row + divider. ──
		/// <summary>A collection section for reuse outside the feed (the detail's related rows).</summary>
		public static View RelatedCollection(SnackCollection collection) => CollectionSection(collection);

		static View CollectionSection(SnackCollection collection) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				T.TitleLarge(collection.Name).Color(T.Brand)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexGrow(1).FlexBasis(0),
				new Icon("arrow_back").IconSize(24).Color(T.Brand)
					.Frame(width: 48, height: 48).Padding(new Thickness(12))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
			}.Frame(height: 56).Padding(new Thickness(24, 0, 4, 0)),
			collection.Type == CollectionType.Highlight
				? HighlightRow(collection)
				: SnackRow(collection),
			Divider(2).Margin(new Thickness(0, 16, 0, 0)),
		};

		static View HighlightRow(SnackCollection collection)
		{
			// Index derived from the item, NOT a captured counter — ViewFor re-runs on
			// every rebuild/reload and a counter would shift the gradient alternation.
			var row = new ListView<Snack>(() => collection.Snacks)
			{
				Horizontal = true,
				// Wrapper padding, not item margin: the LazyRow item box is the row root's
				// measured width, which excludes margins — cards rendered flush without it.
				ViewFor = snack => new VStack(spacing: 0f)
				{
					HighlightSnackItem(snack, IndexOf(collection.Snacks, snack)),
				}.Padding(new Thickness(0, 0, 16, 0)),
			};
			return row.Frame(height: 266).Margin(left: 24)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		/// <summary>HighlightSnackItem: 170×250 r20 card — gradient band 160 (alternating
		/// gradient6_1/6_2 like the gold's index % 2), 120 circle image overlapping the band,
		/// name titleLarge, tagline bodyLarge textHelp.</summary>
		static View HighlightSnackItem(Snack snack, int index) => new VStack(spacing: 0f)
		{
			new ZStack
			{
				// Gradient band pinned to the card TOP (Snacks.kt Box: gradient 100 at top,
				// image bottom-center overlapping it).
				new VStack(spacing: 0f) { new HStack().Frame(height: 100) }
					.Frame(width: HighlightCardWidth, height: 100)
					.BackgroundGradient(index % 2 == 0 ? T.Gradient6_1 : T.Gradient6_2)
					.VerticalLayoutAlignment(LayoutAlignment.Start),
				new Image(snack.ImageRes).Frame(width: 120, height: 120).CornerRadius(60)
					.Margin(new Thickness(25, 40, 25, 0)),
			}.Frame(width: HighlightCardWidth, height: 160),
			new HStack().Frame(height: 8),
			T.TitleLarge(snack.Name).Color(T.TextSecondary).MaxLines(1)
				.Padding(new Thickness(16, 0, 16, 0)),
			new HStack().Frame(height: 4),
			T.BodyLarge(snack.Tagline).Color(T.TextHelp).MaxLines(1)
				.Padding(new Thickness(16, 0, 16, 0)),
		}
		.Frame(width: HighlightCardWidth, height: 250)
		.Background(T.UiBackground).CornerRadius(20)
		.Border(1, T.UiBorder)
		.OnTap(_ => OpenSnack(snack.Id));

		static int IndexOf(System.Collections.Generic.IReadOnlyList<Snack> snacks, Snack snack)
		{
			for (int i = 0; i < snacks.Count; i++)
				if (ReferenceEquals(snacks[i], snack))
					return i;
			return 0;
		}

		/// <summary>SnackItem (Normal rows): 120 circle image + name titleMedium under it.</summary>
		static View SnackItem(Snack snack) => new VStack(spacing: 0f)
		{
			new Image(snack.ImageRes).Frame(width: 120, height: 120).CornerRadius(60),
			T.TitleMedium(snack.Name).Color(T.TextSecondary)
				.Padding(new Thickness(0, 8, 0, 0))
				.HorizontalLayoutAlignment(LayoutAlignment.Center),
		}
		.Padding(new Thickness(8, 0, 8, 8))
		.OnTap(_ => OpenSnack(snack.Id));

		static View SnackRow(SnackCollection collection)
		{
			var row = new ListView<Snack>(() => collection.Snacks)
			{
				Horizontal = true,
				ViewFor = SnackItem,
			};
			return row.Frame(height: 160).Margin(left: 12)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		/// <summary>JetsnackDivider: hairline uiBorder.</summary>
		public static View Divider(float thickness) => new HStack()
			.Frame(height: thickness)
			.Background(T.UiBorder)
			.HorizontalLayoutAlignment(LayoutAlignment.Fill);
	}
}
