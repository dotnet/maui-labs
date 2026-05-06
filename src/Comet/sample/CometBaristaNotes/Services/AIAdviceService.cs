using System.ClientModel;
using Azure.AI.OpenAI;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services.DTOs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CometBaristaNotes.Services;

/// <summary>
/// Service for generating AI-powered espresso shot advice.
/// Tries on-device AI first (Apple Intelligence), falls back to Azure OpenAI if available.
/// Once local client fails, it is disabled for the remainder of the app session.
/// </summary>
public class AIAdviceService : IAIAdviceService
{
	private readonly IShotService _shotService;
	private readonly IBeanService _beanService;
	private readonly IBagService _bagService;
	private readonly IEquipmentService _equipmentService;
	private readonly IConfiguration _configuration;
	private readonly ILogger<AIAdviceService> _logger;

	// Injected on-device client (from AddPlatformChatClient)
	private readonly IChatClient? _localClient;

	// Azure OpenAI client created on demand
	private IChatClient? _azureOpenAIClient;

	// Session-level flag: once local client fails, don't retry until app restart
	private bool _localClientDisabled = false;

	private const string ModelId = "gpt-4.1-mini";
	private const int LocalTimeoutSeconds = 10;
	private const int CloudTimeoutSeconds = 20;

	private const string SourceOnDevice = "via Apple Intelligence";
	private const string SourceCloud = "via Azure OpenAI";

	private const string SystemPrompt = @"You are an expert barista assistant helping improve espresso shots.
Analyze the shot data and provide 1-3 specific parameter adjustments.
Consider extraction ratio (target 1:2 to 1:2.5), time (25-35s), grind, and dose.
Be practical and specific with amounts (e.g., '0.5g', '2 clicks finer', '3 seconds').
Provide brief reasoning in one sentence.";

	private const string RecommendationSystemPrompt = @"You are an expert barista assistant.
Recommend espresso extraction parameters based on bean characteristics.
Use standard ratios (1:2 to 1:2.5), typical times (25-35s).
Adjust for roast level: darker roasts need coarser grind.";

	public AIAdviceService(
		IShotService shotService,
		IBeanService beanService,
		IBagService bagService,
		IEquipmentService equipmentService,
		IConfiguration configuration,
		ILogger<AIAdviceService> logger,
		IChatClient? chatClient = null)
	{
		_shotService = shotService;
		_beanService = beanService;
		_bagService = bagService;
		_equipmentService = equipmentService;
		_configuration = configuration;
		_logger = logger;
		_localClient = chatClient;
	}

	public Task<bool> IsConfiguredAsync()
	{
		var hasLocalClient = _localClient != null && !_localClientDisabled;
		var hasAzureOpenAI = !string.IsNullOrWhiteSpace(_configuration["AzureOpenAI:Endpoint"]) &&
		                     !string.IsNullOrWhiteSpace(_configuration["AzureOpenAI:ApiKey"]);
		return Task.FromResult(hasLocalClient || hasAzureOpenAI);
	}

	public async Task<AIAdviceResponseDto> GetAdviceForShotAsync(
		int shotId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (!await IsConfiguredAsync())
			{
				return new AIAdviceResponseDto
				{
					Success = false,
					ErrorMessage = "AI advice is temporarily unavailable. Please try again later."
				};
			}

			var context = BuildShotContext(shotId);
			if (context == null)
			{
				return new AIAdviceResponseDto
				{
					Success = false,
					ErrorMessage = "Shot not found."
				};
			}

			var userMessage = AIPromptBuilder.BuildPrompt(context);

			var messages = new List<ChatMessage>
			{
				new ChatMessage(ChatRole.System, SystemPrompt),
				new ChatMessage(ChatRole.User, userMessage)
			};

			var (advice, source) = await TryGetTypedResponseWithFallbackAsync<ShotAdviceJson>(
				messages,
				LocalTimeoutSeconds,
				CloudTimeoutSeconds,
				cancellationToken);

			if (advice == null)
			{
				return new AIAdviceResponseDto
				{
					Success = false,
					ErrorMessage = "AI service error. Please try again later."
				};
			}

			return new AIAdviceResponseDto
			{
				Success = true,
				Adjustments = advice.Adjustments,
				Reasoning = advice.Reasoning,
				Source = source,
				PromptSent = userMessage,
				HistoricalShotsCount = context.HistoricalShots.Count,
				GeneratedAt = DateTime.UtcNow
			};
		}
		catch (OperationCanceledException)
		{
			return new AIAdviceResponseDto
			{
				Success = false,
				ErrorMessage = "Request timed out. Please try again."
			};
		}
		catch (HttpRequestException)
		{
			return new AIAdviceResponseDto
			{
				Success = false,
				ErrorMessage = "Unable to connect. Please check your internet connection."
			};
		}
		catch (Exception ex) when (ex.Message.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
		                            ex.Message.Contains("429"))
		{
			return new AIAdviceResponseDto
			{
				Success = false,
				ErrorMessage = "Too many requests. Please wait a moment."
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error getting AI advice for shot {ShotId}", shotId);
			return new AIAdviceResponseDto
			{
				Success = false,
				ErrorMessage = "An unexpected error occurred. Please try again."
			};
		}
	}

	public async Task<string?> GetPassiveInsightAsync(int shotId)
	{
		try
		{
			if (!await IsConfiguredAsync())
				return null;

			var context = BuildShotContext(shotId);
			if (context == null)
				return null;

			if (!HasSignificantDeviation(context))
				return null;

			var passivePrompt = AIPromptBuilder.BuildPassivePrompt(context);

			var messages = new List<ChatMessage>
			{
				new ChatMessage(ChatRole.System, "You are a brief espresso advisor. Give ONE short sentence of advice (max 15 words)."),
				new ChatMessage(ChatRole.User, passivePrompt)
			};

			var (response, _) = await TryGetResponseWithFallbackAsync(
				messages,
				localTimeoutSeconds: 10,
				cloudTimeoutSeconds: 20,
				CancellationToken.None);

			return response;
		}
		catch
		{
			// Passive insights are non-critical — silently fail
			return null;
		}
	}

	public async Task<AIRecommendationDto> GetRecommendationsForBeanAsync(
		int beanId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (!await IsConfiguredAsync())
			{
				return new AIRecommendationDto
				{
					Success = false,
					ErrorMessage = "AI recommendations are temporarily unavailable. Please try again later."
				};
			}

			var context = BuildBeanRecommendationContext(beanId);
			if (context == null)
			{
				return new AIRecommendationDto
				{
					Success = false,
					ErrorMessage = "Bean not found."
				};
			}

			var userMessage = context.HasHistory
				? AIPromptBuilder.BuildReturningBeanPrompt(context)
				: AIPromptBuilder.BuildNewBeanPrompt(context);

			var recommendationType = context.HasHistory
				? RecommendationType.ReturningBean
				: RecommendationType.NewBean;

			var messages = new List<ChatMessage>
			{
				new ChatMessage(ChatRole.System, RecommendationSystemPrompt),
				new ChatMessage(ChatRole.User, userMessage)
			};

			var (recommendation, source) = await TryGetTypedResponseWithFallbackAsync<BeanRecommendationJson>(
				messages,
				LocalTimeoutSeconds,
				CloudTimeoutSeconds,
				cancellationToken);

			if (recommendation == null)
			{
				return new AIRecommendationDto
				{
					Success = false,
					ErrorMessage = "AI service error. Please try again later."
				};
			}

			return new AIRecommendationDto
			{
				Success = true,
				Dose = recommendation.Dose,
				GrindSetting = recommendation.Grind,
				Output = recommendation.Output,
				Duration = recommendation.Duration,
				RecommendationType = recommendationType,
				Source = source
			};
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (HttpRequestException)
		{
			return new AIRecommendationDto
			{
				Success = false,
				ErrorMessage = "Unable to connect. Please check your internet connection."
			};
		}
		catch (Exception ex) when (ex.Message.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
		                            ex.Message.Contains("429"))
		{
			return new AIRecommendationDto
			{
				Success = false,
				ErrorMessage = "Too many requests. Please wait a moment."
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error getting AI recommendations for bean {BeanId}", beanId);
			return new AIRecommendationDto
			{
				Success = false,
				ErrorMessage = "An unexpected error occurred. Please try again."
			};
		}
	}

	/// <summary>
	/// Builds shot context for AI from the available data services.
	/// Adapts the original IShotService.GetShotContextForAIAsync pattern to CometBaristaNotes.
	/// </summary>
	private AIAdviceRequestDto? BuildShotContext(int shotId)
	{
		var shot = _shotService.GetShot(shotId);
		if (shot == null)
			return null;

		// Find the bag and bean for this shot
		var bag = _bagService.GetBag(shot.BagId);
		var bean = bag != null ? _beanService.GetBean(bag.BeanId) : null;

		var currentShot = new ShotContextDto
		{
			DoseIn = shot.DoseIn,
			ActualOutput = shot.ActualOutput,
			ActualTime = shot.ActualTime,
			GrindSetting = shot.GrindSetting,
			Rating = shot.Rating,
			TastingNotes = shot.TastingNotes,
			DrinkType = shot.DrinkType,
			Timestamp = shot.Timestamp
		};

		var beanInfo = new BeanContextDto
		{
			Name = bean?.Name ?? bag?.BeanName ?? "Unknown",
			Roaster = bean?.Roaster,
			Origin = bean?.Origin,
			RoastDate = bag?.RoastDate ?? DateTime.Now,
			DaysFromRoast = bag != null ? (int)(DateTime.Now - bag.RoastDate).TotalDays : 0,
			Notes = bean?.Notes
		};

		// Get historical shots for the same bean (up to 10, sorted by rating desc)
		var historicalShots = new List<ShotContextDto>();
		if (bean != null)
		{
			var beanShots = _shotService.GetShotsByBean(bean.Id)
				.Where(s => s.Id != shotId)
				.OrderByDescending(s => s.Rating ?? 0)
				.Take(10)
				.Select(s => new ShotContextDto
				{
					DoseIn = s.DoseIn,
					ActualOutput = s.ActualOutput,
					ActualTime = s.ActualTime,
					GrindSetting = s.GrindSetting,
					Rating = s.Rating,
					TastingNotes = s.TastingNotes,
					DrinkType = s.DrinkType,
					Timestamp = s.Timestamp
				})
				.ToList();
			historicalShots = beanShots;
		}

		// Build equipment context
		EquipmentContextDto? equipment = null;
		if (shot.MachineId.HasValue || shot.GrinderId.HasValue)
		{
			var machine = shot.MachineId.HasValue ? _equipmentService.GetEquipment(shot.MachineId.Value) : null;
			var grinder = shot.GrinderId.HasValue ? _equipmentService.GetEquipment(shot.GrinderId.Value) : null;
			equipment = new EquipmentContextDto
			{
				MachineName = machine?.Name ?? shot.MachineName,
				GrinderName = grinder?.Name ?? shot.GrinderName
			};
		}

		return new AIAdviceRequestDto
		{
			ShotId = shotId,
			CurrentShot = currentShot,
			HistoricalShots = historicalShots,
			BeanInfo = beanInfo,
			Equipment = equipment
		};
	}

	/// <summary>
	/// Builds bean recommendation context from available data services.
	/// </summary>
	private BeanRecommendationContextDto? BuildBeanRecommendationContext(int beanId)
	{
		var bean = _beanService.GetBean(beanId);
		if (bean == null)
			return null;

		// Get the most recent active bag for roast date
		var bags = _bagService.GetBagsForBean(beanId);
		var latestBag = bags.OrderByDescending(b => b.RoastDate).FirstOrDefault();

		// Get historical shots for this bean
		var beanShots = _shotService.GetShotsByBean(beanId)
			.OrderByDescending(s => s.Rating ?? 0)
			.Take(10)
			.Select(s => new ShotContextDto
			{
				DoseIn = s.DoseIn,
				ActualOutput = s.ActualOutput,
				ActualTime = s.ActualTime,
				GrindSetting = s.GrindSetting,
				Rating = s.Rating,
				TastingNotes = s.TastingNotes,
				DrinkType = s.DrinkType,
				Timestamp = s.Timestamp
			})
			.ToList();

		// Get equipment context (first machine and grinder)
		EquipmentContextDto? equipment = null;
		var machines = _equipmentService.GetByType(EquipmentType.Machine);
		var grinders = _equipmentService.GetByType(EquipmentType.Grinder);
		if (machines.Count > 0 || grinders.Count > 0)
		{
			equipment = new EquipmentContextDto
			{
				MachineName = machines.FirstOrDefault()?.Name,
				GrinderName = grinders.FirstOrDefault()?.Name
			};
		}

		return new BeanRecommendationContextDto
		{
			BeanId = beanId,
			BeanName = bean.Name,
			Roaster = bean.Roaster,
			Origin = bean.Origin,
			Notes = bean.Notes,
			RoastDate = latestBag?.RoastDate,
			DaysFromRoast = latestBag != null ? (int)(DateTime.Now - latestBag.RoastDate).TotalDays : null,
			HasHistory = beanShots.Count > 0,
			HistoricalShots = beanShots,
			Equipment = equipment
		};
	}

	/// <summary>
	/// Checks if current shot deviates significantly from best historical shots.
	/// </summary>
	private bool HasSignificantDeviation(AIAdviceRequestDto context)
	{
		var bestShots = context.HistoricalShots
			.Where(s => s.Rating.HasValue && s.Rating >= 3)
			.ToList();

		if (bestShots.Count == 0)
			return false;

		var avgDose = bestShots.Average(s => (double)s.DoseIn);
		var avgOutput = bestShots
			.Where(s => s.ActualOutput.HasValue)
			.Select(s => (double)s.ActualOutput!.Value)
			.DefaultIfEmpty(0)
			.Average();
		var avgTime = bestShots
			.Where(s => s.ActualTime.HasValue)
			.Select(s => (double)s.ActualTime!.Value)
			.DefaultIfEmpty(0)
			.Average();

		var current = context.CurrentShot;

		// Check dose deviation (>10%)
		if (avgDose > 0 && Math.Abs((double)current.DoseIn - avgDose) / avgDose > 0.1)
			return true;

		// Check output deviation (>15%)
		if (current.ActualOutput.HasValue && avgOutput > 0)
		{
			if (Math.Abs((double)current.ActualOutput.Value - avgOutput) / avgOutput > 0.15)
				return true;
		}

		// Check time deviation (>20%)
		if (current.ActualTime.HasValue && avgTime > 0)
		{
			if (Math.Abs((double)current.ActualTime.Value - avgTime) / avgTime > 0.2)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Tries to get a typed response from available AI clients with fallback.
	/// Uses IChatClient.GetResponseAsync{T}() for structured JSON output.
	/// Tries local client first (if not disabled), then falls back to OpenAI.
	/// </summary>
	private async Task<(T? Response, string? Source)> TryGetTypedResponseWithFallbackAsync<T>(
		List<ChatMessage> messages,
		int localTimeoutSeconds,
		int cloudTimeoutSeconds,
		CancellationToken cancellationToken) where T : class
	{
		// Try local client first if available and not disabled
		if (_localClient != null && !_localClientDisabled)
		{
			try
			{
				_logger.LogDebug("Attempting on-device AI request for type {ResponseType}", typeof(T).Name);

				using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				localCts.CancelAfter(TimeSpan.FromSeconds(localTimeoutSeconds));

				var localResponse = await _localClient.GetResponseAsync<T>(
					messages,
					cancellationToken: localCts.Token);

				if (localResponse?.Result != null)
				{
					_logger.LogDebug("On-device AI request succeeded for type {ResponseType}", typeof(T).Name);
					return (localResponse.Result, SourceOnDevice);
				}
			}
			catch (Exception ex)
			{
				_localClientDisabled = true;
				_logger.LogWarning(ex, "On-device AI failed for type {ResponseType}, disabling for session. Falling back to Azure OpenAI.", typeof(T).Name);
			}
		}

		// Try Azure OpenAI as fallback
		var azureClient = GetOrCreateAzureOpenAIClient();
		if (azureClient != null)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				_logger.LogDebug("Skipping Azure OpenAI fallback — request was cancelled");
				return (null, null);
			}

			try
			{
				_logger.LogDebug("Attempting Azure OpenAI request for type {ResponseType}", typeof(T).Name);

				using var cloudCts = new CancellationTokenSource();
				cloudCts.CancelAfter(TimeSpan.FromSeconds(cloudTimeoutSeconds));
				using var registration = cancellationToken.Register(() => cloudCts.Cancel());

				var cloudResponse = await azureClient.GetResponseAsync<T>(
					messages,
					cancellationToken: cloudCts.Token);

				if (cloudResponse?.Result != null)
				{
					_logger.LogDebug("Azure OpenAI request succeeded for type {ResponseType}", typeof(T).Name);
					return (cloudResponse.Result, SourceCloud);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Azure OpenAI request failed for type {ResponseType}", typeof(T).Name);
				throw;
			}
		}

		return (null, null);
	}

	/// <summary>
	/// Tries to get a text response from available AI clients with fallback.
	/// </summary>
	private async Task<(string? Response, string? Source)> TryGetResponseWithFallbackAsync(
		List<ChatMessage> messages,
		int localTimeoutSeconds,
		int cloudTimeoutSeconds,
		CancellationToken cancellationToken)
	{
		// Try local client first if available and not disabled
		if (_localClient != null && !_localClientDisabled)
		{
			try
			{
				_logger.LogDebug("Attempting on-device AI request");

				using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				localCts.CancelAfter(TimeSpan.FromSeconds(localTimeoutSeconds));

				var localResponse = await _localClient.GetResponseAsync(
					messages,
					cancellationToken: localCts.Token);

				if (!string.IsNullOrWhiteSpace(localResponse?.Text))
				{
					_logger.LogDebug("On-device AI request succeeded");
					return (localResponse.Text, SourceOnDevice);
				}
			}
			catch (Exception ex)
			{
				_localClientDisabled = true;
				_logger.LogWarning(ex, "On-device AI failed, disabling for session. Falling back to Azure OpenAI.");
			}
		}

		// Try Azure OpenAI as fallback
		var azureClient = GetOrCreateAzureOpenAIClient();
		if (azureClient != null)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				_logger.LogDebug("Skipping Azure OpenAI fallback — request was cancelled");
				return (null, null);
			}

			try
			{
				_logger.LogDebug("Attempting Azure OpenAI request");

				using var cloudCts = new CancellationTokenSource();
				cloudCts.CancelAfter(TimeSpan.FromSeconds(cloudTimeoutSeconds));
				using var registration = cancellationToken.Register(() => cloudCts.Cancel());

				var cloudResponse = await azureClient.GetResponseAsync(
					messages,
					cancellationToken: cloudCts.Token);

				if (!string.IsNullOrWhiteSpace(cloudResponse?.Text))
				{
					_logger.LogDebug("Azure OpenAI request succeeded");
					return (cloudResponse.Text, SourceCloud);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Azure OpenAI request failed");
				throw;
			}
		}

		return (null, null);
	}

	/// <summary>
	/// Gets or creates the Azure OpenAI client instance.
	/// </summary>
	private IChatClient? GetOrCreateAzureOpenAIClient()
	{
		if (_azureOpenAIClient != null)
			return _azureOpenAIClient;

		var endpoint = _configuration["AzureOpenAI:Endpoint"];
		var apiKey = _configuration["AzureOpenAI:ApiKey"];

		if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
			return null;

		try
		{
			var azureClient = new AzureOpenAIClient(
				new Uri(endpoint),
				new ApiKeyCredential(apiKey));
			_azureOpenAIClient = azureClient.GetChatClient(ModelId).AsIChatClient();
			return _azureOpenAIClient;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to create Azure OpenAI client");
			return null;
		}
	}
}
