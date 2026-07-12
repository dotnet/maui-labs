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
	/// The Filters sheet (ui/home/FilterScreen.kt): a rounded card floated over a scrim —
	/// Sort rows (icon + name + check), Price chips, Category chips. Shown/hidden by
	/// reactive Opacity on the overlay (the Jetchat FAB idiom — a faded-out node
	/// doesn't eat taps); content is a one-row list reloaded on any filter toggle.
	/// </summary>
	public static class JetsnackFilters
	{
		static readonly Signal<string> SortSelection = new(SnackRepo.SortDefault);
		static ListView<object>? _sheetList;
		static bool _hooked;

		public static View Overlay(Signal<bool> open)
		{
			var sheet = new ListView<object>(() => new object[] { 0 })
			{
				ViewFor = _ => SheetContent(open),
			};
			_sheetList = sheet;
			if (!_hooked)
			{
				_hooked = true;
				SortSelection.PropertyChanged += (_, _) => _sheetList?.ReloadData();
				foreach (var f in AllFilters())
					f.Enabled.PropertyChanged += (_, _) => _sheetList?.ReloadData();
			}

			var overlay = new ZStack
			{
				// Scrim — tap closes (FilterScreen dismissOnClickOutside).
				new VStack(spacing: 0f)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill)
					.VerticalLayoutAlignment(LayoutAlignment.Fill)
					.Background(T.Neutral8.WithAlpha(0.45f))
					.OnTap(_ => open.Value = false),
				new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					new VStack(spacing: 0f) { sheet.Frame(height: 470) }
						.Frame(width: 340)
						.Background(T.UiBackground).CornerRadius(16)
						.HorizontalLayoutAlignment(LayoutAlignment.Center),
					new HStack().FlexGrow(1),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Opacity(open.Peek() ? 1.0 : 0.0);

			open.PropertyChanged += (_, _) => overlay.Opacity(open.Peek() ? 1.0 : 0.0);
			return overlay;
		}

		static IEnumerable<Filter> AllFilters()
		{
			foreach (var f in SnackRepo.PriceFilters) yield return f;
			foreach (var f in SnackRepo.CategoryFilters) yield return f;
		}

		static View SheetContent(Signal<bool> open) => new VStack(spacing: 0f)
		{
			// Header: X close | centered "Filters" titleLarge.
			new HStack(spacing: 0f)
			{
				new Icon("close").IconSize(24).Color(T.IconSecondary)
					.Frame(width: 48, height: 48).Padding(new Thickness(12))
					.FlexShrink(0)
					.OnTap(_ => open.Value = false),
				new HStack().FlexGrow(1),
				new Text("Filters").FontSize(18).FontWeight(FontWeight.Medium).Color(T.TextSecondary)
					.VerticalLayoutAlignment(LayoutAlignment.Center),
				new HStack().FlexGrow(1),
				new HStack().Frame(width: 48).FlexShrink(0),
			}.Frame(height: 56),

			SectionHeader("Sort"),
			SortRow("android", "Android's favorite (default)"),
			SortRow("star", "Rating"),
			SortRow("sort_by_alpha", "Alphabetical"),

			SectionHeader("Price"),
			ChipRow(SnackRepo.PriceFilters),

			SectionHeader("Category"),
			ChipRow(SnackRepo.CategoryFilters.Take2()),
			ChipRow(SnackRepo.CategoryFilters.Skip2()),
			new HStack().Frame(height: 16),
		}.Padding(new Thickness(16, 0, 16, 0));

		static View SectionHeader(string title) =>
			new Text(title).FontSize(16).FontWeight(FontWeight.Medium).Color(T.Brand)
				.Padding(new Thickness(8, 16, 8, 8));

		// FilterScreen SortFilters: icon + name + trailing check on the selection.
		static View SortRow(string icon, string name) => new HStack(spacing: 0f)
		{
			new Icon(icon).IconSize(20).Color(T.TextSecondary)
				.Frame(width: 36, height: 36).Padding(new Thickness(8))
				.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
			new Text(name).FontSize(14).Color(T.TextSecondary)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.FlexGrow(1).FlexBasis(0),
			SortSelection.Peek() == name
				? new Icon("check").IconSize(20).Color(T.Brand)
					.Frame(width: 36, height: 36).Padding(new Thickness(8))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0)
				: new HStack().Frame(width: 36).FlexShrink(0),
		}
		.Frame(height: 44).Padding(new Thickness(8, 0, 8, 0))
		.OnTap(_ => SortSelection.Value = name);

		static View ChipRow(IReadOnlyList<Filter> filters)
		{
			var row = new HStack(spacing: 8f);
			foreach (var filter in filters)
				row.Add(JetsnackHome.FilterChip(filter).FlexShrink(0));
			return row.Frame(height: 44).Padding(new Thickness(8, 4, 8, 4));
		}
	}

	static class FilterListSlices
	{
		public static IReadOnlyList<Filter> Take2(this IReadOnlyList<Filter> list) =>
			new[] { list[0], list[1] };
		public static IReadOnlyList<Filter> Skip2(this IReadOnlyList<Filter> list) =>
			new[] { list[2], list[3] };
	}
}
