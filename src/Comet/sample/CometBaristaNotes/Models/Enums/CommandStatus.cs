namespace CometBaristaNotes.Models.Enums;

/// <summary>
/// Processing status for a voice command.
/// </summary>
public enum CommandStatus
{
	Listening = 0,
	Processing = 1,
	AwaitingConfirmation = 2,
	Executing = 3,
	Completed = 4,
	Failed = 5,
	Cancelled = 6
}
