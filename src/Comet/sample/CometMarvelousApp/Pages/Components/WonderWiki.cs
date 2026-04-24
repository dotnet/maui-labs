using CometMarvelousApp.Models;

namespace CometMarvelousApp.Pages.Components;

/// <summary>
/// Editorial/wiki view for a selected wonder. Shows title, subtitle, region,
/// date range, and a photo, all over a gradient background matching the wonder's
/// color theme. Ported from MauiReactor's ScrollView/Canvas-based wiki page.
/// </summary>
public class WonderWiki : View
{
	readonly WonderType _wonderType;

	public WonderWiki(WonderType wonderType)
	{
		_wonderType = wonderType;
	}

	[Body]
	View body()
	{
		var wikiConfig = Wonder.Config.ContainsKey(_wonderType)
			? Wonder.Config[_wonderType]
			: null;
		var wonderConfig = Illustration.Config[_wonderType];

		return new Comet.Grid
		{
			// Background
			RenderBackground(wonderConfig),

			// Scrollable content overlay
			ScrollView(Orientation.Vertical,
				VStack(
					// Subtitle bar with dividers
					RenderSubtitleBar(wikiConfig, wonderConfig),

					// Wonder title
					Text(wonderConfig.Title)
						.FontFamily("YesevaOne")
						.FontSize(56)
						.Color(Colors.White)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Margin(new Thickness(16, 8)),

					// Region
					wikiConfig != null
						? Text(wikiConfig.RegionTitle.ToUpper())
							.FontFamily("TenorSans")
							.FontSize(16)
							.Color(Colors.White)
							.HorizontalTextAlignment(TextAlignment.Center)
							.Margin(new Thickness(16, 4))
						: (View)new Spacer(),

					// Compass separator
					RenderSeparator(wonderConfig),

					// Date range
					wikiConfig != null
						? Text($"{wikiConfig.StartYr} {(wikiConfig.StartYr < 0 ? "BCE" : "CE")} to {wikiConfig.EndYr} {(wikiConfig.EndYr < 0 ? "BCE" : "CE")}")
							.FontFamily("RalewayBold")
							.Color(Colors.White)
							.HorizontalTextAlignment(TextAlignment.Center)
							.Margin(new Thickness(16, 16))
						: (View)new Spacer(),

					// Photo
					Image(wonderConfig.Photo1)
						.Aspect(Aspect.AspectFill)
						.Margin(new Thickness(16, 16)),

					// History info
					wikiConfig != null
						? VStack(
							Text(wikiConfig.HistoryInfo1)
								.FontFamily("RalewayRegular")
								.FontSize(14)
								.Color(Colors.White)
								.Margin(new Thickness(16, 8)),
							Text(wikiConfig.HistoryInfo2)
								.FontFamily("RalewayRegular")
								.FontSize(14)
								.Color(Colors.White)
								.Margin(new Thickness(16, 8))
						)
						: (View)new Spacer(),

					// Bottom spacer for navigator
					new Spacer().Frame(height: 100)
				)
			)
			.Padding(new Thickness(0, 280, 0, 0)),
		};
	}

	View RenderBackground(Illustration config)
	{
		return new Comet.Grid
		{
			// Top section with secondary color
			new BoxView(config.SecondaryColor)
				.Frame(height: 260)
				.Alignment(Alignment.Top),

			// Editorial illustration
			Image(config.MainObjectEditorialImage.Source)
				.Aspect(Aspect.AspectFill)
				.Frame(height: 260)
				.Alignment(Alignment.Top)
				.Opacity(0.6),

			// Bottom fill
			new BoxView(config.PrimaryColor)
				.Margin(new Thickness(0, 260, 0, 0))
				.FillHorizontal()
				.FillVertical(),
		}
		.Background(config.PrimaryColor);
	}

	View RenderSubtitleBar(Wonder? wikiConfig, Illustration wonderConfig)
	{
		if (wikiConfig == null) return Spacer();

		return HStack(
			new BoxView(wonderConfig.SecondaryColor)
				.Frame(height: 1)
				.FillHorizontal()
				.Margin(new Thickness(20, 0)),

			Text(wikiConfig.SubTitle.ToUpper())
				.FontFamily("TenorSans")
				.FontSize(14)
				.Color(Colors.White)
				.HorizontalTextAlignment(TextAlignment.Center),

			new BoxView(wonderConfig.SecondaryColor)
				.Frame(height: 1)
				.FillHorizontal()
				.Margin(new Thickness(20, 0))
		)
		.Frame(height: 52)
		.Alignment(Alignment.Center);
	}

	View RenderSeparator(Illustration wonderConfig)
	{
		return HStack(
			new BoxView(wonderConfig.SecondaryColor)
				.Frame(height: 2)
				.FillHorizontal()
				.Margin(new Thickness(20, 0)),

			Image("common_compass_full.png")
				.Frame(width: 42, height: 42),

			new BoxView(wonderConfig.SecondaryColor)
				.Frame(height: 2)
				.FillHorizontal()
				.Margin(new Thickness(20, 0))
		)
		.Frame(height: 42)
		.Alignment(Alignment.Center);
	}
}
