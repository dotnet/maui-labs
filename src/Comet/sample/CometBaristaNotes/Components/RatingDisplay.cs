using Comet;
using Comet.Styles;
using CometBaristaNotes.Models;
using CometBaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometBaristaNotes.Components;

/// <summary>
/// Factory for creating the rating display panel.
/// Shows average rating (large), total shots, best/worst, and distribution bars.
/// </summary>
public static class RatingDisplayFactory
{
	public static View Create(RatingAggregate rating)
	{
		var avgText = rating.RatedShots > 0 ? $"{rating.AverageRating:F1}" : "—";

		var header = HStack(CoffeeColors.SpacingM,
			// Large average rating number
			VStack(2,
				Text(avgText)
					.Modifier(CoffeeModifiers.RatingAverage),
				Text("Average")
					.Modifier(CoffeeModifiers.RatingLabel)
					.HorizontalTextAlignment(TextAlignment.Center)
			),
			// Stats column
			VStack(4,
				MakeStatRow("Total shots", $"{rating.TotalShots}"),
				MakeStatRow("Best", rating.BestRating?.ToString() ?? "—"),
				MakeStatRow("Worst", rating.WorstRating?.ToString() ?? "—")
			).FillHorizontal()
		);

		var bars = BuildDistributionBars(rating);

		var content = VStack(CoffeeColors.SpacingS,
			header,
			bars
		);

		return Border(content)
			.Modifier(CoffeeModifiers.Card);
	}

	static View MakeStatRow(string label, string value)
	{
		return HStack(4,
			Text(label)
				.Modifier(CoffeeModifiers.RatingStatLabel),
			Spacer(),
			Text(value)
				.Modifier(CoffeeModifiers.RatingStatValue)
		);
	}

	static View BuildDistributionBars(RatingAggregate rating)
	{
		var maxCount = rating.Distribution.Values.DefaultIfEmpty(0).Max();
		if (maxCount == 0) maxCount = 1;

		var stack = VStack(4);
		for (int level = 4; level >= 0; level--)
		{
			var count = rating.Distribution.GetValueOrDefault(level, 0);
			var pct = (double)count / maxCount;

			stack.Add(HStack(CoffeeColors.SpacingXS,
				Text($"{level}")
					.Modifier(CoffeeModifiers.RatingLevelLabel)
					.Frame(width: 16)
					.HorizontalTextAlignment(TextAlignment.Center),

				MakeBar(pct).FillHorizontal(),

				Text($"{count}")
					.Modifier(CoffeeModifiers.RatingCountLabel)
					.Frame(width: 24)
					.HorizontalTextAlignment(TextAlignment.End)
			));
		}

		return stack;
	}

	static View MakeBar(double fillFraction)
	{
		var fill = new Comet.BoxView(CoffeeColors.Primary)
			.Modifier(CoffeeModifiers.RatingBar);

		var background = new Comet.BoxView(CoffeeColors.SurfaceVariant)
			.Modifier(CoffeeModifiers.RatingBar);

		// Use a Grid to overlay the fill on the background
		// The fill fraction controls the relative width via column definitions
		var clampedPct = Math.Clamp(fillFraction, 0, 1);
		var fillStar = Math.Max(clampedPct, 0.01);
		var remainStar = 1.0 - clampedPct;

		return Grid(
			columns: new object[] { $"{fillStar}*", $"{remainStar}*" },
			rows: new object[] { "Auto" },
			fill.Cell(row: 0, column: 0),
			background.Cell(row: 0, column: 1)
		);
	}
}
