using System.Text;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

/// <summary>
/// Utility class for building AI prompts from shot context.
/// Extracted for testability from the MAUI-dependent AIAdviceService.
/// </summary>
public static class AIPromptBuilder
{
	/// <summary>
	/// Builds the user prompt from shot context.
	/// </summary>
	public static string BuildPrompt(AIAdviceRequestDto context)
	{
		var sb = new StringBuilder();

		sb.AppendLine("## Current Shot");
		if (!string.IsNullOrWhiteSpace(context.CurrentShot.DrinkType))
			sb.AppendLine($"- Drink type: {context.CurrentShot.DrinkType}");
		sb.AppendLine($"- Dose: {context.CurrentShot.DoseIn}g in");
		if (context.CurrentShot.ActualOutput.HasValue)
			sb.AppendLine($"- Yield: {context.CurrentShot.ActualOutput}g out");
		if (context.CurrentShot.ActualTime.HasValue)
			sb.AppendLine($"- Time: {context.CurrentShot.ActualTime}s");
		if (!string.IsNullOrWhiteSpace(context.CurrentShot.GrindSetting))
			sb.AppendLine($"- Grind: {context.CurrentShot.GrindSetting}");
		if (context.CurrentShot.Rating.HasValue)
			sb.AppendLine($"- Rating: {context.CurrentShot.Rating}/4");
		if (!string.IsNullOrWhiteSpace(context.CurrentShot.TastingNotes))
			sb.AppendLine($"- Tasting notes: {context.CurrentShot.TastingNotes}");

		sb.AppendLine();
		sb.AppendLine("## Bean Information");
		sb.AppendLine($"- Name: {context.BeanInfo.Name}");
		if (!string.IsNullOrWhiteSpace(context.BeanInfo.Roaster))
			sb.AppendLine($"- Roaster: {context.BeanInfo.Roaster}");
		if (!string.IsNullOrWhiteSpace(context.BeanInfo.Origin))
			sb.AppendLine($"- Origin: {context.BeanInfo.Origin}");
		sb.AppendLine($"- Days since roast: {context.BeanInfo.DaysFromRoast}");
		if (!string.IsNullOrWhiteSpace(context.BeanInfo.Notes))
			sb.AppendLine($"- Flavor notes: {context.BeanInfo.Notes}");

		if (context.Equipment != null)
		{
			sb.AppendLine();
			sb.AppendLine("## Equipment");
			if (!string.IsNullOrWhiteSpace(context.Equipment.MachineName))
				sb.AppendLine($"- Machine: {context.Equipment.MachineName}");
			if (!string.IsNullOrWhiteSpace(context.Equipment.GrinderName))
				sb.AppendLine($"- Grinder: {context.Equipment.GrinderName}");
		}

		if (context.HistoricalShots.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("## Previous Shots (same beans, sorted by rating)");

			var bestShots = context.HistoricalShots
				.Where(s => s.Rating.HasValue && s.Rating >= 3)
				.Take(5)
				.ToList();

			if (bestShots.Count > 0)
			{
				sb.AppendLine("Best rated shots:");
				foreach (var shot in bestShots)
				{
					var details = new List<string> { $"{shot.DoseIn}g in" };
					if (shot.ActualOutput.HasValue) details.Add($"{shot.ActualOutput}g out");
					if (shot.ActualTime.HasValue) details.Add($"{shot.ActualTime}s");
					if (!string.IsNullOrWhiteSpace(shot.GrindSetting)) details.Add($"grind {shot.GrindSetting}");
					details.Add($"rated {shot.Rating}/4");
					sb.AppendLine($"- {string.Join(", ", details)}");
				}
			}

			var recentShots = context.HistoricalShots
				.OrderByDescending(s => s.Timestamp)
				.Take(3)
				.ToList();

			if (recentShots.Count > 0)
			{
				sb.AppendLine("Most recent shots:");
				foreach (var shot in recentShots)
				{
					var details = new List<string> { $"{shot.DoseIn}g in" };
					if (shot.ActualOutput.HasValue) details.Add($"{shot.ActualOutput}g out");
					if (shot.ActualTime.HasValue) details.Add($"{shot.ActualTime}s");
					if (shot.Rating.HasValue) details.Add($"rated {shot.Rating}/4");
					sb.AppendLine($"- {string.Join(", ", details)}");
				}
			}
		}

		sb.AppendLine();
		sb.AppendLine("Based on this shot, the drink type, and my history, what adjustments would you suggest to improve my next shot?");

		return sb.ToString();
	}

	/// <summary>
	/// Builds a brief prompt for passive insights.
	/// </summary>
	public static string BuildPassivePrompt(AIAdviceRequestDto context)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"Shot: {context.CurrentShot.DoseIn}g in");
		if (context.CurrentShot.ActualOutput.HasValue)
			sb.Append($", {context.CurrentShot.ActualOutput}g out");
		if (context.CurrentShot.ActualTime.HasValue)
			sb.Append($", {context.CurrentShot.ActualTime}s");
		if (!string.IsNullOrWhiteSpace(context.CurrentShot.GrindSetting))
			sb.Append($", grind {context.CurrentShot.GrindSetting}");

		if (context.HistoricalShots.Any(s => s.Rating >= 3))
		{
			var best = context.HistoricalShots.First(s => s.Rating >= 3);
			sb.AppendLine();
			sb.Append($"Best shot was: {best.DoseIn}g in");
			if (best.ActualOutput.HasValue)
				sb.Append($", {best.ActualOutput}g out");
			if (best.ActualTime.HasValue)
				sb.Append($", {best.ActualTime}s");
		}

		sb.AppendLine();
		sb.AppendLine("Quick tip?");

		return sb.ToString();
	}

	/// <summary>
	/// Builds a prompt for new bean recommendations (no shot history).
	/// </summary>
	public static string BuildNewBeanPrompt(BeanRecommendationContextDto context)
	{
		var sb = new StringBuilder();

		sb.AppendLine("## Bean Information");
		sb.AppendLine($"- Name: {context.BeanName}");
		if (!string.IsNullOrWhiteSpace(context.Roaster))
			sb.AppendLine($"- Roaster: {context.Roaster}");
		if (!string.IsNullOrWhiteSpace(context.Origin))
			sb.AppendLine($"- Origin: {context.Origin}");
		if (context.DaysFromRoast.HasValue)
			sb.AppendLine($"- Days since roast: {context.DaysFromRoast}");
		if (!string.IsNullOrWhiteSpace(context.Notes))
			sb.AppendLine($"- Flavor notes: {context.Notes}");

		if (context.Equipment != null)
		{
			sb.AppendLine();
			sb.AppendLine("## Equipment");
			if (!string.IsNullOrWhiteSpace(context.Equipment.MachineName))
				sb.AppendLine($"- Machine: {context.Equipment.MachineName}");
			if (!string.IsNullOrWhiteSpace(context.Equipment.GrinderName))
				sb.AppendLine($"- Grinder: {context.Equipment.GrinderName}");
		}

		sb.AppendLine();
		sb.AppendLine("I have no previous shots with this bean. Based on the bean characteristics above, recommend starting extraction parameters.");
		sb.AppendLine();
		sb.AppendLine("Respond ONLY with a JSON object in this exact format (no other text):");
		sb.AppendLine("{");
		sb.AppendLine("  \"dose\": <number in grams, typically 18-20>,");
		sb.AppendLine("  \"grind\": \"<grinder setting as string>\",");
		sb.AppendLine("  \"output\": <number in grams, typically 36-50>,");
		sb.AppendLine("  \"duration\": <number in seconds, typically 25-35>");
		sb.AppendLine("}");

		return sb.ToString();
	}

	/// <summary>
	/// Builds a prompt for returning bean recommendations (with shot history).
	/// </summary>
	public static string BuildReturningBeanPrompt(BeanRecommendationContextDto context)
	{
		var sb = new StringBuilder();

		sb.AppendLine("## Bean Information");
		sb.AppendLine($"- Name: {context.BeanName}");
		if (!string.IsNullOrWhiteSpace(context.Roaster))
			sb.AppendLine($"- Roaster: {context.Roaster}");
		if (!string.IsNullOrWhiteSpace(context.Origin))
			sb.AppendLine($"- Origin: {context.Origin}");
		if (context.DaysFromRoast.HasValue)
			sb.AppendLine($"- Days since roast: {context.DaysFromRoast}");
		if (!string.IsNullOrWhiteSpace(context.Notes))
			sb.AppendLine($"- Flavor notes: {context.Notes}");

		if (context.Equipment != null)
		{
			sb.AppendLine();
			sb.AppendLine("## Equipment");
			if (!string.IsNullOrWhiteSpace(context.Equipment.MachineName))
				sb.AppendLine($"- Machine: {context.Equipment.MachineName}");
			if (!string.IsNullOrWhiteSpace(context.Equipment.GrinderName))
				sb.AppendLine($"- Grinder: {context.Equipment.GrinderName}");
		}

		if (context.HistoricalShots?.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("## Previous Shots (sorted by rating)");
			foreach (var shot in context.HistoricalShots)
			{
				var details = new List<string> { $"{shot.DoseIn}g in" };
				if (shot.ActualOutput.HasValue) details.Add($"{shot.ActualOutput}g out");
				if (shot.ActualTime.HasValue) details.Add($"{shot.ActualTime}s");
				if (!string.IsNullOrWhiteSpace(shot.GrindSetting)) details.Add($"grind {shot.GrindSetting}");
				if (shot.Rating.HasValue) details.Add($"rated {shot.Rating}/4");
				sb.AppendLine($"- {string.Join(", ", details)}");
			}
		}

		sb.AppendLine();
		sb.AppendLine("Based on my shot history with this bean, recommend optimal extraction parameters.");
		sb.AppendLine();
		sb.AppendLine("Respond ONLY with a JSON object in this exact format (no other text):");
		sb.AppendLine("{");
		sb.AppendLine("  \"dose\": <number in grams>,");
		sb.AppendLine("  \"grind\": \"<grinder setting as string>\",");
		sb.AppendLine("  \"output\": <number in grams>,");
		sb.AppendLine("  \"duration\": <number in seconds>");
		sb.AppendLine("}");

		return sb.ToString();
	}
}
