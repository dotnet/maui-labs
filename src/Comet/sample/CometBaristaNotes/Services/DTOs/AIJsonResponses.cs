using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// JSON schema for AI shot advice (structured output).
/// </summary>
public sealed record ShotAdviceJson
{
	[JsonPropertyName("adjustments")]
	[Description("List of 1-3 specific parameter changes to improve the shot")]
	public List<ShotAdjustment> Adjustments { get; init; } = [];

	[JsonPropertyName("reasoning")]
	[Description("Single sentence explaining the root cause or reasoning behind the adjustments")]
	public string Reasoning { get; init; } = string.Empty;
}

/// <summary>
/// A specific shot parameter adjustment recommendation.
/// </summary>
public sealed record ShotAdjustment
{
	[JsonPropertyName("parameter")]
	[Description("The shot parameter to change: dose, grind, yield, or time")]
	public string Parameter { get; init; } = string.Empty;

	[JsonPropertyName("direction")]
	[Description("Direction of change: increase, decrease, finer, coarser")]
	public string Direction { get; init; } = string.Empty;

	[JsonPropertyName("amount")]
	[Description("Specific amount to change (e.g., '0.5g', '2 clicks', '3 seconds')")]
	public string Amount { get; init; } = string.Empty;
}

/// <summary>
/// JSON schema for AI bean recommendation (structured output).
/// </summary>
public sealed record BeanRecommendationJson
{
	[JsonPropertyName("dose")]
	[Description("Recommended dose in grams, typically 16-20g")]
	public decimal Dose { get; init; }

	[JsonPropertyName("grind")]
	[Description("Grind setting recommendation (e.g., 'medium-fine', 'finer than medium')")]
	public string Grind { get; init; } = string.Empty;

	[JsonPropertyName("output")]
	[Description("Target yield in grams, typically 1:2 to 1:2.5 ratio")]
	public decimal Output { get; init; }

	[JsonPropertyName("duration")]
	[Description("Target extraction time in seconds, typically 25-35")]
	public decimal Duration { get; init; }
}
