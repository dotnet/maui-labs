namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Context gathered for an AI advice request.
/// </summary>
public record AIAdviceRequestDto
{
	public int ShotId { get; init; }
	public required ShotContextDto CurrentShot { get; init; }
	public List<ShotContextDto> HistoricalShots { get; init; } = new();
	public required BeanContextDto BeanInfo { get; init; }
	public EquipmentContextDto? Equipment { get; init; }
}
