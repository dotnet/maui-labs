namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Simplified shot data for AI context.
/// </summary>
public record ShotContextDto
{
	public decimal DoseIn { get; init; }
	public decimal? ActualOutput { get; init; }
	public decimal? ActualTime { get; init; }
	public string GrindSetting { get; init; } = string.Empty;
	public int? Rating { get; init; }
	public string? TastingNotes { get; init; }
	public string? DrinkType { get; init; }
	public DateTime Timestamp { get; init; }
}
