namespace CometBaristaNotes.Models.Enums;

/// <summary>
/// Identifies the action type from a voice command.
/// </summary>
public enum CommandIntent
{
	Unknown = 0,
	LogShot = 1,
	AddBean = 2,
	AddBag = 3,
	RateShot = 4,
	AddTastingNotes = 5,
	AddEquipment = 6,
	AddProfile = 7,
	Navigate = 8,
	Query = 9,
	Cancel = 10,
	Help = 11
}
