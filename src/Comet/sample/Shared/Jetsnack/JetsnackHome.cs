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
				new Text("Delivery to 1600 Amphitheater Way").FontSize(16).FontWeight(FontWeight.Medium)
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

		// FilterBar.kt: filter-list icon in a bordered circle + the filter chips, 8dp gaps,
		// horizontal list padded to the section inset.
		static View FilterBar(System.Action openFilters)
		{
			var row = new HStack(spacing: 8f)
			{
				new Icon("filter_list").IconSize(20).Color(T.Brand)
					.Frame(width: 46, height: 46).Padding(new Thickness(13))
					.Border(1, T.UiBorder).CornerRadius(23)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexShrink(0)
					.OnTap(_ => openFilters()),
			};
			foreach (var filter in SnackRepo.Filters)
				row.Add(FilterChip(filter).VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0));
			return row.Frame(height: 66).Padding(new Thickness(12, 4, 12, 4));
		}

		/// <summary>FilterChip.kt: pill (50% corner) with the diagonal-gradient border when
		/// off / gradient fill when on; text brand ↔ textInteractive.</summary>
		public static View FilterChip(Filter filter)
		{
			bool on = filter.Enabled.Peek();
			var chip = new HStack(spacing: 0f)
			{
				new Text(filter.Name).FontSize(14).FontWeight(FontWeight.Medium)
					.Color(on ? T.TextInteractive : T.TextSecondary)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
			}
			.Frame(height: 28)
			.Padding(new Thickness(20, 6, 20, 6))
			.CornerRadius(14)
			.OnTap(_ => filter.Enabled.Value = !filter.Enabled.Peek());
			return on
				? chip.BackgroundGradient(T.Tornado1)
				: chip.Border(1, T.Brand.WithAlpha(0.4f)).Background(T.UiBackground);
		}

		// ── Collection section (Snacks.kt SnackCollection): header (name titleLarge BRAND,
		// min 56, start 24, arrow at end) + Highlight card row or plain circle row + divider. ──
		/// <summary>A collection section for reuse outside the feed (the detail's related rows).</summary>
		public static View RelatedCollection(SnackCollection collection) => CollectionSection(collection);

		static View CollectionSection(SnackCollection collection) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				new Text(collection.Name).FontSize(22).Color(T.Brand)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexGrow(1).FlexBasis(0),
				new Icon("arrow_forward").IconSize(24).Color(T.Brand)
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
			int index = 0;
			var row = new ListView<Snack>(() => collection.Snacks)
			{
				Horizontal = true,
				ViewFor = snack => HighlightSnackItem(snack, index++),
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
			new Text(snack.Name).FontSize(22).Color(T.TextSecondary).MaxLines(1)
				.Padding(new Thickness(16, 0, 16, 0)),
			new HStack().Frame(height: 4),
			new Text(snack.Tagline).FontSize(16).Color(T.TextHelp).MaxLines(1)
				.Padding(new Thickness(16, 0, 16, 0)),
		}
		.Frame(width: HighlightCardWidth, height: 250)
		.Background(T.UiBackground).CornerRadius(20)
		.Border(1, T.UiBorder)
		.Margin(new Thickness(0, 0, 16, 16))
		.OnTap(_ => OpenSnack(snack.Id));

		/// <summary>SnackItem (Normal rows): 120 circle image + name titleMedium under it.</summary>
		static View SnackItem(Snack snack) => new VStack(spacing: 0f)
		{
			new Image(snack.ImageRes).Frame(width: 120, height: 120).CornerRadius(60),
			new Text(snack.Name).FontSize(16).FontWeight(FontWeight.Medium).Color(T.TextSecondary)
				.Padding(new Thickness(0, 8, 0, 0))
				.HorizontalLayoutAlignment(LayoutAlignment.Center),
		}
		.Padding(new Thickness(4, 0, 4, 8))
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
