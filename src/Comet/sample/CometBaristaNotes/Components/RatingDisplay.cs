namespace CometBaristaNotes.Components;

/// <summary>
/// Displays the rating panel: average rating (large), total shots,
/// best/worst, and distribution bars.
/// </summary>
public class RatingDisplay : View
{
	readonly RatingAggregate _rating;

	public RatingDisplay(RatingAggregate rating)
	{
		_rating = rating;
	}

	[Body]
	View body()
	{
		var avgText = _rating.RatedShots > 0 ? $"{_rating.AverageRating:F1}" : "\u2014";

		var header = HStack(CoffeeColors.SpacingM,
			VStack(2,
				Text(avgText)
					.Modifier(CoffeeModifiers.RatingAverage),
				Text("Average")
					.Modifier(CoffeeModifiers.RatingLabel)
					.HorizontalTextAlignment(TextAlignment.Center)
			),
			VStack(4,
				MakeStatRow("Total shots", $"{_rating.TotalShots}"),
				MakeStatRow("Best", _rating.BestRating?.ToString() ?? "\u2014"),
				MakeStatRow("Worst", _rating.WorstRating?.ToString() ?? "\u2014")
			).FillHorizontal()
		);

		var content = VStack(CoffeeColors.SpacingS,
			header,
			BuildDistributionBars()
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

	View BuildDistributionBars()
	{
		var maxCount = _rating.Distribution.Values.DefaultIfEmpty(0).Max();
		if (maxCount == 0) maxCount = 1;

		var stack = VStack(4);
		for (int level = 4; level >= 0; level--)
		{
			var count = _rating.Distribution.GetValueOrDefault(level, 0);
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
