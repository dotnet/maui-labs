using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Input to the voice command service after speech recognition.
/// </summary>
public record VoiceCommandRequestDto(
	string Transcript,
	double Confidence,
	int? ActiveBagId = null,
	int? ActiveEquipmentId = null,
	int? ActiveUserId = null
);

/// <summary>
/// Output from AI interpretation of a voice command.
/// </summary>
public record VoiceCommandResponseDto
{
	/// <summary>
	/// The identified action type.
	/// </summary>
	public CommandIntent Intent { get; init; }

	/// <summary>
	/// Extracted parameters from the voice command.
	/// </summary>
	public Dictionary<string, object> Parameters { get; init; } = new();

	/// <summary>
	/// Human-readable confirmation message for the user.
	/// </summary>
	public string ConfirmationMessage { get; init; } = string.Empty;

	/// <summary>
	/// Whether the action requires explicit user confirmation before execution.
	/// </summary>
	public bool RequiresConfirmation { get; init; }

	/// <summary>
	/// Error message if interpretation failed.
	/// </summary>
	public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result after executing a voice command tool.
/// </summary>
public record VoiceToolResultDto
{
	/// <summary>
	/// Whether the operation succeeded.
	/// </summary>
	public bool Success { get; init; }

	/// <summary>
	/// Human-readable result message.
	/// </summary>
	public string Message { get; init; } = string.Empty;

	/// <summary>
	/// The entity created or modified, if applicable.
	/// </summary>
	public object? CreatedEntity { get; init; }

	/// <summary>
	/// The ID of the created or modified entity.
	/// </summary>
	public int? EntityId { get; init; }
}
