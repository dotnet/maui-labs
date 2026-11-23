using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public static class AIAdviceErrors
{
    public const string Unavailable = "AI_UNAVAILABLE";
    public const string NotFound = "NOT_FOUND";
    public const string Cancelled = "CANCELLED";
    public const string Connectivity = "CONNECTIVITY";
    public const string RateLimited = "RATE_LIMITED";
    public const string InvalidResponse = "INVALID_RESPONSE";
    public const string Unexpected = "UNEXPECTED";
}

public sealed class AIAdviceService(
    IShotService shotService,
    IAIAdviceAdapter? adapter = null) : IAIAdviceService
{
    public Task<bool> IsConfiguredAsync() =>
        Task.FromResult(adapter is { IsConfigured: true });

    public async Task<AIAdviceResponseDto> GetAdviceForShotAsync(
        int shotId,
        CancellationToken cancellationToken = default)
    {
        if (adapter is not { IsConfigured: true })
        {
            return AdviceFailure(
                AIAdviceErrors.Unavailable,
                "AI advice is temporarily unavailable. Please try again later.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = await shotService.GetShotContextForAIAsync(shotId);
            cancellationToken.ThrowIfCancellationRequested();
            if (context is null)
                return AdviceFailure(AIAdviceErrors.NotFound, "Shot not found.");

            var response = await adapter.GetShotAdviceAsync(context, cancellationToken);
            if (!response.Success)
                return NormalizeAdviceFailure(response);

            if (response.Adjustments.Count == 0 && string.IsNullOrWhiteSpace(response.Reasoning))
            {
                return AdviceFailure(
                    AIAdviceErrors.InvalidResponse,
                    "AI service error. Please try again later.");
            }

            return response with
            {
                ErrorMessage = null,
                ErrorCode = null,
                HistoricalShotsCount = context.HistoricalShots.Count
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AdviceFailure(
                AIAdviceErrors.Cancelled,
                "Request was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return AdviceFailure(
                AIAdviceErrors.Connectivity,
                "AI service timed out. Please try again.");
        }
        catch (HttpRequestException)
        {
            return AdviceFailure(
                AIAdviceErrors.Connectivity,
                "Unable to connect. Please check your internet connection.");
        }
        catch (Exception exception) when (IsRateLimit(exception))
        {
            return AdviceFailure(
                AIAdviceErrors.RateLimited,
                "Too many requests. Please wait a moment.");
        }
        catch (InvalidDataException)
        {
            return AdviceFailure(
                AIAdviceErrors.InvalidResponse,
                "AI service returned an invalid response.");
        }
        catch
        {
            return AdviceFailure(
                AIAdviceErrors.Unexpected,
                "An unexpected error occurred. Please try again.");
        }
    }

    public async Task<string?> GetPassiveInsightAsync(
        int shotId,
        CancellationToken cancellationToken = default)
    {
        if (adapter is not { IsConfigured: true })
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = await shotService.GetShotContextForAIAsync(shotId);
            if (context is null || !HasSignificantDeviation(context))
                return null;

            return await adapter.GetPassiveInsightAsync(context, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<AIRecommendationDto> GetRecommendationsForBeanAsync(
        int beanId,
        CancellationToken cancellationToken = default)
    {
        if (adapter is not { IsConfigured: true })
        {
            return RecommendationFailure(
                AIAdviceErrors.Unavailable,
                "AI recommendations are temporarily unavailable. Please try again later.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = await shotService.GetBeanRecommendationContextAsync(beanId);
            cancellationToken.ThrowIfCancellationRequested();
            if (context is null)
                return RecommendationFailure(AIAdviceErrors.NotFound, "Bean not found.");

            var response = await adapter.GetBeanRecommendationAsync(context, cancellationToken);
            if (!response.Success)
                return NormalizeRecommendationFailure(response);

            if (response.Dose <= 0 ||
                response.Output <= 0 ||
                response.Duration <= 0 ||
                string.IsNullOrWhiteSpace(response.GrindSetting))
            {
                return RecommendationFailure(
                    AIAdviceErrors.InvalidResponse,
                    "AI service error. Please try again later.");
            }

            return response with
            {
                ErrorMessage = null,
                ErrorCode = null,
                RecommendationType = context.HasHistory
                    ? RecommendationType.ReturningBean
                    : RecommendationType.NewBean
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return RecommendationFailure(
                AIAdviceErrors.Connectivity,
                "AI service timed out. Please try again.");
        }
        catch (HttpRequestException)
        {
            return RecommendationFailure(
                AIAdviceErrors.Connectivity,
                "Unable to connect. Please check your internet connection.");
        }
        catch (Exception exception) when (IsRateLimit(exception))
        {
            return RecommendationFailure(
                AIAdviceErrors.RateLimited,
                "Too many requests. Please wait a moment.");
        }
        catch (InvalidDataException)
        {
            return RecommendationFailure(
                AIAdviceErrors.InvalidResponse,
                "AI service returned an invalid response.");
        }
        catch
        {
            return RecommendationFailure(
                AIAdviceErrors.Unexpected,
                "An unexpected error occurred. Please try again.");
        }
    }

    private static bool HasSignificantDeviation(AIAdviceRequestDto context)
    {
        var best = context.HistoricalShots
            .Where(shot => shot.Rating >= 3)
            .ToList();
        if (best.Count == 0)
            return false;

        var averageDose = best.Average(shot => shot.DoseIn);
        if (averageDose > 0 &&
            Math.Abs(context.CurrentShot.DoseIn - averageDose) / averageDose > 0.1m)
        {
            return true;
        }

        var outputValues = best
            .Where(shot => shot.ActualOutput.HasValue)
            .Select(shot => shot.ActualOutput!.Value)
            .ToList();
        if (context.CurrentShot.ActualOutput is decimal output && outputValues.Count != 0)
        {
            var average = outputValues.Average();
            if (average > 0 && Math.Abs(output - average) / average > 0.15m)
                return true;
        }

        var timeValues = best
            .Where(shot => shot.ActualTime.HasValue)
            .Select(shot => shot.ActualTime!.Value)
            .ToList();
        if (context.CurrentShot.ActualTime is decimal duration && timeValues.Count != 0)
        {
            var average = timeValues.Average();
            if (average > 0 && Math.Abs(duration - average) / average > 0.2m)
                return true;
        }

        return false;
    }

    private static AIAdviceResponseDto NormalizeAdviceFailure(AIAdviceResponseDto response) =>
        AdviceFailure(
            string.IsNullOrWhiteSpace(response.ErrorCode)
                ? AIAdviceErrors.Unexpected
                : response.ErrorCode,
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "AI service error. Please try again later."
                : response.ErrorMessage);

    private static AIRecommendationDto NormalizeRecommendationFailure(
        AIRecommendationDto response) =>
        RecommendationFailure(
            string.IsNullOrWhiteSpace(response.ErrorCode)
                ? AIAdviceErrors.Unexpected
                : response.ErrorCode,
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "AI service error. Please try again later."
                : response.ErrorMessage);

    private static AIAdviceResponseDto AdviceFailure(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        Adjustments = Array.Empty<ShotAdjustment>()
    };

    private static AIRecommendationDto RecommendationFailure(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };

    private static bool IsRateLimit(Exception exception) =>
        exception.Message.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("429", StringComparison.Ordinal);
}

public sealed class UnavailableAIAdviceAdapter : IAIAdviceAdapter
{
    public bool IsConfigured => false;

    public Task<AIAdviceResponseDto> GetShotAdviceAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AIAdviceResponseDto
        {
            Success = false,
            ErrorCode = AIAdviceErrors.Unavailable,
            ErrorMessage = "AI advice is temporarily unavailable. Please try again later."
        });

    public Task<string?> GetPassiveInsightAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<AIRecommendationDto> GetBeanRecommendationAsync(
        BeanRecommendationContextDto context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AIRecommendationDto
        {
            Success = false,
            ErrorCode = AIAdviceErrors.Unavailable,
            ErrorMessage = "AI recommendations are temporarily unavailable. Please try again later."
        });
}

public sealed class FallbackAIAdviceAdapter(
    IAIAdviceAdapter? localAdapter,
    IAIAdviceAdapter? cloudAdapter) : IAIAdviceAdapter
{
    private volatile bool _localDisabled;

    public bool IsConfigured =>
        (!_localDisabled && localAdapter is { IsConfigured: true }) ||
        cloudAdapter is { IsConfigured: true };

    public async Task<AIAdviceResponseDto> GetShotAdviceAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!_localDisabled && localAdapter is { IsConfigured: true })
        {
            try
            {
                var local = await localAdapter.GetShotAdviceAsync(request, cancellationToken);
                if (local.Success)
                    return local;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _localDisabled = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (cloudAdapter is { IsConfigured: true })
            return await cloudAdapter.GetShotAdviceAsync(request, cancellationToken);

        return await new UnavailableAIAdviceAdapter()
            .GetShotAdviceAsync(request, cancellationToken);
    }

    public async Task<string?> GetPassiveInsightAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!_localDisabled && localAdapter is { IsConfigured: true })
        {
            try
            {
                var local = await localAdapter.GetPassiveInsightAsync(request, cancellationToken);
                if (!string.IsNullOrWhiteSpace(local))
                    return local;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _localDisabled = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return cloudAdapter is { IsConfigured: true }
            ? await cloudAdapter.GetPassiveInsightAsync(request, cancellationToken)
            : null;
    }

    public async Task<AIRecommendationDto> GetBeanRecommendationAsync(
        BeanRecommendationContextDto context,
        CancellationToken cancellationToken = default)
    {
        if (!_localDisabled && localAdapter is { IsConfigured: true })
        {
            try
            {
                var local = await localAdapter.GetBeanRecommendationAsync(context, cancellationToken);
                if (local.Success)
                    return local;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _localDisabled = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (cloudAdapter is { IsConfigured: true })
            return await cloudAdapter.GetBeanRecommendationAsync(context, cancellationToken);

        return await new UnavailableAIAdviceAdapter()
            .GetBeanRecommendationAsync(context, cancellationToken);
    }
}
