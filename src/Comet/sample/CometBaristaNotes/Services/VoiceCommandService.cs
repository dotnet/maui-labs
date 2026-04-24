using System.ClientModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CometBaristaNotes.Services;

// Force en-US culture for number parsing throughout this service
internal static class VoiceCommandCulture
{
	internal static readonly CultureInfo EnUS = CultureInfo.GetCultureInfo("en-US");
}

/// <summary>
/// Service for processing voice commands using AI tool calling.
/// Supports on-device AI (Apple Intelligence) with Azure OpenAI fallback.
/// </summary>
public class VoiceCommandService : IVoiceCommandService
{
	private readonly IShotService _shotService;
	private readonly IBeanService _beanService;
	private readonly IBagService _bagService;
	private readonly IEquipmentService _equipmentService;
	private readonly IUserProfileService _userProfileService;
	private readonly IDataChangeNotifier _dataChangeNotifier;
	private readonly INavigationRegistry _navigationRegistry;
	private readonly IConfiguration _configuration;
	private readonly ILogger<VoiceCommandService> _logger;
	private readonly IVisionService? _visionService;

	// Injected on-device client (from AddPlatformChatClient)
	private readonly IChatClient? _localClient;

	// Azure OpenAI client created on demand
	private IChatClient? _azureOpenAIClient;

	// Session-level flag: once local client fails, don't retry until app restart
	private bool _localClientDisabled = false;

	// Conversation history for the current voice session
	private readonly List<ChatMessage> _conversationHistory = new();

	// Events for speech coordination
	public event EventHandler? PauseSpeechRequested;
	public event EventHandler? ResumeSpeechRequested;

	private const string ModelId = "gpt-4.1-mini";
	private const int LocalTimeoutSeconds = 15;
	private const int CloudTimeoutSeconds = 30;

	private const string SystemPrompt = """
		You are BaristaNotes voice assistant. Help users log espresso shots, manage their coffee data, and query their shot history.

		CONTEXT:
		- Rating scale is 0-4 (0=terrible, 1=bad, 2=average, 3=good, 4=excellent)
		- Common terms: dose (coffee in), yield/output (coffee out), pull time, extraction, grind size
		- "Pretty good" = rating 3, "excellent/amazing" = rating 4, "not great/meh" = rating 2, "okay" = rating 2
		- "this morning" or "last shot" refers to most recent shot
		- A "bean" is a type of coffee (e.g., "Ethiopia Yirgacheffe", "Prologue Blend")
		- A "bag" is a physical bag of a bean with a specific roast date

		SPEECH RECOGNITION CORRECTIONS (the user likely meant):
		- "crime" or "grand" or "grime" or "grimes" or "Ryan" → "grind" (as in grind size)
		- "does" or "those" → "dose" (as in coffee dose)
		- "pulled" or "pool" → "pull" (as in pull time)
		- "yelled" or "yeild" → "yield" (as in coffee yield/output)
		- "story" or "Storey" → "Storyville" (coffee roaster)
		- "pro" or "prolog" → "Prologue" (coffee name)
		- "extra action" → "extraction"
		- "grams" may be heard as "grants" or "grands"
		- "be" or "being" or "beings" or "beams" or "beam" or "bead" or "beat" → "bean" or "beans" (coffee beans)
		- "bags" may be heard as "back" or "backs"
		When you see these misrecognitions, interpret them as the coffee terms.

		INTENT DETECTION - CRITICAL:
		Distinguish between NAVIGATION intent and QUERY intent based on how the user phrases their request:

		NAVIGATION INTENT ("show me", "take me to", "go to", "open", "navigate to"):
		- "Show me the user profiles" → NavigateTo 'profiles' page
		- "Show me my beans" → NavigateTo 'beans' page
		- "Take me to settings" → NavigateTo 'settings' page
		- "Open my equipment" → NavigateTo 'equipment' page
		- "Go to activity" → NavigateTo 'history' page
		- "Show me my shot history" → NavigateTo 'history' page
		For navigation requests, use GetAvailablePages if unsure, then NavigateTo the appropriate page.

		QUERY INTENT ("what", "how many", "list", "tell me", "find"):
		- "What user profiles do I have?" → Use FindProfiles, return text response
		- "How many shots today?" → Use GetShotCount, return text response
		- "What was my last shot?" → Use GetLastShot, return text response
		- "List my beans" → Use FindBeans, return text response
		After answering a query, you may offer: "Would you like me to show you the [X] page?"

		HYBRID (query + specific item → navigate to detail if possible):
		- "Show me the last shot I made" → Use GetLastShot to get the shot ID, then call NavigateToShotDetail(shotId)
		- "Show me Angie's profile" → Use FindProfiles(name="Angie") to get the profile ID, then call NavigateToProfileDetail(profileId)

		QUERY CAPABILITIES:
		Shots:
		- "How many shots?" or "How many shots made by David?" - use GetShotCount for counting
		- "What was my last shot?" - returns full details of most recent shot
		- "Find shots with Ethiopia" - searches by bean name (returns list)
		- "What shots did I make for Sarah?" - filters by made for (returns list)
		- "Find my shots from this week" - filters by time period (returns list)
		
		Beans:
		- "How many beans have I tried?" - counts unique beans
		- "What beans from Storyville have I used?" - lists beans by roaster
		- "Find Ethiopian beans" - searches beans by origin
		- "What beans do I have?" - lists all beans
		
		Bags:
		- "How many bags do I have?" - counts bags (active and completed)
		- "What bags are active?" - lists current/active bags
		- "Find bags of Prologue" - searches bags by bean name
		
		Equipment:
		- "How many pieces of equipment do I have?" - counts equipment
		- "What grinders do I have?" - lists equipment by type (machine, grinder, tamper, puck screen, other)
		- "What equipment do I have?" - lists all equipment
		- "Find equipment named Niche" - searches equipment by name
		
		Profiles:
		- "How many profiles are there?" - counts user profiles
		- "What profiles do I have?" - lists all user profiles
		- "Find profile named David" - searches profiles by name

		VISION/CAMERA CAPABILITIES:
		When the user asks you to "look at", "see", "count people in", or asks about coffee needs for a group/room:
		- "Look at this room and tell me how many cups of coffee I need" → Use AnalyzeRoomForCoffee tool
		- "Count the people here" → Use AnalyzeRoomForCoffee tool
		- "How many cups of coffee do I need to make?" → Use AnalyzeRoomForCoffee tool
		The tool will take a photo and analyze it to count people, then calculate coffee needs (18g per person).

		RULES:
		1. Always use available tools to complete actions immediately
		2. NEVER ask follow-up questions - use defaults for missing optional values
		3. For shots: dose, output, and time are required. Rating defaults to 2 (average) if not specified
		4. Execute the tool immediately with provided values, don't ask for confirmation
		5. Keep responses concise - just confirm what was done in one sentence
		6. For queries, return the information directly from the tool response
		7. For "show me" requests about a category (profiles, beans, etc.), NAVIGATE to that page
		8. For "what/how many" questions, provide a TEXT response
		9. For vision/camera requests (look at, count people, coffee for group), use AnalyzeRoomForCoffee
		""";

	public VoiceCommandService(
		IShotService shotService,
		IBeanService beanService,
		IBagService bagService,
		IEquipmentService equipmentService,
		IUserProfileService userProfileService,
		IDataChangeNotifier dataChangeNotifier,
		INavigationRegistry navigationRegistry,
		IConfiguration configuration,
		ILogger<VoiceCommandService> logger,
		IChatClient? chatClient = null,
		IVisionService? visionService = null)
	{
		_shotService = shotService;
		_beanService = beanService;
		_bagService = bagService;
		_equipmentService = equipmentService;
		_userProfileService = userProfileService;
		_dataChangeNotifier = dataChangeNotifier;
		_navigationRegistry = navigationRegistry;
		_configuration = configuration;
		_logger = logger;
		_localClient = chatClient;
		_visionService = visionService;
	}

	/// <inheritdoc />
	public async Task<VoiceCommandResponseDto> InterpretCommandAsync(
		VoiceCommandRequestDto request,
		CancellationToken cancellationToken = default)
	{
		var normalizedTranscript = NormalizeTranscript(request.Transcript);
		_logger.LogDebug("Interpreting command: {Transcript} (normalized: {Normalized})",
			request.Transcript, normalizedTranscript);

		return await InterpretCommandInternalAsync(normalizedTranscript, forceAzure: false, cancellationToken);
	}

	private async Task<VoiceCommandResponseDto> InterpretCommandInternalAsync(
		string normalizedTranscript,
		bool forceAzure,
		CancellationToken cancellationToken)
	{
		try
		{
			var client = GetChatClientWithTools(forceAzure);
			if (client == null)
			{
				return new VoiceCommandResponseDto
				{
					Intent = CommandIntent.Unknown,
					ErrorMessage = "Voice commands are temporarily unavailable."
				};
			}

			// Build messages with conversation history for context
			var messages = new List<ChatMessage>
			{
				new(ChatRole.System, SystemPrompt)
			};

			// Add conversation history (keeps context from previous exchanges in this session)
			messages.AddRange(_conversationHistory);

			// Add the new user message
			var userMessage = new ChatMessage(ChatRole.User, normalizedTranscript);
			messages.Add(userMessage);

			_logger.LogDebug("Sending request with {HistoryCount} history messages", _conversationHistory.Count);

			var chatOptions = CreateChatOptions();
			var response = await client.GetResponseAsync(messages, chatOptions, cancellationToken);

			// Store the exchange in conversation history for future context
			_conversationHistory.Add(userMessage);
			_conversationHistory.Add(new ChatMessage(ChatRole.Assistant, response.Text ?? "Command processed."));

			// Limit history to last 20 exchanges (40 messages) to avoid token limits
			while (_conversationHistory.Count > 40)
			{
				_conversationHistory.RemoveAt(0);
				_conversationHistory.RemoveAt(0);
			}

			return new VoiceCommandResponseDto
			{
				Intent = CommandIntent.Unknown, // Intent determined by tool call
				ConfirmationMessage = response.Text ?? "Command processed.",
				RequiresConfirmation = false
			};
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Voice command interpretation cancelled");
			return new VoiceCommandResponseDto
			{
				Intent = CommandIntent.Cancel,
				ErrorMessage = "Cancelled"
			};
		}
		catch (Exception ex) when (!forceAzure && IsLocalAIToolCallingError(ex))
		{
			// Local AI (Apple Intelligence) failed — disable and retry with Azure OpenAI
			_logger.LogWarning(ex, "Local AI failed with tool calling error, falling back to Azure OpenAI. Error: {Message}", ex.Message);
			_localClientDisabled = true;

			return await InterpretCommandInternalAsync(normalizedTranscript, forceAzure: true, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error interpreting voice command: {Transcript}", normalizedTranscript);

			var errorMessage = ex switch
			{
				HttpRequestException => "Network error. Please check your connection and try again.",
				TaskCanceledException => "Request timed out. Please try again.",
				_ when ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) =>
					"Voice commands require Azure OpenAI configuration. Please check settings.",
				_ when ex.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
				       ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) =>
					"Invalid API key. Please check your Azure OpenAI configuration.",
				_ when ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
				       ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) =>
					"Too many requests. Please wait a moment and try again.",
				_ => $"Sorry, I couldn't process that command. Error: {ex.Message}"
			};

			return new VoiceCommandResponseDto
			{
				Intent = CommandIntent.Unknown,
				ErrorMessage = errorMessage
			};
		}
	}

	/// <inheritdoc />
	public Task<VoiceToolResultDto> ExecuteCommandAsync(
		VoiceCommandResponseDto response,
		CancellationToken cancellationToken = default)
	{
		// Tool execution happens automatically via UseFunctionInvocation()
		return Task.FromResult(new VoiceToolResultDto
		{
			Success = true,
			Message = response.ConfirmationMessage
		});
	}

	/// <inheritdoc />
	public async Task<VoiceToolResultDto> ProcessCommandAsync(
		VoiceCommandRequestDto request,
		CancellationToken cancellationToken = default)
	{
		var interpretation = await InterpretCommandAsync(request, cancellationToken);

		if (!string.IsNullOrEmpty(interpretation.ErrorMessage))
		{
			return new VoiceToolResultDto
			{
				Success = false,
				Message = interpretation.ErrorMessage
			};
		}

		return new VoiceToolResultDto
		{
			Success = true,
			Message = interpretation.ConfirmationMessage
		};
	}

	/// <inheritdoc />
	public void ClearConversationHistory()
	{
		_conversationHistory.Clear();
		_logger.LogDebug("Conversation history cleared");
	}

	private IChatClient? GetChatClientWithTools(bool forceAzure = false)
	{
		// TEMPORARY: Skip Apple Intelligence until assets are available
		if (!forceAzure && _localClient != null && !_localClientDisabled && false) // Disabled for now
		{
			_logger.LogDebug("Using local AI (Apple Intelligence) for voice commands with tool calling");
			return new ChatClientBuilder(_localClient)
				.Build();
		}

		if (_localClientDisabled)
		{
			_logger.LogDebug("Local AI disabled for this session, using Azure OpenAI fallback");
		}

		// Use Azure OpenAI which supports function calling
		var azureClient = GetOrCreateAzureOpenAIClient();
		if (azureClient != null)
		{
			_logger.LogDebug("Using Azure OpenAI for voice commands");
			return new ChatClientBuilder(azureClient)
				.UseFunctionInvocation()
				.Build();
		}

		_logger.LogWarning("No AI client available for voice commands. Configure AzureOpenAI:Endpoint and AzureOpenAI:ApiKey in settings.");
		return null;
	}

	private IChatClient? GetOrCreateAzureOpenAIClient()
	{
		if (_azureOpenAIClient != null)
		{
			return _azureOpenAIClient;
		}

		var endpoint = _configuration["AzureOpenAI:Endpoint"];
		var apiKey = _configuration["AzureOpenAI:ApiKey"];

		if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
		{
			return null;
		}

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

	/// <summary>
	/// Determines if an exception indicates the local AI (Apple Intelligence) failed
	/// in a way that warrants falling back to Azure OpenAI.
	/// </summary>
	private bool IsLocalAIToolCallingError(Exception ex)
	{
		var fullText = ex.ToString();

		if (fullText.Contains("Model assets are unavailable", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("ChatClientNative", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("NSLocalizedDescription", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("assets have finished downloading", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("com.apple.modelcatalog", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("UnifiedAssetFramework", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("no underlying assets", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("Model Catalog error", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("TokenGenerator", StringComparison.OrdinalIgnoreCase) ||
		    fullText.Contains("ModelManagerError", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (ex is ArgumentException argEx &&
		    (argEx.Message.Contains("FunctionCall", StringComparison.OrdinalIgnoreCase) ||
		     argEx.Message.Contains("tool", StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (ex is NotSupportedException || ex is PlatformNotSupportedException)
		{
			return true;
		}

		if (ex is InvalidOperationException invalidOp &&
		    (invalidOp.Message.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
		     invalidOp.Message.Contains("Intelligence", StringComparison.OrdinalIgnoreCase) ||
		     invalidOp.Message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
		     invalidOp.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (ex.InnerException != null && IsLocalAIToolCallingError(ex.InnerException))
		{
			return true;
		}

		var message = ex.Message;
		if (message.Contains("tool calling", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("function calling", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("Apple Intelligence", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("Model assets are unavailable", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("ChatClientNative", StringComparison.OrdinalIgnoreCase) ||
		    message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
		    (message.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
		     message.Contains("function", StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Normalizes speech-to-text transcript to fix common issues like mixed number formats
	/// and misrecognized coffee terminology.
	/// </summary>
	private string NormalizeTranscript(string transcript)
	{
		if (string.IsNullOrWhiteSpace(transcript))
			return transcript;

		var result = transcript;

		// COFFEE VOCABULARY NORMALIZATION: Fix commonly misrecognized coffee terms
		var coffeeVocabularyFixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Grind variations
			{"grand", "grind"}, {"grin", "grind"}, {"grined", "grind"},
			{"grinding", "grind"}, {"ground", "grind"},

			// Dose variations
			{"doze", "dose"}, {"those", "dose"}, {"doughs", "dose"},

			// Espresso variations
			{"expresso", "espresso"}, {"express oh", "espresso"}, {"s presso", "espresso"},

			// Yield variations
			{"yelled", "yield"}, {"yeild", "yield"},

			// Shot variations
			{"short", "shot"}, {"shut", "shot"},

			// Extraction variations
			{"extra action", "extraction"}, {"extra shin", "extraction"},

			// Portafilter/tamper/puck variations
			{"porta filter", "portafilter"}, {"port a filter", "portafilter"}, {"quarter filter", "portafilter"},
			{"temper", "tamper"}, {"tapper", "tamper"},
			{"pock", "puck"}, {"pack", "puck"},

			// Crema/bloom variations
			{"cream a", "crema"}, {"creamer", "crema"}, {"blue", "bloom"},

			// Pre-infusion
			{"pre infusion", "preinfusion"}, {"pre-infusion", "preinfusion"},

			// Rating/stars
			{"store", "star"}, {"stores", "stars"}, {"stare", "star"}, {"stares", "stars"},

			// Common bean origins
			{"ethiopia", "Ethiopia"}, {"ethiopian", "Ethiopian"},
			{"columbia", "Colombia"}, {"columbian", "Colombian"},
			{"brazilian", "Brazilian"}, {"brazil", "Brazil"},
			{"guatemalan", "Guatemalan"}, {"guatemala", "Guatemala"},
			{"costa rican", "Costa Rican"}, {"costa rica", "Costa Rica"},
			{"kenyan", "Kenyan"}, {"kenya", "Kenya"},
			{"sumatran", "Sumatran"}, {"sumatra", "Sumatra"},
			{"yirgacheffe", "Yirgacheffe"}, {"your gosh if", "Yirgacheffe"}, {"your gosh if a", "Yirgacheffe"},

			// Common roasters
			{"counter culture", "Counter Culture"}, {"blue bottle", "Blue Bottle"},
			{"stumped town", "Stumptown"}, {"stump town", "Stumptown"},
			{"intelligencia", "Intelligentsia"}, {"intelligence ya", "Intelligentsia"},
		};

		// Apply vocabulary fixes (word boundary aware)
		foreach (var (misheard, correct) in coffeeVocabularyFixes)
		{
			result = Regex.Replace(result, $@"\b{Regex.Escape(misheard)}\b", correct, RegexOptions.IgnoreCase);
		}

		// iOS speech recognition quirk: splits compound numbers like "34" into "30 4"
		result = Regex.Replace(result, @"\b([2-9])0\s+([1-9])\b",
			m => $"{m.Groups[1].Value}{m.Groups[2].Value}");

		// Also handle: "30 four" -> "34" (tens digit + space + word digit)
		var onesWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{"one", "1"}, {"two", "2"}, {"three", "3"}, {"four", "4"}, {"five", "5"},
			{"six", "6"}, {"seven", "7"}, {"eight", "8"}, {"nine", "9"}
		};
		foreach (var (word, digit) in onesWords)
		{
			result = Regex.Replace(result, $@"\b([2-9])0\s+{word}\b",
				m => $"{m.Groups[1].Value}{digit}", RegexOptions.IgnoreCase);
		}

		// Simple word-number conversion for common spoken numbers
		var wordNumbers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{"zero", "0"}, {"one", "1"}, {"two", "2"}, {"three", "3"}, {"four", "4"},
			{"five", "5"}, {"six", "6"}, {"seven", "7"}, {"eight", "8"}, {"nine", "9"},
			{"ten", "10"}, {"eleven", "11"}, {"twelve", "12"}, {"thirteen", "13"},
			{"fourteen", "14"}, {"fifteen", "15"}, {"sixteen", "16"}, {"seventeen", "17"},
			{"eighteen", "18"}, {"nineteen", "19"}, {"twenty", "20"}, {"thirty", "30"},
			{"forty", "40"}, {"fifty", "50"}, {"sixty", "60"}, {"seventy", "70"},
			{"eighty", "80"}, {"ninety", "90"}
		};

		foreach (var (word, number) in wordNumbers)
		{
			result = Regex.Replace(result, $@"\b{Regex.Escape(word)}\b", number, RegexOptions.IgnoreCase);
		}

		// Clean up multiple spaces
		result = Regex.Replace(result, @"\s+", " ").Trim();

		_logger.LogDebug("Normalized transcript: '{Original}' -> '{Normalized}'", transcript, result);

		return result;
	}

	private ChatOptions CreateChatOptions()
	{
		return new ChatOptions
		{
			Tools =
			[
				AIFunctionFactory.Create(LogShotToolAsync),
				AIFunctionFactory.Create(AddBeanToolAsync),
				AIFunctionFactory.Create(AddBagToolAsync),
				AIFunctionFactory.Create(RateLastShotToolAsync),
				AIFunctionFactory.Create(AddTastingNotesToolAsync),
				AIFunctionFactory.Create(AddEquipmentToolAsync),
				AIFunctionFactory.Create(AddProfileToolAsync),
				AIFunctionFactory.Create(GetShotCountToolAsync),
				AIFunctionFactory.Create(GetAvailablePagesToolAsync),
				AIFunctionFactory.Create(NavigateToToolAsync),
				AIFunctionFactory.Create(NavigateToShotDetailToolAsync),
				AIFunctionFactory.Create(NavigateToProfileDetailToolAsync),
				AIFunctionFactory.Create(FilterShotsToolAsync),
				AIFunctionFactory.Create(GetLastShotToolAsync),
				AIFunctionFactory.Create(FindShotsToolAsync),
				AIFunctionFactory.Create(GetBeanCountToolAsync),
				AIFunctionFactory.Create(FindBeansToolAsync),
				AIFunctionFactory.Create(GetBagCountToolAsync),
				AIFunctionFactory.Create(FindBagsToolAsync),
				AIFunctionFactory.Create(GetEquipmentCountToolAsync),
				AIFunctionFactory.Create(FindEquipmentToolAsync),
				AIFunctionFactory.Create(GetProfileCountToolAsync),
				AIFunctionFactory.Create(FindProfilesToolAsync),
				AIFunctionFactory.Create(AnalyzeRoomForCoffeeToolAsync),
			]
		};
	}

	#region Tool Functions

	[Description("Logs a new espresso shot with the specified parameters")]
	private Task<string> LogShotToolAsync(
		[Description("Coffee dose in grams (input weight)")] double doseGrams,
		[Description("Coffee output/yield in grams")] double outputGrams,
		[Description("Extraction time in seconds")] int timeSeconds,
		[Description("Shot rating from 0-4 (0=terrible, 4=excellent)")] int? rating = null,
		[Description("Tasting notes describing the shot flavor")] string? tastingNotes = null)
	{
		_logger.LogInformation("LogShot tool called: {Dose}g in, {Output}g out, {Time}s, rating: {Rating}",
			doseGrams, outputGrams, timeSeconds, rating);

		try
		{
			if (doseGrams <= 0 || outputGrams <= 0 || timeSeconds <= 0)
			{
				return Task.FromResult("Please provide valid dose, output, and time values.");
			}

			if (rating.HasValue && (rating < 0 || rating > 4))
			{
				return Task.FromResult("Rating must be between 0 and 4.");
			}

			// Get the most recently used active bag as default
			var allBags = _bagService.GetAllBags().Where(b => b.IsActive && !b.IsComplete).ToList();
			var defaultBag = allBags.FirstOrDefault();

			if (defaultBag == null)
			{
				return Task.FromResult("No active coffee bag found. Please add a bag first before logging shots.");
			}

			// Get the most recent shot to inherit defaults
			var allShots = _shotService.GetAllShots();
			var lastShot = allShots.OrderByDescending(s => s.Timestamp).FirstOrDefault();

			// Convert 0-4 scale to service 1-5 scale; default to rating 2 (average)
			var serviceRating = rating.HasValue ? rating.Value + 1 : 3;

			var newShot = new ShotRecord
			{
				Timestamp = DateTime.Now,
				BagId = defaultBag.Id,
				DoseIn = (decimal)doseGrams,
				ExpectedOutput = (decimal)outputGrams,
				ExpectedTime = (decimal)timeSeconds,
				ActualOutput = (decimal)outputGrams,
				ActualTime = (decimal)timeSeconds,
				Rating = serviceRating,
				TastingNotes = tastingNotes,
				GrindSetting = lastShot?.GrindSetting ?? "5.5",
				DrinkType = lastShot?.DrinkType ?? "Espresso",
				MadeById = lastShot?.MadeById,
				MadeForId = lastShot?.MadeForId,
				MachineId = lastShot?.MachineId,
				GrinderId = lastShot?.GrinderId,
			};

			var shot = _shotService.CreateShot(newShot);
			_dataChangeNotifier.NotifyChange("Shot", shot.Id, DataChangeType.Created);

			var result = $"Shot logged: {doseGrams}g → {outputGrams}g in {timeSeconds}s";
			if (rating.HasValue)
			{
				result += $", rated {rating}/4";
			}
			result += $" (using {defaultBag.BeanName ?? "current bag"})";

			_logger.LogInformation("Shot logged successfully via voice, ID: {ShotId}, BagId: {BagId}",
				shot.Id, defaultBag.Id);
			return Task.FromResult(result);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error logging shot via voice");
			return Task.FromResult("Sorry, I couldn't log that shot. Please try again.");
		}
	}

	[Description("Creates a new coffee bean entry")]
	private Task<string> AddBeanToolAsync(
		[Description("Name of the coffee bean")] string name,
		[Description("Roaster company name")] string? roaster = null,
		[Description("Origin country or region")] string? origin = null)
	{
		_logger.LogInformation("AddBean tool called: {Name} from {Roaster}, origin: {Origin}",
			name, roaster, origin);

		try
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return Task.FromResult("Please provide a bean name.");
			}

			var newBean = new Bean
			{
				Name = name,
				Roaster = roaster,
				Origin = origin,
				IsActive = true,
				CreatedAt = DateTime.Now
			};

			var bean = _beanService.CreateBean(newBean);
			_dataChangeNotifier.NotifyChange("Bean", bean.Id, DataChangeType.Created);

			var result = $"Added bean: {name}";
			if (!string.IsNullOrEmpty(roaster))
			{
				result += $" from {roaster}";
			}

			_logger.LogInformation("Bean created successfully via voice: {Name}, ID: {BeanId}", name, bean.Id);
			return Task.FromResult(result);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating bean via voice");
			return Task.FromResult("Sorry, I couldn't add that bean. Please try again.");
		}
	}

	[Description("Creates a new bag of an existing coffee bean")]
	private Task<string> AddBagToolAsync(
		[Description("Name of the bean for this bag")] string beanName,
		[Description("Roast date (YYYY-MM-DD, 'today', 'yesterday', or days ago like '3 days ago')")] string? roastDate = null)
	{
		_logger.LogInformation("AddBag tool called: {Bean}, roasted {Date}", beanName, roastDate);

		try
		{
			if (string.IsNullOrWhiteSpace(beanName))
			{
				return Task.FromResult("Please provide the bean name for this bag.");
			}

			// Find bean by name (case-insensitive)
			var beans = _beanService.GetAllBeans();
			var matchingBean = beans.FirstOrDefault(b =>
				b.Name.Equals(beanName, StringComparison.OrdinalIgnoreCase));

			if (matchingBean == null)
			{
				return Task.FromResult($"Bean '{beanName}' not found. Please add the bean first or check the name.");
			}

			var parsedDate = ParseRoastDate(roastDate);

			var newBag = new Bag
			{
				BeanId = matchingBean.Id,
				BeanName = matchingBean.Name,
				RoastDate = parsedDate,
				IsActive = true,
				IsComplete = false,
				CreatedAt = DateTime.Now
			};

			var bag = _bagService.CreateBag(newBag);
			_dataChangeNotifier.NotifyChange("Bag", bag.Id, DataChangeType.Created);

			var result = $"Added bag of {beanName}";
			if (!string.IsNullOrEmpty(roastDate))
			{
				result += $" roasted {parsedDate:MMM d}";
			}

			_logger.LogInformation("Bag created successfully via voice for bean: {BeanName}, ID: {BagId}", beanName, bag.Id);
			return Task.FromResult(result);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating bag via voice");
			return Task.FromResult("Sorry, I couldn't add that bag. Please try again.");
		}
	}

	private static DateTime ParseRoastDate(string? roastDate)
	{
		if (string.IsNullOrEmpty(roastDate))
			return DateTime.Today;

		var lowerDate = roastDate.ToLowerInvariant().Trim();

		if (lowerDate == "today")
			return DateTime.Today;

		if (lowerDate == "yesterday")
			return DateTime.Today.AddDays(-1);

		if (lowerDate.Contains("days ago"))
		{
			var parts = lowerDate.Replace("days ago", "").Trim().Split(' ');
			if (int.TryParse(parts.LastOrDefault(), NumberStyles.Integer, VoiceCommandCulture.EnUS, out var daysAgo))
				return DateTime.Today.AddDays(-daysAgo);
		}

		if (DateTime.TryParse(roastDate, VoiceCommandCulture.EnUS, DateTimeStyles.None, out var parsed))
			return parsed;

		return DateTime.Today;
	}

	[Description("Rates the most recently logged shot")]
	private Task<string> RateLastShotToolAsync(
		[Description("Rating from 0-4 (0=terrible, 4=excellent)")] int rating)
	{
		_logger.LogInformation("RateLastShot tool called: {Rating}", rating);

		try
		{
			if (rating < 0 || rating > 4)
			{
				return Task.FromResult("Rating must be between 0 and 4.");
			}

			var allShots = _shotService.GetAllShots();
			var lastShot = allShots.OrderByDescending(s => s.Timestamp).FirstOrDefault();
			if (lastShot == null)
			{
				return Task.FromResult("No shots found to rate. Log a shot first.");
			}

			// Convert 0-4 scale to service 1-5 scale
			lastShot.Rating = rating + 1;
			_shotService.UpdateShot(lastShot);
			_dataChangeNotifier.NotifyChange("Shot", lastShot.Id, DataChangeType.Updated);

			_logger.LogInformation("Last shot rated via voice: {Rating}, shot ID: {ShotId}", rating, lastShot.Id);
			return Task.FromResult($"Rated your last shot {rating}/4");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error rating shot via voice");
			return Task.FromResult("Sorry, I couldn't rate that shot. Please try again.");
		}
	}

	[Description("Adds tasting notes to the most recently logged shot")]
	private Task<string> AddTastingNotesToolAsync(
		[Description("Tasting notes describing flavor, body, acidity, etc.")] string notes)
	{
		_logger.LogInformation("AddTastingNotes tool called: {Notes}", notes);

		try
		{
			if (string.IsNullOrWhiteSpace(notes))
			{
				return Task.FromResult("Please provide tasting notes to add.");
			}

			var allShots = _shotService.GetAllShots();
			var lastShot = allShots.OrderByDescending(s => s.Timestamp).FirstOrDefault();
			if (lastShot == null)
			{
				return Task.FromResult("No shots found to add notes to. Log a shot first.");
			}

			var existingNotes = lastShot.TastingNotes;
			lastShot.TastingNotes = string.IsNullOrEmpty(existingNotes)
				? notes
				: $"{existingNotes}; {notes}";

			_shotService.UpdateShot(lastShot);
			_dataChangeNotifier.NotifyChange("Shot", lastShot.Id, DataChangeType.Updated);

			_logger.LogInformation("Tasting notes added via voice to shot ID: {ShotId}", lastShot.Id);
			return Task.FromResult($"Added tasting notes to your last shot: \"{notes}\"");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error adding tasting notes via voice");
			return Task.FromResult("Sorry, I couldn't add those tasting notes. Please try again.");
		}
	}

	[Description("Creates new coffee equipment (machine, grinder, tamper, etc.)")]
	private Task<string> AddEquipmentToolAsync(
		[Description("Equipment name")] string name,
		[Description("Type of equipment: 'machine', 'grinder', 'tamper', 'puckscreen', or 'other'")] string type)
	{
		_logger.LogInformation("AddEquipment tool called: {Name} ({Type})", name, type);

		try
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return Task.FromResult("Please provide an equipment name.");
			}

			var equipmentType = ParseEquipmentType(type);
			var newEquipment = new Equipment
			{
				Name = name,
				Type = equipmentType,
				IsActive = true,
				CreatedAt = DateTime.Now
			};

			var equipment = _equipmentService.CreateEquipment(newEquipment);
			_dataChangeNotifier.NotifyChange("Equipment", equipment.Id, DataChangeType.Created);

			_logger.LogInformation("Equipment created successfully via voice: {Name}, ID: {EquipmentId}", name, equipment.Id);
			return Task.FromResult($"Added {type}: {name}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating equipment via voice");
			return Task.FromResult("Sorry, I couldn't add that equipment. Please try again.");
		}
	}

	private static EquipmentType ParseEquipmentType(string type)
	{
		var lowerType = type?.ToLowerInvariant().Trim() ?? "";

		return lowerType switch
		{
			"machine" or "espresso machine" => EquipmentType.Machine,
			"grinder" or "coffee grinder" => EquipmentType.Grinder,
			"tamper" => EquipmentType.Tamper,
			"puckscreen" or "puck screen" => EquipmentType.PuckScreen,
			_ => EquipmentType.Other
		};
	}

	[Description("Creates a new user profile")]
	private Task<string> AddProfileToolAsync(
		[Description("Person's name")] string name)
	{
		_logger.LogInformation("AddProfile tool called: {Name}", name);

		try
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return Task.FromResult("Please provide a name for the profile.");
			}

			var newProfile = new UserProfile
			{
				Name = name,
				CreatedAt = DateTime.Now
			};

			var profile = _userProfileService.CreateProfile(newProfile);
			_dataChangeNotifier.NotifyChange("Profile", profile.Id, DataChangeType.Created);

			_logger.LogInformation("Profile created successfully via voice: {Name}, ID: {ProfileId}", name, profile.Id);
			return Task.FromResult($"Added profile for {name}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating profile via voice");
			return Task.FromResult("Sorry, I couldn't add that profile. Please try again.");
		}
	}

	[Description("Gets shot count for a time period")]
	private Task<string> GetShotCountToolAsync(
		[Description("Period: 'today', 'this week', 'this month', or 'all time' (default 'all time')")] string period = "all time",
		[Description("Filter by bean name")] string? beanName = null,
		[Description("Filter by who made the shot")] string? madeBy = null,
		[Description("Filter by who the shot was made for")] string? madeFor = null,
		[Description("Filter by minimum rating (0-4)")] int? minRating = null)
	{
		_logger.LogInformation("GetShotCount tool called: Period={Period}, Bean={Bean}, MadeBy={MadeBy}, MadeFor={MadeFor}, MinRating={MinRating}",
			period, beanName, madeBy, madeFor, minRating);

		try
		{
			var allShots = _shotService.GetAllShots();
			var now = DateTime.Now;
			var shots = allShots.AsEnumerable();

			var lowerPeriod = period?.ToLowerInvariant().Trim() ?? "all time";
			switch (lowerPeriod)
			{
				case "today":
					shots = shots.Where(s => s.Timestamp.Date == now.Date);
					break;
				case "this week" or "week":
					var weekStart = now.AddDays(-(int)now.DayOfWeek);
					shots = shots.Where(s => s.Timestamp >= weekStart);
					break;
				case "this month" or "month":
					shots = shots.Where(s => s.Timestamp.Year == now.Year && s.Timestamp.Month == now.Month);
					break;
			}

			if (!string.IsNullOrEmpty(beanName))
			{
				shots = shots.Where(s =>
					s.BeanName?.Contains(beanName, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (!string.IsNullOrEmpty(madeBy))
			{
				shots = shots.Where(s =>
					s.MadeByName?.Contains(madeBy, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (!string.IsNullOrEmpty(madeFor))
			{
				shots = shots.Where(s =>
					s.MadeForName?.Contains(madeFor, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (minRating.HasValue)
			{
				var minRatingService = minRating.Value + 1;
				shots = shots.Where(s => s.Rating.HasValue && s.Rating.Value >= minRatingService);
			}

			var count = shots.Count();

			var filterParts = new List<string>();
			if (!string.IsNullOrEmpty(beanName)) filterParts.Add($"with {beanName}");
			if (!string.IsNullOrEmpty(madeBy)) filterParts.Add($"made by {madeBy}");
			if (!string.IsNullOrEmpty(madeFor)) filterParts.Add($"made for {madeFor}");
			if (minRating.HasValue) filterParts.Add($"rated {minRating}+ stars");

			var filterDesc = filterParts.Count > 0 ? " " + string.Join(" ", filterParts) : "";
			var periodDesc = lowerPeriod == "all time" || lowerPeriod == "all" ? "" : $" {lowerPeriod}";

			if (!string.IsNullOrEmpty(madeBy))
			{
				return Task.FromResult($"{madeBy} has made {count} shot{(count != 1 ? "s" : "")}{periodDesc}");
			}

			return Task.FromResult($"You've pulled {count} shot{(count != 1 ? "s" : "")}{filterDesc}{periodDesc}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting shot count via voice");
			return Task.FromResult("Sorry, I couldn't get that information. Please try again.");
		}
	}

	[Description("Gets available pages in the app that can be navigated to")]
	private Task<string> GetAvailablePagesToolAsync()
	{
		_logger.LogInformation("GetAvailablePages tool called");

		try
		{
			var routes = _navigationRegistry.GetAllRoutes();
			if (routes.Count == 0)
			{
				return Task.FromResult("No pages discovered. The app may still be initializing.");
			}

			var sb = new System.Text.StringBuilder();
			sb.AppendLine("Available pages:");
			foreach (var route in routes)
			{
				sb.AppendLine($"- {route.DisplayName} (route: {route.Route}): {route.Description}");
			}

			return Task.FromResult(sb.ToString());
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting available pages");
			return Task.FromResult("Error discovering available pages.");
		}
	}

	[Description("Navigates to a specific page in the app. Use GetAvailablePages first if unsure what pages exist.")]
	private Task<string> NavigateToToolAsync(
		[Description("Page name or alias to navigate to (e.g., 'activity', 'new shot', 'settings')")] string pageName)
	{
		_logger.LogInformation("NavigateTo tool called: {Page}", pageName);

		try
		{
			var routeInfo = _navigationRegistry.FindRoute(pageName);
			if (routeInfo == null)
			{
				var allRoutes = _navigationRegistry.GetAllRoutes();
				var availablePages = string.Join(", ", allRoutes.Select(r => r.DisplayName));
				return Task.FromResult($"Unknown page '{pageName}'. Available pages: {availablePages}.");
			}

			// Navigate via the registry
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await _navigationRegistry.NavigateToRoute(routeInfo.Route);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error navigating to {Route}", routeInfo.Route);
				}
			});

			_logger.LogInformation("Navigating via voice to: {Route} ({DisplayName})", routeInfo.Route, routeInfo.DisplayName);
			return Task.FromResult($"I've taken you to {routeInfo.DisplayName}.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error navigating via voice");
			return Task.FromResult("Sorry, I couldn't navigate there. Please try again.");
		}
	}

	[Description("Navigate to a specific shot's detail page by shot ID")]
	private Task<string> NavigateToShotDetailToolAsync(
		[Description("The shot ID (integer) to navigate to")] int shotId)
	{
		_logger.LogInformation("NavigateToShotDetail tool called: {ShotId}", shotId);

		try
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await _navigationRegistry.NavigateToRoute("shot-detail",
						new Dictionary<string, object> { ["shotId"] = shotId });
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error navigating to shot detail {ShotId}", shotId);
				}
			});

			_logger.LogInformation("Navigating via voice to shot detail: {ShotId}", shotId);
			return Task.FromResult("I've opened the shot details.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error navigating to shot detail via voice");
			return Task.FromResult("Sorry, I couldn't open that shot. Please try again.");
		}
	}

	[Description("Navigate to a specific profile's detail/edit page by profile ID")]
	private Task<string> NavigateToProfileDetailToolAsync(
		[Description("The profile ID (integer) to navigate to")] int profileId)
	{
		_logger.LogInformation("NavigateToProfileDetail tool called: {ProfileId}", profileId);

		try
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await _navigationRegistry.NavigateToRoute("profile-form",
						new Dictionary<string, object> { ["profileId"] = profileId });
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error navigating to profile detail {ProfileId}", profileId);
				}
			});

			_logger.LogInformation("Navigating via voice to profile detail: {ProfileId}", profileId);
			return Task.FromResult("I've opened the profile details.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error navigating to profile detail via voice");
			return Task.FromResult("Sorry, I couldn't open that profile. Please try again.");
		}
	}

	[Description("Filters shots by criteria and navigates to activity feed")]
	private Task<string> FilterShotsToolAsync(
		[Description("Filter by bean name")] string? beanName = null,
		[Description("Filter by period: 'today', 'this week', 'this month'")] string? period = null,
		[Description("Filter by minimum rating (0-4)")] int? minRating = null)
	{
		_logger.LogInformation("FilterShots tool called: bean={Bean}, period={Period}, minRating={MinRating}",
			beanName, period, minRating);

		try
		{
			// Build query parameters for navigation
			var queryParams = new Dictionary<string, object>();

			if (!string.IsNullOrEmpty(beanName))
				queryParams["bean"] = beanName;
			if (!string.IsNullOrEmpty(period))
				queryParams["period"] = period;
			if (minRating.HasValue && minRating >= 0 && minRating <= 4)
				queryParams["minRating"] = minRating.Value;

			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await _navigationRegistry.NavigateToRoute("activity", queryParams);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error navigating to filtered activity");
				}
			});

			var filterDesc = new List<string>();
			if (!string.IsNullOrEmpty(beanName)) filterDesc.Add($"{beanName} bean");
			if (!string.IsNullOrEmpty(period)) filterDesc.Add(period);
			if (minRating.HasValue) filterDesc.Add($"{minRating}+ stars");

			var description = filterDesc.Count > 0 ? string.Join(", ", filterDesc) : "all";
			return Task.FromResult($"Showing shots: {description}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error filtering shots via voice");
			return Task.FromResult("Sorry, I couldn't filter those shots. Please try again.");
		}
	}

	[Description("Gets details about the most recent/last shot including all settings and values")]
	private Task<string> GetLastShotToolAsync()
	{
		_logger.LogInformation("GetLastShot tool called");

		try
		{
			var allShots = _shotService.GetAllShots();
			var lastShot = allShots.OrderByDescending(s => s.Timestamp).FirstOrDefault();
			if (lastShot == null)
			{
				return Task.FromResult("No shots found. Log a shot first.");
			}

			var details = new List<string>
			{
				$"Last shot (ID: {lastShot.Id}, {lastShot.Timestamp:MMM d} at {lastShot.Timestamp:h:mm tt}):",
				$"• Dose: {lastShot.DoseIn:F1}g in → {lastShot.ActualOutput ?? lastShot.ExpectedOutput:F1}g out",
				$"• Time: {lastShot.ActualTime ?? lastShot.ExpectedTime:F0} seconds",
				$"• Grind: {lastShot.GrindSetting}",
				$"• Drink: {lastShot.DrinkType}"
			};

			if (lastShot.Rating.HasValue)
			{
				var rating = lastShot.Rating.Value - 1; // Convert from 1-5 to 0-4 scale
				details.Add($"• Rating: {rating}/4");
			}

			if (!string.IsNullOrEmpty(lastShot.BeanName))
				details.Add($"• Bean: {lastShot.BeanName}");

			if (!string.IsNullOrEmpty(lastShot.MachineName))
				details.Add($"• Machine: {lastShot.MachineName}");

			if (!string.IsNullOrEmpty(lastShot.GrinderName))
				details.Add($"• Grinder: {lastShot.GrinderName}");

			if (!string.IsNullOrEmpty(lastShot.MadeByName))
				details.Add($"• Made by: {lastShot.MadeByName}");

			if (!string.IsNullOrEmpty(lastShot.MadeForName))
				details.Add($"• Made for: {lastShot.MadeForName}");

			if (!string.IsNullOrEmpty(lastShot.TastingNotes))
				details.Add($"• Notes: {lastShot.TastingNotes}");

			_logger.LogInformation("Retrieved last shot details via voice, ID: {ShotId}", lastShot.Id);
			return Task.FromResult(string.Join("\n", details));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting last shot via voice");
			return Task.FromResult("Sorry, I couldn't retrieve that information. Please try again.");
		}
	}

	[Description("Finds and summarizes shots matching specified criteria (bean, rating, person, time period)")]
	private Task<string> FindShotsToolAsync(
		[Description("Filter by bean name")] string? beanName = null,
		[Description("Filter by who made the shot")] string? madeBy = null,
		[Description("Filter by who the shot was made for")] string? madeFor = null,
		[Description("Filter by minimum rating (0-4)")] int? minRating = null,
		[Description("Filter by period: 'today', 'yesterday', 'this week', 'this month'")] string? period = null,
		[Description("How many shots to return (default 5, max 10)")] int? limit = null)
	{
		_logger.LogInformation("FindShots tool called: bean={Bean}, madeBy={MadeBy}, madeFor={MadeFor}, minRating={MinRating}, period={Period}, limit={Limit}",
			beanName, madeBy, madeFor, minRating, period, limit);

		try
		{
			var actualLimit = Math.Min(limit ?? 5, 10);
			var allShots = _shotService.GetAllShots();

			if (allShots.Count == 0)
			{
				return Task.FromResult("No shots found in your history.");
			}

			var filtered = allShots.OrderByDescending(s => s.Timestamp).AsEnumerable();

			if (!string.IsNullOrEmpty(beanName))
			{
				filtered = filtered.Where(s =>
					s.BeanName?.Contains(beanName, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (!string.IsNullOrEmpty(madeBy))
			{
				filtered = filtered.Where(s =>
					s.MadeByName?.Contains(madeBy, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (!string.IsNullOrEmpty(madeFor))
			{
				filtered = filtered.Where(s =>
					s.MadeForName?.Contains(madeFor, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (minRating.HasValue)
			{
				var minRatingService = minRating.Value + 1;
				filtered = filtered.Where(s => s.Rating.HasValue && s.Rating.Value >= minRatingService);
			}

			if (!string.IsNullOrEmpty(period))
			{
				var now = DateTime.Now;
				var startDate = period.ToLowerInvariant() switch
				{
					"today" => now.Date,
					"yesterday" => now.Date.AddDays(-1),
					"this week" => now.Date.AddDays(-(int)now.DayOfWeek),
					"this month" => new DateTime(now.Year, now.Month, 1),
					_ => DateTime.MinValue
				};

				if (startDate != DateTime.MinValue)
				{
					var endDate = period.ToLowerInvariant() == "yesterday" ? now.Date : DateTime.MaxValue;
					filtered = filtered.Where(s => s.Timestamp >= startDate && s.Timestamp < endDate);
				}
			}

			var results = filtered.Take(actualLimit).ToList();

			if (results.Count == 0)
			{
				var filterDesc = BuildFilterDescription(beanName, madeBy, madeFor, minRating, period);
				return Task.FromResult($"No shots found matching: {filterDesc}");
			}

			var summary = new List<string>();
			var filterDescription = BuildFilterDescription(beanName, madeBy, madeFor, minRating, period);
			summary.Add($"Found {results.Count} shot{(results.Count != 1 ? "s" : "")}" +
				(filterDescription != "all" ? $" matching {filterDescription}" : "") + ":");

			foreach (var shot in results)
			{
				var ratingStr = shot.Rating.HasValue ? $" ({shot.Rating.Value - 1}/4)" : "";
				var beanInfo = shot.BeanName ?? "unknown bean";
				summary.Add($"• ID:{shot.Id} {shot.Timestamp:MMM d}: {shot.DoseIn:F1}g→{shot.ActualOutput ?? shot.ExpectedOutput:F1}g, {shot.ActualTime ?? shot.ExpectedTime:F0}s{ratingStr} [{beanInfo}]");
			}

			_logger.LogInformation("Found {Count} shots via voice query", results.Count);
			return Task.FromResult(string.Join("\n", summary));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding shots via voice");
			return Task.FromResult("Sorry, I couldn't search for shots. Please try again.");
		}
	}

	[Description("Gets the count of unique beans the user has tried")]
	private Task<string> GetBeanCountToolAsync(
		[Description("Optional roaster name to filter by")] string? roaster = null,
		[Description("Optional origin to filter by (e.g., 'Ethiopia', 'Colombia')")] string? origin = null)
	{
		_logger.LogInformation("GetBeanCount tool called: Roaster={Roaster}, Origin={Origin}", roaster, origin);

		try
		{
			var beans = _beanService.GetAllBeans().Where(b => b.IsActive).ToList();

			if (!string.IsNullOrWhiteSpace(roaster))
			{
				beans = beans.Where(b => b.Roaster != null &&
					b.Roaster.Contains(roaster, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			if (!string.IsNullOrWhiteSpace(origin))
			{
				beans = beans.Where(b => b.Origin != null &&
					b.Origin.Contains(origin, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			var count = beans.Count;
			var filterDesc = BuildBeanFilterDescription(roaster, origin);

			return Task.FromResult($"You have {count} unique bean{(count != 1 ? "s" : "")} {filterDesc}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting bean count via voice");
			return Task.FromResult("Sorry, I couldn't get that information. Please try again.");
		}
	}

	[Description("Finds and lists beans matching the search criteria")]
	private Task<string> FindBeansToolAsync(
		[Description("Optional roaster name to filter by")] string? roaster = null,
		[Description("Optional origin to filter by")] string? origin = null,
		[Description("Optional bean name to search for")] string? name = null,
		[Description("Maximum number of results to return (default 5)")] int limit = 5)
	{
		_logger.LogInformation("FindBeans tool called: Roaster={Roaster}, Origin={Origin}, Name={Name}, Limit={Limit}",
			roaster, origin, name, limit);

		try
		{
			var beans = _beanService.GetAllBeans().Where(b => b.IsActive).ToList();

			if (!string.IsNullOrWhiteSpace(roaster))
				beans = beans.Where(b => b.Roaster?.Contains(roaster, StringComparison.OrdinalIgnoreCase) == true).ToList();

			if (!string.IsNullOrWhiteSpace(origin))
				beans = beans.Where(b => b.Origin?.Contains(origin, StringComparison.OrdinalIgnoreCase) == true).ToList();

			if (!string.IsNullOrWhiteSpace(name))
				beans = beans.Where(b => b.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();

			var count = beans.Count;
			if (count == 0)
			{
				var filterDesc = BuildBeanFilterDescription(roaster, origin, name);
				return Task.FromResult($"No beans found {filterDesc}");
			}

			var results = beans.Take(limit).ToList();
			var beanDescriptions = results.Select(b =>
			{
				var parts = new List<string> { b.Name };
				if (!string.IsNullOrEmpty(b.Roaster)) parts.Add($"by {b.Roaster}");
				if (!string.IsNullOrEmpty(b.Origin)) parts.Add($"from {b.Origin}");
				return string.Join(" ", parts);
			});

			var filterDescription = BuildBeanFilterDescription(roaster, origin, name);
			var resultText = string.Join("; ", beanDescriptions);
			var moreText = count > limit ? $" (showing {limit} of {count})" : "";

			return Task.FromResult($"Found {count} bean{(count != 1 ? "s" : "")} {filterDescription}{moreText}: {resultText}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding beans via voice");
			return Task.FromResult("Sorry, I couldn't search for beans. Please try again.");
		}
	}

	[Description("Gets the count of bags (physical bags of coffee) the user has")]
	private Task<string> GetBagCountToolAsync(
		[Description("Whether to include completed/finished bags (default true)")] bool includeCompleted = true)
	{
		_logger.LogInformation("GetBagCount tool called: IncludeCompleted={IncludeCompleted}", includeCompleted);

		try
		{
			var allBags = _bagService.GetAllBags();
			var activeBags = allBags.Where(b => b.IsActive && !b.IsComplete).Count();
			var totalBags = includeCompleted ? allBags.Count : activeBags;

			if (includeCompleted)
			{
				return Task.FromResult($"You have {totalBags} bag{(totalBags != 1 ? "s" : "")} total ({activeBags} active, {totalBags - activeBags} finished)");
			}
			else
			{
				return Task.FromResult($"You have {activeBags} active bag{(activeBags != 1 ? "s" : "")}");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting bag count via voice");
			return Task.FromResult("Sorry, I couldn't get that information. Please try again.");
		}
	}

	[Description("Finds and lists bags (physical bags of coffee) matching the search criteria")]
	private Task<string> FindBagsToolAsync(
		[Description("Optional bean name to filter by")] string? beanName = null,
		[Description("Optional roaster name to filter by")] string? roaster = null,
		[Description("Whether to include only active (not completed) bags (default true)")] bool activeOnly = true,
		[Description("Maximum number of results to return (default 5)")] int limit = 5)
	{
		_logger.LogInformation("FindBags tool called: BeanName={BeanName}, Roaster={Roaster}, ActiveOnly={ActiveOnly}, Limit={Limit}",
			beanName, roaster, activeOnly, limit);

		try
		{
			var allBags = _bagService.GetAllBags();
			var filtered = allBags.AsEnumerable();

			if (activeOnly)
				filtered = filtered.Where(b => b.IsActive && !b.IsComplete);

			if (!string.IsNullOrWhiteSpace(beanName))
				filtered = filtered.Where(b => b.BeanName?.Contains(beanName, StringComparison.OrdinalIgnoreCase) == true);

			if (!string.IsNullOrWhiteSpace(roaster))
			{
				// Cross-reference with beans for roaster info
				var beans = _beanService.GetAllBeans();
				var roasterBeanIds = beans
					.Where(b => b.Roaster?.Contains(roaster, StringComparison.OrdinalIgnoreCase) == true)
					.Select(b => b.Id)
					.ToHashSet();
				filtered = filtered.Where(b => roasterBeanIds.Contains(b.BeanId));
			}

			var matchingBags = filtered.OrderByDescending(b => b.RoastDate).ToList();
			var count = matchingBags.Count;

			if (count == 0)
			{
				var filterDesc = BuildBagFilterDescription(beanName, roaster, activeOnly);
				return Task.FromResult($"No bags found {filterDesc}");
			}

			var results = matchingBags.Take(limit).ToList();
			var bagDescriptions = results.Select(b =>
			{
				var parts = new List<string> { b.BeanName ?? "Unknown bean" };
				parts.Add($"roasted {b.RoastDate:MMM d}");
				if (b.ShotCount > 0) parts.Add($"{b.ShotCount} shots");
				if (b.IsComplete) parts.Add("(finished)");
				return string.Join(" ", parts);
			});

			var filterDescription = BuildBagFilterDescription(beanName, roaster, activeOnly);
			var resultText = string.Join("; ", bagDescriptions);
			var moreText = count > limit ? $" (showing {limit} of {count})" : "";

			return Task.FromResult($"Found {count} bag{(count != 1 ? "s" : "")} {filterDescription}{moreText}: {resultText}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding bags via voice");
			return Task.FromResult("Sorry, I couldn't search for bags. Please try again.");
		}
	}

	[Description("Gets the count of equipment items the user has")]
	private Task<string> GetEquipmentCountToolAsync(
		[Description("Optional equipment type filter: 'machine', 'grinder', 'tamper', 'puck screen', or 'other'")] string? type = null)
	{
		_logger.LogInformation("GetEquipmentCount tool called: Type={Type}", type);

		try
		{
			var equipment = _equipmentService.GetAllEquipment().Where(e => e.IsActive).ToList();

			if (!string.IsNullOrWhiteSpace(type))
			{
				var equipmentType = ParseEquipmentType(type);
				equipment = equipment.Where(e => e.Type == equipmentType).ToList();
			}

			var count = equipment.Count;
			var typeDesc = !string.IsNullOrWhiteSpace(type) ? $" ({type})" : "";

			return Task.FromResult($"You have {count} piece{(count != 1 ? "s" : "")} of equipment{typeDesc}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting equipment count via voice");
			return Task.FromResult("Sorry, I couldn't get that information. Please try again.");
		}
	}

	[Description("Finds and lists equipment matching the search criteria")]
	private Task<string> FindEquipmentToolAsync(
		[Description("Optional equipment type filter: 'machine', 'grinder', 'tamper', 'puck screen', or 'other'")] string? type = null,
		[Description("Optional name to search for")] string? name = null,
		[Description("Maximum number of results to return (default 5)")] int limit = 5)
	{
		_logger.LogInformation("FindEquipment tool called: Type={Type}, Name={Name}, Limit={Limit}", type, name, limit);

		try
		{
			var equipment = _equipmentService.GetAllEquipment().Where(e => e.IsActive).ToList();

			if (!string.IsNullOrWhiteSpace(type))
			{
				var equipmentType = ParseEquipmentType(type);
				equipment = equipment.Where(e => e.Type == equipmentType).ToList();
			}

			if (!string.IsNullOrWhiteSpace(name))
			{
				equipment = equipment.Where(e =>
					e.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			var count = equipment.Count;
			if (count == 0)
			{
				var filterDesc = BuildEquipmentFilterDescription(type, name);
				return Task.FromResult($"No equipment found {filterDesc}");
			}

			var results = equipment.Take(limit).ToList();
			var equipmentDescriptions = results.Select(e =>
			{
				var typeStr = e.Type switch
				{
					EquipmentType.Machine => "machine",
					EquipmentType.Grinder => "grinder",
					EquipmentType.Tamper => "tamper",
					EquipmentType.PuckScreen => "puck screen",
					_ => "equipment"
				};
				return $"{e.Name} ({typeStr})";
			});

			var filterDescription = BuildEquipmentFilterDescription(type, name);
			var resultText = string.Join("; ", equipmentDescriptions);
			var moreText = count > limit ? $" (showing {limit} of {count})" : "";

			return Task.FromResult($"Found {count} item{(count != 1 ? "s" : "")} {filterDescription}{moreText}: {resultText}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding equipment via voice");
			return Task.FromResult("Sorry, I couldn't search for equipment. Please try again.");
		}
	}

	[Description("Gets the count of user profiles")]
	private Task<string> GetProfileCountToolAsync()
	{
		_logger.LogInformation("GetProfileCount tool called");

		try
		{
			var profiles = _userProfileService.GetAllProfiles();
			var count = profiles.Count;

			return Task.FromResult($"You have {count} user profile{(count != 1 ? "s" : "")}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting profile count via voice");
			return Task.FromResult("Sorry, I couldn't get that information. Please try again.");
		}
	}

	[Description("Finds and lists user profiles")]
	private Task<string> FindProfilesToolAsync(
		[Description("Optional name to search for")] string? name = null,
		[Description("Maximum number of results to return (default 5)")] int limit = 5)
	{
		_logger.LogInformation("FindProfiles tool called: Name={Name}, Limit={Limit}", name, limit);

		try
		{
			var profiles = _userProfileService.GetAllProfiles();

			if (!string.IsNullOrWhiteSpace(name))
			{
				profiles = profiles.Where(p =>
					p.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			var count = profiles.Count;
			if (count == 0)
			{
				return Task.FromResult(string.IsNullOrWhiteSpace(name)
					? "No user profiles found"
					: $"No profiles found matching '{name}'");
			}

			var results = profiles.Take(limit).ToList();
			var profileDetails = results.Select(p => $"ID:{p.Id} {p.Name}");

			var resultText = string.Join(", ", profileDetails);
			var moreText = count > limit ? $" (showing {limit} of {count})" : "";
			var filterText = !string.IsNullOrWhiteSpace(name) ? $" matching '{name}'" : "";

			return Task.FromResult($"Found {count} profile{(count != 1 ? "s" : "")}{filterText}{moreText}: {resultText}");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error finding profiles via voice");
			return Task.FromResult("Sorry, I couldn't search for profiles. Please try again.");
		}
	}

	[Description("Takes a photo and analyzes it to count people and determine coffee needs")]
	private async Task<string> AnalyzeRoomForCoffeeToolAsync(
		[Description("The user's question about the image")] string userQuestion)
	{
		_logger.LogInformation("AnalyzeRoomForCoffee tool called with question: {Question}", userQuestion);

		try
		{
			if (_visionService == null || !_visionService.IsAvailable)
			{
				return "Vision service is not available. Please check your configuration.";
			}

			if (!MediaPicker.Default.IsCaptureSupported)
			{
				return "Camera is not available on this device.";
			}

			// Signal to pause speech recognition before opening camera
			_logger.LogDebug("Requesting speech pause for camera capture");
			PauseSpeechRequested?.Invoke(this, EventArgs.Empty);

			FileResult? photo = null;
			try
			{
				_logger.LogDebug("Requesting camera capture on main thread");

				photo = await MainThread.InvokeOnMainThreadAsync(async () =>
				{
					return await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
					{
						Title = "Take a photo of the room",
					});
				});

				if (photo == null)
				{
					return "Photo capture was cancelled.";
				}

				_logger.LogDebug("Photo captured: {FileName}, analyzing with vision service", photo.FileName);
				var result = await _visionService.AnalyzeImage(photo.FullPath);

				var response = !string.IsNullOrEmpty(result.Description)
					? result.Description
					: "I analyzed the photo but couldn't determine the coffee needs.";

				_logger.LogInformation("Vision analysis complete: {Description}", result.Description);
				return response;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Error during camera capture");
				return $"Failed to capture or process photo: {ex.Message}";
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error in AnalyzeRoomForCoffee tool");
			return $"Sorry, I couldn't analyze the room: {ex.Message}";
		}
	}

	#endregion

	#region Filter Description Helpers

	private static string BuildFilterDescription(string? beanName, string? madeBy, string? madeFor, int? minRating, string? period)
	{
		var parts = new List<string>();
		if (!string.IsNullOrEmpty(beanName)) parts.Add($"{beanName} bean");
		if (!string.IsNullOrEmpty(madeBy)) parts.Add($"made by {madeBy}");
		if (!string.IsNullOrEmpty(madeFor)) parts.Add($"made for {madeFor}");
		if (minRating.HasValue) parts.Add($"{minRating}+ rating");
		if (!string.IsNullOrEmpty(period)) parts.Add(period);
		return parts.Count > 0 ? string.Join(", ", parts) : "all";
	}

	private static string BuildBeanFilterDescription(string? roaster = null, string? origin = null, string? name = null)
	{
		var parts = new List<string>();
		if (!string.IsNullOrEmpty(roaster)) parts.Add($"from {roaster}");
		if (!string.IsNullOrEmpty(origin)) parts.Add($"origin {origin}");
		if (!string.IsNullOrEmpty(name)) parts.Add($"named '{name}'");
		return parts.Count > 0 ? string.Join(", ", parts) : "";
	}

	private static string BuildBagFilterDescription(string? beanName = null, string? roaster = null, bool activeOnly = true)
	{
		var parts = new List<string>();
		if (!string.IsNullOrEmpty(beanName)) parts.Add($"of {beanName}");
		if (!string.IsNullOrEmpty(roaster)) parts.Add($"from {roaster}");
		if (activeOnly) parts.Add("(active only)");
		return parts.Count > 0 ? string.Join(" ", parts) : "";
	}

	private static string BuildEquipmentFilterDescription(string? type = null, string? name = null)
	{
		var parts = new List<string>();
		if (!string.IsNullOrEmpty(type)) parts.Add($"type '{type}'");
		if (!string.IsNullOrEmpty(name)) parts.Add($"named '{name}'");
		return parts.Count > 0 ? string.Join(", ", parts) : "";
	}

	#endregion
}
