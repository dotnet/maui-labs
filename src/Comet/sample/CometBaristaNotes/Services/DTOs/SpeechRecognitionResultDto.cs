namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Result from speech recognition.
/// </summary>
public record SpeechRecognitionResultDto
{
	public bool Success { get; init; }
	public string? Transcript { get; init; }
	public double Confidence { get; init; }
	public string? ErrorMessage { get; init; }
}
