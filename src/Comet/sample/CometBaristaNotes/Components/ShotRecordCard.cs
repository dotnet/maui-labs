namespace CometBaristaNotes.Components;

/// <summary>
/// Displays a shot record card matching the original BaristaNotes layout.
/// Card shows: DrinkType + smiley badge, BeanName, Recipe, Timestamp • By • For.
/// </summary>
public class ShotRecordCard : View
{
	readonly ShotRecord _shot;
	readonly Action? _onTap;

	public ShotRecordCard(ShotRecord shot, Action? onTap = null)
	{
		_shot = shot;
		_onTap = onTap;
	}

	[Body]
	View body()
	{
		var beanName = _shot.BeanName ?? _shot.BagDisplayName ?? "Unknown Bean";

		var card = VStack(spacing: 6f,
			HStack(spacing: 4f,
				FormHelpers.MakeIcon(Icons.Coffee, 18, CoffeeColors.Primary),
				Text(_shot.DrinkType)
					.Modifier(CoffeeModifiers.TitleSmall),
				new Spacer(),
				MakeRatingBadge()
			),
			Text(beanName)
				.Modifier(CoffeeModifiers.FormValue),
			Text(FormatRecipeLine())
				.Modifier(CoffeeModifiers.SecondaryText),
			Text(FormatFooterLine())
				.Modifier(CoffeeModifiers.Caption)
		)
		.Modifier(CoffeeModifiers.ShotCard);

		if (_onTap != null)
			card = card.OnTap(_ => _onTap());

		return card;
	}

	View MakeRatingBadge()
	{
		if (!_shot.Rating.HasValue)
			return Text("").Frame(width: 0, height: 0).Opacity(0);

		var glyph = _shot.Rating.Value switch
		{
			0 => Icons.SentimentVeryDissatisfied,
			1 => Icons.SentimentDissatisfied,
			2 => Icons.SentimentNeutral,
			3 => Icons.SentimentSatisfied,
			4 => Icons.SentimentVerySatisfied,
			_ => Icons.SentimentNeutral,
		};

		return FormHelpers.MakeIcon(glyph, 24, CoffeeColors.Primary);
	}

	string FormatRecipeLine()
	{
		var doseIn = $"{_shot.DoseIn:F1}g in";
		var doseOut = _shot.ActualOutput.HasValue ? $"{_shot.ActualOutput:F1}g out" : "\u2014";
		var time = _shot.ActualTime.HasValue ? $"({_shot.ActualTime:F1}s)" : "";
		return $"{doseIn} \u2192 {doseOut} {time}".Trim();
	}

	string FormatFooterLine()
	{
		var parts = new List<string> { FormatTimestamp() };
		if (_shot.MadeByName != null)
			parts.Add($"By: {_shot.MadeByName}");
		if (_shot.MadeForName != null)
			parts.Add($"For: {_shot.MadeForName}");
		return string.Join(" \u2022 ", parts);
	}

	string FormatTimestamp()
	{
		var diff = DateTime.Now - _shot.Timestamp;
		if (diff.TotalMinutes < 1) return "Just now";
		if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
		if (diff.TotalHours < 24) return _shot.Timestamp.ToString("h:mm tt");
		if (diff.TotalDays < 7) return _shot.Timestamp.ToString("ddd h:mm tt");
		return _shot.Timestamp.ToString("MMM d, h:mm tt");
	}
}
