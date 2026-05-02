namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Bean information for AI context.
/// </summary>
public record BeanContextDto
{
	public string Name { get; init; } = string.Empty;
	public string? Roaster { get; init; }
	public string? Origin { get; init; }
	public DateTime RoastDate { get; init; }
	public int DaysFromRoast { get; init; }
	public string? Notes { get; init; }
}
