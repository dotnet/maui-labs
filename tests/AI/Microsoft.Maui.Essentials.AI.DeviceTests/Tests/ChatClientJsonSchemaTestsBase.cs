using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient JSON schema response format tests.
/// Provides common tests for any IChatClient implementation that supports structured JSON output.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientJsonSchemaTestsBase<T>
	where T : class, IChatClient, new()
{
	protected virtual bool SupportsStructuredOutput => true;
	// Null preserves legacy bindings, including their existing schema-free JSON overrides.
	protected virtual bool? RequiresExplicitJsonSchema => null;
	protected virtual bool SupportsBaselineSamplingOptions => false;
	protected virtual int? BaselineMaxOutputTokens => null;
	// Models (null = the client default) that each live schema-shape scenario runs against.
	protected virtual IReadOnlyList<string?> StructuredOutputModelIds => [null];
	// True when ReasoningOutput.Full can be combined with a JSON schema on ReasoningModelId.
	protected virtual bool SupportsReasoningOutput => false;
	protected virtual string? ReasoningModelId => null;
	// False when the model may skip reasoning or return empty summaries for simple prompts.
	protected virtual bool ReasoningContentIsGuaranteed => SupportsReasoningOutput;
	// Mirrors ChatClientUsageTestsBase: false asserts that structured responses report no usage.
	protected virtual bool IsUsageAvailable => true;
	// Gemini Nano supports 20 levels; OpenAI strict structured outputs document a limit of 10.
	protected virtual int StructuredSchemaNestingDepth => 10;
	// True when structured streaming yields the JSON across several text updates rather than one final update.
	protected virtual bool StreamsStructuredOutputIncrementally => false;

	private void ConfigureBaselineSamplingOptions(ChatOptions options, float temperature)
	{
		if (!SupportsBaselineSamplingOptions)
			return;

		options.Temperature = temperature;
		options.Reasoning = new() { Effort = ReasoningEffort.None };
		options.MaxOutputTokens ??= BaselineMaxOutputTokens;
	}

	private ChatOptions CreateStructuredOptions(string? modelId, ChatResponseFormat? responseFormat = null, int? maxOutputTokens = null)
	{
		var options = new ChatOptions { ModelId = modelId, MaxOutputTokens = maxOutputTokens, ResponseFormat = responseFormat };
		if (SupportsBaselineSamplingOptions)
			options.Temperature = 0;
		return options;
	}

	private static async Task<ChatResponse> GetResponseAsync(
		IChatClient client, ChatMessage[] messages, ChatOptions options, bool streaming, CancellationToken cancellationToken) =>
		streaming
			? await client.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken)
			: await client.GetResponseAsync(messages, options, cancellationToken);

	// Record types for testing JSON schema responses
	public record WeatherResponse(string Location, int Temperature, string Condition);
	public record SimpleResponse(string Message);
	public record Reply(string Answer);
	public record Continents(string[] Names);
	public record Person(string Name, int Age, string City);
	public record TripItinerary(
		string Destination,
		int Days,
		[property: MinLength(2), MaxLength(2)] Activity[] Activities);
	public record Activity(string Name, string Description, string Time);

	private const string ItineraryPrompt =
		"Return a 1-day trip to Seattle with exactly 2 activities, in this order: " +
		"Space Needle at 09:00, description View the city; " +
		"Pike Place Market at 12:00, description Explore the market. Use these exact values.";

	[Theory]
	[InlineData("weather", false)]
	[InlineData("weather", true)]
	[InlineData("simple", false)]
	[InlineData("simple", true)]
	[InlineData("itinerary", false)]
	[InlineData("itinerary", true)]
	[InlineData("custom-options", false)]
	[InlineData("custom-options", true)]
	[InlineData("typed-weather", false)]
	[InlineData("typed-simple", false)]
	[InlineData("typed-itinerary", false)]
	[InlineData("typed-options", false)]
	public async Task StructuredOutput_WhenUnsupported_RejectsBeforeInference(string scenario, bool streaming)
	{
		if (SupportsStructuredOutput)
			return;

		using var client = new T();
		ChatMessage[] messages = [new(ChatRole.User, "Return structured data.")];
		var options = new ChatOptions();
		if (scenario is "custom-options" or "typed-options")
			ConfigureBaselineSamplingOptions(options, 0.5f);
		if (!scenario.StartsWith("typed-", StringComparison.Ordinal))
		{
			options.ResponseFormat = scenario switch
			{
				"weather" => ChatResponseFormat.ForJsonSchema<WeatherResponse>(),
				"itinerary" => ChatResponseFormat.ForJsonSchema<TripItinerary>(),
				"custom-options" => ChatResponseFormat.ForJsonSchema<SimpleResponse>(JsonSerializerOptions.Web),
				_ => ChatResponseFormat.ForJsonSchema<SimpleResponse>()
			};
		}

		await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (scenario is "typed-weather" or "typed-options")
				await client.GetResponseAsync<WeatherResponse>(messages, options);
			else if (scenario == "typed-simple")
				await client.GetResponseAsync<SimpleResponse>(messages, options);
			else if (scenario == "typed-itinerary")
				await client.GetResponseAsync<TripItinerary>(messages, options);
			else if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(bool streaming)
	{
		if (RequiresExplicitJsonSchema is not true && SupportsStructuredOutput)
			return;

		using var client = new T();
		ChatMessage[] messages = [new(ChatRole.User, "Return a JSON object.")];
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
		var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
		if (RequiresExplicitJsonSchema is true)
			Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TypedPromptFallback_WhenUnsupported_RejectsBeforeInference()
	{
		if (RequiresExplicitJsonSchema is not true && SupportsStructuredOutput)
			return;

		using var client = new T();
		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync<SimpleResponse>(
				[new ChatMessage(ChatRole.User, "Return exactly hello in the message field.")],
				useJsonSchemaResponseFormat: false));
		if (RequiresExplicitJsonSchema is true)
			Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<WeatherResponse>()
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify the response can be parsed as the expected type
		var weatherResponse = JsonSerializer.Deserialize<WeatherResponse>(response.Text, JsonSerializerOptions.Web);
		Assert.NotNull(weatherResponse);
		Assert.NotNull(weatherResponse.Location);
		Assert.Equal("Seattle", weatherResponse.Location, ignoreCase: true);
		Assert.Equal(72, weatherResponse.Temperature);
		Assert.Equal("sunny", weatherResponse.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return exactly hello in the message field.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SimpleResponse>()
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify it's valid JSON
		using var doc = JsonDocument.Parse(response.Text);
		Assert.NotNull(doc);
		var simpleResponse = JsonSerializer.Deserialize<SimpleResponse>(response.Text, JsonSerializerOptions.Web);
		Assert.NotNull(simpleResponse);
		Assert.Equal("hello", simpleResponse.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Boston, temperature 68, and condition sunny.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<WeatherResponse>()
		};
		ConfigureBaselineSamplingOptions(options, 0);

		var textBuilder = new System.Text.StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			foreach (var content in update.Contents)
			{
				if (content is TextContent textContent)
				{
					textBuilder.Append(textContent.Text);
				}
			}
		}

		var completeText = textBuilder.ToString();
		Assert.False(string.IsNullOrEmpty(completeText), "Should receive streaming response");

		// Verify the complete response is valid JSON
		var weatherResponse = JsonSerializer.Deserialize<WeatherResponse>(completeText, JsonSerializerOptions.Web);
		Assert.NotNull(weatherResponse);
		Assert.Equal("Boston", weatherResponse.Location, ignoreCase: true);
		Assert.Equal(68, weatherResponse.Temperature);
		Assert.Equal("sunny", weatherResponse.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		if (RequiresExplicitJsonSchema is true || !SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object with a 'greeting' field containing 'hello'")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);
		Assert.False(string.IsNullOrWhiteSpace(response.Text), "Response text should not be empty");

		// Verify the response is valid JSON (will throw if invalid)
		using var doc = JsonDocument.Parse(response.Text);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson()
	{
		if (RequiresExplicitJsonSchema is not false || !SupportsStructuredOutput)
			return;

		using var client = new T();

		var response = await client.GetResponseAsync<SimpleResponse>(
			[new ChatMessage(ChatRole.User, "Return exactly hello in the message field.")],
			useJsonSchemaResponseFormat: false);

		Assert.Equal("hello", response.Result.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		if (RequiresExplicitJsonSchema is true || !SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object with a 'greeting' field containing 'hello'")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var textBuilder = new System.Text.StringBuilder();

		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			if (!string.IsNullOrEmpty(update.Text))
			{
				textBuilder.Append(update.Text);
			}
		}

		var responseText = textBuilder.ToString();
		Assert.False(string.IsNullOrWhiteSpace(responseText), "Streaming response should not be empty");

		// Verify the complete response is valid JSON (will throw if invalid)
		using var doc = JsonDocument.Parse(responseText);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return exactly hello in the message field.")
		};

		var jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SimpleResponse>(jsonOptions)
		};
		ConfigureBaselineSamplingOptions(options, 0.5f);

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify the response can be parsed
		var simpleResponse = JsonSerializer.Deserialize<SimpleResponse>(response.Text, jsonOptions);
		Assert.NotNull(simpleResponse);
		Assert.Equal("hello", simpleResponse.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a travel assistant. Always respond with structured itinerary data."),
			new(ChatRole.User, ItineraryPrompt)
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<TripItinerary>()
		};
		ConfigureBaselineSamplingOptions(options, 0);

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		var itinerary = JsonSerializer.Deserialize<TripItinerary>(response.Text, JsonSerializerOptions.Web);
		AssertItinerary(itinerary);
	}

	[Fact]
	public async Task GetResponseAsync_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.GetResponseAsync(null!));
	}

	[Fact]
	public async Task GetStreamingResponseAsync_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(null!))
			{
				// Should not reach here
			}
		});
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.");

		var response = await client.GetResponseAsync<WeatherResponse>(message);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Seattle", response.Result.Location, ignoreCase: true);
		Assert.Equal(72, response.Result.Temperature);
		Assert.Equal("sunny", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return exactly hello in the message field.");

		var response = await client.GetResponseAsync<SimpleResponse>(message);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Message);
		Assert.Equal("hello", response.Result.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a travel assistant. Always respond with structured itinerary data."),
			new(ChatRole.User, ItineraryPrompt)
		};

		var options = new ChatOptions();
		ConfigureBaselineSamplingOptions(options, 0);
		var response = await client.GetResponseAsync<TripItinerary>(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		AssertItinerary(response.Result);
	}

	private static void AssertItinerary(TripItinerary? itinerary)
	{
		Assert.NotNull(itinerary);
		Assert.Equal("Seattle", itinerary.Destination, ignoreCase: true);
		Assert.Equal(1, itinerary.Days);
		Assert.NotNull(itinerary.Activities);
		Assert.Equal(2, itinerary.Activities.Length);
		Assert.Collection(itinerary.Activities,
			activity =>
			{
				Assert.Equal("Space Needle", activity.Name, ignoreCase: true);
				Assert.Equal("View the city", activity.Description, ignoreCase: true);
				Assert.Equal("09:00", activity.Time);
			},
			activity =>
			{
				Assert.Equal("Pike Place Market", activity.Name, ignoreCase: true);
				Assert.Equal("Explore the market", activity.Description, ignoreCase: true);
				Assert.Equal("12:00", activity.Time);
			});
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return location Boston, temperature 68, and condition sunny.");
		var options = new ChatOptions { MaxOutputTokens = 128 };
		ConfigureBaselineSamplingOptions(options, 0.5f);
		var response = await client.GetResponseAsync<WeatherResponse>(message, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Boston", response.Result.Location, ignoreCase: true);
		Assert.Equal(68, response.Result.Temperature);
		Assert.Equal("sunny", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Chicago, temperature 65, and condition cloudy.")
		};

		var response = await client.GetResponseAsync<WeatherResponse>(messages);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Chicago", response.Result.Location, ignoreCase: true);
		Assert.Equal(65, response.Result.Temperature);
		Assert.Equal("cloudy", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	public async Task GetResponseAsyncT_WithNullMessage_Throws()
	{
		var client = new T();

		var error = await Record.ExceptionAsync(
			() => client.GetResponseAsync<SimpleResponse>((ChatMessage)null!));

		Assert.True(
			error is ArgumentNullException or NullReferenceException,
			$"Expected a null-message exception, but got {error?.GetType().FullName ?? "no exception"}.");
	}

	[Fact]
	public async Task GetResponseAsyncT_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.GetResponseAsync<SimpleResponse>((IList<ChatMessage>)null!));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithCancellationToken_SupportsCancellation()
	{
		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Say hello in a message field.");
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => client.GetResponseAsync<SimpleResponse>(message, cancellationToken: cts.Token));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StructuredStreaming_EmitsIncrementalJsonThatMatchesTheFinalResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		foreach (var modelId in StructuredOutputModelIds)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
			var options = CreateStructuredOptions(modelId, ChatResponseFormat.ForJsonSchema<Continents>());
			List<ChatResponseUpdate> updates = [];
			await foreach (var update in client.GetStreamingResponseAsync(
				[new(ChatRole.User, "List the names of the seven continents.")], options, timeout.Token))
				updates.Add(update);
			var text = string.Concat(updates.Select(update => update.Text));
			var textUpdates = updates.Count(update => update.Text.Length > 0);
			if (StreamsStructuredOutputIncrementally)
				Assert.True(textUpdates > 1, $"{modelId ?? "(default)"} streamed {textUpdates} text updates: {text}");
			else
				Assert.True(textUpdates >= 1, $"{modelId ?? "(default)"} streamed no text.");
			Assert.InRange(JsonSerializer.Deserialize<Continents>(text, JsonSerializerOptions.Web)!.Names.Length, 5, 8);
		}
	}

	// Mirrors Microsoft.Extensions.DataIngestion batching: system prompt plus one user message with one text per chunk.
	// GetResponseAsync<string[]> and <string[][]> exercise MEAI's {"data": ...} wrapping of non-object results.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task DataIngestionBatchSchemas_ReturnTypedSummaryAndKeywordCollections()
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		foreach (var modelId in StructuredOutputModelIds)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
			var options = CreateStructuredOptions(modelId);
			ChatMessage chunks = new(ChatRole.User,
			[
				new TextContent("Build cross-platform mobile apps with .NET MAUI, then deploy them to Android and iOS."),
				new TextContent("Use Azure to host secure cloud APIs and store application data."),
			]);

			var summaries = await client.GetResponseAsync<string[]>(
			[
				new(ChatRole.System, "For each of the following texts, write a summary text with no more than 24 words."),
				chunks,
			], options, cancellationToken: timeout.Token);
			Assert.NotEmpty(summaries.Result);
			Assert.All(summaries.Result, summary => Assert.False(string.IsNullOrWhiteSpace(summary)));

			var keywords = await client.GetResponseAsync<string[][]>(
			[
				new(ChatRole.System, "You are a keyword extraction expert. For each of the following texts, extract up to 3 most relevant keywords. " +
					"Focus on extracting keywords from the following predefined list: AI, Cloud, Data, Mobile, Web, Security. " +
					"Exclude keywords with confidence score below 0.7."),
				chunks,
			], options, cancellationToken: timeout.Token);
			Assert.NotEmpty(keywords.Result);
			Assert.All(keywords.Result, Assert.NotNull);
			Assert.Contains(keywords.Result.SelectMany(values => values),
				keyword => keyword.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
					keyword.Contains("Cloud", StringComparison.OrdinalIgnoreCase));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task EnumsArraysAndBounds_ConstrainGeneration(bool streaming)
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		foreach (var modelId in StructuredOutputModelIds)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
			async Task<JsonElement> Generate(string property, string prompt)
			{
				using var document = JsonDocument.Parse($$"""{"type":"object","properties":{"value":{{property}}},"required":["value"]}""");
				var options = CreateStructuredOptions(modelId, ChatResponseFormat.ForJsonSchema(document.RootElement));
				var response = await GetResponseAsync(client, [new(ChatRole.User, prompt)], options, streaming, timeout.Token);
				return JsonDocument.Parse(response.Text).RootElement.GetProperty("value").Clone();
			}

			var colors = await Generate("""{"type":"array","items":{"type":"string","enum":["red","green","blue"]},"minItems":3,"maxItems":3}""",
				"List the three primary colors of light.");
			Assert.Equal(3, colors.GetArrayLength());
			Assert.All(colors.EnumerateArray(), color => Assert.Contains(color.GetString(), new[] { "red", "green", "blue" }));

			var mood = await Generate("""{"type":"string","enum":["positive","negative","neutral"]}""",
				"Classify the sentiment: I absolutely love this phone!");
			Assert.Equal("positive", mood.GetString());

			var count = await Generate("""{"type":"integer","minimum":1,"maximum":20}""", "How many legs does a spider have?");
			// The bounds are enforced; small on-device models do not always answer this correctly.
			Assert.InRange(count.GetInt32(), 1, 20);
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ArbitrarySchemas_GenerateNativelyWithoutShapeLimits()
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		var depth = StructuredSchemaNestingDepth;
		var deep = """{"type":"string","description":"The name of the planet we live on."}""";
		for (var level = 0; level < depth; level++)
			deep = $$"""{"type":"object","properties":{"level{{level}}":{{deep}}},"required":["level{{level}}"],"additionalProperties":false}""";
		using var deepSchema = JsonDocument.Parse(deep);
		using var arraySchema = JsonDocument.Parse("""{"type":"object","properties":{"colors":{"type":"array","items":{"type":["string","null"]},"minItems":3,"maxItems":3}},"required":["colors"],"additionalProperties":false}""");

		foreach (var modelId in StructuredOutputModelIds)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
			var deepResponse = await client.GetResponseAsync([new(ChatRole.User, "Which planet do we live on?")],
				CreateStructuredOptions(modelId, ChatResponseFormat.ForJsonSchema(deepSchema.RootElement), maxOutputTokens: 512),
				timeout.Token);
			var value = JsonDocument.Parse(deepResponse.Text).RootElement;
			for (var level = depth - 1; level >= 0; level--)
				value = value.GetProperty($"level{level}");
			Assert.Contains("earth", value.GetString(), StringComparison.OrdinalIgnoreCase);

			var arrayResponse = await client.GetResponseAsync([new(ChatRole.User, "List the three primary colors of light.")],
				CreateStructuredOptions(modelId, ChatResponseFormat.ForJsonSchema(arraySchema.RootElement), maxOutputTokens: 512),
				timeout.Token);
			Assert.Equal(3, JsonDocument.Parse(arrayResponse.Text).RootElement.GetProperty("colors").GetArrayLength());
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ThinkingAndSchema_ReturnThoughtsAndTypedResult(bool streaming)
	{
		if (!SupportsStructuredOutput || !SupportsReasoningOutput)
			return;

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new T();
		var options = CreateStructuredOptions(ReasoningModelId, ChatResponseFormat.ForJsonSchema<Person>(JsonSerializerOptions.Web));
		options.Reasoning = new() { Output = ReasoningOutput.Full };
		var response = await GetResponseAsync(client,
			[new(ChatRole.User, "Extract the person: Alice is 30 years old and lives in Oslo.")], options, streaming, timeout.Token);
		var thoughts = string.Concat(response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>()
			.Select(content => content.Text));
		if (ReasoningContentIsGuaranteed)
			Assert.NotEmpty(thoughts);
		Assert.Equal(new Person("Alice", 30, "Oslo"), JsonSerializer.Deserialize<Person>(response.Text, JsonSerializerOptions.Web));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StructuredResponse_PreservesHistoryAndModelUsage(bool streaming)
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		ChatMessage[] messages =
		[
			new(ChatRole.System, "Extract the exact requested answer from the conversation."),
			new(ChatRole.User, "The secret word is azure."),
			new(ChatRole.Assistant, "I will remember azure."),
			new(ChatRole.User, "Return the secret word in the answer field."),
		];
		foreach (var modelId in StructuredOutputModelIds)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
			var options = CreateStructuredOptions(modelId, ChatResponseFormat.ForJsonSchema<Reply>(), maxOutputTokens: 128);
			var response = await GetResponseAsync(client, messages, options, streaming, timeout.Token);
			Assert.Equal("azure", JsonSerializer.Deserialize<Reply>(response.Text, JsonSerializerOptions.Web)!.Answer,
				ignoreCase: true);
			Assert.False(string.IsNullOrWhiteSpace(response.ModelId));
			if (IsUsageAvailable)
				Assert.True(response.Usage?.InputTokenCount > 0);
			else
				Assert.Null(response.Usage);
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ConcurrentSchemasAndCancellation_DoNotLeakSchemaState()
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return hello.")], cancellationToken: cancelled.Token));

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		var first = client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return exactly hello in the answer field.")], cancellationToken: timeout.Token);
		var second = client.GetResponseAsync<WeatherResponse>(
			[new(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.")], cancellationToken: timeout.Token);
		Assert.Equal("hello", (await first).Result.Answer, ignoreCase: true);
		var weather = (await second).Result;
		Assert.Equal("Seattle", weather.Location, ignoreCase: true);
		Assert.Equal(72, weather.Temperature);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task CancellingStructuredInference_ReleasesSchemaForTheNextRequest()
	{
		if (!SupportsStructuredOutput)
			return;

		using var client = new T();
		using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Write a long answer about every country in Europe.")],
			cancellationToken: cancelled.Token));
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		var next = await client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return exactly hello in the answer field.")],
			cancellationToken: timeout.Token);
		Assert.Equal("hello", next.Result.Answer, ignoreCase: true);
	}
}
