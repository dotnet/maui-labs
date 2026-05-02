namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Equipment information for AI context.
/// </summary>
public record EquipmentContextDto
{
	public string? MachineName { get; init; }
	public string? GrinderName { get; init; }
}
