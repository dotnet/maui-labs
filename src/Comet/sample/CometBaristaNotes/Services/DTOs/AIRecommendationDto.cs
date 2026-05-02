namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Response from AI service for bean extraction recommendations.
/// </summary>
public record AIRecommendationDto
{
	public bool Success { get; init; }
	public decimal Dose { get; init; }
	public string GrindSetting { get; init; } = string.Empty;
	public decimal Output { get; init; }
	public decimal Duration { get; init; }
	public RecommendationType RecommendationType { get; init; }
	public string? Confidence { get; init; }
	public string? ErrorMessage { get; init; }
	public string? Source { get; init; }
}

public enum RecommendationType
{
	NewBean,
	ReturningBean
}
