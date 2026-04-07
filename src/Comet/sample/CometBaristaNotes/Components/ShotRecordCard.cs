using Comet;
using Comet.Styles;
using CometBaristaNotes.Models;
using CometBaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometBaristaNotes.Components;

/// <summary>
/// Factory for creating shot record cards matching the original BaristaNotes layout.
/// Card shows: DrinkType + smiley badge, BeanName, Recipe, Timestamp • By • For.
/// Equipment is hidden (matches original which has it commented out).
/// </summary>
public static class ShotRecordCardFactory
{
	public static View Create(ShotRecord shot, Action? onTap = null)
	{
		var beanName = shot.BeanName ?? shot.BagDisplayName ?? "Unknown Bean";

		// Tuned for visual density parity with original: spacing 6, padding 10.
		// Original MauiReactor uses spacing:8/padding:12 but CollectionView renders more compactly
		// than ScrollView+VStack, so we compensate with tighter values.
		var card = VStack(spacing: 6f,
			// Header: coffee icon + drink type + rating smiley badge
			HStack(spacing: 4f,
				FormHelpers.MakeIcon(Icons.Coffee, 18, CoffeeColors.Primary),
				Text(shot.DrinkType)
					.Modifier(CoffeeModifiers.TitleSmall),
				new Spacer(),
				MakeRatingBadge(shot)
			),

			// Bean name
			Text(beanName)
				.Modifier(CoffeeModifiers.FormValue),

			// Recipe line
			Text(FormatRecipeLine(shot))
				.Modifier(CoffeeModifiers.SecondaryText),

			// Footer: single line "Timestamp • By: Name • For: Name"
			Text(FormatFooterLine(shot))
				.Modifier(CoffeeModifiers.Caption)
		)
		.Modifier(CoffeeModifiers.ShotCard);

		if (onTap != null)
			card = card.OnTap(_ => onTap());

		return card;
	}

	static View MakeRatingBadge(ShotRecord shot)
	{
		if (!shot.Rating.HasValue)
			return Text("").Frame(width: 0, height: 0).Opacity(0);

		var rating = shot.Rating.Value;
		var glyph = rating switch
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

	static string FormatRecipeLine(ShotRecord shot)
	{
		var doseIn = $"{shot.DoseIn:F1}g in";
		var doseOut = shot.ActualOutput.HasValue ? $"{shot.ActualOutput:F1}g out" : "\u2014";
		var time = shot.ActualTime.HasValue ? $"({shot.ActualTime:F1}s)" : "";
		return $"{doseIn} \u2192 {doseOut} {time}".Trim();
	}

	static string FormatFooterLine(ShotRecord shot)
	{
		var parts = new List<string> { FormatTimestamp(shot) };
		if (shot.MadeByName != null)
			parts.Add($"By: {shot.MadeByName}");
		if (shot.MadeForName != null)
			parts.Add($"For: {shot.MadeForName}");
		return string.Join(" \u2022 ", parts);
	}

	static string FormatTimestamp(ShotRecord shot)
	{
		var diff = DateTime.Now - shot.Timestamp;
		if (diff.TotalMinutes < 1) return "Just now";
		if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
		if (diff.TotalHours < 24) return shot.Timestamp.ToString("h:mm tt");
		if (diff.TotalDays < 7) return shot.Timestamp.ToString("ddd h:mm tt");
		return shot.Timestamp.ToString("MMM d, h:mm tt");
	}
}
