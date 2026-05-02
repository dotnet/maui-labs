using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

/// <summary>
/// Service for generating AI-powered espresso shot advice.
/// API key is app-provided (loaded from configuration), not user-configured.
/// </summary>
public interface IAIAdviceService
{
	/// <summary>
	/// Checks if the AI service is configured and ready to use.
	/// Returns true if local client works or Azure OpenAI is configured.
	/// </summary>
	Task<bool> IsConfiguredAsync();

	/// <summary>
	/// Gets detailed AI advice for a specific shot.
	/// </summary>
	Task<AIAdviceResponseDto> GetAdviceForShotAsync(
		int shotId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a brief passive insight for a shot if parameters deviate from history.
	/// </summary>
	Task<string?> GetPassiveInsightAsync(int shotId);

	/// <summary>
	/// Gets AI-powered extraction parameter recommendations for a selected bean.
	/// </summary>
	Task<AIRecommendationDto> GetRecommendationsForBeanAsync(
		int beanId,
		CancellationToken cancellationToken = default);
}
