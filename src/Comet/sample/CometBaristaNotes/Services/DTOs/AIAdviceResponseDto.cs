namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Response from the AI advice service.
/// </summary>
public record AIAdviceResponseDto
{
	public bool Success { get; init; }
	public List<ShotAdjustment> Adjustments { get; init; } = [];
	public string? Reasoning { get; init; }
	public string? ErrorMessage { get; init; }
	public string? Source { get; init; }
	public string? PromptSent { get; init; }
	public int HistoricalShotsCount { get; init; }
	public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}
