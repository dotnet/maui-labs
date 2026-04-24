using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

/// <summary>
/// Service for processing voice commands using AI interpretation.
/// </summary>
public interface IVoiceCommandService
{
	/// <summary>
	/// Event raised when the service needs to pause speech recognition
	/// (e.g., when opening camera for vision analysis).
	/// </summary>
	event EventHandler? PauseSpeechRequested;

	/// <summary>
	/// Event raised when the service is done and speech can resume.
	/// </summary>
	event EventHandler? ResumeSpeechRequested;

	/// <summary>
	/// Interprets a voice command transcript and returns the parsed intent and parameters.
	/// </summary>
	Task<VoiceCommandResponseDto> InterpretCommandAsync(
		VoiceCommandRequestDto request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Executes a voice command after interpretation.
	/// </summary>
	Task<VoiceToolResultDto> ExecuteCommandAsync(
		VoiceCommandResponseDto response,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Interprets and executes a voice command in one step (for non-confirmation flows).
	/// </summary>
	Task<VoiceToolResultDto> ProcessCommandAsync(
		VoiceCommandRequestDto request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Clears the conversation history for the current session.
	/// Call this when starting a new voice session.
	/// </summary>
	void ClearConversationHistory();
}
