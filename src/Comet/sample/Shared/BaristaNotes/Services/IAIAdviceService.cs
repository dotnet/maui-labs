using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IAIAdviceService
{
    Task<bool> IsConfiguredAsync();

    Task<AIAdviceResponseDto> GetAdviceForShotAsync(
        int shotId,
        CancellationToken cancellationToken = default);

    Task<string?> GetPassiveInsightAsync(
        int shotId,
        CancellationToken cancellationToken = default);

    Task<AIRecommendationDto> GetRecommendationsForBeanAsync(
        int beanId,
        CancellationToken cancellationToken = default);
}
