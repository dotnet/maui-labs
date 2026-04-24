namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Context data for AI bean recommendations.
/// </summary>
public record BeanRecommendationContextDto
{
	public int BeanId { get; init; }
	public string BeanName { get; init; } = string.Empty;
	public string? Roaster { get; init; }
	public string? Origin { get; init; }
	public string? Notes { get; init; }
	public DateTime? RoastDate { get; init; }
	public int? DaysFromRoast { get; init; }
	public bool HasHistory { get; init; }
	public List<ShotContextDto>? HistoricalShots { get; init; }
	public EquipmentContextDto? Equipment { get; init; }
}
